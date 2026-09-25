// GameCore.Unity.Runtime - engine-free temporal core (GC-009), namespace GameCore.Execution.Time.
//
// Registered plugin clocks and typed wake records (P-036, P-038). `LogicalStepId`, fixed-step simulation duration,
// monotonic host timestamps and plugin-local clocks are distinct: a plugin clock declares how it advances, how it
// behaves while the world is paused, and whether it persists. A clock can schedule a typed wake, which is the only
// way a plugin-timer makes a command-driven world advance without a player command. Wakes are data: collecting
// them produces demand for the temporal driver, never a callback that mutates ECS.
//
// This file is Unity-free and is compiled both into the Unity assembly GameCore.Unity.Runtime and by the
// plain-dotnet project dotnet/src/GameCore.Execution (Runtime/Pure/**).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Execution.Time
{
    /// <summary>How a registered plugin clock advances (P-038).</summary>
    public enum PluginClockKind
    {
        /// <summary>Advances one tick per committed logical step; it has no wall-clock meaning (P-038).</summary>
        LogicalStep = 0,

        /// <summary>Advances by the fixed-step simulation duration the world declares (P-036).</summary>
        FixedDuration = 1,

        /// <summary>Declared, repeatable clock the domain advances explicitly; the world never infers it (P-038).</summary>
        DomainExplicit = 2,
    }

    /// <summary>What happens to a plugin clock's pending wakes while the world is paused (P-035, P-038, O-26).</summary>
    public enum WakePausePolicy
    {
        /// <summary>Due and pending wakes stay pending across the pause and fire after resume.</summary>
        Defer = 0,

        /// <summary>The pause cancels every pending wake of this clock; each cancellation is counted.</summary>
        Cancel = 1,
    }

    /// <summary>Declaration of one plugin-local clock (P-038).</summary>
    public sealed class PluginClockSpec
    {
        public PluginClockSpec(Id128 clockId, string diagnosticName, PluginClockKind kind, WakePausePolicy onPause, bool persists)
        {
            ClockId = clockId;
            DiagnosticName = string.IsNullOrEmpty(diagnosticName) ? "clock" : diagnosticName;
            Kind = kind;
            OnPause = onPause;
            Persists = persists;
        }

        public Id128 ClockId { get; }

        /// <summary>Diagnostic name only; it never participates in identity or ordering (P-004, P-008).</summary>
        public string DiagnosticName { get; }

        public PluginClockKind Kind { get; }

        public WakePausePolicy OnPause { get; }

        /// <summary>True when the clock's value belongs in a checkpoint; a transient clock is not saved (P-053).</summary>
        public bool Persists { get; }

        public override string ToString() => DiagnosticName + ":" + Kind.ToString();
    }

    /// <summary>
    /// One typed wake request (P-036, P-038). It carries a stable wake id so duplicate delivery can be recognised,
    /// the clock that owns it, the payload schema the domain will read, and the remaining delay.
    /// </summary>
    public sealed class WakeRecord
    {
        internal WakeRecord(
            Id128 wakeId,
            Id128 clockId,
            SchemaRef payloadSchema,
            ulong delaySteps,
            ulong delayTicks,
            ulong scheduledAtSequence)
        {
            WakeId = wakeId;
            ClockId = clockId;
            PayloadSchema = payloadSchema;
            RemainingSteps = delaySteps;
            RemainingTicks = delayTicks;
            ScheduledAtSequence = scheduledAtSequence;
        }

        public Id128 WakeId { get; }

        public Id128 ClockId { get; }

        public SchemaRef PayloadSchema { get; }

        /// <summary>Host admission sequence at scheduling time; canonical order of equal-delay wakes (P-008, P-038).</summary>
        public ulong ScheduledAtSequence { get; }

        /// <summary>Remaining delay in logical steps for a <see cref="PluginClockKind.LogicalStep"/> clock.</summary>
        public ulong RemainingSteps { get; private set; }

        /// <summary>Remaining delay in clock ticks for a duration or explicit clock.</summary>
        public ulong RemainingTicks { get; private set; }

        /// <summary>True once the wake is due; a due wake becomes one unit of demand (P-036).</summary>
        public bool IsDue { get; private set; }

        /// <summary>True once the driver consumed it as demand; a consumed wake never fires twice (P-037).</summary>
        public bool IsConsumed { get; private set; }

        /// <summary>True when a pause policy cancelled it; a cancelled wake is retained for diagnosis (P-038).</summary>
        public bool IsCancelled { get; private set; }

        public bool IsPending => !IsDue && !IsCancelled && !IsConsumed;

        internal void Advance(ulong steps, ulong ticks)
        {
            if (IsCancelled || IsConsumed || IsDue)
            {
                return;
            }

            if (RemainingTicks > 0UL)
            {
                RemainingTicks = ticks >= RemainingTicks ? 0UL : RemainingTicks - ticks;
            }

            if (RemainingSteps > 0UL)
            {
                RemainingSteps = steps >= RemainingSteps ? 0UL : RemainingSteps - steps;
            }

            if (RemainingSteps == 0UL && RemainingTicks == 0UL)
            {
                IsDue = true;
            }
        }

        internal void Cancel() => IsCancelled = true;

        internal void Consume() => IsConsumed = true;

        public override string ToString()
        {
            return "wake(" + WakeId.ToString() + " clock=" + ClockId.ToString()
                + " steps=" + RemainingSteps.ToString(CultureInfo.InvariantCulture)
                + " ticks=" + RemainingTicks.ToString(CultureInfo.InvariantCulture)
                + (IsDue ? " due" : string.Empty)
                + (IsConsumed ? " consumed" : string.Empty)
                + (IsCancelled ? " cancelled" : string.Empty) + ")";
        }
    }

    /// <summary>
    /// Registered plugin clocks of one world (P-038). Bounded: a clock that schedules more wakes than the declared
    /// capacity is refused explicitly instead of overflowing silently, and wake demand is collected in canonical
    /// order (due, then scheduling sequence, then wake id) rather than in completion order.
    /// </summary>
    public sealed class PluginClockRegistry
    {
        private readonly Dictionary<Id128, PluginClockSpec> clocks = new Dictionary<Id128, PluginClockSpec>();
        private readonly Dictionary<Id128, List<WakeRecord>> wakesByClock = new Dictionary<Id128, List<WakeRecord>>();
        private readonly List<WakeRecord> allWakes = new List<WakeRecord>();
        private readonly int maxWakes;

        public PluginClockRegistry(int maxWakes)
        {
            if (maxWakes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxWakes), "The wake queue must be positive (P-038).");
            }

            this.maxWakes = maxWakes;
        }

        public int ClockCount => clocks.Count;

        public int WakeCount => allWakes.Count;

        /// <summary>Wakes refused because the bounded queue was full; reported, never silently dropped (P-038).</summary>
        public int OverflowCount { get; private set; }

        /// <summary>Duplicate wake ids refused; a wake id identifies one wake (P-004).</summary>
        public int DuplicateCount { get; private set; }

        public int CancelledCount { get; private set; }

        public int ConsumedCount { get; private set; }

        public int PendingWakeCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < allWakes.Count; i++)
                {
                    if (allWakes[i].IsPending)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int DueWakeCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < allWakes.Count; i++)
                {
                    if (allWakes[i].IsDue && !allWakes[i].IsConsumed)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Registered clocks in canonical id order; iteration order is never registration order (P-008).</summary>
        public IReadOnlyList<PluginClockSpec> Clocks
        {
            get
            {
                var list = new List<PluginClockSpec>(clocks.Values);
                list.Sort(CompareClockSpecs);
                return list;
            }
        }

        /// <summary>Registers one clock declaration. A repeated id with a different declaration is refused (P-004).</summary>
        public bool TryRegister(PluginClockSpec spec, out DiagnosticCode code)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            if (spec.ClockId.IsDefault)
            {
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            if (clocks.TryGetValue(spec.ClockId, out PluginClockSpec? existing))
            {
                if (existing.Kind != spec.Kind || existing.OnPause != spec.OnPause || existing.Persists != spec.Persists)
                {
                    code = DiagnosticCode.OwnershipConflict;
                    return false;
                }

                code = DiagnosticCode.None;
                return true;
            }

            clocks.Add(spec.ClockId, spec);
            wakesByClock.Add(spec.ClockId, new List<WakeRecord>());
            code = DiagnosticCode.None;
            return true;
        }

        /// <summary>
        /// Unregisters a clock. Every wake that has not been consumed or cancelled — pending or already due — is
        /// cancelled and retained for diagnosis, so a wake whose clock is gone never fires and never silently
        /// disappears (P-047, P-048). Re-registering the same id afterwards is legal: the per-clock list is dropped
        /// here, so a plugin reload cannot leave a stale key behind.
        /// </summary>
        public int TryUnregister(Id128 clockId)
        {
            if (!clocks.Remove(clockId))
            {
                return 0;
            }

            int cancelled = 0;
            if (wakesByClock.TryGetValue(clockId, out List<WakeRecord>? wakes))
            {
                for (int i = 0; i < wakes.Count; i++)
                {
                    if (wakes[i].IsConsumed || wakes[i].IsCancelled)
                    {
                        continue;
                    }

                    wakes[i].Cancel();
                    cancelled++;
                    CancelledCount++;
                }

                wakesByClock.Remove(clockId);
            }

            return cancelled;
        }

        public bool IsRegistered(Id128 clockId) => clocks.ContainsKey(clockId);

        /// <summary>
        /// Schedules one typed wake on a registered clock. <paramref name="delaySteps"/> and
        /// <paramref name="delayTicks"/> are read according to the clock's declared kind, and the admitted sequence
        /// is the caller's host admission stamp, so equal-delay wakes have a canonical order (P-008, P-038).
        /// </summary>
        public bool TryScheduleWake(
            Id128 clockId,
            Id128 wakeId,
            SchemaRef payloadSchema,
            ulong delaySteps,
            ulong delayTicks,
            ulong admittedSequence,
            out WakeRecord? wake,
            out DiagnosticCode code)
        {
            wake = null;

            if (!clocks.TryGetValue(clockId, out PluginClockSpec? spec))
            {
                code = DiagnosticCode.MissingDependency;
                return false;
            }

            if (wakeId.IsDefault)
            {
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            for (int i = 0; i < allWakes.Count; i++)
            {
                if (allWakes[i].WakeId.Equals(wakeId))
                {
                    DuplicateCount++;
                    code = DiagnosticCode.IdempotencyConflict;
                    return false;
                }
            }

            if (allWakes.Count >= maxWakes)
            {
                OverflowCount++;
                code = DiagnosticCode.BudgetExceeded;
                return false;
            }

            // A logical-step clock counts steps; every other kind counts ticks in its own domain (P-038).
            ulong steps = spec.Kind == PluginClockKind.LogicalStep ? delaySteps : 0UL;
            ulong ticks = spec.Kind == PluginClockKind.LogicalStep ? 0UL : delayTicks;
            var created = new WakeRecord(wakeId, clockId, payloadSchema, steps, ticks, admittedSequence);
            if (steps == 0UL && ticks == 0UL)
            {
                created.Advance(0UL, 0UL);
            }

            allWakes.Add(created);
            wakesByClock[clockId].Add(created);
            wake = created;
            code = DiagnosticCode.None;
            return true;
        }

        /// <summary>
        /// Advances every registered clock by the logical steps a frame actually committed: a logical-step clock
        /// advances one tick per committed step, a duration clock by the committed simulation ticks, and an explicit
        /// clock only when the domain advances it (P-036, P-038).
        /// </summary>
        /// <param name="committedSteps">Logical steps the committing frame ran; zero advances no logical clock.</param>
        /// <param name="simulationTicks">Simulation ticks those steps represent.</param>
        /// <returns>Number of wakes that became due.</returns>
        public int AdvanceStep(ulong committedSteps, ulong simulationTicks)
        {
            int due = 0;
            for (int i = 0; i < allWakes.Count; i++)
            {
                WakeRecord wake = allWakes[i];
                if (!wake.IsPending)
                {
                    continue;
                }

                if (!clocks.TryGetValue(wake.ClockId, out PluginClockSpec? owning))
                {
                    // Its clock was unregistered: the wake was cancelled there and never fires.
                    continue;
                }

                PluginClockKind kind = owning.Kind;
                ulong steps = kind == PluginClockKind.LogicalStep ? committedSteps : 0UL;
                ulong ticks = kind == PluginClockKind.FixedDuration ? simulationTicks : 0UL;
                wake.Advance(steps, ticks);
                if (wake.IsDue)
                {
                    due++;
                }
            }

            return due;
        }

        /// <summary>Advances explicit (domain) clocks by an amount a command declared; never inferred from a frame.</summary>
        public int AdvanceExplicit(Id128 clockId, ulong ticks)
        {
            if (!wakesByClock.TryGetValue(clockId, out List<WakeRecord>? wakes))
            {
                return 0;
            }

            int due = 0;
            for (int i = 0; i < wakes.Count; i++)
            {
                WakeRecord wake = wakes[i];
                if (!wake.IsPending)
                {
                    continue;
                }

                wake.Advance(0UL, ticks);
                if (wake.IsDue)
                {
                    due++;
                }
            }

            return due;
        }

        /// <summary>
        /// Collects due wakes as demand, in canonical order (scheduling sequence, then wake id), and marks them
        /// consumed. A paused world calls nothing: its clocks do not fire (P-035, P-036, P-038).
        /// </summary>
        public int CollectDue(List<WakeRecord> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            var candidates = new List<WakeRecord>();
            for (int i = 0; i < allWakes.Count; i++)
            {
                WakeRecord wake = allWakes[i];
                if (wake.IsDue && !wake.IsConsumed && !wake.IsCancelled)
                {
                    candidates.Add(wake);
                }
            }

            candidates.Sort(CompareWakes);

            for (int i = 0; i < candidates.Count; i++)
            {
                candidates[i].Consume();
                ConsumedCount++;
                into.Add(candidates[i]);
            }

            return candidates.Count;
        }

        /// <summary>
        /// Applies the world's pause to every clock: a <see cref="WakePausePolicy.Defer"/> clock keeps its wakes
        /// (pending or already due but unconsumed) for the resume, a <see cref="WakePausePolicy.Cancel"/> clock
        /// cancels them and counts the loss (P-035, P-038, O-26).
        /// </summary>
        public int ApplyPause()
        {
            int cancelled = 0;
            for (int i = 0; i < allWakes.Count; i++)
            {
                WakeRecord wake = allWakes[i];
                if (wake.IsConsumed || wake.IsCancelled)
                {
                    continue;
                }

                if (clocks.TryGetValue(wake.ClockId, out PluginClockSpec? owning)
                    && owning.OnPause == WakePausePolicy.Cancel)
                {
                    wake.Cancel();
                    cancelled++;
                    CancelledCount++;
                }
            }

            return cancelled;
        }

        /// <summary>Removes consumed and cancelled wakes, so a long session does not retain unbounded tombstones.</summary>
        public int Compact()
        {
            int removed = 0;
            for (int i = allWakes.Count - 1; i >= 0; i--)
            {
                WakeRecord wake = allWakes[i];
                if (!wake.IsConsumed && !wake.IsCancelled)
                {
                    continue;
                }

                allWakes.RemoveAt(i);
                if (wakesByClock.TryGetValue(wake.ClockId, out List<WakeRecord>? list))
                {
                    list.Remove(wake);
                }

                removed++;
            }

            return removed;
        }

        /// <summary>Clears every clock and wake; the teardown path (P-048).</summary>
        public void Clear()
        {
            clocks.Clear();
            wakesByClock.Clear();
            allWakes.Clear();
        }

        public IReadOnlyList<WakeRecord> WakesOf(Id128 clockId)
            => wakesByClock.TryGetValue(clockId, out List<WakeRecord>? wakes)
                ? new List<WakeRecord>(wakes)
                : Array.Empty<WakeRecord>();

        private static int CompareClockSpecs(PluginClockSpec left, PluginClockSpec right)
            => left.ClockId.CompareTo(right.ClockId);

        private static int CompareWakes(WakeRecord left, WakeRecord right)
        {
            int order = left.ScheduledAtSequence.CompareTo(right.ScheduledAtSequence);
            return order != 0 ? order : left.WakeId.CompareTo(right.WakeId);
        }
    }
}
