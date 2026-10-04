// GameCore.Gameplay.Ui - the UI plugin's generated-style declarations (P1.5, catalog row 10; P-034, P-039, P-042).
//
// One plugin type and one owner (gameplay.ui.owner) with one domain on one target per world, the UI session target:
//   gameplay.ui.domain.session   ui.screen, ui.returnTo, ui.message
// One stage (gameplay.ui.stage.command) runs one managed system that drains three routes:
//   ui.open     (session target; screen)            -> ScreenChanged
//   ui.close    (session target; 0)                 -> ScreenChanged
//   ui.command  (session target; action, argument)  -> ScreenChanged (with the host action the UI host performs)
// Every accepted command commits exactly one ScreenChanged event (from, to, action, argument); a pure screen change
// carries action None. The session target is seeded from the static session recipe under the world scope.
#nullable enable
using System.Collections.Generic;
using System.Text;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Unity.Runtime;

namespace GameCore.Gameplay.Ui
{
    /// <summary>Identities and declarations of the UI plugin.</summary>
    public static class UiDeclarations
    {
        public const string PackageVersion = "1.0.0";

        public const int LaneCapacity = 16;

        /// <summary>Bytes one lane holds per step: the largest UI payload is 8 bytes.</summary>
        public const int LaneByteCapacity = 256;

        public static readonly Id128 OwnerPackage = GameplayIds.Id(GameplayCatalogNames.UiPackage);

        public static readonly PluginTypeId PluginType = GameplayIds.PluginType("ui.plugin-type");

        public static readonly PluginInstanceId Instance = GameplayIds.Instance("ui.instance");

        public static readonly FactoryKey PluginFactory = GameplayIds.Key(GameplayCatalogNames.UiPlugin);

        public static readonly SchemaRef ConfigSchema = GameplayIds.Schema(GameplayCatalogNames.UiConfigSchema, 1U);

        public static readonly SchemaRef SessionDomain = GameplayIds.Schema(GameplayCatalogNames.UiSessionDomainSchema, 1U);

        public static readonly FactoryKey SessionLayout = GameplayIds.Key(GameplayCatalogNames.UiSessionLayout);

        public static readonly FactoryKey SessionApplier = GameplayIds.Key(GameplayCatalogNames.UiSessionApplier);

        public static readonly OwnerId Owner = PresentationSlots.UiOwner;

        public static readonly StageId Stage = GameplayIds.Stage("ui.stage.command");

        public static readonly FactoryKey CommandSystem = GameplayIds.Key(GameplayCatalogNames.UiCommandSystem);

        public static readonly SchemaRef SessionRecipeSchema = GameplayIds.Schema("ui.schema.session-recipe", 1U);

        /// <summary>The recipe the UI session target is seeded from (a static definition, revision 1).</summary>
        public static readonly DefinitionRef SessionRecipe =
            new DefinitionRef(GameplayIds.Definition("ui.session-recipe"), SessionRecipeSchema, DefinitionRevision.First);

        public static readonly FactoryKey IngressProducer = GameplayIds.Key("ui.ingress");

        public static readonly RouteId OpenRoute = GameplayIds.Route("ui.route.open");
        public static readonly RouteId CloseRoute = GameplayIds.Route("ui.route.close");
        public static readonly RouteId UiCommandRoute = GameplayIds.Route("ui.route.command");

        public static readonly SchemaRef OpenCommand = GameplayIds.Schema("ui.command.open", 1U);
        public static readonly SchemaRef CloseCommand = GameplayIds.Schema("ui.command.close", 1U);
        public static readonly SchemaRef CommandCommand = GameplayIds.Schema("ui.command.command", 1U);

        public static readonly SchemaRef ScreenChangedEvent = GameplayIds.Schema("ui.event.screen-changed", 1U);

        public static readonly BufferId OpenBuffer = GameplayIds.Buffer("ui.buffer.open");
        public static readonly BufferId CloseBuffer = GameplayIds.Buffer("ui.buffer.close");
        public static readonly BufferId CommandBuffer = GameplayIds.Buffer("ui.buffer.command");

        public static readonly FactoryKey OpenOrder = GameplayIds.Key("ui.order.open");
        public static readonly FactoryKey CloseOrder = GameplayIds.Key("ui.order.close");
        public static readonly FactoryKey CommandOrder = GameplayIds.Key("ui.order.command");

        public static ContentHash PackageContentHash() =>
            ContentHash.Compute(Encoding.UTF8.GetBytes(
                "gameplay.ui/" + PackageVersion + ";" + GameplayIds.Hex(PluginType.Value) + ";"
                + GameplayIds.Hex(SessionDomain.Id.Value) + ";" + GameplayIds.Hex(Stage.Value)));

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
                Slot(PresentationSlots.Screen, "ui.field.screen"),
                Slot(PresentationSlots.ReturnTo, "ui.field.return-to"),
                Slot(PresentationSlots.Message, "ui.field.message"),
            };
        }

        public static IReadOnlyList<StageSpec> Stages()
        {
            var access = new AccessSet(new[]
            {
                new AccessDeclaration(SessionDomain, AccessMode.ReadWrite, default(Id128)),
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
                Buffer(OpenBuffer, OpenCommand, OpenOrder),
                Buffer(CloseBuffer, CloseCommand, CloseOrder),
                Buffer(CommandBuffer, CommandCommand, CommandOrder),
            };
        }

        public static IReadOnlyList<CommandRoute> Routes()
        {
            return new List<CommandRoute>
            {
                new CommandRoute(OpenRoute, Owner, OpenCommand, Stage, Stage, OpenBuffer, IngressProducer, LaneCapacity, false),
                new CommandRoute(CloseRoute, Owner, CloseCommand, Stage, Stage, CloseBuffer, IngressProducer, LaneCapacity, false),
                new CommandRoute(UiCommandRoute, Owner, CommandCommand, Stage, Stage, CommandBuffer, IngressProducer, LaneCapacity, false),
            };
        }

        public static IReadOnlyList<MessageBufferDescriptor> Lanes()
        {
            return new List<MessageBufferDescriptor>
            {
                Lane(OpenBuffer, OpenCommand, OpenOrder),
                Lane(CloseBuffer, CloseCommand, CloseOrder),
                Lane(CommandBuffer, CommandCommand, CommandOrder),
            };
        }

        public static SystemRegistration CommandSystemRegistration() =>
            new ManagedSystemRegistration<UiCommandSystem>(CommandSystem, Stage, "GameplayUiCommandSystem");

        private static StateSlotSpec Slot(SlotId slot, string field)
        {
            return new StateSlotSpec(
                slot,
                Owner,
                SessionDomain,
                SessionLayout,
                new List<FieldOwnership> { new FieldOwnership(SessionDomain, GameplayIds.Id(field)) },
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
