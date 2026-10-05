// GameCore.Gameplay.Logic - the kernel plumbing the four narrative plugins share (P1.4).
//
//   NarrativePluginSpec   builds one plugin's manifest, routes, lanes and system registration with exactly the shapes of
//                         the entities and world plugins (one owner, one stage, own domains ReadWrite, one bounded
//                         lane per route, PreserveDormant slots), plus optional-after edges for the stage order
//                         world -> inventory -> quest -> dialogue -> logic
//   NarrativeCommand      the payload of every narrative command: a fixed number of little-endian int32 values
//   NarrativeIndex        key -> target for every narrative target, fact key -> slot, entity key -> entity target
//   NarrativeState        IConditionState over committed slots (in-step it reads the same storage, so a stage sees
//                         what earlier stages of the step wrote)
//   NarrativeSlots        owned-slot reads and writes on targets through the registry
//   StepEventBatch        commits several events for one command (extra events as request-kind copies, then the
//                         command's own result last), the shape the world plugin uses for RegionLeft/RegionEntered
//   NarrativeSubmitter    submits narrative commands with one issuer and a strictly increasing sequence
//
// P1.7a:
//   * A route may accept optional trailing int32 values (the obligation request id the delivery ports append, A1): the
//     reader accepts Ints..Ints+Optional values; absent values read as 0.
//   * StepEventBatch.CommitAll takes the world's step tap: it refuses before mutation when the outbox has no room, and
//     hands every committed event to the tap, which records the deliveries it owes in the same step (A1).
//   * NarrativeObligations is the destination half: the claim/ring decision and the settle after the effect committed.
//   * NarrativeState.NowMs reads GameplayClock (step x stepMs in a command-driven world, A5).
//   * NarrativeSubmitter.NextRequestId derives command request ids from (issuer, logical step, serial) (A6).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Inventory;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using GameCore.Unity.Runtime.Time;
using Unity.Entities;

namespace GameCore.Gameplay.Logic
{
    /// <summary>The payload of every narrative command: a fixed count of int32 values.</summary>
    public readonly struct NarrativeCommand
    {
        private readonly int[]? values;

        public NarrativeCommand(int[] values)
        {
            this.values = values;
        }

        public int Count => values != null ? values.Length : 0;

        public int this[int index] => values != null && index >= 0 && index < values.Length ? values[index] : 0;
    }

    /// <summary>Reader of one narrative command schema: <see cref="Ints"/> int32 values plus up to <see cref="Optional"/> more.</summary>
    public sealed class NarrativeCommandReader : ICommandPayloadReader<NarrativeCommand>
    {
        public NarrativeCommandReader(SchemaRef schema, int ints)
            : this(schema, ints, 0)
        {
        }

        public NarrativeCommandReader(SchemaRef schema, int ints, int optional)
        {
            Schema = schema;
            Ints = ints;
            Optional = optional < 0 ? 0 : optional;
        }

        public SchemaRef Schema { get; }

        public int Ints { get; }

        /// <summary>Optional trailing int32 values (P1.7a: the obligation request id).</summary>
        public int Optional { get; }

        public NarrativeCommand Read(IReadOnlyList<byte> payload)
        {
            for (int extra = 0; extra <= Optional; extra++)
            {
                if (NarrativeCommands.TryReadInts(payload, Ints + extra, out int[] values))
                {
                    return new NarrativeCommand(values);
                }
            }

            throw new FormatException("a " + Schema + " command is " + (Ints * 4).ToString(CultureInfo.InvariantCulture)
                + (Optional > 0 ? ".." + ((Ints + Optional) * 4).ToString(CultureInfo.InvariantCulture) : string.Empty) + " bytes");
        }
    }

    /// <summary>One command route of a narrative plugin.</summary>
    public sealed class NarrativeRoute
    {
        public NarrativeRoute(RouteId route, SchemaRef command, string name, int ints)
            : this(route, command, name, ints, 0)
        {
        }

        public NarrativeRoute(RouteId route, SchemaRef command, string name, int ints, int optional)
        {
            Route = route;
            Command = command;
            Name = name;
            Ints = ints;
            Optional = optional < 0 ? 0 : optional;
        }

        /// <summary>Optional trailing int32 values (P1.7a).</summary>
        public int Optional { get; }

        public RouteId Route { get; }

        public SchemaRef Command { get; }

        /// <summary>Short name (buffer and order keys derive from it).</summary>
        public string Name { get; }

        public int Ints { get; }
    }

    /// <summary>One slot a narrative plugin declares, with the domain it belongs to.</summary>
    public sealed class NarrativeSlotDeclaration
    {
        public NarrativeSlotDeclaration(SlotId slot, int domain, string field)
        {
            Slot = slot;
            Domain = domain;
            Field = field;
        }

