// GameCore.Gameplay.Audio - the audio plugin as a world extension, and the typed audio command issuer (P1.5).
//
// AudioWorldExtension joins the audio plugin to a gameplay world build: the audio session target is seeded under the
// world scope and the plugin mounted at the world scope. Attach resolves the known music state keys and the ambience
// zones (the region keys of regions with an AmbienceDefinition) against the baked manifest, seeds the slots of a fresh
// world (start music state, the start region's ambience, the player's mirrored volumes) and raises Attached.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Audio;
using GameCore.Unity.App;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;

namespace GameCore.Gameplay.Audio
{
    /// <summary>What the audio extension needs from the game's audio content (engine-free keys).</summary>
    public sealed class AudioWorldSettings
    {
        /// <summary>Known music state keys.</summary>
        public List<int> MusicStates { get; } = new List<int>();

        /// <summary>Authoring ids of the regions that have an ambience.</summary>
        public List<string> AmbienceRegions { get; } = new List<string>();

        public int StartMusicState { get; set; }

        /// <summary>Initial volumes (master, music, sfx, voice) in permille.</summary>
        public int[] Volumes { get; } = { 800, 800, 800, 800 };
    }

    /// <summary>The audio plugin mounted with a gameplay world.</summary>
    public sealed class AudioWorldExtension : IGameplayWorldExtension
    {
        private readonly CatalogPluginDeclaration declaration;

        public AudioWorldExtension(AudioWorldSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            declaration = new CatalogPluginDeclaration(AudioDeclarations.Manifest(), null);
        }

        public AudioWorldSettings Settings { get; }

        public event Action<GameplayWorld, AudioModule>? Attached;

        public AudioModule? Module { get; private set; }

        public string Name => "audio";

        public CatalogPluginDeclaration Declaration => declaration;

        public PluginInstanceId Instance => AudioDeclarations.Instance;

        public FactoryKey CommandSystem => AudioDeclarations.CommandSystem;

        public SystemRegistration CommandSystemRegistration() => AudioDeclarations.CommandSystemRegistration();

        public IReadOnlyList<CommandRoute> Routes() => AudioDeclarations.Routes();

        public IReadOnlyList<MessageBufferDescriptor> Lanes() => AudioDeclarations.Lanes();

        public void BindReaders(CommandPayloadReaders readers) => AudioReaders.BindInto(readers);

        public IReadOnlyList<SpawnRecipe> Recipes()
        {
            var schemas = new List<SchemaRef> { AudioDeclarations.SessionRecipeSchema };
            var descriptor = new TargetDescriptor(
                AudioDeclarations.SessionRecipe,
                schemas,
                null,
                null,
                default(AssetAdapterDescriptor),
                null,
                null,
                null,
                null);
            return new[] { new SpawnRecipe(AudioDeclarations.SessionRecipe, descriptor, schemas, new AudioSessionApplier()) };
        }

        public IReadOnlyList<GameplayExtensionTarget> Targets(RegionManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            return new[] { new GameplayExtensionTarget("session", PresentationSlots.AudioSessionTarget(manifest.WorldId), AudioDeclarations.SessionRecipe) };
        }

        /// <summary>The region keys of the ambience regions present in <paramref name="manifest"/>.</summary>
        public List<int> ZonesOf(RegionManifest manifest)
        {
            var zones = new List<int>();
            for (int i = 0; i < Settings.AmbienceRegions.Count; i++)
            {
                ManifestRegion? region = manifest.FindRegion(Settings.AmbienceRegions[i]);
                if (region != null && !zones.Contains(region.key))
                {
                    zones.Add(region.key);
                }
            }

            zones.Sort();
            return zones;
        }

        public void Attach(GameApplicationRoot root, GameplayWorld world, bool seedSlots)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            RegionManifest manifest = world.Manifest;
            TargetId session = PresentationSlots.AudioSessionTarget(manifest.WorldId);
            List<int> zones = ZonesOf(manifest);
            var states = new List<int>(Settings.MusicStates);
            states.Sort();
            if (seedSlots)
            {
                ManifestRegion? start = manifest.FindRegion(manifest.StartRegionId);
                int zone = start != null ? AmbienceRules.ZoneForRegion(AmbienceRules.NoZone, start.key, zones) : AmbienceRules.NoZone;
                Seed(root, session, PresentationSlots.MusicState, states.Contains(Settings.StartMusicState) ? Settings.StartMusicState : MusicStateRules.Silence);
                Seed(root, session, PresentationSlots.AmbienceZone, zone);
                for (int channel = 0; channel < VolumeRules.ChannelCount; channel++)
                {
                    PresentationSlots.TryVolumeSlot(channel, out SlotId slot);
                    Seed(root, session, slot, Math.Max(0, Math.Min(VolumeRules.MaxPermille, Settings.Volumes[channel])));
                }
            }

