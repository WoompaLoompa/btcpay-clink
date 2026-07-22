using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace BTCPayServer.Plugins.Clink.Nostr;

public static class Secp256k1Point
{
    private static readonly BigInteger P = FromHexBE("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEFFFFFC2F");
    private static readonly BigInteger N = FromHexBE("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141");
    private static readonly BigInteger Seven = new(7);

    private static readonly BigInteger Gx = FromHexBE("79BE667EF9DCBBAC55A06295CE870B07029BFCDB2DCE28D959F2815B16F81798");
    private static readonly BigInteger Gy = FromHexBE("483ADA7726A9C465761BA7FC3240F1C31936F4CDFF673E2EE1BAF0D8C6D6457C");

    public static byte[] LiftXToCompressed(ReadOnlySpan<byte> x32)
    {
        var x = new BigInteger(x32, isUnsigned: true, isBigEndian: true);
        var ySq = (x * x % P * x % P + Seven) % P;

        if (!IsQuadraticResidue(ySq, P))
            throw new InvalidOperationException("Point not on secp256k1 curve");

        var y = ModPow(ySq, (P + 1) / 4, P);

        if (!y.IsEven)
            y = P - y;

        return CompressPubKey(x, y);
    }

    public static byte[] ComputeSharedX(ReadOnlySpan<byte> privKey32, ReadOnlySpan<byte> pubKeyXOnly32)
    {
        var compressed = LiftXToCompressed(pubKeyXOnly32);
        var shared = PointMultiply(compressed, privKey32);
        return ToBytes32(shared.X);
    }

    public static byte[] DeriveConversationKey(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> publicKeyXOnly)
    {
        var sharedX = ComputeSharedX(privateKey, publicKeyXOnly);
        return HKDF.Extract(HashAlgorithmName.SHA256, sharedX, Encoding.UTF8.GetBytes("nip44-v2"));
    }

    public static byte[] GetPublicKeyXOnly(ReadOnlySpan<byte> privateKey32)
    {
        var pubCompressed = PointMultiply(CompressedG, privateKey32);
        return ToBytes32(pubCompressed.X);
    }

    public static byte[] SignSchnorr(ReadOnlySpan<byte> privateKey32, ReadOnlySpan<byte> msg32)
    {
        var d = new BigInteger(privateKey32, isUnsigned: true, isBigEndian: true);
        var pubCompressed = PointMultiply(CompressedG, privateKey32);
        var px = ToBytes32(pubCompressed.X);
        var py = pubCompressed.Y;

        if (!py.IsEven)
            d = N - d;

        var aux = RandomNumberGenerator.GetBytes(32);
        var t = XorBytes(ToBytes32(d), TaggedHash("BIP0340/aux", aux));
        var randBytes = TaggedHash("BIP0340/nonce", ConcatBytes(ConcatBytes(t, px), msg32));
        var rand = new BigInteger(randBytes, isUnsigned: true, isBigEndian: true);

        var rCompressed = PointMultiply(CompressedG, randBytes);
        var rx = ToBytes32(rCompressed.X);
        var ry = rCompressed.Y;

        if (!ry.IsEven)
            rand = N - rand;

        var challengeBytes = TaggedHash("BIP0340/challenge", ConcatBytes(ConcatBytes(rx, px), msg32));
        var challenge = new BigInteger(challengeBytes, isUnsigned: true, isBigEndian: true);
        var e = challenge % N;

        var sigK = (rand + e * d) % N;
        var sigBytes = ConcatBytes(rx, ToBytes32(sigK));
        return sigBytes;
    }

    private static byte[] CompressedG
    {
        get
        {
            var result = new byte[33];
            result[0] = 0x02;
            var gxBytes = ToBytes32(Gx);
            gxBytes.CopyTo(result, 1);
            return result;
        }
    }

    private static (BigInteger X, BigInteger Y) PointMultiply(ReadOnlySpan<byte> compressedPubKey, ReadOnlySpan<byte> scalarBytes)
    {
        var point = ParseCompressed(compressedPubKey);
        var scalar = new BigInteger(scalarBytes, isUnsigned: true, isBigEndian: true);
        return PointMultiplyInternal(point, scalar);
    }

    private static (BigInteger X, BigInteger Y) PointMultiplyInternal((BigInteger X, BigInteger Y) point, BigInteger scalar)
    {
        scalar %= N;
        if (scalar < 0) scalar += N;
        if (scalar == 0) return (0, 0);

        var (rx, ry) = (BigInteger.Zero, BigInteger.Zero);
        var (ax, ay) = point;

        while (scalar > 0)
        {
            if ((scalar & 1) == 1)
                (rx, ry) = PointAdd(rx, ry, ax, ay);
            (ax, ay) = PointDouble(ax, ay);
            scalar >>= 1;
        }

        return (rx, ry);
    }