        public SlotId Slot { get; }

        /// <summary>Index into the plugin's domains.</summary>
        public int Domain { get; }

        public string Field { get; }
    }

    /// <summary>Everything one narrative plugin declares, built in the shapes of the P1.1 plugins.</summary>
    public sealed class NarrativePluginSpec
    {
        public const string PackageVersion = "1.0.0";

        /// <summary>Rows one command lane holds per step (P-043).</summary>
        public const int LaneCapacity = 16;

        private readonly List<NarrativeRoute> routes = new List<NarrativeRoute>();
        private readonly List<NarrativeSlotDeclaration> slots = new List<NarrativeSlotDeclaration>();
        private readonly HashSet<SlotId> declared = new HashSet<SlotId>();
        private readonly List<StageId> after = new List<StageId>();

        public NarrativePluginSpec(string stem, NarrativeCatalogSet catalog, OwnerId owner)
        {
            Stem = stem;
            Catalog = catalog;
            Owner = owner;
            OwnerPackage = GameplayIds.Id(catalog.Package);
            PluginType = GameplayIds.PluginType(stem + ".plugin-type");
            Instance = GameplayIds.Instance(stem + ".instance");
            PluginFactory = GameplayIds.Key(catalog.Plugin);
            ConfigSchema = GameplayIds.Schema(catalog.Schemas[0].Schema, 1U);
            var domains = new List<SchemaRef>();
            for (int i = 1; i < catalog.Schemas.Count; i++)
            {
                domains.Add(GameplayIds.Schema(catalog.Schemas[i].Schema, 1U));
            }

            Domains = domains;
            var layouts = new List<FactoryKey>();
            for (int i = 0; i < catalog.Layouts.Count; i++)
            {
                layouts.Add(GameplayIds.Key(catalog.Layouts[i]));
            }

            Layouts = layouts;
            Stage = GameplayIds.Stage(stem + ".stage.command");
            CommandSystem = GameplayIds.Key(catalog.CommandSystem);
            Applier = GameplayIds.Key(catalog.Applier);
            IngressProducer = GameplayIds.Key(stem + ".ingress");
            RecipeSchema = GameplayIds.Schema(stem + ".schema.recipe", 1U);
        }

        public string Stem { get; }

        public NarrativeCatalogSet Catalog { get; }

        public OwnerId Owner { get; }

        public Id128 OwnerPackage { get; }

        public PluginTypeId PluginType { get; }

        public PluginInstanceId Instance { get; }

        public FactoryKey PluginFactory { get; }

        public SchemaRef ConfigSchema { get; }

        public IReadOnlyList<SchemaRef> Domains { get; }

        public IReadOnlyList<FactoryKey> Layouts { get; }

        public StageId Stage { get; }

        public FactoryKey CommandSystem { get; }

        public FactoryKey Applier { get; }

        public FactoryKey IngressProducer { get; }

        public SchemaRef RecipeSchema { get; }

        public IReadOnlyList<NarrativeRoute> RouteList => routes;

        public IReadOnlyList<NarrativeSlotDeclaration> SlotList => slots;

        /// <summary>The stage of another narrative plugin (or the world plugin) this stage runs after, when present.</summary>
        public static StageId StageOf(string stem) => GameplayIds.Stage(stem + ".stage.command");

        public NarrativePluginSpec Route(RouteId route, SchemaRef command, string name, int ints)
        {
            routes.Add(new NarrativeRoute(route, command, name, ints));
            return this;
        }

        /// <summary>A route whose command may carry <paramref name="optional"/> trailing int32 values (P1.7a).</summary>
        public NarrativePluginSpec Route(RouteId route, SchemaRef command, string name, int ints, int optional)
        {
            routes.Add(new NarrativeRoute(route, command, name, ints, optional));
            return this;
        }

        public NarrativePluginSpec Slot(SlotId slot, int domain, string field)
        {
            if (declared.Add(slot))
            {
                slots.Add(new NarrativeSlotDeclaration(slot, domain < 0 || domain >= Domains.Count ? 0 : domain, Stem + ".field." + field));
            }

            return this;
        }

        public NarrativePluginSpec After(StageId stage)
        {
            if (!after.Contains(stage))
            {
                after.Add(stage);
            }

            return this;
        }

        /// <summary>Non-empty package content hash: SHA-256 of the canonical declaration names, slots included (P-009).</summary>
        public ContentHash PackageContentHash()
        {
            var text = new StringBuilder("gameplay." + Stem + "/" + PackageVersion + ";" + GameplayIds.Hex(PluginType.Value) + ";" + GameplayIds.Hex(Stage.Value));
            for (int i = 0; i < Domains.Count; i++)
            {
                text.Append(';').Append(GameplayIds.Hex(Domains[i].Id.Value));
            }

            for (int i = 0; i < slots.Count; i++)
            {
                text.Append(';').Append(GameplayIds.Hex(slots[i].Slot.Value));
            }

            return ContentHash.Compute(Encoding.UTF8.GetBytes(text.ToString()));
        }

