// GameCore.Rules.Narrative — one canonical digest function for evidence (P-008, P-028, P-060).
//
// A trace compares content, not prose, so every digest in this package is SHA-256 over an explicitly ordered,
// newline-separated canonical text: no culture, no clock, no machine path, no declaration order. The digest is
// never an identity: it labels a recording of a run (P-004).
#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace GameCore.Rules.Narrative
{
    /// <summary>Canonical text digests of the narrative package's recordings.</summary>
    public static class NarrativeDigest
    {
        /// <summary>Digest of a canonical, ordered text: SHA-256 over its UTF-8 bytes, lowercase hex.</summary>
        public static string OfText(string canonicalText)
        {
            if (canonicalText == null)
            {
                throw new ArgumentNullException(nameof(canonicalText));
            }

            byte[] digest;
            using (SHA256 sha256 = SHA256.Create())
            {
                digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonicalText));
            }

            var text = new StringBuilder(digest.Length * 2);
            for (int i = 0; i < digest.Length; i++)
            {
                text.Append(HexDigit(digest[i] >> 4)).Append(HexDigit(digest[i] & 0x0F));
            }

            return text.ToString();
        }

        /// <summary>
        /// Digest of an ordered list of canonical lines: one line per element, LF separated, no trailing newline.
        /// An empty list has a digest too, so "nothing was recorded" cannot be confused with "no digest".
        /// </summary>
        public static string OfLines(IReadOnlyList<string>? lines)
        {
            if (lines == null)
            {
                return OfText(string.Empty);
            }

            var text = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i != 0)
                {
                    text.Append('\n');
                }

                text.Append(lines[i]);
            }

            return OfText(text.ToString());
        }

        private static char HexDigit(int value) => (char)(value < 10 ? ('0' + value) : ('a' + (value - 10)));
    }
}
