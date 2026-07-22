using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using BTCPayServer.Plugins.Clink.Models;

namespace BTCPayServer.Plugins.Clink.Nostr;

public class ClinkProtocol
{
    public async Task<NostrInvoiceResult> RequestInvoiceAsync(
        string noffer, long amountSats, string? description, int expiresInSeconds,
        string? additionalRelays = null, CancellationToken ct = default)
    {
        if (amountSats <= 0)
            throw new ArgumentException("Amount must be positive", nameof(amountSats));

        var nofferData = ClinkBech32.DecodeNoffer(noffer);
        var relays = new[] { nofferData.Relay }
            .Concat((additionalRelays ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

        var merchantPubkey = FromHex(nofferData.Pubkey);
        var ephemeralSk = RandomNumberGenerator.GetBytes(32);
        var ephemeralPk = GetPublicKey(ephemeralSk);

        var conversationKey = Nip44.GetConversationKey(ephemeralSk, merchantPubkey);

        var data = new Dictionary<string, object?>
        {
            ["offer"] = nofferData.Offer,
            ["amount_sats"] = amountSats,
            ["description"] = description,
            ["expires_in_seconds"] = expiresInSeconds > 0 ? expiresInSeconds : 3600,
        };
        var plaintext = JsonSerializer.Serialize(data);
        var encryptedContent = Nip44.Encrypt(plaintext, conversationKey);

        var createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var pubkeyHex = ToHex(ephemeralPk);

        var tags = new object[]
        {
            new object[] { "p", nofferData.Pubkey },
            new object[] { "clink_version", "1" },
        };

        var serialized = SerializeEventForId(pubkeyHex, createdAt, 21001, tags, encryptedContent);
        var eventId = SHA256.HashData(Encoding.UTF8.GetBytes(serialized));

        var sig = SignSchnorr(ephemeralSk, eventId);

        var eventObj = new Dictionary<string, object?>
        {
            ["id"] = ToHex(eventId),
            ["pubkey"] = pubkeyHex,
            ["created_at"] = createdAt,
            ["kind"] = 21001,
            ["tags"] = tags,
            ["content"] = encryptedContent,
            ["sig"] = ToHex(sig),
        };

        var eventElement = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(eventObj, IdJsonOptions));

        var filter = new Dictionary<string, object?>
        {
            ["since"] = createdAt - 1,
            ["kinds"] = new[] { 21001 },
            ["#p"] = new[] { pubkeyHex },
            ["#e"] = new[] { ToHex(eventId) },
        };
        var filterElement = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(filter));

        var errors = new List<string>();
        foreach (var (relayUrl, idx) in relays.Select((url, i) => (url, i)))
        {
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException();

            var timeoutSeconds = idx == 0 ? 60 : 15;
            await using var relay = new NostrRelayClient();
            try
            {
                await relay.ConnectAsync(relayUrl, ct);

                var responseTask = relay.SubscribeOneAsync(filterElement, timeoutSeconds, ct);
                await relay.PublishAsync(eventElement, ct);

                var response = await responseTask;
                using var respDoc = JsonDocument.Parse(response);
                var respContent = respDoc.RootElement.GetProperty("content").GetString() ?? "";
                var decrypted = Nip44.Decrypt(respContent, conversationKey);
                var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(decrypted);

                if (result == null)
                    throw new InvalidOperationException("Empty response from CLINK node");

                if (result.TryGetValue("bolt11", out var bolt11) && bolt11.GetString() is { Length: > 0 } bolt11Str)
                {
                    return new NostrInvoiceResult
                    {
                        Bolt11 = bolt11Str,
                        EventId = ToHex(eventId),
                        FromPub = pubkeyHex,
                        PrivkeyHex = ToHex(ephemeralSk),
                    };
                }

                var error = result.TryGetValue("error", out var errEl) ? errEl.GetString() : "Unknown error";
                throw new InvalidOperationException(error ?? "Unknown error from CLINK node");
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception ex)
            {
                var reason = ex is OperationCanceledException ? $"timed out ({timeoutSeconds}s)" : ex.GetType().Name;
                errors.Add($"{relayUrl}: {reason}: {ex.Message}");
                continue;
            }
        }

        var allFailedMsg = "Could not reach any CLINK relay:\n  " + string.Join("\n  ", errors);
        throw new InvalidOperationException(allFailedMsg);
    }

