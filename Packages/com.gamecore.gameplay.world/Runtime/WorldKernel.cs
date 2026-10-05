// GameCore.Gameplay.World - the world plugin's kernel half: payloads, the region recipe, the per-world module and the
// command system (P1.1; P-032, P-042, P-044).
//
// Travel is validated by the pure travel rules over the baked region graph: the traveller must stand in a region with
// a portal to the destination. An accepted travel moves the traveller to the destination side's arrival pose, counts a
// visit on the destination region target and commits two events in order: RegionLeft (staged through a request-kind
// copy of the command, which records no second ledger row) and RegionEntered (the command's own committed result).
// world.setResidency is host-only: the system refuses it unless the request's issuer is the application root's issuer,
// so only the region streamer, which owns scene loading, can move the residency state machine.
//
// P1.7a:
//   * world.posX/Y/Z/yaw is the authoritative pose of every entity (A4); world.place is accepted only from the host
//     issuer (the application root) or the Studio issuer, and a place on the player or an NPC is adopted by their
//     kernels (they read world.pos for every decision).
//   * A portal may carry a condition reference (ManifestPortal.conditionRef, P1.7b's PortalDefinition field once it
//     lands): travel through it is evaluated through the world's IConditionEvaluator and refused with a stable GP-WLD
//     code unless the verdict is True.
//   * Every refusal records an explain entry (the stable code, the subject and why) in the module's explain ring.
//   * world.travel accepts an optional trailing request id (12-byte payload). A negative id is an outbox obligation's
//     (the narrative "world-travel" port): it is claimed through the world's step tap and applied at most once.
//   * Committed RegionEntered events are handed to the step tap, so rule triggers on them become outbox obligations in
//     the same step.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Entities;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Rules.Gameplay.World;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using Unity.Entities;

namespace GameCore.Gameplay.World
{
    /// <summary>
    /// world.travel payload: destination region key and portal key (zero = any connecting portal), optionally followed by
    /// a request id (<see cref="LengthWithRequest"/> bytes; P1.7a).
    /// </summary>
    public readonly struct TravelPayload
    {
        public const int Length = 8;

        public const int LengthWithRequest = 12;

        public TravelPayload(int destination, int portal)
            : this(destination, portal, 0)
        {
        }

        public TravelPayload(int destination, int portal, int requestId)
        {
            Destination = destination;
            Portal = portal;
            RequestId = requestId;
        }

        public int Destination { get; }

        public int Portal { get; }

        /// <summary>0 when the command carries none; negative for an outbox obligation's id.</summary>
        public int RequestId { get; }

        public static FrozenPayload Encode(int destination, int portal) =>
            new GameplayPayloadWriter().Int32(destination).Int32(portal).Freeze();

        public static FrozenPayload Encode(int destination, int portal, int requestId) =>
            new GameplayPayloadWriter().Int32(destination).Int32(portal).Int32(requestId).Freeze();
    }

