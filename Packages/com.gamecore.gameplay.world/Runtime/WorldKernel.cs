// GameCore.Gameplay.World - the world plugin's kernel half: payloads, the region recipe, the per-world module and the
// command system (P1.1; P-032, P-042, P-044).
//
// Travel is validated by the pure travel rules over the baked region graph: the traveller must stand in a region with
// a portal to the destination. An accepted travel moves the traveller to the destination side's arrival pose, counts a
// visit on the destination region target and commits two events in order: RegionLeft (staged through a request-kind
// copy of the command, which records no second ledger row) and RegionEntered (the command's own committed result).
// world.setResidency is host-only: the system refuses it unless the request's issuer is the application root's issuer,
// so only the region streamer, which owns scene loading, can move the residency state machine.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Rules.Gameplay.World;
using GameCore.Unity.Runtime;
using Unity.Entities;

namespace GameCore.Gameplay.World
{
    /// <summary>world.travel payload: destination region key and portal key (zero = any connecting portal).</summary>
    public readonly struct TravelPayload
    {
        public const int Length = 8;

        public TravelPayload(int destination, int portal)
        {
            Destination = destination;
            Portal = portal;
        }

        public int Destination { get; }

        public int Portal { get; }

        public static FrozenPayload Encode(int destination, int portal) =>
            new GameplayPayloadWriter().Int32(destination).Int32(portal).Freeze();
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
            if (!reader.HasLength(TravelPayload.Length))
            {
                throw new FormatException("a travel command is exactly " + TravelPayload.Length + " bytes");
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
        }

        public string AuthoringId { get; }

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
    public sealed class WorldModule
    {
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
                Refuse(module, plane, message, DiagnosticCode.UnsupportedVersion);
                return;
            }

            if (!module.Registry.TryResolveTarget(message.Target, out TargetHandle _, out Entity traveller)
                || !SlotState.TryRead(entityManager, traveller, GameplaySlots.WorldOwner, GameplaySlots.Region, out int from))
            {
                Refuse(module, plane, message, DiagnosticCode.StaleHandle);
                return;
            }

            if (TravelRules.Validate(module.Graph, from, travel.Destination, travel.Portal) != TravelRefusal.None)
            {
                Refuse(module, plane, message, DiagnosticCode.Ineligible);
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
                Refuse(module, plane, message, DiagnosticCode.MissingDependency);
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
            if (!plane.Commit(left, WorldDeclarations.RegionLeftEvent,
                    WorldEvent.Encode(message.Target, from, travel.Destination, portalKey, 0), plane.ExecutingStep, out string _))
            {
                Refuse(module, plane, message, DiagnosticCode.BudgetExceeded);
                return;
            }

            if (!plane.Commit(message, WorldDeclarations.RegionEnteredEvent,
                    WorldEvent.Encode(message.Target, from, travel.Destination, portalKey, 0), plane.ExecutingStep, out string _))
            {
                Refuse(module, plane, message, DiagnosticCode.BudgetExceeded);
                return;
            }

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
                Refuse(module, plane, message, DiagnosticCode.Ineligible);
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
                Refuse(module, plane, message, DiagnosticCode.Ineligible);
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
                Refuse(module, plane, message, DiagnosticCode.StaleHandle);
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
            module.CountRefused();
            plane.Reject(message, code, plane.ExecutingStep);
        }
    }
}
