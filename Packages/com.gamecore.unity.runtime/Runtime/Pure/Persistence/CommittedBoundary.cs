// GameCore.Unity.Runtime (engine-free part, compiled as GameCore.Execution) - the committed boundary seam (GC-018).
//
// Normative sources: 00 P-053 ("checkpoints are taken at a committed boundary after required jobs complete; they
// contain catalog/protocol fingerprints, world definition/temporal settings, step/time/debt, stable
// scope/install/target IDs, descriptors, explicit imports/overrides/exclusions, mode, definitions, active and
// dormant authoritative state, owner versions, RNG streams, bounded pending next-step messages, and external
// outbox/dedup cursors when used. Raw host timestamps are normalized to declared clock policy. New host commands
// wait during capture; already queued external commands are either included with ledger/cutoff or explicitly
// rejected before capture according to the checkpoint option, never ambiguously omitted.") and 06 s7 ("Capture at a
// committed boundary; copy authoritative active/dormant slots and composition state under the fence, then
// serialize off the hot lane.").
//
// This interface is the only thing capture knows about a running world. It is engine-free on purpose: the capture
// and restore orchestration below can then be exercised by the pure test suites with no Unity world, while the
// Unity implementation reads real ECS storage. The sibling observation task (GC-016) owns bounded snapshot leases;
// when its lease interface is present, an implementation of this seam is expected to *use* that lease rather than
// reaching into live storage, and the shape below stays the frozen contract between them (see
// artifacts/gc-018/HANDOFF.md).
//
// `TryRead` MUST copy, not alias: every list it returns is a value snapshot taken under the boundary condition, so
// nothing that happens to the world afterwards can change what the capture serializes (P-029's copy-then-migrate
// discipline, applied to capture).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Execution.Persistence
{
    /// <summary>Why a capture or a restore was refused before it touched anything (P-052).</summary>
    public enum BoundaryRefusal
    {
        /// <summary>The boundary was readable and stable.</summary>
        None = 0,

        /// <summary>A step or an apply is in progress; a checkpoint belongs at an end-of-step or idle boundary.</summary>
        NotAtBoundary = 1,

        /// <summary>The world is faulted, stopping or disposed and cannot be captured (P-031, P-035).</summary>
        WorldUnavailable = 2,

        /// <summary>The caller named a world the reader does not own (P-004).</summary>
        ForeignWorld = 3,

        /// <summary>A required sub-reader was absent, so the capture would have been incomplete (P-053).</summary>
        IncompleteBoundary = 4,
    }

    /// <summary>
    /// Everything one capture needs from a world at a committed boundary. It is one value so a reader cannot be
    /// interleaved with a publication: the reader returns a coherent set or refuses (P-030, P-053).
    /// </summary>
    public sealed class CommittedBoundarySnapshot
    {
        public CommittedBoundarySnapshot(
            WorldId sourceWorld,
            WorldDefinitionId definition,
            TemporalModel temporalModel,
            ulong stepDurationTicks,
            ulong ticksPerSecond,
            uint maxStepsPerPump,
            bool usesUnscaledHostClock,
            ulong hostTicksPerSecond,
            LogicalStepId logicalStep,
            TimeDebt retainedDebt,
            double domainSeconds,
            ulong pendingDemand,
            PropagationMode mode,
            CompositionRevision publishedRevision,
            AssemblyEpoch publishedEpoch,
            ContentHash catalogFingerprint,
            EventSequence lastEventSequence,
            AdmissionSequence admissionCutoff,
            IReadOnlyList<ScopeRecordValue>? scopes,
            IReadOnlyList<InstallRecordValue>? installs,
            IReadOnlyList<SelectionRecordValue>? selections,
            IReadOnlyList<TargetRecordValue>? targets,
            IReadOnlyList<SlotRecordValue>? slots,
            IReadOnlyList<GrantRecordValue>? grants,
            IReadOnlyList<ClockRecordValue>? clocks,
            IReadOnlyList<CommandRecordValue>? queuedCommands,
            IReadOnlyList<MessageRecordValue>? nextStepMessages,
            IReadOnlyList<RngRecordValue>? rngStreams,
            IReadOnlyList<CursorRecordValue>? cursors)
        {
            SourceWorld = sourceWorld;
            Definition = definition;
            Model = temporalModel;
            StepDurationTicks = stepDurationTicks;
            TicksPerSecond = ticksPerSecond;
            MaxStepsPerPump = maxStepsPerPump;
            UsesUnscaledHostClock = usesUnscaledHostClock;
            HostTicksPerSecond = hostTicksPerSecond;
            LogicalStep = logicalStep;
            RetainedDebt = retainedDebt;
            DomainSeconds = domainSeconds;
            PendingDemand = pendingDemand;
            Mode = mode;
            PublishedRevision = publishedRevision;
            PublishedEpoch = publishedEpoch;
            CatalogFingerprint = catalogFingerprint;
            LastEventSequence = lastEventSequence;
            AdmissionCutoff = admissionCutoff;
            Scopes = ContractCollections.Freeze(scopes);
            Installs = ContractCollections.Freeze(installs);
            Selections = ContractCollections.Freeze(selections);
            Targets = ContractCollections.Freeze(targets);
            Slots = ContractCollections.Freeze(slots);
            Grants = ContractCollections.Freeze(grants);
            Clocks = ContractCollections.Freeze(clocks);
            QueuedCommands = ContractCollections.Freeze(queuedCommands);
            NextStepMessages = ContractCollections.Freeze(nextStepMessages);
            RngStreams = ContractCollections.Freeze(rngStreams);
            Cursors = ContractCollections.Freeze(cursors);
        }

        /// <summary>Session the boundary was read from; recorded for evidence, never reused by a restore (P-004).</summary>
        public WorldId SourceWorld { get; }

        public WorldDefinitionId Definition { get; }

        public TemporalModel Model { get; }

        public ulong StepDurationTicks { get; }

        public ulong TicksPerSecond { get; }

        public uint MaxStepsPerPump { get; }

        public bool UsesUnscaledHostClock { get; }

        public ulong HostTicksPerSecond { get; }

        public LogicalStepId LogicalStep { get; }

        public TimeDebt RetainedDebt { get; }

        public double DomainSeconds { get; }

        public ulong PendingDemand { get; }

        public PropagationMode Mode { get; }

        public CompositionRevision PublishedRevision { get; }

        public AssemblyEpoch PublishedEpoch { get; }

        public ContentHash CatalogFingerprint { get; }

        public EventSequence LastEventSequence { get; }

        /// <summary>Highest admission sequence sealed into a step before this boundary (P-037).</summary>
        public AdmissionSequence AdmissionCutoff { get; }

        public IReadOnlyList<ScopeRecordValue> Scopes { get; }

        public IReadOnlyList<InstallRecordValue> Installs { get; }

        public IReadOnlyList<SelectionRecordValue> Selections { get; }

        public IReadOnlyList<TargetRecordValue> Targets { get; }

        /// <summary>Active **and** dormant authoritative slots (P-032, P-053).</summary>
        public IReadOnlyList<SlotRecordValue> Slots { get; }

        public IReadOnlyList<GrantRecordValue> Grants { get; }

        public IReadOnlyList<ClockRecordValue> Clocks { get; }

        /// <summary>External commands admitted but not yet executed, before the queue policy is applied (P-053).</summary>
        public IReadOnlyList<CommandRecordValue> QueuedCommands { get; }

        /// <summary>Bounded pending next-step messages carried across the boundary (P-043, P-053).</summary>
        public IReadOnlyList<MessageRecordValue> NextStepMessages { get; }

        public IReadOnlyList<RngRecordValue> RngStreams { get; }

        /// <summary>Event cursors and per-issuer high-water marks, including outbox/dedup cursors when used (P-053).</summary>
        public IReadOnlyList<CursorRecordValue> Cursors { get; }

        /// <summary>Counts of this snapshot, in the header's declaration order.</summary>
        public CheckpointCounts Counts => new CheckpointCounts(
            Scopes.Count,
            Installs.Count,
            Selections.Count,
            Targets.Count,
            Slots.Count,
            Grants.Count,
            Clocks.Count,
            QueuedCommands.Count,
            NextStepMessages.Count,
            RngStreams.Count,
            Cursors.Count);

        public override string ToString() =>
            "boundary(" + SourceWorld.Session.ToString() + ",step="
            + LogicalStep.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "," + Counts + ")";
    }

    /// <summary>
    /// Reads one world's state at a committed boundary. An implementation refuses rather than reading a world that
    /// is mid-step, mid-apply or faulted, because a checkpoint taken from a moving world is not a checkpoint (P-053).
    /// </summary>
    public interface ICommittedBoundaryReader
    {
        /// <summary>The world this reader owns; a caller naming another world is refused (P-004).</summary>
        WorldId World { get; }

        /// <summary>
        /// True when the world is at an end-of-step or idle boundary with no apply in progress. A capture checks
        /// this before it reads anything (P-030, P-053).
        /// </summary>
        bool IsAtCommittedBoundary { get; }

        /// <summary>
        /// Copies the whole boundary. False reports the exact refusal; the out snapshot is null and nothing was
        /// mutated (P-052).
        /// </summary>
        bool TryRead(
            WorldId world,
            out CommittedBoundarySnapshot? snapshot,
            out BoundaryRefusal refusal,
            out DiagnosticCode code,
            out string detail);
    }
}
