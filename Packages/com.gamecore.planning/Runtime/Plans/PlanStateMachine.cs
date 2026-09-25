// GameCore.Planning — plan states and their legal transitions (GC-008).
//
// Normative sources: docs/game-core/00-core-protocols.md (P-027 states, P-028 validation and recheck, P-029
// preparation, P-030 publication, P-031 apply failure, P-050/P-051 operation discipline) and
// docs/game-core/05-contracts-and-data-model.md s4 (plan/result fields).
//
// 05 s4 fixes the state sequence: `Draft -> Validated -> Prepared -> Applying -> Published`, with `Rejected`,
// `Cancelled` and `Faulted` as the alternatives, and it fixes the *meaning* of the terminal outcomes:
//
//   * `PublishedWithCleanupErrors` is an outcome, not a state: the new assembly is already authoritative and
//     cannot be "cancelled back", while `Rejected` means no live writes were made and `Faulted` means live
//     storage is no longer safe to execute. This type keeps those three distinguishable instead of returning a
//     bool, because collapsing them is the defect the protocol names.
//   * Only one terminal transition is possible per plan; a refused transition changes nothing and is counted,
//     so a caller that accidentally retries a faulted plan sees a refusal rather than a second apply.
//
// The machine owns no live state, no ECS access and no clock: `AssemblyPlanner` drives it while planning and
// `AssemblyPublisher` drives it across the fenced apply boundary (P-002: the planner is a pure consumer of
// immutable snapshots).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Planning
{
    /// <summary>Plan state (P-027). Terminal states admit no further transition.</summary>
    public enum PlanPhase
    {
        Draft = 0,
        Validated = 1,
        Prepared = 2,
        Applying = 3,
        Published = 4,
        Rejected = 5,
        Cancelled = 6,
        Faulted = 7,
    }

    /// <summary>One recorded transition, kept for diagnosis: plans are inspectable after the fact (P-052).</summary>
    public readonly struct PlanTransition
    {
        public readonly PlanPhase From;
        public readonly PlanPhase To;
        public readonly DiagnosticCode Code;
        public readonly string Reason;

        public PlanTransition(PlanPhase from, PlanPhase to, DiagnosticCode code, string reason)
        {
            From = from;
            To = to;
            Code = code;
            Reason = reason ?? string.Empty;
        }

        public bool IsTerminal => PlanStateMachine.IsTerminalPhase(To);

        public override string ToString() =>
            From.ToString() + "->" + To.ToString()
            + (Code == DiagnosticCode.None ? string.Empty : "(" + DiagnosticCodeText.Of(Code) + ")");
    }

    /// <summary>
    /// State machine of one <see cref="ChangePlan"/>. Every transition is guarded by the table in P-027 and every
    /// refusal is a value: no exception is used as control flow, because a plan rejection is a protocol outcome.
    /// </summary>
    public sealed class PlanStateMachine
    {
        private readonly List<PlanTransition> history = new List<PlanTransition>();

        public PlanStateMachine(ChangePlan plan)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            Phase = PlanPhase.Draft;
            Outcome = Outcome.Pending;
            Code = DiagnosticCode.None;
            Detail = string.Empty;
        }

        /// <summary>The immutable plan this machine tracks; its contents never change (05 s4).</summary>
        public ChangePlan Plan { get; }

        public PlanPhase Phase { get; private set; }

        /// <summary><see cref="Outcome.Pending"/> until a terminal state; then the terminal outcome (05 s4).</summary>
        public Outcome Outcome { get; private set; }

        public DiagnosticCode Code { get; private set; }

        public string Detail { get; private set; }

        /// <summary>Transitions refused because they are not in P-027's table; a repeated terminal drive lands here.</summary>
        public int RefusedTransitionCount { get; private set; }

        /// <summary>Transition history in order; the first entry is the plan's validation.</summary>
        public IReadOnlyList<PlanTransition> History => history;

        public bool IsTerminal => IsTerminalPhase(Phase);

        /// <summary>True once the plan's staged resources are known and the fence may copy/migrate state (P-029).</summary>
        public bool IsPrepared => Phase == PlanPhase.Prepared || Phase == PlanPhase.Applying;

        /// <summary>True once live writes may have happened, i.e. the postwrite fault cutoff (P-031).</summary>
        public bool HasCrossedLiveWriteBoundary => Phase == PlanPhase.Applying || Phase == PlanPhase.Published;

        /// <summary>True when the plan published with cleanup errors, which is still an authoritative assembly.</summary>
        public bool PublishedWithCleanupErrors =>
            Phase == PlanPhase.Published && Outcome == Outcome.PublishedWithCleanupErrors;

        /// <summary>True when the plan published without a single live write; `NoChange` advances no counter.</summary>
        public bool IsNoChange => Plan.Composition.Installs.Count == 0
            && Plan.Composition.Scopes.Count == 0
            && Plan.Composition.Memberships.Count == 0
            && Plan.Composition.Configs.Count == 0
            && Plan.Composition.Mode == null
            && Plan.Derivation.Added.Count == 0
            && Plan.Derivation.Removed.Count == 0
            && Plan.Derivation.Changed.Count == 0
            && Plan.Runtime.Recipes.Count == 0
            && Plan.Runtime.Layouts.Count == 0;

        public static bool IsTerminalPhase(PlanPhase phase) =>
            phase == PlanPhase.Published
            || phase == PlanPhase.Rejected
            || phase == PlanPhase.Cancelled
            || phase == PlanPhase.Faulted;

        /// <summary>P-027's transition table, exactly; anything else is a refused transition.</summary>
        public static bool IsLegalTransition(PlanPhase from, PlanPhase to)
        {
            switch (from)
            {
                case PlanPhase.Draft:
                    return to == PlanPhase.Validated || to == PlanPhase.Rejected || to == PlanPhase.Cancelled;
                case PlanPhase.Validated:
                    return to == PlanPhase.Prepared || to == PlanPhase.Rejected || to == PlanPhase.Cancelled;
                case PlanPhase.Prepared:
                    return to == PlanPhase.Applying || to == PlanPhase.Rejected || to == PlanPhase.Cancelled;
                case PlanPhase.Applying:
                    return to == PlanPhase.Published || to == PlanPhase.Faulted;
                default:
                    return false;
            }
        }

        /// <summary>Draft -> Validated: catalog, graph, stratum, ownership, buffer and budget checks passed (P-028).</summary>
        public bool TryValidate(out DiagnosticCode code) => TryAdvance(PlanPhase.Validated, DiagnosticCode.None, "validated", out code);

        /// <summary>Validated -> Prepared: staged bindings, leases and bounded scratch exist (P-029).</summary>
        public bool TryPrepare(out DiagnosticCode code) => TryAdvance(PlanPhase.Prepared, DiagnosticCode.None, "prepared", out code);

        /// <summary>Prepared -> Applying: the cancellation cutoff has been crossed (P-051).</summary>
        public bool TryBeginApplying(out DiagnosticCode code) => TryAdvance(PlanPhase.Applying, DiagnosticCode.None, "applying", out code);

        /// <summary>
        /// Applying -> Published. Only the two published outcomes are legal, because a plan that reached the apply
        /// boundary has already produced an authoritative assembly (05 s4).
        /// </summary>
        public bool TryPublish(Outcome outcome, out DiagnosticCode code)
        {
            if (outcome != Outcome.Published && outcome != Outcome.PublishedWithCleanupErrors)
            {
                RefusedTransitionCount++;
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            return TryAdvance(
                PlanPhase.Published,
                outcome == Outcome.PublishedWithCleanupErrors ? DiagnosticCode.ResourceUnavailable : DiagnosticCode.None,
                outcome == Outcome.PublishedWithCleanupErrors ? "published with cleanup errors" : "published",
                out code,
                outcome);
        }

        /// <summary>
        /// Prepared -> Published with <see cref="Outcome.NoChange"/>: the plan produced no effective change, so
        /// nothing was written and no revision or epoch increments (P-006). `NoChange` is an outcome rather than a
        /// phase because the plan itself did reach a terminal, non-error state (05 s4).
        /// </summary>
        public bool TryNoChange(string detail)
        {
            if (Phase != PlanPhase.Prepared && Phase != PlanPhase.Validated)
            {
                RefusedTransitionCount++;
                return false;
            }

            var transition = new PlanTransition(Phase, PlanPhase.Published, DiagnosticCode.None, detail ?? string.Empty);
            Phase = PlanPhase.Published;
            Code = DiagnosticCode.None;
            Detail = transition.Reason;
            Outcome = Outcome.NoChange;
            history.Add(transition);
            return true;
        }

        /// <summary>Any nonterminal phase -> Rejected. Rejected means no live writes were made (05 s4).</summary>
        public bool TryReject(DiagnosticCode code, string detail) =>
            TryAdvance(PlanPhase.Rejected, code == DiagnosticCode.None ? DiagnosticCode.StalePlan : code, detail, out _);

        /// <summary>Any nonterminal phase -> Cancelled; only legal before Applying (P-051).</summary>
        public bool TryCancel(string detail) =>
            TryAdvance(PlanPhase.Cancelled, DiagnosticCode.Cancelled, detail, out _);

        /// <summary>
        /// Applying -> Faulted: a failure after the first live write. Admission stays closed and no epoch or
        /// snapshot publishes; a partially applied plan is never retried against the same storage (P-031).
        /// </summary>
        public bool TryFault(DiagnosticCode code, string detail) =>
            TryAdvance(PlanPhase.Faulted, code == DiagnosticCode.None ? DiagnosticCode.ApplyFault : code, detail, out _);

        /// <summary>
        /// P-028's recheck at the application boundary: the plan's expected revision and base epoch must still be
        /// the published ones. A stale plan rejects without mutation and the caller regenerates with a new
        /// operation id; this method never changes the phase by itself.
        /// </summary>
        public bool RecheckBase(CompositionRevision revision, AssemblyEpoch epoch, out DiagnosticCode code)
        {
            if (!Plan.BaseRevision.Equals(revision))
            {
                code = DiagnosticCode.StalePlan;
                return false;
            }

            if (!Plan.BaseEpoch.Equals(epoch))
            {
                code = DiagnosticCode.StalePlan;
                return false;
            }

            code = DiagnosticCode.None;
            return true;
        }

        /// <summary>Human-readable one-line state, used by diagnostics and tests (P-052).</summary>
        public string Describe() =>
            Phase.ToString()
            + (Code == DiagnosticCode.None ? string.Empty : "(" + DiagnosticCodeText.Of(Code) + ")")
            + " rev " + Plan.BaseRevision.Value.ToString(CultureInfo.InvariantCulture)
            + " epoch " + Plan.BaseEpoch.Value.ToString(CultureInfo.InvariantCulture)
            + " hash " + Plan.PlanHash.ToHex();

        private bool TryAdvance(
            PlanPhase to,
            DiagnosticCode code,
            string reason,
            out DiagnosticCode refusedCode,
            Outcome outcome = Outcome.Pending)
        {
            if (!IsLegalTransition(Phase, to))
            {
                RefusedTransitionCount++;
                refusedCode = DiagnosticCode.TooLate;
                return false;
            }

            PlanTransition transition = new PlanTransition(Phase, to, code, reason ?? string.Empty);
            Phase = to;
            Code = code;
            Detail = transition.Reason;
            Outcome = outcome;
            history.Add(transition);
            refusedCode = code;
            return true;
        }
    }
}
