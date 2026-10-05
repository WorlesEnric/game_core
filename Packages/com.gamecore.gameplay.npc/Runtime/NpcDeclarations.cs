// GameCore.Gameplay.Npc - the npc plugin's generated-style declarations (P1.3; P-034, P-039, P-042, P-043).
//
// One plugin type, one owner (gameplay.npc.owner) and one authoritative domain (gameplay.npc.domain.npc, v1) holding
// eleven int32 slots on every NPC's entity target. One stage runs one managed system that drains five command routes
// (setBehaviour, goTo, face, setMood, converse), each with its own bounded ingress lane, and steps every NPC.
#nullable enable
using System.Collections.Generic;
using System.Text;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Unity.Runtime;

namespace GameCore.Gameplay.Npc
{
    /// <summary>Identities and declarations of the npc plugin.</summary>
    public static class NpcDeclarations
    {
        public const string PackageVersion = "1.0.0";

        public const int LaneCapacity = 32;

        public const int LaneByteCapacity = 512;

        public const string CatalogPackage = "package.npc";
        public const string CatalogPlugin = "npc.plugin";
        public const string CatalogCommandSystem = "npc.system.command";
        public const string CatalogLayout = "npc.layout.npc";
        public const string CatalogConfigSchema = "npc.schema.config";
        public const string CatalogDomainSchema = "npc.domain.npc";

        public static readonly Id128 OwnerPackage = GameplayIds.Id(CatalogPackage);

        public static readonly PluginTypeId PluginType = GameplayIds.PluginType("npc.plugin-type");

        public static readonly PluginInstanceId Instance = GameplayIds.Instance("npc.instance");

        public static readonly FactoryKey PluginFactory = GameplayIds.Key(CatalogPlugin);

        public static readonly SchemaRef ConfigSchema = GameplayIds.Schema(CatalogConfigSchema, 1U);

        public static readonly SchemaRef Domain = GameplayIds.Schema(CatalogDomainSchema, 1U);

        public static readonly FactoryKey Layout = GameplayIds.Key(CatalogLayout);

        public static readonly OwnerId Owner = NpcSlots.Owner;

        public static readonly StageId Stage = GameplayIds.Stage("npc.stage.command");

        public static readonly FactoryKey CommandSystem = GameplayIds.Key(CatalogCommandSystem);

        public static readonly FactoryKey IngressProducer = GameplayIds.Key("npc.ingress");

        /// <summary>Producer key of the system's own (internal) event messages.</summary>
        public static readonly FactoryKey InternalProducer = GameplayIds.Key("npc.internal");

        public static readonly RouteId StepRoute = GameplayIds.Route("npc.route.step");

        private static readonly IReadOnlyList<(RouteId Route, SchemaRef Schema, string Name)> Commands = System.Array.AsReadOnly(new (RouteId Route, SchemaRef Schema, string Name)[]
        {
            (NpcSlots.SetBehaviourRoute, NpcSlots.SetBehaviourCommand, "set-behaviour"),
            (NpcSlots.GoToRoute, NpcSlots.GoToCommand, "go-to"),
            (NpcSlots.FaceRoute, NpcSlots.FaceCommand, "face"),
            (NpcSlots.SetMoodRoute, NpcSlots.SetMoodCommand, "set-mood"),
            (NpcSlots.ConverseRoute, NpcSlots.ConverseCommand, "converse"),
        });

        public static IReadOnlyList<GameplayCatalogNames.SchemaName> CatalogSchemas { get; } = System.Array.AsReadOnly(new[]
        {
            new GameplayCatalogNames.SchemaName(CatalogConfigSchema, "npc.serializer.config", "NpcConfig", CatalogPackage),
            new GameplayCatalogNames.SchemaName(CatalogDomainSchema, "npc.serializer.domain-npc", "NpcDomain", CatalogPackage),
        });

        public static IReadOnlyList<GameplayCatalogNames.EntryName> CatalogEntries { get; } = System.Array.AsReadOnly(new[]
        {
            new GameplayCatalogNames.EntryName("PluginFactory", CatalogPlugin, "NpcPluginKey", CatalogPackage),
            new GameplayCatalogNames.EntryName("SystemFactory", CatalogCommandSystem, "NpcCommandSystemKey", CatalogPackage),
            new GameplayCatalogNames.EntryName("LayoutApply", CatalogLayout, "NpcLayoutKey", CatalogPackage),
        });

