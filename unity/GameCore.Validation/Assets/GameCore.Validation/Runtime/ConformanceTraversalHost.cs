// GameCore.Validation.ProbeHost — the GC-024 traversal conformance host.
//
// `Gc020TraversalHost.CourseFamily` already declares the whole traversal course as a family: its catalog
// declarations, its course scope tree, its live runners and checkpoint volumes, its two acceleration modifiers and
// its real fixed-step stage runtime. This file adds the *GC-024* half — one more partial part of the same class —
// implementing `IConformanceFamily`, so 07 s4.3's before/after table runs over the same course the GC-020 gate runs
// (P-001).
//
// WHAT `Apply` DOES. Every operation key maps to a payload the traversal package itself declares
// (`TraversalCourseComposition` / `TraversalPayloads`), published through the ordinary lane:
//
//   * `mount-provider` / `unmount-provider` are 07 s4.3's Tailwind lifecycle (`Valley`), and
//     `mount-second-provider` / `unmount-second-provider` its Headwind counterpart (`Ridge`);
//   * `mount-nested-provider` mounts the conformance run's own second acceleration contribution INSIDE 07 s4.1's
//     `Showcase` capability boundary. `DerivationPolicy.CollectBoundaries` states the rule this row observes: "a
//     boundary blocks outside rules while providers installed at or below it still work" — so the valley's Tailwind
//     never reaches `runner-display` while a provider mounted at the showcase scope itself does (P-016);
//   * `reparent-moved-scope`, both mode directions, `suspend-provider` / `resume-provider` and
//     `apply-exclusion` are the course's own O-02/O-08/O-06/O-04/P-016 edits;
//   * `commit-command` is MOTION, not a command envelope: the course is a fixed-step world, so `operand` zero-input
//     integrations are pumped through `PumpDeclaredStep`, which is exactly 07 s4.3's numeric sequence (P-036);
//   * `spawn-future-target` publishes the course's own declared future runner (`runner-c`) fully assembled, riding
//     on the neutral publication `Gc020Scenario` itself uses before its spawn (P-024);
//   * `commit-command-rejected` submits a movement envelope for a live target the course does NOT own as a runner, so
//     the input stage's own domain refusal is the observed answer. It never reports `Published` unless the pump
//     really committed and the refusal was really recorded.
//
// 07 s4.3 declares no reconfiguration row (the fixture's reconfiguration is the card market's 07:100) and the
// traversal course declares no reward bridge or outbox, so those keys are reported as unsupported rather than
// approximated by an edit the course does not have.
//
// WHY THE MOTION FIELDS ARE READ THROUGH THE COURSE MODULE. `TraversalModule` is the course's own runtime state: it
// holds the runner entities the seeding step created (`TryRunner`) and the course entity that owns `RunProgress`
// (`CourseEntity`, read through `TraversalAccess.TryReadProgress`). Reading pose, velocity, jump state and progress
// through it is reading the same objects the five compiled stages write, never a parallel view (P-034). Derived
// values are read from the published assembly, one row per `(target, traversal.acceleration, slot 0)`, exactly as
// `Gc020Scenario.TryActiveBindingValue` reads them (P-030).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Gameplay.Traversal;
using GameCore.Gameplay.Traversal.Fixtures;
using GameCore.ReferenceConformance;
using GameCore.Rules.Traversal;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using Unity.Entities;

namespace GameCore.Validation.ProbeHost
{
    public static partial class Gc020TraversalHost
    {
        /// <summary>
        /// Stable name of the conformance run's own second acceleration contribution. It is a lane-only declaration
        /// (a capability rule and its contract, no stage, buffer or state slot), so the compiled ownership surface
        /// every earlier gate asserts on is untouched (P-009's "an empty category is explicit").
        /// </summary>
        private const string ConformanceNestedModifier = "gc020.traversal.conformance-nested-modifier";

        /// <summary>
        /// Stable-name subject of the complete-opt-in runner `Gc020TraversalHost.OptedInTarget` names (its identity
        /// is `TraversalIdentity.Target(<this>)`, declared there as a literal because no rules-package vocabulary
        /// names it). The two must agree, which is what makes the field key below the target's own name (P-004).
        /// </summary>
        private const string ConformanceOptedInRunner = "gc020.traversal.opted-in-runner";

        /// <summary>The traversal course's half of `IConformanceFamily`: one more partial part of the same family.</summary>
        public sealed partial class CourseFamily : IConformanceFamily
        {
            private const string AccelerationXSuffix = ".acceleration.x";

