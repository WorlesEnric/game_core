// GameCore.Gameplay.Logic - composing the narrative plugins into a baked gameplay world (P1.4).
//
// WorldBuilder (P1.1) builds the application definition of the entities and world plugins and has no extension point.
// The composer leaves it untouched: it builds the world plan as usual, then copies the plan's definition into a new
// builder and adds what every narrative module declares (plugin, system, routes, lanes, readers, recipes, one seed
// step per narrative target before the first mount, one mount step per plugin after P1.1's), with one composite
// dispatch-kind table and a larger message plane. After boot WorldBuilder.Attach runs unchanged (it only reads the
// plan's manifest, frame and issuer), then each module seeds its slots and hands its command system its module, and
// the narrative host (event routing, quest tracking, rule triggers, delivery) joins the world's input phase.
//
// Modules are passed in by the game (or a test); nothing is discovered by reflection and nothing is static.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Unity.App;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;
using GameCore.Unity.Runtime.Time;
using Unity.Entities;
using Seams = GameCore.Gameplay.Contracts.Narrative;

namespace GameCore.Gameplay.Logic
{
    /// <summary>One narrative package's part of a world: its converter, its declarations and its attach step.</summary>
    public interface INarrativeModule
    {
        string Name { get; }

        /// <summary>The converter of the package's definition kinds (null when it has none).</summary>
        INarrativeContentConverter? Converter { get; }

        /// <summary>Declares the package's plugin and targets (before boot; the models are known).</summary>
        void Declare(NarrativeComposition composition);

        /// <summary>Seeds the package's slots and attaches its command system (after boot, world still paused).</summary>
        void Attach(NarrativeRuntime runtime, bool seedSlots);
    }

    /// <summary>One narrative target to seed at boot.</summary>
    public sealed class NarrativeSeed
    {
        public NarrativeSeed(TargetId target, DefinitionRef recipe, string name)
        {
            Target = target;
            Recipe = recipe;
            Name = name;
        }

        public TargetId Target { get; }

        public DefinitionRef Recipe { get; }

        public string Name { get; }
    }

    /// <summary>What the modules declare before boot.</summary>
    public sealed class NarrativeComposition
    {
        private readonly List<NarrativePluginSpec> plugins = new List<NarrativePluginSpec>();
        private readonly List<SystemRegistration> systems = new List<SystemRegistration>();
        private readonly List<SpawnRecipe> recipes = new List<SpawnRecipe>();
        private readonly List<NarrativeSeed> seeds = new List<NarrativeSeed>();
        private readonly HashSet<TargetId> seeded = new HashSet<TargetId>();

        public NarrativeComposition(RegionManifest world, GameplayContentManifest content, NarrativeModelSet models, NarrativeIndex index)
        {
            World = world;
            Content = content;
            Models = models;
            Index = index;
        }

        public RegionManifest World { get; }

        public GameplayContentManifest Content { get; }

        public NarrativeModelSet Models { get; }

        public NarrativeIndex Index { get; }

        public IReadOnlyList<NarrativePluginSpec> Plugins => plugins;

        public IReadOnlyList<SystemRegistration> Systems => systems;

        public IReadOnlyList<SpawnRecipe> Recipes => recipes;

        public IReadOnlyList<NarrativeSeed> Seeds => seeds;

        public void AddPlugin(NarrativePluginSpec spec, SystemRegistration system)
        {
            plugins.Add(spec ?? throw new ArgumentNullException(nameof(spec)));
            systems.Add(system ?? throw new ArgumentNullException(nameof(system)));
        }

        public void AddRecipe(SpawnRecipe recipe)
        {
            for (int i = 0; i < recipes.Count; i++)
            {
                if (recipes[i].Recipe.Equals(recipe.Recipe))
                {
                    return;
                }
            }

            recipes.Add(recipe);
        }

        public void AddSeed(TargetId target, DefinitionRef recipe, string name)
        {
            if (!target.IsDefault && seeded.Add(target))
            {
                seeds.Add(new NarrativeSeed(target, recipe, name));
            }
        }

