// GameCore.Unity.Runtime - temporal driver of one owned world (GC-009).
//
// GC-005 owns the accumulators: fixed-step debt/catch-up and command-driven demand are already implemented there,
// and this type does not replace them. What it adds is the per-step input cutoff (P-037) and the registered plugin
// clocks/wakes (P-036, P-038) around the world's existing pump:
//
//   * before a pump it seals the next step's input batch in host admission order and feeds the world's demand
//     (one admitted command or one due wake per logical step for a command-driven world);
//   * after a pump, if no step committed, the sealed batch returns to the queue: a frame below one step duration,
//     a paused world or a refused step retains its input instead of consuming it;
//   * plugin clocks advance only from committed logical steps and explicit domain ticks, never from wall time;
//   * pause applies each clock's declared wake policy, and resume restores accumulation without injecting debt.
//
// The world remains the authority for lifecycle, step, debt and demand (P-002, P-035, P-036); this driver only
// decides which admitted input belongs to which step and what plugin-timer demand exists.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution.Time;

namespace GameCore.Unity.Runtime.Time
{
    /// <summary>What one wrapped pump frame decided and observed (P-036, P-037, P-038).</summary>
    public sealed class TimeFrameReport
    {
        public TimeFrameReport(
            WorldPumpResult pump,
            InputSeal seal,
            int dueWakes,
            int restoredInput,
            ulong stepsCommitted,
            ulong simulationTicks,
            bool paused)
        {
            Pump = pump;
            Seal = seal;
            DueWakes = dueWakes;
            RestoredInput = restoredInput;
            StepsCommitted = stepsCommitted;
            SimulationTicks = simulationTicks;
            Paused = paused;
        }

        public WorldPumpResult Pump { get; }

        /// <summary>The batch sealed for the step this frame advanced, if any.</summary>
        public InputSeal Seal { get; }

        /// <summary>Due plugin wakes handed to the world as demand this frame.</summary>
        public int DueWakes { get; }

        /// <summary>Sealed input returned to the queue because no step committed; zero when a step ran.</summary>
        public int RestoredInput { get; }

        /// <summary>Logical steps this frame committed.</summary>
        public ulong StepsCommitted { get; }

        /// <summary>Simulation ticks those steps represent, which is how duration clocks advanced.</summary>
        public ulong SimulationTicks { get; }

        public bool Paused { get; }

        /// <summary>True when the frame ran at least one step and therefore consumed its sealed batch.</summary>
        public bool ConsumedSeal => StepsCommitted > 0UL;

        public override string ToString()
        {
            return "timeFrame(steps=" + StepsCommitted.ToString(CultureInfo.InvariantCulture)
                + ", commands=" + Seal.AdmittedCommands.ToString(CultureInfo.InvariantCulture)
                + ", wakes=" + DueWakes.ToString(CultureInfo.InvariantCulture)
                + ", restored=" + RestoredInput.ToString(CultureInfo.InvariantCulture)
                + ", paused=" + (Paused ? "yes" : "no") + ")";
        }
    }

    /// <summary>
    /// Temporal driver binding of one world: the input cutoff, the plugin clocks and GC-005's accumulator seen
    /// through the world's own pump. One instance per world; it never creates a second host, accumulator or world.
    /// </summary>
    public sealed class WorldTimeDriver
    {
        private readonly UnityWorldHost host;
        private readonly List<WakeRecord> dueWakes = new List<WakeRecord>();

        private readonly List<NativeDependencyTable> resourceTables = new List<NativeDependencyTable>();

        public WorldTimeDriver(
            UnityWorldHost host,
            StepInputCutoff cutoff,
            PluginClockRegistry clocks,
            uint perStepCommandCapacity,
            int maxPendingInput = 256,
            int maxRememberedRequestKeys = 1024)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            Cutoff = cutoff ?? new StepInputCutoff(maxPendingInput, maxRememberedRequestKeys);
            Clocks = clocks ?? new PluginClockRegistry(64);
            PerStepCommandCapacity = perStepCommandCapacity == 0U ? 1U : perStepCommandCapacity;
        }

        public UnityWorldHost Host => host;