        public PluginManifest Manifest()
        {
            return new PluginManifest(
                PluginType,
                PackageVersion,
                PackageContentHash(),
                new SupportedProtocolRange(1, 0, 0),
                null,
                ConfigSchema,
                PluginFactory,
                null,
                null,
                null,
                null,
                null,
                Slots(),
                Stages(),
                Buffers(),
                null);
        }

        public IReadOnlyList<StateSlotSpec> Slots()
        {
            var list = new List<StateSlotSpec>(slots.Count);
            for (int i = 0; i < slots.Count; i++)
            {
                NarrativeSlotDeclaration declaration = slots[i];
                SchemaRef domain = Domains[declaration.Domain];
                FactoryKey layout = Layouts[declaration.Domain < Layouts.Count ? declaration.Domain : 0];
                list.Add(new StateSlotSpec(
                    declaration.Slot,
                    Owner,
                    domain,
                    layout,
                    new List<FieldOwnership> { new FieldOwnership(domain, GameplayIds.Id(declaration.Field)) },
                    default(FactoryKey),
                    default(FactoryKey),
                    default(FactoryKey),
                    LastSupportPolicy.PreserveDormant,
                    default(FactoryKey),
                    null));
            }

            return list;
        }

        public IReadOnlyList<StageSpec> Stages()
        {
            AccessSet access = Access();
            return new List<StageSpec>
            {
                new StageSpec(
                    Stage,
                    1U,
                    OwnerPackage,
                    HostAffinity.ManagedMain,
                    null,
                    null,
                    access,
                    null,
                    null,
                    null,
                    after.Count > 0 ? new List<StageId>(after) : null,
                    new List<SystemSpec> { new SystemSpec(CommandSystem, SystemMultiplicity.World, access, null, null, null, null) },
                    null),
            };
        }

        public IReadOnlyList<BufferSpec> Buffers()
        {
            var list = new List<BufferSpec>(routes.Count);
            for (int i = 0; i < routes.Count; i++)
            {
                NarrativeRoute route = routes[i];
                list.Add(new BufferSpec(
                    BufferOf(route),
                    route.Command,
                    new List<FactoryKey> { IngressProducer },
                    Stage,
                    Stage,
                    OrderOf(route),
                    BufferLifetime.Step,
                    LaneCapacity,
                    BufferOverflowPolicy.RejectBeforeMutation,
                    BufferCancellationPolicy.Drain));
            }

            return list;
        }

        public IReadOnlyList<CommandRoute> Routes()
        {
            var list = new List<CommandRoute>(routes.Count);
            for (int i = 0; i < routes.Count; i++)
            {
                NarrativeRoute route = routes[i];
                list.Add(new CommandRoute(route.Route, Owner, route.Command, Stage, Stage, BufferOf(route), IngressProducer, LaneCapacity, false));
            }

            return list;
        }

        public IReadOnlyList<MessageBufferDescriptor> Lanes()
        {
            var list = new List<MessageBufferDescriptor>(routes.Count);
            for (int i = 0; i < routes.Count; i++)
            {
                NarrativeRoute route = routes[i];
                int bytes = Math.Max(256, LaneCapacity * (route.Ints + route.Optional) * 4);
                list.Add(new MessageBufferDescriptor(
                    BufferOf(route),
                    route.Command,
                    new[] { IngressProducer },
                    Owner,
                    Stage,
                    Stage,
                    OrderOf(route),
                    BufferLifetime.Step,
                    LaneCapacity,
                    bytes,
                    BufferOverflowPolicy.RejectBeforeMutation,
                    BufferCancellationPolicy.Drain));
            }

            return list;
        }

        public void BindReaders(CommandPayloadReaders readers)
        {
            for (int i = 0; i < routes.Count; i++)
            {
                if (!readers.TryBind(new NarrativeCommandReader(routes[i].Command, routes[i].Ints, routes[i].Optional), out string failure))
                {
                    throw new InvalidOperationException(Stem + " reader registration failed: " + failure);
                }
            }
        }

        public DefinitionRef Recipe(string kind) =>
            new DefinitionRef(GameplayIds.Definition(Stem + ".recipe." + kind), RecipeSchema, DefinitionRevision.First);

        public SpawnRecipe CreateRecipe(string kind)
        {
            DefinitionRef recipe = Recipe(kind);
            var schemas = new List<SchemaRef> { RecipeSchema };
            var descriptor = new TargetDescriptor(recipe, schemas, null, null, default(AssetAdapterDescriptor), null, null, null, null);
            return new SpawnRecipe(recipe, descriptor, schemas, new NarrativeApplier(Applier));
        }

