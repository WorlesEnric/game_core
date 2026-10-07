#nullable enable
// Hollowmere.Mechanism.PressurePlate - the mechanism's composition surface (W-MECH-01 sample).
//
// A mechanism package extends a world's application definition instead of regenerating it:
//
//   GameApplicationDefinition extended = PressurePlateMechanism.Extend(plan.Definition);
//   GameApplicationRoot root = GameApplication.Boot(extended, options);
//   GameplayWorld world = WorldBuilder.Attach(root, plan);          // the world's own modules
//   PressurePlateWorld plates = PressurePlateMechanism.Attach(root); // the plate module
//
// Extend copies every public property of the base definition and adds exactly the plate's contributions: the composite
// catalog (world catalog + the plate's own generated catalog, fingerprint = CatalogSet.Combine), the plate plugin and its
// command system, its dispatch kind, its route and lane (on a rebuilt message plane that keeps the base limits), its
// payload reader, its recipe and the boot step that mounts the plugin at the base root scope.
//
// Open item (documented, not solved here): WorldBuilder has no extension seam, so Extend works on the built definition.
// A WorldBuilder seam (WorldBuildOptions.Extensions applied inside Build) would let the world's own plan carry the
// mechanism, so plan.Definition and the booted definition could not drift.
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.World;
using GameCore.Unity.App;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;
using GameCore.Unity.Runtime.Time;
using Hollowmere.Mechanism.PressurePlate.Generated;

namespace Hollowmere.Mechanism.PressurePlate
{
    /// <summary>Catalog, definition extension and attachment of the pressure plate mechanism.</summary>
    public static class PressurePlateMechanism
    {
        /// <summary>Boot step name of the plate plugin mount.</summary>
        public const string MountStepName = "mount-pressureplate";

        /// <summary>The mechanism's own generated catalog; throws when it does not build or its fingerprint drifted.</summary>
        public static ICatalog Catalog()
        {
            CatalogBuildResult result = PressurePlateCatalog.BuildCatalog();
            if (result.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the pressure plate catalog does not build: " + result.Diagnostics.Count + " diagnostic(s)");
            }

            if (!string.Equals(result.Catalog.Fingerprint.ToHex(), PressurePlateCatalog.CatalogFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "the pressure plate catalog fingerprint " + result.Catalog.Fingerprint.ToHex()
                    + " differs from the generated constant " + PressurePlateCatalog.CatalogFingerprint);
            }

            return result.Catalog;
        }

        /// <summary>The plate plugin declaration (schema defaults, no local configuration).</summary>
        public static CatalogPluginDeclaration Declaration() =>
            new CatalogPluginDeclaration(PressurePlateDeclarations.Manifest(), ConfigDocument.Empty);

        /// <summary>
        /// A copy of <paramref name="baseDefinition"/> with the pressure plate mechanism composed in (see the file header).
        /// The base definition is not changed, except that its payload reader table gains the plate.press reader when it
        /// has one: the table cannot be enumerated or copied, so the extended definition shares it (binding is idempotent).
        /// </summary>
        public static GameApplicationDefinition Extend(GameApplicationDefinition baseDefinition)
        {
            if (baseDefinition == null)
            {
                throw new ArgumentNullException(nameof(baseDefinition));
            }

            for (int i = 0; i < baseDefinition.Plugins.Count; i++)
            {
                if (baseDefinition.Plugins[i].Manifest.PluginTypeId.Equals(PressurePlateDeclarations.PluginType))
                {
                    throw new InvalidOperationException("the definition " + baseDefinition.Name + " already composes the pressure plate");
                }
            }

            var composite = new CompositeCatalog(baseDefinition.Catalog, new[] { Catalog() });
            CatalogPluginDeclaration declaration = Declaration();

            var builder = new GameApplicationDefinition.Builder(baseDefinition.Name)
                .WithCatalog(composite, composite.Fingerprint)
                .WithWorld(baseDefinition.WorldDefinition, baseDefinition.TemporalModel, baseDefinition.FixedStep)
                .WithPropagation(baseDefinition.Propagation)
                .WithRootScope(baseDefinition.RootScope)
                .WithDispatchKinds(new ExtendedDispatchKinds(baseDefinition.DispatchKinds))
                .WithSlotPolicyMigrations(baseDefinition.SlotPolicyMigrations)
                .WithMessages(ExtendMessages(baseDefinition.Messages), ExtendReaders(baseDefinition.MessageReaders))
                .WithRecipes(ExtendRecipes(baseDefinition.Recipes))
                .WithValues(baseDefinition.Values)
                .WithBudget(baseDefinition.Budget, baseDefinition.StagedByteCeiling)
                .WithTargetCapacity(baseDefinition.TargetCapacity)
                .WithIssuer(baseDefinition.Issuer)
                .WithProvenance(baseDefinition.Provenance);

            for (int i = 0; i < baseDefinition.Plugins.Count; i++)
            {
                builder.AddPlugin(baseDefinition.Plugins[i]);
            }

            builder.AddPlugin(declaration);

            for (int i = 0; i < baseDefinition.ScopeSeeds.Count; i++)
            {
                builder.AddScopeSeed(baseDefinition.ScopeSeeds[i]);
            }

            for (int i = 0; i < baseDefinition.Systems.Count; i++)
            {
                builder.AddSystem(baseDefinition.Systems[i]);
            }

            builder.AddSystem(PressurePlateDeclarations.CommandSystemRegistration());

            for (int i = 0; i < baseDefinition.Migrations.Count; i++)
            {
                builder.AddMigration(baseDefinition.Migrations[i]);
            }

            if (baseDefinition.SeedWorldState != null)
            {
                builder.WithSeedWorldState(baseDefinition.SeedWorldState);
            }

            for (int i = 0; i < baseDefinition.RuleKeys.Count; i++)
            {
                builder.AddRuleKeys(baseDefinition.RuleKeys[i]);
            }

            for (int i = 0; i < baseDefinition.Overrides.Count; i++)
            {
                builder.AddOverride(baseDefinition.Overrides[i]);
            }

            for (int i = 0; i < baseDefinition.ConfigBindings.Count; i++)
            {
                RuleConfigBinding binding = baseDefinition.ConfigBindings[i];
                builder.BindRuleToConfig(binding.Rule, binding.ConfigField);
            }

            for (int i = 0; i < baseDefinition.BootSteps.Count; i++)
            {
                builder.AddBootStep(baseDefinition.BootSteps[i]);
            }

            builder.AddBootStep(GameApplicationBootStep.Apply(
                MountStepName, WorldBuilder.Mount(declaration, PressurePlateDeclarations.Instance, baseDefinition.RootScope)));

            if (baseDefinition.RestoreHook != null)
            {
                builder.WithRestoreHook(baseDefinition.RestoreHook);
            }

            if (baseDefinition.AdapterFrame != null)
            {
                builder.WithAdapterFrame(baseDefinition.AdapterFrame);
            }

            return builder.Build();
        }

