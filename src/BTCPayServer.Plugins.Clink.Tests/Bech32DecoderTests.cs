using System;
using BTCPayServer.Plugins.Clink.Nostr;
using Xunit;

namespace BTCPayServer.Plugins.Clink.Tests;

public class Bech32DecoderTests
{
    [Fact]
    public void Decode_Invalid_Bech32_Throws()
    {
        Assert.Throws<ArgumentException>(() => Bech32Decoder.Decode(""));
    }

    [Fact]
    public void Decode_No_Separator_Throws()
    {
        Assert.Throws<FormatException>(() => Bech32Decoder.Decode("abcdef"));
    }

    [Fact]
    public void Decode_Invalid_Checksum_Throws()
    {
        Assert.Throws<FormatException>(() => Bech32Decoder.Decode("noffer1qyz3unz"));
    }

    [Fact]
    public void ClinkBech32_DecodeNoffer_WrongHrp_Throws()
    {
        Assert.Throws<FormatException>(() => ClinkBech32.DecodeNoffer("ndebit1qyz3unz"));
    }

    [Fact]
    public void ClinkBech32_DecodeNdebit_WrongHrp_Throws()
    {
        Assert.Throws<FormatException>(() => ClinkBech32.DecodeNdebit("noffer1qyz3unz"));
    }
}