        /// <summary>The narrative plugins added to a copy of <paramref name="baseDefinition"/>.</summary>
        public GameApplicationDefinition Extend(GameApplicationDefinition baseDefinition)
        {
            GameApplicationDefinition b = baseDefinition ?? throw new ArgumentNullException(nameof(baseDefinition));
            var builder = new GameApplicationDefinition.Builder(b.Name).WithCatalog(b.Catalog, b.CatalogHash);
            for (int i = 0; i < b.Plugins.Count; i++)
            {
                builder.AddPlugin(b.Plugins[i]);
            }

            var declarations = new List<CatalogPluginDeclaration>();
            for (int i = 0; i < plugins.Count; i++)
            {
                var declaration = new CatalogPluginDeclaration(plugins[i].Manifest(), ConfigDocument.Empty);
                declarations.Add(declaration);
                builder.AddPlugin(declaration);
            }

            builder.WithWorld(b.WorldDefinition, b.TemporalModel, b.FixedStep)
                .WithPropagation(b.Propagation)
                .WithRootScope(b.RootScope);
            for (int i = 0; i < b.ScopeSeeds.Count; i++)
            {
                builder.AddScopeSeed(b.ScopeSeeds[i]);
            }

            for (int i = 0; i < b.Systems.Count; i++)
            {
                builder.AddSystem(b.Systems[i]);
            }

            var kinds = new ScheduleDispatchKindTable();
            for (int i = 0; i < systems.Count; i++)
            {
                builder.AddSystem(systems[i]);
                kinds.Add(plugins[i].CommandSystem, SystemDispatchKind.ManagedSystem);
            }

            builder.WithDispatchKinds(new CompositeDispatchKinds(b.DispatchKinds, kinds));
            builder.WithSlotPolicyMigrations(b.SlotPolicyMigrations);
            for (int i = 0; i < b.Migrations.Count; i++)
            {
                builder.AddMigration(b.Migrations[i]);
            }

            var routes = new List<CommandRoute>();
            var lanes = new List<MessageBufferDescriptor>();
            var readPorts = new List<BufferReadPort>();
            int pending = 64;
            int results = 64;
            int retained = 512;
            int perStep = 32;
            int nextStep = 4;
            if (b.Messages != null)
            {
                routes.AddRange(b.Messages.Routes);
                lanes.AddRange(b.Messages.Buffers);
                readPorts.AddRange(b.Messages.ReadPorts);
                pending = b.Messages.MaxPendingRequests;
                results = b.Messages.MaxRetainedResults;
                retained = b.Messages.MaxRetainedEvents;
                perStep = b.Messages.MaxEventsPerStep;
                nextStep = b.Messages.NextStepCapacity;
            }

            CommandPayloadReaders readers = b.MessageReaders ?? new CommandPayloadReaders();
            for (int i = 0; i < plugins.Count; i++)
            {
                routes.AddRange(plugins[i].Routes());
                lanes.AddRange(plugins[i].Lanes());
                plugins[i].BindReaders(readers);
            }

            var plane = new MessagePlaneRegistration(
                routes,
                lanes,
                readPorts,
                Math.Max(pending, 128),
                Math.Max(results, 256),
                Math.Max(retained, 1024),
                Math.Max(perStep, 64),
                (ushort)Math.Max(nextStep, 8));
            builder.WithMessages(plane, readers);
            if (b.SeedWorldState != null)
            {
                builder.WithSeedWorldState(b.SeedWorldState);
            }

            var allRecipes = new List<SpawnRecipe>(b.Recipes.Recipes);
            allRecipes.AddRange(recipes);
            builder.WithRecipes(new SpawnRecipeCatalog(allRecipes))
                .WithValues(b.Values);
            for (int i = 0; i < b.RuleKeys.Count; i++)
            {
                builder.AddRuleKeys(b.RuleKeys[i]);
            }

            for (int i = 0; i < b.Overrides.Count; i++)
            {
                builder.AddOverride(b.Overrides[i]);
            }

            for (int i = 0; i < b.ConfigBindings.Count; i++)
            {
                builder.BindRuleToConfig(b.ConfigBindings[i].Rule, b.ConfigBindings[i].ConfigField);
            }

            builder.WithBudget(b.Budget, b.StagedByteCeiling)
                .WithTargetCapacity(b.TargetCapacity + seeds.Count + 32)
                .WithIssuer(b.Issuer)
                .WithProvenance(b.Provenance);
            if (b.RestoreHook != null)
            {
                builder.WithRestoreHook(b.RestoreHook);
            }

            if (b.AdapterFrame != null)
            {
                builder.WithAdapterFrame(b.AdapterFrame);
            }

            // Seeds before the first mount (P1.1's seeds, then the narrative targets), then P1.1's mounts, then ours.
            int firstEdit = b.BootSteps.Count;
            for (int i = 0; i < b.BootSteps.Count; i++)
            {
                if (b.BootSteps[i].Kind == GameApplicationBootStepKind.Edit)
                {
                    firstEdit = i;
                    break;
                }
            }

            for (int i = 0; i < firstEdit; i++)
            {
                builder.AddBootStep(b.BootSteps[i]);
            }

            for (int i = 0; i < seeds.Count; i++)
            {
                builder.AddBootStep(GameApplicationBootStep.Seed("seed-narrative:" + seeds[i].Name, seeds[i].Target, b.RootScope, seeds[i].Recipe));
            }

            for (int i = firstEdit; i < b.BootSteps.Count; i++)
            {
                builder.AddBootStep(b.BootSteps[i]);
            }

            for (int i = 0; i < plugins.Count; i++)
            {
                builder.AddBootStep(GameApplicationBootStep.Apply("mount-" + plugins[i].Stem, WorldBuilder.Mount(declarations[i], plugins[i].Instance, b.RootScope)));
            }

            return builder.Build();
        }
    }