    public async Task<(bool Paid, string? Preimage)> CheckPaymentAsync(
        string noffer, string eventId, string fromPub, string privkeyHex,
        string? additionalRelays = null, CancellationToken ct = default)
    {
        var nofferData = ClinkBech32.DecodeNoffer(noffer);
        var relays = new[] { nofferData.Relay }
            .Concat((additionalRelays ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

        var privateKey = FromHex(privkeyHex);
        var merchantPubkeyBytes = FromHex(nofferData.Pubkey);
        var conversationKey = Nip44.GetConversationKey(privateKey, merchantPubkeyBytes);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var filter = new Dictionary<string, object?>
        {
            ["since"] = now - 86400,
            ["kinds"] = new[] { 21001, 21002 },
            ["#p"] = new[] { fromPub },
            ["authors"] = new[] { nofferData.Pubkey },
        };
        var filterElement = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(filter));

        foreach (var relayUrl in relays)
        {
            if (ct.IsCancellationRequested) return (false, null);
            await using var relay = new NostrRelayClient();
            try
            {
                await relay.ConnectAsync(relayUrl, ct);
                var response = await relay.SubscribeAsync(filterElement, 15, ct);
                using var respDoc = JsonDocument.Parse(response);
                var content = respDoc.RootElement.GetProperty("content").GetString() ?? "";
                var decrypted = Nip44.Decrypt(content, conversationKey);
                var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(decrypted);
                if (result != null && result.TryGetValue("res", out var res) && res.GetString() == "ok")
                {
                    var preimage = result.TryGetValue("preimage", out var pi) ? pi.GetString() : null;
                    return (true, preimage);
                }
            }
            catch { }
        }

        return (false, null);
    }

    public async Task<NostrPayResult> PayInvoiceAsync(
        string ndebit, string bolt11, long amountSats,
        string? additionalRelays = null, CancellationToken ct = default)
    {
        var ndebitData = ClinkBech32.DecodeNdebit(ndebit);
        var relays = new[] { ndebitData.Relay }
            .Concat((additionalRelays ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

        var merchantPubkeyBytes = FromHex(ndebitData.Pubkey);
        var ephemeralSk = RandomNumberGenerator.GetBytes(32);
        var ephemeralPk = GetPublicKey(ephemeralSk);

        var conversationKey = Nip44.GetConversationKey(ephemeralSk, merchantPubkeyBytes);

        var data = new Dictionary<string, object?>
        {
            ["bolt11"] = bolt11,
            ["amount_sats"] = amountSats,
            ["action"] = "pay",
            ["ndebit"] = ndebit,
        };
        var plaintext = JsonSerializer.Serialize(data);
        var encryptedContent = Nip44.Encrypt(plaintext, conversationKey);

        var createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var pubkeyHex = ToHex(ephemeralPk);

        var tags = new object[]
        {
            new object[] { "p", ndebitData.Pubkey },
            new object[] { "clink_version", "1" },
        };

        var serialized = SerializeEventForId(pubkeyHex, createdAt, 21002, tags, encryptedContent);
        var eventId = SHA256.HashData(Encoding.UTF8.GetBytes(serialized));

        var sig = SignSchnorr(ephemeralSk, eventId);

        var eventObj = new Dictionary<string, object?>
        {
            ["id"] = ToHex(eventId),
            ["pubkey"] = pubkeyHex,
            ["created_at"] = createdAt,
            ["kind"] = 21002,
            ["tags"] = tags,
            ["content"] = encryptedContent,
            ["sig"] = ToHex(sig),
        };

        var eventElement = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(eventObj));

        var filter = new Dictionary<string, object?>
        {
            ["since"] = createdAt - 1,
            ["kinds"] = new[] { 21002 },
            ["#p"] = new[] { pubkeyHex },
            ["#e"] = new[] { ToHex(eventId) },
        };
        var filterElement = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(filter));

        var errors = new List<string>();
        foreach (var (relayUrl, idx) in relays.Select((url, i) => (url, i)))
        {
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException();

            var timeoutSeconds = idx == 0 ? 45 : 15;
            await using var relay = new NostrRelayClient();
            try
            {
                await relay.ConnectAsync(relayUrl, ct);

                var responseTask = relay.SubscribeOneAsync(filterElement, timeoutSeconds, ct);
                await relay.PublishAsync(eventElement, ct);

                var response = await responseTask;
                using var respDoc = JsonDocument.Parse(response);

                var respContent = respDoc.RootElement.GetProperty("content").GetString() ?? "";
                var decrypted = Nip44.Decrypt(respContent, conversationKey);
                var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(decrypted);

                if (result == null)
                    throw new InvalidOperationException("Empty pay response");

                if (result.TryGetValue("res", out var res) && res.GetString() == "ok")
                {
                    return new NostrPayResult
                    {
                        Res = "ok",
                        Preimage = result.TryGetValue("preimage", out var pi) ? pi.GetString() : null,
                    };
                }

                var error = result.TryGetValue("error", out var errEl) ? errEl.GetString() : "Payment failed";
                throw new InvalidOperationException(error ?? "Payment failed");
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception ex)
            {
                var reason = ex is OperationCanceledException ? $"timed out ({timeoutSeconds}s)" : ex.GetType().Name;
                errors.Add($"{relayUrl}: {reason}: {ex.Message}");
                continue;
            }
        }

        var allFailedMsg = "Could not reach any CLINK relay:\n  " + string.Join("\n  ", errors);
        throw new InvalidOperationException(allFailedMsg);
    }

    private static readonly JsonSerializerOptions IdJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string SerializeEventForId(string pubkey, long createdAt, int kind, object tags, string content)
    {
        return JsonSerializer.Serialize(new object?[]
        {
            0,
            pubkey,
            createdAt,
            kind,
            tags,
            content,
        }, IdJsonOptions);
    }

    private static byte[] GetPublicKey(byte[] privateKey)
    {
        return Secp256k1Point.GetPublicKeyXOnly(privateKey);
    }

    private static byte[] SignSchnorr(byte[] privateKey, byte[] msg32)
    {
        return Secp256k1Point.SignSchnorr(privateKey, msg32);
    }

    private static string ToHex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
    private static byte[] FromHex(string hex) => Convert.FromHexString(hex);
}