            private const string AccelerationProviderSuffix = ".acceleration-provider";

            private const string PoseSuffix = ".pose";

            private const string VelocitySuffix = ".velocity";

            private const string JumpSuffix = ".jump";

            private const string ProgressSuffix = ".progress";

            /// <summary>
            /// The one lane-only declaration 07 s4.1's showcase boundary case needs: a second `traversal.acceleration`
            /// provider of the course's own declared shape, with its own plugin type, factory key and rule identity so
            /// it composes additively beside Tailwind instead of colliding with it (P-009, P-017, P-019).
            /// </summary>
            private static readonly IReadOnlyList<CatalogPluginDeclaration> conformanceDeclarations =
                new List<CatalogPluginDeclaration>
                {
                    new CatalogPluginDeclaration(
                        TraversalDeclarations.AccelerationModifier(
                            TraversalKeys.PluginType(ConformanceNestedModifier),
                            TraversalKeys.PluginFactoryKey,
                            TraversalKeys.ConfigSchema,
                            ConformanceNestedModifier,
                            new TraversalVector3i(TraversalVocabulary.TailwindMilli, 0, 0)),
                        ConfigDocument.Empty),
                };

            /// <summary>The label every observation of this family's run is qualified with.</summary>
            public string ConformanceLabel => Gc020TraversalHost.Label;

            /// <summary>
            /// The nested second acceleration provider of the showcase boundary case. It reaches the lane's manifest
            /// source only: `CompilePipeline` builds the schedule from the course's own declarations, so no compiled
            /// stage of this world changes (P-009, P-043).
            /// </summary>
            public IReadOnlyList<CatalogPluginDeclaration> ConformanceDeclarations => conformanceDeclarations;

            /// <summary>
            /// Executes one operation key of a `ConformanceScript` against one live traversal world. Every payload
            /// comes from the course's own builders, so the edits are the shape `Gc020Scenario` itself submits
            /// (P-002, P-042).
            /// </summary>
            public ConformanceOperationResult Apply(string operation, int operand, ConformanceWorld world)
            {
                if (world == null)
                {
                    return Unsupported("no world");
                }

                switch (operation)
                {
                    case ConformanceOperations.MountProvider:
                        return world.PublishEdit(MountProvider(), "mount-tailwind");

                    case ConformanceOperations.MountSecondProvider:
                        return world.PublishEdit(MountSecondProvider(), "mount-headwind");

                    case ConformanceOperations.MountNestedProvider:
                        return world.PublishEdit(
                            TraversalCourseComposition.MountModifier(
                                conformanceDeclarations[0].Manifest,
                                TraversalKeys.Instance(ConformanceNestedModifier),
                                TraversalCourseComposition.ShowcaseScope),
                            "mount-nested-modifier");

                    case ConformanceOperations.MountConflictProvider:
                        return world.PublishEdit(MountConflictProvider(), "mount-conflict-one");

                    case ConformanceOperations.MountConflictSecondProvider:
                        return world.PublishEdit(MountConflictSecondProvider(), "mount-conflict-two");

                    case ConformanceOperations.UnmountProvider:
                        return world.PublishEdit(
                            TraversalCourseComposition.Unmount(TraversalCourseComposition.TailwindInstance),
                            "unmount-tailwind");

                    case ConformanceOperations.UnmountSecondProvider:
                        return world.PublishEdit(
                            TraversalCourseComposition.Unmount(TraversalCourseComposition.HeadwindInstance),
                            "unmount-headwind");

                    case ConformanceOperations.ReconfigureProvider:
                        return Unsupported(
                            "07 s4.3 declares no reconfiguration of either acceleration modifier; the fixture's"
                            + " reconfiguration row is the card market's 07:100, and a +" + operand.ToString(
                                CultureInfo.InvariantCulture) + " modifier is not a declaration this catalog"
                            + " registers (P-009)");

                    case ConformanceOperations.ReparentMovedScope:
                        return world.PublishEdit(ScopeReparent(), "reparent-valley-runners");

                    case ConformanceOperations.ModeAutomatic:
                        return world.PublishEdit(ModeSet(PropagationMode.Automatic), "mode-automatic");

                    case ConformanceOperations.ModeConservative:
                        return world.PublishEdit(ModeSet(PropagationMode.Conservative), "mode-conservative");

                    case ConformanceOperations.SuspendProvider:
                        return world.PublishEdit(
                            LifecycleEdit(
                                CompositionEditSubject.InstallSuspend, TraversalCourseComposition.TailwindInstance),
                            "suspend-tailwind");

                    case ConformanceOperations.ResumeProvider:
                        return world.PublishEdit(
                            LifecycleEdit(
                                CompositionEditSubject.InstallResume, TraversalCourseComposition.TailwindInstance),
                            "resume-tailwind");

                    case ConformanceOperations.SpawnFutureTarget:
                        return SpawnFuture(world, operand);

                    case ConformanceOperations.SeedOptedInTarget:
                        return SeedOptedIn(world);

                    case ConformanceOperations.ApplyExclusion:
                        return ExcludeTarget(world, operand);

                    case ConformanceOperations.CommitCommand:
                        return IntegrateSteps(world, operand);

                    case ConformanceOperations.CommitCommandRejected:
                        return RejectMovement(world);

                    // 07 s5's durable outbox and reward bridge belong to the combined world: the traversal course
                    // declares no reward source, no recipient and no bridge provider, so these keys are cross-family.
                    case ConformanceOperations.MountRewardBridge:
                    case ConformanceOperations.UnmountRewardBridge:
                    case ConformanceOperations.SettleReward:
                    case ConformanceOperations.RedeliverReward:
                        return Unsupported(
                            "'" + operation + "' is the cross-family combination's operation; the traversal course"
                            + " declares no reward bridge, no reward and no durable outbox");

                    default:
                        return Unsupported("the traversal course declares no '" + operation + "' operation");
                }
            }

