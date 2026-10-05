// GameCore.Gameplay.Ui - the UI plugin as a world extension, and the typed UI command issuer (P1.5).
//
// UiWorldExtension joins the UI plugin to a gameplay world build (WorldBuildOptions.Extensions): the UI session target
// is seeded under the world scope, the plugin is mounted at the world scope, and Attach seeds ui.screen with the flow's
// start screen (unless the root was composed by a restore), hands the command system its module and raises Attached so
// the UI runtime rebinds its presentation to the (new) world.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Ui;
using GameCore.Unity.App;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;

namespace GameCore.Gameplay.Ui
{
    /// <summary>The UI plugin mounted with a gameplay world.</summary>
    public sealed class UiWorldExtension : IGameplayWorldExtension, IGameplayWorldTargets
    {
        private readonly GameplayPluginMount[] plugins;
        private readonly GameplaySystem[] systems;

        public UiWorldExtension(UiScreen startScreen)
        {
            StartScreen = startScreen;
            plugins = new[] { new GameplayPluginMount(new CatalogPluginDeclaration(UiDeclarations.Manifest(), null), UiDeclarations.Instance) };
            systems = new[] { new GameplaySystem(UiDeclarations.CommandSystem, UiDeclarations.CommandSystemRegistration()) };
        }

        /// <summary>The screen a freshly booted world starts on (the main menu in a game, the HUD in some tests).</summary>
        public UiScreen StartScreen { get; set; }

        /// <summary>Raised after every attach (boot and restore) with the world and its UI module.</summary>
        public event Action<GameplayWorld, UiModule>? Attached;

        /// <summary>The module of the most recent attach; null before the first boot.</summary>
        public UiModule? Module { get; private set; }

        public string Name => "ui";

        public IReadOnlyList<GameplayPluginMount> Plugins => plugins;

        public IReadOnlyList<GameplaySystem> Systems => systems;

        public IReadOnlyList<CommandRoute> Routes => UiDeclarations.Routes();

        public IReadOnlyList<MessageBufferDescriptor> Lanes => UiDeclarations.Lanes();

        public void BindReaders(CommandPayloadReaders readers) => UiReaders.BindInto(readers);

        public IReadOnlyList<SpawnRecipe> Recipes()
        {
            var schemas = new List<SchemaRef> { UiDeclarations.SessionRecipeSchema };
            var descriptor = new TargetDescriptor(
                UiDeclarations.SessionRecipe,
                schemas,
                null,
                null,
                default(AssetAdapterDescriptor),
                null,
                null,
                null,
                null);
            return new[] { new SpawnRecipe(UiDeclarations.SessionRecipe, descriptor, schemas, new UiSessionApplier()) };
        }

        public IReadOnlyList<GameplayExtensionTarget> Targets(RegionManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            return new[] { new GameplayExtensionTarget("session", PresentationSlots.UiSessionTarget(manifest.WorldId), UiDeclarations.SessionRecipe) };
        }

        /// <summary>Raised when the world shuts down (GameplayWorld.Shutdown), before its root stops.</summary>
        public event Action<GameplayWorld>? Detached;

        public void Validate(RegionManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }
        }

        public void Detach(GameplayWorld world) => Detached?.Invoke(world);

        public void Attach(GameplayWorld world, bool seedSlots)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            GameApplicationRoot root = world.Root;

            TargetId session = PresentationSlots.UiSessionTarget(world.Manifest.WorldId);
            if (seedSlots)
            {
                Seed(root, session, PresentationSlots.Screen, (int)StartScreen);
                Seed(root, session, PresentationSlots.ReturnTo, (int)UiScreen.None);
                Seed(root, session, PresentationSlots.Message, 0);
            }

            UiCommandSystem? system = root.Host.EntityWorld.GetExistingSystemManaged<UiCommandSystem>();
            if (system == null)
            {
                throw new InvalidOperationException("the UI command system is not registered in world " + root.Host.DiagnosticName);
            }

            var module = new UiModule(root.Host, root.Registry, session);
            system.Module = module;
            Module = module;
            Attached?.Invoke(world, module);
        }

        private static void Seed(GameApplicationRoot root, TargetId target, SlotId slot, int value)
        {
            if (!root.Seeder.TrySeedSlot(target, PresentationSlots.UiOwner, slot, PresentationSlots.SchemaVersion, value, out DiagnosticCode code, out string detail))
            {
                throw new InvalidOperationException("seeding the UI session " + target + " failed: " + code + ": " + detail);
            }
        }
    }

    /// <summary>Submits typed UI commands for one world through its gameplay command issuer.</summary>
    public sealed class UiCommandIssuer
    {
        public UiCommandIssuer(GameplayWorld world)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Session = PresentationSlots.UiSessionTarget(world.Manifest.WorldId);
        }

        public GameplayWorld World { get; }

        public TargetId Session { get; }

        /// <summary>ui.open{screen}.</summary>
        public CommandAdmissionReceipt Open(UiScreen screen) =>
            World.Commands.Submit(UiDeclarations.OpenRoute, Session, UiDeclarations.OpenCommand, UiCommandPayload.EncodeOne((int)screen));

        /// <summary>ui.close.</summary>
        public CommandAdmissionReceipt Close() =>
            World.Commands.Submit(UiDeclarations.CloseRoute, Session, UiDeclarations.CloseCommand, UiCommandPayload.EncodeOne(0));

        /// <summary>ui.command{action, argument}.</summary>
        public CommandAdmissionReceipt Command(UiAction action, int argument = 0) =>
            World.Commands.Submit(UiDeclarations.UiCommandRoute, Session, UiDeclarations.CommandCommand, UiCommandPayload.EncodeTwo((int)action, argument));

        /// <summary>The committed UI state of the world (None/None/0 before the session is seeded).</summary>
        public UiState State()
        {
            ICommittedSlotReader slots = World.Slots;
            int screen = slots.TryRead(Session, PresentationSlots.UiOwner, PresentationSlots.Screen, out int s) ? s : 0;
            int returnTo = slots.TryRead(Session, PresentationSlots.UiOwner, PresentationSlots.ReturnTo, out int r) ? r : 0;
            int message = slots.TryRead(Session, PresentationSlots.UiOwner, PresentationSlots.Message, out int m) ? m : 0;
            return new UiState(screen, returnTo, message);
        }
    }
}
