#nullable enable
using Unity.Entities;
using Unity.Jobs;

namespace GameCore.Unity.Fixtures
{
    /// <summary>Ingress system: adapter input is collected before a step and never advances a logical step (04 s3).</summary>
    [DisableAutoCreation]
    public partial class FixtureIngressSystem : SystemBase
    {
        private EntityQuery trailQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            trailQuery = GetEntityQuery(ComponentType.ReadWrite<FixtureTrail>());
        }

        protected override void OnUpdate()
        {
            if (trailQuery.IsEmpty)
            {
                return;
            }

            Entity entity = trailQuery.GetSingletonEntity();
            FixtureTrail trail = EntityManager.GetComponentData<FixtureTrail>(entity);
            trail.IngressCount++;
            EntityManager.SetComponentData(entity, trail);
        }
    }

    /// <summary>First gameplay stage: writes its counter and the authoritative value (P-034, P-044).</summary>
    [DisableAutoCreation]
    public partial class FixtureAcceptSystem : SystemBase
    {
        private EntityQuery trailQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            trailQuery = GetEntityQuery(ComponentType.ReadWrite<FixtureTrail>());
        }

        protected override void OnUpdate()
        {
            if (trailQuery.IsEmpty)
            {
                return;
            }

            Entity entity = trailQuery.GetSingletonEntity();
            FixtureTrail trail = EntityManager.GetComponentData<FixtureTrail>(entity);
            trail.AcceptCount++;
            EntityManager.SetComponentData(entity, trail);

            FixtureCounter counter = EntityManager.GetComponentData<FixtureCounter>(entity);
            counter.Value += 1;
            EntityManager.SetComponentData(entity, counter);
        }
    }

    /// <summary>Second gameplay stage: writes state and schedules a real Burst job, so a step has a tracked handle.</summary>
    [DisableAutoCreation]
    public partial class FixtureSettleSystem : SystemBase
    {
        private EntityQuery trailQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            trailQuery = GetEntityQuery(ComponentType.ReadWrite<FixtureTrail>());
        }

        protected override void OnUpdate()
        {
            if (trailQuery.IsEmpty)
            {
                return;
            }

            Entity entity = trailQuery.GetSingletonEntity();
            FixtureTrail trail = EntityManager.GetComponentData<FixtureTrail>(entity);
            trail.SettleCount++;
            EntityManager.SetComponentData(entity, trail);

            FixtureCounter counter = EntityManager.GetComponentData<FixtureCounter>(entity);
            counter.Value += 10;
            EntityManager.SetComponentData(entity, counter);

            ComponentLookup<FixtureJobResult> targets = GetComponentLookup<FixtureJobResult>(false);
            Dependency = new FixtureWriteJob { Targets = targets, Target = entity, Value = counter.Value }.Schedule(Dependency);
        }
    }

    /// <summary>
    /// Injected-fault stage: writes authoritative state, schedules a job, then throws when the fault flag is set.
    /// This is the exact case the guarded dispatcher must stop on (04 s4, P-031).
    /// </summary>
    [DisableAutoCreation]
    public partial class FixtureFaultSystem : SystemBase
    {
        private EntityQuery trailQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            trailQuery = GetEntityQuery(ComponentType.ReadWrite<FixtureTrail>());
        }

        protected override void OnUpdate()
        {
            if (trailQuery.IsEmpty)
            {
                return;
            }

            Entity entity = trailQuery.GetSingletonEntity();
            FixtureTrail trail = EntityManager.GetComponentData<FixtureTrail>(entity);
            trail.FaultCount++;
            EntityManager.SetComponentData(entity, trail);

            FixtureCounter counter = EntityManager.GetComponentData<FixtureCounter>(entity);
            counter.Value += 100;
            EntityManager.SetComponentData(entity, counter);

            ComponentLookup<FixtureJobResult> targets = GetComponentLookup<FixtureJobResult>(false);
            Dependency = new FixtureWriteJob { Targets = targets, Target = entity, Value = counter.Value } .Schedule(Dependency);

            FixtureFaultFlag flag = EntityManager.GetComponentData<FixtureFaultFlag>(entity);
            if (flag.Enabled != 0)
            {
                throw new FixtureInjectedFaultException(
                    "fixture fault stage wrote authoritative state and then threw (deterministic fail-stop fixture)");
            }
        }
    }

    /// <summary>Stage after the fault stage: its counter proves that a failed step never reaches later systems.</summary>
    [DisableAutoCreation]
    public partial class FixtureProjectSystem : SystemBase
    {
        private EntityQuery trailQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            trailQuery = GetEntityQuery(ComponentType.ReadWrite<FixtureTrail>());
        }

        protected override void OnUpdate()
        {
            if (trailQuery.IsEmpty)
            {
                return;
            }

            Entity entity = trailQuery.GetSingletonEntity();
            FixtureTrail trail = EntityManager.GetComponentData<FixtureTrail>(entity);
            // The preceding stage's component-writing job must be visible before this dependent read.
            trail.ProjectObservedJobValue = EntityManager.GetComponentData<FixtureJobResult>(entity).Value;
            trail.ProjectCount++;
            EntityManager.SetComponentData(entity, trail);
        }
    }

    /// <summary>Output system: presentation runs on host frames from the last published image (04 s3).</summary>
    [DisableAutoCreation]
    public partial class FixtureOutputSystem : SystemBase
    {
        private EntityQuery trailQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            trailQuery = GetEntityQuery(ComponentType.ReadWrite<FixtureTrail>());
        }

        protected override void OnUpdate()
        {
            if (trailQuery.IsEmpty)
            {
                return;
            }

            Entity entity = trailQuery.GetSingletonEntity();
            FixtureTrail trail = EntityManager.GetComponentData<FixtureTrail>(entity);
            trail.OutputCount++;
            EntityManager.SetComponentData(entity, trail);
        }
    }

    /// <summary>
    /// Unmanaged fixed-step stage. It exercises the <c>SystemHandle.Update(World.Unmanaged)</c> dispatch path that
    /// the managed stages do not cover (04 s4).
    /// </summary>
    [DisableAutoCreation]
    public partial struct FixtureTickSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<FixtureTrail>();
        }

        public void OnUpdate(ref SystemState state)
        {
            EntityQuery query = state.GetEntityQuery(ComponentType.ReadWrite<FixtureTrail>());
            if (query.IsEmpty)
            {
                return;
            }

            Entity entity = query.GetSingletonEntity();
            FixtureTrail trail = state.EntityManager.GetComponentData<FixtureTrail>(entity);
            trail.TickCount++;
            state.EntityManager.SetComponentData(entity, trail);
        }
    }
}
