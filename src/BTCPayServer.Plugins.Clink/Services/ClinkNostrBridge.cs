using BTCPayServer.Plugins.Clink.Models;
using BTCPayServer.Plugins.Clink.Nostr;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Clink.Services;

public class ClinkNostrBridge
{
    private readonly ILogger<ClinkNostrBridge> _logger;

    public ClinkNostrBridge(ILogger<ClinkNostrBridge> logger)
    {
        _logger = logger;
        _logger.LogInformation("ClinkNostrBridge initialized (C# Native Nostr protocol)");
    }

    public async Task<NostrInvoiceResult> RequestInvoice(
        string noffer, long amountSats, string? description, int expiresInSeconds,
        string? additionalRelays = null, CancellationToken cancellation = default)
    {
        _logger.LogInformation("RequestInvoice: {Sats} sats via noffer", amountSats);

        var protocol = new ClinkProtocol();
        var result = await protocol.RequestInvoiceAsync(
            noffer, amountSats, description, expiresInSeconds, additionalRelays, cancellation);

        _logger.LogInformation("RequestInvoice: got BOLT11={Bolt11}, eventId={EventId}",
            result.Bolt11[..Math.Min(result.Bolt11.Length, 60)], result.EventId);

        return result;
    }

    public async Task<NostrPayResult> PayInvoice(string ndebit, string bolt11, long amountSats,
        string? additionalRelays = null, CancellationToken cancellation = default)
    {
        _logger.LogInformation("PayInvoice: paying {Bolt11} via ndebit",
            bolt11[..Math.Min(bolt11.Length, 60)]);

        var protocol = new ClinkProtocol();
        var result = await protocol.PayInvoiceAsync(ndebit, bolt11, amountSats, additionalRelays, cancellation);

        _logger.LogInformation("PayInvoice: paid successfully, preimage={Preimage}", result.Preimage ?? "(none)");
        return result;
    }

    public async Task<bool> CheckPayment(string noffer, string eventId, string fromPub, string privkeyHex,
        string? additionalRelays = null, CancellationToken cancellation = default)
    {
        try
        {
            var protocol = new ClinkProtocol();
            return await protocol.CheckPaymentAsync(noffer, eventId, fromPub, privkeyHex, additionalRelays, cancellation);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CheckPayment failed");
            return false;
        }
    }
}
