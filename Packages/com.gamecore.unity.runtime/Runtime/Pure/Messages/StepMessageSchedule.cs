// GameCore.Execution.Messages — the declared buffer set of one step: sealing, drain validation, deferred playback
// (GC-007).
//
// Normative sources: docs/game-core/00-core-protocols.md P-037 (input cutoff seals each step's batch), P-041
// (stage-local deferred structural buffer played back after its producers finish and before dependent readers),
// P-043 (no consumer reads a buffer before producer completion; unconsumed step buffers fail commit validation) and
// P-044 (a successful step completes all required work and validates required drains before publication).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Execution.Messages
{
    /// <summary>What actually ran in one step, so buffer edges and drain rules can be checked against fact (P-043).</summary>
    public interface IStepDispatchFacts
    {
        /// <summary>True when the given producer system key was dispatched in this step.</summary>
        bool Ran(FactoryKey producer);

        /// <summary>True when at least one entry of the given stage ran in this step.</summary>
        bool RanStage(StageId stage);
    }

    /// <summary>Sealing state of one step's admitted input prefix (P-037).</summary>
    public readonly struct InputSeal
    {
        public readonly LogicalStepId Step;
        public readonly AssemblyEpoch Epoch;

        /// <summary>Highest host-assigned admission sequence included in this step; zero means an empty prefix.</summary>
        public readonly AdmissionSequence Cutoff;

        /// <summary>Number of admitted commands in the sealed prefix.</summary>
        public readonly int AdmittedCommands;

        public InputSeal(LogicalStepId step, AssemblyEpoch epoch, AdmissionSequence cutoff, int admittedCommands)
        {
            Step = step;
            Epoch = epoch;
            Cutoff = cutoff;
            AdmittedCommands = admittedCommands;
        }

        public bool IsEmpty => AdmittedCommands == 0;

        public override string ToString()
            => "step" + Step.Value.ToString(CultureInfo.InvariantCulture) + "@" + Epoch.ToString()
                + " cutoff=" + Cutoff.ToString();
    }

    /// <summary>Outcome of one commit-time buffer validation across every declared buffer (P-043, P-044).</summary>
    public sealed class StepBufferCommitReport
    {
        internal StepBufferCommitReport(
            bool succeeded,
            DiagnosticCode code,
            IReadOnlyList<MessageDrainReport> drains,
            string detail)
        {
            Succeeded = succeeded;
            Code = code;
            Drains = ContractCollections.Freeze(drains);
            Detail = detail ?? string.Empty;
        }

        public bool Succeeded { get; }

        public DiagnosticCode Code { get; }

        /// <summary>Per-buffer drain verdicts for this step, in buffer identity order.</summary>
        public IReadOnlyList<MessageDrainReport> Drains { get; }

        public string Detail { get; }

        public override string ToString()
            => Succeeded ? "buffers committed" : Code + ": " + Detail;
    }

    /// <summary>
    /// The declared bounded buffers of one world plus the sealing, drain and deferred-playback rules that connect
    /// them. It owns no authority: an owner commits through its own buffer, and this type only proves that the
    /// declared edges were respected (P-043).
    /// </summary>
    public sealed class StepMessageSchedule : ITelemetryOwner
    {
        string ITelemetryOwner.TelemetryOwner => "gamecore.messages.schedule";

        private readonly List<MessageBufferDescriptor> descriptors = new List<MessageBufferDescriptor>();
        private readonly Dictionary<Id128, BoundedMessageBuffer> buffers = new Dictionary<Id128, BoundedMessageBuffer>();
        private readonly List<BufferReadPort> readPorts = new List<BufferReadPort>();
        private readonly List<DeferredStructuralOperation> deferred = new List<DeferredStructuralOperation>();

        private ushort nextStepCapacity = 64;
        private AdmissionSequence lastCutoff = AdmissionSequence.Zero;
        private OperationId lastAdmittedRequest;

        /// <summary>Declares one bounded buffer; a duplicate buffer identity is refused, not replaced (P-043).</summary>
        public bool TryDeclare(MessageBufferDescriptor descriptor, out string failure)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            if (descriptor.Buffer.Value.IsDefault)
            {
                failure = "a default zero buffer id is not a buffer contract (P-043)";
                return false;
            }

            for (int i = 0; i < descriptors.Count; i++)
            {
                if (!descriptors[i].Buffer.Value.Equals(descriptor.Buffer.Value))
                {
                    continue;
                }

                failure = "buffer " + descriptor.Buffer.ToString()
                    + " is already declared; a buffer has one contract (P-043)";
                return false;
            }

            // A buffer with exactly one consuming owner: a second declaration naming another owner is an authority
            // conflict, never a second reader (P-043).
            if (descriptor.Owner.Value.IsDefault)
            {
                failure = "buffer " + descriptor.Buffer.ToString()
                    + " declares a default zero owner; a buffer has exactly one consuming owner (P-043)";
                return false;
            }

            if (descriptor.Schema.Id.Value.IsDefault)
            {
                failure = "buffer " + descriptor.Buffer.ToString() + " declares a default zero schema (P-043)";
                return false;
            }

            if (descriptor.ConsumerStage.Value.IsDefault)
            {
                failure = "buffer " + descriptor.Buffer.ToString()
                    + " declares a default zero consumer stage; commit validation needs the consuming stage (P-043)";
                return false;
            }

            for (int i = 0; i < descriptors.Count; i++)
            {
                MessageBufferDescriptor existing = descriptors[i];
                if (existing.Owner.Equals(descriptor.Owner)
                    && existing.ConsumerStage.Equals(descriptor.ConsumerStage)
                    && existing.Schema.Id.Value.Equals(descriptor.Schema.Id.Value)
                    && existing.Lifetime != descriptor.Lifetime)
                {
                    failure = "owner " + descriptor.Owner.ToString()
                        + " declares two lifetimes for one schema/stage pair; a buffer contract fixes one lifetime (P-043)";
                    return false;
                }
            }

            descriptors.Add(descriptor);
            buffers.Add(descriptor.Buffer.Value, new BoundedMessageBuffer(descriptor));
            failure = string.Empty;
            return true;
        }

        /// <summary>Declares an explicit immutable read port: fan-out never transfers the consuming owner (P-043).</summary>
        public bool TryAddReadPort(BufferReadPort port, out string failure)
        {
            if (!buffers.ContainsKey(port.Buffer.Value))
            {
                failure = "read port names buffer " + port.Buffer.ToString() + " which is not declared (P-043)";
                return false;
            }

            readPorts.Add(port);
            failure = string.Empty;
            return true;
        }

        public int BufferCount => descriptors.Count;

        public int ReadPortCount => readPorts.Count;

        public IReadOnlyList<MessageBufferDescriptor> Descriptors => descriptors;

        public IReadOnlyList<BufferReadPort> ReadPorts => readPorts;
        public int PendingStructuralCount => deferred.Count;

        /// <summary>
        /// Cumulative structural operations recorded into a deferred buffer (08 `structural operations`, P-041).
        /// Unlike <see cref="PendingStructuralCount"/> this is a running total, so a per-step structural regression
        /// is visible after playback has emptied the buffers.
        /// </summary>
        public long RecordedStructuralOperationCount { get; private set; }

        /// <summary>Deepest the step's deferred structural buffer was observed to be (08 request high-water).</summary>
        public int StructuralHighWaterMark { get; private set; }

        /// <summary>Writes the step-message counters through the fixed compact schema (GC-023).</summary>
        public void WriteTelemetry(TelemetryCounterSet into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Add(TelemetryCounter.StructuralOperations, RecordedStructuralOperationCount);
            // The deepest the deferred buffer was observed to be, not its current depth: a caller that samples after
            // playback would otherwise always read zero and could never see a structural backlog (GC-023).
            into.ObserveMax(TelemetryCounter.RequestHighWater, StructuralHighWaterMark);
        }

        /// <summary>Bounded capacity of deferred next-step queues, per buffer (P-043).</summary>
        public ushort NextStepCapacity
        {
            get => nextStepCapacity;
            set => nextStepCapacity = value;
        }

        /// <summary>Highest admission sequence sealed into a step so far (P-037).</summary>
        public AdmissionSequence LastCutoff => lastCutoff;

        public bool TryGetBuffer(BufferId buffer, out BoundedMessageBuffer? bounded)
            => buffers.TryGetValue(buffer.Value, out bounded);

        /// <summary>
        /// Seals the admitted input prefix of one step. Commands that arrive after this cutoff wait for the next
        /// step; the seal itself is host-assigned and monotonic (P-037).
        /// </summary>
        public InputSeal SealStepInput(
            LogicalStepId step,
            AssemblyEpoch epoch,
            AdmissionSequence cutoff,
            IReadOnlyList<OperationId>? admitted)
        {
            if (cutoff.CompareTo(lastCutoff) < 0)
            {
                // Admission sequences are monotonic; a backward seal would reorder admitted input (P-037).
                throw new InvalidOperationException(
                    "the input cutoff of step " + step.Value.ToString(CultureInfo.InvariantCulture)
                    + " is below the last sealed cutoff " + lastCutoff.ToString()
                    + "; admission sequences are monotonic (P-037).");
            }

            lastCutoff = cutoff;
            int count = admitted == null ? 0 : admitted.Count;
            if (count != 0)
            {
                lastAdmittedRequest = admitted![count - 1];
            }

            return new InputSeal(step, epoch, cutoff, count);
        }

        /// <summary>The last request identity of the sealed prefix; default when nothing was admitted (P-037).</summary>
        public OperationId LastAdmittedRequest => lastAdmittedRequest;

        /// <summary>
        /// Records a structural operation into a declared stage-local deferred buffer (P-041). The operation is
        /// played back later, never applied while a producer is still writing.
        /// </summary>
        public bool TryRecordStructural(
            BufferId buffer,
            FactoryKey producer,
            TargetId target,
            SchemaRef schema,
            bool add,
            MessageOrderKey order,
            out string failure)
        {
            if (!buffers.TryGetValue(buffer.Value, out BoundedMessageBuffer? bounded) || bounded == null)
            {
                failure = "deferred buffer " + buffer.ToString() + " is not declared (P-041)";
                return false;
            }

            if (!bounded.Descriptor.DeclaresProducer(producer) && bounded.Descriptor.Producers.Count != 0)
            {
                failure = "system " + producer.ToString() + " is not a declared producer of deferred buffer "
                    + buffer.ToString() + " (P-043)";
                return false;
            }

            if (target.Value.IsDefault)
            {
                failure = "a deferred structural operation must name a stable target id (P-041)";
                return false;
            }

            if (deferred.Count >= bounded.Descriptor.Capacity)
            {
                failure = "deferred buffer " + buffer.ToString() + " is at its declared capacity of "
                    + bounded.Descriptor.Capacity.ToString(CultureInfo.InvariantCulture)
                    + "; the affected batch is rejected before mutation (P-041, P-043)";
                return false;
            }

            deferred.Add(new DeferredStructuralOperation(buffer, producer, target, schema, add, order));
            RecordedStructuralOperationCount++;
            if (deferred.Count > StructuralHighWaterMark)
            {
                StructuralHighWaterMark = deferred.Count;
            }
            failure = string.Empty;
            return true;
        }

        /// <summary>
        /// Plays back the deferred operations in canonical stable order once the named producers completed and
        /// before dependent readers run (P-041). The returned sequence is independent of record order.
        /// </summary>
        public IReadOnlyList<DeferredStructuralOperation> Playback(IReadOnlyList<FactoryKey>? completedProducers)
        {
            var playback = new List<DeferredStructuralOperation>();
            if (completedProducers == null)
            {
                playback.AddRange(deferred);
                deferred.Clear();
            }
            else
            {
                for (int i = 0; i < deferred.Count; i++)
                {
                    if (!ContainsProducer(completedProducers, deferred[i].Producer))
                    {
                        continue;
                    }

                    // Only a producer that actually completed releases its operations; an unfinished producer keeps
                    // them queued for the next legal boundary (P-041).
                    playback.Add(deferred[i]);
                    deferred.RemoveAt(i);
                    i--;
                }
            }

            playback.Sort(DeferredOperationComparer.Instance);
            return playback;
        }

        /// <summary>
        /// Commit-time validation: every reliable buffer a producer wrote in this step must have been drained by its
        /// single consuming stage, and every declared producer→consumer edge must have been respected (P-043, O-16).
        /// </summary>
        public StepBufferCommitReport ValidateCommit(IStepDispatchFacts facts)
        {
            if (facts == null)
            {
                throw new ArgumentNullException(nameof(facts));
            }

            var drains = new List<MessageDrainReport>(descriptors.Count);
            for (int i = 0; i < descriptors.Count; i++)
            {
                MessageBufferDescriptor descriptor = descriptors[i];
                BoundedMessageBuffer bounded = buffers[descriptor.Buffer.Value];

                if (bounded.RowCount == 0)
                {
                    drains.Add(MessageDrainReport.Ok);
                    continue;
                }

                bool produced = false;
                for (int p = 0; p < descriptor.Producers.Count; p++)
                {
                    if (facts.Ran(descriptor.Producers[p]))
                    {
                        produced = true;
                        break;
                    }
                }

                if (!produced && descriptor.Producers.Count != 0)
                {
                    // Rows without a producer that ran can only come from an earlier step of a next-step queue.
                    drains.Add(MessageDrainReport.Ok);
                    continue;
                }

                if (descriptor.IsReliable && !facts.RanStage(descriptor.ConsumerStage))
                {
                    // No consumer stage ran, so the reliable rows would be dropped by the boundary: fail the commit.
                    drains.Add(MessageDrainReport.Unconsumed(descriptor.Buffer, bounded.RowCount));
                    continue;
                }

                drains.Add(BoundedBufferRules.ValidateDrain(descriptor, bounded.RowCount, bounded.WasConsumed));
            }

            for (int i = 0; i < drains.Count; i++)
            {
                if (!drains[i].Succeeded)
                {
                    return new StepBufferCommitReport(false, drains[i].Code, drains, drains[i].Detail);
                }
            }

            return new StepBufferCommitReport(true, DiagnosticCode.None, drains, string.Empty);
        }

        /// <summary>
        /// Closes one step for every buffer: reliable stage/step rows are released, bounded next-step queues carry
        /// forward with revalidated stable references, and deferred work behind an unfinished producer stays queued
        /// (P-043, P-047).
        /// </summary>
        public IReadOnlyList<NextStepCarryReport> EndStep(IMessageTargetLiveness? liveness)
        {
            var reports = new List<NextStepCarryReport>(descriptors.Count);
            for (int i = 0; i < descriptors.Count; i++)
            {
                MessageBufferDescriptor descriptor = descriptors[i];
                BoundedMessageBuffer bounded = buffers[descriptor.Buffer.Value];
                NextStepCarryReport report = bounded.EndStep(liveness, nextStepCapacity);
                if (descriptor.Lifetime == BufferLifetime.BoundedNextStep)
                {
                    reports.Add(report);
                }
            }

            return reports;
        }

        /// <summary>Restores every buffer to the state of a world that has not advanced a step (P-031 fail-stop).</summary>
        public void AbortStep()
        {
            for (int i = 0; i < descriptors.Count; i++)
            {
                BoundedMessageBuffer bounded = buffers[descriptors[i].Buffer.Value];
                bounded.ConsumeSealedBatch();
            }
        }

        /// <summary>Every buffer restores to empty: used by a world recreation, never by a normal boundary.</summary>
        public void ResetAll()
        {
            for (int i = 0; i < descriptors.Count; i++)
            {
                BoundedMessageBuffer bounded = buffers[descriptors[i].Buffer.Value];
                bounded.ConsumeSealedBatch();
                bounded.EndStep(AlwaysLiveTargets.Instance, nextStepCapacity);
            }

            deferred.Clear();
            lastCutoff = AdmissionSequence.Zero;
            lastAdmittedRequest = default(OperationId);
        }

        public override string ToString()
            => "buffers=" + descriptors.Count.ToString(CultureInfo.InvariantCulture)
                + ", ports=" + readPorts.Count.ToString(CultureInfo.InvariantCulture)
                + ", deferred=" + deferred.Count.ToString(CultureInfo.InvariantCulture);

        private static bool ContainsProducer(IReadOnlyList<FactoryKey> producers, FactoryKey producer)
        {
            for (int i = 0; i < producers.Count; i++)
            {
                if (producers[i].RegistrationKey.Equals(producer.RegistrationKey))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
