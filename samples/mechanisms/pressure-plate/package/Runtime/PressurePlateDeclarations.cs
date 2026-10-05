#nullable enable
// Hollowmere.Mechanism.PressurePlate - the pressure plate plugin's generated-style declarations (W-MECH-01 sample).
//
// One plugin type, one owner (hollowmere.pressureplate.owner) and one authoritative domain
// (hollowmere.pressureplate.domain.plate, v1) holding two int32 slots per plate target:
//   plate.pressed  0/1, 1 while weight >= the plate's threshold
//   plate.weight   actors standing on the plate
// One stage runs one managed system that drains one command route:
//   plate.press {actor, load}  -> PlatePressed (released -> pressed) / PlateReleased (pressed -> released) /
//                                 PlateWeightChanged (accepted, no flip)
// Every identity is derived from a stable name under the mechanism's own prefix "hollowmere.pressureplate.", so no
// declaration can collide with the gameplay catalog ("gameplay.") a plate world is composed with.
using System.Collections.Generic;
using System.Text;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Unity.Runtime;

namespace Hollowmere.Mechanism.PressurePlate
{
    /// <summary>Derivation of the mechanism's identities from stable names.</summary>
    public static class PlateIds
    {
        /// <summary>Prefix of every stable name of the mechanism.</summary>
        public const string Prefix = "hollowmere.pressureplate.";

        public static Id128 Id(string name) => StableNameKeyDerivation.Derive(Prefix + name);

        public static FactoryKey Key(string name) => new FactoryKey(Id(name), 1U);

        public static SchemaRef Schema(string name, uint version) => new SchemaRef(new SchemaId(Id(name)), version);

        public static string Hex(Id128 id) => Id128Codec.ToHex(id);
    }

    /// <summary>Identities and declarations of the pressure plate plugin.</summary>
    public static class PressurePlateDeclarations
    {
        public const string PackageName = "com.hollowmere.mechanism.pressureplate";

        public const string PackageVersion = "0.1.0";

        public const uint SlotSchemaVersion = 1U;

        /// <summary>Rows the press lane holds per step (P-043).</summary>
        public const int LaneCapacity = 16;

        /// <summary>Bytes the press lane holds per step: a press is 20 bytes.</summary>
        public const int LaneByteCapacity = 512;

        public static readonly Id128 OwnerPackage = PlateIds.Id("package");

        public static readonly PluginTypeId PluginType = new PluginTypeId(PlateIds.Id("plugin-type"));

        public static readonly PluginInstanceId Instance = new PluginInstanceId(PlateIds.Id("instance"));

        /// <summary>Matches the generated catalog key <c>PressurePlatePluginKey</c>.</summary>
        public static readonly FactoryKey PluginFactory = PlateIds.Key("plugin");

        public static readonly SchemaRef ConfigSchema = PlateIds.Schema("schema.config", 1U);

        public static readonly SchemaRef Domain = PlateIds.Schema("domain.plate", 1U);

        /// <summary>Matches the generated catalog key <c>PlateLayoutKey</c>.</summary>
        public static readonly FactoryKey Layout = PlateIds.Key("layout.plate");

        public static readonly OwnerId Owner = new OwnerId(PlateIds.Id("owner"));

        public static readonly SlotId PressedSlot = new SlotId(PlateIds.Id("slot.plate.pressed"));

        public static readonly SlotId WeightSlot = new SlotId(PlateIds.Id("slot.plate.weight"));

        public static readonly StageId Stage = new StageId(PlateIds.Id("stage.command"));

        /// <summary>Matches the generated catalog key <c>PressurePlateCommandSystemKey</c>.</summary>
        public static readonly FactoryKey CommandSystem = PlateIds.Key("system.command");

        /// <summary>Matches the generated catalog key <c>PlateApplierKey</c>.</summary>
        public static readonly FactoryKey Applier = PlateIds.Key("applier");

        public static readonly SchemaRef RecipeSchema = PlateIds.Schema("schema.plate-recipe", 1U);