        private AccessSet Access()
        {
            var declarations = new List<AccessDeclaration>();
            for (int i = 0; i < Domains.Count; i++)
            {
                declarations.Add(new AccessDeclaration(Domains[i], AccessMode.ReadWrite, default(Id128)));
            }

            return new AccessSet(declarations);
        }

        private BufferId BufferOf(NarrativeRoute route) => GameplayIds.Buffer(Stem + ".buffer." + route.Name);

        private FactoryKey OrderOf(NarrativeRoute route) => GameplayIds.Key(Stem + ".order." + route.Name);
    }

    /// <summary>Base layout of every narrative target: an empty owned-slot buffer (slots are seeded after boot).</summary>
    public sealed class NarrativeApplier : ISpawnApplier
    {
        public NarrativeApplier(FactoryKey key)
        {
            Key = key;
        }

        public FactoryKey Key { get; }

        public void ApplyBaseLayout(EntityManager entityManager, Entity entity, SpawnRecipe recipe)
        {
            if (!entityManager.HasBuffer<TargetSlotState>(entity))
            {
                entityManager.AddBuffer<TargetSlotState>(entity);
            }
        }
    }

    /// <summary>Dispatch-kind resolution over several tables (the world's and the narrative plugins').</summary>
    public sealed class CompositeDispatchKinds : IScheduleDispatchKindResolver
    {
        private readonly List<IScheduleDispatchKindResolver> resolvers = new List<IScheduleDispatchKindResolver>();

        public CompositeDispatchKinds(params IScheduleDispatchKindResolver?[] tables)
        {
            for (int i = 0; i < tables.Length; i++)
            {
                if (tables[i] != null)
                {
                    resolvers.Add(tables[i]!);
                }
            }
        }

        public bool TryResolveKind(FactoryKey systemKey, out SystemDispatchKind kind)
        {
            for (int i = 0; i < resolvers.Count; i++)
            {
                if (resolvers[i].TryResolveKind(systemKey, out kind))
                {
                    return true;
                }
            }

            kind = default(SystemDispatchKind);
            return false;
        }
    }

    /// <summary>What kind of narrative target a key addresses.</summary>
    public enum NarrativeTargetKind
    {
        State = 0,
        Hub = 1,
        Graph = 2,
        Quest = 3,
        Inventory = 4,
        Vendor = 5,
        WorldItem = 6,
        Rule = 7,
    }

    /// <summary>One narrative target of a world.</summary>
    public sealed class NarrativeTarget
    {
        public NarrativeTarget(NarrativeTargetKind kind, int key, string authoringId, string name, TargetId target)
        {
            Kind = kind;
            Key = key;
            AuthoringId = authoringId;
            Name = name;
            Target = target;
        }

        public NarrativeTargetKind Kind { get; }

        public int Key { get; }

        public string AuthoringId { get; }

        public string Name { get; }

        public TargetId Target { get; }
    }

    /// <summary>Every narrative target of one world and the key/slot tables that address them.</summary>
    public sealed class NarrativeIndex
    {
        private readonly Dictionary<long, NarrativeTarget> byKindKey = new Dictionary<long, NarrativeTarget>();
        private readonly List<NarrativeTarget> targets = new List<NarrativeTarget>();
        private readonly Dictionary<int, SlotId> factSlots = new Dictionary<int, SlotId>();
        private readonly Dictionary<int, TargetId> entities = new Dictionary<int, TargetId>();
        private readonly Dictionary<int, string> entityIds = new Dictionary<int, string>();
        private readonly Dictionary<int, string> regionIds = new Dictionary<int, string>();

        public NarrativeIndex(RegionManifest world, GameplayContentManifest content, NarrativeModelSet models)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            WorldId = world.WorldId;
            StateTarget = DialogueIds.StateTarget(WorldId);
            HubTarget = LogicIds.HubTarget(WorldId);
            Add(new NarrativeTarget(NarrativeTargetKind.State, 0, string.Empty, "narrative state", StateTarget));
            Add(new NarrativeTarget(NarrativeTargetKind.Hub, 0, string.Empty, "logic hub", HubTarget));
            foreach (FactModel fact in models.Facts)
            {
                factSlots[fact.Key] = DialogueIds.Fact(fact.Name);
            }

