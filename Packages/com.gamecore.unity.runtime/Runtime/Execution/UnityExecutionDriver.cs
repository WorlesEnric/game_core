#nullable enable
using System;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Execution.Messages;
using GameCore.Unity.Runtime.Messages;
using Unity.Collections;
using Unity.Jobs;

namespace GameCore.Unity.Runtime
{
    /// <summary>
    /// Guarded execution driver of one owned world (P-031, P-036, P-041, P-044). It owns step admission, the ordered
    /// dispatch of the bound table, the drain check, the step fence, the fault latch and the commit that advances
    /// <c>LogicalStepId</c> and exposes the step image together.
    /// </summary>
    public sealed class UnityExecutionDriver : IExecutionDriver, IGuardedDispatchSink
    {
        private readonly IWorldExecutionContext context;
        private readonly ITemporalAccumulator temporal;
        private readonly GuardedDispatchPlan stepPlan;
        private NativeArray<Id128> stepJobIds;
        private NativeArray<JobHandle> stepJobHandles;

        private int stepJobCount;
        private bool disposed;

        internal UnityExecutionDriver(
            IWorldExecutionContext context,
            ITemporalAccumulator temporal,
            GuardedDispatchPlan stepPlan)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.temporal = temporal ?? throw new ArgumentNullException(nameof(temporal));
            this.stepPlan = stepPlan ?? throw new ArgumentNullException(nameof(stepPlan));

            int capacity = stepPlan.Entries.Count < 1 ? 1 : stepPlan.Entries.Count;
            stepJobIds = new NativeArray<Id128>(capacity, Allocator.Persistent);
            stepJobHandles = new NativeArray<JobHandle>(capacity, Allocator.Persistent);
            RetainedJobs = new RetainedJobHandles(8);
        }

        public WorldId World => context.World;

        public AssemblyEpoch CurrentEpoch => context.CurrentEpoch;

        public LogicalStepId CurrentStep => context.CurrentStep;

        /// <summary>True once a post-write failure latched; no further dispatch, step or publication occurs (P-031).</summary>
        public bool IsFaulted { get; private set; }

        public DiagnosticCode FaultCode { get; private set; }

        public string FaultDetail { get; private set; } = string.Empty;

        /// <summary>Faults latched by this driver; repeated failures after a latch are not counted again.</summary>
        public int FaultCount { get; private set; }

        /// <summary>Committed logical steps, i.e. how many times this driver advanced a step (TEST-018).</summary>
        public int CommittedStepCount { get; private set; }

        /// <summary>Steps refused before dispatch (faulted, stale, paused, or zero demand) (P-035, O-14).</summary>
        public int RefusedStepCount { get; private set; }

        /// <summary>Temporal samples that were rejected (a backwards host clock or exhausted debt) (P-036).</summary>
        public int RejectedSampleCount { get; private set; }

        /// <summary>Last drain verdict, so an unconsumed reliable buffer is observable at commit (P-043, O-16).</summary>
        public DrainValidation LastDrain { get; private set; } = DrainValidation.Ok;

        /// <summary>Handles retained by a failed step; teardown settles them before storage is released (P-047, P-048).</summary>
        public RetainedJobHandles RetainedJobs { get; }

        bool IGuardedDispatchSink.FaultLatched => IsFaulted;

        /// <summary>Admits and runs at most the requested steps, retaining unspent time as debt (P-036).</summary>
        public StepAdvanceResult Advance(StepAdvanceRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!request.World.Session.Equals(context.World.Session))
            {
                return Reject(request, DiagnosticCode.StaleHandle);
            }

            if (IsFaulted)
            {
                return FaultResult(request, FaultCode);
            }

            WorldLifecycleState lifecycle = context.Lifecycle;
            if (lifecycle == WorldLifecycleState.Faulted)
            {
                return FaultResult(request, DiagnosticCode.ApplyFault);
            }

            if (lifecycle == WorldLifecycleState.Created ||
                lifecycle == WorldLifecycleState.Stopping ||
                lifecycle == WorldLifecycleState.Disposed)
            {
                return Reject(request, DiagnosticCode.TooLate);
            }

            if (!request.BaseEpoch.Equals(context.CurrentEpoch) || !request.ExpectedStep.Equals(context.CurrentStep))
            {
                return Reject(request, DiagnosticCode.StalePlan);
            }

