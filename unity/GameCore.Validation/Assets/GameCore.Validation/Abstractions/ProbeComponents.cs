#nullable enable
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace GameCore.Validation.Probe
{
    /// <summary>
    /// Unmanaged component owned by the probe world. The probe creates entities with this component and reads
    /// them back through a real <c>EntityQuery</c>; no managed model substitutes for ECS storage.
    /// </summary>
    public struct ProbeCounter : IComponentData
    {
        public int Value;
    }

    /// <summary>Component that no entity carries; used to prove an empty query result.</summary>
    public struct ProbeAbsent : IComponentData
    {
        public int Value;
    }

    /// <summary>
    /// Generic Burst job whose closed instantiation <c>ProbeAggregateJob&lt;ProbeVector3Value&gt;</c> is rooted by
    /// the generated catalog and registered through <c>[assembly: RegisterGenericJobType]</c>. The payload array
    /// length participates in the result, so a wrong or missing instantiation changes the observable answer.
    /// </summary>
    [BurstCompile]
    public struct ProbeAggregateJob<TPayload> : IJob
        where TPayload : unmanaged
    {
        public ProbeAggregateJob(
            NativeArray<TPayload> payloads,
            NativeArray<int> sources,
            NativeArray<int> result,
            int scale)
        {
            Payloads = payloads;
            Sources = sources;
            Result = result;
            Scale = scale;
        }

        public NativeArray<TPayload> Payloads;

        public NativeArray<int> Sources;

        public NativeArray<int> Result;

        public int Scale;

        public void Execute()
        {
            int total = Payloads.Length;
            for (int i = 0; i < Sources.Length; i++)
            {
                total += Sources[i] * Scale;
            }

            Result[0] = total;
        }
    }
}