        /// <summary>
        /// Hands the booted world's plate command system its per-world module and returns the plate surface. Call once,
        /// right after boot (the world may still be paused).
        /// </summary>
        public static PressurePlateWorld Attach(GameApplicationRoot root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            PressurePlateCommandSystem? system = root.Host.EntityWorld.GetExistingSystemManaged<PressurePlateCommandSystem>();
            if (system == null)
            {
                throw new InvalidOperationException(
                    "the pressure plate command system is not registered in world " + root.Host.DiagnosticName
                    + "; boot a definition returned by PressurePlateMechanism.Extend");
            }

            if (system.Module != null)
            {
                throw new InvalidOperationException("the pressure plate module is already attached to world " + root.Host.DiagnosticName);
            }

            var module = new PlateModule(root.Host, root.Registry);
            system.Module = module;
            return new PressurePlateWorld(root, module);
        }

        private static MessagePlaneRegistration ExtendMessages(MessagePlaneRegistration? source)
        {
            var routes = new List<CommandRoute>();
            var lanes = new List<MessageBufferDescriptor>();
            IReadOnlyList<BufferReadPort>? ports = null;
            if (source != null)
            {
                routes.AddRange(source.Routes);
                lanes.AddRange(source.Buffers);
                ports = source.ReadPorts;
            }

            routes.AddRange(PressurePlateDeclarations.Routes());
            lanes.AddRange(PressurePlateDeclarations.Lanes());
            if (source == null)
            {
                return new MessagePlaneRegistration(routes, lanes, null, 64, 64, 512, 32, 4);
            }

            return new MessagePlaneRegistration(
                routes,
                lanes,
                ports,
                source.MaxPendingRequests,
                source.MaxRetainedResults,
                source.MaxRetainedEvents,
                source.MaxEventsPerStep,
                source.NextStepCapacity);
        }

        private static CommandPayloadReaders ExtendReaders(CommandPayloadReaders? source)
        {
            CommandPayloadReaders readers = source ?? new CommandPayloadReaders();
            PlateReaders.BindInto(readers);
            return readers;
        }

        private static SpawnRecipeCatalog ExtendRecipes(SpawnRecipeCatalog source)
        {
            var recipes = new List<SpawnRecipe>(source.Recipes);
            recipes.Add(PlateRecipes.Create());
            return new SpawnRecipeCatalog(recipes);
        }

        /// <summary>The base dispatch kinds plus the plate command system (a managed system).</summary>
        private sealed class ExtendedDispatchKinds : IScheduleDispatchKindResolver
        {
            private readonly IScheduleDispatchKindResolver inner;

            internal ExtendedDispatchKinds(IScheduleDispatchKindResolver inner)
            {
                this.inner = inner;
            }

            public bool TryResolveKind(FactoryKey systemKey, out SystemDispatchKind kind)
            {
                if (systemKey.Equals(PressurePlateDeclarations.CommandSystem))
                {
                    kind = SystemDispatchKind.ManagedSystem;
                    return true;
                }

                return inner.TryResolveKind(systemKey, out kind);
            }
        }
    }
}
