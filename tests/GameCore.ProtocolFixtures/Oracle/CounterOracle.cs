// Independent pure oracle (GC-002). Counter/epoch/revision advancement and overflow rejection from P-005
// and P-006, plus the publication/step invariants ("a publication never increments a step", and a no-op
// proposal returns NoChange without increments).
#nullable enable
using System;
using GameCore.Contracts;

namespace GameCore.ProtocolFixtures.Oracle
{
    /// <summary>Counters whose overflow must reject rather than wrap (P-005, P-006).</summary>
    public enum CounterName
    {
        CompositionRevision = 0,
        AssemblyEpoch = 1,
        ActivationEpoch = 2,
        LogicalStepId = 3,
        InstallationGeneration = 4,
        DefinitionRevision = 5,
        EventSequence = 6,
    }

    /// <summary>Result of one counter advancement attempt.</summary>
    public readonly struct CounterAdvance
    {
        public CounterAdvance(CounterName counter, bool accepted, ulong current, ulong next, string code)
        {
            Counter = counter;
            Accepted = accepted;
            Current = current;
            Next = next;
            Code = code;
        }

        public CounterName Counter { get; }

        public bool Accepted { get; }

        public ulong Current { get; }

        /// <summary>Unchanged current value when the advance was rejected; never a wrapped value.</summary>
        public ulong Next { get; }

        /// <summary>"OverflowRejected" when refused, empty when accepted.</summary>
        public string Code { get; }

        public bool Wrapped => Accepted && Next < Current;
    }

    /// <summary>Version-domain state observed at one publication boundary (P-006).</summary>
    public readonly struct PublicationState
    {
        public PublicationState(ulong compositionRevision, ulong assemblyEpoch, ulong logicalStepId)
        {
            CompositionRevision = compositionRevision;
            AssemblyEpoch = assemblyEpoch;
            LogicalStepId = logicalStepId;
        }

        public static PublicationState FreshSession { get; } = new PublicationState(0UL, 0UL, 0UL);

        public ulong CompositionRevision { get; }

        public ulong AssemblyEpoch { get; }

        public ulong LogicalStepId { get; }

        public override string ToString() =>
            "rev=" + CompositionRevision + ",epoch=" + AssemblyEpoch + ",step=" + LogicalStepId;
    }

    /// <summary>Publication/step advancement result, including the no-op and overflow cases.</summary>
    public readonly struct PublicationAdvance
    {
        public PublicationAdvance(bool accepted, PublicationState before, PublicationState after, string code)
        {
            Accepted = accepted;
            Before = before;
            After = after;
            Code = code;
        }

        public bool Accepted { get; }

        public PublicationState Before { get; }

        public PublicationState After { get; }

        public string Code { get; }

        public bool RevisionIncremented => After.CompositionRevision == Before.CompositionRevision + 1UL;

        public bool EpochIncremented => After.AssemblyEpoch == Before.AssemblyEpoch + 1UL;

        public bool StepUnchanged => After.LogicalStepId == Before.LogicalStepId;
    }

    /// <summary>Counter arithmetic, overflow rejection and publication-boundary invariants.</summary>
    public static class CounterOracle
    {
        public const string OverflowCode = "OverflowRejected";

        public static CounterAdvance Advance(CounterName counter, ulong current)
        {
            if (current == ulong.MaxValue)
            {
                return new CounterAdvance(counter, false, current, current, OverflowCode);
            }

            return new CounterAdvance(counter, true, current, current + 1UL, string.Empty);
        }

        /// <summary>True when the next allocation must be refused because the counter would wrap (P-005).</summary>
        public static bool RequiresWorldRecreation(ulong current) => current == ulong.MaxValue;

        /// <summary>
        /// Advances the version domains for one publication. <paramref name="changesComposition"/> false is the
        /// NoChange case: no revision, epoch or step movement at all (P-006).
        /// </summary>
        public static PublicationAdvance AfterFirstPublication(PublicationState before)
        {
            if (before.CompositionRevision == ulong.MaxValue || before.AssemblyEpoch == ulong.MaxValue)
            {
                return new PublicationAdvance(false, before, before, OverflowCode);
            }

            PublicationState after = new PublicationState(
                before.CompositionRevision + 1UL,
                before.AssemblyEpoch + 1UL,
                before.LogicalStepId);
            return new PublicationAdvance(true, before, after, string.Empty);
        }

        /// <summary>Advances the version domains for one ordinary publication (step unchanged).</summary>
        public static PublicationAdvance AfterPublication(PublicationState before) => AfterFirstPublication(before);

        /// <summary>The NoChange case: a publication-shaped operation that changes nothing.</summary>
        public static PublicationAdvance AfterNoChangeProposal(PublicationState before) =>
            new PublicationAdvance(true, before, before, "NoChange");

        /// <summary>Advances only the committed logical step; a publication never does this (P-006).</summary>
        public static PublicationAdvance AfterCommittedStep(PublicationState before)
        {
            if (before.LogicalStepId == ulong.MaxValue)
            {
                return new PublicationAdvance(false, before, before, OverflowCode);
            }

            PublicationState after = new PublicationState(
                before.CompositionRevision,
                before.AssemblyEpoch,
                before.LogicalStepId + 1UL);
            return new PublicationAdvance(true, before, after, string.Empty);
        }

        /// <summary>True when any counter moved; used to assert a faulted or rejected plan changed nothing.</summary>
        public static bool AnyCounterMoved(PublicationState before, PublicationState after) =>
            before.CompositionRevision != after.CompositionRevision ||
            before.AssemblyEpoch != after.AssemblyEpoch ||
            before.LogicalStepId != after.LogicalStepId;

        public static PublicationState FromSeam(CompositionRevision revision, AssemblyEpoch epoch, LogicalStepId step) =>
            new PublicationState(revision.Value, epoch.Value, step.Value);
    }
}
