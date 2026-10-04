// GameCore.Gameplay.Audio - the audio plugin's generated-style declarations (P1.5, catalog row 11; P-034, P-039, P-042).
//
// One plugin type and one owner (gameplay.audio.owner) with one domain on one target per world, the audio session
// target:
//   gameplay.audio.domain.session  audio.musicState, audio.ambienceZone, audio.volumeMaster/Music/Sfx/Voice
// One stage (gameplay.audio.stage.command) runs one managed system that drains six routes:
//   audio.setMusicState    (state key, stinger key)        -> MusicStateChanged
//   audio.setAmbienceZone  (region key)                    -> AmbienceChanged
//   audio.setVolume        (channel, permille)             -> VolumeChanged
//   audio.playSfx          (sfx key, x, y, z mm)           -> SfxPlayed
//   audio.playVoice        (clip key, speaker key)         -> VoicePlayed
//   audio.stopVoice        (0)                             -> VoiceStopped
// Continuous state (music, ambience, volumes) is slots, so it is saved and restored; one-shots (sfx, voice) are events
// the presentation plays when they commit.
#nullable enable
using System.Collections.Generic;
using System.Text;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Unity.Runtime;

namespace GameCore.Gameplay.Audio
{
    /// <summary>Identities and declarations of the audio plugin.</summary>
    public static class AudioDeclarations
    {
        public const string PackageVersion = "1.0.0";

        public const int LaneCapacity = 16;

        /// <summary>Bytes one lane holds per step: the largest audio payload is 16 bytes.</summary>
        public const int LaneByteCapacity = 512;

        public static readonly Id128 OwnerPackage = GameplayIds.Id(GameplayCatalogNames.AudioPackage);

        public static readonly PluginTypeId PluginType = GameplayIds.PluginType("audio.plugin-type");

        public static readonly PluginInstanceId Instance = GameplayIds.Instance("audio.instance");

        public static readonly FactoryKey PluginFactory = GameplayIds.Key(GameplayCatalogNames.AudioPlugin);

        public static readonly SchemaRef ConfigSchema = GameplayIds.Schema(GameplayCatalogNames.AudioConfigSchema, 1U);

        public static readonly SchemaRef SessionDomain = GameplayIds.Schema(GameplayCatalogNames.AudioSessionDomainSchema, 1U);

        public static readonly FactoryKey SessionLayout = GameplayIds.Key(GameplayCatalogNames.AudioSessionLayout);

        public static readonly FactoryKey SessionApplier = GameplayIds.Key(GameplayCatalogNames.AudioSessionApplier);

        public static readonly OwnerId Owner = PresentationSlots.AudioOwner;

        public static readonly StageId Stage = GameplayIds.Stage("audio.stage.command");

        public static readonly FactoryKey CommandSystem = GameplayIds.Key(GameplayCatalogNames.AudioCommandSystem);

        public static readonly SchemaRef SessionRecipeSchema = GameplayIds.Schema("audio.schema.session-recipe", 1U);

        public static readonly DefinitionRef SessionRecipe =
            new DefinitionRef(GameplayIds.Definition("audio.session-recipe"), SessionRecipeSchema, DefinitionRevision.First);

        public static readonly FactoryKey IngressProducer = GameplayIds.Key("audio.ingress");

        public static readonly RouteId SetMusicStateRoute = GameplayIds.Route("audio.route.set-music-state");
        public static readonly RouteId SetAmbienceZoneRoute = GameplayIds.Route("audio.route.set-ambience-zone");
        public static readonly RouteId SetVolumeRoute = GameplayIds.Route("audio.route.set-volume");
        public static readonly RouteId PlaySfxRoute = GameplayIds.Route("audio.route.play-sfx");
        public static readonly RouteId PlayVoiceRoute = GameplayIds.Route("audio.route.play-voice");
        public static readonly RouteId StopVoiceRoute = GameplayIds.Route("audio.route.stop-voice");

        public static readonly SchemaRef SetMusicStateCommand = GameplayIds.Schema("audio.command.set-music-state", 1U);
        public static readonly SchemaRef SetAmbienceZoneCommand = GameplayIds.Schema("audio.command.set-ambience-zone", 1U);
        public static readonly SchemaRef SetVolumeCommand = GameplayIds.Schema("audio.command.set-volume", 1U);
        public static readonly SchemaRef PlaySfxCommand = GameplayIds.Schema("audio.command.play-sfx", 1U);
        public static readonly SchemaRef PlayVoiceCommand = GameplayIds.Schema("audio.command.play-voice", 1U);
        public static readonly SchemaRef StopVoiceCommand = GameplayIds.Schema("audio.command.stop-voice", 1U);

        public static readonly SchemaRef MusicStateChangedEvent = GameplayIds.Schema("audio.event.music-state-changed", 1U);
        public static readonly SchemaRef AmbienceChangedEvent = GameplayIds.Schema("audio.event.ambience-changed", 1U);
        public static readonly SchemaRef VolumeChangedEvent = GameplayIds.Schema("audio.event.volume-changed", 1U);
        public static readonly SchemaRef SfxPlayedEvent = GameplayIds.Schema("audio.event.sfx-played", 1U);
        public static readonly SchemaRef VoicePlayedEvent = GameplayIds.Schema("audio.event.voice-played", 1U);
        public static readonly SchemaRef VoiceStoppedEvent = GameplayIds.Schema("audio.event.voice-stopped", 1U);

