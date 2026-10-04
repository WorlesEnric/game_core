// GameCore.Gameplay.Entities - the entities plugin's generated-style declarations (P1.1; P-034, P-039, P-042, P-043).
//
// One plugin type, one owner (gameplay.entities.owner) and one authoritative domain (gameplay.entities.domain.entity,
// v1) holding four int32 slots per entity target. One stage (gameplay.entities.stage.command) runs one managed system
// that drains the three command routes: entity.spawn, entity.despawn and entity.setVariant, each with its own bounded
// ingress lane. The manifest carries no capability contracts or rules: an entity's state is owned state, not derived
// state, so mounting the plugin publishes no binding rows.
#nullable enable
using System.Collections.Generic;
using System.Text;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Unity.Runtime;

namespace GameCore.Gameplay.Entities
{
    /// <summary>Identities and declarations of the entities plugin.</summary>
    public static class EntityDeclarations
    {
        public const string PackageVersion = "1.0.0";

        /// <summary>Rows one command lane holds per step (P-043).</summary>
        public const int LaneCapacity = 16;

        /// <summary>Bytes one command lane holds per step: the largest entity payload is 4 bytes.</summary>
        public const int LaneByteCapacity = 256;

        public static readonly Id128 OwnerPackage = GameplayIds.Id(GameplayCatalogNames.EntitiesPackage);

        public static readonly PluginTypeId PluginType = GameplayIds.PluginType("entities.plugin-type");

        public static readonly PluginInstanceId Instance = GameplayIds.Instance("entities.instance");

        public static readonly FactoryKey PluginFactory = GameplayIds.Key(GameplayCatalogNames.EntitiesPlugin);

        public static readonly SchemaRef ConfigSchema = GameplayIds.Schema(GameplayCatalogNames.EntitiesConfigSchema, 1U);

        public static readonly SchemaRef Domain = GameplayIds.Schema(GameplayCatalogNames.EntityDomainSchema, 1U);

        public static readonly FactoryKey Layout = GameplayIds.Key(GameplayCatalogNames.EntityLayout);

        public static readonly OwnerId Owner = GameplaySlots.EntityOwner;

        public static readonly StageId Stage = GameplayIds.Stage("entities.stage.command");

        public static readonly FactoryKey CommandSystem = GameplayIds.Key(GameplayCatalogNames.EntitiesCommandSystem);

        public static readonly FactoryKey Applier = GameplayIds.Key(GameplayCatalogNames.EntityApplier);

        /// <summary>The recipe schema every entity definition's recipe is written in.</summary>
        public static readonly SchemaRef RecipeSchema = GameplayIds.Schema("entities.schema.recipe", 1U);

        /// <summary>Producer key of the host's ingress (the command port), shared by the three lanes.</summary>
        public static readonly FactoryKey IngressProducer = GameplayIds.Key("entities.ingress");

        public static readonly RouteId SpawnRoute = GameplayIds.Route("entities.route.spawn");
        public static readonly RouteId DespawnRoute = GameplayIds.Route("entities.route.despawn");
        public static readonly RouteId SetVariantRoute = GameplayIds.Route("entities.route.set-variant");

        public static readonly SchemaRef SpawnCommand = GameplayIds.Schema("entities.command.spawn", 1U);
        public static readonly SchemaRef DespawnCommand = GameplayIds.Schema("entities.command.despawn", 1U);
        public static readonly SchemaRef SetVariantCommand = GameplayIds.Schema("entities.command.set-variant", 1U);

        public static readonly SchemaRef SpawnedEvent = GameplayIds.Schema("entities.event.spawned", 1U);
        public static readonly SchemaRef DespawnedEvent = GameplayIds.Schema("entities.event.despawned", 1U);
        public static readonly SchemaRef VariantChangedEvent = GameplayIds.Schema("entities.event.variant-changed", 1U);