    /// <summary>world.setResidency payload: the next residency value.</summary>
    public readonly struct ResidencyPayload
    {
        public const int Length = 4;

        public ResidencyPayload(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public static FrozenPayload Encode(int value) => new GameplayPayloadWriter().Int32(value).Freeze();
    }

    /// <summary>world.place payload: position (mm) and yaw (mrad).</summary>
    public readonly struct PlacePayload
    {
        public const int Length = 16;

        public PlacePayload(int x, int y, int z, int yaw)
        {
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
        }

        public int X { get; }

        public int Y { get; }

        public int Z { get; }

        public int Yaw { get; }

        public static FrozenPayload Encode(int x, int y, int z, int yaw) =>
            new GameplayPayloadWriter().Int32(x).Int32(y).Int32(z).Int32(yaw).Freeze();
    }

    public sealed class TravelReader : ICommandPayloadReader<TravelPayload>
    {
        public SchemaRef Schema => WorldDeclarations.TravelCommand;

        public TravelPayload Read(IReadOnlyList<byte> payload)
        {
            var reader = new GameplayPayloadReader(payload);
            if (reader.HasLength(TravelPayload.LengthWithRequest))
            {
                return new TravelPayload(reader.Int32(), reader.Int32(), reader.Int32());
            }

            if (!reader.HasLength(TravelPayload.Length))
            {
                throw new FormatException("a travel command is " + TravelPayload.Length + " or " + TravelPayload.LengthWithRequest + " bytes");
            }

            return new TravelPayload(reader.Int32(), reader.Int32());
        }
    }

    public sealed class ResidencyReader : ICommandPayloadReader<ResidencyPayload>
    {
        public SchemaRef Schema => WorldDeclarations.SetResidencyCommand;

        public ResidencyPayload Read(IReadOnlyList<byte> payload)
        {
            var reader = new GameplayPayloadReader(payload);
            if (!reader.HasLength(ResidencyPayload.Length))
            {
                throw new FormatException("a residency command is exactly " + ResidencyPayload.Length + " bytes");
            }

            return new ResidencyPayload(reader.Int32());
        }
    }

    public sealed class PlaceReader : ICommandPayloadReader<PlacePayload>
    {
        public SchemaRef Schema => WorldDeclarations.PlaceCommand;

        public PlacePayload Read(IReadOnlyList<byte> payload)
        {
            var reader = new GameplayPayloadReader(payload);
            if (!reader.HasLength(PlacePayload.Length))
            {
                throw new FormatException("a place command is exactly " + PlacePayload.Length + " bytes");
            }

            return new PlacePayload(reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32());
        }
    }

    /// <summary>A decoded world event: the target it is about and up to four int32 values.</summary>
    public readonly struct WorldEvent
    {
        public WorldEvent(TargetId target, int a, int b, int c, int d)
        {
            Target = target;
            A = a;
            B = b;
            C = c;
            D = d;
        }

        public TargetId Target { get; }

        /// <summary>RegionEntered/RegionLeft: from-region key. ResidencyChanged: residency. EntityPlaced: x.</summary>
        public int A { get; }

        /// <summary>RegionEntered/RegionLeft: to-region key. EntityPlaced: y.</summary>
        public int B { get; }

        /// <summary>RegionEntered/RegionLeft: portal key. EntityPlaced: z.</summary>
        public int C { get; }

        /// <summary>EntityPlaced: yaw.</summary>
        public int D { get; }

        public const int Length = 32;

        public static FrozenPayload Encode(TargetId target, int a, int b, int c, int d) =>
            new GameplayPayloadWriter().Id(target.Value).Int32(a).Int32(b).Int32(c).Int32(d).Freeze();

        public static bool TryDecode(FrozenPayload payload, out WorldEvent decoded)
        {
            decoded = default(WorldEvent);
            if (payload == null || payload.Length != Length)
            {
                return false;
            }

            var reader = new GameplayPayloadReader(payload.Bytes);
            decoded = new WorldEvent(new TargetId(reader.Id()), reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32());
            return true;
        }
    }

    public static class WorldReaders
    {
        public static void BindInto(CommandPayloadReaders readers)
        {
            if (readers == null)
            {
                throw new ArgumentNullException(nameof(readers));
            }

            Require(readers.TryBind(new TravelReader(), out string failure), failure);
            Require(readers.TryBind(new ResidencyReader(), out failure), failure);
            Require(readers.TryBind(new PlaceReader(), out failure), failure);
        }

        private static void Require(bool bound, string failure)
        {
            if (!bound)
            {
                throw new InvalidOperationException("world reader registration failed: " + failure);
            }
        }
    }

    /// <summary>Base layout of a region target: an empty owned-slot buffer.</summary>
    public sealed class RegionRecipeApplier : ISpawnApplier
    {
        public FactoryKey Key => WorldDeclarations.RegionApplier;

        public void ApplyBaseLayout(EntityManager entityManager, Entity entity, SpawnRecipe recipe)
        {
            if (!entityManager.HasBuffer<TargetSlotState>(entity))
            {
                entityManager.AddBuffer<TargetSlotState>(entity);
            }
        }
    }

    /// <summary>One region as the world module knows it.</summary>
    public sealed class RegionRecord
    {
        public RegionRecord(string authoringId, string name, int key, TargetId target, ScopeId scope, string scenePath)
        {
            AuthoringId = authoringId;
            Name = name;
            Key = key;
            Target = target;
            Scope = scope;
            ScenePath = scenePath;
        }

        public string AuthoringId { get; }

        public string Name { get; }

        public int Key { get; }

        public TargetId Target { get; }

        public ScopeId Scope { get; }

        public string ScenePath { get; }
    }

    /// <summary>One portal as the world module knows it, with both arrival poses.</summary>
    public sealed class PortalRecord
    {
        public PortalRecord(ManifestPortal portal, int keyA, int keyB)
        {
            AuthoringId = portal.authoringId;
            Key = portal.key;
            RegionA = keyA;
            RegionB = keyB;
            ArrivalA = new[] { portal.arrivalAX, portal.arrivalAY, portal.arrivalAZ, portal.arrivalAYaw };
            ArrivalB = new[] { portal.arrivalBX, portal.arrivalBY, portal.arrivalBZ, portal.arrivalBYaw };
            ConditionRef = portal.conditionRef ?? string.Empty;
        }

        public string AuthoringId { get; }

        /// <summary>The portal's travel condition (empty = always allowed; P1.7a).</summary>
        public string ConditionRef { get; }

        public int Key { get; }

        public int RegionA { get; }

        public int RegionB { get; }

        private int[] ArrivalA { get; }

        private int[] ArrivalB { get; }

        /// <summary>Arrival pose (x, y, z, yaw) of a traveller entering <paramref name="destination"/> through this portal.</summary>
        public int[] ArrivalInto(int destination) => (int[])(destination == RegionA ? ArrivalA : ArrivalB).Clone();

        public bool Connects(int region) => region == RegionA || region == RegionB;

        public int Other(int region) => region == RegionA ? RegionB : RegionA;
    }

    /// <summary>The world plugin's state of one world. Instance state only.</summary>
    public sealed class WorldModule : IGameplayStepTapHost
    {
        /// <summary>Refusal explain records the module keeps (newest first through <see cref="RecentRefusals"/>).</summary>
        public const int ExplainCapacity = 64;

        private readonly BoundedRing<ExplainRecord> explain = new BoundedRing<ExplainRecord>(ExplainCapacity);
        private readonly Dictionary<TargetId, int> entityKeys = new Dictionary<TargetId, int>();
        private readonly Dictionary<TargetId, string> entityIds = new Dictionary<TargetId, string>();
        private readonly Dictionary<int, RegionRecord> regionsByKey = new Dictionary<int, RegionRecord>();
        private readonly Dictionary<string, RegionRecord> regionsById = new Dictionary<string, RegionRecord>(StringComparer.Ordinal);
        private readonly Dictionary<int, PortalRecord> portals = new Dictionary<int, PortalRecord>();
        private readonly Dictionary<string, PortalRecord> portalsById = new Dictionary<string, PortalRecord>(StringComparer.Ordinal);
        private readonly List<RegionRecord> orderedRegions = new List<RegionRecord>();

        public WorldModule(UnityWorldHost host, TargetRegistry registry, Id128 hostIssuer, RegionManifest manifest)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            HostIssuer = hostIssuer;
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            WorldId = manifest.WorldId;
            StudioIssuer = StudioIssuerOf(manifest.WorldId);
            for (int i = 0; i < manifest.Entities.Count; i++)
            {
                TargetId target = AuthoringIds.TargetIdFor(manifest.Entities[i].authoringId);
                entityKeys[target] = AuthoringIds.StableKey(manifest.Entities[i].authoringId);
                entityIds[target] = manifest.Entities[i].authoringId;
            }

            if (manifest.Regions.Count > 0)
            {
                string anchor = manifest.Regions[0].authoringId;
                for (int i = 1; i < manifest.Regions.Count; i++)
                {
                    if (string.CompareOrdinal(manifest.Regions[i].authoringId, anchor) < 0)
                    {
                        anchor = manifest.Regions[i].authoringId;
                    }
                }

                AnchorTarget = AuthoringIds.TargetIdFor(anchor);
            }

            var keys = new List<int>();
            for (int i = 0; i < manifest.Regions.Count; i++)
            {
                ManifestRegion region = manifest.Regions[i];
                var record = new RegionRecord(
                    region.authoringId,
                    region.name,
                    region.key,
                    AuthoringIds.TargetIdFor(region.authoringId),
                    AuthoringIds.ScopeIdFor(region.authoringId),
                    region.scenePath);
                regionsByKey[record.Key] = record;
                regionsById[record.AuthoringId] = record;
                orderedRegions.Add(record);
                keys.Add(record.Key);
            }

            orderedRegions.Sort((l, r) => string.CompareOrdinal(l.AuthoringId, r.AuthoringId));
            var links = new List<PortalLink>();
            for (int i = 0; i < manifest.Portals.Count; i++)
            {
                ManifestPortal portal = manifest.Portals[i];
                if (!regionsById.TryGetValue(portal.regionA, out RegionRecord? a) || !regionsById.TryGetValue(portal.regionB, out RegionRecord? b))
                {
                    continue;
                }

                var record = new PortalRecord(portal, a.Key, b.Key);
                portals[record.Key] = record;
                portalsById[record.AuthoringId] = record;
                links.Add(new PortalLink(record.Key, a.Key, b.Key));
            }

            Graph = new RegionGraph(keys, links);
        }

        public UnityWorldHost Host { get; }

        public TargetRegistry Registry { get; }

        /// <summary>The application root's issuer: the only issuer world.setResidency accepts.</summary>
        public Id128 HostIssuer { get; }

        /// <summary>The world's authoring id.</summary>
        public string WorldId { get; }

        /// <summary>Studio's issuer of this world: world.place accepts it beside the host issuer (P1.7a).</summary>
        public Id128 StudioIssuer { get; }

        /// <summary>The world's anchor region target (first region in ordinal authoring-id order): holds world.spawnOrdinal.</summary>
        public TargetId AnchorTarget { get; }

        /// <summary>The world's in-step outbox seam (the narrative delivery sets it); null without one.</summary>
        public IGameplayStepTap? StepTap { get; set; }

        /// <summary>Evaluates portal conditions (P1.4's evaluator in a narrative world); null = no evaluator (Unknown).</summary>
        public IConditionEvaluator? Conditions { get; set; }

        /// <summary>The committed-slot reader conditions read through (set by the gameplay world on attach).</summary>
        public ICommittedSlotReader? Slots { get; set; }

        /// <summary>The stable code of the last refusal (empty before any).</summary>
        public string LastRefusalCode { get; private set; } = string.Empty;

        /// <summary>The Studio issuer of a world authoring id.</summary>
        public static Id128 StudioIssuerOf(string worldId) => GameplayIssuers.Studio(worldId);

        public RegionGraph Graph { get; }

        /// <summary>Regions in canonical (ordinal authoring id) order.</summary>
        public IReadOnlyList<RegionRecord> Regions => orderedRegions;

        public int Travels { get; private set; }

        public int Refused { get; private set; }

        public int ResidencyChanges { get; private set; }

        public int Placements { get; private set; }

        public bool TryRegion(int key, out RegionRecord? region) => regionsByKey.TryGetValue(key, out region);

        public bool TryRegion(string authoringId, out RegionRecord? region) => regionsById.TryGetValue(authoringId, out region);

        public bool TryPortal(int key, out PortalRecord? portal) => portals.TryGetValue(key, out portal);

        public bool TryPortal(string authoringId, out PortalRecord? portal) => portalsById.TryGetValue(authoringId, out portal);

        internal void CountTravel() => Travels++;

        internal void CountRefused() => Refused++;

        /// <summary>Up to <paramref name="max"/> refusal explain records, newest first.</summary>
        public IReadOnlyList<ExplainRecord> RecentRefusals(int max) => explain.Recent(max);

        /// <summary>The stable key of a placed entity's target (0 for a target the manifest does not place).</summary>
        public int EntityKeyOf(TargetId target) => entityKeys.TryGetValue(target, out int key) ? key : 0;

        /// <summary>The authoring id of a placed entity's target (empty for a runtime target).</summary>
        public string EntityIdOf(TargetId target) => entityIds.TryGetValue(target, out string? id) ? id : string.Empty;

        /// <summary>True when world.place accepts <paramref name="issuer"/> (the host or Studio).</summary>
        public bool MayPlace(Id128 issuer) => issuer.Equals(HostIssuer) || issuer.Equals(StudioIssuer);

        /// <summary>The verdict of a portal's condition for a traveller (True for a portal without one).</summary>
        public ConditionVerdict EvaluatePortal(PortalRecord portal, TargetId traveller)
        {
            if (portal == null || string.IsNullOrEmpty(portal.ConditionRef))
            {
                return ConditionVerdict.True;
            }

            IConditionEvaluator? evaluator = Conditions;
            if (evaluator == null)
            {
                return ConditionVerdict.Unknown;
            }

            return evaluator.Evaluate(portal.ConditionRef, new InteractionContext(portal.AuthoringId, portal.Key, EntityKeyOf(traveller), 0, Slots ?? NullSlots.Instance));
        }

        internal void Explain(StepMessage message, string route, string subject, string code, string detail)
        {
            LastRefusalCode = code;
            explain.Add(new ExplainRecord(subject, route, (long)message.Step.Value, false, code, -1, detail, Array.Empty<string>()));
        }

        /// <summary>A reader that holds nothing (conditions asked before the world attached its reader).</summary>
        private sealed class NullSlots : ICommittedSlotReader
        {
            public static readonly NullSlots Instance = new NullSlots();

            public bool TryRead(TargetId target, OwnerId owner, SlotId slot, out int value)
            {
                value = 0;
                return false;
            }
        }

        internal void CountResidency() => ResidencyChanges++;

        internal void CountPlacement() => Placements++;

        public static void WritePose(EntityManager entityManager, Entity entity, int x, int y, int z, int yaw)
        {
            SlotState.Write(entityManager, entity, GameplaySlots.WorldOwner, GameplaySlots.PosX, x);
            SlotState.Write(entityManager, entity, GameplaySlots.WorldOwner, GameplaySlots.PosY, y);
            SlotState.Write(entityManager, entity, GameplaySlots.WorldOwner, GameplaySlots.PosZ, z);
            SlotState.Write(entityManager, entity, GameplaySlots.WorldOwner, GameplaySlots.Yaw, yaw);
        }
    }

