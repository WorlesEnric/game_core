// GameCore.Unity.Runtime - the production O-21 restore builder (SADR-012 (studio)).
//
// Normative sources: O-21, 06 s7 ("Build an unexposed world with a fresh WorldId, reconstruct scope/target stable IDs,
// rederive capabilities, allocate recipes, restore owner slots, repair stable references, restore clocks/RNG/outbox
// cursors, then publish"), P-024 (a recipe at another revision is stale, never published half-assembled), P-032
// (active and dormant rows), P-036/P-038 (debt and domain time), P-043 (next-step buffers), P-045 (outbox), P-049 (a
// new WorldId) and P-053 (what a checkpoint contains).
//
// This is the builder every V1 validation family wrote by hand, without the family. It is driven by the plan and by
// the game's composer only:
//
//   1. before any world exists: the plan must be direct (slot rows are migrated before planning), the checkpoint's
//      temporal model must be the world's, and every captured target's recipe must be registered at exactly the
//      captured revision - a mismatch is a typed refusal naming the recipe and both revisions;
//   2. the temporal origin is computed (continued, or legacy step 0 when the document does not declare continuity);
//   3. the world is created unexposed at that origin and the composer composes it with the captured scope tree as
//      its lane seed (zero publications for any number of scopes);
//   4. targets get their recipe base layout and their captured slot rows; rows the plan does not name are pruned
//      (zero publications for any number of targets or rows);
//   5. the root boundary (when it differs), each installation, each scope's imports and the world mode (when it
//      differs) are replayed as one publication each, the last one deriving over every seeded target;
//   6. clocks and wakes, random streams, next-step messages, commands and the outbox are rebuilt into the composer's
//      modules and the staging world.
//
// Nothing here is exposed: the executor validates and exposes, or discards (O-21).
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Execution.Persistence;
using GameCore.Execution.Recovery;
using GameCore.Execution.Time;
using GameCore.Unity.Runtime.Delivery;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;
using Unity.Entities;

namespace GameCore.Unity.Runtime.Persistence
{
    /// <summary>
    /// The manifest/recipe-driven restore builder of a production game (SADR-012). It has no fixture knowledge: the
    /// game's <see cref="IProductionWorldComposer"/> composes the world, and everything a checkpoint carries is rebuilt
    /// from the plan. One instance serves one restore attempt at a time; its <see cref="LastReport"/> describes the
    /// most recent attempt.
    /// </summary>
    public sealed class ProductionRestoreTargetBuilder : IRestoreTargetBuilder, IRestoreOutboxBuilder
    {
        private readonly IProductionWorldComposer composer;
        private readonly int maxWakes;
        private bool declaresTemporalContinuity;
        private ProductionWorldParts? parts;

        public ProductionRestoreTargetBuilder(IProductionWorldComposer composer, int maxWakes = 64)
        {
            this.composer = composer ?? throw new ArgumentNullException(nameof(composer));
            if (maxWakes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxWakes), "The wake queue is bounded and positive (P-038).");
            }