            if (!request.RetainedDebt.Equals(temporal.RetainedDebt))
            {
                // The accumulator owns retained time; a caller carrying a different debt is planning against a stale
                // world image rather than against current host state (P-036).
                return Reject(request, DiagnosticCode.StalePlan);
            }

            if (lifecycle == WorldLifecycleState.Paused || request.RequestedSteps == 0UL)
            {
                // Paused or idle: no step, no epoch change, no image, and no discarded debt (P-035, P-036, O-14).
                RefusedStepCount++;
                return new StepAdvanceResult(
                    true,
                    Outcome.NoChange,
                    DiagnosticCode.None,
                    context.CurrentStep,
                    context.CurrentEpoch,
                    temporal.RetainedDebt,
                    null);
            }

            ulong admitted = request.RequestedSteps;
            uint limit = temporal.MaxStepsPerPump;
            if (admitted > limit)
            {
                admitted = limit;
            }

            SnapshotToken? published = null;
            for (ulong i = 0; i < admitted; i++)
            {
                AssemblyEpoch epoch = context.CurrentEpoch;
                LogicalStepId step = context.CurrentStep;

                // The logical clock is host-supplied; command-driven worlds supply zero delta (04 s3, P-036, P-038).
                context.ApplyStepClock(step);

                // Step admission seals the admitted input prefix: commands arriving after this cutoff wait for the
                // next step, and the sealed batch is what this step's owners may consume (P-037).
                WorldMessagePlane? messages = context.Messages;
                messages?.SealStep(step, epoch);

                DispatchRunResult run = context.StepGroup.DispatchRun(epoch, step, context.StepGroup.BoundTable);
                if (!run.Completed)
                {
                    // The world already wrote authoritative state in this step: no retry, no publication (P-031).
                    return FaultResult(request, run.Code == DiagnosticCode.None ? DiagnosticCode.ApplyFault : run.Code);
                }

                DrainValidation drains = stepPlan.ValidateDrains(
                    context.StepGroup.DispatchedKeySet,
                    context.StepGroup.StageDispatchedFlags);
                LastDrain = drains;
                if (!drains.Succeeded)
                {
                    // Unconsumed reliable data fails the commit rather than publishing partial success (P-043, O-16).
                    LatchFault(drains.Code, "Unconsumed step buffer " + drains.UnconsumedBuffer.ToString() + " at commit.");
                    return FaultResult(request, drains.Code);
                }
                if (messages != null)
                {
                    StepBufferCommitReport messageDrains = messages.ValidateCommit(context.StepGroup, out string messageDetail);
                    if (!messageDrains.Succeeded)
                    {
                        // A reliable declared buffer a producer wrote must have been drained by its consumer stage;
                        // otherwise the commit fails instead of publishing partial success (P-043, O-16).
                        LatchFault(messageDrains.Code, messageDetail);
                        return FaultResult(request, messageDrains.Code);
                    }
                }

                if (!context.StepGroup.Fences!.CompleteAndReset())
                {
                    LatchFault(DiagnosticCode.ApplyFault, "Completing the step fence failed.");
                    return FaultResult(request, DiagnosticCode.ApplyFault);
                }

                // Every recorded handle of this step is covered by the fence that just completed (P-041).
                CompleteStepJobs();

                LogicalStepId committed;
                if (!step.TryIncrement(out committed))
                {
                    LatchFault(DiagnosticCode.BudgetExceeded, "LogicalStepId is exhausted; a world cannot wrap step ids (P-005).");
                    return FaultResult(request, DiagnosticCode.BudgetExceeded);
                }

                if (!context.TryCommitStep(committed, run.DispatchedCount, out SnapshotToken token))
                {
                    LatchFault(DiagnosticCode.ApplyFault, "Publishing the committed step image was refused.");
                    return FaultResult(request, DiagnosticCode.ApplyFault);
                }

                published = token;
                CommittedStepCount++;
                messages?.EndStep();
                context.ConsumeDemand(1UL);
            }

