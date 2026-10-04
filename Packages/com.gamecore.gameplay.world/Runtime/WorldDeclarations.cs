// GameCore.Gameplay.World - the world plugin's generated-style declarations (P1.1; P-034, P-039, P-042, P-043).
//
// One plugin type and one owner (gameplay.world.owner) with two domains:
//   gameplay.world.domain.region     world.residency, world.visits        on region targets
//   gameplay.world.domain.placement  world.region, posX, posY, posZ, yaw  on entity targets
// One stage (gameplay.world.stage.command) runs one managed system that drains three routes:
//   world.travel        (traveller entity; destination region key, portal key)  -> RegionLeft, RegionEntered
//   world.setResidency  (region target; residency) host issuer only             -> RegionResidencyChanged
//   world.place         (entity; x, y, z mm, yaw mrad)                          -> EntityPlaced
#nullable enable
using System.Collections.Generic;
using System.Text;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Unity.Runtime;

namespace GameCore.Gameplay.World
{
    /// <summary>Identities and declarations of the world plugin.</summary>
    public static class WorldDeclarations
    {
        public const string PackageVersion = "1.0.0";

        public const int LaneCapacity = 16;

        /// <summary>Bytes one lane holds per step: the largest world payload is 16 bytes.</summary>
        public const int LaneByteCapacity = 512;

        public static readonly Id128 OwnerPackage = GameplayIds.Id(GameplayCatalogNames.WorldPackage);

        public static readonly PluginTypeId PluginType = GameplayIds.PluginType("world.plugin-type");

        public static readonly PluginInstanceId Instance = GameplayIds.Instance("world.instance");

        public static readonly FactoryKey PluginFactory = GameplayIds.Key(GameplayCatalogNames.WorldPlugin);

        public static readonly SchemaRef ConfigSchema = GameplayIds.Schema(GameplayCatalogNames.WorldConfigSchema, 1U);

        public static readonly SchemaRef RegionDomain = GameplayIds.Schema(GameplayCatalogNames.RegionDomainSchema, 1U);

        public static readonly SchemaRef PlacementDomain = GameplayIds.Schema(GameplayCatalogNames.PlacementDomainSchema, 1U);

        public static readonly FactoryKey RegionLayout = GameplayIds.Key(GameplayCatalogNames.RegionLayout);

        public static readonly FactoryKey PlacementLayout = GameplayIds.Key(GameplayCatalogNames.PlacementLayout);

        public static readonly OwnerId Owner = GameplaySlots.WorldOwner;

        public static readonly StageId Stage = GameplayIds.Stage("world.stage.command");

        public static readonly FactoryKey CommandSystem = GameplayIds.Key(GameplayCatalogNames.WorldCommandSystem);

        public static readonly FactoryKey RegionApplier = GameplayIds.Key(GameplayCatalogNames.RegionApplier);

        /// <summary>The recipe schema of region targets.</summary>
        public static readonly SchemaRef RegionRecipeSchema = GameplayIds.Schema("world.schema.region-recipe", 1U);

        /// <summary>The one recipe every region target is seeded from (a static definition, revision 1).</summary>
        public static readonly DefinitionRef RegionRecipe =
            new DefinitionRef(GameplayIds.Definition("world.region-recipe"), RegionRecipeSchema, DefinitionRevision.First);

        public static readonly FactoryKey IngressProducer = GameplayIds.Key("world.ingress");

        public static readonly RouteId TravelRoute = GameplayIds.Route("world.route.travel");
        public static readonly RouteId SetResidencyRoute = GameplayIds.Route("world.route.set-residency");
        public static readonly RouteId PlaceRoute = GameplayIds.Route("world.route.place");

        public static readonly SchemaRef TravelCommand = GameplayIds.Schema("world.command.travel", 1U);
        public static readonly SchemaRef SetResidencyCommand = GameplayIds.Schema("world.command.set-residency", 1U);
        public static readonly SchemaRef PlaceCommand = GameplayIds.Schema("world.command.place", 1U);