            if (content != null)
            {
                for (int i = 0; i < content.Entries.Count; i++)
                {
                    ContentEntry entry = content.Entries[i];
                    NarrativeTargetKind kind;
                    if (!KindOf(entry.kind, out kind))
                    {
                        continue;
                    }

                    if (kind == NarrativeTargetKind.WorldItem)
                    {
                        Add(new NarrativeTarget(kind, entry.key, entry.authoringId, entry.name, AuthoringIds.TargetIdFor(entry.authoringId)));
                        continue;
                    }

                    Add(new NarrativeTarget(kind, entry.key, entry.authoringId, entry.name, TargetOf(kind, entry.authoringId)));
                }
            }

            for (int i = 0; i < world.Entities.Count; i++)
            {
                ManifestEntity entity = world.Entities[i];
                int key = AuthoringIds.StableKey(entity.authoringId);
                entities[key] = AuthoringIds.TargetIdFor(entity.authoringId);
                entityIds[key] = entity.authoringId;
            }

            for (int i = 0; i < world.Regions.Count; i++)
            {
                regionIds[world.Regions[i].key] = world.Regions[i].authoringId;
            }

            FocusEntityKey = AuthoringIds.IsValid(world.FocusEntityId) ? AuthoringIds.StableKey(world.FocusEntityId) : 0;
            Focus = AuthoringIds.IsValid(world.FocusEntityId) ? AuthoringIds.TargetIdFor(world.FocusEntityId) : default(TargetId);
            PlayerInventoryKey = models.PlayerInventory != null ? models.PlayerInventory.Key : 0;
        }

        public string WorldId { get; }

        public TargetId StateTarget { get; }

        public TargetId HubTarget { get; }

        public int FocusEntityKey { get; }

        public TargetId Focus { get; }

        public int PlayerInventoryKey { get; }

        public IReadOnlyList<NarrativeTarget> Targets => targets;

        /// <summary>The target id of a definition-backed narrative target.</summary>
        public static TargetId TargetOf(NarrativeTargetKind kind, string authoringId) =>
            GameplayIds.Target("narrative." + kind.ToString().ToLowerInvariant() + "." + authoringId);

        public static bool KindOf(string narrativeKind, out NarrativeTargetKind kind)
        {
            switch (narrativeKind)
            {
                case NarrativeKinds.Graph: kind = NarrativeTargetKind.Graph; return true;
                case NarrativeKinds.Quest: kind = NarrativeTargetKind.Quest; return true;
                case NarrativeKinds.Inventory: kind = NarrativeTargetKind.Inventory; return true;
                case NarrativeKinds.Vendor: kind = NarrativeTargetKind.Vendor; return true;
                case NarrativeKinds.WorldItem: kind = NarrativeTargetKind.WorldItem; return true;
                case NarrativeKinds.Rule: kind = NarrativeTargetKind.Rule; return true;
                default: kind = NarrativeTargetKind.State; return false;
            }
        }

        public bool TryTarget(NarrativeTargetKind kind, int key, out NarrativeTarget? target) =>
            byKindKey.TryGetValue(Pair(kind, key), out target);

        public TargetId TargetOf(NarrativeTargetKind kind, int key) =>
            byKindKey.TryGetValue(Pair(kind, key), out NarrativeTarget? found) && found != null ? found.Target : default(TargetId);

        /// <summary>The target of an inventory key: an inventory, else a vendor's stock.</summary>
        public bool TryInventoryTarget(int key, out TargetId target)
        {
            int resolved = key == 0 ? PlayerInventoryKey : key;
            if (byKindKey.TryGetValue(Pair(NarrativeTargetKind.Inventory, resolved), out NarrativeTarget? inventory) && inventory != null)
            {
                target = inventory.Target;
                return true;
            }

            if (byKindKey.TryGetValue(Pair(NarrativeTargetKind.Vendor, resolved), out NarrativeTarget? vendor) && vendor != null)
            {
                target = vendor.Target;
                return true;
            }

            target = default(TargetId);
            return false;
        }

        public bool TryFactSlot(int factKey, out SlotId slot) => factSlots.TryGetValue(factKey, out slot);

        public IEnumerable<KeyValuePair<int, SlotId>> FactSlots => factSlots;

        /// <summary>The target of an entity key; key 0 is the world's focus entity.</summary>
        public bool TryEntityTarget(int entityKey, out TargetId target)
        {
            if (entityKey == 0)
            {
                target = Focus;
                return !Focus.IsDefault;
            }

            return entities.TryGetValue(entityKey, out target);
        }

        public string EntityAuthoringIdOf(int entityKey) => entityIds.TryGetValue(entityKey, out string? id) ? id : string.Empty;

        /// <summary>The entity key of a placed entity's target (0 when the target is not a placed entity).</summary>
        public int EntityKeyOf(TargetId target)
        {
            foreach (KeyValuePair<int, TargetId> pair in entities)
            {
                if (pair.Value.Equals(target))
                {
                    return pair.Key;
                }
            }

            return 0;
        }

