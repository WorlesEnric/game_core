// GameCore.Studio.Model - content stamps (docs/studio/03-authoring-contracts.md s1).
// A stamp is "sha256:" + 64 lowercase hex digits over canonical bytes chosen by the caller (an asset's bytes, or a
// scene object's serialized form). This helper hashes bytes; it does not define a canonical JSON form (the etos SDK
// owns canonical-JSON digests, 02 s4).
#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GameCore.Studio.Model
{
    /// <summary>SHA-256 content stamps.</summary>
    public static class ContentStamp
    {
        public const string Prefix = "sha256:";

        private const string HexDigits = "0123456789abcdef";

        private static readonly Regex StampPattern = new Regex(StudioPatterns.Stamp, RegexOptions.CultureInvariant);

        private static readonly Regex HexPattern = new Regex(StudioPatterns.Sha256Hex, RegexOptions.CultureInvariant);

        /// <summary>The stamp of <paramref name="canonicalBytes"/>.</summary>
        public static string Of(byte[] canonicalBytes)
        {
            return Prefix + Sha256Hex(canonicalBytes);
        }

        /// <summary>The stamp of the UTF-8 encoding of <paramref name="canonicalText"/>.</summary>
        public static string OfUtf8(string canonicalText)
        {
            if (canonicalText == null)
            {
                throw new ArgumentNullException(nameof(canonicalText));
            }

            return Of(Encoding.UTF8.GetBytes(canonicalText));
        }

        /// <summary>Lowercase hex SHA-256 of <paramref name="bytes"/> (the bare form artifact entries use).</summary>
        public static string Sha256Hex(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            byte[] digest;
            using (SHA256 sha256 = SHA256.Create())
            {
                digest = sha256.ComputeHash(bytes);
            }

            char[] text = new char[digest.Length * 2];
            for (int i = 0; i < digest.Length; i++)
            {
                text[i * 2] = HexDigits[digest[i] >> 4];
                text[(i * 2) + 1] = HexDigits[digest[i] & 0xF];
            }

            return new string(text);
        }

        /// <summary>True for <c>sha256:</c> + 64 lowercase hex digits.</summary>
        public static bool IsValid(string? stamp) => stamp != null && StampPattern.IsMatch(stamp);

        /// <summary>True for 64 lowercase hex digits.</summary>
        public static bool IsValidHex(string? digest) => digest != null && HexPattern.IsMatch(digest);

        /// <summary>The bare digest of a valid stamp; throws for anything else.</summary>
        public static string DigestOf(string stamp)
        {
            if (!IsValid(stamp))
            {
                throw new ArgumentException("Not a content stamp: '" + stamp + "'.", nameof(stamp));
            }

            return stamp.Substring(Prefix.Length);
        }
    }
}