        public static ContentHash PackageContentHash() =>
            ContentHash.Compute(Encoding.UTF8.GetBytes(
                "gameplay.npc/" + PackageVersion + ";" + GameplayIds.Hex(PluginType.Value) + ";"
                + GameplayIds.Hex(Domain.Id.Value) + ";" + GameplayIds.Hex(Stage.Value)));

        public static PluginManifest Manifest()
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

        public static IReadOnlyList<StateSlotSpec> Slots()
        {
            return new List<StateSlotSpec>
            {
                Slot(NpcSlots.State, "npc.field.state"),
                Slot(NpcSlots.Behaviour, "npc.field.behaviour"),
                Slot(NpcSlots.PatrolIndex, "npc.field.patrol-index"),
                Slot(NpcSlots.Mood, "npc.field.mood"),
                Slot(NpcSlots.SchedulePhase, "npc.field.schedule-phase"),
                Slot(NpcSlots.TargetX, "npc.field.target-x"),
                Slot(NpcSlots.TargetZ, "npc.field.target-z"),
                Slot(NpcSlots.PosX, "npc.field.pos-x"),
                Slot(NpcSlots.PosZ, "npc.field.pos-z"),
                Slot(NpcSlots.Yaw, "npc.field.yaw"),
                Slot(NpcSlots.TimerMs, "npc.field.timer-ms"),
            };
        }

        public static IReadOnlyList<StageSpec> Stages()
        {
            var access = new AccessSet(new[] { new AccessDeclaration(Domain, AccessMode.ReadWrite, default(Id128)) });
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
                    null,
                    new List<SystemSpec> { new SystemSpec(CommandSystem, SystemMultiplicity.World, access, null, null, null, null) },
                    null),
            };
        }

        public static IReadOnlyList<BufferSpec> Buffers()
        {
            var buffers = new List<BufferSpec>();
            for (int i = 0; i < Commands.Count; i++)
            {
                buffers.Add(new BufferSpec(
                    BufferOf(i),
                    Commands[i].Schema,
                    new List<FactoryKey> { IngressProducer },
                    Stage,
                    Stage,
                    OrderOf(i),
                    BufferLifetime.Step,
                    LaneCapacity,
                    BufferOverflowPolicy.RejectBeforeMutation,
                    BufferCancellationPolicy.Drain));
            }

            return buffers;
        }

        public static IReadOnlyList<CommandRoute> Routes()
        {
            var routes = new List<CommandRoute>();
            for (int i = 0; i < Commands.Count; i++)
            {
                routes.Add(new CommandRoute(Commands[i].Route, Owner, Commands[i].Schema, Stage, Stage, BufferOf(i), IngressProducer, LaneCapacity, false));
            }

            return routes;
        }

        public static IReadOnlyList<MessageBufferDescriptor> Lanes()
        {
            var lanes = new List<MessageBufferDescriptor>();
            for (int i = 0; i < Commands.Count; i++)
            {
                lanes.Add(new MessageBufferDescriptor(
                    BufferOf(i),
                    Commands[i].Schema,
                    new[] { IngressProducer },
                    Owner,
                    Stage,
                    Stage,
                    OrderOf(i),
                    BufferLifetime.Step,
                    LaneCapacity,
                    LaneByteCapacity,
                    BufferOverflowPolicy.RejectBeforeMutation,
                    BufferCancellationPolicy.Drain));
            }

            return lanes;
        }

        public static SystemRegistration CommandSystemRegistration() =>
            new ManagedSystemRegistration<NpcCommandSystem>(CommandSystem, Stage, "GameplayNpcCommandSystem");

        private static BufferId BufferOf(int index) => GameplayIds.Buffer("npc.buffer." + Commands[index].Name);

        private static FactoryKey OrderOf(int index) => GameplayIds.Key("npc.order." + Commands[index].Name);

        private static StateSlotSpec Slot(SlotId slot, string field)
        {
            return new StateSlotSpec(
                slot,
                Owner,
                Domain,
                Layout,
                new List<FieldOwnership> { new FieldOwnership(Domain, GameplayIds.Id(field)) },
                default(FactoryKey),
                default(FactoryKey),
                default(FactoryKey),
                LastSupportPolicy.PreserveDormant,
                default(FactoryKey),
                null);
        }
    }
}
