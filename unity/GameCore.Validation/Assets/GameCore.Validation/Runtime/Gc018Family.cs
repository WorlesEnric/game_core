// GameCore.Validation.ProbeHost — the GC-018 checkpoint/restore family contract.
//
// The gate sentence this contract serves, from `docs/game-core/09-implementation-guide.md` (GC-018):
//
//   "A checkpoint taken at a committed boundary round-trips into a new unexposed world: active and dormant
//    authoritative state, composition boundaries, mode, clocks, RNG streams and cursors survive, a migration
//    rejection or a corrupt reference exposes nothing, and every handle minted in the capturing session is refused
//    by the restored one." — and, from the task's own acceptance: "the probe is 'active+dormant state round-trips
//    without handles' (P-053, P-054)."
//
// One runner, two family adapters, exactly as the GC-013 and Wave 4 sequences have one runner and two adapters.
// `Gc018Scenario` owns the scripted sequence; a family owns only what its genre declares. Everything GC-013's own
// sequence already needed (catalog, scope tree, live targets, the provider to derive from, the mode edits) comes
// from `IGc013Family`, which the GC-018 adapters implement unchanged: checkpointing is a property of the kernel,
// not a fourth parallel scenario per genre.
//
// This file therefore adds exactly what a checkpoint round trip needs from a genre:
//
//   * the one external command the run queues through the family's real route, so "already queued external
//     commands are either included with ledger/cutoff or explicitly rejected before capture, never ambiguously
//     omitted" (P-053) is demonstrated with a real payload on a real declared route;
//   * the declared slot that is seeded dormant, because P-032 makes a dormant row authoritative state that a
//     capture saves and a restore installs as dormant rather than dropping or reactivating it;
//   * the persistent plugin clock and the payload schema its wake declares, so a capture carries a clock
//     declaration and one pending wake with its remaining delay (P-038, P-053);
//   * the scope-isolation and exclusion edit the source world applies before capture, so "the same composition
//     boundaries" is a claim about non-empty captured grant data rather than about two empty sets.
//
// Every member is data or a payload: the runner owns the ordering, the captures, the refusals and the
// observations, so both genres are driven through exactly the same sequence (P-001).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Planning.Scheduling;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// The world pieces a family's runtime attach works over: the host the scenario created for it, its target
    /// index and the seeder that resolves a live target's native entity. The family supplies the module its own
    /// generated systems resolve the world through; the scenario owns the world (P-002, P-004).
    /// </summary>
    public sealed class Gc018RuntimeWorld
    {
        public Gc018RuntimeWorld(
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

        /// <summary>
        /// The compiled schedule this world's dispatch tables were installed from, so a family runtime binds to
        /// the same compiled order its world executes (GC-009).
        /// </summary>
        public CompiledSchedule Schedule { get; }
    }

    /// <summary>
    /// One genre's declared facts for the GC-018 checkpoint round trip: everything
    /// <see cref="IGc013Family"/> declares, plus the command, the dormant slot, the persistent clock and the
    /// composition enrichment a capture has to carry.
    /// </summary>
    public interface IGc018Family : IGc013Family
    {
        /// <summary>
        /// The one external command this run admits and leaves unexecuted, so the capture has a real queued
        /// command to disposition (P-037, P-053). It is built on the family's own declared route, target and
        /// payload schema, so nothing about the command is fabricated by the scenario.
        /// </summary>
        CommandEnvelope QueuedCommand(WorldId world, OperationId operation);

        /// <summary>
        /// Schema version the target's active row is seeded at, so "the active row survived with its value and its
        /// version" is a comparison the scenario can make without naming a family domain (P-032).
        /// </summary>
        uint ActiveSlotVersion { get; }

        /// <summary>Stable identity of the persistent plugin clock this run declares (P-038, P-053).</summary>
        Id128 WakeClockId { get; }

        /// <summary>Payload schema the clock's scheduled wake declares (P-038, P-053).</summary>
        SchemaRef WakePayloadSchema { get; }

        /// <summary>Target whose second declared state slot is seeded dormant (P-032).</summary>
        TargetId DormantTarget { get; }

        /// <summary>Owner of that dormant row: the same owner as the target's active row.</summary>
        OwnerId DormantOwner { get; }

        /// <summary>The declared slot seeded dormant; it is a different slot from the active row's (P-032).</summary>
        SlotId DormantSlot { get; }

        /// <summary>Schema version the dormant row is seeded at, so its version survives the round trip (P-032).</summary>
        uint DormantVersion { get; }

        /// <summary>Non-default value the dormant row is seeded with (P-032).</summary>
        int DormantValue { get; }

        /// <summary>
        /// The composition enrichment the source world applies before capture: one capability-isolation member and
        /// one exclusion on a scope that owns live targets (P-016). It is an O-04-subject edit over the family's own
        /// declared capability, so a restore that lost the boundaries is observable and a restore that reopened them
        /// is not silently accepted.
        /// </summary>
        CompositionEditPayload BoundaryEnrichment();

        /// <summary>
        /// The scope <see cref="BoundaryEnrichment"/> acts on: it must exist in the family's declared tree and own
        /// at least one live target (P-010, P-016).
        /// </summary>
        ScopeId EnrichedScope { get; }

        /// <summary>
        /// Attaches the genre's own runtime module to a world this scenario just created: the module its generated
        /// step systems resolve their world through, with every live target mapped to its native entity and the
        /// trail's root bound. A restored world is a running world, so when the restore re-admits the captured
        /// command the family's input stage must consume its ingress lane like any other world of this genre
        /// (P-037, P-042, P-043). Returns false with a detail when a target cannot be mapped, so a restore that
        /// cannot run is refused rather than exposed.
        /// </summary>
        bool TryAttachRuntime(Gc018RuntimeWorld world, out string detail);

        /// <summary>
        /// Releases the module <see cref="TryAttachRuntime"/> added for a world this scenario created; the
        /// scenario's own teardown path, called whether the world passed or faulted (P-048).
        /// </summary>
        void DetachRuntime(Gc018RuntimeWorld world);
    }
}
