using System.Text;

namespace BTCPayServer.Plugins.Clink.Nostr;

public static class Bech32Decoder
{
    private const string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
    private static readonly int[] Generator = { 0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3 };

    public static (string Hrp, byte[] Data) Decode(string bech32)
    {
        if (string.IsNullOrEmpty(bech32))
            throw new ArgumentException("Empty bech32 string");

        bech32 = bech32.ToLowerInvariant();
        var pos = bech32.LastIndexOf('1');
        if (pos < 1 || pos + 7 > bech32.Length)
            throw new FormatException("Invalid bech32 format");

        var hrp = bech32[..pos];
        var dataStr = bech32[(pos + 1)..];

        var values = new byte[dataStr.Length];
        for (var i = 0; i < dataStr.Length; i++)
        {
            var idx = Charset.IndexOf(dataStr[i]);
            if (idx < 0)
                throw new FormatException($"Invalid character in bech32: {dataStr[i]}");
            values[i] = (byte)idx;
        }

        if (!VerifyChecksum(hrp, values))
            throw new FormatException("Invalid bech32 checksum");

        return (hrp, values.AsSpan(0, values.Length - 6).ToArray());
    }

    private static bool VerifyChecksum(string hrp, ReadOnlySpan<byte> values)
    {
        var expanded = ExpandHrp(hrp);
        var combined = new byte[expanded.Length + values.Length];
        Buffer.BlockCopy(expanded, 0, combined, 0, expanded.Length);
        values.CopyTo(combined.AsSpan(expanded.Length));
        return Polymod(combined) == 1;
    }

    private static byte[] ExpandHrp(string hrp)
    {
        var result = new byte[hrp.Length * 2 + 1];
        for (var i = 0; i < hrp.Length; i++)
        {
            result[i] = (byte)(hrp[i] >> 5);
            result[hrp.Length + 1 + i] = (byte)(hrp[i] & 31);
        }
        result[hrp.Length] = 0;
        return result;
    }

    private static long Polymod(ReadOnlySpan<byte> values)
    {
        long chk = 1;
        foreach (var v in values)
        {
            var top = chk >> 25;
            chk = ((chk & 0x1ffffff) << 5) ^ v;
            for (var i = 0; i < 5; i++)
            {
                if ((top & (1 << i)) != 0)
                    chk ^= Generator[i];
            }
        }
        return chk;
    }

}

public class NofferData
{
    public string Relay { get; set; } = "";
    public string Pubkey { get; set; } = "";
    public string Offer { get; set; } = "";
}

public class NdebitData
{
    public string Pubkey { get; set; } = "";
    public string Relay { get; set; } = "";
    public string? Pointer { get; set; }
}

public static class ClinkBech32
{
    public static NofferData DecodeNoffer(string noffer)
    {
        var (hrp, data5) = Bech32Decoder.Decode(noffer);
        if (hrp != "noffer")
            throw new FormatException($"Expected hrp 'noffer', got '{hrp}'");

        var data8 = ConvertBitsToBytes(data5);
        return ParseNofferTlv(data8);
    }

    public static NdebitData DecodeNdebit(string ndebit)
    {
        var (hrp, data5) = Bech32Decoder.Decode(ndebit);
        if (hrp != "ndebit")
            throw new FormatException($"Expected hrp 'ndebit', got '{hrp}'");

        var data8 = ConvertBitsToBytes(data5);
        return ParseNdebitTlv(data8);
    }

    private static byte[] ConvertBitsToBytes(byte[] data5)
    {
        var acc = 0;
        var bits = 0;
        var maxv = 255;
        var result = new List<byte>();

        foreach (var value in data5)
        {
            acc = (acc << 5) | value;
            bits += 5;
            while (bits >= 8)
            {
                bits -= 8;
                result.Add((byte)((acc >> bits) & maxv));
            }
        }

        return result.ToArray();
    }

    private static NofferData ParseNofferTlv(byte[] data)
    {
        var result = new NofferData();
        var pos = 0;
        while (pos < data.Length)
        {
            if (pos + 2 > data.Length) break;
            var tag = data[pos++];
            var len = data[pos++];
            if (pos + len > data.Length) break;

            switch (tag)
            {
                case 0:
                    result.Pubkey = Convert.ToHexString(data, pos, len).ToLowerInvariant();
                    break;
                case 1:
                    result.Relay = Encoding.UTF8.GetString(data, pos, len);
                    break;
                case 2:
                    result.Offer = Encoding.UTF8.GetString(data, pos, len);
                    break;
            }
            pos += len;
        }
        return result;
    }

    private static NdebitData ParseNdebitTlv(byte[] data)
    {
        var result = new NdebitData();
        var pos = 0;
        while (pos < data.Length)
        {
            if (pos + 2 > data.Length) break;
            var tag = data[pos++];
            var len = data[pos++];
            if (pos + len > data.Length) break;

            var value = data.AsSpan(pos, len).ToArray();
            pos += len;

            switch (tag)
            {
                case 0:
                    if (value.Length == 32)
                        result.Pubkey = Convert.ToHexString(value).ToLowerInvariant();
                    break;
                case 1:
                    result.Relay = Encoding.UTF8.GetString(value);
                    break;
                case 2:
                    result.Pointer = Encoding.UTF8.GetString(value);
                    break;
            }
        }
        return result;
    }
}
