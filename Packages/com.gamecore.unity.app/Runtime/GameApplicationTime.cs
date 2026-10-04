// GameCore.Unity.App - the world time driver of an application root (P1.1, completing P0.4 leftover 3).
//
// The application pump (GameCoreApplicationPump) calls the adapter frame, then host.PumpFrame, then the adapter frame
// again; it never calls WorldTimeDriver.PumpFrame, which would be a second pump path. So the root drives the time
// driver through its adapter frame instead, inside the one-pump counter:
//
//   CollectInput (before the host pump):  reset every adopted native resource table (P-041), seal the next step's
//                                         input batch through the driver's cutoff (P-037) and hand the world the
//                                         demand of sealed commands and due plugin wakes (command-driven worlds);
//   Present      (after the host pump):   if no logical step committed, return the sealed batch to the cutoff;
//                                         otherwise advance the plugin clocks by the committed steps (P-036, P-038).
//
// This is exactly WorldTimeDriver.PumpFrame's algorithm split around the pump the application already performs. The
// root adopts the schedule adaptation's native dependency table at composition, and pause/resume apply the clocks'
// declared wake policies. Commands submitted through UnityWorldHost.Submit keep creating their own demand there; the
// cutoff carries input admitted through WorldTimeDriver.TryAdmitCommand (plugin-side input) and wakes.
#nullable enable
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution.Time;
using GameCore.Unity.Adapters;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Time;

namespace GameCore.Unity.App
{
    /// <summary>The adapter frame that runs the root's <see cref="WorldTimeDriver"/> around the one application pump.</summary>
    public sealed class GameApplicationTimeFrame : IAdapterFrame
    {
        private readonly UnityWorldHost host;
        private readonly List<WakeRecord> due = new List<WakeRecord>();
        private IAdapterFrame? inner;
        private InputSeal? seal;
        private ulong stepBefore;
        private bool armed;

        public GameApplicationTimeFrame(UnityWorldHost host, WorldTimeDriver time)
        {
            this.host = host ?? throw new System.ArgumentNullException(nameof(host));
            Time = time ?? throw new System.ArgumentNullException(nameof(time));
        }

        public WorldId World => host.World;

        public WorldTimeDriver Time { get; }

        /// <summary>The game's own adapter frame, called after the time driver's input half and before its present half.</summary>
        public IAdapterFrame? Inner => inner;

        /// <summary>Frames whose input half ran.</summary>
        public int Frames { get; private set; }

        /// <summary>Native resource table resets performed (adopted tables x frames that admitted a step).</summary>
        public int TableResets { get; private set; }

        /// <summary>Logical steps the plugin clocks were advanced by.</summary>
        public ulong StepsAdvanced { get; private set; }

        /// <summary>Sealed input units returned to the cutoff because their frame committed no step.</summary>
        public int RestoredInput { get; private set; }

        /// <summary>Due wakes handed to the world as demand.</summary>
        public int WakesFed { get; private set; }

        public void SetInner(IAdapterFrame? frame) => inner = frame;

        public AdapterFrameReport CollectInput()
        {
            Frames++;
            WorldLifecycleState lifecycle = host.Lifecycle;
            bool running = lifecycle == WorldLifecycleState.Running;
            bool paused = lifecycle == WorldLifecycleState.Paused;
            armed = running || paused;
            if (armed)
            {
                IReadOnlyList<NativeDependencyTable> tables = Time.ResourceTables;
                for (int i = 0; i < tables.Count; i++)
                {
                    tables[i].ResetStep();
                    TableResets++;
                }

                stepBefore = host.CurrentStep.Value;
                seal = paused
                    ? Time.Cutoff.SealNothing(host.CurrentStep)
                    : Time.Cutoff.Seal(host.CurrentStep, Time.PerStepCommandCapacity);
                if (running)
                {
                    FeedDemand(seal);
                }
            }

            return inner != null ? inner.CollectInput() : AdapterFrameReport.Completed("time", 0, 0, "sealed");
        }

        public AdapterFrameReport Present()
        {
            if (armed)
            {
                armed = false;
                ulong now = host.CurrentStep.Value;
                ulong steps = now > stepBefore ? now - stepBefore : 0UL;
                if (steps == 0UL)
                {
                    RestoredInput += Time.Cutoff.Unseal(seal);
                }
                else
                {
                    Time.Clocks.AdvanceStep(steps, steps * Time.StepDurationTicks);
                    StepsAdvanced += steps;
                }

                seal = null;
            }

            return inner != null ? inner.Present() : AdapterFrameReport.Completed("time", 0, 0, "advanced");
        }

        public override string ToString() =>
            "timeFrame{frames=" + Frames.ToString(CultureInfo.InvariantCulture)
            + ";steps=" + StepsAdvanced.ToString(CultureInfo.InvariantCulture)
            + ";resets=" + TableResets.ToString(CultureInfo.InvariantCulture)
            + ";restored=" + RestoredInput.ToString(CultureInfo.InvariantCulture) + "}";

        private void FeedDemand(InputSeal batch)
        {
            bool commandDriven = host.TemporalModel == TemporalModel.CommandDriven;
            if (commandDriven && batch.AdmittedCommands > 0)
            {
                host.NotifyCommandAdmitted((uint)batch.AdmittedCommands);
            }

            due.Clear();
            int collected = Time.Clocks.CollectDue(due);
            if (!commandDriven)
            {
                return;
            }

            for (int i = 0; i < collected; i++)
            {
                host.RequestWake(1U);
                WakesFed++;
            }
        }
    }
}