    /// <summary>The world command stage: travel, residency and placement.</summary>
    [DisableAutoCreation]
    public partial class WorldCommandSystem : SystemBase
    {
        /// <summary>This world's module; set by the application root after boot. Until then the stage is idle.</summary>
        public WorldModule? Module { get; set; }

        protected override void OnUpdate()
        {
            WorldModule? module = Module;
            if (module == null)
            {
                return;
            }

            WorldMessagePlane? plane = module.Host.Messages;
            if (plane == null)
            {
                return;
            }

            IReadOnlyList<StepMessage> batch = plane.DrainOwnerBatch(WorldDeclarations.Owner);
            EntityManager entityManager = EntityManager;
            for (int i = 0; i < batch.Count; i++)
            {
                StepMessage message = batch[i];
                if (message.Route.Equals(WorldDeclarations.TravelRoute))
                {
                    Travel(module, plane, entityManager, message);
                }
                else if (message.Route.Equals(WorldDeclarations.SetResidencyRoute))
                {
                    SetResidency(module, plane, entityManager, message);
                }
                else if (message.Route.Equals(WorldDeclarations.PlaceRoute))
                {
                    Place(module, plane, entityManager, message);
                }
                else
                {
                    Refuse(module, plane, message, DiagnosticCode.Ineligible);
                }
            }

            plane.ReleaseConsumed(WorldDeclarations.Owner);
        }