            /// <summary>
            /// Reads one canonical field of `ConformanceFields` out of the live traversal world. Derived rows come
            /// from the published assembly (P-030), motion and progress from the course module's own entities
            /// (P-034), and `world.mode` from the committed composition. A field this course does not own, or a
            /// target that is not a live runner of it, is a miss with its reason, never a substituted value.
            /// </summary>
            public bool TryReadField(string field, ConformanceWorld world, out string value, out string detail)
            {
                value = ConformanceValue.None;
                detail = string.Empty;
                if (world == null || world.Host == null)
                {
                    detail = "no world";
                    return false;
                }

                if (string.Equals(field, ConformanceFields.WorldMode, StringComparison.Ordinal))
                {
                    if (world.Lane == null)
                    {
                        detail = "the world has no composition lane";
                        return false;
                    }

                    value = world.Lane.Committed.Mode == PropagationMode.Conservative
                        ? "conservative"
                        : "automatic";
                    return true;
                }

                // Derived rows: the effective `traversal.acceleration` value and the installation supporting it.
                if (TrySubject(field, AccelerationXSuffix, out string accelerationSubject))
                {
                    return TryReadAccelerationX(world, accelerationSubject, out value, out detail);
                }

                if (TrySubject(field, AccelerationProviderSuffix, out string providerSubject))
                {
                    return TryReadAccelerationProvider(world, providerSubject, out value, out detail);
                }

                // Runner-owned motion and the course-owned run progress.
                if (TrySubject(field, PoseSuffix, out string poseSubject))
                {
                    return TryReadPose(world, poseSubject, out value, out detail);
                }

                if (TrySubject(field, VelocitySuffix, out string velocitySubject))
                {
                    return TryReadVelocity(world, velocitySubject, out value, out detail);
                }

                if (TrySubject(field, JumpSuffix, out string jumpSubject))
                {
                    return TryReadJump(world, jumpSubject, out value, out detail);
                }

                if (TrySubject(field, ProgressSuffix, out string progressSubject))
                {
                    return TryReadProgress(world, progressSubject, out value, out detail);
                }

                detail = "the traversal course owns no field '" + field + "'";
                return false;
            }

            // ------------------------------------------------------------------ operations

