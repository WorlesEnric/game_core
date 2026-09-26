// GameCore.Execution.Tests.Observation — deterministic collaborators for the GC-016 observation tests.
//
// The same files run as Unity EditMode tests (through this package's Tests folder) and under plain dotnet
// (dotnet/tests/GameCore.Execution.Tests globs this folder), so nothing here may touch Unity and nothing reads a
// clock, a thread id or a random source: every identity is derived from an explicit literal or ordinal (P-008).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Execution.Observation;

namespace GameCore.Execution.Tests.Observation
{
    /// <summary>Literal identities, commit/event builders and the boundary facts double used by these tests.</summary>
    internal static class ObservationFixture
    {
        public static readonly WorldId World = new WorldId(new Id128(0x47433031364F4253UL, 1UL));

        public static readonly WorldId OtherWorld = new WorldId(new Id128(0x47433031364F4253UL, 2UL));

        public static readonly SchemaRef Schema =
            new SchemaRef(new SchemaId(new Id128(0x47433031364F4353UL, 1UL)), 1U);

        /// <summary>Dispatched-entry count every committed image in these tests is fingerprinted with.</summary>
        public const int Dispatched = 3;

        /// <summary>One committed step with no events.</summary>
        public static StepCommitEvent Commit(ulong step) => Commit(step, Array.Empty<CommittedEvent>());

        /// <summary>One committed step carrying exactly the events given.</summary>
        public static StepCommitEvent Commit(ulong step, IReadOnlyList<CommittedEvent>? events)
        {
            var token = new SnapshotToken(World, AssemblyEpoch.First, new LogicalStepId(step));
            return new StepCommitEvent(
                token,
                events,
                EventSequence.Zero,
                StepFingerprint.Compute(World, AssemblyEpoch.First, new LogicalStepId(step), Dispatched));
        }

        public static SnapshotToken Token(ulong step, WorldId? world = null) =>
            new SnapshotToken(world ?? World, AssemblyEpoch.First, new LogicalStepId(step));

        /// <summary>One committed event of <paramref name="step"/> with the given event sequence.</summary>
        public static CommittedEvent Event(ulong sequence, ulong step, WorldId? world = null)
        {
            WorldId host = world ?? World;
            return new CommittedEvent(
                new EventCursor(host, new EventSequence(sequence)),
                Schema,
                AssemblyEpoch.First,
                new LogicalStepId(step),
                new OperationId(host, new Id128(0x47433031364F5045UL, 1UL), sequence),
                new FrozenPayload(new byte[] { (byte)sequence }));
        }

        /// <summary>One committed step carrying exactly one event; step and sequence move together.</summary>
        public static StepCommitEvent CommitWithEvent(ulong step)
        {
            var events = new[] { Event(step, step) };
            return Commit(step, events);
        }

        /// <summary>SHA-256 of the frozen bytes a lease of <paramref name="step"/> carries.</summary>
        public static ContentHash ExpectedHash(ulong step) =>
            ContentHash.Compute(StepFingerprint.Compute(World, AssemblyEpoch.First, new LogicalStepId(step), Dispatched).ToArray());

        /// <summary>Explicit boundary facts: never gathered from a world, never guessed.</summary>
        internal sealed class Facts : ICommittedBoundaryFactsSource
        {
            public Facts(int queuedCommands, int stagedOperations, BoundaryQueueDisposition disposition)
            {
                QueuedCommandCount = queuedCommands;
                StagedOperationCount = stagedOperations;
                QueueDisposition = disposition;
            }

            public int QueuedCommandCount { get; }

            public int StagedOperationCount { get; }

            public BoundaryQueueDisposition QueueDisposition { get; }

            public override string ToString() => "Facts(" + QueueDisposition.ToString() + ")";
        }
    }
}
