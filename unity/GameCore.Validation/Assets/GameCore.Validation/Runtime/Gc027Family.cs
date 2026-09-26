// GameCore.Validation.ProbeHost — the GC-027 recovery family contract.
//
// The gate sentence this contract serves, from `docs/game-core/09-implementation-guide.md` (GC-027):
//
//   "Compose `RecoverWorld` (O-22) over the existing capture/restore path, with P-049 host-configured bounded
//    retries. Inject faults at capture copy, file publication, restore reference repair, postwrite apply, outbox
//    append, delivery, ack and restart. For each: the permitted observable result, the failed old world never
//    resumes, no hidden external replay. Restore cards, narrative and traversal-compatible checkpoint data into a
//    NEW world with different native handles; verify active/dormant state, pending-command disposition and delivery
//    cursor intact; replayed external delivery does not duplicate the test destination effect; incompatible content
//    leaves the new world unexposed."
//
// One runner, two family adapters, exactly as GC-013's, the Wave 4 gate's and GC-018's sequences have one runner and
// two adapters. Everything a checkpoint round trip already needed (catalog, scope tree, live targets, the provider to
// derive from, the dormant slot, the persistent clock, the composition enrichment, the captured outbox rows' genre
// facts) comes from `IGc018Family`, so this contract adds only what a *recovery* needs beyond a round trip:
//
//   * the one delivery obligation the source world commits and never delivers, plus the destination identity and
//     command schema it is addressed to — a real obligation on a real outbox, because "delivery cursor intact"
//     and "replayed external delivery does not duplicate the destination effect" are claims about committed
//     delivery state and cannot be made about an empty outbox (P-045, P-053);
//   * the durable outbox parameters the world runs with, so the run's durability is declared rather than assumed;
//   * the composition edit that faults the source world after its first live write, so a recovery has a *real*
//     faulted world to recover from rather than a world a test labelled faulted (P-031).
//
// Every member is data or a payload: the runner owns the ordering, the fault latches, the captures, the recoveries
// and the observations, so both genres are driven through exactly the same sequence (P-001).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Delivery;
using GameCore.Execution.Messages;
using GameCore.Execution.Persistence;
using GameCore.Planning.Scheduling;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// One genre's declared facts for the GC-027 recovery proof: everything <see cref="IGc018Family"/> declares,
    /// plus the delivery obligation the recovery has to carry across and the edit that faults the source world.
    /// </summary>
    public interface IGc027Family : IGc018Family
    {
        /// <summary>
        /// Stable identity of the destination the run's one obligation is addressed to. It is a genre fact, because
        /// only the recipient's package can say who receives a committed event (P-003).
        /// </summary>
        Id128 DeliveryDestinationId { get; }

        /// <summary>Command schema the destination accepts; a mismatch is refused before the port is asked (P-054).</summary>
        SchemaRef DeliveryCommandSchema { get; }

        /// <summary>The command bytes of the one obligation, deterministic so two runs carry the same obligation.</summary>
        byte[] DeliveryPayload();

        /// <summary>Schema the obligation's payload is recorded under (P-053).</summary>
        SchemaRef DeliveryPayloadSchema { get; }

        /// <summary>Open obligations the run's outbox may hold; exhaustion is explicit, never a silent drop (P-043).</summary>
        int OutboxCapacity { get; }

        /// <summary>Terminal delivery records retained per destination before the oldest is pruned (P-045).</summary>
        int OutboxTerminalRetention { get; }

        /// <summary>The durability the run's outbox is configured with; the run proves what it configures (P-045).</summary>
        OutboxDurability OutboxDurabilityClass { get; }

        /// <summary>
        /// The composition edit the run submits to fault the source world after its first live write. It must be an
        /// edit this genre's own declarations accept, so the fault happens inside a real apply and not in validation
        /// (TEST-016 row 5, P-031).
        /// </summary>
        CompositionEditPayload FaultEdit();

        /// <summary>
        /// The family's catalog fingerprint as a value, parsed once by the adapter from the emitted literal, so the
        /// runner never parses a string a scenario could mistype (P-028). It is a distinct name from the family's own
        /// `CatalogFingerprint` string property, which reports the emitted literal.
        /// </summary>
        ContentHash CatalogHash();

        /// <summary>
        /// The committed generated checkpoint codecs of this family's catalog (P-054). The runner must not build them
        /// itself: a second binding table could drift into a second format, which is exactly what P-054 forbids.
        /// </summary>
        CheckpointCodecSet Codecs { get; }

        /// <summary>
        /// The directed migration graph the destination catalog registers. GC-018's own runs register none, so the
        /// registry is empty unless a genre declares a step (P-054).
        /// </summary>
        CheckpointMigrationRegistry DirectMigrations { get; }

        /// <summary>
        /// The schemas the destination can allocate at the version it carries: the checkpoint container, every recipe
        /// the family's catalog registers and the payload schema its declared wake names. A captured schema outside
        /// this set has no path to the build's version, which is what a restore must refuse (P-054).
        /// </summary>
        IReadOnlyList<SchemaRef> AllocatedSchemas { get; }

        /// <summary>
        /// True when this genre commits delivery obligations the run must carry across a recovery. The two
        /// reward-consuming genres answer yes; the traversal course declares no destination at all, so it answers no
        /// and the run skips the delivery observations rather than inventing an endpoint (P-003, P-045).
        /// </summary>
        bool HasDeliveryObligation { get; }

        /// <summary>
        /// True when the run publishes the family's declared `SetupEdits` through the lane. The card and narrative
        /// worlds have no declared tree of their own, so their setup edits are real scope creations; the traversal
        /// course's tree is already the world definition's declared tree in its lane seed, so publishing its creates
        /// would be a duplicate create for scopes the seed already carries (P-010, P-006).
        /// </summary>
        bool PublishesDeclaredSetupEdits { get; }

        /// <summary>
        /// The temporal model a recovered world of this genre is created with. It is the family's own declared model,
        /// so a fixed-step course recovers as a fixed-step course rather than as a command-driven world (P-036).
        /// </summary>
        TemporalModel TemporalModel { get; }

        /// <summary>
        /// The declared fixed-step settings of that model, or null for a command-driven genre. A recovery request
        /// that names <see cref="TemporalModel.FixedStep"/> without them is refused as malformed (P-036).
        /// </summary>
        FixedStepSettings? FixedStep { get; }

        /// <summary>
        /// The declared fixed-step duration in seconds, or zero for a command-driven genre. One admitted engine
        /// simulation is stepped for exactly this interval, so the engine and the world commit the same step (P-036).
        /// </summary>
        double FixedStepSeconds { get; }

        /// <summary>
        /// The compiled pipeline descriptor of the world being built, handed to the family before its runtime is
        /// attached. A genre whose runtime needs the descriptor (the traversal course passes it to its stage
        /// runtime) remembers it; a genre whose runtime does not need one ignores it. It is called exactly once per
        /// world, before <see cref="IGc018Family.TryAttachRuntime"/> (GC-009).
        /// </summary>
        void NotePipelineForAttach(PipelineDescriptorReport descriptor);

        /// <summary>
        /// True when this genre declares an engine physical domain at all. It is the table predicate for the
        /// physics observations (a family answers it without a world, unlike <see cref="PhysicsDomain"/>), and the
        /// two ECS-owned genres answer false so their runs record no physics observation (P-034, P-059).
        /// </summary>
        bool DeclaresEnginePhysicsDomain { get; }

        /// <summary>
        /// The input one admitted step of this genre consumes over its own declared route, or null when the genre's
        /// steps need none. The runner submits it through the ordinary input ingress before it pumps, so a fixed-step
        /// course's integrate stage really runs and its state is non-trivial before the capture (P-037, P-042).
        /// </summary>
        CommandEnvelope? StepInput(WorldId world, OperationId operation);

        /// <summary>
        /// Admitted steps this genre's source world runs before it is captured and faulted. Zero for a genre whose
        /// state is already complete at creation; the traversal course runs its declared steps so the recovered
        /// world's motion state is a value that really moved (P-036).
        /// </summary>
        uint AdmittedStepsBeforeFault { get; }

        /// <summary>
        /// Canonical text of this genre's authoritative state for the recovered world, or null when the genre has
        /// none to compare beyond the restored slot rows. The traversal course answers its runners' pose, velocity
        /// and accepted-checkpoint progress, so "the authoritative state survived" is a comparison of two worlds'
        /// own values rather than a claim about a plan (P-053, REF-A01).
        /// </summary>
        string? AuthoritativeStateText(UnityWorldHost world);

        /// <summary>
        /// The genre's engine-physics domain, or null when it declares none. The card and narrative genres are
        /// ECS-owned and answer null; the traversal course answers its real local scene so the run can prove the
        /// engine's own state is re-derived from the authoritative ECS pose rather than restored (P-054, 04 s7).
        /// It is null until the genre's runtime is attached, and never null afterwards.
        /// </summary>
        Gc027PhysicsDomain? PhysicsDomain { get; }
    }

    /// <summary>
    /// The world pieces one GC-027 run works over: the source world the runner created, its live target index, its
    /// seeder and the schedule its runtime binds to. The runner owns all of them (P-002, P-004).
    /// </summary>
    public sealed class Gc027RuntimeWorld
    {
        public Gc027RuntimeWorld(
            UnityWorldHost host,
            LiveTargetIndex targets,
            LiveTargetSeeder seeder,
            CompiledSchedule schedule)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
            Targets = targets ?? throw new ArgumentNullException(nameof(targets));
            Seeder = seeder ?? throw new ArgumentNullException(nameof(seeder));
            Schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        }

        public UnityWorldHost Host { get; }

        public LiveTargetIndex Targets { get; }

        public LiveTargetSeeder Seeder { get; }

        public CompiledSchedule Schedule { get; }

        /// <summary>The shape GC-018's own runtime attach already accepts, so a family implements one attach (P-002).</summary>
        public Gc018RuntimeWorld ToGc018RuntimeWorld() => new Gc018RuntimeWorld(Host, Targets, Seeder, Schedule);
    }
}
