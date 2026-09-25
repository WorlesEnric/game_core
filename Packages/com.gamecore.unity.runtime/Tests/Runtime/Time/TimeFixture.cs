// GameCore.Unity.Runtime test fixture (GC-009): stages, systems and a non-component native container owner.
//
// The fixture exists to make three things measurable on a real Unity world:
//   * a producer stage that schedules a real job writing a native container the ECS does not track, and
//     registers its handle in the time module's native dependency table;
//   * a dependent consumer stage in a later fence slot that must wait for that handle before it reads;
//   * a clock-driven fixture world whose steps come only from registered wakes.
//
// It owns no other task's types: keys, components and systems are local to this test assembly.
#nullable enable
using System;
using GameCore.Contracts;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace GameCore.Unity.Runtime.Tests.Time
{
    /// <summary>Generated-style keys of the time fixture; a fixture namespace can never collide with a package key.</summary>
    public static class TimeFixtureKeys
    {
        public const ulong Namespace = 0x474354494D454641UL;

        public static readonly StageId ProduceStage = Stage(1UL);
        public static readonly StageId ConsumeStage = Stage(2UL);
        public static readonly StageId CaptureStage = Stage(3UL);

        public static readonly FactoryKey ProduceSystem = Key(1UL, "time.fixture.produce");
        public static readonly FactoryKey ConsumeSystem = Key(2UL, "time.fixture.consume");
        public static readonly FactoryKey CaptureSystem = Key(3UL, "time.fixture.capture");

        /// <summary>The declared buffer whose producer fence lives outside ECS component tracking (P-041).</summary>
        public static readonly BufferId ResultBuffer = new BufferId(new Id128(Namespace, 0x2001UL));

        private static StageId Stage(ulong ordinal) => new StageId(new Id128(Namespace, 0x1000UL + ordinal));

        private static FactoryKey Key(ulong ordinal, string stableName)
            => new FactoryKey(new Id128(Namespace, ordinal), NameKeyVersion(stableName));

        private static uint NameKeyVersion(string stableName)
        {
            uint hash = 2166136261U;
            for (int i = 0; i < stableName.Length; i++)
            {
                hash ^= stableName[i];
                hash *= 16777619U;
            }

            return hash == 0U ? 1U : hash;
        }
    }

    /// <summary>Counters the time fixture stages write, so a test can assert which stage ran and what it observed.</summary>
    public struct TimeFixtureTrail : IComponentData
    {
        public int ProduceCount;
        public int ConsumeCount;
        public int CaptureCount;
        public int ConsumedValue;
        public int Steps;
    }

    /// <summary>
    /// The non-component native container of the fixture. It is deliberately <b>not</b> an <c>IComponentData</c>:
    /// Unity's component safety cannot see it, which is exactly the dependency the native table must carry (P-041).
    /// </summary>
    public sealed class TimeFixtureContainer : IDisposable
    {
        public TimeFixtureContainer(int length)
        {
            Results = new NativeArray<int>(length, Allocator.Persistent);
        }

        public NativeArray<int> Results { get; private set; }

        public void Dispose()
        {
            if (Results.IsCreated)
            {
                Results.Dispose();
            }
        }
    }

    /// <summary>
    /// Real scheduled job writing the non-component container; it runs on the job system's workers. Burst compilation
    /// is not required for the dependency semantics under test and this assembly does not reference Unity.Burst, so
    /// the job is a plain <see cref="IJob"/>.
    /// </summary>
    public struct TimeFixtureWriteJob : IJob
    {
        public NativeArray<int> Results;

        public int Value;

        public void Execute()
        {
            for (int i = 0; i < Results.Length; i++)
            {
                Results[i] = Value;
            }
        }
    }

    /// <summary>Real scheduled job reading the non-component container through the dependency the consumer combined.</summary>
    public struct TimeFixtureReadJob : IJob
    {
        public NativeArray<int> Results;

        public NativeArray<int> Observed;

        public void Execute()
        {
            Observed[0] = Results.Length == 0 ? 0 : Results[0];
        }
    }

    /// <summary>Seeded world state of the time fixture.</summary>
    public static class TimeFixtureWorldState
    {
        public static Entity Seed(World world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            EntityManager entityManager = world.EntityManager;
            Entity entity = entityManager.CreateEntity(typeof(TimeFixtureTrail));
            entityManager.SetName(entity, "TimeFixtureWorldState");
            return entity;
        }

        public static bool TryRead(EntityManager entityManager, out TimeFixtureTrail trail)
        {
            using (EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<TimeFixtureTrail>()))
            {
                if (query.IsEmpty)
                {
                    trail = default(TimeFixtureTrail);
                    return false;
                }

                trail = entityManager.GetComponentData<TimeFixtureTrail>(query.GetSingletonEntity());
                return true;
            }
        }
    }
}