            /// <summary>
            /// P-024's spawn: the neutral publication first (a scope no live target lives in, so its derivation
            /// carries no target change), then the spawn itself, then the composition index registration — exactly
            /// the three steps `Gc020Scenario`'s spawn path performs, so the future runner's first visible image is
            /// already its complete effective assembly with the modifier the course currently publishes.
            /// </summary>
            private ConformanceOperationResult SpawnFuture(ConformanceWorld world, int operand)
            {
                if (operand != 0)
                {
                    return Unsupported(
                        "the traversal course declares exactly one future runner (" + TraversalVocabulary.RunnerC
                        + "); operand " + operand.ToString(CultureInfo.InvariantCulture)
                        + " names no declared recipe (P-009)");
                }

                if (world.Pipeline == null || world.Targets == null || world.Seeder == null)
                {
                    return Unsupported("the world or its pipeline is missing");
                }

                TargetId target = FutureTarget;
                DefinitionRef recipe = FutureRecipe;
                ScopeId scope = FutureScope;
                PrepareSpawn();

                ConformanceOperationResult neutral = world.PublishEdit(
                    SpareScopeEdits[1], "spawn-neutral-publication");
                if (!neutral.Published)
                {
                    return neutral;
                }

                DerivedAssemblyReport spawn = world.Pipeline.PublishSpawn(
                    world.NextOperation(), target, recipe, scope);
                if (spawn.Outcome != DerivedAssemblyOutcome.Published)
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Refused,
                        "the spawn of " + TraversalVocabulary.RunnerC + " was refused: " + spawn.Describe());
                }