            this.maxWakes = maxWakes;
        }

        /// <summary>The report of the most recent build attempt.</summary>
        public ProductionRestoreReport LastReport { get; private set; } = new ProductionRestoreReport();

        /// <summary>The composed parts of the most recent successful build, or null.</summary>
        public ProductionWorldParts? Parts => parts;

        /// <summary>Build attempts made; zero proves a refused restore never staged a world.</summary>
        public int BuildCount { get; private set; }

        /// <summary>
        /// Reads the container features of the document about to be restored. The plan does not carry them, so a
        /// caller that wants temporal continuity tells the builder which document it is restoring; without this call
        /// every restore uses the legacy step-0 origin and reports it (P-055).
        /// </summary>
        public void UseDocument(CheckpointDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            declaresTemporalContinuity = document.DeclaresTemporalContinuity;
        }

        /// <summary>Whether the next build continues the captured clock.</summary>
        public bool DeclaresTemporalContinuity => declaresTemporalContinuity;

        public bool TryBuild(
            WorldId session,
            RestorePlan plan,
            int migrationCount,
            out UnityWorldHost? staging,
            out DiagnosticCode code,
            out string detail)
        {
            BuildCount++;
            staging = null;
            parts = null;
            var report = new ProductionRestoreReport();
            LastReport = report;
            Stopwatch total = Stopwatch.StartNew();

            if (plan == null)
            {
                return Refuse(report, ProductionRestoreRefusal.CompositionFailed, DiagnosticCode.MissingDependency,
                    "the production restore builder was called without a plan.", out code, out detail);
            }

            // 1. Before any world exists.
            if (migrationCount != 0 || plan.Migrations.Count != 0)
            {
                return Refuse(report, ProductionRestoreRefusal.MigrationNotApplied, DiagnosticCode.MigrationRequired,
                    "the plan carries " + plan.Migrations.Count.ToString(CultureInfo.InvariantCulture)
                    + " schema migration(s); slot rows are migrated with SlotMigrationExecutor before planning, and a"
                    + " recipe or clock schema migration is not executable by this build (P-054).",
                    out code, out detail);
            }

            if (plan.Header.Temporal != composer.TemporalModel)
            {
                return Refuse(report, ProductionRestoreRefusal.TemporalModelMismatch, DiagnosticCode.UnsupportedVersion,
                    "the checkpoint was captured from a " + plan.Header.Temporal + " world and this build's world is "
                    + composer.TemporalModel + " (P-036).", out code, out detail);
            }

            if (!CheckRecipes(plan, out ProductionRestoreRefusal recipeRefusal, out code, out detail))
            {
                return Refuse(report, recipeRefusal, code, detail, out code, out detail);
            }

            // 2. The temporal origin.
            RestoredTemporalOrigin origin = RestoredTemporalOrigin.FromCheckpoint(
                plan.Header, plan.Cursors, declaresTemporalContinuity);
            report.Origin = origin;

            // 3. The unexposed world at that origin, composed with the captured scope tree as its lane seed.
            Stopwatch phase = Stopwatch.StartNew();
            WorldCreateRequest request = composer.CreateRequest(session);
            WorldCreateResult created = UnityWorldHost.TryCreateUnexposed(
                request, composer.CreateRegistration(), origin, out UnityWorldHost? createdHost);
            report.CreateMilliseconds = phase.Elapsed.TotalMilliseconds;
            if (!created.Created || createdHost == null)
            {
                return Refuse(report, ProductionRestoreRefusal.WorldCreationFailed, created.Code,
                    "creating the unexposed world for session " + session.Session.ToString() + " failed: "
                    + created.Detail, out code, out detail);
            }

            UnityWorldHost host = createdHost;
            phase.Restart();
            IReadOnlyList<ScopeRecord> seedScopes = SeedScopes(plan);
            report.SeededScopes = seedScopes.Count;
            var composeRequest = new ProductionComposeRequest(
                session, plan, origin, seedScopes, Math.Max(1, plan.Targets.Count));
            if (!composer.TryCompose(host, composeRequest, out ProductionWorldParts? composed, out code, out detail)
                || composed == null)
            {
                DisposeQuietly(host);
                return Refuse(report, ProductionRestoreRefusal.CompositionFailed,
                    code == DiagnosticCode.None ? DiagnosticCode.MissingDependency : code,
                    "the game's composer refused the staging world: " + detail, out code, out detail);
            }

            report.ComposeMilliseconds = phase.Elapsed.TotalMilliseconds;
            if (!ReferenceEquals(composed.Host, host))
            {
                composer.Abandon(composed);
                DisposeQuietly(host);
                return Refuse(report, ProductionRestoreRefusal.CompositionFailed, DiagnosticCode.OwnershipConflict,
                    "the composer returned parts of another world than the staging world it was handed (P-004).",
                    out code, out detail);
            }

            parts = composed;

            // 4. Targets and base layouts. Slot rows are written after the composition replay (step 5b): an install's
            // preflight checks every live slot against the declared descriptors (P-032), and a row owned by an
            // installation that is not replayed yet would be refused as undeclared.
            phase.Restart();
            if (!SeedTargets(composed, plan, report, out code, out detail))
            {
                return Abandon(report, ProductionRestoreRefusal.StateSeedRefused, code, detail, out staging, out code, out detail);
            }

            report.SeedMilliseconds = phase.Elapsed.TotalMilliseconds;

            // 5. Composition replay: one publication per edit, none per target.
            phase.Restart();
            if (!ReplayComposition(composed, plan, report, out code, out detail))
            {
                return Abandon(report, ProductionRestoreRefusal.CompositionReplayRefused, code, detail, out staging, out code, out detail);
            }

            report.ReplayMilliseconds = phase.Elapsed.TotalMilliseconds;

            // 5b. Slot rows (dormant rows included), then prune rows the capture never carried.
            phase.Restart();
            if (!RestoreSlots(composed, plan, report, out code, out detail))
            {
                return Abandon(report, ProductionRestoreRefusal.StateSeedRefused, code, detail, out staging, out code, out detail);
            }

            report.SeedMilliseconds += phase.Elapsed.TotalMilliseconds;

            // 6. Modules and the message plane.
            phase.Restart();
            if (!RebuildClocks(composed.Modules, plan, report, out code, out detail))
            {
                return Abandon(report, ProductionRestoreRefusal.ClockRestoreRefused, code, detail, out staging, out code, out detail);
            }

            if (!RngStreamTable.TryRestore(plan.RngStreams, out RngStreamTable? rng, out string rngDetail) || rng == null)
            {
                return Abandon(report, ProductionRestoreRefusal.RngRestoreRefused, DiagnosticCode.OwnershipConflict,
                    "rebuilding the captured random streams was refused: " + rngDetail, out staging, out code, out detail);
            }

            composed.Modules.Rng = rng;
            report.RngStreams = plan.RngStreams.Count;

            if (!RestoreNextStepMessages(composed, plan, origin, report, out code, out detail))
            {
                return Abandon(report, ProductionRestoreRefusal.MessageRestoreRefused, code, detail, out staging, out code, out detail);
            }

            if (!ReadmitCommands(composed, session, plan, report, out code, out detail))
            {
                return Abandon(report, ProductionRestoreRefusal.CommandReadmissionRefused, code, detail, out staging, out code, out detail);
            }

            report.ModulesMilliseconds = phase.Elapsed.TotalMilliseconds;
            report.TotalMilliseconds = total.Elapsed.TotalMilliseconds;
            staging = host;
            code = DiagnosticCode.None;
            detail = report.Describe();
            report.Detail = detail;
            return true;
        }

        public bool TryReinstateOutbox(
            WorldId session,
            RestorePlan plan,
            out int reinstatedRows,
            out DiagnosticCode code,
            out string detail)
        {
            reinstatedRows = 0;
            code = DiagnosticCode.None;
            detail = string.Empty;
            WorldDeliveryOwner? delivery = parts?.Modules.Delivery;
            if (plan.Outbox.Count == 0)
            {
                return true;
            }

            if (delivery == null)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "the checkpoint carries " + plan.Outbox.Count.ToString(CultureInfo.InvariantCulture)
                    + " outbox row(s) and the restored session " + session.Session.ToString()
                    + " has no delivery owner to reinstate them into (P-045, P-053).";
                LastReport.Refusal = ProductionRestoreRefusal.OutboxRefused;
                LastReport.Code = code;
                LastReport.Detail = detail;
                return false;
            }

            if (!delivery.TryReinstate(plan.Outbox, out code, out detail))
            {
                LastReport.Refusal = ProductionRestoreRefusal.OutboxRefused;
                LastReport.Code = code;
                LastReport.Detail = detail;
                return false;
            }

            reinstatedRows = plan.Outbox.Count;
            LastReport.OutboxRows = reinstatedRows;
            return true;
        }

        public bool TryProveOutbox(
            WorldId session,
            IReadOnlyList<OutboxRecordValue> expected,
            out DiagnosticCode code,
            out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            WorldDeliveryOwner? delivery = parts?.Modules.Delivery;
            if (delivery == null)
            {
                if (expected.Count == 0)
                {
                    return true;
                }

                code = DiagnosticCode.MissingDependency;
                detail = "the restored session " + session.Session.ToString() + " has no delivery owner to prove "
                    + expected.Count.ToString(CultureInfo.InvariantCulture) + " outbox row(s) against (P-045).";
                return false;
            }

            OutboxConsistencyReport proof = OutboxConsistency.Verify(expected, delivery.Outbox, "staging:" + session.Session.ToString());
            if (!proof.Consistent)
            {
                code = DiagnosticCode.MissingDependency;
                detail = proof.Describe().Replace('\n', ' ');
                return false;
            }

            return true;
        }

        /// <summary>
        /// Releases what the composer registered for the last staging world when the executor refused it after the
        /// build (validation, outbox proof, exposure). The executor disposes the world itself.
        /// </summary>
        public void AbandonStaging()
        {
            ProductionWorldParts? last = parts;
            parts = null;
            if (last != null)
            {
                composer.Abandon(last);
            }
        }

        // ------------------------------------------------------------------ 1. recipes

        private bool CheckRecipes(
            RestorePlan plan,
            out ProductionRestoreRefusal refusal,
            out DiagnosticCode code,
            out string detail)
        {
            IReadOnlyList<SpawnRecipe> recipes = composer.Recipes.Recipes;
            var registered = new Dictionary<Id128, SpawnRecipe>(recipes.Count);
            for (int i = 0; i < recipes.Count; i++)
            {
                registered[recipes[i].Recipe.Id.Value] = recipes[i];
            }

            for (int i = 0; i < plan.Targets.Count; i++)
            {
                DefinitionRef captured = plan.Targets[i].Recipe;
                if (!registered.TryGetValue(captured.Id.Value, out SpawnRecipe? recipe) || recipe == null)
                {
                    refusal = ProductionRestoreRefusal.RecipeMissing;
                    code = DiagnosticCode.MissingDependency;
                    detail = "captured target " + plan.Targets[i].Target.ToString() + " names recipe "
                        + captured.Id.ToString() + " (revision " + captured.Revision.Value.ToString(CultureInfo.InvariantCulture)
                        + "), which this build does not register (P-015, P-024).";
                    return false;
                }

                if (!recipe.Recipe.Revision.Equals(captured.Revision))
                {
                    refusal = ProductionRestoreRefusal.RecipeRevisionMismatch;
                    code = DiagnosticCode.StalePlan;
                    detail = "recipe " + captured.Id.ToString() + " was captured at revision "
                        + captured.Revision.Value.ToString(CultureInfo.InvariantCulture) + " and this build registers revision "
                        + recipe.Recipe.Revision.Value.ToString(CultureInfo.InvariantCulture)
                        + " (target " + plan.Targets[i].Target.ToString() + "); a recipe is restored at exactly its"
                        + " captured revision (P-024).";
                    return false;
                }
            }

            refusal = ProductionRestoreRefusal.None;
            code = DiagnosticCode.None;
            detail = string.Empty;
            return true;
        }

        // ------------------------------------------------------------------ 3. scope seed

        private static IReadOnlyList<ScopeRecord> SeedScopes(RestorePlan plan)
        {
            var seed = new List<ScopeRecord>();
            for (int i = 0; i < plan.Scopes.Count; i++)
            {
                ScopeRecordValue row = plan.Scopes[i];
                if (row.IsRoot)
                {
                    continue;
                }

                seed.Add(new ScopeRecord(
                    row.Scope,
                    row.Parent,
                    (int)row.Depth,
                    CapturedIsolation(plan.Grants, row.Scope, GrantKind.ServiceIsolationMember, row.ServiceIsolationAll),
                    CapturedIsolation(plan.Grants, row.Scope, GrantKind.CapabilityIsolationMember, row.CapabilityIsolationAll),
                    CapturedExclusions(plan.Grants, row.Scope),
                    null));
            }

            return seed;
        }

        // ------------------------------------------------------------------ 4. targets

        private static bool SeedTargets(
            ProductionWorldParts composed,
            RestorePlan plan,
            ProductionRestoreReport report,
            out DiagnosticCode code,
            out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            EntityManager entityManager = composed.Host.EntityWorld.EntityManager;
            LiveTargetSeeder seeder = composed.Seeder;
            for (int i = 0; i < plan.Targets.Count; i++)
            {
                TargetRecordValue row = plan.Targets[i];
                if (!seeder.TrySeed(row.Target, row.Scope, row.Recipe, out TargetHandle _, out code, out detail))
                {
                    detail = "seeding captured target " + row.Target.ToString() + " at scope " + row.Scope.ToString()
                        + " with recipe " + row.Recipe.ToString() + " was refused: " + detail;
                    return false;
                }

                if (!composed.Publisher.Recipes.TryResolve(row.Recipe, out SpawnRecipe? recipe, out code) || recipe == null)
                {
                    detail = "recipe " + row.Recipe.ToString() + " did not resolve in the composed world's catalog (P-024).";
                    return false;
                }

                if (!seeder.TryGetEntity(row.Target, out Entity entity))
                {
                    code = DiagnosticCode.StaleHandle;
                    detail = "target " + row.Target.ToString() + " was seeded but does not resolve (P-005).";
                    return false;
                }

                // 06 s7 "allocate recipes": the restored target gets the base layout a spawn installs (P-024).
                recipe.Applier.ApplyBaseLayout(entityManager, entity, recipe);
                report.Targets++;
            }

            return true;
        }

        // ------------------------------------------------------------------ 5b. slot rows

        private static bool RestoreSlots(
            ProductionWorldParts composed,
            RestorePlan plan,
            ProductionRestoreReport report,
            out DiagnosticCode code,
            out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            EntityManager entityManager = composed.Host.EntityWorld.EntityManager;
            LiveTargetSeeder seeder = composed.Seeder;
            for (int i = 0; i < plan.Slots.Count; i++)
            {
                SlotRecordValue row = plan.Slots[i];
                StateSlotKey key = row.Key;
                if (!seeder.TrySeedSlot(key.Target, key.Owner, key.Slot, row.SchemaVersion, row.Value, row.Active, out code, out detail))
                {
                    detail = "seeding captured state slot " + key.ToString() + " was refused: " + detail;
                    return false;
                }

                report.Slots++;
                if (!row.Active)
                {
                    report.DormantSlots++;
                }
            }

            // A base layout or a replayed installation's state policy may create rows the capture never carried; the
            // restored state is the captured state and nothing else (P-032, P-053).
            var planned = new HashSet<(Id128 target, Id128 owner, Id128 slot)>();
            for (int i = 0; i < plan.Slots.Count; i++)
            {
                StateSlotKey key = plan.Slots[i].Key;
                planned.Add((key.Target.Value, key.Owner.Value, key.Slot.Value));
            }

            for (int i = 0; i < plan.Targets.Count; i++)
            {
                TargetRecordValue row = plan.Targets[i];
                if (!seeder.TryGetEntity(row.Target, out Entity entity) || !entityManager.HasBuffer<TargetSlotState>(entity))
                {
                    continue;
                }

                DynamicBuffer<TargetSlotState> slots = entityManager.GetBuffer<TargetSlotState>(entity);
                for (int s = slots.Length - 1; s >= 0; s--)
                {
                    TargetSlotState slot = slots[s];
                    if (!planned.Contains((row.Target.Value, slot.Owner.Value, slot.Slot.Value)))
                    {
                        slots.RemoveAt(s);
                        report.PrunedSlots++;
                    }
                }
            }

            return true;
        }

        // ------------------------------------------------------------------ 5. composition replay

        private static bool ReplayComposition(
            ProductionWorldParts composed,
            RestorePlan plan,
            ProductionRestoreReport report,
            out DiagnosticCode code,
            out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            CompositionHost lane = composed.Lane;

            // Every captured scope must now be in the lane: the seed carried the non-root scopes, the composer the root.
            for (int i = 0; i < plan.Scopes.Count; i++)
            {
                ScopeRecordValue row = plan.Scopes[i];
                if (!lane.Committed.Scopes.TryGet(row.Scope, out ScopeRecord? present) || present == null)
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "captured scope " + row.Scope.ToString() + " is absent from the composed lane; the composer"
                        + " must seed the request's scopes (P-010).";
                    return false;
                }

                if (!row.IsRoot)
                {
                    continue;
                }

                // The root is the composer's own record; a captured root boundary that differs is one edit (P-016).
                IsolationSet service = CapturedIsolation(plan.Grants, row.Scope, GrantKind.ServiceIsolationMember, row.ServiceIsolationAll);
                IsolationSet capability = CapturedIsolation(plan.Grants, row.Scope, GrantKind.CapabilityIsolationMember, row.CapabilityIsolationAll);
                IReadOnlyList<ExclusionRule> exclusions = CapturedExclusions(plan.Grants, row.Scope);
                if (string.Equals(
                        BoundaryText(present.ServiceIsolation, present.CapabilityIsolation, present.Exclusions),
                        BoundaryText(service, capability, exclusions),
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (!Apply(composed, ScopeBoundaries(row.Scope, service, capability, exclusions), report, out code, out detail))
                {
                    detail = "replaying the captured root boundary was refused: " + detail;
                    return false;
                }

                report.ReplayedRootBoundaries++;
            }

            for (int i = 0; i < plan.Installs.Count; i++)
            {
                InstallRecordValue row = plan.Installs[i];
                if (row.Lifecycle == InstallationState.Disposed)
                {
                    continue;
                }

                if (!TryMountPayload(row, plan.Selections, out CompositionEditPayload? payload, out detail) || payload == null)
                {
                    code = DiagnosticCode.UnsupportedVersion;
                    return false;
                }

                if (!Apply(composed, payload, report, out code, out detail))
                {
                    detail = "replaying captured install " + row.Instance.ToString() + " at scope " + row.Scope.ToString()
                        + " was refused: " + detail;
                    return false;
                }

                report.ReplayedInstalls++;
            }

            for (int i = 0; i < plan.Scopes.Count; i++)
            {
                ScopeRecordValue row = plan.Scopes[i];
                IReadOnlyList<CapabilityImport> imports = CapturedImports(plan.Grants, row.Scope);
                if (imports.Count == 0)
                {
                    continue;
                }

                if (lane.Committed.Scopes.TryGet(row.Scope, out ScopeRecord? present) && present != null
                    && string.Equals(ImportsText(present.Grants.Imports), ImportsText(imports), StringComparison.Ordinal))
                {
                    continue;
                }

                if (!Apply(composed, ScopeImports(row.Scope, imports), report, out code, out detail))
                {
                    detail = "replaying the imports of scope " + row.Scope.ToString() + " was refused: " + detail;
                    return false;
                }

                report.ReplayedImportScopes++;
            }

            if (plan.Header.Propagation != lane.Committed.Mode)
            {
                if (!Apply(composed, ModeSet(plan.Header.Propagation), report, out code, out detail))
                {
                    detail = "replaying the captured world mode " + plan.Header.Propagation + " was refused: " + detail;
                    return false;
                }

                report.ReplayedModes++;
            }

            return true;
        }

        private static bool Apply(
            ProductionWorldParts composed,
            CompositionEditPayload payload,
            ProductionRestoreReport report,
            out DiagnosticCode code,
            out string detail)
        {
            ProductionEditResult result = composed.ApplyEdit(payload, out code, out detail);
            if (result == ProductionEditResult.Published)
            {
                report.Publications++;
                return true;
            }

            if (result == ProductionEditResult.NoChange)
            {
                report.NoChangeEdits++;
                code = DiagnosticCode.None;
                return true;
            }

            if (code == DiagnosticCode.None)
            {
                code = DiagnosticCode.ApplyFault;
            }

            return false;
        }

        // ------------------------------------------------------------------ 6. modules

        private bool RebuildClocks(
            ProductionWorldModules modules,
            RestorePlan plan,
            ProductionRestoreReport report,
            out DiagnosticCode code,
            out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            modules.ResetClocks(maxWakes);
            for (int i = 0; i < plan.Clocks.Count; i++)
            {
                ClockRecordValue row = plan.Clocks[i];
                if (row.IsDeclaration)
                {
                    var spec = new PluginClockSpec(
                        row.ClockId,
                        modules.NameOf(row.ClockId),
                        (PluginClockKind)row.ClockKind,
                        (WakePausePolicy)row.PausePolicy,
                        row.Persists);
                    if (!modules.Clocks.TryRegister(spec, out code))
                    {
                        detail = "re-registering captured clock " + row.ClockId.ToString() + " was refused: "
                            + DiagnosticCodeText.Of(code) + " (P-038).";
                        return false;
                    }

                    report.Clocks++;
                    continue;
                }

                if (row.State == ClockWakeState.Consumed)
                {
                    continue;
                }

                if (!modules.Clocks.TryScheduleWake(
                        row.ClockId,
                        row.WakeId,
                        row.PayloadSchema,
                        row.RemainingSteps,
                        row.RemainingTicks,
                        row.ScheduledAtSequence,
                        out WakeRecord? wake,
                        out code)
                    || wake == null)
                {
                    detail = "re-scheduling captured wake " + row.WakeId.ToString() + " on clock " + row.ClockId.ToString()
                        + " was refused: " + DiagnosticCodeText.Of(code) + " (P-038).";
                    return false;
                }

                report.Wakes++;
            }

            // Declared clocks the capture did not carry (a clock added by this build) are registered empty.
            IReadOnlyList<PluginClockSpec> declared = modules.DeclaredClocks;
            for (int i = 0; i < declared.Count; i++)
            {
                modules.Clocks.TryRegister(declared[i], out DiagnosticCode _);
            }

            return true;
        }

        private static bool RestoreNextStepMessages(
            ProductionWorldParts composed,
            RestorePlan plan,
            RestoredTemporalOrigin origin,
            ProductionRestoreReport report,
            out DiagnosticCode code,
            out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (plan.Messages.Count == 0)
            {
                return true;
            }

            WorldMessagePlane? plane = composed.Host.Messages;
            if (plane == null)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "the checkpoint carries " + plan.Messages.Count.ToString(CultureInfo.InvariantCulture)
                    + " next-step message(s) and the restored world declares no message plane (P-043).";
                return false;
            }

            WorldId session = composed.Host.World;
            ulong capturedStep = plan.Header.LogicalStep;
            for (int i = 0; i < plan.Messages.Count; i++)
            {
                MessageRecordValue row = plan.Messages[i];
                if (!plane.Schedule.TryGetBuffer(row.Buffer, out BoundedMessageBuffer? buffer) || buffer == null)
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "captured next-step message " + i.ToString(CultureInfo.InvariantCulture) + " names buffer "
                        + row.Buffer.ToString() + ", which the restored world does not declare (P-043).";
                    return false;
                }

                // A next-step row keeps its distance from the captured boundary: with continuity the step is the same,
                // with legacy step-0 semantics it moves with the origin (P-043, P-055).
                ulong offset = row.Step >= capturedStep ? row.Step - capturedStep : 0UL;
                var step = new LogicalStepId(origin.LogicalStep.Value + offset);
                OperationId requestId = row.HasRequest
                    ? new OperationId(session, new Id128(row.RequestIssuerHigh, row.RequestIssuerLow), row.RequestSequence)
                    : default(OperationId);
                byte[]? payload = row.HasPayload ? row.Payload : null;
                int length = payload == null ? 0 : payload.Length;
                var message = new StepMessage(
                    step,
                    composed.Host.CurrentEpoch,
                    requestId,
                    row.Route,
                    row.Owner,
                    row.Target,
                    row.PayloadSchema,
                    (MessageKind)row.MessageKind,
                    new MessageOrderKey(new AdmissionSequence(row.OrderAdmitted), row.OrderOrdinal, row.OriginKey),
                    row.Producer,
                    0,
                    length);
                BufferAppendOutcome appended = buffer.TryAppend(message, payload, out string appendDetail);
                if (appended != BufferAppendOutcome.Accepted)
                {
                    code = DiagnosticCode.BudgetExceeded;
                    detail = "re-appending captured next-step message " + i.ToString(CultureInfo.InvariantCulture)
                        + " to buffer " + row.Buffer.ToString() + " was refused: " + appended + " " + appendDetail + " (P-043).";
                    return false;
                }

                report.NextStepMessages++;
            }

            return true;
        }

        private static bool ReadmitCommands(
            ProductionWorldParts composed,
            WorldId session,
            RestorePlan plan,
            ProductionRestoreReport report,
            out DiagnosticCode code,
            out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            for (int i = 0; i < plan.Commands.Count; i++)
            {
                CommandRecordValue command = plan.Commands[i];
                OperationId request = command.RequestIdIn(session);
                var envelope = new CommandEnvelope(request, command.Route, command.Target, command.Schema, null, command.Frozen);
                CommandAdmissionReceipt receipt = composed.Host.Submit(envelope);
                if (!receipt.Admitted)
                {
                    code = receipt.Result.Reason == DiagnosticCode.None ? DiagnosticCode.ResourceUnavailable : receipt.Result.Reason;
                    detail = "re-admitting captured command " + request.ToString() + " on the restored session was refused: "
                        + receipt.Result.Kind + "/" + DiagnosticCodeText.Of(receipt.Result.Reason) + " (P-037).";
                    return false;
                }

                if (command.Frozen != null)
                {
                    composed.Modules.RecordCommandPayload(request, command.Frozen);
                }

                composed.Host.NotifyCommandAdmitted(1U);
                report.ReadmittedCommands++;
            }

            return true;
        }

        // ------------------------------------------------------------------ refusal plumbing

        private static bool Refuse(
            ProductionRestoreReport report,
            ProductionRestoreRefusal refusal,
            DiagnosticCode refusalCode,
            string refusalDetail,
            out DiagnosticCode code,
            out string detail)
        {
            report.Refusal = refusal;
            report.Code = refusalCode == DiagnosticCode.None ? DiagnosticCode.ApplyFault : refusalCode;
            report.Detail = refusal.ToString() + ": " + refusalDetail;
            code = report.Code;
            detail = report.Detail;
            return false;
        }

        private bool Abandon(
            ProductionRestoreReport report,
            ProductionRestoreRefusal refusal,
            DiagnosticCode refusalCode,
            string refusalDetail,
            out UnityWorldHost? staging,
            out DiagnosticCode code,
            out string detail)
        {
            staging = null;
            ProductionWorldParts? last = parts;
            parts = null;
            if (last != null)
            {
                composer.Abandon(last);
                DisposeQuietly(last.Host);
            }

            return Refuse(report, refusal, refusalCode, refusalDetail, out code, out detail);
        }

        private static void DisposeQuietly(UnityWorldHost host)
        {
            try
            {
                if (host.Lifecycle != WorldLifecycleState.Disposed)
                {
                    host.Dispose();
                }
            }
            catch (InvalidOperationException)
            {
                // A blocked teardown keeps its resources pinned and is visible on the host itself (P-048); the refusal
                // the caller receives already names why the staging world does not exist.
            }
        }

        // ------------------------------------------------------------------ payloads and captured grants

        internal static CompositionEditPayload ScopeBoundaries(
            ScopeId scope,
            IsolationSet service,
            IsolationSet capability,
            IReadOnlyList<ExclusionRule> exclusions) =>
            new CompositionEditPayload(
                CompositionEditSubject.ScopeIsolation, scope, default(ScopeId), false, service, capability, exclusions,
                null, default(PluginTypeId), default(PluginInstanceId), DefinitionRevision.Zero, ContentHash.Empty,
                null, 0, null, PropagationMode.Automatic);

        internal static CompositionEditPayload ScopeImports(ScopeId scope, IReadOnlyList<CapabilityImport> imports) =>
            new CompositionEditPayload(
                CompositionEditSubject.ScopeGrants, scope, default(ScopeId), false, null, null, null, imports,
                default(PluginTypeId), default(PluginInstanceId), DefinitionRevision.Zero, ContentHash.Empty, null, 0,
                null, PropagationMode.Automatic);

        internal static CompositionEditPayload ModeSet(PropagationMode mode) =>
            new CompositionEditPayload(
                CompositionEditSubject.ModeSet, default(ScopeId), default(ScopeId), false, null, null, null, null,
                default(PluginTypeId), default(PluginInstanceId), DefinitionRevision.Zero, ContentHash.Empty, null, 0,
                null, mode);

        private static bool TryMountPayload(
            InstallRecordValue row,
            IReadOnlyList<SelectionRecordValue> selections,
            out CompositionEditPayload? payload,
            out string detail)
        {
            payload = null;
            detail = string.Empty;
            ConfigDocument? config = ConfigDocument.Empty;
            if (row.HasConfigDocument)
            {
                byte[]? bytes = row.ConfigBytes;
                if (bytes == null || !ConfigDocumentCodec.TryDecode(new FrozenPayload(bytes), out config) || config == null)
                {
                    detail = "install " + row.Instance.ToString() + " carried a configuration document that would not decode.";
                    return false;
                }
            }

            var selected = new List<ServiceSelection>();
            for (int i = 0; i < selections.Count; i++)
            {
                if (selections[i].Instance.Equals(row.Instance))
                {
                    selected.Add(selections[i].ToSelection());
                }
            }

            payload = new CompositionEditPayload(
                CompositionEditSubject.InstallMount,
                row.Scope,
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                row.PluginType,
                row.Instance,
                row.Revision,
                row.ConfigHash,
                config,
                row.Priority,
                selected,
                PropagationMode.Automatic);
            return true;
        }

        private static IsolationSet CapturedIsolation(
            IReadOnlyList<GrantRecordValue> grants,
            ScopeId scope,
            GrantKind kind,
            bool allContracts)
        {
            var members = new List<Id128>();
            for (int i = 0; i < grants.Count; i++)
            {
                GrantRecordValue row = grants[i];
                if (row.Grant == kind && row.Scope.Equals(scope))
                {
                    members.Add(row.Subject);
                }
            }

            return new IsolationSet(allContracts, members);
        }

        private static IReadOnlyList<CapabilityImport> CapturedImports(IReadOnlyList<GrantRecordValue> grants, ScopeId scope)
        {
            var imports = new List<CapabilityImport>();
            for (int i = 0; i < grants.Count; i++)
            {
                GrantRecordValue row = grants[i];
                if (row.Grant == GrantKind.ScopeImport && row.Scope.Equals(scope))
                {
                    imports.Add(row.ToImport());
                }
            }

            return imports;
        }

        private static IReadOnlyList<ExclusionRule> CapturedExclusions(IReadOnlyList<GrantRecordValue> grants, ScopeId scope)
        {
            var rules = new List<ExclusionRule>();
            for (int i = 0; i < grants.Count; i++)
            {
                GrantRecordValue row = grants[i];
                if (row.Grant == GrantKind.Exclusion && row.Scope.Equals(scope))
                {
                    rules.Add(row.ToExclusion());
                }
            }

            return rules;
        }

        private static string BoundaryText(IsolationSet service, IsolationSet capability, IReadOnlyList<ExclusionRule> exclusions)
        {
            var lines = new List<string> { "service=" + IsolationText(service), "capability=" + IsolationText(capability) };
            for (int i = 0; i < exclusions.Count; i++)
            {
                ExclusionRule rule = exclusions[i];
                lines.Add("exclusion=" + rule.Kind + ";" + rule.TargetId.ToString() + ";" + rule.AtScope.ToString() + ";"
                    + rule.AtTarget.ToString() + ";" + rule.AppliesToSubtree.ToString());
            }

            lines.Sort(StringComparer.Ordinal);
            return string.Join("\n", lines.ToArray());
        }

        private static string IsolationText(IsolationSet set)
        {
            var lines = new List<string>();
            for (int i = 0; i < set.Contracts.Count; i++)
            {
                lines.Add(set.Contracts[i].ToString());
            }

            lines.Sort(StringComparer.Ordinal);
            return set.AllContracts.ToString() + ":" + string.Join(",", lines.ToArray());
        }

        private static string ImportsText(IReadOnlyList<CapabilityImport> imports)
        {
            var lines = new List<string>(imports.Count);
            for (int i = 0; i < imports.Count; i++)
            {
                lines.Add(imports[i].CapabilityId.ToString() + "@" + imports[i].ProviderInstallationId.ToString());
            }

            lines.Sort(StringComparer.Ordinal);
            return string.Join("\n", lines.ToArray());
        }
    }
}
