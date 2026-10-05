// GameCore.Gameplay.Player - the player plugin's generated-style declarations (P1.3; P-034, P-039, P-042, P-043).
//
// One plugin type, one owner (gameplay.player.owner) and one authoritative domain (gameplay.player.domain.player, v1)
// holding eight int32 slots on the player's entity target. One stage (gameplay.player.stage.command) runs one managed
// system that drains three command routes - player.move, player.interact and player.setFocus - each with its own
// bounded ingress lane. The shapes copy EntityDeclarations; the catalog names below are contributed to the generated
// catalog by the Editor's PlayerCatalogContributor.
#nullable enable
using System.Collections.Generic;
using System.Text;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Unity.Runtime;

namespace GameCore.Gameplay.Player
{
    /// <summary>Identities and declarations of the player plugin.</summary>
    public static class PlayerDeclarations
    {
        public const string PackageVersion = "1.0.0";

        public const int LaneCapacity = 16;

        /// <summary>Bytes one lane holds per step: the largest player payload (move) is 20 bytes.</summary>
        public const int LaneByteCapacity = 512;

        // Catalog stable names (without the gameplay. prefix).
        public const string CatalogPackage = "package.player";
        public const string CatalogPlugin = "player.plugin";
        public const string CatalogCommandSystem = "player.system.command";
        public const string CatalogLayout = "player.layout.player";
        public const string CatalogConfigSchema = "player.schema.config";
        public const string CatalogDomainSchema = "player.domain.player";

        public static readonly Id128 OwnerPackage = GameplayIds.Id(CatalogPackage);

        public static readonly PluginTypeId PluginType = GameplayIds.PluginType("player.plugin-type");

        public static readonly PluginInstanceId Instance = GameplayIds.Instance("player.instance");

        public static readonly FactoryKey PluginFactory = GameplayIds.Key(CatalogPlugin);

        public static readonly SchemaRef ConfigSchema = GameplayIds.Schema(CatalogConfigSchema, 1U);

        public static readonly SchemaRef Domain = GameplayIds.Schema(CatalogDomainSchema, 1U);

        public static readonly FactoryKey Layout = GameplayIds.Key(CatalogLayout);

        public static readonly OwnerId Owner = PlayerSlots.Owner;

        public static readonly StageId Stage = GameplayIds.Stage("player.stage.command");

        public static readonly FactoryKey CommandSystem = GameplayIds.Key(CatalogCommandSystem);

        public static readonly FactoryKey IngressProducer = GameplayIds.Key("player.ingress");

        public static readonly BufferId MoveBuffer = GameplayIds.Buffer("player.buffer.move");
        public static readonly BufferId InteractBuffer = GameplayIds.Buffer("player.buffer.interact");
        public static readonly BufferId SetFocusBuffer = GameplayIds.Buffer("player.buffer.set-focus");

        /// <summary>P1.7a (P3.1 request): the lane of player.restoreStamina.</summary>
        public static readonly BufferId RestoreStaminaBuffer = GameplayIds.Buffer("player.buffer.restore-stamina");

        public static readonly FactoryKey MoveOrder = GameplayIds.Key("player.order.move");
        public static readonly FactoryKey InteractOrder = GameplayIds.Key("player.order.interact");
        public static readonly FactoryKey SetFocusOrder = GameplayIds.Key("player.order.set-focus");

        public static readonly FactoryKey RestoreStaminaOrder = GameplayIds.Key("player.order.restore-stamina");

        /// <summary>The schemas this package contributes to the generated catalog (one UInt32 field each).</summary>
        public static IReadOnlyList<GameplayCatalogNames.SchemaName> CatalogSchemas { get; } = System.Array.AsReadOnly(new[]
        {
            new GameplayCatalogNames.SchemaName(CatalogConfigSchema, "player.serializer.config", "PlayerConfig", CatalogPackage),
            new GameplayCatalogNames.SchemaName(CatalogDomainSchema, "player.serializer.domain-player", "PlayerDomain", CatalogPackage),
        });

