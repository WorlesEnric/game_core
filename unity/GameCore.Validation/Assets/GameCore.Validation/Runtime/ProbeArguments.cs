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
        private const string ReplayArgumentName = "-probeReplay";

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
            bool w4Gate,
            bool faults,
            bool gc018,
            bool gc019,
            bool w5Gate,
            bool replay,
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
            W4Gate = w4Gate;
            Faults = faults;
            Gc018 = gc018;
            Gc019 = gc019;
            W5Gate = w5Gate;
            Replay = replay;
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
        /// Runs the GC-023 replay mode: the recorded 10,000-step integer fixture replayed across the supported
        /// worker counts and under a shuffled producer/completion order, the differential propagation sweep with its
        /// reducer, the observation replay separated from the native-physics comparison, and the instrumented
        /// counters of one real owned world with the raw benchmark trace written beside the probe result
        /// (P-008, P-023, TEST-022, TEST-023).
        /// </summary>
        public bool Replay { get; }

        /// <summary>Destination path of the structured JSON result.</summary>
        public string? ResultPath { get; }

        /// <summary>True when the process was launched as a probe rather than as a normal player run.</summary>
        public bool IsProbeInvocation =>
            MissingRegistration || WorldDispatch || W1Gate || W2Gate || W3Gate || Narrative || Cards || W4Profile
            || Gc013 || W4Gate || Faults || Gc018 || Gc019 || W5Gate || Replay
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
            bool w4Gate = false;
            bool faults = false;
            bool gc018 = false;
            bool gc019 = false;
            bool w5Gate = false;
            bool replay = false;
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
                else if (argument == ReplayArgumentName)
                {
                    replay = true;
                }
                else if (argument == ResultArgumentName && i + 1 < arguments.Length)
                {
                    resultPath = arguments[i + 1];
                }
            }

            return new ProbeArguments(
                missingRegistration, worldDispatch, w1Gate, w2Gate, w3Gate, narrative, cards, w4Profile, gc013,
                w4Gate, faults, gc018, gc019, w5Gate, replay, resultPath);
        }
    }
}