    private static (BigInteger X, BigInteger Y) ParseCompressed(ReadOnlySpan<byte> compressed)
    {
        if (compressed.Length != 33 || (compressed[0] != 0x02 && compressed[0] != 0x03))
            throw new ArgumentException("Invalid compressed pubkey");
        var x = new BigInteger(compressed.Slice(1), isUnsigned: true, isBigEndian: true);
        var ySq = (x * x % P * x % P + Seven) % P;
        var y = ModPow(ySq, (P + 1) / 4, P);
        bool wantOdd = compressed[0] == 0x03;
        if (y.IsEven == wantOdd)
            y = P - y;
        return (x, y);
    }

    private static byte[] CompressPubKey(BigInteger x, BigInteger y)
    {
        var prefix = y.IsEven ? (byte)0x02 : (byte)0x03;
        var xBytes = ToBytes32(x);
        var result = new byte[33];
        result[0] = prefix;
        xBytes.CopyTo(result, 1);
        return result;
    }

    private static (BigInteger X, BigInteger Y) PointDouble(BigInteger x, BigInteger y)
    {
        var s = (3 * x * x % P) * ModInverse(2 * y % P, P) % P;
        var x3 = (s * s - 2 * x) % P;
        var y3 = (s * (x - x3) - y) % P;
        if (x3 < 0) x3 += P;
        if (y3 < 0) y3 += P;
        return (x3, y3);
    }

    private static (BigInteger X, BigInteger Y) PointAdd(BigInteger x1, BigInteger y1, BigInteger x2, BigInteger y2)
    {
        if (x1 == 0 && y1 == 0) return (x2, y2);
        if (x2 == 0 && y2 == 0) return (x1, y1);

        if (x1 == x2)
        {
            if (y1 == y2)
                return PointDouble(x1, y1);
            return (0, 0);
        }

        var s = (y2 - y1) * ModInverse(x2 - x1, P) % P;
        var x3 = (s * s - x1 - x2) % P;
        var y3 = (s * (x1 - x3) - y1) % P;
        if (x3 < 0) x3 += P;
        if (y3 < 0) y3 += P;
        return (x3, y3);
    }

    private static BigInteger ModInverse(BigInteger a, BigInteger m)
    {
        if (a < 0) a += m;
        var (g, x, _) = ExtendedGcd(a % m, m);
        if (g != 1)
            throw new InvalidOperationException("Modular inverse does not exist");
        return (x % m + m) % m;
    }

    private static (BigInteger Gcd, BigInteger X, BigInteger Y) ExtendedGcd(BigInteger a, BigInteger b)
    {
        if (a == 0) return (b, 0, 1);
        var (g, x1, y1) = ExtendedGcd(b % a, a);
        return (g, y1 - (b / a) * x1, x1);
    }

    private static BigInteger ModPow(BigInteger value, BigInteger exponent, BigInteger modulus)
    {
        return BigInteger.ModPow(value, exponent, modulus);
    }

    private static bool IsQuadraticResidue(BigInteger a, BigInteger p)
    {
        return ModPow(a, (p - 1) / 2, p) == 1;
    }

    private static byte[] ToBytes32(BigInteger value)
    {
        var bytes = value.ToByteArray(isUnsigned: false, isBigEndian: true);
        if (bytes.Length == 32) return bytes;
        if (bytes.Length < 32)
        {
            var result = new byte[32];
            bytes.CopyTo(result, 32 - bytes.Length);
            return result;
        }
        if (bytes.Length == 33 && bytes[0] == 0)
        {
            var result = new byte[32];
            Array.Copy(bytes, 1, result, 0, 32);
            return result;
        }
        throw new InvalidOperationException("Unexpected byte length for BigInteger");
    }

    private static byte[] TaggedHash(string tag, ReadOnlySpan<byte> msg)
    {
        var tagHash = SHA256.HashData(Encoding.UTF8.GetBytes(tag));
        var combined = new byte[tagHash.Length * 2 + msg.Length];
        tagHash.CopyTo(combined, 0);
        tagHash.CopyTo(combined, tagHash.Length);
        msg.CopyTo(combined.AsSpan(tagHash.Length * 2));
        return SHA256.HashData(combined);
    }

    private static byte[] ConcatBytes(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var result = new byte[a.Length + b.Length];
        a.CopyTo(result);
        b.CopyTo(result.AsSpan(a.Length));
        return result;
    }

    private static byte[] XorBytes(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var result = new byte[a.Length];
        for (int i = 0; i < a.Length; i++)
            result[i] = (byte)(a[i] ^ b[i]);
        return result;
    }

    private static BigInteger FromHexBE(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
    }
}
