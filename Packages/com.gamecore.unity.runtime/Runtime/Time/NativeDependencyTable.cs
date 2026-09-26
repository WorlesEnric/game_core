// GameCore.Unity.Runtime - non-component native resource dependencies (GC-009).
//
// Unity's component safety tracks declared component access, but a job that touches a native container the ECS
// does not own (a plugin's external queue, a blob replacement, a service-owned buffer, a manually scheduled job)
// is invisible to that tracking. 04 section 4 requires explicit dependency registration for exactly those cases.
//
// Every declared buffer of the compiled schedule therefore gets a slot here. A producer stores its output
// `JobHandle` in the slot, which (a) makes the handle the fence a dependent reader or a structural playback must
// combine, and (b) forwards it into the host-owned stage fence table, so the step's publication fence really
// completes it and a failed step retains it behind quarantine instead of releasing its memory (P-041, P-047,
// P-048). The table is reset at step admission and is only touched by the serialized scheduler on the main thread;
// a running worker never captures it.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using Unity.Collections;
using Unity.Jobs;

namespace GameCore.Unity.Runtime.Time
{
    /// <summary>
    /// Host-owned fence table of non-component native resources, indexed by resource slot (P-041). Slot indices are
    /// assigned when the schedule is adapted, so the table never resizes during a step. The native backing appears
    /// with the first stored producer fence, so an adapted schedule whose systems schedule no non-component job
    /// holds no native memory at all.
    /// </summary>
    public sealed class NativeDependencyTable : IDisposable
    {
        // The backing array appears with the first stored producer fence: a schedule whose systems never schedule
        // a non-component job holds no native memory at all (P-041), and the slots never resize once allocated.
        private NativeArray<JobHandle> slots;
        private JobHandle stepFence;
        private bool disposed;

        public NativeDependencyTable(int slotCount)
        {
            if (slotCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slotCount), "A fence table cannot be negative.");
            }

