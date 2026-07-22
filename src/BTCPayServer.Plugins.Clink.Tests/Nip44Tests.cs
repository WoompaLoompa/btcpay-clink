using System.Security.Cryptography;
using BTCPayServer.Plugins.Clink.Nostr;
using Xunit;

namespace BTCPayServer.Plugins.Clink.Tests;

public class Nip44Tests
{
    [Fact]
    public void Encrypt_Decrypt_RoundTrip_Succeeds()
    {
        var aliceSk = RandomNumberGenerator.GetBytes(32);
        var bobSk = RandomNumberGenerator.GetBytes(32);
        var alicePk = Secp256k1Point.GetPublicKeyXOnly(aliceSk);
        var bobPk = Secp256k1Point.GetPublicKeyXOnly(bobSk);

        var aliceConv = Nip44.GetConversationKey(aliceSk, bobPk);
        var bobConv = Nip44.GetConversationKey(bobSk, alicePk);

        var plaintext = "Hello NIP-44!";
        var encrypted = Nip44.Encrypt(plaintext, aliceConv);
        var decrypted = Nip44.Decrypt(encrypted, bobConv);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_Decrypt_With_Same_Key_RoundTrip()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var plaintext = "Test message with same conversation key";
        var encrypted = Nip44.Encrypt(plaintext, key);
        var decrypted = Nip44.Decrypt(encrypted, key);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_Produces_Base64_Output()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var encrypted = Nip44.Encrypt("test", key);
        Assert.NotNull(encrypted);
        // Should be valid base64; decoded must be at least 99 bytes per NIP-44 spec
        var decoded = Convert.FromBase64String(encrypted);
        Assert.True(decoded.Length >= 99);
    }

    [Fact]
    public void Decrypt_Wrong_Key_Throws()
    {
        var key1 = RandomNumberGenerator.GetBytes(32);
        var key2 = RandomNumberGenerator.GetBytes(32);
        var encrypted = Nip44.Encrypt("secret", key1);
        Assert.Throws<CryptographicException>(() => Nip44.Decrypt(encrypted, key2));
    }

    [Fact]
    public void Decrypt_Invalid_Payload_Throws()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        Assert.Throws<InvalidOperationException>(() => Nip44.Decrypt("AAAA", key));
    }

    [Fact]
    public void CalcPaddedLen_Matches_Spec()
    {
        Assert.Equal(32, Nip44.CalcPaddedLen(1));
        Assert.Equal(32, Nip44.CalcPaddedLen(32));
        Assert.Equal(64, Nip44.CalcPaddedLen(33));
        Assert.Equal(64, Nip44.CalcPaddedLen(64));
        Assert.Equal(128, Nip44.CalcPaddedLen(100));
        Assert.Equal(224, Nip44.CalcPaddedLen(200));
        Assert.Equal(512, Nip44.CalcPaddedLen(500));
    }

    [Fact]
    public void Pad_Unpad_RoundTrip()
    {
        var original = "Hello NIP-44 padding test!";
        var padded = Nip44.Pad(original);
        var unpadded = Nip44.Unpad(padded);
        Assert.Equal(original, unpadded);
    }

    [Fact]
    public void Pad_Unpad_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => Nip44.Pad(""));
    }
}