            AudioCommandSystem? system = root.Host.EntityWorld.GetExistingSystemManaged<AudioCommandSystem>();
            if (system == null)
            {
                throw new InvalidOperationException("the audio command system is not registered in world " + root.Host.DiagnosticName);
            }

            var module = new AudioModule(root.Host, root.Registry, session, states, zones);
            system.Module = module;
            Module = module;
            Attached?.Invoke(world, module);
        }

        private static void Seed(GameApplicationRoot root, TargetId target, SlotId slot, int value)
        {
            if (!root.Seeder.TrySeedSlot(target, PresentationSlots.AudioOwner, slot, PresentationSlots.SchemaVersion, value, out DiagnosticCode code, out string detail))
            {
                throw new InvalidOperationException("seeding the audio session " + target + " failed: " + code + ": " + detail);
            }
        }
    }

    /// <summary>Submits typed audio commands for one world through its gameplay command issuer.</summary>
    public sealed class AudioCommandIssuer
    {
        public AudioCommandIssuer(GameplayWorld world)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Session = PresentationSlots.AudioSessionTarget(world.Manifest.WorldId);
        }

        public GameplayWorld World { get; }

        public TargetId Session { get; }

        /// <summary>audio.setMusicState{state, stinger}.</summary>
        public CommandAdmissionReceipt SetMusicState(string stateId, string stingerId = "") =>
            Submit(AudioDeclarations.SetMusicStateRoute, AudioDeclarations.SetMusicStateCommand,
                AudioCommandPayload.Encode(PresentationSlots.KeyOf(stateId), PresentationSlots.KeyOf(stingerId)));

        /// <summary>audio.setAmbienceZone{region key}.</summary>
        public CommandAdmissionReceipt SetAmbienceZone(int regionKey) =>
            Submit(AudioDeclarations.SetAmbienceZoneRoute, AudioDeclarations.SetAmbienceZoneCommand, AudioCommandPayload.Encode(regionKey));

        /// <summary>audio.setVolume{channel, permille}.</summary>
        public CommandAdmissionReceipt SetVolume(int channel, int permille) =>
            Submit(AudioDeclarations.SetVolumeRoute, AudioDeclarations.SetVolumeCommand, AudioCommandPayload.Encode(channel, permille));

        /// <summary>audio.playSfx{id, x, y, z mm}.</summary>
        public CommandAdmissionReceipt PlaySfx(string sfxId, int x = 0, int y = 0, int z = 0) =>
            Submit(AudioDeclarations.PlaySfxRoute, AudioDeclarations.PlaySfxCommand, AudioCommandPayload.Encode(PresentationSlots.KeyOf(sfxId), x, y, z));

        /// <summary>audio.playVoice{clip, speaker}.</summary>
        public CommandAdmissionReceipt PlayVoice(string clipId, string speakerId) =>
            Submit(AudioDeclarations.PlayVoiceRoute, AudioDeclarations.PlayVoiceCommand,
                AudioCommandPayload.Encode(PresentationSlots.KeyOf(clipId), PresentationSlots.KeyOf(speakerId)));

        /// <summary>audio.stopVoice.</summary>
        public CommandAdmissionReceipt StopVoice() =>
            Submit(AudioDeclarations.StopVoiceRoute, AudioDeclarations.StopVoiceCommand, AudioCommandPayload.Encode(0));

        /// <summary>The committed value of an audio slot, or <paramref name="fallback"/>.</summary>
        public int Read(SlotId slot, int fallback) =>
            World.Slots.TryRead(Session, PresentationSlots.AudioOwner, slot, out int value) ? value : fallback;

        private CommandAdmissionReceipt Submit(RouteId route, SchemaRef schema, FrozenPayload payload) =>
            World.Commands.Submit(route, Session, schema, payload);
    }
}
