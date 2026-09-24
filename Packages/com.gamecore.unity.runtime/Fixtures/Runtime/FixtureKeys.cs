#nullable enable
using GameCore.Contracts;
using Unity.Entities;

namespace GameCore.Unity.Fixtures
{
    /// <summary>
    /// Hand-written "generated-style" registration keys for the fixture. A build-time generator emits exactly this
    /// shape (stable key literals plus direct typed references); nothing here is discovered by reflection (04 s8).
    /// </summary>
    public static class FixtureKeys
    {
        /// <summary>Namespace word of every fixture key, so a fixture key can never collide with a package key.</summary>
        public const ulong Namespace = 0x4743464958545552UL;

        public static readonly StageId IngressStage = Stage(1UL);

        public static readonly StageId OutputStage = Stage(2UL);

        public static readonly StageId AcceptStage = Stage(3UL);

        public static readonly StageId SettleStage = Stage(4UL);

        public static readonly StageId FaultStage = Stage(5UL);

        public static readonly StageId ProjectStage = Stage(6UL);

        public static readonly StageId TickStage = Stage(7UL);

        public static readonly FactoryKey IngressSystem = Key(1UL, "fixture.stage.ingress");

        public static readonly FactoryKey OutputSystem = Key(2UL, "fixture.stage.output");

        public static readonly FactoryKey AcceptSystem = Key(3UL, "fixture.stage.accept");

        public static readonly FactoryKey SettleSystem = Key(4UL, "fixture.stage.settle");

        public static readonly FactoryKey FaultSystem = Key(5UL, "fixture.stage.fault");

        public static readonly FactoryKey ProjectSystem = Key(6UL, "fixture.stage.project");

        public static readonly FactoryKey TickSystem = Key(7UL, "fixture.stage.tick");

        /// <summary>Step buffer produced by the settle stage and consumed by the project stage (P-043).</summary>
        public static readonly BufferId CounterBuffer = new BufferId(new Id128(Namespace, 0x2001UL));

        private static StageId Stage(ulong ordinal) => new StageId(new Id128(Namespace, 0x1000UL + ordinal));

        private static FactoryKey Key(ulong ordinal, string stableName)
            => new FactoryKey(new Id128(Namespace, ordinal), NameKeyVersion(stableName));

        /// <summary>FNV-1a fold of the stable name into the key version, so the name participates in the key.</summary>
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

    /// <summary>Seeded world state and read helpers shared by the fixture, the Unity tests and the player probe.</summary>
    public static class FixtureWorldState
    {
        /// <summary>Creates the single fixture world entity carrying every fixture counter and flag.</summary>
        public static Entity Seed(World world)
        {
            if (world == null)
            {
                throw new System.ArgumentNullException(nameof(world));
            }

            EntityManager entityManager = world.EntityManager;
            Entity entity = entityManager.CreateEntity(
                typeof(FixtureTrail),
                typeof(FixtureCounter),
                typeof(FixtureJobResult),
                typeof(FixtureFaultFlag));
            entityManager.SetName(entity, "FixtureWorldState");
            return entity;
        }

        /// <summary>World entity carrying the fixture counters, or <see cref="Entity.Null"/> when unseeded.</summary>
        public static Entity Find(EntityManager entityManager)
        {
            using (EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<FixtureTrail>()))
            {
                return query.IsEmpty ? Entity.Null : query.GetSingletonEntity();
            }
        }

        public static bool TryReadTrail(EntityManager entityManager, out FixtureTrail trail)
        {
            Entity entity = Find(entityManager);
            if (entity == Entity.Null)
            {
                trail = default(FixtureTrail);
                return false;
            }

            trail = entityManager.GetComponentData<FixtureTrail>(entity);
            return true;
        }

        public static int ReadCounter(EntityManager entityManager)
        {
            Entity entity = Find(entityManager);
            return entity == Entity.Null ? 0 : entityManager.GetComponentData<FixtureCounter>(entity).Value;
        }

        public static int ReadJobResult(EntityManager entityManager)
        {
            Entity entity = Find(entityManager);
            return entity == Entity.Null ? 0 : entityManager.GetComponentData<FixtureJobResult>(entity).Value;
        }

        /// <summary>Enables or disables the deterministic fault stage (P-031 fault injection).</summary>
        public static bool SetFaultEnabled(EntityManager entityManager, bool enabled)
        {
            Entity entity = Find(entityManager);
            if (entity == Entity.Null)
            {
                return false;
            }

            entityManager.SetComponentData(entity, new FixtureFaultFlag { Enabled = enabled ? 1 : 0 });
            return true;
        }
    }
}