            return new StepAdvanceResult(
                true,
                Outcome.Published,
                DiagnosticCode.None,
                context.CurrentStep,
                context.CurrentEpoch,
                temporal.RetainedDebt,
                published);
        }

        /// <summary>Dispatches one step's ordered table; stops at the first system failure (04 s4).</summary>
        public DispatchRunResult Dispatch(StageDispatchRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (IsFaulted)
            {
                return new DispatchRunResult(false, 0, -1, DiagnosticCode.ApplyFault, default(FactoryKey), null);
            }

            return context.StepGroup.DispatchOne(request);
        }

        /// <summary>Latches a post-write fault raised outside a system invocation (drain, fence or image step).</summary>
        public void LatchFault(DiagnosticCode code, string detail)
        {
            OnDispatchFaulted(code, default(FactoryKey), detail ?? string.Empty);
        }

        Id128 IGuardedDispatchSink.RecordStepJob(
            JobHandle handle,
            StageId stage,
            FactoryKey systemKey,
            AssemblyEpoch epoch,
            LogicalStepId step)
        {
            Id128 jobId = context.Ledger.RecordJob(stage, systemKey, epoch, step);
            if (stepJobCount < stepJobIds.Length)
            {
                stepJobIds[stepJobCount] = jobId;
                stepJobHandles[stepJobCount] = handle;
                stepJobCount++;
            }

            return jobId;
        }

        void IGuardedDispatchSink.CompleteStepJobs() => CompleteStepJobs();

        void IGuardedDispatchSink.RetainStepJobsByQuarantine() => RetainStepJobsByQuarantine();

        void IGuardedDispatchSink.OnDispatchFaulted(DiagnosticCode code, FactoryKey failingSystemKey, string detail)
            => OnDispatchFaulted(code, failingSystemKey, detail);

        /// <summary>Marks this step's recorded jobs complete after its fence completed (P-044).</summary>
        public void CompleteStepJobs()
        {
            for (int i = 0; i < stepJobCount; i++)
            {
                context.Ledger.CompleteJob(stepJobIds[i]);
            }

            stepJobCount = 0;
        }

        /// <summary>
        /// Retains this step's unfinished jobs after a failure: their buffers stay reachable until teardown settles
        /// them, and no timeout authorizes releasing them (P-047, P-048).
        /// </summary>
        public void RetainStepJobsByQuarantine()
        {
            for (int i = 0; i < stepJobCount; i++)
            {
                context.Ledger.RetainJobByQuarantine(stepJobIds[i]);
                RetainedJobs.Add(stepJobIds[i], stepJobHandles[i]);
            }

            stepJobCount = 0;
        }

        /// <summary>Settles every retained handle and reports how many completed (P-048).</summary>
        public int SettleRetainedJobs() => RetainedJobs.CompleteAll(context.Ledger);

        internal void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            stepJobCount = 0;
            RetainedJobs.Dispose();
            if (stepJobIds.IsCreated)
            {
                stepJobIds.Dispose();
            }

            if (stepJobHandles.IsCreated)
            {
                stepJobHandles.Dispose();
            }
        }

        private void OnDispatchFaulted(DiagnosticCode code, FactoryKey failingSystemKey, string detail)
        {
            if (IsFaulted)
            {
                return;
            }

            IsFaulted = true;
            FaultCode = code == DiagnosticCode.None ? DiagnosticCode.ApplyFault : code;
            FaultDetail = failingSystemKey.Equals(default(FactoryKey))
                ? detail ?? string.Empty
                : failingSystemKey.ToString() + ": " + (detail ?? string.Empty);
            FaultCount++;
            context.EnterFaulted(FaultCode, FaultDetail);
        }

        private StepAdvanceResult Reject(StepAdvanceRequest request, DiagnosticCode code)
        {
            RefusedStepCount++;
            return new StepAdvanceResult(
                false,
                Outcome.Rejected,
                code,
                context.CurrentStep,
                context.CurrentEpoch,
                temporal.RetainedDebt,
                null);
        }

        private StepAdvanceResult FaultResult(StepAdvanceRequest request, DiagnosticCode code)
        {
            _ = request;
            return new StepAdvanceResult(
                false,
                Outcome.Faulted,
                code == DiagnosticCode.None ? DiagnosticCode.ApplyFault : code,
                context.CurrentStep,
                context.CurrentEpoch,
                temporal.RetainedDebt,
                null);
        }

        internal void NoteRejectedSample()
        {
            RejectedSampleCount++;
        }
    }
}
