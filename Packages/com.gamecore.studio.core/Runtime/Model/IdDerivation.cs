// GameCore.Studio.Model - identity derivation (docs/studio/02-architecture.md s6, 03 s1/s6).
//
// TargetId: 02 s6 / 03 s1 write the rule as StableNameKeyDerivation("auth:" + authoringId). The contracts'
// StableNameKeyDerivation.Derive refuses ':' (its admissible alphabet is a-z 0-9 . _ -), so calling it with that
// name always throws. This file applies the identical derivation (StableNameKeyDerivation.Scope: SHA-256 over the
// UTF-8 name, first 16 digest bytes read as two big-endian 64-bit words) to the exact documented name
// "auth:" + authoringId, after checking that the authoring id itself is canonical stable-name text. The derived
// key is therefore bit-identical to what the documented formula means; only the alphabet check of the colon
// separator is bypassed. Recorded in PACKET.md for the contract owner.
//
// Change-set and selection ids: prefix + 26-character Crockford base32 ULID (48-bit Unix milliseconds, 80 random
// bits). The clock and the entropy are supplied by the caller, so ids are deterministic under test.
#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GameCore.Contracts;

namespace GameCore.Studio.Model
{
    /// <summary>Source of the random part of a ULID-style id.</summary>
    public interface IIdEntropy
    {
        /// <summary>Fills <paramref name="buffer"/> with random bytes.</summary>
        void Fill(byte[] buffer);
    }

    /// <summary>Cryptographic entropy for production ids.</summary>
    public sealed class CryptoIdEntropy : IIdEntropy
    {
        public static readonly CryptoIdEntropy Instance = new CryptoIdEntropy();

        public void Fill(byte[] buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(buffer);
            }
        }
    }

    /// <summary>Derivations of Studio identities (02 s6).</summary>
    public static class IdDerivation
    {
        /// <summary>Prefix of the stable name a TargetId is derived from (02 s6).</summary>
        public const string AuthoringNamePrefix = "auth:";

        public const string ChangeSetPrefix = "cs_";

        public const string SelectionPrefix = "sel_";

        /// <summary>Crockford base32 alphabet (no I, L, O, U).</summary>
        public const string CrockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        /// <summary>Largest Unix-millisecond time a 48-bit ULID timestamp holds.</summary>
        public const long MaxUlidTime = (1L << 48) - 1;

        private static readonly Regex ChangeSetIdPattern = new Regex(StudioPatterns.ChangeSetId, RegexOptions.CultureInvariant);

        private static readonly Regex SelectionIdPattern = new Regex(StudioPatterns.SelectionId, RegexOptions.CultureInvariant);

        /// <summary>
        /// <c>TargetId</c> of an authored entity/definition: the stable-name key of <c>"auth:" + authoringId</c>
        /// (SHA-256 of the UTF-8 name, first 16 bytes as big-endian High/Low; see the file header). Prefab-variant
        /// instances pass their instance id (03 s1).
        /// </summary>
        public static TargetId TargetIdFor(string authoringId)
        {
            if (authoringId == null)
            {
                throw new ArgumentNullException(nameof(authoringId));
            }

            if (!StableNameKeyDerivation.IsCanonicalStableName(authoringId))
            {
                throw new ArgumentException(
                    "An authoring id uses only " + StableNameKeyDerivation.AllowedCharacters
                    + " (lowercase GUID text) and cannot start or end with '.'; received '" + authoringId + "'.",
                    nameof(authoringId));
            }

            return new TargetId(DeriveKey(AuthoringNamePrefix + authoringId));
        }

        /// <summary>
        /// The StableNameKeyDerivation rule applied to <paramref name="name"/> without the alphabet check. Equal to
        /// <see cref="StableNameKeyDerivation.Derive"/> for every canonical name.
        /// </summary>
        public static Id128 DeriveKey(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A key name cannot be empty.", nameof(name));
            }

            byte[] digest;
            using (SHA256 sha256 = SHA256.Create())
            {
                digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(name));
            }

            Id128 key = new Id128(ReadBigEndian(digest, 0), ReadBigEndian(digest, 8));
            if (key.IsDefault)
            {
                throw new InvalidOperationException("The derived key is all-zero, which is not a valid identity.");
            }

            return key;
        }

        /// <summary>A new change-set id <c>cs_</c> + ULID from the given time and entropy.</summary>
        public static string NewChangeSetId(long unixTimeMilliseconds, IIdEntropy entropy)
        {
            return ChangeSetPrefix + NewUlid(unixTimeMilliseconds, entropy);
        }

        /// <summary>A new change-set id from the current UTC time and cryptographic entropy.</summary>
        public static string NewChangeSetId()
        {
            return NewChangeSetId(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), CryptoIdEntropy.Instance);
        }

        /// <summary>A new selection id <c>sel_</c> + ULID from the given time and entropy.</summary>
        public static string NewSelectionId(long unixTimeMilliseconds, IIdEntropy entropy)
        {
            return SelectionPrefix + NewUlid(unixTimeMilliseconds, entropy);
        }

        public static bool IsChangeSetId(string? id) => id != null && ChangeSetIdPattern.IsMatch(id);

        public static bool IsSelectionId(string? id) => id != null && SelectionIdPattern.IsMatch(id);

        /// <summary>The 26-character ULID of a time and 10 entropy bytes.</summary>
        public static string NewUlid(long unixTimeMilliseconds, IIdEntropy entropy)
        {
            if (entropy == null)
            {
                throw new ArgumentNullException(nameof(entropy));
            }

            byte[] random = new byte[10];
            entropy.Fill(random);
            return FormatUlid(unixTimeMilliseconds, random);
        }

        /// <summary>Encodes 48-bit time + 80-bit random (10 bytes) as 26 Crockford base32 characters, big-endian.</summary>
        public static string FormatUlid(long unixTimeMilliseconds, byte[] random)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            if (random.Length != 10)
            {
                throw new ArgumentException("A ULID carries exactly 10 random bytes.", nameof(random));
            }

            if (unixTimeMilliseconds < 0 || unixTimeMilliseconds > MaxUlidTime)
            {
                throw new ArgumentOutOfRangeException(nameof(unixTimeMilliseconds), "A ULID time is 0..2^48-1 Unix milliseconds.");
            }

            byte[] value = new byte[16];
            for (int i = 0; i < 6; i++)
            {
                value[i] = (byte)(unixTimeMilliseconds >> (8 * (5 - i)));
            }

            Array.Copy(random, 0, value, 6, 10);

            // 128 bits as 26 five-bit groups; the first group holds the top 3 bits (two leading zero bits).
            char[] text = new char[26];
            for (int group = 0; group < 26; group++)
            {
                int bitOffset = (group * 5) - 2;
                int index = 0;
                for (int bit = 0; bit < 5; bit++)
                {
                    int position = bitOffset + bit;
                    int value01 = 0;
                    if (position >= 0)
                    {
                        value01 = (value[position >> 3] >> (7 - (position & 7))) & 1;
                    }

                    index = (index << 1) | value01;
                }

                text[group] = CrockfordAlphabet[index];
            }

            return new string(text);
        }

        private static ulong ReadBigEndian(byte[] bytes, int offset)
        {
            ulong value = 0UL;
            for (int i = 0; i < 8; i++)
            {
                value = (value << 8) | bytes[offset + i];
            }

            return value;
        }
    }
}
