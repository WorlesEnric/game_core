// GameCore.Validation.ProbeHost — the Wave 6 gate's traversal family adapter.
//
// The third genre GC-020 added, and the one the gate's fixed-step, cost-counter and replay observations run on.
// `Gc020TraversalHost.CourseFamily` already declares the course as `IGc020Family` (its catalog data, its course scope
// tree, its live runners and volumes, its two acceleration modifiers, its fixed-step identities and its real stage
// runtime with the local physics scene); this part adds only the two catalog entry points and the `IW6Family`
// answers.
//
// The traversal course does not satisfy `IGc019Family` (its `AttachStageRuntime` is `IGc020Family`'s, which returns
// `Gc020StageRuntime`; a second member with that signature would be a duplicate, not an overload), so it answers the
// gate's own cycle contract directly with the course's own movement route, payload and runner target — the same
// identities `Gc020Scenario` submits. Its four cycle manifests are acceleration modifiers of the course's own
// declared shape, mounted and unmounted under a fresh instance identity every cycle, which is exactly what GC-022's
// stress does for the other two genres (P-009, P-046).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Composition;
using GameCore.Gameplay.Traversal;
using GameCore.Gameplay.Traversal.Fixtures;
using GameCore.Rules.Traversal;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;

namespace GameCore.Validation.ProbeHost
{
    public static partial class Gc020TraversalHost
    {
        /// <summary>
        /// Digest the traversal gate run must report over its seven named observations, all passing. It is computed
        /// from the frozen name table, so a renamed, reordered, added or dropped observation changes the literal and
        /// the gate cannot silently shrink (P-008).
        ///
        /// There is exactly one literal, not two: this revision has no committed generated traversal catalog (its
        /// emission is GC-025's catalog-coverage work), so the gate runs the fixture catalog once and says so rather
        /// than implying a second catalog ran (see <see cref="GeneratedCatalogPresent"/>).
        /// </summary>
        public const string W6GateDigest =
            "ca29b5f7099f9fbc4abeb7031c7b1e35ba39ec1ad39db70dd0f037fa8d45675e";

        /// <summary>Runs the Wave 6 gate over the traversal course's hand-written generated-style catalog.</summary>
        public static W6GateScenarioResult RunW6Gate()
        {
            CatalogBuildResult build = TraversalCatalogTable.Build();
            ImmutableCatalog? catalog = build.Catalog;
            if (catalog == null)
            {
                throw new InvalidOperationException(
                    "the hand-written generated-style traversal catalog was rejected: " + build.Describe());
            }

            return W6GateScenario.Run(
                new CourseFamily(catalog, Declarations(), TraversalCatalogTable.Fingerprint().ToHex()),
                false);
        }

        /// <summary>
        /// Runs the one catalog this revision has and answers both out-parameters with it, exactly as
        /// <see cref="RunBoth"/> does: the gate's digest step then compares two references that are provably the same
        /// run rather than pretending a second catalog exists (P-060).
        /// </summary>
        public static IReadOnlyList<W6GateStep> RunBothW6Gate(
            out W6GateScenarioResult generated,
            out W6GateScenarioResult fixture)
        {
            fixture = RunW6Gate();
            generated = fixture;
            return fixture.Steps;
        }

        /// <summary>
        /// The traversal course's reload route, which the Play Mode reload matrix drives once per session: it builds
        /// one real course world, commits one admitted step through the course's own movement route and disposes it.
        /// The matrix's {domain, scene} combinations therefore cover the third genre as well as the application world,
        /// which is what "extend the reload matrix to include traversal" means for the Wave 6 gate (TEST-018).
        /// Returns a detail string beginning with <c>pass</c> or <c>fail</c>.
        /// </summary>
        public static string RunReloadRoute()
        {
            CatalogBuildResult build = TraversalCatalogTable.Build();
            ImmutableCatalog? catalog = build.Catalog;
            if (catalog == null)
            {
                return "fail; the hand-written generated-style traversal catalog was rejected: " + build.Describe();
            }

            return W6GateScenario.RunReloadRoute(
                new CourseFamily(catalog, Declarations(), TraversalCatalogTable.Fingerprint().ToHex()));
        }

