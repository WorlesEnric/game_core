// GameCore.Studio.Model - authoring identity (docs/studio/03-authoring-contracts.md s1, 02 s6).
// An AuthoringRef is a pointer to an authored thing that is stable across sessions. Unity instance ids, ECS entity
// indices and TargetHandles never appear here (03 s1): those are session-local and are not authoring identity.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace GameCore.Studio.Model
{
    /// <summary>A pointer to an authored thing, stable across sessions (03 s1). Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class AuthoringRef : IEquatable<AuthoringRef>
    {
        private static readonly Regex DefinitionPattern = new Regex(StudioPatterns.DefinitionRef, RegexOptions.CultureInvariant);

        [JsonConstructor]
        public AuthoringRef(
            AuthoringKind kind,
            string? authoringId = null,
            string? global = null,
            string? assetGuid = null,
            string? path = null,
            string? definition = null,
            AuthorScope? scope = null,
            string? stamp = null,
            LocationRef? location = null)
        {
            Kind = kind;
            AuthoringId = authoringId;
            Global = global;
            AssetGuid = assetGuid;
            Path = path;
            Definition = definition;
            Scope = scope;
            Stamp = stamp;
            Location = location;
        }

        [JsonProperty("kind", Required = Required.Always)]
        public AuthoringKind Kind { get; }

        /// <summary>GUID minted once by the authoring importer; absent for Location and Asset.</summary>
        [JsonProperty("authoringId", NullValueHandling = NullValueHandling.Ignore)]
        [SchemaHint(MinLength = 1)]
        public string? AuthoringId { get; }

        /// <summary>Unity GlobalObjectId text; absent for Location.</summary>
        [JsonProperty("global", NullValueHandling = NullValueHandling.Ignore)]
        [SchemaHint(MinLength = 1)]
        public string? Global { get; }

        [JsonProperty("assetGuid", NullValueHandling = NullValueHandling.Ignore)]
        [SchemaHint(MinLength = 1)]
        public string? AssetGuid { get; }

        /// <summary>Asset path, optionally with a <c>#/hierarchy/path</c> suffix for scene objects.</summary>
        [JsonProperty("path", NullValueHandling = NullValueHandling.Ignore)]
        [SchemaHint(MinLength = 1)]
        public string? Path { get; }

        /// <summary>DefinitionRef text form <c>name@revision</c> when the thing has a definition.</summary>
        [JsonProperty("definition", NullValueHandling = NullValueHandling.Ignore)]
        [SchemaHint(Pattern = StudioPatterns.DefinitionRef)]
        public string? Definition { get; }

        /// <summary>What the user chose to edit; a single value.</summary>
        [JsonProperty("scope", NullValueHandling = NullValueHandling.Ignore)]
        public AuthorScope? Scope { get; }

        /// <summary>Content stamp at selection time; the precondition value of an operation (03 s1).</summary>
        [JsonProperty("stamp", NullValueHandling = NullValueHandling.Ignore)]
        [SchemaHint(Pattern = StudioPatterns.Stamp)]
        public string? Stamp { get; }

        /// <summary>Kind=Location only.</summary>
        [JsonProperty("location", NullValueHandling = NullValueHandling.Ignore)]
        public LocationRef? Location { get; }

        /// <summary>
        /// Identity used to match two refs to the same authored thing regardless of stamp or scope: the authoring id
        /// when present, else the GlobalObjectId, else the asset GUID, else the path; a Location by region+position.
        /// </summary>
        public string IdentityKey
        {
            get
            {
                if (Kind == AuthoringKind.Location && Location != null)
                {
                    return "location:" + Location.Region + "@" + Location.PositionText();
                }

                if (AuthoringId != null)
                {
                    return "auth:" + AuthoringId;
                }

                if (Global != null)
                {
                    return "global:" + Global;
                }

                if (AssetGuid != null)
                {
                    return "asset:" + AssetGuid;
                }

                if (Path != null)
                {
                    return "path:" + Path;
                }

                return "unidentified:" + Kind.ToString();
            }
        }

        /// <summary>
        /// True when both refs name the same authored thing: same kind and the strongest identifier both carry is equal
        /// (authoring id, then GlobalObjectId, then asset GUID + path, then path; a Location by region + position).
        /// Stamp and scope are ignored.
        /// </summary>
        public bool SameTarget(AuthoringRef? other)
        {
            if (other == null || other.Kind != Kind)
            {
                return false;
            }

            if (Kind == AuthoringKind.Location)
            {
                return Location != null && other.Location != null
                    && string.Equals(Location.Region, other.Location.Region, StringComparison.Ordinal)
                    && string.Equals(Location.PositionText(), other.Location.PositionText(), StringComparison.Ordinal);
            }

            if (AuthoringId != null && other.AuthoringId != null)
            {
                return string.Equals(AuthoringId, other.AuthoringId, StringComparison.Ordinal);
            }

            if (Global != null && other.Global != null)
            {
                return string.Equals(Global, other.Global, StringComparison.Ordinal);
            }

            if (AssetGuid != null && other.AssetGuid != null)
            {
                return string.Equals(AssetGuid, other.AssetGuid, StringComparison.Ordinal)
                    && (Path == null || other.Path == null || string.Equals(Path, other.Path, StringComparison.Ordinal));
            }

            return Path != null && other.Path != null && string.Equals(Path, other.Path, StringComparison.Ordinal);
        }

        /// <summary>A copy with a different content stamp.</summary>
        public AuthoringRef WithStamp(string? stamp)
        {
            return new AuthoringRef(Kind, AuthoringId, Global, AssetGuid, Path, Definition, Scope, stamp, Location);
        }

        /// <summary>A copy with a different edit scope.</summary>
        public AuthoringRef WithScope(AuthorScope? scope)
        {
            return new AuthoringRef(Kind, AuthoringId, Global, AssetGuid, Path, Definition, scope, Stamp, Location);
        }

        /// <summary>Violations of the 03 s1 shape rules, as human-readable sentences; empty when the ref is well formed.</summary>
        public IReadOnlyList<string> ShapeProblems()
        {
            List<string> problems = new List<string>();
            if (Kind == AuthoringKind.Location)
            {
                if (Location == null)
                {
                    problems.Add("a Location ref needs 'location'");
                }

                if (AuthoringId != null)
                {
                    problems.Add("a Location ref has no 'authoringId'");
                }

                if (Global != null)
                {
                    problems.Add("a Location ref has no 'global'");
                }
            }
            else
            {
                if (Location != null)
                {
                    problems.Add("'location' is allowed only on a Location ref");
                }

                if (Kind == AuthoringKind.Asset && AuthoringId != null)
                {
                    problems.Add("an Asset ref has no 'authoringId'");
                }

                if (AuthoringId == null && Global == null && AssetGuid == null && Path == null)
                {
                    problems.Add("a " + Kind.ToString() + " ref needs one of 'authoringId', 'global', 'assetGuid' or 'path'");
                }
            }

            if (Location != null)
            {
                problems.AddRange(Location.ShapeProblems());
            }

            if (Stamp != null && !ContentStamp.IsValid(Stamp))
            {
                problems.Add("'stamp' must be 'sha256:' plus 64 lowercase hex digits");
            }

            if (Definition != null && !DefinitionPattern.IsMatch(Definition))
            {
                problems.Add("'definition' must be 'name@revision'");
            }

            if (Scope.HasValue && !IsSingleScope(Scope.Value))
            {
                problems.Add("'scope' must be exactly one of Instance, Prefab, Definition, Scope");
            }

            return problems;
        }

        /// <summary>True when <paramref name="scope"/> is exactly one defined flag.</summary>
        public static bool IsSingleScope(AuthorScope scope)
        {
            return scope == AuthorScope.Instance || scope == AuthorScope.Prefab || scope == AuthorScope.Definition || scope == AuthorScope.Scope;
        }

        public bool Equals(AuthoringRef? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return other != null
                && Kind == other.Kind
                && string.Equals(AuthoringId, other.AuthoringId, StringComparison.Ordinal)
                && string.Equals(Global, other.Global, StringComparison.Ordinal)
                && string.Equals(AssetGuid, other.AssetGuid, StringComparison.Ordinal)
                && string.Equals(Path, other.Path, StringComparison.Ordinal)
                && string.Equals(Definition, other.Definition, StringComparison.Ordinal)
                && Scope == other.Scope
                && string.Equals(Stamp, other.Stamp, StringComparison.Ordinal)
                && Equals(Location, other.Location);
        }

        public override bool Equals(object? obj) => obj is AuthoringRef other && Equals(other);

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(IdentityKey) ^ (int)Kind;
        }

        public override string ToString() => Kind.ToString() + "(" + IdentityKey + ")";
    }

    /// <summary>A sampled world location (03 s1/s2, <c>PointAt</c>). Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class LocationRef : IEquatable<LocationRef>
    {
        [JsonConstructor]
        public LocationRef(string region, IReadOnlyList<double> position, IReadOnlyList<double>? normal = null)
        {
            Region = ModelLists.NotEmpty(region, nameof(region));
            Position = ModelLists.Required(position, nameof(position));
            Normal = ModelLists.Optional(normal, nameof(normal));
        }

        /// <summary>Region identifier (e.g. <c>marsh</c>).</summary>
        [JsonProperty("region", Required = Required.Always)]
        [SchemaHint(MinLength = 1)]
        public string Region { get; }

        /// <summary>World position in metres, [x, y, z].</summary>
        [JsonProperty("position", Required = Required.Always)]
        [SchemaHint(MinItems = 3, MaxItems = 3)]
        public IReadOnlyList<double> Position { get; }

        /// <summary>Surface normal, [x, y, z].</summary>
        [JsonProperty("normal", NullValueHandling = NullValueHandling.Ignore)]
        [SchemaHint(MinItems = 3, MaxItems = 3)]
        public IReadOnlyList<double>? Normal { get; }

        internal string PositionText()
        {
            string[] parts = new string[Position.Count];
            for (int i = 0; i < Position.Count; i++)
            {
                parts[i] = Position[i].ToString("R", CultureInfo.InvariantCulture);
            }

            return string.Join(",", parts);
        }

        internal IReadOnlyList<string> ShapeProblems()
        {
            List<string> problems = new List<string>();
            if (Position.Count != 3)
            {
                problems.Add("'location.position' must have 3 components");
            }

            if (Normal != null && Normal.Count != 3)
            {
                problems.Add("'location.normal' must have 3 components");
            }

            return problems;
        }

        public bool Equals(LocationRef? other)
        {
            if (other == null || !string.Equals(Region, other.Region, StringComparison.Ordinal))
            {
                return false;
            }

            return SequenceEqual(Position, other.Position) && SequenceEqual(Normal, other.Normal);
        }

        public override bool Equals(object? obj) => obj is LocationRef other && Equals(other);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Region + "@" + PositionText());

        private static bool SequenceEqual(IReadOnlyList<double>? left, IReadOnlyList<double>? right)
        {
            if (left == null || right == null)
            {
                return left == null && right == null;
            }

            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!left[i].Equals(right[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
