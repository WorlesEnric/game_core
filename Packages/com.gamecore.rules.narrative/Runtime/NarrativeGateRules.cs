// GameCore.Rules.Narrative — gate evaluation (07 section 3.1/3.3, P-032, REF-N05).
//
// `GateState { IsOpen, EvaluatedFactVersion }` is owned by the gate owner. The decision is a pure function of the
// ledger's fact value and version, which is what makes the reference's `RebindGate` migration expressible: the same
// function runs over fenced copies during preparation and over live state when the gate becomes interactive again.
//
// The gate never reads the ledger's slot buffer directly: the fact reaches it as a value plus a version, so an
// unbound gate renders its last decision (dormant, P-032) and a rebind re-evaluates the current facts before the
// gate is interactive again (REF-N05).
#nullable enable
using System.Globalization;

namespace GameCore.Rules.Narrative
{
    /// <summary>The gate domain's two decision values, as the integers its slots carry.</summary>
    public static class NarrativeGateRules
    {
        /// <summary>The condition does not hold: the gate stays closed.</summary>
        public const int Closed = 0;

        /// <summary>The condition holds: the gate is open.</summary>
        public const int Open = 1;

        /// <summary>True when the integer is a legal gate decision.</summary>
        public static bool IsDecision(int value) => value == Closed || value == Open;

        /// <summary>The gate decision one fact value implies; an out-of-domain fact value keeps the gate closed.</summary>
        public static int Evaluate(int conditionFactValue)
            => conditionFactValue == NarrativeFacts.True ? Open : Closed;

        /// <summary>
        /// Evaluates one condition against the ledger's committed fact value and version, reporting both the
        /// decision and the fact version it was taken from. A gate whose evaluated version lags the ledger is
        /// observably stale rather than silently current.
        /// </summary>
        public static bool TryEvaluate(
            int conditionFactValue,
            int factVersion,
            out int decision,
            out int evaluatedFactVersion,
            out string refusalCode)
        {
            decision = Closed;
            evaluatedFactVersion = 0;
            refusalCode = NarrativeRefusals.None;
            if (!NarrativeFacts.IsValidValue(conditionFactValue))
            {
                refusalCode = NarrativeRefusals.FactValueOutOfDomain;
                return false;
            }

            if (factVersion < NarrativeFacts.InitialVersion)
            {
                refusalCode = NarrativeRefusals.FactVersionBelowInitial;
                return false;
            }

            decision = Evaluate(conditionFactValue);
            evaluatedFactVersion = factVersion;
            return true;
        }

        /// <summary>The evaluation without the refusal code, for callers that already validated their inputs.</summary>
        public static bool TryEvaluate(
            int conditionFactValue,
            int factVersion,
            out int decision,
            out int evaluatedFactVersion)
            => TryEvaluate(conditionFactValue, factVersion, out decision, out evaluatedFactVersion, out string _);

        /// <summary>
        /// The registered `RebindGate` pure function: given a fenced copy of a gate's decision plus the current
        /// ledger fact, produce the gate's new decision and evaluated version. It is the same evaluation the live
        /// gate owner runs, so a rebind cannot invent a different policy (07 section 3.3).
        /// </summary>
        public static bool TryRebind(
            int currentDecision,
            int conditionFactValue,
            int factVersion,
            out int decision,
            out int evaluatedFactVersion,
            out string refusalCode)
        {
            decision = currentDecision;
            evaluatedFactVersion = 0;
            refusalCode = NarrativeRefusals.None;
            if (!IsDecision(currentDecision))
            {
                refusalCode = NarrativeRefusals.GateDecisionOutOfDomain;
                return false;
            }

            return TryEvaluate(conditionFactValue, factVersion, out decision, out evaluatedFactVersion, out refusalCode);
        }

        /// <summary>Canonical text form of one gate decision, for evidence only.</summary>
        public static string Describe(int decision, int evaluatedFactVersion)
            => (decision == Open ? "open" : "closed")
                + "@v" + evaluatedFactVersion.ToString(CultureInfo.InvariantCulture);
    }
}