                if (!world.Targets.TryRegister(target, scope, recipe, out DiagnosticCode code, out string detail))
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Refused,
                        "the spawned runner could not be registered: " + code + ": " + detail);
                }

                if (!world.Seeder.TryGetEntity(target, out Entity entity) || entity == Entity.Null)
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Refused,
                        "the spawned runner has no live entity (P-005)");
                }

                // The runner the spawn created is a runner of this course from this publication on, so the module
                // that owns every runner's motion learns about it here: without this the integrator would not own
                // its pose, and a later step would silently skip it (P-005, P-034).
                TraversalModule? module = ModuleOf(world);
                if (module != null)
                {
                    module.BindRunner(target, entity, recipe);
                }

                return new ConformanceOperationResult(
                    ConformanceOperationOutcome.Published,
                    "spawned " + TraversalVocabulary.RunnerC + " fully assembled (P-024)");
            }

            /// <summary>
            /// Seeds and publishes the complete-opt-in runner exactly as the family's own sequence does: the
            /// descriptor's explicit opt-in is immutable assembly input (P-015), so it cannot arrive as a later edit,
            /// and the seeding rides on the neutral scope publication the family declares for it. The publication is
            /// also what derives this target's rows, because the composition index learns about it in the same step.
            /// </summary>
            private ConformanceOperationResult SeedOptedIn(ConformanceWorld world)
            {
                if (world.Host == null || world.Targets == null || world.Seeder == null)
                {
                    return Unsupported("the world or its target index is missing");
                }

                if (!SeedOptedInTarget(new Gc013WorldContext(world.Host, world.Targets, world.Seeder)))
                {
                    return Unsupported("seeding the explicitly opted-in runner was refused");
                }

                ConformanceOperationResult published = world.PublishEdit(
                    SpareScopeEdits[0], "seed-opted-in-publication");
                if (!published.Published)
                {
                    return published;
                }

                // The module bound every runner the seeding step created when the course runtime attached, and this
                // one is seeded after that, so the operation that seeds it is the one that tells the course runtime
                // about it — exactly as the spawn operation does for the future runner. Without it the course would
                // hold a runner whose motion no integration owns (P-005, P-034).
                TraversalModule? module = ModuleOf(world);
                if (module != null
                    && world.Seeder.TryGetEntity(OptedInTarget, out Entity entity)
                    && entity != Entity.Null)
                {
                    module.BindRunner(OptedInTarget, entity, OptedInRunnerRecipe);
                }

                return published;
            }

            /// <summary>
            /// P-016's exclusion on one target, selected by <paramref name="operand"/>: an isolation edit on the
            /// runner's own scope naming the acceleration capability and that one target, with the scope's existing
            /// isolation sets read back from the committed composition so the edit changes only what it says it
            /// changes. Operand 0 is the automatically eligible valley runner (the case the reference row names) and
            /// operand 1 is the ridge runner, the sibling-branch control.
            /// </summary>
            private ConformanceOperationResult ExcludeTarget(ConformanceWorld world, int operand)
            {
                if (world.Lane == null)
                {
                    return Unsupported("the world has no composition lane");
                }

                ScopeId scope;
                TargetId target;
                string label;
                switch (operand)
                {
                    case 0:
                        scope = ValleyRunnerScope;
                        target = TraversalCourseTargets.RunnerA;
                        label = "exclude-valley-runner";
                        break;

                    case 1:
                        scope = RidgeRunnerScope;
                        target = TraversalCourseTargets.RunnerB;
                        label = "exclude-ridge-runner";
                        break;

                    default:
                        return Unsupported(
                            "the traversal course declares a target exclusion for its two declared runners only"
                            + " (operand 0 = " + TraversalVocabulary.RunnerA + ", operand 1 = "
                            + TraversalVocabulary.RunnerB + "); operand "
                            + operand.ToString(CultureInfo.InvariantCulture) + " names no declared runner");
                }

                if (!world.Lane.Committed.Scopes.TryGet(scope, out ScopeRecord? record) || record == null)
                {
                    return Unsupported("the excluded runner's scope is not part of the committed composition");
                }

                var payload = new CompositionEditPayload(
                    CompositionEditSubject.ScopeIsolation,
                    scope,
                    record.Parent,
                    false,
                    record.ServiceIsolation,
                    record.CapabilityIsolation,
                    new List<ExclusionRule>
                    {
                        new ExclusionRule(
                            ExclusionTargetKind.Capability,
                            TraversalVocabulary.AccelerationCapability.Value,
                            scope,
                            target,
                            false),
                    },
                    null,
                    default(PluginTypeId),
                    default(PluginInstanceId),
                    DefinitionRevision.Zero,
                    ContentHash.Empty,
                    null,
                    0,
                    null,
                    PropagationMode.Automatic);

                return world.PublishEdit(payload, label);
            }

            /// <summary>
            /// 07 s4.3's integration sequence: `operand` fixed steps with zero horizontal input, each advancing the
            /// course's own declared step exactly once (P-036). The modifiers contribute their derived acceleration
            /// through the integrator's read of the published binding row, which is why this is the row's operation
            /// and not a command envelope.
            /// </summary>
            private ConformanceOperationResult IntegrateSteps(ConformanceWorld world, int operand)
            {
                int steps = operand <= 0 ? 1 : operand;
                ConformanceOperationResult last = Unsupported("no step was pumped");
                for (int i = 0; i < steps; i++)
                {
                    last = world.PumpDeclaredStep("integrate");
                    if (!last.Published)
                    {
                        return last;
                    }
                }

                return last;
            }

            /// <summary>
            /// A movement envelope the course's own rules reject: it names a live target that is not a runner of this
            /// course, so `traversal.input` rejects it with the domain's own code and writes nothing (P-042). The
            /// refusal is observed, not assumed: the step must commit, the module must have recorded the rejection,
            /// and no runner may have captured input. Anything else is reported as unsupported rather than as the
            /// refusal the row would want.
            /// </summary>
            private ConformanceOperationResult RejectMovement(ConformanceWorld world)
            {
                if (world.Host == null)
                {
                    return Unsupported("the world is missing");
                }

                TraversalModule? module = ModuleOf(world);
                int rejectionsBefore = module != null ? module.InputRejectionCount : 0;
                var envelope = new CommandEnvelope(
                    world.NextOperation(),
                    MovementRoute,
                    TraversalCourseTargets.CheckpointOne,
                    MovementSchema,
                    null,
                    TraversalCommandCodec.WriteInput(0, 0, 0));

                CommandAdmissionReceipt receipt = world.Host.Submit(envelope);
                if (!receipt.Admitted)
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Refused,
                        "the movement envelope was refused at admission (" + receipt.Result.Kind + "/"
                        + DiagnosticCodeText.Of(receipt.Result.Reason) + ")");
                }

                ConformanceOperationResult pump = world.PumpDeclaredStep("commit-command-rejected");
                if (!pump.Published)
                {
                    return pump;
                }

                bool domainRefused = module != null && module.InputRejectionCount == rejectionsBefore + 1;
                if (domainRefused && !AnyCapturedInput(world))
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Refused,
                        "traversal.input rejected the movement envelope for " + TraversalVocabulary.CheckpointOne
                        + ", which this course does not own as a runner, and no runner captured input");
                }
                return Unsupported(
                    "the movement envelope for a non-runner target was admitted without the input stage's refusal"
                    + " being observed, so no refusal can be reported");
            }

            // ------------------------------------------------------------------ readings

            /// <summary>
            /// The effective `traversal.acceleration` value of one target, read from the published assembly: the
            /// active row of slot 0, or `none` when nothing supports it (P-019, P-030). An absent row is an observed
            /// absence — 07:240's "the additional x acceleration is 0" — so it is a successful read of `none`; a
            /// world that publishes no assembly at all is a miss.
            /// </summary>
            private bool TryReadAccelerationX(
                ConformanceWorld world, string subject, out string value, out string detail)
            {
                value = ConformanceValue.None;
                detail = string.Empty;
                if (!TryResolveSubject(subject, out TargetId target, out bool isRunner))
                {
                    detail = "the traversal course declares no target '" + subject + "'";
                    return false;
                }

                if (!TryReadAccelerationRow(world, target, out CapabilityBinding row, out string miss))
                {
                    if (world.Publisher == null)
                    {
                        detail = miss;
                        return false;
                    }

                    value = ConformanceValue.None;
                    detail = isRunner
                        ? miss
                        : subject + " is a checkpoint volume of the course: it declares only the sensor contract, so"
                            + " no acceleration modifier selects it (07 s4.1)";
                    return true;
                }

                value = ConformanceValue.Int(row.Value);
                return true;
            }

            /// <summary>
            /// The stable name of the installation supporting one target's acceleration row, so a provider change is
            /// observable as a name rather than as a hex identity (P-004, P-017). A target with no active row has no
            /// provider to name, so that read is a miss with its reason.
            /// </summary>
            private bool TryReadAccelerationProvider(
                ConformanceWorld world, string subject, out string value, out string detail)
            {
                value = ConformanceValue.None;
                detail = string.Empty;
                if (!TryResolveSubject(subject, out TargetId target, out bool _))
                {
                    detail = "the traversal course declares no target '" + subject + "'";
                    return false;
                }

                if (!TryReadAccelerationRow(world, target, out CapabilityBinding row, out detail))
                {
                    return false;
                }

                // The provider identity is compared against this course's own installation identities, so the label
                // is the catalog's stable name and never a hex id (P-004).
                if (row.Provider.Value.Equals(TraversalKeys.Instance(TraversalVocabulary.Tailwind).Value))
                {
                    value = TraversalVocabulary.Tailwind;
                    return true;
                }

                if (row.Provider.Value.Equals(TraversalKeys.Instance(TraversalVocabulary.Headwind).Value))
                {
                    value = TraversalVocabulary.Headwind;
                    return true;
                }

                if (row.Provider.Value.Equals(TraversalKeys.Instance(ConformanceNestedModifier).Value))
                {
                    value = ConformanceNestedModifier;
                    return true;
                }

                detail = "the traversal.acceleration row for " + target.ToString()
                    + " names an installation this run does not declare";
                return false;
            }

            /// <summary>07 s4.2's `KinematicPose`, as the canonical `(x,y,z)` token in millimetres.</summary>
            private bool TryReadPose(ConformanceWorld world, string subject, out string value, out string detail)
            {
                value = ConformanceValue.None;
                if (!TryResolveRunner(subject, out TargetId target, out detail))
                {
                    return false;
                }

                if (!TryRunnerEntity(world, target, out Entity runner, out detail))
                {
                    return false;
                }

                EntityManager entityManager = world.Host!.EntityWorld.EntityManager;
                value = entityManager.GetComponentData<TraversalPose>(runner).Vector.ToString();
                return true;
            }

            /// <summary>07 s4.2's `Velocity`, as the canonical `(x,y,z)` token in thousandths of a metre per second.</summary>
            private bool TryReadVelocity(ConformanceWorld world, string subject, out string value, out string detail)
            {
                value = ConformanceValue.None;
                if (!TryResolveRunner(subject, out TargetId target, out detail))
                {
                    return false;
                }

                if (!TryRunnerEntity(world, target, out Entity runner, out detail))
                {
                    return false;
                }

                EntityManager entityManager = world.Host!.EntityWorld.EntityManager;
                value = entityManager.GetComponentData<TraversalVelocity>(runner).Vector.ToString();
                return true;
            }

            /// <summary>
            /// 07 s4.2's `JumpState`: `0` while the run has never jumped, otherwise the logical step of its last
            /// accepted jump. `JumpCount` is the discriminator, because a jump accepted in step zero would otherwise
            /// be indistinguishable from a run that never jumped (P-044).
            /// </summary>
            private bool TryReadJump(ConformanceWorld world, string subject, out string value, out string detail)
            {
                value = ConformanceValue.None;
                if (!TryResolveRunner(subject, out TargetId target, out detail))
                {
                    return false;
                }

                if (!TryRunnerEntity(world, target, out Entity runner, out detail))
                {
                    return false;
                }

                EntityManager entityManager = world.Host!.EntityWorld.EntityManager;
                TraversalJumpState jump = entityManager.GetComponentData<TraversalJumpState>(runner);
                value = jump.JumpCount == 0U
                    ? "0"
                    : ConformanceValue.UInt(jump.LastJumpStep);
                return true;
            }

            /// <summary>
            /// 07 s4.2's `RunProgress.Count` for one runner, read from the course entity that owns the buffer. A
            /// runner with no row yet is a fresh run, whose count is the declared zero rather than a missing
            /// observation; the course entity not being bound at all is a miss.
            /// </summary>
            private bool TryReadProgress(ConformanceWorld world, string subject, out string value, out string detail)
            {
                value = ConformanceValue.None;
                if (!TryResolveRunner(subject, out TargetId target, out detail))
                {
                    return false;
                }

                TraversalModule? module = ModuleOf(world);
                if (module == null)
                {
                    detail = "the traversal course module is not attached to this world (P-043)";
                    return false;
                }

                EntityManager entityManager = world.Host!.EntityWorld.EntityManager;
                Entity course = module.CourseEntity;
                if (course == Entity.Null
                    || !entityManager.Exists(course)
                    || !entityManager.HasBuffer<TraversalProgressRow>(course))
                {
                    detail = "the course entity that owns RunProgress is not bound in this world (P-043)";
                    return false;
                }

                if (!TraversalAccess.TryReadProgress(entityManager, course, target, out TraversalProgressRow row))
                {
                    value = "0";
                    detail = "no run-progress row exists for " + target.ToString() + " yet";
                    return true;
                }

                value = ConformanceValue.UInt(row.Count);
                return true;
            }

            // ------------------------------------------------------------------ helpers

            /// <summary>
            /// The active `traversal.acceleration` slot-0 row of one target, or a miss with its reason: no
            /// published assembly, or no active row (which is the honest absence of a contribution, P-015).
            /// </summary>
            private static bool TryReadAccelerationRow(
                ConformanceWorld world, TargetId target, out CapabilityBinding row, out string miss)
            {
                row = default(CapabilityBinding);
                miss = string.Empty;
                if (world.Publisher == null)
                {
                    miss = "the world publishes no assembly";
                    return false;
                }

                IReadOnlyList<CapabilityBinding> rows = world.Publisher.ReadBindingRows(target);
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].IsActive
                        && rows[i].Capability.Equals(TraversalVocabulary.AccelerationCapability)
                        && rows[i].OutputSlot == 0U)
                    {
                        row = rows[i];
                        return true;
                    }
                }

                miss = "no active traversal.acceleration row is published for " + target.ToString();
                return false;
            }

            /// <summary>The course module of one live world: the entity map and progress owner the stages use.</summary>
            private static TraversalModule? ModuleOf(ConformanceWorld world) =>
                world.Runtime != null && world.Runtime.Traversal != null ? world.Runtime.Traversal.Module : null;

            /// <summary>
            /// Resolves one field subject to a live runner target. A subject this course does not declare is a miss
            /// with its reason, so a misspelled field can never read as an absent row.
            /// </summary>
            private static bool TryResolveRunner(string subject, out TargetId target, out string detail)
            {
                detail = string.Empty;
                if (!TryResolveSubject(subject, out target, out bool isRunner))
                {
                    detail = "the traversal course declares no target '" + subject + "'";
                    return false;
                }

                if (!isRunner)
                {
                    detail = subject + " is a checkpoint volume of the course, not a runner (07 s4.1)";
                    return false;
                }

                return true;
            }

            /// <summary>
            /// One field subject as one of the course's declared targets (07 s4.1's tree plus the complete-opt-in
            /// runner). <paramref name="isRunner"/> distinguishes the two target kinds, so a motion read on a
            /// checkpoint is a reported miss rather than a component lookup that would throw.
            /// </summary>
            private static bool TryResolveSubject(string subject, out TargetId target, out bool isRunner)
            {
                target = default(TargetId);
                isRunner = false;
                switch (subject)
                {
                    case TraversalVocabulary.RunnerA:
                        target = TraversalCourseTargets.RunnerA;
                        isRunner = true;
                        return true;

                    case TraversalVocabulary.RunnerB:
                        target = TraversalCourseTargets.RunnerB;
                        isRunner = true;
                        return true;

                    case TraversalVocabulary.RunnerC:
                        target = TraversalCourseTargets.RunnerC;
                        isRunner = true;
                        return true;

                    case TraversalVocabulary.RunnerDisplay:
                        target = TraversalCourseTargets.RunnerDisplay;
                        isRunner = true;
                        return true;

                    case TraversalVocabulary.CheckpointOne:
                        target = TraversalCourseTargets.CheckpointOne;
                        isRunner = false;
                        return true;

                    case ConformanceOptedInRunner:
                        target = Gc020TraversalHost.OptedInTarget;
                        isRunner = true;
                        return true;

                    default:
                        return false;
                }
            }

            /// <summary>
            /// The module's own entity for one runner, with its motion storage checked: the module is the course's
            /// runtime map, so a target it does not hold is not a runner of this world (P-005, P-043).
            /// </summary>
            private static bool TryRunnerEntity(
                ConformanceWorld world, TargetId target, out Entity runner, out string detail)
            {
                runner = Entity.Null;
                detail = string.Empty;
                TraversalModule? module = ModuleOf(world);
                if (module == null)
                {
                    detail = "the traversal course module is not attached to this world (P-043)";
                    return false;
                }

                if (!module.TryRunner(target, out runner) || runner == Entity.Null)
                {
                    detail = target.ToString() + " is not a live runner of this course (P-005)";
                    return false;
                }

                EntityManager entityManager = world.Host!.EntityWorld.EntityManager;
                if (!entityManager.Exists(runner)
                    || !entityManager.HasComponent<TraversalPose>(runner)
                    || !entityManager.HasComponent<TraversalVelocity>(runner)
                    || !entityManager.HasComponent<TraversalJumpState>(runner))
                {
                    detail = target.ToString() + " carries no runner motion storage (P-005)";
                    return false;
                }

                return true;
            }

            /// <summary>
            /// True while any runner of the course holds captured input, which is what "no live write" means for a
            /// refused movement envelope: the input stage clears every runner's capture at the start of a step and
            /// only an accepted envelope sets it again (07 s4.2, P-043).
            /// </summary>
            private static bool AnyCapturedInput(ConformanceWorld world)
            {
                TraversalModule? module = ModuleOf(world);
                if (module == null || world.Host == null)
                {
                    return false;
                }

                EntityManager entityManager = world.Host.EntityWorld.EntityManager;
                IReadOnlyList<TraversalRunnerRef> runners = module.Runners();
                for (int i = 0; i < runners.Count; i++)
                {
                    Entity runner = runners[i].Entity;
                    if (!entityManager.Exists(runner)
                        || !entityManager.HasComponent<TraversalMovementInput>(runner))
                    {
                        continue;
                    }

                    if (entityManager.GetComponentData<TraversalMovementInput>(runner).Captured != 0)
                    {
                        return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// O-06/O-04's lifecycle edit. The traversal package declares no suspend/resume builder (its
            /// `TraversalPayloads` owns mount, unmount, mode, scope-create and reparent only), so this mirrors the
            /// shape `CardLifecyclePayloads.{Suspend,Resume}` and `Gc019Scenario.LifecycleEdit` build: one named
            /// installation, no scope, no configuration and no payload (P-046).
            /// </summary>
            private static CompositionEditPayload LifecycleEdit(
                CompositionEditSubject subject, PluginInstanceId instance)
            {
                if (instance.IsDefault)
                {
                    throw new ArgumentException(
                        "a lifecycle edit names one real installation identity (P-004).", nameof(instance));
                }

                return new CompositionEditPayload(
                    subject,
                    default(ScopeId),
                    default(ScopeId),
                    false,
                    null,
                    null,
                    null,
                    null,
                    default(PluginTypeId),
                    instance,
                    DefinitionRevision.Zero,
                    ContentHash.Empty,
                    null,
                    0,
                    null,
                    PropagationMode.Automatic);
            }

            /// <summary>One field key with its suffix removed, so the subject can be resolved without string surgery.</summary>
            private static bool TrySubject(string field, string suffix, out string subject)
            {
                subject = string.Empty;
                if (!field.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return false;
                }

                subject = field.Substring(0, field.Length - suffix.Length);
                return subject.Length != 0;
            }

            private static ConformanceOperationResult Unsupported(string detail)
                => new ConformanceOperationResult(ConformanceOperationOutcome.Unsupported, detail);
        }
    }
}