        public StepInputCutoff Cutoff { get; }

        public PluginClockRegistry Clocks { get; }

        /// <summary>
        /// Command units one logical step consumes; the default of one is P-037's command-driven admission rule,
        /// and a domain that declares an atomic batch envelope supplies its own bound at construction.
        /// </summary>
        public uint PerStepCommandCapacity { get; }

        /// <summary>Simulation ticks one logical step represents; zero for a world with no fixed-step duration.</summary>
        public ulong StepDurationTicks
        {
            get
            {
                FixedStepSettings? settings = host.Request.FixedStep;
                return settings == null ? 0UL : settings.StepDurationTicks;
            }
        }

        /// <summary>Frames this driver wrapped.</summary>
        public int FrameCount { get; private set; }

        /// <summary>Batches that had to be returned because their frame committed no step.</summary>
        public int RestoredFrameCount { get; private set; }

        /// <summary>Total input units returned to the queue; retained, never dropped.</summary>
        public ulong RestoredInputTotal { get; private set; }

        /// <summary>Wakes handed to the world as demand.</summary>
        public int WakesFedCount { get; private set; }

        public int PauseCount { get; private set; }

        public int ResumeCount { get; private set; }

        /// <summary>
        /// Admits one command through the cutoff. The host still admits the envelope on its own ledger; this call
        /// only stamps the step-batch order and refuses a duplicate request key (P-037, P-042).
        /// </summary>
        public bool TryAdmitCommand(
            Id128 requestKey,
            SchemaRef schema,
            out AdmissionSequence sequence,
            out DiagnosticCode code)
            => Cutoff.TryAdmit(requestKey, schema, DemandKind.Command, out sequence, out code);

        /// <summary>
        /// Schedules one typed plugin wake on a registered clock. The admission sequence stamp makes equal-delay
        /// wakes canonically ordered without involving thread timing (P-008, P-038).
        /// </summary>
        public bool TryScheduleWake(
            Id128 clockId,
            Id128 wakeId,
            SchemaRef payloadSchema,
            ulong delaySteps,
            ulong delayTicks,
            out WakeRecord? wake,
            out DiagnosticCode code)
            => Clocks.TryScheduleWake(
                clockId,
                wakeId,
                payloadSchema,
                delaySteps,
                delayTicks,
                Cutoff.LastAssignedSequence,
                out wake,
                out code);

        /// <summary>
        /// Adopts a host-owned native resource fence table: it is reset at every step admission this driver performs,
        /// so a slot only ever holds the handle a producer stored in the current step (P-041). The host remains the
        /// table's owner; this driver only decides when its step begins.
        /// </summary>
        public void AdoptResourceTable(NativeDependencyTable table)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            for (int i = 0; i < resourceTables.Count; i++)
            {
                if (ReferenceEquals(resourceTables[i], table))
                {
                    return;
                }
            }

