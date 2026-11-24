using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// What an authenticator app shows for a time-based one-time code (RFC 6238 over RFC 4226): HMAC-SHA1, six digits, 30
/// seconds, Keycloak's defaults. The key arrives as an authenticator app reads it, in base32 (RFC 4648).
/// </summary>
public static class Totp
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    private const int Period = 30;

    /// <summary>The code for the period <paramref name="offset"/> periods from now.</summary>
    public static string Code(string base32Key, int offset = 0)
    {
        var counter = (TimeProvider.System.GetUtcNow().ToUnixTimeSeconds() / Period) + offset;
        Span<byte> message = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(message, counter);
        // RFC 6238's HMAC-SHA1, which every authenticator app computes and Keycloak checks by default: the test computes
        // what the operator's app shows, so it cannot choose another hash.
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, Decode(base32Key));
        hmac.AppendData(message);
        var hash = hmac.GetHashAndReset();
        var start = hash[^1] & 0x0f;
        var value = (BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(start, 4)) & 0x7fffffff) % 1_000_000;
        return value.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Decode(string base32)
    {
        List<byte> bytes = [];
        var buffer = 0;
        var bits = 0;
        foreach (var character in base32.TrimEnd('=').ToUpperInvariant())
        {
            var index = Alphabet.IndexOf(character, StringComparison.Ordinal);
            index.ShouldBeGreaterThanOrEqualTo(0, $"'{character}' is no base32 character.");
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }

        return [.. bytes];
    }
}