        /// <summary>The one recipe a plate target is spawned or seeded from (a static definition, revision 1).</summary>
        public static readonly DefinitionRef PlateRecipe =
            new DefinitionRef(new DefinitionId(PlateIds.Id("plate-recipe")), RecipeSchema, DefinitionRevision.First);

        public static readonly FactoryKey IngressProducer = PlateIds.Key("ingress");

        /// <summary>The route <c>plate.press</c>.</summary>
        public static readonly RouteId PressRoute = new RouteId(PlateIds.Id("route.plate.press"));

        public static readonly SchemaRef PressCommand = PlateIds.Schema("command.plate.press", 1U);

        /// <summary>The event <c>PlatePressed</c>.</summary>
        public static readonly SchemaRef PressedEvent = PlateIds.Schema("event.plate-pressed", 1U);

        /// <summary>The event <c>PlateReleased</c>.</summary>
        public static readonly SchemaRef ReleasedEvent = PlateIds.Schema("event.plate-released", 1U);

        /// <summary>The event <c>PlateWeightChanged</c>: an accepted press that did not flip the plate.</summary>
        public static readonly SchemaRef WeightChangedEvent = PlateIds.Schema("event.plate-weight-changed", 1U);

        public static readonly BufferId PressBuffer = new BufferId(PlateIds.Id("buffer.plate.press"));

        public static readonly FactoryKey PressOrder = PlateIds.Key("order.plate.press");

        /// <summary>Non-empty package content hash: SHA-256 of the canonical declaration names (P-009).</summary>
        public static ContentHash PackageContentHash() =>
            ContentHash.Compute(Encoding.UTF8.GetBytes(
                PackageName + "/" + PackageVersion + ";" + PlateIds.Hex(PluginType.Value) + ";"
                + PlateIds.Hex(Domain.Id.Value) + ";" + PlateIds.Hex(Stage.Value)));

        /// <summary>The pressure plate plugin manifest.</summary>
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
                Slot(PressedSlot, "field.plate.pressed"),
                Slot(WeightSlot, "field.plate.weight"),
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
                new BufferSpec(
                    PressBuffer,
                    PressCommand,
                    new List<FactoryKey> { IngressProducer },
                    Stage,
                    Stage,
                    PressOrder,
                    BufferLifetime.Step,
                    LaneCapacity,
                    BufferOverflowPolicy.RejectBeforeMutation,
                    BufferCancellationPolicy.Drain),
            };
        }

        /// <summary>The <c>plate.press</c> route (owner = the plate owner, consumed in the command stage).</summary>
        public static IReadOnlyList<CommandRoute> Routes()
        {
            return new List<CommandRoute>
            {
                new CommandRoute(PressRoute, Owner, PressCommand, Stage, Stage, PressBuffer, IngressProducer, LaneCapacity, false),
            };
        }

        /// <summary>The bounded ingress lane of the press route.</summary>
        public static IReadOnlyList<MessageBufferDescriptor> Lanes()
        {
            return new List<MessageBufferDescriptor>
            {
                new MessageBufferDescriptor(
                    PressBuffer,
                    PressCommand,
                    new[] { IngressProducer },
                    Owner,
                    Stage,
                    Stage,
                    PressOrder,
                    BufferLifetime.Step,
                    LaneCapacity,
                    LaneByteCapacity,
                    BufferOverflowPolicy.RejectBeforeMutation,
                    BufferCancellationPolicy.Drain),
            };
        }

        /// <summary>The managed system registration of the command stage.</summary>
        public static SystemRegistration CommandSystemRegistration() =>
            new ManagedSystemRegistration<PressurePlateCommandSystem>(CommandSystem, Stage, "PressurePlateCommandSystem");

        private static StateSlotSpec Slot(SlotId slot, string field)
        {
            return new StateSlotSpec(
                slot,
                Owner,
                Domain,
                Layout,
                new List<FieldOwnership> { new FieldOwnership(Domain, PlateIds.Id(field)) },
                default(FactoryKey),
                default(FactoryKey),
                default(FactoryKey),
                LastSupportPolicy.PreserveDormant,
                default(FactoryKey),
                null);
        }
    }
}