            resourceTables.Add(table);
        }

        /// <summary>Native resource tables this driver resets at step admission, in adoption order.</summary>
        public IReadOnlyList<NativeDependencyTable> ResourceTables => resourceTables;

        /// <summary>
        /// One wrapped host frame: seal the next step's input, hand the world its demand, pump, then advance plugin
        /// clocks by the steps that actually committed.
        /// </summary>
        public TimeFrameReport PumpFrame(ulong hostTicksNow)
        {
            FrameCount++;

            bool running = host.Lifecycle == WorldLifecycleState.Running;
            bool paused = host.Lifecycle == WorldLifecycleState.Paused;

            if (!running && !paused)
            {
                // Faulted, created, stopping or disposed: no seal, no demand, no clock advance. Queued input stays
                // retained in the cutoff and is dropped only by an explicit teardown (P-031, P-048).
                WorldPumpResult refused = host.PumpFrame(hostTicksNow);
                return new TimeFrameReport(refused, InputSeal.None(host.CurrentStep, Cutoff.PendingCount), 0, 0, 0UL, 0UL, false);
            }

            LogicalStepId stepBefore = host.CurrentStep;

            // Step admission: each adopted native resource table starts its step with no stored handle, so a slot
            // never carries a previous step's fence forward (P-041).
            for (int i = 0; i < resourceTables.Count; i++)
            {
                resourceTables[i].ResetStep();
            }

            // A paused world seals nothing and fires no clock; its queued commands simply wait (P-035, O-26).
            InputSeal seal = paused
                ? Cutoff.SealNothing(host.CurrentStep)
                : Cutoff.Seal(host.CurrentStep, FrameCapacity());

            int wakes = 0;
            if (running)
            {
                wakes = FeedDemand(seal);
            }

            WorldPumpResult pump = host.PumpFrame(hostTicksNow);

            ulong stepsCommitted = pump.Pumped && host.CurrentStep.Value > stepBefore.Value
                ? host.CurrentStep.Value - stepBefore.Value
                : 0UL;

            ulong simulationTicks = stepsCommitted * StepDurationTicks;
            int restored = 0;
            if (stepsCommitted == 0UL)
            {
                // No step ran: the sealed batch returns to the queue, in its original admission order (P-037).
                restored = Cutoff.Unseal(seal);
                if (restored > 0)
                {
                    RestoredFrameCount++;
                    RestoredInputTotal += (ulong)restored;
                }
            }
            else
            {
                // Plugin clocks tick only from committed logical steps (P-036, P-038).
                Clocks.AdvanceStep(stepsCommitted, simulationTicks);
            }

            return new TimeFrameReport(pump, seal, wakes, restored, stepsCommitted, simulationTicks, paused);
        }

        /// <summary>
        /// Applies the world's pause to the plugin clocks. Called after the host published its lifecycle change
        /// (O-26); the host remains the author of the transition.
        /// </summary>
        public int OnWorldPaused()
        {
            PauseCount++;
            return Clocks.ApplyPause();
        }

        /// <summary>
        /// Applies the resume to the plugin clocks. Host simulation debt is untouched here: the accumulator owns it
        /// and a paused interval adds none (P-036, O-26).
        /// </summary>
        public int OnWorldResumed()
        {
            ResumeCount++;
            return Clocks.PendingWakeCount;
        }

        /// <summary>Advances an explicit domain clock by ticks a command declared; never inferred from a frame.</summary>
        public int AdvanceDomainClock(Id128 clockId, ulong ticks) => Clocks.AdvanceExplicit(clockId, ticks);

        /// <summary>
        /// Drops every queued command and wake; the teardown path. Returns the number of command units discarded
        /// and the number of wakes still pending, so an operator can see what a shutdown abandoned (P-048).
        /// </summary>
        public void Clear(out int discardedCommands, out int pendingWakes)
        {
            discardedCommands = Cutoff.Clear();
            pendingWakes = Clocks.PendingWakeCount;
            Clocks.Clear();
            dueWakes.Clear();
        }

        /// <summary>
        /// Command units one frame seals: exactly one logical step's worth. A fixed-step frame that catches up
        /// several steps consumes this one sealed batch for its first step; the queue retains the input of the
        /// remaining steps for later frames rather than running a step whose batch was never sealed (P-037).
        /// </summary>
        private uint FrameCapacity() => PerStepCommandCapacity;

        /// <summary>
        /// Hands the world the demand that an admitted command or a due plugin wake represents. Only a
        /// command-driven world consumes demand: it admits one unit per logical step, so a sealed command or a due
        /// wake is exactly one step. A fixed-step world advances on elapsed time, so its demand count is never
        /// inflated; its due wakes are still collected and reported for the caller's adapter stages (P-036, P-037).
        /// </summary>
        private int FeedDemand(InputSeal seal)
        {
            bool commandDriven = host.TemporalModel == TemporalModel.CommandDriven;

            if (commandDriven && seal.AdmittedCommands > 0)
            {
                host.NotifyCommandAdmitted((uint)seal.AdmittedCommands);
            }

            dueWakes.Clear();
            int collected = Clocks.CollectDue(dueWakes);
            if (!commandDriven)
            {
                return collected;
            }

            for (int i = 0; i < collected; i++)
            {
                host.RequestWake(1U);
                WakesFedCount++;
            }

            return collected;
        }
    }
}
