// GameCore.Gameplay.Rewards — what one reward-lifecycle operation did (GC-024).
//
// Every operation of `RewardsInstallation` returns one of these values instead of throwing, and its `Detail` names
// the identity triple the conformance step asserts: the installation id, the outbox slot id and the pending-work
// count. A caller therefore never has to guess which installation or which slot a refusal was about (P-052).
//
// The outcome set is deliberately small and each member names the shipped mechanism behind it:
//
//   * `Settled`           — the operation completed: the lane published the installation to `Disposed` with no
//                          retained lease, or the state-policy pass applied the declared `PreserveDormant`
//                          decision, or the pending-work lease was armed/completed.
//   * `RefusedPendingWork`— a teardown could not settle because unfinished work still reaches a resource: the
//                          P-048 order reported `DiagnosticCode.TeardownBlocked` and the installation stayed
//                          `Retiring` (TeardownSequencer.cs:328-336, InstallationLifecycleCoordinator.cs:448-459).
//   * `RefusedPrecondition`— the registered migration refused the copied pending-work count, so 07 s5's "no pending
//                          work remains" precondition failed and the old assembly keeps its state (P-029).
//   * `Transferred`       — the durable rows moved to the named destination owner.
//   * `Unsupported`       — nothing could be attempted, or the shipped contract cannot express the request; the
//                          detail says exactly what was asked and what the kernel answered.
//
// There is deliberately no `Undo`, `Revert`, `Rollback` or `Effect` member and no such method anywhere in this
// package: a granted reward is committed gameplay and nothing reverses it (P-003).
#nullable enable
using GameCore.Contracts;

namespace GameCore.Gameplay.Rewards
{
    /// <summary>The enumerated outcome of one reward-lifecycle operation.</summary>
    public enum RewardsLifecycleOutcome
    {
        /// <summary>The operation completed.</summary>
        Settled = 0,

        /// <summary>A teardown was blocked by unfinished work that still reaches a held resource (P-047, P-048).</summary>
        RefusedPendingWork = 1,

        /// <summary>The migration precondition refused the copied pending-work count (07 s5, P-029).</summary>
        RefusedPrecondition = 2,

        /// <summary>The durable outbox rows moved to the named destination owner.</summary>
        Transferred = 3,

        /// <summary>Nothing was attempted, or the shipped contract cannot express the request.</summary>
        Unsupported = 4,
    }

    /// <summary>
    /// One lifecycle result: the outcome, the diagnostic code the kernel reported for it, and a detail that names
    /// the installation, the outbox slot and the pending-work count.
    /// </summary>
    public readonly struct RewardsLifecycleResult
    {
        public RewardsLifecycleResult(RewardsLifecycleOutcome outcome, DiagnosticCode code, string detail)
        {
            Outcome = outcome;
            Code = code;
            Detail = detail ?? string.Empty;
        }

        /// <summary>What the operation did.</summary>
        public RewardsLifecycleOutcome Outcome { get; }

        /// <summary>The code the kernel reported; `None` when nothing refused.</summary>
        public DiagnosticCode Code { get; }

        /// <summary>
        /// Human-readable evidence: this installation's id, the outbox slot's id, the pending-work count, and
        /// whatever the mechanism reported (a plan's code and detail, a teardown report, an admission decision).
        /// </summary>
        public string Detail { get; }

        /// <summary>True only for <see cref="RewardsLifecycleOutcome.Settled"/>.</summary>
        public bool Settled => Outcome == RewardsLifecycleOutcome.Settled;

        /// <summary>The diagnostic text of <see cref="Code"/>, for a caller that renders one line.</summary>
        public string CodeText => DiagnosticCodeText.Of(Code);

        public override string ToString() => Outcome.ToString() + "(" + CodeText + "): " + Detail;
    }
}
