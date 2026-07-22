using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace BTCPayServer.Plugins.Clink.Nostr;

public static class Nip44
{
    private const int MinPlaintextSize = 1;

    public static byte[] GetConversationKey(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> publicKeyXOnly)
    {
        return Secp256k1Point.DeriveConversationKey(privateKey, publicKeyXOnly);
    }

    public static string Encrypt(string plaintext, byte[] conversationKey)
    {
        var nonce = RandomNumberGenerator.GetBytes(32);
        var (chachaKey, chachaNonce, hmacKey) = GetMessageKeys(conversationKey, nonce);

        var padded = Pad(plaintext);
        var ciphertext = ChaCha20Encrypt(padded, chachaKey, chachaNonce);

        var mac = HmacAad(hmacKey, ciphertext, nonce);

        var combined = new byte[1 + 32 + ciphertext.Length + 32];
        combined[0] = 2;
        nonce.CopyTo(combined.AsSpan(1, 32));
        ciphertext.CopyTo(combined.AsSpan(33, ciphertext.Length));
        mac.CopyTo(combined.AsSpan(33 + ciphertext.Length, 32));

        return Convert.ToBase64String(combined);
    }

    public static string Decrypt(string payload, byte[] conversationKey)
    {
        if (string.IsNullOrEmpty(payload) || payload[0] == '#')
            throw new InvalidOperationException("Unknown NIP-44 version");

        var data = Convert.FromBase64String(payload);
        if (data.Length < 99)
            throw new InvalidOperationException("Invalid NIP-44 payload size");

        if (data[0] != 2)
            throw new InvalidOperationException($"Unsupported NIP-44 version: {data[0]}");

        var nonce = data.AsSpan(1, 32).ToArray();
        var ciphertext = data.AsSpan(33, data.Length - 33 - 32).ToArray();
        var mac = data.AsSpan(data.Length - 32, 32).ToArray();

        var (chachaKey, chachaNonce, hmacKey) = GetMessageKeys(conversationKey, nonce);

        var computedMac = HmacAad(hmacKey, ciphertext, nonce);
        if (!ConstantTimeEquals(computedMac, mac))
            throw new CryptographicException("Invalid NIP-44 MAC");

        var paddedPlaintext = ChaCha20Encrypt(ciphertext, chachaKey, chachaNonce);

        return Unpad(paddedPlaintext);
    }

    private static byte[] ChaCha20Encrypt(byte[] plaintext, byte[] key, byte[] nonce12)
    {
        var ciphertext = new byte[plaintext.Length];
        var state = new uint[16];
        var keyWords = MemoryMarshal.Cast<byte, uint>(key.AsSpan());
        var nonceWords = MemoryMarshal.Cast<byte, uint>(nonce12.AsSpan());

        state[0] = 0x61707865; state[1] = 0x3320646e;
        state[2] = 0x79622d32; state[3] = 0x6b206574;
        keyWords[..8].CopyTo(state.AsSpan(4, 8));
        var counter = 0u;

        for (var offset = 0; offset < plaintext.Length; offset += 64)
        {
            state[12] = counter++;
            nonceWords[..3].CopyTo(state.AsSpan(13, 3));

            var working = state.AsSpan().ToArray();
            for (var r = 0; r < 10; r++)
            {
                QuarterRound(ref working[0], ref working[4], ref working[8], ref working[12]);
                QuarterRound(ref working[1], ref working[5], ref working[9], ref working[13]);
                QuarterRound(ref working[2], ref working[6], ref working[10], ref working[14]);
                QuarterRound(ref working[3], ref working[7], ref working[11], ref working[15]);
                QuarterRound(ref working[0], ref working[5], ref working[10], ref working[15]);
                QuarterRound(ref working[1], ref working[6], ref working[11], ref working[12]);
                QuarterRound(ref working[2], ref working[7], ref working[8], ref working[13]);
                QuarterRound(ref working[3], ref working[4], ref working[9], ref working[14]);
            }

            for (var i = 0; i < 16; i++)
                working[i] += state[i];

            var keystream = MemoryMarshal.AsBytes<uint>(working.AsSpan());
            var remaining = plaintext.Length - offset;
            var chunkSize = remaining < 64 ? remaining : 64;
            for (var i = 0; i < chunkSize; i++)
                ciphertext[offset + i] = (byte)(plaintext[offset + i] ^ keystream[i]);
        }

        return ciphertext;
    }

    private static void QuarterRound(ref uint a, ref uint b, ref uint c, ref uint d)
    {
        a += b; d ^= a; d = Rotl(d, 16);
        c += d; b ^= c; b = Rotl(b, 12);
        a += b; d ^= a; d = Rotl(d, 8);
        c += d; b ^= c; b = Rotl(b, 7);
    }

    private static uint Rotl(uint x, int n) => (x << n) | (x >> (32 - n));

    private static (byte[] ChachaKey, byte[] ChachaNonce, byte[] HmacKey) GetMessageKeys(
        byte[] conversationKey, byte[] nonce)
    {
        var keys = HKDF.Expand(HashAlgorithmName.SHA256, conversationKey, 76, nonce);
        return (
            keys.AsSpan(0, 32).ToArray(),
            keys.AsSpan(32, 12).ToArray(),
            keys.AsSpan(44, 32).ToArray()
        );
    }

    private static byte[] HmacAad(byte[] key, byte[] message, byte[] aad)
    {
        var combined = new byte[aad.Length + message.Length];
        aad.CopyTo(combined, 0);
        message.CopyTo(combined.AsSpan(aad.Length));
        return HMACSHA256.HashData(key, combined);
    }

    private static bool ConstantTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        var result = 0;
        for (var i = 0; i < a.Length; i++)
            result |= a[i] ^ b[i];
        return result == 0;
    }

    public static int CalcPaddedLen(int unpaddedLen)
    {
        if (unpaddedLen <= 32)
            return 32;

        var nextPower = 1 << (int)Math.Floor(Math.Log2(unpaddedLen - 1)) + 1;
        var chunk = nextPower <= 256 ? 32 : nextPower / 8;
        return chunk * (int)(Math.Floor((double)(unpaddedLen - 1) / chunk) + 1);
    }

    public static byte[] Pad(string plaintext)
    {
        var unpadded = Encoding.UTF8.GetBytes(plaintext);
        var unpaddedLen = unpadded.Length;

        if (unpaddedLen < MinPlaintextSize || unpaddedLen > 65535)
            throw new ArgumentException("Invalid plaintext length", nameof(plaintext));

        var prefix = new byte[2];
        prefix[0] = (byte)((uint)unpaddedLen >> 8);
        prefix[1] = (byte)unpaddedLen;

        var paddedLen = CalcPaddedLen(unpaddedLen);
        var suffixLen = paddedLen - unpaddedLen;

        var result = new byte[2 + unpaddedLen + suffixLen];
        prefix.CopyTo(result, 0);
        unpadded.CopyTo(result.AsSpan(2));
        return result;
    }

    public static string Unpad(byte[] padded)
    {
        var unpaddedLen = (padded[0] << 8) | padded[1];
        if (unpaddedLen == 0)
            throw new InvalidOperationException("Invalid NIP-44 padding: zero length");

        var unpadded = padded.AsSpan(2, unpaddedLen).ToArray();
        if (padded.Length != 2 + CalcPaddedLen(unpaddedLen))
            throw new InvalidOperationException("Invalid NIP-44 padding: incorrect padded length");

        return Encoding.UTF8.GetString(unpadded);
    }
}
