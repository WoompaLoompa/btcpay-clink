using System.Security.Cryptography;
using System.Text;

namespace BTCPayServer.Plugins.Clink.Nostr;

public static class Nip44
{
    private static readonly byte[] ProtocolName = Encoding.UTF8.GetBytes("nip44-v2");
    private static readonly byte[] KeysInfo = Encoding.UTF8.GetBytes("nip44-v2-keys");

    public static byte[] GetConversationKey(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> publicKeyXOnly)
    {
        return Secp256k1Point.DeriveConversationKey(privateKey, publicKeyXOnly);
    }

    public static string Encrypt(string plaintext, byte[] conversationKey)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var salt = RandomNumberGenerator.GetBytes(32);
        var keys = HKDF.DeriveKey(HashAlgorithmName.SHA256, conversationKey, 76, salt, KeysInfo);

        var aeadKey = keys.AsSpan(0, 32).ToArray();
        var nonce = keys.AsSpan(32, 12).ToArray();

        using var aead = new ChaCha20Poly1305(aeadKey);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];
        aead.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var combined = new byte[1 + 32 + 12 + ciphertext.Length + tag.Length];
        combined[0] = 2;
        salt.CopyTo(combined.AsSpan(1, 32));
        nonce.CopyTo(combined.AsSpan(33, 12));
        ciphertext.CopyTo(combined.AsSpan(45, ciphertext.Length));
        tag.CopyTo(combined.AsSpan(45 + ciphertext.Length, tag.Length));

        return Convert.ToBase64String(combined);
    }

    public static string Decrypt(string payload, byte[] conversationKey)
    {
        var data = Convert.FromBase64String(payload);
        if (data.Length < 1 + 32 + 12 + 1 + 16)
            throw new InvalidOperationException("Invalid NIP-44 payload");

        if (data[0] != 2)
            throw new InvalidOperationException($"Unsupported NIP-44 version: {data[0]}");

        var salt = data.AsSpan(1, 32).ToArray();
        var nonce = data.AsSpan(33, 12).ToArray();
        var ciphertext = data.AsSpan(45, data.Length - 45 - 16).ToArray();
        var tag = data.AsSpan(data.Length - 16, 16).ToArray();

        var keys = HKDF.DeriveKey(HashAlgorithmName.SHA256, conversationKey, 76, salt, KeysInfo);
        var aeadKey = keys.AsSpan(0, 32).ToArray();

        using var aead = new ChaCha20Poly1305(aeadKey);
        var plaintextBytes = new byte[ciphertext.Length];
        aead.Decrypt(nonce, ciphertext, tag, plaintextBytes);

        return Encoding.UTF8.GetString(plaintextBytes);
    }
}