        /// <summary>The static registrations this package contributes to the generated catalog.</summary>
        public static IReadOnlyList<GameplayCatalogNames.EntryName> CatalogEntries { get; } = System.Array.AsReadOnly(new[]
        {
            new GameplayCatalogNames.EntryName("PluginFactory", CatalogPlugin, "PlayerPluginKey", CatalogPackage),
            new GameplayCatalogNames.EntryName("SystemFactory", CatalogCommandSystem, "PlayerCommandSystemKey", CatalogPackage),
            new GameplayCatalogNames.EntryName("LayoutApply", CatalogLayout, "PlayerLayoutKey", CatalogPackage),
        });

        /// <summary>Non-empty package content hash: SHA-256 of the canonical declaration names (P-009).</summary>
        public static ContentHash PackageContentHash() =>
            ContentHash.Compute(Encoding.UTF8.GetBytes(
                "gameplay.player/" + PackageVersion + ";" + GameplayIds.Hex(PluginType.Value) + ";"
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
                Slot(PlayerSlots.PosX, "player.field.pos-x"),
                Slot(PlayerSlots.PosY, "player.field.pos-y"),
                Slot(PlayerSlots.PosZ, "player.field.pos-z"),
                Slot(PlayerSlots.Yaw, "player.field.yaw"),
                Slot(PlayerSlots.Stamina, "player.field.stamina"),
                Slot(PlayerSlots.Focus, "player.field.focus"),
                Slot(PlayerSlots.RegionKey, "player.field.region-key"),
                Slot(PlayerSlots.RegenDelayMs, "player.field.regen-delay-ms"),
                Slot(PlayerMotionSlots.VerticalSpeed, "player.field.vertical-speed"),
                Slot(PlayerMotionSlots.Grounded, "player.field.grounded"),
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
                Buffer(MoveBuffer, PlayerSlots.MoveCommand, MoveOrder),
                Buffer(InteractBuffer, PlayerSlots.InteractCommand, InteractOrder),
                Buffer(SetFocusBuffer, PlayerSlots.SetFocusCommand, SetFocusOrder),
                Buffer(RestoreStaminaBuffer, PlayerMotionSlots.RestoreStaminaCommand, RestoreStaminaOrder),
            };
        }

        public static IReadOnlyList<CommandRoute> Routes()
        {
            return new List<CommandRoute>
            {
                new CommandRoute(PlayerSlots.MoveRoute, Owner, PlayerSlots.MoveCommand, Stage, Stage, MoveBuffer, IngressProducer, LaneCapacity, false),
                new CommandRoute(PlayerSlots.InteractRoute, Owner, PlayerSlots.InteractCommand, Stage, Stage, InteractBuffer, IngressProducer, LaneCapacity, false),
                new CommandRoute(PlayerSlots.SetFocusRoute, Owner, PlayerSlots.SetFocusCommand, Stage, Stage, SetFocusBuffer, IngressProducer, LaneCapacity, false),
                new CommandRoute(PlayerMotionSlots.RestoreStaminaRoute, Owner, PlayerMotionSlots.RestoreStaminaCommand, Stage, Stage, RestoreStaminaBuffer, IngressProducer, LaneCapacity, false),
            };
        }

        public static IReadOnlyList<MessageBufferDescriptor> Lanes()
        {
            return new List<MessageBufferDescriptor>
            {
                Lane(MoveBuffer, PlayerSlots.MoveCommand, MoveOrder),
                Lane(InteractBuffer, PlayerSlots.InteractCommand, InteractOrder),
                Lane(SetFocusBuffer, PlayerSlots.SetFocusCommand, SetFocusOrder),
                Lane(RestoreStaminaBuffer, PlayerMotionSlots.RestoreStaminaCommand, RestoreStaminaOrder),
            };
        }

        public static SystemRegistration CommandSystemRegistration() =>
            new ManagedSystemRegistration<PlayerCommandSystem>(CommandSystem, Stage, "GameplayPlayerCommandSystem");

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
