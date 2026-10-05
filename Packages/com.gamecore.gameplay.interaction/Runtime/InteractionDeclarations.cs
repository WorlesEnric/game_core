// GameCore.Gameplay.Interaction - the interaction plugin's generated-style declarations (P1.3; P-034, P-039, P-042, P-043).
//
// One plugin type, one owner (gameplay.interaction.owner) and one authoritative domain
// (gameplay.interaction.domain.interactable, v1) holding four int32 slots on every interactable or trigger entity target.
// One stage runs one managed system that drains three command routes (use, setState, trigger), each with its own
// bounded ingress lane, and counts cooldowns down.
#nullable enable
using System.Collections.Generic;
using System.Text;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Unity.Runtime;

namespace GameCore.Gameplay.Interaction
{
    /// <summary>Identities and declarations of the interaction plugin.</summary>
    public static class InteractionDeclarations
    {
        public const string PackageVersion = "1.0.0";

        public const int LaneCapacity = 32;

        public const int LaneByteCapacity = 1024;

        public const string CatalogPackage = "package.interaction";
        public const string CatalogPlugin = "interaction.plugin";
        public const string CatalogCommandSystem = "interaction.system.command";
        public const string CatalogLayout = "interaction.layout.interactable";
        public const string CatalogConfigSchema = "interaction.schema.config";
        public const string CatalogDomainSchema = "interaction.domain.interactable";

        public static readonly Id128 OwnerPackage = GameplayIds.Id(CatalogPackage);

        public static readonly PluginTypeId PluginType = GameplayIds.PluginType("interaction.plugin-type");

        public static readonly PluginInstanceId Instance = GameplayIds.Instance("interaction.instance");

        public static readonly FactoryKey PluginFactory = GameplayIds.Key(CatalogPlugin);

        public static readonly SchemaRef ConfigSchema = GameplayIds.Schema(CatalogConfigSchema, 1U);

        public static readonly SchemaRef Domain = GameplayIds.Schema(CatalogDomainSchema, 1U);

        public static readonly FactoryKey Layout = GameplayIds.Key(CatalogLayout);

        public static readonly OwnerId Owner = InteractionSlots.Owner;

        public static readonly StageId Stage = GameplayIds.Stage("interaction.stage.command");

        public static readonly FactoryKey CommandSystem = GameplayIds.Key(CatalogCommandSystem);

        public static readonly FactoryKey IngressProducer = GameplayIds.Key("interaction.ingress");

        /// <summary>Producer key of the system's own (internal) event messages.</summary>
        public static readonly FactoryKey InternalProducer = GameplayIds.Key("interaction.internal");

        public static readonly RouteId StepRoute = GameplayIds.Route("interaction.route.step");

        private static readonly (RouteId Route, SchemaRef Schema, string Name)[] Commands =
        {
            (InteractionSlots.UseRoute, InteractionSlots.UseCommand, "use"),
            (InteractionSlots.SetStateRoute, InteractionSlots.SetStateCommand, "set-state"),
            (InteractionSlots.TriggerRoute, InteractionSlots.TriggerCommand, "trigger"),
        };

        public static IReadOnlyList<GameplayCatalogNames.SchemaName> CatalogSchemas { get; } = System.Array.AsReadOnly(new[]
        {
            new GameplayCatalogNames.SchemaName(CatalogConfigSchema, "interaction.serializer.config", "InteractionConfig", CatalogPackage),
            new GameplayCatalogNames.SchemaName(CatalogDomainSchema, "interaction.serializer.domain-interactable", "InteractionDomain", CatalogPackage),
        });

        public static IReadOnlyList<GameplayCatalogNames.EntryName> CatalogEntries { get; } = System.Array.AsReadOnly(new[]
        {
            new GameplayCatalogNames.EntryName("PluginFactory", CatalogPlugin, "InteractionPluginKey", CatalogPackage),
            new GameplayCatalogNames.EntryName("SystemFactory", CatalogCommandSystem, "InteractionCommandSystemKey", CatalogPackage),
            new GameplayCatalogNames.EntryName("LayoutApply", CatalogLayout, "InteractionLayoutKey", CatalogPackage),
        });

        public static ContentHash PackageContentHash() =>
            ContentHash.Compute(Encoding.UTF8.GetBytes(
                "gameplay.interaction/" + PackageVersion + ";" + GameplayIds.Hex(PluginType.Value) + ";"
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
                Slot(InteractionSlots.State, "interaction.field.state"),
                Slot(InteractionSlots.Uses, "interaction.field.uses"),
                Slot(InteractionSlots.CooldownMs, "interaction.field.cooldown-ms"),
                Slot(InteractionSlots.Occupants, "interaction.field.occupants"),
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
            for (int i = 0; i < Commands.Length; i++)
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
            for (int i = 0; i < Commands.Length; i++)
            {
                routes.Add(new CommandRoute(Commands[i].Route, Owner, Commands[i].Schema, Stage, Stage, BufferOf(i), IngressProducer, LaneCapacity, false));
            }

            return routes;
        }

        public static IReadOnlyList<MessageBufferDescriptor> Lanes()
        {
            var lanes = new List<MessageBufferDescriptor>();
            for (int i = 0; i < Commands.Length; i++)
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
            new ManagedSystemRegistration<InteractionCommandSystem>(CommandSystem, Stage, "GameplayInteractionCommandSystem");

        private static BufferId BufferOf(int index) => GameplayIds.Buffer("interaction.buffer." + Commands[index].Name);

        private static FactoryKey OrderOf(int index) => GameplayIds.Key("interaction.order." + Commands[index].Name);

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