        public static readonly BufferId SpawnBuffer = GameplayIds.Buffer("entities.buffer.spawn");
        public static readonly BufferId DespawnBuffer = GameplayIds.Buffer("entities.buffer.despawn");
        public static readonly BufferId SetVariantBuffer = GameplayIds.Buffer("entities.buffer.set-variant");

        public static readonly FactoryKey SpawnOrder = GameplayIds.Key("entities.order.spawn");
        public static readonly FactoryKey DespawnOrder = GameplayIds.Key("entities.order.despawn");
        public static readonly FactoryKey SetVariantOrder = GameplayIds.Key("entities.order.set-variant");

        /// <summary>Non-empty package content hash: SHA-256 of the canonical declaration names (P-009).</summary>
        public static ContentHash PackageContentHash() =>
            ContentHash.Compute(Encoding.UTF8.GetBytes(
                "gameplay.entities/" + PackageVersion + ";" + GameplayIds.Hex(PluginType.Value) + ";"
                + GameplayIds.Hex(Domain.Id.Value) + ";" + GameplayIds.Hex(Stage.Value)));

        /// <summary>The entities plugin manifest.</summary>
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
                Slot(GameplaySlots.Alive, "entities.field.alive"),
                Slot(GameplaySlots.Variant, "entities.field.variant"),
                Slot(GameplaySlots.ScaleMilli, "entities.field.scale-milli"),
                Slot(GameplaySlots.Visible, "entities.field.visible"),
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
            return new List<BufferSpec>
            {
                Buffer(SpawnBuffer, SpawnCommand, SpawnOrder),
                Buffer(DespawnBuffer, DespawnCommand, DespawnOrder),
                Buffer(SetVariantBuffer, SetVariantCommand, SetVariantOrder),
            };
        }

        /// <summary>The three command routes (owner = the entities owner, consumed in the command stage).</summary>
        public static IReadOnlyList<CommandRoute> Routes()
        {
            return new List<CommandRoute>
            {
                new CommandRoute(SpawnRoute, Owner, SpawnCommand, Stage, Stage, SpawnBuffer, IngressProducer, LaneCapacity, false),
                new CommandRoute(DespawnRoute, Owner, DespawnCommand, Stage, Stage, DespawnBuffer, IngressProducer, LaneCapacity, false),
                new CommandRoute(SetVariantRoute, Owner, SetVariantCommand, Stage, Stage, SetVariantBuffer, IngressProducer, LaneCapacity, false),
            };
        }

        /// <summary>The bounded ingress lanes of the three routes.</summary>
        public static IReadOnlyList<MessageBufferDescriptor> Lanes()
        {
            return new List<MessageBufferDescriptor>
            {
                Lane(SpawnBuffer, SpawnCommand, SpawnOrder),
                Lane(DespawnBuffer, DespawnCommand, DespawnOrder),
                Lane(SetVariantBuffer, SetVariantCommand, SetVariantOrder),
            };
        }

        /// <summary>The managed system registration of the command stage.</summary>
        public static SystemRegistration CommandSystemRegistration() =>
            new ManagedSystemRegistration<EntityCommandSystem>(CommandSystem, Stage, "GameplayEntityCommandSystem");

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

        private static BufferSpec Buffer(BufferId buffer, SchemaRef schema, FactoryKey order)
        {
            return new BufferSpec(
                buffer,
                schema,
                new List<FactoryKey> { IngressProducer },
                Stage,
                Stage,
                order,
                BufferLifetime.Step,
                LaneCapacity,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);
        }

        private static MessageBufferDescriptor Lane(BufferId buffer, SchemaRef schema, FactoryKey order)
        {
            return new MessageBufferDescriptor(
                buffer,
                schema,
                new[] { IngressProducer },
                Owner,
                Stage,
                Stage,
                order,
                BufferLifetime.Step,
                LaneCapacity,
                LaneByteCapacity,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);
        }
    }
}