        public static readonly BufferId SetMusicStateBuffer = GameplayIds.Buffer("audio.buffer.set-music-state");
        public static readonly BufferId SetAmbienceZoneBuffer = GameplayIds.Buffer("audio.buffer.set-ambience-zone");
        public static readonly BufferId SetVolumeBuffer = GameplayIds.Buffer("audio.buffer.set-volume");
        public static readonly BufferId PlaySfxBuffer = GameplayIds.Buffer("audio.buffer.play-sfx");
        public static readonly BufferId PlayVoiceBuffer = GameplayIds.Buffer("audio.buffer.play-voice");
        public static readonly BufferId StopVoiceBuffer = GameplayIds.Buffer("audio.buffer.stop-voice");

        public static readonly FactoryKey SetMusicStateOrder = GameplayIds.Key("audio.order.set-music-state");
        public static readonly FactoryKey SetAmbienceZoneOrder = GameplayIds.Key("audio.order.set-ambience-zone");
        public static readonly FactoryKey SetVolumeOrder = GameplayIds.Key("audio.order.set-volume");
        public static readonly FactoryKey PlaySfxOrder = GameplayIds.Key("audio.order.play-sfx");
        public static readonly FactoryKey PlayVoiceOrder = GameplayIds.Key("audio.order.play-voice");
        public static readonly FactoryKey StopVoiceOrder = GameplayIds.Key("audio.order.stop-voice");

        public static ContentHash PackageContentHash() =>
            ContentHash.Compute(Encoding.UTF8.GetBytes(
                "gameplay.audio/" + PackageVersion + ";" + GameplayIds.Hex(PluginType.Value) + ";"
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
                Slot(PresentationSlots.MusicState, "audio.field.music-state"),
                Slot(PresentationSlots.AmbienceZone, "audio.field.ambience-zone"),
                Slot(PresentationSlots.VolumeMaster, "audio.field.volume-master"),
                Slot(PresentationSlots.VolumeMusic, "audio.field.volume-music"),
                Slot(PresentationSlots.VolumeSfx, "audio.field.volume-sfx"),
                Slot(PresentationSlots.VolumeVoice, "audio.field.volume-voice"),
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
                Buffer(SetMusicStateBuffer, SetMusicStateCommand, SetMusicStateOrder),
                Buffer(SetAmbienceZoneBuffer, SetAmbienceZoneCommand, SetAmbienceZoneOrder),
                Buffer(SetVolumeBuffer, SetVolumeCommand, SetVolumeOrder),
                Buffer(PlaySfxBuffer, PlaySfxCommand, PlaySfxOrder),
                Buffer(PlayVoiceBuffer, PlayVoiceCommand, PlayVoiceOrder),
                Buffer(StopVoiceBuffer, StopVoiceCommand, StopVoiceOrder),
            };
        }

        public static IReadOnlyList<CommandRoute> Routes()
        {
            return new List<CommandRoute>
            {
                Route(SetMusicStateRoute, SetMusicStateCommand, SetMusicStateBuffer),
                Route(SetAmbienceZoneRoute, SetAmbienceZoneCommand, SetAmbienceZoneBuffer),
                Route(SetVolumeRoute, SetVolumeCommand, SetVolumeBuffer),
                Route(PlaySfxRoute, PlaySfxCommand, PlaySfxBuffer),
                Route(PlayVoiceRoute, PlayVoiceCommand, PlayVoiceBuffer),
                Route(StopVoiceRoute, StopVoiceCommand, StopVoiceBuffer),
            };
        }

        public static IReadOnlyList<MessageBufferDescriptor> Lanes()
        {
            return new List<MessageBufferDescriptor>
            {
                Lane(SetMusicStateBuffer, SetMusicStateCommand, SetMusicStateOrder),
                Lane(SetAmbienceZoneBuffer, SetAmbienceZoneCommand, SetAmbienceZoneOrder),
                Lane(SetVolumeBuffer, SetVolumeCommand, SetVolumeOrder),
                Lane(PlaySfxBuffer, PlaySfxCommand, PlaySfxOrder),
                Lane(PlayVoiceBuffer, PlayVoiceCommand, PlayVoiceOrder),
                Lane(StopVoiceBuffer, StopVoiceCommand, StopVoiceOrder),
            };
        }

        public static SystemRegistration CommandSystemRegistration() =>
            new ManagedSystemRegistration<AudioCommandSystem>(CommandSystem, Stage, "GameplayAudioCommandSystem");

        private static CommandRoute Route(RouteId route, SchemaRef schema, BufferId buffer) =>
            new CommandRoute(route, Owner, schema, Stage, Stage, buffer, IngressProducer, LaneCapacity, false);

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