        public static readonly SchemaRef RegionEnteredEvent = GameplayIds.Schema("world.event.region-entered", 1U);
        public static readonly SchemaRef RegionLeftEvent = GameplayIds.Schema("world.event.region-left", 1U);
        public static readonly SchemaRef ResidencyChangedEvent = GameplayIds.Schema("world.event.residency-changed", 1U);
        public static readonly SchemaRef EntityPlacedEvent = GameplayIds.Schema("world.event.entity-placed", 1U);

        public static readonly BufferId TravelBuffer = GameplayIds.Buffer("world.buffer.travel");
        public static readonly BufferId SetResidencyBuffer = GameplayIds.Buffer("world.buffer.set-residency");
        public static readonly BufferId PlaceBuffer = GameplayIds.Buffer("world.buffer.place");

        public static readonly FactoryKey TravelOrder = GameplayIds.Key("world.order.travel");
        public static readonly FactoryKey SetResidencyOrder = GameplayIds.Key("world.order.set-residency");
        public static readonly FactoryKey PlaceOrder = GameplayIds.Key("world.order.place");

        public static ContentHash PackageContentHash() =>
            ContentHash.Compute(Encoding.UTF8.GetBytes(
                "gameplay.world/" + PackageVersion + ";" + GameplayIds.Hex(PluginType.Value) + ";"
                + GameplayIds.Hex(RegionDomain.Id.Value) + ";" + GameplayIds.Hex(PlacementDomain.Id.Value) + ";"
                + GameplayIds.Hex(Stage.Value)));

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
                Slot(GameplaySlots.Residency, RegionDomain, RegionLayout, "world.field.residency"),
                Slot(GameplaySlots.Visits, RegionDomain, RegionLayout, "world.field.visits"),
                Slot(GameplaySlots.Region, PlacementDomain, PlacementLayout, "world.field.region"),
                Slot(GameplaySlots.PosX, PlacementDomain, PlacementLayout, "world.field.pos-x"),
                Slot(GameplaySlots.PosY, PlacementDomain, PlacementLayout, "world.field.pos-y"),
                Slot(GameplaySlots.PosZ, PlacementDomain, PlacementLayout, "world.field.pos-z"),
                Slot(GameplaySlots.Yaw, PlacementDomain, PlacementLayout, "world.field.yaw"),
            };
        }

        public static IReadOnlyList<StageSpec> Stages()
        {
            var access = new AccessSet(new[]
            {
                new AccessDeclaration(RegionDomain, AccessMode.ReadWrite, default(Id128)),
                new AccessDeclaration(PlacementDomain, AccessMode.ReadWrite, default(Id128)),
            });
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
                Buffer(TravelBuffer, TravelCommand, TravelOrder),
                Buffer(SetResidencyBuffer, SetResidencyCommand, SetResidencyOrder),
                Buffer(PlaceBuffer, PlaceCommand, PlaceOrder),
            };
        }

        public static IReadOnlyList<CommandRoute> Routes()
        {
            return new List<CommandRoute>
            {
                new CommandRoute(TravelRoute, Owner, TravelCommand, Stage, Stage, TravelBuffer, IngressProducer, LaneCapacity, false),
                new CommandRoute(SetResidencyRoute, Owner, SetResidencyCommand, Stage, Stage, SetResidencyBuffer, IngressProducer, LaneCapacity, false),
                new CommandRoute(PlaceRoute, Owner, PlaceCommand, Stage, Stage, PlaceBuffer, IngressProducer, LaneCapacity, false),
            };
        }

        public static IReadOnlyList<MessageBufferDescriptor> Lanes()
        {
            return new List<MessageBufferDescriptor>
            {
                Lane(TravelBuffer, TravelCommand, TravelOrder),
                Lane(SetResidencyBuffer, SetResidencyCommand, SetResidencyOrder),
                Lane(PlaceBuffer, PlaceCommand, PlaceOrder),
            };
        }

        public static SystemRegistration CommandSystemRegistration() =>
            new ManagedSystemRegistration<WorldCommandSystem>(CommandSystem, Stage, "GameplayWorldCommandSystem");

        private static StateSlotSpec Slot(SlotId slot, SchemaRef domain, FactoryKey layout, string field)
        {
            return new StateSlotSpec(
                slot,
                Owner,
                domain,
                layout,
                new List<FieldOwnership> { new FieldOwnership(domain, GameplayIds.Id(field)) },
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
