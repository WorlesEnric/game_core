#nullable enable
using System;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// Parsed probe command line. Custom arguments are passed after the standard Unity player arguments
    /// (<c>-batchmode -nographics -logFile ...</c>) and are read from <see cref="Environment.GetCommandLineArgs"/>.
    /// </summary>
    public readonly struct ProbeArguments
    {
        private const string ResultArgumentName = "-probeResult";
        private const string MissingRegistrationArgumentName = "-probeMissingRegistration";
        private const string WorldDispatchArgumentName = "-probeWorldDispatch";
        private const string W1GateArgumentName = "-probeW1Gate";
        private const string W2GateArgumentName = "-probeW2Gate";
        private const string W3GateArgumentName = "-probeW3Gate";
        private const string NarrativeArgumentName = "-probeNarrative";
        private const string CardsArgumentName = "-probeCards";
        private const string W4ProfileArgumentName = "-probeW4Profile";
        private const string Gc013ArgumentName = "-probeGc013";
        private const string W4GateArgumentName = "-probeW4Gate";
        private const string FaultsArgumentName = "-probeFaults";
        private const string Gc018ArgumentName = "-probeGc018";
        private const string Gc019ArgumentName = "-probeGc019";
        private const string W5GateArgumentName = "-probeW5Gate";
        private const string TraversalArgumentName = "-probeTraversal";
        private const string Gc021ArgumentName = "-probeGc021";
        private const string RecoveryArgumentName = "-probeRecovery";
        private const string LifecycleStressArgumentName = "-probeLifecycleStress";
        private const string ReplayArgumentName = "-probeReplay";
        private const string W6GateArgumentName = "-probeW6Gate";
        private const string CatalogCoverageArgumentName = "-probeCatalogCoverage";

        private ProbeArguments(
            bool missingRegistration,
            bool worldDispatch,
            bool w1Gate,
            bool w2Gate,
            bool w3Gate,
            bool narrative,
            bool cards,
            bool w4Profile,
            bool gc013,
            bool lifecycleStress,
            bool w4Gate,
            bool faults,
            bool gc018,
            bool gc019,
            bool w5Gate,
            bool traversal,
            bool gc021,
            bool recovery,
            bool replay,
            bool w6Gate,
            bool catalogCoverage,
            string? resultPath)
        {
            MissingRegistration = missingRegistration;
            WorldDispatch = worldDispatch;
            W1Gate = w1Gate;
            W2Gate = w2Gate;
            W3Gate = w3Gate;
            Narrative = narrative;
            Cards = cards;
            W4Profile = w4Profile;
            Gc013 = gc013;
            LifecycleStress = lifecycleStress;
            W4Gate = w4Gate;
            Faults = faults;
            Gc018 = gc018;
            Gc019 = gc019;
            W5Gate = w5Gate;
            Traversal = traversal;
            Gc021 = gc021;
            Recovery = recovery;
            Replay = replay;
            W6Gate = w6Gate;
            CatalogCoverage = catalogCoverage;
            ResultPath = resultPath;
        }

        /// <summary>Runs the negative mode that requires an explicit missing-registration result.</summary>
        public bool MissingRegistration { get; }

        /// <summary>
        /// Runs the GC-005 owned-world mode: guarded dispatch, fail-stop, idle command-driven steps, two independent
        /// worlds and the fixed-step debt clock, all inside the standalone player (TEST-011, TEST-018).
        /// </summary>
        public bool WorldDispatch { get; }

        /// <summary>
        /// Runs the W1 integration gate: two owned worlds, one admitted operation executed as a guarded stage, a
        /// thrown post-write exception that stops the next stage and publication, and an idle second world.
        /// </summary>
        public bool W1Gate { get; }

        /// <summary>
        /// Runs the W2 integration gate: a mounted provider derived and published into a real world, one bounded
        /// command committed, a compiled schedule executed with GC-009's temporal drivers, a future target spawned
        /// fully assembled and an idle world that performs no step.
        /// </summary>
        public bool W2Gate { get; }

        /// <summary>
        /// Runs the W3 integration gate: the narrative composition and the card composition, each in its own world in
        /// one process on the same kernel assemblies, with zero idle command steps in both, automatic existing and
        /// future targets in both, the narrative state change observed through the committed snapshot, the card
        /// domain transfer committed atomically, and the kernel-separation audit.
        /// </summary>
        public bool W3Gate { get; }

        /// <summary>
        /// Runs the GC-010 narrative vertical slice: the chapter providers mounted over a real catalog, the derived
        /// binding layout published into a real world, one choice command committed with its durable fact, the
        /// chapter-two mount, the spawned target, an idle world that performs no step and the genre neutrality audit.
        /// </summary>
        public bool Narrative { get; }

        /// <summary>
        /// Runs the GC-011 card-game Automatic vertical slice: the market's scope tree and its provider mounts, the
        /// inherited scoring modifier on every eligible existing seat, one bounded command that commits both sides,
        /// a duplicate that transfers once, a rejected settlement that changes nothing, a transfer that commits both
        /// sides, a future seat that inherits the modifier before its first step, an idle world that performs no step
        /// and a teardown that settles and disposes.
        /// </summary>
        public bool Cards { get; }

        /// <summary>
        /// Runs the GC-012 Wave 4 provisional generic-execution profile gate: the two generated inactive family
        /// entries resolving by key, both families over their committed generated catalog and their hand-written
        /// fixture catalog with canonical comparison, the P-017/P-019 multi-supporter slot in a live world, and the
        /// kernel separation audit.
        /// </summary>
        public bool W4Profile { get; }

        /// <summary>
        /// Runs the GC-013 wave-4 transition mode: the GC-010 narrative composition and the GC-011 card composition,
        /// each over the committed generated catalog and over its hand-written generated-style catalog, with the
        /// providers mounted in Automatic, a branch reparented while its identity and live state survive, both
        /// propagation-mode directions applied, a future target spawned in Conservative, an exclusive conflict
        /// refused with the old mode and membership intact, and the isolated branch unchanged throughout
        /// (P-013, P-014, P-016, P-025).
        /// </summary>
        public bool Gc013 { get; }

        /// <summary>Runs the integrated Wave 4 gate over both families and both catalogs.</summary>
        public bool W4Gate { get; }

        /// <summary>
        /// Runs the GC-017 fault-boundary mode: every named observation of TEST-016's apply/cancellation matrix over
        /// both families, each family over its committed generated catalog and over its hand-written
        /// generated-style catalog, inside the stripped player.
        /// </summary>
        public bool Faults { get; }

        /// <summary>
        /// Runs the GC-018 checkpoint round-trip mode: a committed-boundary capture with an explicit queued-command
        /// disposition, the tampered/truncated/unknown-schema/ambiguous-migration/corrupt-reference refusals, and a
        /// restore into a fresh unexposed world that keeps active and dormant state, the mode, the boundaries, the
        /// clocks and the cursors while refusing every old handle (P-032, P-049, P-053, P-054).
        /// </summary>
        public bool Gc018 { get; }

        /// <summary>
        /// Runs the GC-019 adapter mode: stamped input submitted through the world's own command port, a bounded
        /// asynchronous asset lease, committed-image presentation with stable views, the visual-reparent/composition
        /// separation and adapter teardown in the lifecycle, over both families.
        /// </summary>
        public bool Gc019 { get; }

        /// <summary>
        /// Runs the Wave 5 integration gate: retained observation, deterministic faults, checkpoint restore and the
        /// common adapters joined in one actual world per family — prewrite rejection with the old assembly intact,
        /// a postwrite fail-stop with no further step or image, a checkpoint captured at the committed boundary and
        /// restored into a new session, read-only pinned snapshots that leak no writable reference, and a late asset
        /// callback from the retired world rejected (P-007, P-027..P-031, P-045, P-047..P-055).
        /// </summary>
        public bool W5Gate { get; }

        /// <summary>
        /// Runs the GC-020 real-time action reference: the fixed-step traversal course with its owner/stage policies,
        /// its inherited Additive acceleration modifier, its data-defined checkpoint volumes, the committed crossing
        /// output, and the optional engine stages — one local `PhysicsScene` simulation per admitted step, committed
        /// animation output and committed audio output whose sink is engine-free because the headless player has audio
        /// disabled (P-034, P-036, P-039..P-041, P-044, P-045, P-059).
        /// </summary>
        public bool Traversal { get; }

        /// <summary>
        /// Runs the GC-021 durable-delivery mode: the delivery key derivation, a durable commit that is persisted
        /// before it is applied, a deterministic crash at the seam's own after-delivery boundary, the redelivery that
        /// applies the destination mutation exactly once, capacity exhaustion that is never a silent drop, the
        /// volatile/durable distinction, a committed obligation that outlives the unload of its world, a checkpoint
        /// that carries the outbox and its cursor, the absence of a universal effect API, and the reward bridge's one
        /// committed choice becoming one durable, idempotent card mutation (P-003, P-043, P-045, P-050, P-053).
        /// </summary>
        public bool Gc021 { get; }

        /// <summary>
        /// Runs the GC-027 checkpoint-and-recovery mode: the captured checkpoint that is verified before it is used,
        /// the fault refusals at the capture-copy, publication, reference-repair, postwrite-apply and
        /// recovery-publication seams, a recovery into a new session at different native handles with the active and
        /// dormant state intact, the outbox and its delivery cursor carried across, the outbox-append, delivery and
        /// acknowledgement faults, a restart from the store alone, a transient failure retried under the host's own
        /// bound, and a full teardown (P-030, P-045, P-049, P-052, P-053).
        /// </summary>
        public bool Recovery { get; }

        /// <summary>
        /// Runs the GC-022 lifecycle stress: the counted mount/unmount cycles over each family's committed generated
        /// catalog and over its fixture identity set, with delayed completions, stalled jobs, a throwing disposer,
        /// required-provider churn and headless cleanup, under native leak detection with full stack traces
        /// (P-047, P-048, P-050).
        /// </summary>
        public bool LifecycleStress { get; }

        /// <summary>
        /// Runs the GC-023 replay mode: the recorded 10,000-step integer fixture replayed across the supported
        /// worker counts and under a shuffled producer/completion order, the differential propagation sweep with its
        /// reducer, the observation replay separated from the native-physics comparison, and the instrumented
        /// counters of one real owned world with the raw benchmark trace written beside the probe result
        /// (P-008, P-023, TEST-022, TEST-023).
        /// </summary>
        public bool Replay { get; }

        /// <summary>
        /// Runs the Wave 6 integration-gate mode: the fixed-step traversal course with the cost counters and the
        /// recorded-input replay, the durable reward delivery across an unload/reload of its receiving world, the
        /// composition audit that keeps the optional physics/animation/audio surface out of cards and narrative, and
        /// the create/mount/step/unmount/teardown loop over all three genres (W6-GATE).
        /// </summary>
        public bool W6Gate { get; }

        /// <summary>
        /// Runs the GC-025 catalog coverage mode: the committed reachability manifest against the live generated
        /// catalogs, every generated registration/serializer/closed-generic root executed in the player, the
        /// generated traversal catalog against its hand-written counterpart, the inactive plugin late mount, the
        /// editor-baked and runtime-recipe materializations of the traversal course, the refused unknown/stale
        /// recipes, a stopped-and-restarted world host and the headless reference execution against the pure-rule
        /// canonical fixtures (P-009, P-054, P-058; TEST-001, TEST-020).
        /// </summary>
        public bool CatalogCoverage { get; }

        /// <summary>Destination path of the structured JSON result.</summary>
        public string? ResultPath { get; }

        /// <summary>True when the process was launched as a probe rather than as a normal player run.</summary>
        public bool IsProbeInvocation =>
            MissingRegistration || WorldDispatch || W1Gate || W2Gate || W3Gate || Narrative || Cards || W4Profile
            || Gc013 || W4Gate || Faults || Gc018 || Gc019 || W5Gate || Traversal || Gc021 || Recovery
            || LifecycleStress
            || Replay
            || W6Gate
            || CatalogCoverage
            || !string.IsNullOrEmpty(ResultPath);

        /// <summary>True when a result destination was supplied; without it the probe cannot record evidence.</summary>
        public bool HasResultPath => !string.IsNullOrEmpty(ResultPath);

        public static ProbeArguments Parse(string[] arguments)
        {
            bool missingRegistration = false;
            bool worldDispatch = false;
            bool w1Gate = false;
            bool w2Gate = false;
            bool w3Gate = false;
            bool narrative = false;
            bool cards = false;
            bool w4Profile = false;
            bool gc013 = false;
            bool lifecycleStress = false;
            bool w4Gate = false;
            bool faults = false;
            bool gc018 = false;
            bool gc019 = false;
            bool w5Gate = false;
            bool traversal = false;
            bool gc021 = false;
            bool recovery = false;
            bool replay = false;
            bool w6Gate = false;
            bool catalogCoverage = false;
            string? resultPath = null;
            for (int i = 0; i < arguments.Length; i++)
            {
                string argument = arguments[i];
                if (argument == MissingRegistrationArgumentName)
                {
                    missingRegistration = true;
                }
                else if (argument == WorldDispatchArgumentName)
                {
                    worldDispatch = true;
                }
                else if (argument == W1GateArgumentName)
                {
                    w1Gate = true;
                }
                else if (argument == W2GateArgumentName)
                {
                    w2Gate = true;
                }
                else if (argument == W3GateArgumentName)
                {
                    w3Gate = true;
                }
                else if (argument == NarrativeArgumentName)
                {
                    narrative = true;
                }
                else if (argument == CardsArgumentName)
                {
                    cards = true;
                }
                else if (argument == W4ProfileArgumentName)
                {
                    w4Profile = true;
                }
                else if (argument == Gc013ArgumentName)
                {
                    gc013 = true;
                }
                else if (argument == W4GateArgumentName)
                {
                    w4Gate = true;
                }
                else if (argument == FaultsArgumentName)
                {
                    faults = true;
                }
                else if (argument == Gc018ArgumentName)
                {
                    gc018 = true;
                }
                else if (argument == Gc019ArgumentName)
                {
                    gc019 = true;
                }
                else if (argument == W5GateArgumentName)
                {
                    w5Gate = true;
                }
                else if (argument == TraversalArgumentName)
                {
                    traversal = true;
                }
                else if (argument == Gc021ArgumentName)
                {
                    gc021 = true;
                }
                else if (argument == RecoveryArgumentName)
                {
                    recovery = true;
                }
                else if (argument == LifecycleStressArgumentName)
                {
                    lifecycleStress = true;
                }
                else if (argument == ReplayArgumentName)
                {
                    replay = true;
                }
                else if (argument == W6GateArgumentName)
                {
                    w6Gate = true;
                }
                else if (argument == CatalogCoverageArgumentName)
                {
                    catalogCoverage = true;
                }
                else if (argument == ResultArgumentName && i + 1 < arguments.Length)
                {
                    resultPath = arguments[i + 1];
                }
            }

            return new ProbeArguments(
                missingRegistration, worldDispatch, w1Gate, w2Gate, w3Gate, narrative, cards, w4Profile, gc013,
                lifecycleStress,
                w4Gate, faults, gc018, gc019, w5Gate, traversal, gc021, recovery, replay, w6Gate, catalogCoverage,
                resultPath);
        }
    }
}
