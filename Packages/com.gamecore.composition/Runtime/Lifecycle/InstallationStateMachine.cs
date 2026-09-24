// GameCore.Composition — installation lifecycle state machine (P-046, 06 s1).
//
// 06 s1 fixes the exact legal transitions. This type is the single place that decides whether an edge is legal,
// so no caller can invent a shortcut into `Active` or quietly revive a retired installation. Invalid edges are
// reported as a diagnostic value, never as an exception in normal control flow (P-051).
#nullable enable
using System;
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
        /// Legal edges, exactly the diagram in 06 s1. `Quiescing` is an internal transitional state with the old
        /// committed assembly still visible; `Retiring` follows an already published removal, so its cleanup can
        /// no longer roll anything back.
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
                    return to == InstallationState.Active ||
                           to == InstallationState.WaitingForDependencies ||
                           to == InstallationState.Failed ||
                           to == InstallationState.Retiring;
                case InstallationState.Active:
                    return to == InstallationState.Quiescing ||
                           to == InstallationState.Failed ||
                           to == InstallationState.Retiring;
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
        /// Whether the installation contributes gameplay in this state. A published waiting instance is a real
        /// composition change but has no active contribution (P-012, O-03).
        /// </summary>
        public static bool Contributes(InstallationState state) =>
            state == InstallationState.Active || state == InstallationState.Quiescing;

        /// <summary>Whether the installation's definition and configuration are retained (P-046 suspension).</summary>
        public static bool RetainsDefinition(InstallationState state) =>
            state != InstallationState.Disposed && state != InstallationState.Retiring;

        /// <summary>Whether the installation keeps execution authority; only an active activation may execute.</summary>
        public static bool HasExecutionAuthority(InstallationState state) => state == InstallationState.Active;

        /// <summary>Terminal states from which no further activation is possible (P-046).</summary>
        public static bool IsTerminal(InstallationState state) => state == InstallationState.Disposed;

        /// <summary>
        /// Whether a new activation epoch is required for this edge. Authority-changing edges move the epoch;
        /// a pure state flip inside a published lifecycle change does not (P-006).
        /// </summary>
        public static bool ChangesActivationEpoch(InstallationState from, InstallationState to) =>
            (to == InstallationState.Active && from != InstallationState.Active) ||
            (from == InstallationState.Active && to != InstallationState.Quiescing);
    }
}
