// GameCore.Gameplay.Contracts - stable identities of gameplay declarations (P-004, 05 s3).
//
// Every gameplay identity (owner, slot, stage, buffer, route, schema, factory key, plugin type) is derived from one
// canonical stable name with the kernel's registration-key rule: StableNameKeyDerivation.Derive("gameplay." + name).
// The generated catalog uses the same "gameplay." stable names for its factory keys, so a key named here and the key
// the content compiler emits cannot disagree.
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Gameplay.Contracts
{
    /// <summary>Derivation of gameplay identities from stable names.</summary>
    public static class GameplayIds
    {
        /// <summary>Prefix of every gameplay stable name.</summary>
        public const string Prefix = "gameplay.";

        /// <summary>The full stable name of a gameplay name (<c>gameplay.</c> + name).</summary>
        public static string StableName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A gameplay name cannot be empty.", nameof(name));
            }

            return Prefix + name;
        }

        public static Id128 Id(string name) => StableNameKeyDerivation.Derive(StableName(name));

        /// <summary>A generated factory key (version 1) of a gameplay name; matches the catalog's key.</summary>
        public static FactoryKey Key(string name) => new FactoryKey(Id(name), 1U);

        public static SchemaRef Schema(string name, uint version) => new SchemaRef(new SchemaId(Id(name)), version);

        public static OwnerId Owner(string name) => new OwnerId(Id(name));

        public static StageId Stage(string name) => new StageId(Id(name));

        public static BufferId Buffer(string name) => new BufferId(Id(name));

        public static RouteId Route(string name) => new RouteId(Id(name));

        public static PluginTypeId PluginType(string name) => new PluginTypeId(Id(name));

        public static PluginInstanceId Instance(string name) => new PluginInstanceId(Id(name));

        public static ScopeId Scope(string name) => new ScopeId(Id(name));

        public static TargetId Target(string name) => new TargetId(Id(name));

        public static WorldDefinitionId WorldDefinition(string name) => new WorldDefinitionId(Id(name));

        public static DefinitionId Definition(string name) => new DefinitionId(Id(name));

        /// <summary>Lowercase hex of an identity, the form catalog descriptions carry.</summary>
        public static string Hex(Id128 id) => Id128Codec.ToHex(id);
    }

    /// <summary>
    /// The command issuers of one gameplay world (P1.7a). Kernels that accept a route from specific issuers only
    /// (world.setResidency: host; world.place: host or Studio; player.restoreStamina: host or the narrative ports) compare
    /// against these derivations. The narrative issuer keeps the derivation P1.4 shipped with.
    /// </summary>
    public static class GameplayIssuers
    {
        /// <summary>The application root's (host) issuer: WorldBuilder.IssuerOf.</summary>
        public static Id128 Host(string worldId) => StableNameKeyDerivation.Derive("gameplay.issuer.host." + worldId);

        /// <summary>The gameplay world's player-command issuer (GameplayCommands).</summary>
        public static Id128 Player(string worldId) => StableNameKeyDerivation.Derive("gameplay.issuer.player." + worldId);

        /// <summary>Studio's issuer of a world.</summary>
        public static Id128 Studio(string worldId) => StableNameKeyDerivation.Derive("gameplay.issuer.studio." + worldId);

        /// <summary>The narrative submitter's issuer (logic, quest, inventory, dialogue host halves and delivery ports).</summary>
        public static Id128 Narrative(string worldId) => GameplayIds.Id("gameplay.issuer.narrative." + worldId);
    }

    /// <summary>
    /// Request ids of gameplay commands (P1.7a, A1/A6). Two disjoint ranges:
    ///   * command request ids (positive int31, never 0) derive from the issuer, the logical step the request was made
    ///     at and a per-issuer serial - never from a per-boot counter alone, so a restored world (which resumes at the
    ///     captured step, SADR-012) cannot mint an id an applied request already holds in a destination's ring, and two
    ///     boots fed the same commands mint the same ids;
    ///   * obligation request ids (negative, never int.MinValue) derive from an outbox obligation's identity; a destination
    ///     claims them through the world's step tap, so exactly-once rests on the outbox's own terminal retention instead
    ///     of an eight-entry ring.
    /// </summary>
    public static class GameplayRequestIds
    {
        /// <summary>True for an obligation request id (negative and not int.MinValue).</summary>
        public static bool IsObligation(int requestId) => requestId < 0 && requestId != int.MinValue;

        /// <summary>True for a command request id a destination ring tracks (positive).</summary>
        public static bool IsCommand(int requestId) => requestId > 0;

        /// <summary>The obligation request id of an outbox obligation identity (negative, never int.MinValue).</summary>
        public static int OfObligation(Id128 outboxId)
        {
            int value = (int)((outboxId.High >> 33) & 0x7FFFFFFFUL);
            return value == 0 ? -1 : -value;
        }

        /// <summary>A command request id: positive int31 of (issuer, logical step, serial); never 0.</summary>
        public static int OfCommand(Id128 issuer, ulong step, ulong serial)
        {
            ulong h = Mix(issuer.High ^ 0x9E3779B97F4A7C15UL);
            h = Mix(h ^ issuer.Low);
            h = Mix(h ^ step);
            h = Mix(h ^ serial);
            int value = (int)(h & 0x7FFFFFFFUL);
            return value == 0 ? 1 : value;
        }

        // splitmix64 finalizer: a fixed, platform-independent mix.
        private static ulong Mix(ulong z)
        {
            unchecked
            {
                z += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }

    /// <summary>
    /// Slot identities: <c>SlotNames.Of("world", "posX")</c> is the slot <c>gameplay.slot.world.pos-x</c>. The member name
    /// is written in camelCase in code and kebab-case in the stable name, so the derivation stays canonical lowercase.
    /// </summary>
    public static class SlotNames
    {
        public const string SlotPrefix = "slot.";

        /// <summary>The slot id of <paramref name="name"/> in <paramref name="domain"/>.</summary>
        public static SlotId Of(string domain, string name) => new SlotId(GameplayIds.Id(StableName(domain, name)));

        /// <summary>The stable name (without the gameplay prefix) of a slot: <c>slot.&lt;domain&gt;.&lt;kebab-name&gt;</c>.</summary>
        public static string StableName(string domain, string name)
        {
            if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A slot needs a domain and a member name.");
            }

            string stable = SlotPrefix + Kebab(domain) + "." + Kebab(name);
            if (!StableNameKeyDerivation.IsCanonicalStableName(GameplayIds.Prefix + stable))
            {
                throw new ArgumentException("Slot '" + domain + "." + name + "' does not form a canonical stable name.");
            }

            return stable;
        }

        /// <summary>camelCase to kebab-case: <c>scaleMilli</c> becomes <c>scale-milli</c>.</summary>
        public static string Kebab(string name)
        {
            var builder = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c >= 'A' && c <= 'Z')
                {
                    if (i > 0 && name[i - 1] != '.' && name[i - 1] != '-')
                    {
                        builder.Append('-');
                    }

                    builder.Append((char)(c - 'A' + 'a'));
                    continue;
                }

                builder.Append(c);
            }

            return builder.ToString();
        }
    }

    /// <summary>What a registered gameplay name identifies.</summary>
    public enum GameplayIdKind
    {
        Route = 0,
        CommandSchema = 1,
        EventSchema = 2,
        Slot = 3,
        Stage = 4,
        Buffer = 5,
        Owner = 6,
    }

    /// <summary>
    /// A per-plugin registry of command, event and slot names. It derives each identity once and refuses a second name
    /// that derives the same identity, so a manifest never carries two declarations for one id (P-004). Instance state
    /// only: each plugin declaration owns its own registry.
    /// </summary>
    public sealed class GameplayIdRegistry
    {
        private readonly Dictionary<Id128, string> names = new Dictionary<Id128, string>();
        private readonly List<string> order = new List<string>();

        public int Count => names.Count;

        /// <summary>Registered names in registration order, each as <c>kind:name</c>.</summary>
        public IReadOnlyList<string> Names => order;

        /// <summary>Registers one name of one kind and returns its identity; throws on a collision.</summary>
        public Id128 Register(GameplayIdKind kind, string name)
        {
            Id128 id = GameplayIds.Id(name);
            string label = kind.ToString() + ":" + name;
            if (names.TryGetValue(id, out string? existing))
            {
                if (string.Equals(existing, label, StringComparison.Ordinal))
                {
                    return id;
                }

                throw new InvalidOperationException(
                    "Gameplay names '" + existing + "' and '" + label + "' derive the same identity " + GameplayIds.Hex(id) + ".");
            }

            names.Add(id, label);
            order.Add(label);
            return id;
        }

        public RouteId Route(string name) => new RouteId(Register(GameplayIdKind.Route, name));

        public SchemaRef Command(string name, uint version) =>
            new SchemaRef(new SchemaId(Register(GameplayIdKind.CommandSchema, name)), version);

        public SchemaRef Event(string name, uint version) =>
            new SchemaRef(new SchemaId(Register(GameplayIdKind.EventSchema, name)), version);

        public SlotId Slot(string domain, string member) =>
            new SlotId(Register(GameplayIdKind.Slot, SlotNames.StableName(domain, member)));

        public StageId Stage(string name) => new StageId(Register(GameplayIdKind.Stage, name));

        public BufferId Buffer(string name) => new BufferId(Register(GameplayIdKind.Buffer, name));

        public OwnerId Owner(string name) => new OwnerId(Register(GameplayIdKind.Owner, name));

        public bool Contains(Id128 id) => names.ContainsKey(id);
    }
}
