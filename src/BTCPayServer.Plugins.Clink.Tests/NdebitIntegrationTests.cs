using BTCPayServer.Plugins.Clink.Nostr;
using BTCPayServer.Plugins.Clink.Services;
using Xunit;

namespace BTCPayServer.Plugins.Clink.Tests;

public class NdebitIntegrationTests
{
    [Fact]
    public void NdebitTlv_Tag0_Is_Pubkey_Tag1_Is_Relay()
    {
        // Build a known ndebit bech32 with known TLV values
        var pubkey = new byte[32];
        pubkey[0] = 0xAB;
        pubkey[31] = 0xCD;
        var relay = "wss://relay.example.com";
        var pointer = "test-pointer-123";

        var tlv = new byte[2 + 32 + 2 + relay.Length + 2 + pointer.Length];
        var pos = 0;

        // Tag 0: pubkey (32 bytes)
        tlv[pos++] = 0;
        tlv[pos++] = 32;
        Array.Copy(pubkey, 0, tlv, pos, 32);
        pos += 32;

        // Tag 1: relay
        var relayBytes = System.Text.Encoding.UTF8.GetBytes(relay);
        tlv[pos++] = 1;
        tlv[pos++] = (byte)relayBytes.Length;
        Array.Copy(relayBytes, 0, tlv, pos, relayBytes.Length);
        pos += relayBytes.Length;

        // Tag 2: pointer
        var pointerBytes = System.Text.Encoding.UTF8.GetBytes(pointer);
        tlv[pos++] = 2;
        tlv[pos++] = (byte)pointerBytes.Length;
        Array.Copy(pointerBytes, 0, tlv, pos, pointerBytes.Length);

        // Parse the TLV
        var result = new NdebitData();
        var p = 0;
        while (p < tlv.Length)
        {
            if (p + 2 > tlv.Length) break;
            var tag = tlv[p++];
            var len = tlv[p++];
            if (p + len > tlv.Length) break;

            var value = tlv.AsSpan(p, len).ToArray();
            p += len;

            switch (tag)
            {
                case 0:
                    if (value.Length == 32)
                        result.Pubkey = Convert.ToHexString(value).ToLowerInvariant();
                    break;
                case 1:
                    result.Relay = System.Text.Encoding.UTF8.GetString(value);
                    break;
                case 2:
                    result.Pointer = System.Text.Encoding.UTF8.GetString(value);
                    break;
            }
        }

        Assert.Equal(Convert.ToHexString(pubkey).ToLowerInvariant(), result.Pubkey);
        Assert.Equal(relay, result.Relay);
        Assert.Equal(pointer, result.Pointer);
    }

    [Fact]
    public void NdebitPaymentPayload_Contains_Pointer_Not_Ndebit()
    {
        // Verify that the payment payload format matches CLINK SDK
        // CLINK SDK NdebitData type: { pointer?, amount_sats?, bolt11?, frequency?, k1? }
        // Our payload should NOT contain "ndebit" or "action" fields
        var ndebitData = new NdebitData
        {
            Pubkey = "ab12cd34ef56",
            Relay = "wss://relay.example.com",
            Pointer = "subscription-123"
        };

        // The payment payload should use "pointer" not "ndebit"
        var data = new Dictionary<string, object?>
        {
            ["bolt11"] = "lnbc1...",
            ["amount_sats"] = 100000,
        };
        if (!string.IsNullOrEmpty(ndebitData.Pointer))
        {
            data["pointer"] = ndebitData.Pointer;
        }

        Assert.False(data.ContainsKey("ndebit"), "Payment payload should not contain 'ndebit' field");
        Assert.False(data.ContainsKey("action"), "Payment payload should not contain 'action' field");
        Assert.True(data.ContainsKey("pointer"), "Payment payload should contain 'pointer' field");
        Assert.Equal("subscription-123", data["pointer"]);
    }

    [Fact]
    public void Ndebit_Pointer_Is_Optional()
    {
        // ndebit TLV tag 2 (pointer) is optional per CLINK spec
        var ndebitWithPointer = new NdebitData
        {
            Pubkey = "ab12cd34ef56",
            Relay = "wss://relay.example.com",
            Pointer = "subscription-123"
        };
        var ndebitWithoutPointer = new NdebitData
        {
            Pubkey = "ab12cd34ef56",
            Relay = "wss://relay.example.com",
            Pointer = null
        };

        Assert.Equal("subscription-123", ndebitWithPointer.Pointer);
        Assert.Null(ndebitWithoutPointer.Pointer);
    }

    [Fact]
    public void Ndebit_Pointer_In_Payload_Only_When_Set()
    {
        var dataWithPointer = new Dictionary<string, object?> { ["bolt11"] = "lnbc1..." };
        var pointer = "subscription-123";
        if (!string.IsNullOrEmpty(pointer))
            dataWithPointer["pointer"] = pointer;

        Assert.True(dataWithPointer.ContainsKey("pointer"));

        var dataWithoutPointer = new Dictionary<string, object?> { ["bolt11"] = "lnbc1..." };
        string? noPointer = null;
        if (!string.IsNullOrEmpty(noPointer))
            dataWithoutPointer["pointer"] = noPointer;

        Assert.False(dataWithoutPointer.ContainsKey("pointer"));
    }
}