    /// <summary>The narrative side of one running world, shared by the four modules.</summary>
    public sealed class NarrativeRuntime
    {
        private readonly List<INarrativeEventListener> listeners = new List<INarrativeEventListener>();
        private readonly List<IPresentationBinder> presenters = new List<IPresentationBinder>();

        public NarrativeRuntime(GameApplicationRoot root, GameplayWorld world, RegionManifest manifest, GameplayContentManifest content, NarrativeModelSet models, NarrativeIndex index)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            World = world ?? throw new ArgumentNullException(nameof(world));
            Manifest = manifest;
            Content = content;
            Models = models;
            Index = index;
            State = new NarrativeState(world.Slots, index, models, root.Host);
            Submitter = new NarrativeSubmitter(root.Host, GameplayIds.Id("gameplay.issuer.narrative." + manifest.WorldId));
            Explain = new ExplainTrace(models);
            Delivery = new NarrativeDelivery(this);
        }

        public GameApplicationRoot Root { get; }

        public GameplayWorld World { get; }

        public UnityWorldHost Host => Root.Host;

        public TargetRegistry Registry => Root.Registry;

        public RegionManifest Manifest { get; }

        public GameplayContentManifest Content { get; }

        public NarrativeModelSet Models { get; }

        public NarrativeIndex Index { get; }

        public NarrativeState State { get; }

        public ICommittedSlotReader Slots => World.Slots;

        public NarrativeSubmitter Submitter { get; }

        public ExplainTrace Explain { get; }

        public NarrativeDelivery Delivery { get; }

        public INarrativeFeedbackSink Feedback { get; private set; } = new NullNarrativeFeedbackSink();

        public INarrativeMessageSink Messages { get; private set; } = new NullNarrativeMessageSink();

        /// <summary>The player's entity key (the world's focus entity).</summary>
        public int ActorKey => Index.FocusEntityKey;

        public EntityManager EntityManager => Host.EntityWorld.EntityManager;

        public IReadOnlyList<INarrativeEventListener> Listeners => listeners;

        public IReadOnlyList<IPresentationBinder> Presenters => presenters;

        /// <summary>Replaces the playAudio sink (P1.5).</summary>
        public void UseFeedback(INarrativeFeedbackSink sink) => Feedback = sink ?? throw new ArgumentNullException(nameof(sink));

        /// <summary>Replaces the showMessage sink (P1.5).</summary>
        public void UseMessages(INarrativeMessageSink sink) => Messages = sink ?? throw new ArgumentNullException(nameof(sink));

        public void AddListener(INarrativeEventListener listener) => listeners.Add(listener ?? throw new ArgumentNullException(nameof(listener)));

        /// <summary>Adds a presenter to the world's presentation phase (after each pump; committed slots only).</summary>
        public void AddPresenter(IPresentationBinder presenter)
        {
            presenters.Add(presenter ?? throw new ArgumentNullException(nameof(presenter)));
            World.AddBinder(presenter);
        }

        /// <summary>Seeds one owned slot while the world is paused (boot), like WorldBuilder.Attach.</summary>
        public void Seed(TargetId target, OwnerId owner, SlotId slot, int value)
        {
            if (!Root.Seeder.TrySeedSlot(target, owner, slot, GameplaySlots.SchemaVersion, value, out DiagnosticCode code, out string detail))
            {
                throw new InvalidOperationException("seeding narrative target " + target + " failed: " + code + ": " + detail);
            }
        }

