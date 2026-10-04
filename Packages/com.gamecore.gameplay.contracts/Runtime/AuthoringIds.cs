// GameCore.Gameplay.Contracts - authoring identity (docs/studio/02-architecture.md s6, 03-authoring-contracts.md s1).
//
// Every authored object (placed entity, definition, region, portal, world) carries one authoring id: a lowercase GUID in
// the "D" format, minted once and never changed. Everything the kernel needs is derived from it:
//
//   TargetId     = StableNameKeyDerivation.Derive("auth." + authoringId)   (the Studio rule, 02 s6; identical to
//                  GameCore.Studio.Model.IdDerivation.TargetIdFor, which a dotnet test asserts)
//   DefinitionId = StableNameKeyDerivation.Derive("gameplay.def." + authoringId)
//   ScopeId      = StableNameKeyDerivation.Derive("gameplay.scope." + authoringId)
//   stable key   = first four SHA-256 bytes of "gameplay.key." + authoringId, big-endian, masked to a positive int31
//                  (zero maps to one). Stable keys are what int32 slots carry (SADR-004); the bake refuses collisions.
//
// This package deliberately does not depend on com.gamecore.studio.*: the derivation is re-implemented here with the
// kernel helper, so gameplay runtime packages stay free of Studio code (P1.1 coordination note).
#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Gameplay.Contracts
{
    /// <summary>Validation and derivations of authoring ids.</summary>
    public static class AuthoringIds
    {
        /// <summary>Prefix of the stable name a TargetId is derived from (Studio 02 s6).</summary>
        public const string TargetNamePrefix = "auth.";

        public const string DefinitionNamePrefix = "gameplay.def.";

        public const string ScopeNamePrefix = "gameplay.scope.";

        public const string StableKeyPrefix = "gameplay.key.";

        /// <summary>Length of a canonical authoring id (lowercase GUID, "D" format).</summary>
        public const int Length = 36;

        /// <summary>True when <paramref name="id"/> is a canonical lowercase "D"-format GUID.</summary>
        public static bool IsValid(string? id)
        {
            if (id == null || id.Length != Length)
            {
                return false;
            }

            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (i == 8 || i == 13 || i == 18 || i == 23)
                {
                    if (c != '-')
                    {
                        return false;
                    }

                    continue;
                }

                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!hex)
                {
                    return false;
                }
            }

            return id != "00000000-0000-0000-0000-000000000000";
        }

        /// <summary>The canonical authoring id text of a GUID.</summary>
        public static string FromGuid(Guid guid)
        {
            if (guid == Guid.Empty)
            {
                throw new ArgumentException("The empty GUID is not an authoring id.", nameof(guid));
            }

            return guid.ToString("D").ToLowerInvariant();
        }

        /// <summary>Mints a fresh authoring id. Callers mint once and persist the result; it is never re-derived.</summary>
        public static string Mint() => FromGuid(Guid.NewGuid());

        /// <summary>
        /// <c>TargetId</c> of an authored object: <c>StableNameKeyDerivation.Derive("auth." + authoringId)</c>. Accepts
        /// any canonical stable-name text, exactly as the Studio derivation does.
        /// </summary>
        public static TargetId TargetIdFor(string authoringId)
        {
            RequireStableName(authoringId);
            return new TargetId(StableNameKeyDerivation.Derive(TargetNamePrefix + authoringId));
        }

        /// <summary>The kernel definition id of an authored definition.</summary>
        public static DefinitionId DefinitionIdFor(string authoringId)
        {
            RequireStableName(authoringId);
            return new DefinitionId(StableNameKeyDerivation.Derive(DefinitionNamePrefix + authoringId));
        }

        /// <summary>The composition scope of an authored region.</summary>
        public static ScopeId ScopeIdFor(string authoringId)
        {
            RequireStableName(authoringId);
            return new ScopeId(StableNameKeyDerivation.Derive(ScopeNamePrefix + authoringId));
        }

        /// <summary>
        /// The positive int31 stable key an int32 slot carries for an authored object (a region in <c>world.region</c>,
        /// a portal in a travel command). Deterministic; the bake refuses two objects with the same key.
        /// </summary>
        public static int StableKey(string authoringId)
        {
            RequireStableName(authoringId);
            byte[] digest;
            using (SHA256 sha = SHA256.Create())
            {
                digest = sha.ComputeHash(Encoding.UTF8.GetBytes(StableKeyPrefix + authoringId));
            }

            int value = ((digest[0] & 0x7F) << 24) | (digest[1] << 16) | (digest[2] << 8) | digest[3];
            return value == 0 ? 1 : value;
        }

        /// <summary>
        /// The definition revision of a content stamp (lowercase hex SHA-256): its first eight bytes read big-endian; the
        /// reserved zero revision maps to one. The bake and the runtime recipe catalog both use this rule.
        /// </summary>
        public static ulong RevisionOfContentStamp(string contentStamp)
        {
            if (contentStamp == null || contentStamp.Length < 16)
            {
                throw new ArgumentException("A content stamp is 64 lowercase hex characters.", nameof(contentStamp));
            }

            ulong value = 0UL;
            for (int i = 0; i < 16; i++)
            {
                char c = contentStamp[i];
                int digit = c >= '0' && c <= '9' ? c - '0' : (c >= 'a' && c <= 'f' ? c - 'a' + 10 : -1);
                if (digit < 0)
                {
                    throw new ArgumentException("A content stamp is lowercase hex.", nameof(contentStamp));
                }

                value = (value << 4) | (uint)digit;
            }

            return value == 0UL ? 1UL : value;
        }

        private static void RequireStableName(string authoringId)
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
        }
    }
}
