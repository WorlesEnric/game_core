// GameCore.Unity.Runtime - the declared surface one checkpoint capture reads (GC-018).
//
// Normative sources: 00 P-004 ("installation keys and target spawn keys are explicitly stored in
// content/checkpoints/replay records"), P-015 (a target's descriptor and recipe are declared, not discovered),
// P-038 (a plugin local clock registers how it advances, pauses and persists) and P-043 (each buffer declares its
// producer, owner, consumer stage and lifetime).
//
// A committed boundary cannot be read out of ECS storage alone. The scope tree, a target's recipe identity, the
// clock declarations and the set of bounded next-step buffers are *declared* facts of a world definition; storage
// holds only the rows derived from them. `CaptureContext` is where a caller states those facts once, explicitly, so
// a capture reads a known surface rather than guessing one — and so nothing is discovered by reflection or by
// scanning storage for markers (04 s8).
//
// Everything in here is either an immutable value or a reference the caller already owns; the reader copies from it
// and never mutates it, so a context can be shared by every checkpoint of one world.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Execution.Persistence;
using GameCore.Execution.Time;
using GameCore.Unity.Runtime.Integration;

namespace GameCore.Unity.Runtime.Persistence
{
    /// <summary>The declared surface a checkpoint of one world reads, supplied once by the caller (P-015, P-038, P-043).</summary>
    public sealed class CaptureContext
    {
        public CaptureContext(
            WorldId world,
            WorldDefinitionId definition,
            ContentHash catalogFingerprint,
            Integration.LiveTargetIndex targets,
            TargetRegistry registry,
            IReadOnlyList<PluginClockSpec>? clockSpecs,
            PluginClockRegistry? clocks,
            RngStreamTable rng,
            PropagationMode mode,
            ulong stepDurationTicks,
            ulong ticksPerSecond,
            uint maxStepsPerPump,
            bool usesUnscaledHostClock,
            CompositionHost? lane = null,
            AssemblyPublisher? publisher = null,
            IReadOnlyList<BufferId>? nextStepBuffers = null,
            IReadOnlyDictionary<OperationId, FrozenPayload>? commandPayloads = null)
        {
            World = world;
            Definition = definition;
            CatalogFingerprint = catalogFingerprint;
            Targets = targets ?? throw new ArgumentNullException(nameof(targets));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            ClockSpecs = ContractCollections.Freeze(clockSpecs);
            Clocks = clocks;
            Rng = rng ?? throw new ArgumentNullException(nameof(rng));
            Mode = mode;
            StepDurationTicks = stepDurationTicks;
            TicksPerSecond = ticksPerSecond;
            MaxStepsPerPump = maxStepsPerPump;
            UsesUnscaledHostClock = usesUnscaledHostClock;
            Lane = lane;
            Publisher = publisher;
            NextStepBuffers = ContractCollections.Freeze(nextStepBuffers);
            CommandPayloads = commandPayloads ?? EmptyPayloads;

            if (!registry.World.Session.Equals(world.Session))
            {
                throw new ArgumentException(
                    "The target registry belongs to another world incarnation than the context (P-004).",
                    nameof(registry));
            }
        }

        public WorldId World { get; }

        /// <summary>World definition identity, so a restore can refuse a document from another definition (P-004).</summary>
        public WorldDefinitionId Definition { get; }

        /// <summary>Fingerprint of the catalog this world runs; recorded in every checkpoint (P-028, P-053).</summary>
        public ContentHash CatalogFingerprint { get; }

        /// <summary>Live target view: stable identity, owner scope and recipe, with no entity (P-015).</summary>
        public Integration.LiveTargetIndex Targets { get; }

        /// <summary>Target registry, the only place a `TargetId` resolves to an `Entity` for the copy (P-005).</summary>
        public TargetRegistry Registry { get; }

        /// <summary>
        /// Declared plugin clocks of this world, including the transient ones (P-038). The declarations are supplied
        /// separately from the registry because the registry exposes no enumeration of its own registrations, only
        /// the wakes of a clock a caller already knows about.
        /// </summary>
        public IReadOnlyList<PluginClockSpec> ClockSpecs { get; }

        /// <summary>
        /// The world's clock registry, or null when it declares none. The reader needs it to read each persistent
        /// clock's pending wakes with their remaining delay (P-053).
        /// </summary>
        public PluginClockRegistry? Clocks { get; }

        /// <summary>Random streams of this world; their current positions are part of a checkpoint (P-008, P-053).</summary>
        public RngStreamTable Rng { get; }

        /// <summary>World-level propagation mode, because it is one world setting (P-013).</summary>
        public PropagationMode Mode { get; }

        public ulong StepDurationTicks { get; }

        public ulong TicksPerSecond { get; }

        public uint MaxStepsPerPump { get; }

        public bool UsesUnscaledHostClock { get; }

        /// <summary>Committed control lane, or null for a world with no composition (P-010).</summary>
        public CompositionHost? Lane { get; }

        /// <summary>
        /// Assembly publisher, when the world has one. It is what makes the boundary check meaningful: while a
        /// publication is in flight the lane and the world disagree, which is not a boundary (P-030).
        /// </summary>
        public AssemblyPublisher? Publisher { get; }

        /// <summary>Buffers whose retained rows are the bounded next-step messages of this world (P-043).</summary>
        public IReadOnlyList<BufferId> NextStepBuffers { get; }

        /// <summary>
        /// Payloads of the commands this world admitted that have not executed yet. The ledger stores a command's
        /// request identity, route and schema but not its bytes, so the caller that admitted the command supplies
        /// them here; a command whose payload is absent is still recorded, with no payload.
        /// </summary>
        public IReadOnlyDictionary<OperationId, FrozenPayload> CommandPayloads { get; }

        /// <summary>Only the committed revision of the lane is read, so the reader needs no extra accessor.</summary>
        public CompositionRevision LaneRevision => Lane == null ? CompositionRevision.Zero : Lane.Committed.Revision;

        /// <summary>Only the committed epoch of the lane is read; it must equal the world's published epoch (P-006).</summary>
        public AssemblyEpoch LaneEpoch => Lane == null ? AssemblyEpoch.Zero : Lane.Committed.Epoch;

        public override string ToString() =>
            "captureContext(" + World.Session.ToString() + ",targets="
            + Targets.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",clocks=" + ClockSpecs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",streams=" + Rng.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";

        /// <summary>An empty payload map, so a context without supplied payloads needs no null check.</summary>
        private static readonly IReadOnlyDictionary<OperationId, FrozenPayload> EmptyPayloads =
            new Dictionary<OperationId, FrozenPayload>();
    }
}