        /// <summary>The command system of type <typeparamref name="T"/> registered in this world.</summary>
        public T System<T>()
            where T : SystemBase
        {
            T? system = Host.EntityWorld.GetExistingSystemManaged<T>();
            if (system == null)
            {
                throw new InvalidOperationException(typeof(T).Name + " is not registered in world " + Host.DiagnosticName);
            }

            return system;
        }
    }

    /// <summary>The booted narrative world: the gameplay world plus the narrative services.</summary>
    public sealed class NarrativeWorld
    {
        public NarrativeWorld(GameplayWorld world, NarrativeRuntime runtime, NarrativeHost host)
        {
            World = world;
            Runtime = runtime;
            Host = host;
            Conditions = new NarrativeConditionEvaluator(runtime);
            Actions = new NarrativeActionRunner(runtime);
        }

        public GameplayWorld World { get; }

        public NarrativeRuntime Runtime { get; }

        public NarrativeHost Host { get; }

        public GameApplicationRoot Root => World.Root;

        /// <summary>IConditionEvaluator over committed state (P1.3's interactables call it).</summary>
        public NarrativeConditionEvaluator Conditions { get; }

        /// <summary>IActionRunner (P1.3's interactables call it after a committed use).</summary>
        public NarrativeActionRunner Actions { get; }

        /// <summary>IConversationStarter, set by the dialogue module (null-object until then).</summary>
        public Seams.IConversationStarter Conversations { get; set; } = new Seams.NullConversationStarter();

        public IExplainSource Explain => Runtime.Explain;

        public NarrativeDelivery Delivery => Runtime.Delivery;

        /// <summary>Disposes the delivery owner, detaches the world and stops the root.</summary>
        public void Shutdown()
        {
            Runtime.Delivery.Dispose();
            World.Shutdown();
            Root.Stop("narrative world shut down");
        }
    }

    /// <summary>Boots a baked world with narrative modules.</summary>
    public static class NarrativeComposer
    {
        /// <summary>
        /// Builds the world plan, adds the modules' plugins, boots, attaches the world and the modules and installs the
        /// narrative host. The root is left Ready (paused) unless <paramref name="start"/> is true.
        /// </summary>
        public static NarrativeWorld Boot(
            RegionManifest manifest,
            GameplayContentManifest content,
            IReadOnlyList<INarrativeModule> modules,
            GameApplicationBootOptions? options,
            WorldBuildOptions? build,
            bool start)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            if (!string.Equals(content.FormatId, GameplayContentManifest.Format, StringComparison.Ordinal)
                || !string.Equals(content.WorldId, manifest.WorldId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(NarrativeDiagnosticCodes.ContentStale
                    + ": the content manifest is not a bake of world " + manifest.WorldId + "; re-run GameCore.Gameplay.Compile.Entry.Bake");
            }

            if (!GameplayCatalog.TryBuild(manifest.CatalogTypeName, out ICatalog? catalog, out ContentHash fingerprint, out string detail) || catalog == null)
            {
                throw new InvalidOperationException(detail);
            }

            WorldBuildPlan plan = WorldBuilder.Build(manifest, catalog, fingerprint, build);
            var converters = new List<INarrativeContentConverter>();
            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i].Converter != null)
                {
                    converters.Add(modules[i].Converter!);
                }
            }

            NarrativeModelSet models = NarrativeContent.Build(content, converters);
            var index = new NarrativeIndex(manifest, content, models);
            var composition = new NarrativeComposition(manifest, content, models, index);
            for (int i = 0; i < modules.Count; i++)
            {
                modules[i].Declare(composition);
            }

            GameApplicationDefinition definition = composition.Extend(plan.Definition);
            GameApplicationBootOptions effective = options ?? new GameApplicationBootOptions();
            bool startLater = effective.StartImmediately;
            effective.StartImmediately = false;
            if (!GameApplication.TryBoot(definition, effective, out GameApplicationRoot? root, out GameApplicationBootFailed? failure) || root == null)
            {
                throw new InvalidOperationException("the narrative world refused to boot: " + (failure != null ? failure.ToString() : "no root"));
            }

            NarrativeWorld narrative;
            try
            {
                GameplayWorld world = WorldBuilder.Attach(root, plan);
                var runtime = new NarrativeRuntime(root, world, manifest, content, models, index);
                for (int i = 0; i < modules.Count; i++)
                {
                    modules[i].Attach(runtime, true);
                }

                var host = new NarrativeHost(runtime);
                world.AddInput(host);
                narrative = new NarrativeWorld(world, runtime, host);
                for (int i = 0; i < modules.Count; i++)
                {
                    if (modules[i] is INarrativeWorldAware aware)
                    {
                        aware.OnWorld(narrative);
                    }
                }
            }
            catch (Exception)
            {
                root.Stop("narrative attach failed");
                throw;
            }

            if (start || startLater)
            {
                OperationResult started = root.Start();
                if (started.Outcome == Outcome.Rejected)
                {
                    root.Stop("narrative start refused");
                    throw new InvalidOperationException("the narrative world refused to start: " + started.Code);
                }
            }

            return narrative;
        }
    }

    /// <summary>A module that wants the finished narrative world (to publish its services, e.g. the conversation starter).</summary>
    public interface INarrativeWorldAware
    {
        void OnWorld(NarrativeWorld world);
    }
}