            SlotCount = slotCount;
        }

        /// <summary>Number of resource slots; zero for a schedule with no declared buffer.</summary>
        public int SlotCount { get; }

        /// <summary>Handles stored this step; the count of produced non-component work items.</summary>
        public int ProducedCount { get; private set; }

        /// <summary>Reads that combined a slot fence; the count of dependent readers this step.</summary>
        public int DependentReadCount { get; private set; }

        /// <summary>Slot indices ever read or written without being produced first; a scheduling defect (P-041).</summary>
        public int UnproducedReadCount { get; private set; }

        /// <summary>Completion failures seen at the commit boundary; a failed completion is never a false success.</summary>
        public int CompletionFailureCount { get; private set; }

        public bool IsCreated => !disposed;

        /// <summary>Combined fence of every non-component handle produced this step; the step's own extra fence.</summary>
        public JobHandle StepFence => stepFence;

        /// <summary>True when a slot holds a non-default handle; a default handle means nothing was produced.</summary>
        public bool HasProduced(int slot)
        {
            RequireSlot(slot);
            return slots.IsCreated && !slots[slot].Equals(default(JobHandle));
        }

        public JobHandle FenceOf(int slot)
        {
            RequireSlot(slot);
            return slots.IsCreated ? slots[slot] : default(JobHandle);
        }

        /// <summary>
        /// Clears every slot at step admission. The table is host-owned and is never read by a running worker, so
        /// clearing it cannot race a producer (04 section 4).
        /// </summary>
        public void ResetStep()
        {
            if (disposed)
            {
                return;
            }

            if (slots.IsCreated)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    slots[i] = default(JobHandle);
                }
            }

            stepFence = default(JobHandle);
            ProducedCount = 0;
            DependentReadCount = 0;
            UnproducedReadCount = 0;
        }

        /// <summary>
        /// Combines the fences a consumer must wait for. An unproduced slot contributes nothing rather than a stale
        /// handle, and it is counted so an unpublished producer is detectable instead of silent (P-041).
        /// </summary>
        public JobHandle CombineIncoming(IReadOnlyList<int> resourceSlots)
        {
            if (resourceSlots == null)
            {
                throw new ArgumentNullException(nameof(resourceSlots));
            }

            DependentReadCount++;
            if (resourceSlots.Count == 0)
            {
                return default(JobHandle);
            }

            if (resourceSlots.Count == 1)
            {
                RequireSlot(resourceSlots[0]);
                JobHandle single = FenceInSlot(resourceSlots[0]);
                if (single.Equals(default(JobHandle)))
                {
                    UnproducedReadCount++;
                }

                return single;
            }

            JobHandle combined = default(JobHandle);
            for (int i = 0; i < resourceSlots.Count; i++)
            {
                RequireSlot(resourceSlots[i]);
                JobHandle handle = FenceInSlot(resourceSlots[i]);
                if (handle.Equals(default(JobHandle)))
                {
                    UnproducedReadCount++;
                    continue;
                }

                combined = combined.Equals(default(JobHandle))
                    ? handle
                    : JobHandle.CombineDependencies(combined, handle);
            }

            return combined;
        }

        /// <summary>Convenience for the single-slot case.</summary>
        public JobHandle CombineIncoming(int resourceSlot) => CombineIncoming(new[] { resourceSlot });

        /// <summary>
        /// Records a producer's output handle for one native resource slot. The handle also enters the host stage
        /// fence at <paramref name="stageIndex"/> and, when a sink is supplied, the step's tracked job set, so it is
        /// completed at the step's publication boundary or retained behind quarantine after a fault (P-041, P-047).
        /// </summary>
        public Id128 Store(
            int resourceSlot,
            JobHandle produced,
            int stageIndex,
            StageId stage,
            FactoryKey systemKey,
            AssemblyEpoch epoch,
            LogicalStepId step,
            IGuardedDispatchSink? sink,
            NativeFenceTable? stageFences)
        {
            RequireSlot(resourceSlot);
            EnsureSlots();

            // P-043 permits duplicate producers for one buffer: the slot accumulates their fences instead of keeping
            // only the last stored handle, so a dependent reader waits for every producer of that resource.
            slots[resourceSlot] = JobHandle.CombineDependencies(slots[resourceSlot], produced);
            stepFence = JobHandle.CombineDependencies(stepFence, produced);
            ProducedCount++;

            if (stageFences != null)
            {
                // The stage fence is the dispatcher's publication input: forwarding here is what makes a later
                // dependent stage wait for a non-component producer (04 section 4).
                stageFences.Store(stageIndex, produced);
            }

            if (sink == null)
            {
                return default(Id128);
            }

            return sink.RecordStepJob(produced, stage, systemKey, epoch, step);
        }

        /// <summary>
        /// Completes every handle produced this step. Called at the commit boundary, before a dependent read or a
        /// structural playback reads through the container on the main thread.
        /// </summary>
        public bool CompleteStepFence()
        {
            bool ok = true;
            try
            {
                stepFence.Complete();
            }
            catch (Exception)
            {
                CompletionFailureCount++;
                ok = false;
            }

            return ok;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (slots.IsCreated)
            {
                slots.Dispose();
            }
        }

        /// <summary>Allocates the fixed slot backing; called only when a producer actually stores a fence.</summary>
        private void EnsureSlots()
        {
            if (!slots.IsCreated)
            {
                // Cleared, so a table that never saw a step-admission reset still combines no stale handle.
                slots = new NativeArray<JobHandle>(SlotCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            }
        }

        private JobHandle FenceInSlot(int slot) => slots.IsCreated ? slots[slot] : default(JobHandle);

        private void RequireSlot(int slot)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(NativeDependencyTable));
            }

            if (slot < 0 || slot >= SlotCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(slot),
                    "Resource slot " + slot.ToString(CultureInfo.InvariantCulture) + " is outside the table of "
                    + SlotCount.ToString(CultureInfo.InvariantCulture) + " slots.");
            }
        }

        public override string ToString()
        {
            return "nativeDependencies(slots=" + SlotCount.ToString(CultureInfo.InvariantCulture)
                + ", produced=" + ProducedCount.ToString(CultureInfo.InvariantCulture) + ")";
        }
    }
}
