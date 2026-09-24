#nullable enable
using System;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Execution
{
    /// <summary>
    /// Result of one host-clock sample. Fixed-step debt is retained exactly (P-036): elapsed time is never
    /// rounded into a longer step and never silently discarded.
    /// </summary>
    public readonly struct TemporalSample
    {
        public readonly bool Accepted;

        public readonly DiagnosticCode Code;

        /// <summary>Logical steps this sample admits; zero is legal and advances nothing (P-036).</summary>
        public readonly ulong AdmittedSteps;

        /// <summary>Debt remaining after this sample; the host must retain it (P-036).</summary>
        public readonly TimeDebt RetainedDebt;

        /// <summary>Simulation ticks this sample consumed from the retained debt.</summary>
        public readonly ulong ConsumedTicks;

        public TemporalSample(bool accepted, DiagnosticCode code, ulong admittedSteps, TimeDebt retainedDebt, ulong consumedTicks)
        {
            Accepted = accepted;
            Code = code;
            AdmittedSteps = admittedSteps;
            RetainedDebt = retainedDebt;
            ConsumedTicks = consumedTicks;
        }

        public bool HasWork => AdmittedSteps != 0UL;

        /// <summary>No work: an idle command-driven world, a paused world, or a pump below one step duration.</summary>
        public static TemporalSample Idle(TimeDebt debt) => new TemporalSample(true, DiagnosticCode.None, 0UL, debt, 0UL);

        /// <summary>A rejected sample changes nothing: the debt is returned unchanged (P-036).</summary>
        public static TemporalSample Rejected(DiagnosticCode code, TimeDebt debt)
            => new TemporalSample(false, code, 0UL, debt, 0UL);

        public override string ToString()
        {
            return (Accepted ? "accepted" : "rejected")
                + " steps=" + AdmittedSteps.ToString(CultureInfo.InvariantCulture)
                + " debt=" + RetainedDebt.Ticks.ToString(CultureInfo.InvariantCulture)
                + " code=" + Code;
        }
    }

    /// <summary>
    /// Temporal driver of one world (P-036, P-038). It owns the fixed-step accumulator, the retained debt and the
    /// pause state; the caller owns the host clock and the demand count.
    /// </summary>
    public interface ITemporalAccumulator
    {
        TemporalModel Model { get; }

        TimeDebt RetainedDebt { get; }

        bool IsPaused { get; }

        /// <summary>Scaled or unscaled host clock source; a fixed-step configuration choice (P-036).</summary>
        bool UsesUnscaledHostClock { get; }

        /// <summary>Total logical steps this accumulator admitted, for tests and diagnostics.</summary>
        ulong AdmittedStepTotal { get; }
        /// <summary>Catch-up limit of one pump: at most this many logical steps per sample (P-036).</summary>
        uint MaxStepsPerPump { get; }

        /// <summary>Pauses simulation accumulation. Idempotent; no step, epoch or debt change (P-035, O-26).</summary>
        void Pause();

        /// <summary>
        /// Resumes accumulation and resets the host sample origin to <paramref name="hostTicksNow"/>, so the paused
        /// interval adds no debt (P-036, O-26). Idempotent.
        /// </summary>
        void Resume(ulong hostTicksNow);

        /// <summary>Drops the host-time sample origin without changing admitted time or debt.</summary>
        void ResetHostTimeOrigin(ulong hostTicksNow);

        /// <summary>
        /// Samples the host clock. <paramref name="pendingDemand"/> counts admitted commands and registered wake
        /// requests waiting for a logical step; a fixed-step world ignores it and advances on elapsed time.
        /// </summary>
        TemporalSample Sample(ulong hostTicksNow, ulong pendingDemand);
    }

    /// <summary>
    /// Fixed-step accumulator: at most <see cref="FixedStepSettings.MaxStepsPerPump"/> steps per sample, with the
    /// remainder kept as <see cref="TimeDebt"/> and reported to the caller (P-036).
    /// </summary>
    public sealed class FixedStepAccumulator : ITemporalAccumulator
    {
        private readonly FixedStepSettings settings;
        private ulong originTicks;
        private TimeDebt debt = TimeDebt.Zero;

        public FixedStepAccumulator(FixedStepSettings settings, ulong hostTicksOrigin)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (!settings.IsValid)
            {
                throw new ArgumentException(
                    "A fixed-step configuration requires a positive step duration, a positive tick rate and a "
                    + "positive catch-up limit (P-036).",
                    nameof(settings));
            }

            this.settings = settings;
            originTicks = hostTicksOrigin;
        }

        public FixedStepSettings Settings => settings;

        public TemporalModel Model => TemporalModel.FixedStep;

        public TimeDebt RetainedDebt => debt;

        public bool IsPaused { get; private set; }

        public bool UsesUnscaledHostClock => settings.UsesUnscaledHostClock;

        public ulong AdmittedStepTotal { get; private set; }

        public uint MaxStepsPerPump => settings.MaxStepsPerPump;

        public ulong HostElapsedTicks { get; private set; }

        public void Pause() => IsPaused = true;

        public void Resume(ulong hostTicksNow)
        {
            IsPaused = false;
            originTicks = hostTicksNow;
        }

        public void ResetHostTimeOrigin(ulong hostTicksNow) => originTicks = hostTicksNow;

        public TemporalSample Sample(ulong hostTicksNow, ulong pendingDemand)
        {
            if (IsPaused)
            {
                // A paused world freezes accumulation; the origin advances so the paused interval adds no debt,
                // and retained debt is preserved for the resume (P-036, O-26).
                originTicks = hostTicksNow;
                return TemporalSample.Idle(debt);
            }

            if (hostTicksNow < originTicks)
            {
                // A host clock that moved backwards is a timing error, not negative elapsed time (P-038).
                return TemporalSample.Rejected(DiagnosticCode.UnsupportedVersion, debt);
            }

            ulong elapsed = hostTicksNow - originTicks;
            originTicks = hostTicksNow;
            HostElapsedTicks += elapsed;

            TimeDebt accumulated = debt.Add(elapsed, out bool accepted);
            if (!accepted)
            {
                return TemporalSample.Rejected(DiagnosticCode.BudgetExceeded, debt);
            }

            ulong whole = accumulated.WholeSteps(settings.StepDurationTicks, out bool valid);
            if (!valid)
            {
                return TemporalSample.Rejected(DiagnosticCode.UnsupportedVersion, debt);
            }

            ulong admitted = whole < settings.MaxStepsPerPump ? whole : settings.MaxStepsPerPump;
            ulong consumedTicks = admitted * settings.StepDurationTicks;
            TimeDebt retained = accumulated.Subtract(consumedTicks, out bool consumed);
            if (!consumed)
            {
                return TemporalSample.Rejected(DiagnosticCode.BudgetExceeded, debt);
            }

            debt = retained;
            AdmittedStepTotal += admitted;
            return new TemporalSample(true, DiagnosticCode.None, admitted, retained, consumedTicks);
        }
    }

    /// <summary>
    /// Command-driven accumulator: steps come only from accepted commands and registered wake requests. Elapsed
    /// host time is recorded for diagnostics and adds no debt and no simulation seconds (P-036, P-038).
    /// </summary>
    public sealed class CommandDrivenAccumulator : ITemporalAccumulator
    {
        private readonly uint maxStepsPerPump;
        private ulong originTicks;

        public CommandDrivenAccumulator(uint maxStepsPerPump, ulong hostTicksOrigin)
        {
            if (maxStepsPerPump == 0U)
            {
                throw new ArgumentException(
                    "A command-driven world still needs a positive per-pump admission limit (P-036, P-037).",
                    nameof(maxStepsPerPump));
            }

            this.maxStepsPerPump = maxStepsPerPump;
            originTicks = hostTicksOrigin;
        }

        public TemporalModel Model => TemporalModel.CommandDriven;

        /// <summary>Always zero: a command-driven world has no implicit elapsed simulation seconds (P-036).</summary>
        public TimeDebt RetainedDebt => TimeDebt.Zero;

        public bool IsPaused { get; private set; }

        public bool UsesUnscaledHostClock => false;

        public ulong AdmittedStepTotal { get; private set; }

        public ulong HostElapsedTicks { get; private set; }

        public uint MaxStepsPerPump => maxStepsPerPump;

        public void Pause() => IsPaused = true;

        public void Resume(ulong hostTicksNow)
        {
            IsPaused = false;
            originTicks = hostTicksNow;
        }

        public void ResetHostTimeOrigin(ulong hostTicksNow) => originTicks = hostTicksNow;

        public TemporalSample Sample(ulong hostTicksNow, ulong pendingDemand)
        {
            if (hostTicksNow >= originTicks)
            {
                HostElapsedTicks += hostTicksNow - originTicks;
            }

            originTicks = hostTicksNow;

            if (IsPaused)
            {
                // Paused worlds accept queued commands but do not advance simulation (P-035).
                return TemporalSample.Idle(TimeDebt.Zero);
            }

            ulong admitted = pendingDemand < maxStepsPerPump ? pendingDemand : maxStepsPerPump;
            AdmittedStepTotal += admitted;
            return new TemporalSample(true, DiagnosticCode.None, admitted, TimeDebt.Zero, 0UL);
        }
    }

    /// <summary>Creates the accumulator for a world's temporal model (P-036).</summary>
    public static class TemporalAccumulators
    {
        public static ITemporalAccumulator Create(TemporalModel model, FixedStepSettings? settings, ulong hostTicksOrigin)
        {
            if (model == TemporalModel.FixedStep)
            {
                if (settings == null)
                {
                    throw new ArgumentException(
                        "A fixed-step world must declare its step duration, clock source and catch-up limit (P-036).",
                        nameof(settings));
                }

                return new FixedStepAccumulator(settings, hostTicksOrigin);
            }

            // CommandDriven admits one pending command or one wake per logical step by default (P-037).
            return new CommandDrivenAccumulator(1U, hostTicksOrigin);
        }
    }
}
