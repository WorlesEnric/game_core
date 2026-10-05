// GameCore.Gameplay.Player - the player plugin's generated-style declarations (P1.3; P-034, P-039, P-042, P-043).
//
// One plugin type, one owner (gameplay.player.owner) and one authoritative domain (gameplay.player.domain.player, v1)
// holding eight int32 slots on the player's entity target. One stage (gameplay.player.stage.command) runs one managed
// system that drains four command routes - player.move, player.interact, player.setFocus and (declared by P1.7b,
// handled by P1.7a) player.restoreStamina - each with its own bounded ingress lane. The shapes copy EntityDeclarations; the catalog names below are contributed to the generated
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
    /// <summary>player.restoreStamina payload layout: one int32, the stamina units to restore (&gt; 0).</summary>
    public readonly struct RestoreStaminaPayload
    {
        public const int Length = 4;

        public RestoreStaminaPayload(int amount)
        {
            Amount = amount;
        }

        public int Amount { get; }
    }

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

        public static readonly FactoryKey MoveOrder = GameplayIds.Key("player.order.move");
        public static readonly FactoryKey InteractOrder = GameplayIds.Key("player.order.interact");
        public static readonly FactoryKey SetFocusOrder = GameplayIds.Key("player.order.set-focus");

        /// <summary>The tool/action id of the stamina restore command (ActionKind.RestoreStamina runs it).</summary>
        public const string RestoreStaminaCommandId = "player.restoreStamina";

        /// <summary>
        /// player.restoreStamina: payload <see cref="RestoreStaminaPayload"/> (one int32 <c>amount</c>, stamina units, &gt; 0),
        /// targeted at the player's entity. The player clamps the result to PlayerDefinition.staminaMax. Declared by P1.7b;
        /// the handler in the player command system is P1.7a's (until then the system rejects it as Ineligible).
        /// </summary>
        public static readonly RouteId RestoreStaminaRoute = GameplayIds.Route("player.route.restore-stamina");

        public static readonly SchemaRef RestoreStaminaCommand = GameplayIds.Schema("player.command.restore-stamina", 1U);

        public static readonly BufferId RestoreStaminaBuffer = GameplayIds.Buffer("player.buffer.restore-stamina");

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
                Buffer(RestoreStaminaBuffer, RestoreStaminaCommand, RestoreStaminaOrder),
            };
        }

        public static IReadOnlyList<CommandRoute> Routes()
        {
            return new List<CommandRoute>
            {
                new CommandRoute(PlayerSlots.MoveRoute, Owner, PlayerSlots.MoveCommand, Stage, Stage, MoveBuffer, IngressProducer, LaneCapacity, false),
                new CommandRoute(PlayerSlots.InteractRoute, Owner, PlayerSlots.InteractCommand, Stage, Stage, InteractBuffer, IngressProducer, LaneCapacity, false),
                new CommandRoute(PlayerSlots.SetFocusRoute, Owner, PlayerSlots.SetFocusCommand, Stage, Stage, SetFocusBuffer, IngressProducer, LaneCapacity, false),
                new CommandRoute(RestoreStaminaRoute, Owner, RestoreStaminaCommand, Stage, Stage, RestoreStaminaBuffer, IngressProducer, LaneCapacity, false),
            };
        }

        public static IReadOnlyList<MessageBufferDescriptor> Lanes()
        {
            return new List<MessageBufferDescriptor>
            {
                Lane(MoveBuffer, PlayerSlots.MoveCommand, MoveOrder),
                Lane(InteractBuffer, PlayerSlots.InteractCommand, InteractOrder),
                Lane(SetFocusBuffer, PlayerSlots.SetFocusCommand, SetFocusOrder),
                Lane(RestoreStaminaBuffer, RestoreStaminaCommand, RestoreStaminaOrder),
            };
        }

        public static SystemRegistration CommandSystemRegistration() =>
            new ManagedSystemRegistration<PlayerCommandSystem>(CommandSystem, Stage, "GameplayPlayerCommandSystem");

        /// <summary>Encodes a player.restoreStamina payload (refuses a non-positive amount).</summary>
        public static FrozenPayload EncodeRestoreStamina(int amount)
        {
            if (amount <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(amount), AuthoringHardeningCodes.RestoreStaminaInvalid + ": stamina amounts are positive");
            }

            return new GameplayPayloadWriter().Int32(amount).Freeze();
        }

        /// <summary>Decodes a player.restoreStamina payload; false when it is not exactly one positive int32.</summary>
        public static bool TryDecodeRestoreStamina(IReadOnlyList<byte> payload, out RestoreStaminaPayload value)
        {
            value = default(RestoreStaminaPayload);
            if (payload == null)
            {
                return false;
            }

            var reader = new GameplayPayloadReader(payload);
            if (!reader.HasLength(RestoreStaminaPayload.Length))
            {
                return false;
            }

            int amount = reader.Int32();
            if (amount <= 0)
            {
                return false;
            }

            value = new RestoreStaminaPayload(amount);
            return true;
        }

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