        /// <summary>The narrative target record of a target id (null when it is not a narrative target).</summary>
        public NarrativeTarget? FindTarget(TargetId target)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].Target.Equals(target))
                {
                    return targets[i];
                }
            }

            return null;
        }

        public string RegionAuthoringIdOf(int regionKey) => regionIds.TryGetValue(regionKey, out string? id) ? id : string.Empty;

        private void Add(NarrativeTarget target)
        {
            long pair = Pair(target.Kind, target.Key);
            if (byKindKey.ContainsKey(pair))
            {
                return;
            }

            byKindKey[pair] = target;
            targets.Add(target);
        }

        private static long Pair(NarrativeTargetKind kind, int key) => ((long)kind << 32) | (uint)key;
    }

    /// <summary>Owned-slot reads and writes on narrative targets.</summary>
    /// <summary>Committed reads with a fallback over any ICommittedSlotReader.</summary>
    public static class CommittedSlotReads
    {
        public static int ReadOrDefault(this ICommittedSlotReader slots, TargetId target, OwnerId owner, SlotId slot, int fallback) =>
            slots != null && slots.TryRead(target, owner, slot, out int value) ? value : fallback;
    }

    public static class NarrativeSlots
    {
        public static bool TryEntity(TargetRegistry registry, EntityManager entityManager, TargetId target, out Entity entity)
        {
            entity = Entity.Null;
            return !target.IsDefault && registry.TryResolveTarget(target, out TargetHandle _, out entity) && entityManager.Exists(entity);
        }

        public static int Read(TargetRegistry registry, EntityManager entityManager, TargetId target, OwnerId owner, SlotId slot, int fallback) =>
            TryEntity(registry, entityManager, target, out Entity entity)
                ? SlotState.ReadOrDefault(entityManager, entity, owner, slot, fallback)
                : fallback;

        public static void Write(TargetRegistry registry, EntityManager entityManager, TargetId target, OwnerId owner, SlotId slot, int value)
        {
            if (TryEntity(registry, entityManager, target, out Entity entity))
            {
                SlotState.Write(entityManager, entity, owner, slot, value);
            }
        }

        /// <summary>Reads a request ring (<c>&lt;group&gt;.req.0..7</c>) of a target.</summary>
        public static int[] ReadRing(TargetRegistry registry, EntityManager entityManager, TargetId target, OwnerId owner, Func<int, SlotId> slotOf)
        {
            var ring = new int[NarrativeKeys.RequestRingSize];
            for (int i = 0; i < ring.Length; i++)
            {
                ring[i] = Read(registry, entityManager, target, owner, slotOf(i), 0);
            }

            return ring;
        }

        /// <summary>Records <paramref name="requestId"/> in a target's ring (no-op for request id 0).</summary>
        public static void PushRing(TargetRegistry registry, EntityManager entityManager, TargetId target, OwnerId owner, SlotId head, Func<int, SlotId> slotOf, int requestId)
        {
            if (!RequestRing.IsTracked(requestId))
            {
                return;
            }

            int current = Read(registry, entityManager, target, owner, head, 0);
            int[] ring = new int[NarrativeKeys.RequestRingSize];
            int slot = RequestRing.Push(ring, current, requestId, out int next);
            Write(registry, entityManager, target, owner, slotOf(slot), requestId);
            Write(registry, entityManager, target, owner, head, next);
        }
    }

    /// <summary>The committed gameplay state as the condition rules read it.</summary>
    public sealed class NarrativeState : IConditionState
    {
        private readonly ICommittedSlotReader slots;
        private readonly NarrativeIndex index;
        private readonly NarrativeModelSet models;
        private readonly UnityWorldHost host;

        public NarrativeState(ICommittedSlotReader slots, NarrativeIndex index, NarrativeModelSet models, UnityWorldHost host)
        {
            this.slots = slots;
            this.index = index;
            this.models = models;
            this.host = host;
        }

        /// <summary>Milliseconds one logical step stands for (GameplayClock; P1.7a A5).</summary>
        public int StepMilliseconds { get; set; } = GameplayClock.DefaultStepMilliseconds;

        /// <summary>
        /// Gameplay time (P1.7a, A5): step x stepMs in a command-driven world (the executing step inside a step, else the
        /// current step), the domain clock in a fixed-step world. The old DomainSeconds read stood still in a command-driven
        /// world, so cooldowns never ran out.
        /// </summary>
        public int NowMs
        {
            get
            {
                ulong step = host.CurrentStep.Value;
                WorldMessagePlane? plane = host.Messages;
                if (plane != null && plane.ExecutingStep.Value > step)
                {
                    step = plane.ExecutingStep.Value;
                }

                return GameplayClock.NowMs32(host.TemporalModel, step, StepMilliseconds, host.DomainSeconds);
            }
        }

        public int Fact(int factKey)
        {
            int initial = models.TryGetFact(factKey, out FactModel? fact) && fact != null ? fact.Initial : 0;
            return index.TryFactSlot(factKey, out SlotId slot)
                ? slots.ReadOrDefault(index.StateTarget, DialogueIds.Owner, slot, initial)
                : initial;
        }

        public int ItemCount(int inventoryKey, int itemKey, int actorKey)
        {
            if (!index.TryInventoryTarget(inventoryKey, out TargetId target))
            {
                return 0;
            }

            int capacity = SlotCountOf(inventoryKey == 0 ? index.PlayerInventoryKey : inventoryKey);
            int total = 0;
            for (int k = 0; k < capacity; k++)
            {
                if (slots.ReadOrDefault(target, InventoryIds.Owner, InventoryIds.Item(k), 0) == itemKey)
                {
                    total += slots.ReadOrDefault(target, InventoryIds.Owner, InventoryIds.Count(k), 0);
                }
            }

            return total;
        }

        public int Currency(int inventoryKey, int actorKey) =>
            index.TryInventoryTarget(inventoryKey, out TargetId target) ? slots.ReadOrDefault(target, InventoryIds.Owner, InventoryIds.Currency, 0) : 0;

        public int Quest(int questKey, QuestField field)
        {
            TargetId target = index.TargetOf(NarrativeTargetKind.Quest, questKey);
            if (target.IsDefault)
            {
                return 0;
            }

            SlotId slot = field == QuestField.Status ? QuestIds.Status : (field == QuestField.Stage ? QuestIds.Stage : QuestIds.Branch);
            return slots.ReadOrDefault(target, QuestIds.Owner, slot, 0);
        }

        public int ObjectiveDone(int questKey, int objective)
        {
            TargetId target = index.TargetOf(NarrativeTargetKind.Quest, questKey);
            return target.IsDefault ? 0 : slots.ReadOrDefault(target, QuestIds.Owner, QuestIds.ObjectiveDone(objective), 0);
        }

        public int RegionOf(int entityKey) =>
            index.TryEntityTarget(entityKey, out TargetId target) ? slots.ReadOrDefault(target, GameplaySlots.WorldOwner, GameplaySlots.Region, 0) : 0;

        public int NodeVisited(int graphKey, int node)
        {
            TargetId target = index.TargetOf(NarrativeTargetKind.Graph, graphKey);
            if (target.IsDefault || node < 0)
            {
                return 0;
            }

            int word = slots.ReadOrDefault(target, DialogueIds.Owner, DialogueIds.Visited(node / 32), 0);
            return (word >> (node % 32)) & 1;
        }

        public int Slot(int entityKey, int slotRef)
        {
            if (!NarrativeSlotRefs.TryResolve(slotRef, out OwnerId owner, out SlotId slot) || !index.TryEntityTarget(entityKey, out TargetId target))
            {
                return 0;
            }

            return slots.ReadOrDefault(target, owner, slot, 0);
        }

        public int RuleFired(int ruleKey)
        {
            TargetId target = index.TargetOf(NarrativeTargetKind.Rule, ruleKey);
            return target.IsDefault ? 0 : slots.ReadOrDefault(target, LogicIds.Owner, LogicIds.Fired, 0);
        }

        public int SlotCountOf(int inventoryKey)
        {
            if (models.TryGetInventory(inventoryKey, out InventoryModel? inventory) && inventory != null)
            {
                return inventory.SlotCount;
            }

            if (models.TryGetVendor(inventoryKey, out VendorModel? vendor) && vendor != null)
            {
                return vendor.StockSlots;
            }

            return 0;
        }
    }

    /// <summary>Commits several events for one command: extras as request-kind copies, the command's own result last.</summary>
    public sealed class StepEventBatch
    {
        private readonly List<SchemaRef> schemas = new List<SchemaRef>();
        private readonly List<FrozenPayload> payloads = new List<FrozenPayload>();

        public int Count => schemas.Count;

        public void Add(SchemaRef schema, FrozenPayload payload)
        {
            schemas.Add(schema);
            payloads.Add(payload);
        }

        public void Add(SchemaRef schema, TargetId target, int a, int b, int c, int d, int e, int f) =>
            Add(schema, NarrativeEvent.Encode(target, a, b, c, d, e, f));

        /// <summary>Commits every event in order; false when the step's event budget refused one (nothing after it is committed).</summary>
        public bool CommitAll(WorldMessagePlane plane, StepMessage message) => CommitAll(plane, message, null);

        /// <summary>
        /// <see cref="CommitAll(WorldMessagePlane, StepMessage)"/> with the world's step tap (P1.7a, A1): false before
        /// anything is committed when the outbox has no room for what the events may owe; after each committed event the
        /// tap records the deliveries it owes, in this step.
        /// </summary>
        public bool CommitAll(WorldMessagePlane plane, StepMessage message, IGameplayStepTap? tap)
        {
            if (schemas.Count == 0)
            {
                return false;
            }

            if (tap != null && !tap.HasRoom(schemas.Count))
            {
                return false;
            }

            var extra = new StepMessage(
                message.Step,
                message.Epoch,
                message.Request,
                message.Route,
                message.Owner,
                message.Target,
                message.PayloadSchema,
                MessageKind.Request,
                message.Order,
                message.Producer,
                0,
                0);
            for (int i = 0; i < schemas.Count - 1; i++)
            {
                if (!plane.Commit(extra, schemas[i], payloads[i], plane.ExecutingStep, out string _))
                {
                    return false;
                }

                tap?.OnCommitted(schemas[i], payloads[i], message.Request);
            }

            int last = schemas.Count - 1;
            if (!plane.Commit(message, schemas[last], payloads[last], plane.ExecutingStep, out string _))
            {
                return false;
            }

            tap?.OnCommitted(schemas[last], payloads[last], message.Request);
            return true;
        }
    }

    /// <summary>
    /// The destination half of the in-step outbox (P1.7a, A1). A request id is decided before the command mutates:
    /// 0 is not deduplicated; an id in the target's request ring (when it has one) was applied; an obligation id is claimed
    /// through the step tap (open -> apply, settled or unknown -> already applied); any other id is new. After the effect
    /// committed the destination pushes the id into its ring (when it has one) and settles a claimed obligation.
    /// </summary>
    public static class NarrativeObligations
    {
        /// <summary>None to apply, IdempotencyConflict when the request was already applied.</summary>
        public static DiagnosticCode Admit(IGameplayStepTap? tap, IReadOnlyList<int>? ring, int requestId)
        {
            if (!RequestRing.IsTracked(requestId))
            {
                return DiagnosticCode.None;
            }

            if (ring != null && RequestRing.Contains(ring, requestId))
            {
                return DiagnosticCode.IdempotencyConflict;
            }

            return GameplayObligations.Claim(tap, requestId) == ObligationClaim.AlreadyApplied
                ? DiagnosticCode.IdempotencyConflict
                : DiagnosticCode.None;
        }

        /// <summary>Settles an obligation id after the destination committed its effect (no-op for other ids).</summary>
        public static void Settle(IGameplayStepTap? tap, int requestId) => GameplayObligations.Settle(tap, requestId);

        /// <summary>The optional trailing request id of a command (0 when absent).</summary>
        public static int OptionalRequest(NarrativeCommand command, int index) => command.Count > index ? command[index] : 0;
    }

    /// <summary>Submits narrative commands with one issuer and a strictly increasing sequence (P-050).</summary>
    public sealed class NarrativeSubmitter
    {
        private readonly UnityWorldHost host;
        private ulong sequence;

        public NarrativeSubmitter(UnityWorldHost host, Id128 issuer)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            Issuer = issuer;
        }

        public Id128 Issuer { get; }

        public int Submitted { get; private set; }

        public int Refused { get; private set; }

        private ulong requestSerial;

        /// <summary>
        /// A fresh command request id (P1.7a, A6): positive int31 of (issuer, the logical step it is made at, a serial), so
        /// a restored world - which resumes at the captured step - never mints an id an applied request already holds,
        /// and two boots fed the same commands mint the same ids.
        /// </summary>
        public int NextRequestId()
        {
            requestSerial++;
            return GameplayRequestIds.OfCommand(Issuer, host.CurrentStep.Value, requestSerial);
        }

        public CommandAdmissionReceipt Submit(RouteId route, TargetId target, SchemaRef schema, FrozenPayload payload)
        {
            sequence++;
            var envelope = new CommandEnvelope(new OperationId(host.World, Issuer, sequence), route, target, schema, null, payload);
            CommandAdmissionReceipt receipt = host.Submit(envelope);
            if (receipt.Admitted)
            {
                Submitted++;
            }
            else
            {
                Refused++;
            }

            return receipt;
        }

        /// <summary>The ledger outcome of an earlier submission: Accepted (pending), Committed, Rejected/Cancelled, or false when no longer retained.</summary>
        public bool TryOutcome(OperationId request, out RequestResultKind kind, out DiagnosticCode reason)
        {
            kind = RequestResultKind.Accepted;
            reason = DiagnosticCode.None;
            WorldMessagePlane? plane = host.Messages;
            if (plane == null || !plane.Requests.TryGet(request, out RequestRow? row) || row == null)
            {
                return false;
            }

            kind = row.Outcome.Kind;
            reason = row.Outcome.Reason;
            return true;
        }
    }
}