        private static void Travel(WorldModule module, WorldMessagePlane plane, EntityManager entityManager, StepMessage message)
        {
            byte[] payload = plane.PayloadOf(message);
            if (plane.Readers.TryRead<TravelPayload>(message.PayloadSchema, payload, out TravelPayload travel, out string _)
                != PayloadDecodeOutcome.Decoded)
            {
                Refuse(module, plane, message, DiagnosticCode.UnsupportedVersion, "world.travel", WorldRefusalCodes.MalformedCommand, "malformed travel payload");
                return;
            }

            string subject = module.EntityIdOf(message.Target);
            if (GameplayObligations.Claim(module.StepTap, travel.RequestId) == ObligationClaim.AlreadyApplied)
            {
                Refuse(module, plane, message, DiagnosticCode.IdempotencyConflict, "world.travel", WorldRefusalCodes.TravelAlreadyApplied,
                    "the travel obligation was already applied");
                return;
            }

            if (!module.Registry.TryResolveTarget(message.Target, out TargetHandle _, out Entity traveller)
                || !SlotState.TryRead(entityManager, traveller, GameplaySlots.WorldOwner, GameplaySlots.Region, out int from))
            {
                Refuse(module, plane, message, DiagnosticCode.StaleHandle, "world.travel", WorldRefusalCodes.TravelStaleTraveller,
                    "the traveller is not a live target with a region");
                return;
            }

            TravelRefusal validation = TravelRules.Validate(module.Graph, from, travel.Destination, travel.Portal);
            if (validation != TravelRefusal.None)
            {
                Refuse(module, plane, message, DiagnosticCode.Ineligible, "world.travel", WorldRefusalCodes.OfTravel(validation),
                    "travel from region " + from + " to " + travel.Destination + " refused: " + validation);
                return;
            }

            int portalKey = travel.Portal;
            if (portalKey == 0 && module.Graph.TryFindPortal(from, travel.Destination, out PortalLink link))
            {
                portalKey = link.Portal;
            }

            if (!module.TryPortal(portalKey, out PortalRecord? portal) || portal == null
                || !module.TryRegion(travel.Destination, out RegionRecord? destination) || destination == null
                || !module.Registry.TryResolveTarget(destination.Target, out TargetHandle _, out Entity regionEntity))
            {
                Refuse(module, plane, message, DiagnosticCode.MissingDependency, "world.travel", WorldRefusalCodes.TravelMissingPortal,
                    "portal " + portalKey + " or region " + travel.Destination + " is missing");
                return;
            }

            ConditionVerdict verdict = module.EvaluatePortal(portal, message.Target);
            if (verdict != ConditionVerdict.True)
            {
                bool unknown = verdict == ConditionVerdict.Unknown;
                Refuse(module, plane, message, DiagnosticCode.Ineligible, "world.travel",
                    unknown ? WorldRefusalCodes.TravelConditionUnknown : WorldRefusalCodes.TravelConditionFailed,
                    "portal " + portal.AuthoringId + " condition '" + portal.ConditionRef + "' is " + (unknown ? "unknown" : "false"));
                return;
            }

            var left = new StepMessage(
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
            FrozenPayload leftEvent = WorldEvent.Encode(message.Target, from, travel.Destination, portalKey, 0);
            if ((module.StepTap is IGameplayDeliveryBudget budget && !budget.HasRoomFor(
                    new[] { WorldDeclarations.RegionLeftEvent, WorldDeclarations.RegionEnteredEvent }, new[] { leftEvent, leftEvent }))
                || !plane.Commit(left, WorldDeclarations.RegionLeftEvent, leftEvent, plane.ExecutingStep, out string _))
            {
                Refuse(module, plane, message, DiagnosticCode.BudgetExceeded, "world.travel", WorldRefusalCodes.MalformedCommand, "the step's event budget is spent");
                return;
            }

            module.StepTap?.OnCommitted(WorldDeclarations.RegionLeftEvent, leftEvent, message.Request);
            FrozenPayload enteredEvent = WorldEvent.Encode(message.Target, from, travel.Destination, portalKey, 0);
            if (!plane.Commit(message, WorldDeclarations.RegionEnteredEvent, enteredEvent, plane.ExecutingStep, out string _))
            {
                Refuse(module, plane, message, DiagnosticCode.BudgetExceeded, "world.travel", WorldRefusalCodes.MalformedCommand, "the step's event budget is spent");
                return;
            }

            module.StepTap?.OnCommitted(WorldDeclarations.RegionEnteredEvent, enteredEvent, message.Request);
            GameplayObligations.Settle(module.StepTap, travel.RequestId);

            int[] arrival = portal.ArrivalInto(travel.Destination);
            SlotState.Write(entityManager, traveller, GameplaySlots.WorldOwner, GameplaySlots.Region, travel.Destination);
            WorldModule.WritePose(entityManager, traveller, arrival[0], arrival[1], arrival[2], arrival[3]);
            int visits = SlotState.ReadOrDefault(entityManager, regionEntity, GameplaySlots.WorldOwner, GameplaySlots.Visits, 0);
            SlotState.Write(entityManager, regionEntity, GameplaySlots.WorldOwner, GameplaySlots.Visits, visits + 1);
            module.CountTravel();
        }

        private static void SetResidency(WorldModule module, WorldMessagePlane plane, EntityManager entityManager, StepMessage message)
        {
            if (!message.Request.IssuerId.Equals(module.HostIssuer))
            {
                Refuse(module, plane, message, DiagnosticCode.Ineligible, "world.setResidency", GameplayDiagnosticCodes.ResidencyNotHost,
                    "world.setResidency is accepted from the host issuer only");
                return;
            }

            byte[] payload = plane.PayloadOf(message);
            if (plane.Readers.TryRead<ResidencyPayload>(message.PayloadSchema, payload, out ResidencyPayload residency, out string _)
                != PayloadDecodeOutcome.Decoded)
            {
                Refuse(module, plane, message, DiagnosticCode.UnsupportedVersion);
                return;
            }

            if (!module.Registry.TryResolveTarget(message.Target, out TargetHandle _, out Entity region))
            {
                Refuse(module, plane, message, DiagnosticCode.StaleHandle);
                return;
            }

            int current = SlotState.ReadOrDefault(entityManager, region, GameplaySlots.WorldOwner, GameplaySlots.Residency, Residency.Unloaded);
            if (ResidencyRules.Check(current, residency.Value) != ResidencyRefusal.None)
            {
                Refuse(module, plane, message, DiagnosticCode.Ineligible, "world.setResidency", GameplayDiagnosticCodes.ResidencyIllegalTransition,
                    "residency " + Residency.Name(current) + " -> " + Residency.Name(residency.Value) + " is not a legal transition");
                return;
            }

            if (!plane.Commit(message, WorldDeclarations.ResidencyChangedEvent,
                    WorldEvent.Encode(message.Target, residency.Value, current, 0, 0), plane.ExecutingStep, out string _))
            {
                Refuse(module, plane, message, DiagnosticCode.BudgetExceeded);
                return;
            }

            SlotState.Write(entityManager, region, GameplaySlots.WorldOwner, GameplaySlots.Residency, residency.Value);
            module.CountResidency();
        }

        private static void Place(WorldModule module, WorldMessagePlane plane, EntityManager entityManager, StepMessage message)
        {
            if (!module.MayPlace(message.Request.IssuerId))
            {
                Refuse(module, plane, message, DiagnosticCode.Ineligible, "world.place", WorldRefusalCodes.PlaceNotAllowed,
                    "world.place is accepted from the host or Studio issuer only");
                return;
            }

            byte[] payload = plane.PayloadOf(message);
            if (plane.Readers.TryRead<PlacePayload>(message.PayloadSchema, payload, out PlacePayload place, out string _)
                != PayloadDecodeOutcome.Decoded)
            {
                Refuse(module, plane, message, DiagnosticCode.UnsupportedVersion);
                return;
            }

            if (!module.Registry.TryResolveTarget(message.Target, out TargetHandle _, out Entity entity)
                || !SlotState.TryRead(entityManager, entity, GameplaySlots.WorldOwner, GameplaySlots.Region, out int _))
            {
                Refuse(module, plane, message, DiagnosticCode.StaleHandle, "world.place", WorldRefusalCodes.PlaceStaleTarget,
                    "the target is not a live target with a region");
                return;
            }

            if (!plane.Commit(message, WorldDeclarations.EntityPlacedEvent,
                    WorldEvent.Encode(message.Target, place.X, place.Y, place.Z, place.Yaw), plane.ExecutingStep, out string _))
            {
                Refuse(module, plane, message, DiagnosticCode.BudgetExceeded);
                return;
            }

            WorldModule.WritePose(entityManager, entity, place.X, place.Y, place.Z, place.Yaw);
            module.CountPlacement();
        }

        private static void Refuse(WorldModule module, WorldMessagePlane plane, StepMessage message, DiagnosticCode code)
        {
            Refuse(module, plane, message, code, "world", code == DiagnosticCode.StaleHandle ? WorldRefusalCodes.PlaceStaleTarget : WorldRefusalCodes.MalformedCommand,
                "refused: " + code);
        }

        private static void Refuse(WorldModule module, WorldMessagePlane plane, StepMessage message, DiagnosticCode code, string route, string stable, string detail)
        {
            module.CountRefused();
            module.Explain(message, route, module.EntityIdOf(message.Target), stable, detail);
            plane.Reject(message, code, plane.ExecutingStep);
        }
    }
}