        /// <summary>
        /// The Wave 6 gate half of the traversal course: the three-family cycle contract's traversal answers. The
        /// course's movement route, payload and asserted runner are the ones `Gc020Scenario` submits; the four cycle
        /// manifests are acceleration modifiers of the course's own declared shape.
        /// </summary>
        public sealed partial class CourseFamily : IW6Family
        {
            private LifecycleStressDeclarations? w6CycleDeclarations;

            private LifecycleStressDeclarations W6CycleDeclarations =>
                w6CycleDeclarations ??= BuildW6CycleDeclarations();

            LifecycleStressDeclarations IW6Family.StressDeclarations => W6CycleDeclarations;

            /// <summary>
            /// One fresh installation identity per cycle, from the course's own stable-name derivation so a
            /// fixture-derived identity and a package-derived one cannot disagree (P-004).
            /// </summary>
            PluginInstanceId IW6Family.StressInstance(ulong ordinal) =>
                TraversalKeys.Instance("w6gate.cycle-instance." + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ScopeId IW6Family.CycleMountScope => ProviderScope;

            CompositionEditPayload IW6Family.StressMount(
                PluginManifest manifest,
                PluginInstanceId instance,
                ScopeId scope) => TraversalCourseComposition.MountModifier(manifest, instance, scope);

            CompositionEditPayload IW6Family.StressUnmount(PluginInstanceId instance) =>
                TraversalCourseComposition.Unmount(instance);

            /// <summary>The course's declared movement route: the one command a cycle admits (P-042).</summary>
            RouteId IW6Family.CycleRoute => MovementRoute;

            /// <summary>The valley runner the one-step numeric assertion reads: the cycle's command target.</summary>
            TargetId IW6Family.CycleTarget => VelocityAssertedTarget;

            /// <summary>The movement command schema the course's own compile unit registers.</summary>
            SchemaRef IW6Family.CycleSchema => MovementSchema;

            /// <summary>
            /// One captured movement sample encoded exactly the way the course's own reader decodes it: no extra
            /// horizontal acceleration and no jump, so the only acceleration in play is the derived configuration the
            /// cycle's mount published (P-042, 07 s4.3).
            /// </summary>
            FrozenPayload IW6Family.CyclePayload(ulong ordinal)
            {
                _ = ordinal;
                return MovementPayload(0, 0);
            }

            /// <summary>The course is a fixed-step world: one cycle advances exactly its declared step (P-036).</summary>
            ulong IW6Family.CyclePumpTicks => StepDurationTicks;

            W6StageRuntime IW6Family.AttachGateRuntime(
                UnityWorldHost host,
                PipelineDescriptorReport descriptor,
                LiveTargetIndex targets,
                LiveTargetSeeder seeder) =>
                new W6StageRuntime(
                    Label,
                    null,
                    Gc020StageRuntime.Attach(Label, host, CourseTarget, descriptor, targets, seeder, installPhysics: true));

            /// <summary>
            /// The four manifests one cycle may mount, in the course's own acceleration-modifier declaration shape
            /// (`TraversalDeclarations.AccelerationModifier`), with this gate's own plugin types, factory keys, names
            /// and rule identities. Each declares no stage, buffer or state slot of its own, so none of them enters
            /// the compiled schedule — the same shape GC-022's stress declarations have for the other two genres
            /// (P-009, P-019, P-043).
            /// </summary>
            private LifecycleStressDeclarations BuildW6CycleDeclarations()
            {
                return new LifecycleStressDeclarations(
                    installation: W6CycleManifest("installation", new TraversalVector3i(1, 0, 0)),
                    serviceConsumer: W6CycleManifest("consumer", new TraversalVector3i(2, 0, 0)),
                    serviceProvider: W6CycleManifest("provider", new TraversalVector3i(3, 0, 0)),
                    fenced: W6CycleManifest("fenced", new TraversalVector3i(4, 0, 0)),
                    stage: TraversalKeys.InputStage,
                    system: TraversalKeys.InputSystem);
            }

            private static PluginManifest W6CycleManifest(string role, TraversalVector3i acceleration)
            {
                string name = "w6gate.cycle-modifier." + role;
                return TraversalDeclarations.AccelerationModifier(
                    TraversalKeys.PluginType(name),
                    TraversalKeys.PluginFactoryKey,
                    TraversalKeys.ConfigSchema,
                    name,
                    acceleration);
            }
        }
    }
}
