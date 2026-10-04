// GameCore.Unity.App - the application root as the production restore builder's world composer (SADR-012 (studio)).
//
// A restored world is composed exactly like a booted one: the same catalog manifest source, schedule, registry,
// publisher, lane with its validate-before-commit preflight, pipeline, bridge and adapter frame, through the root's own
// `GameApplicationRoot.TryCompose`. Only three things differ, and they are all definition data:
//
//   * there is no boot script: the checkpoint, not the boot script, says which targets exist and what is installed;
//   * the lane seed is the captured scope tree (isolation and exclusions included), not the declared scope seeds;
//   * the target capacity is at least the captured target count.
//
// The root is not restructured: this file builds a definition variant with the root's own builder and composes it.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution.Time;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Delivery;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Persistence;

namespace GameCore.Unity.App
{
    /// <summary>
    /// Composes a restored world over <see cref="GameApplicationRoot.TryCompose"/> (SADR-010, SADR-012). The parts it
    /// returns carry the composed root as their owner, so the save service hands the restored root to the game.
    /// </summary>
    public sealed class SaveRestoreComposer : IProductionWorldComposer
    {
        private readonly GameApplicationDefinition definition;
        private readonly GameApplicationBootOptions bootOptions;
        private readonly Func<ProductionWorldModules> modulesFactory;
        private readonly Func<GameApplicationRoot, ProductionWorldModules, WorldDeliveryOwner?>? deliveryFactory;
        private CatalogManifestSource? manifests;
        private PipelineDescriptorReport? schedule;

        public SaveRestoreComposer(
            GameApplicationDefinition definition,
            GameApplicationBootOptions? bootOptions,
            Func<ProductionWorldModules> modulesFactory,
            Func<GameApplicationRoot, ProductionWorldModules, WorldDeliveryOwner?>? deliveryFactory = null)
        {
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
            this.bootOptions = bootOptions ?? new GameApplicationBootOptions
            {
                InstallPlayerLoop = false,
                AssignDefaultWorld = false,
            };
            this.modulesFactory = modulesFactory ?? throw new ArgumentNullException(nameof(modulesFactory));
            this.deliveryFactory = deliveryFactory;
        }

        public SpawnRecipeCatalog Recipes => definition.Recipes;

        public TemporalModel TemporalModel => definition.TemporalModel;

        /// <summary>The last failure of the root composition, when a compose refused.</summary>
        public GameApplicationBootFailed? LastFailure { get; private set; }

        public WorldCreateRequest CreateRequest(WorldId session) =>
            new WorldCreateRequest(
                session,
                definition.WorldDefinition,
                definition.TemporalModel,
                definition.Propagation,
                definition.CatalogHash,
                new OperationId(session, definition.Issuer, 1UL),
                definition.FixedStep);

        public UnityWorldRegistration CreateRegistration()
        {
            Prepare();
            return GameApplication.CreateRegistration(definition, schedule!);
        }

        public bool TryCompose(
            UnityWorldHost staging,
            ProductionComposeRequest request,
            out ProductionWorldParts? parts,
            out DiagnosticCode code,
            out string detail)
        {
            parts = null;
            Prepare();
            GameApplicationDefinition restoreDefinition = Rebase(definition, request.SeedScopes, request.TargetCapacity);
            GameApplicationRoot? root = GameApplicationRoot.TryCompose(
                restoreDefinition, staging, manifests!, schedule!, bootOptions, 1UL, out GameApplicationBootFailed? failure);
            if (root == null)
            {
                LastFailure = failure;
                code = failure != null ? failure.Diagnostic : DiagnosticCode.ApplyFault;
                detail = failure != null ? failure.ToString() : "the application root refused to compose the restored world";
                return false;
            }

            // The executor validates a running world (O-21); the root composed it paused at its first boundary.
            OperationResult resumed = root.Resume();
            if (resumed.Outcome == Outcome.Rejected)
            {
                root.Stop("restore compose refused");
                code = resumed.Code;
                detail = "the restored world could not be made Running for validation: " + DiagnosticCodeText.Of(resumed.Code);
                return false;
            }

            ProductionWorldModules modules = modulesFactory();
            if (deliveryFactory != null)
            {
                modules.Delivery = deliveryFactory(root, modules);
            }

            parts = new ProductionWorldParts(
                staging,
                root.Registry,
                root.Publisher,
                root.Targets,
                root.Seeder,
                root.Lane,
                (CompositionEditPayload payload, out DiagnosticCode editCode, out string editDetail) =>
                    ApplyThroughBridge(root, payload, out editCode, out editDetail),
                modules,
                root);
            code = DiagnosticCode.None;
            detail = string.Empty;
            return true;
        }

