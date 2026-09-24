// GameCore.Composition — installation lifecycle state machine (P-046, 06 s1).
//
// 06 s1 fixes the exact legal transitions. This type is the single place that decides whether an edge is legal,
// so no caller can invent a shortcut into `Active` or quietly revive a retired installation. Invalid edges are
// reported as a diagnostic value, never as an exception in normal control flow (P-051).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Composition
{
    /// <summary>Result of one requested lifecycle transition.</summary>
    public sealed class LifecycleTransition
    {
        private LifecycleTransition(bool allowed, InstallationState from, InstallationState to, DiagnosticCode code)
        {
            Allowed = allowed;
            From = from;
            To = to;
            Code = code;
        }

        public bool Allowed { get; }

        public InstallationState From { get; }

        public InstallationState To { get; }

        /// <summary><see cref="DiagnosticCode.None"/> when allowed; otherwise the rejection code.</summary>
        public DiagnosticCode Code { get; }

        public static LifecycleTransition Permit(InstallationState from, InstallationState to) =>
            new LifecycleTransition(true, from, to, DiagnosticCode.None);

        /// <summary>
        /// A refused lifecycle edge. <see cref="DiagnosticCode.OwnershipConflict"/> is used because the request
        /// claims an authority the current state does not grant; the code set is fixed by 00 s9 and has no
        /// separate "illegal transition" literal (<see cref="DiagnosticCode.Cycle"/> stays reserved for graphs).
        /// </summary>
        public static LifecycleTransition Refuse(InstallationState from, InstallationState to) =>
            new LifecycleTransition(false, from, to, DiagnosticCode.OwnershipConflict);

        public override string ToString() => From + " -> " + To + (Allowed ? " (allowed)" : " (refused)");
    }

    /// <summary>
    /// Installation lifecycle transition table and the small amount of policy P-046 states about each step:
    /// which states contribute gameplay, which keep the installation, and which are terminal.
    /// </summary>
    public static class InstallationStateMachine
    {
        /// <summary>
        /// Legal edges, exactly the 17 transitions of the 06 s1 diagram. `Quiescing` is an internal transitional
        /// state with the old committed assembly still visible, so a removal or suspension of an *active*
        /// installation walks `Active -> Quiescing -> ...` rather than short-circuiting; `Retiring` follows an
        /// already published removal, so its cleanup can no longer roll anything back. An active activation never
        /// becomes `Failed` on its own: only a candidate activation that fails while preparing does (P-046).
        /// </summary>
        public static bool IsAllowed(InstallationState from, InstallationState to)
        {
            if (from == to)
            {
                return false;
            }

            switch (from)
            {
                case InstallationState.Registered:
                    return to == InstallationState.WaitingForDependencies ||
                           to == InstallationState.Preparing ||
                           to == InstallationState.Retiring;
                case InstallationState.WaitingForDependencies:
                    return to == InstallationState.Preparing || to == InstallationState.Retiring;
                case InstallationState.Preparing:
                    return to == InstallationState.Active || to == InstallationState.Failed;
                case InstallationState.Active:
                    return to == InstallationState.Quiescing;
                case InstallationState.Quiescing:
                    // The prewrite abort reopens the old gates, which is the one path back to Active (06 s1).
                    return to == InstallationState.Active ||
                           to == InstallationState.Suspended ||
                           to == InstallationState.WaitingForDependencies ||
                           to == InstallationState.Retiring;
                case InstallationState.Suspended:
                    return to == InstallationState.Preparing || to == InstallationState.Retiring;
                case InstallationState.Failed:
                    return to == InstallationState.Preparing || to == InstallationState.Retiring;
                case InstallationState.Retiring:
                    return to == InstallationState.Disposed;
                default:
                    return false;
            }
        }

        /// <summary>Evaluates one edge and reports the refusal as a value (P-051).</summary>
        public static LifecycleTransition Request(InstallationState from, InstallationState to) =>
            IsAllowed(from, to) ? LifecycleTransition.Permit(from, to) : LifecycleTransition.Refuse(from, to);

        /// <summary>
        /// The lawful teardown path from one stored state to `Retiring`, as the sequence of diagram edges an
        /// unmount walks. An active installation must pass through `Quiescing`, which is where the old committed
        /// assembly is still visible and new callbacks are already closed (P-047); the other states reach
        /// `Retiring` in one step. `Preparing` has no teardown edge in the diagram, and this host never stores a
        /// preparing installation, so it is reported as unavailable rather than guessed at.
        /// </summary>
        public static bool TryTeardownPath(InstallationState from, out IReadOnlyList<InstallationState>? path)
        {
            switch (from)
            {
                case InstallationState.Active:
                    path = new[] { InstallationState.Quiescing, InstallationState.Retiring };
                    return true;
                case InstallationState.Quiescing:
                case InstallationState.WaitingForDependencies:
                case InstallationState.Suspended:
                case InstallationState.Failed:
                case InstallationState.Registered:
                    path = new[] { InstallationState.Retiring };
                    return true;
                default:
                    path = null;
                    return false;
            }
        }

        /// <summary>
        /// Whether an installation in this state can carry an activation that resolution may bind right now.
        /// `Registered`, `WaitingForDependencies`, `Preparing` and `Active` are resolved normally, where the
        /// dependency closure decides between `Active` and `WaitingForDependencies`. `Suspended`, `Quiescing`,
        /// `Retiring`, `Disposed` and `Failed` cannot: their activation has lost its authority or is being torn
        /// down, so they expose no bindings at all rather than a stale set (P-012, P-046, P-047). The resolver
        /// consults this predicate, so the rule has exactly one home.
        /// </summary>
        public static bool CanResolveActivation(InstallationState state) =>
            state == InstallationState.Registered ||
            state == InstallationState.WaitingForDependencies ||
            state == InstallationState.Preparing ||
            state == InstallationState.Active;

    }
}
