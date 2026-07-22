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

    [Fact]
    public void DecodeLongNoffer()
    {
        var noffer = "noffer1qvqsyqjqvcexxdph89nrse3nvs6k2d35893xze3hxfnxgwpjvf3rxvf4vyunve3j8y6rqdpkx56nzc3k8pjnwcfkx33rydenxajrjdfjv9snqdgprfmhxue69uhhxarjvee8jtnndphkx6ewdejhgam0wf4sqgrka4zlqr820wk9nkxsklfqfpy02vva0wtvzs8lkm7t424s5y75fc6vyk7n";
        var result = ClinkBech32.DecodeNoffer(noffer);
        Assert.Equal("76ed45f00cea7bac59d8d0b7d204848f5319d7b96c140ffb6fcbaaab0a13d44e", result.Pubkey);
        Assert.Equal("wss://strfry.shock.network", result.Relay);
        Assert.Contains("f2c479f8f3d5e649baf72fd82bb315a96f294046551b68e7a64b2737d952aa05", result.Offer);
    }
}
