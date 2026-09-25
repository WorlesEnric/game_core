#nullable enable
using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;

namespace GameCore.Unity.Fixtures
{
    /// <summary>
    /// Per-world fixture counters written by the fixture systems. One seeded world entity carries every counter, so
    /// a test or probe can read the exact number of dispatches a stage received (TEST-018).
    /// </summary>
    public struct FixtureTrail : IComponentData
    {
        public int IngressCount;

        public int AcceptCount;

        public int SettleCount;

        public int FaultCount;

        public int ProjectCount;
        public int ProjectObservedJobValue;

        public int TickCount;

        public int OutputCount;
    }

    /// <summary>Authoritative-looking value state written before the fixture fault stage throws (P-031).</summary>
    public struct FixtureCounter : IComponentData
    {
        public int Value;
    }

    /// <summary>Result written by the fixture's real Burst job, so a scheduled job exists in the step's fence.</summary>
    public struct FixtureJobResult : IComponentData
    {
        public int Value;
    }

    /// <summary>Deterministic fault injection: the fault stage throws only when this flag is enabled.</summary>
    public struct FixtureFaultFlag : IComponentData
    {
        public int Enabled;
    }

    /// <summary>
    /// Raised by the fixture fault stage after it has written authoritative state and scheduled a job, which is
    /// exactly the "wrote then threw" shape the guarded dispatcher must stop on (04 s4, P-031).
    /// </summary>
    public sealed class FixtureInjectedFaultException : System.Exception
    {
        public FixtureInjectedFaultException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Real Burst job scheduled by the fixture stages. It writes a component through a read-write
    /// <c>ComponentLookup</c>, so the scheduled handle is registered against the system's declared access and stays
    /// visible in the step fence (04 s4, P-041).
    /// </summary>
    [BurstCompile]
    public struct FixtureWriteJob : IJob
    {
        public ComponentLookup<FixtureJobResult> Targets;

        public Entity Target;

        public int Value;

        public void Execute()
        {
            Targets[Target] = new FixtureJobResult { Value = Value };
        }
    }
}