        public void Abandon(ProductionWorldParts parts)
        {
            if (parts?.Owner is GameApplicationRoot root && root.State != GameApplicationState.Stopped)
            {
                root.Stop("restore refused; the staging world is discarded");
            }
        }

        /// <summary>
        /// One replayed edit through the root's validate-before-commit bridge (SADR-011): executed is one publication,
        /// an admission the lane found already true is no change, anything else is a refusal with the bridge's code.
        /// </summary>
        private static ProductionEditResult ApplyThroughBridge(
            GameApplicationRoot root,
            CompositionEditPayload payload,
            out DiagnosticCode code,
            out string detail)
        {
            WorldAdmissionReport report = root.Submit(payload);
            if (report.Outcome == BridgeOutcome.Executed)
            {
                code = DiagnosticCode.None;
                detail = string.Empty;
                return ProductionEditResult.Published;
            }

            // The root's own boot rule: an admission the lane refused without a bridge refusal is an edit already true.
            if (report.Outcome == BridgeOutcome.AdmissionRejected && report.Refusal == null)
            {
                code = DiagnosticCode.None;
                detail = string.Empty;
                return ProductionEditResult.NoChange;
            }

            code = report.Refusal != null ? report.Refusal.Code : report.RefusalCode;
            if (code == DiagnosticCode.None)
            {
                code = report.AdmissionCode == DiagnosticCode.None ? DiagnosticCode.ApplyFault : report.AdmissionCode;
            }

            detail = report.Outcome + ": " + (report.Refusal != null ? report.Refusal.ToString() : report.RefusalDetail);
            return ProductionEditResult.Refused;
        }

        private void Prepare()
        {
            if (manifests != null && schedule != null)
            {
                return;
            }

            if (!GameApplication.TryPrepare(definition, out CatalogManifestSource? preparedManifests, out PipelineDescriptorReport? preparedSchedule, out GameApplicationBootFailed? failure)
                || preparedManifests == null
                || preparedSchedule == null)
            {
                LastFailure = failure;
                throw new InvalidOperationException(
                    "the game definition " + definition.Name + " does not prepare: " + (failure != null ? failure.ToString() : "unknown"));
            }

            manifests = preparedManifests;
            schedule = preparedSchedule;
        }

        /// <summary>
        /// The definition a restored world is composed from: the game's definition with no boot script, the captured
        /// scope tree as its scope seeds and a target capacity of at least <paramref name="targetCapacity"/>.
        /// </summary>
        internal static GameApplicationDefinition Rebase(
            GameApplicationDefinition source,
            IReadOnlyList<ScopeRecord> scopes,
            int targetCapacity)
        {
            var builder = new GameApplicationDefinition.Builder(source.Name)
                .WithCatalog(source.Catalog, source.CatalogHash)
                .WithWorld(source.WorldDefinition, source.TemporalModel, source.FixedStep)
                .WithPropagation(source.Propagation)
                .WithRootScope(source.RootScope)
                .WithDispatchKinds(source.DispatchKinds)
                .WithSlotPolicyMigrations(source.SlotPolicyMigrations)
                .WithRecipes(source.Recipes)
                .WithValues(source.Values)
                .WithBudget(source.Budget, source.StagedByteCeiling)
                .WithTargetCapacity(Math.Max(source.TargetCapacity, targetCapacity))
                .WithIssuer(source.Issuer)
                .WithProvenance(source.Provenance);

            builder.PluginList.AddRange(source.Plugins);
            builder.ScopeSeedList.AddRange(scopes);
            builder.SystemList.AddRange(source.Systems);
            builder.MigrationList.AddRange(source.Migrations);
            builder.RuleKeyList.AddRange(source.RuleKeys);
            builder.OverrideList.AddRange(source.Overrides);
            builder.ConfigBindingList.AddRange(source.ConfigBindings);

            if (source.Messages != null)
            {
                builder.WithMessages(source.Messages, source.MessageReaders);
            }

            if (source.SeedWorldState != null)
            {
                builder.WithSeedWorldState(source.SeedWorldState);
            }

            if (source.RestoreHook != null)
            {
                builder.WithRestoreHook(source.RestoreHook);
            }

            if (source.AdapterFrame != null)
            {
                builder.WithAdapterFrame(source.AdapterFrame);
            }

            return builder.Build();
        }
    }
}
