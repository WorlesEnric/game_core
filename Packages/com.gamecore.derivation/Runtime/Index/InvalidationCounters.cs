// GameCore.Derivation — invalidation accounting (GC-013, P-022/P-023).
//
// 02 s9 requires each operation to report how much work an edit caused, and TEST-008 requires evidence that a
// local edit visits the invalidated membership/provider sets rather than the whole world. These counters are that
// evidence: they separate *detection* work (snapshot facts compared), *closure* work (scopes and targets the
// dependency closure enumerated through an index) and *derivation* work (candidates actually evaluated). A local
// edit shows a closure and an evaluation count bounded by the affected subtree; a world mode switch reports the
// whole world explicitly through `WholeWorld` and its reason keys.
//
// It extends `CostCounters` rather than duplicating it, so the P-022 budget accounting, the index-visit counters
// and the invalidation report travel as one object on one `DerivationResult` (GC-013).
//
// Every reason is a stable key, so a test can assert on the cause of a broad invalidation without parsing free
// text (P-052).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GameCore.Derivation
{
    /// <summary>The cause keys an invalidation reason can carry; stable identifiers, never free text (P-052).</summary>
    public static class InvalidationReasons
    {
        /// <summary>Nothing changed: no target is dirty.</summary>
        public const string None = "none";

        /// <summary>The world propagation mode changed, so every gated candidate is re-evaluated (P-013, P-014).</summary>
        public const string ModeChanged = "mode";

        /// <summary>A scope was created (P-010).</summary>
        public const string ScopeCreated = "scope-created";

        /// <summary>A scope was removed (P-010).</summary>
        public const string ScopeRemoved = "scope-removed";

        /// <summary>A scope subtree moved under another parent (P-025).</summary>
        public const string ScopeReparented = "scope-reparented";

        /// <summary>A scope's capability boundary, exclusions or imports changed (P-013, P-016).</summary>
        public const string ScopeFactsChanged = "scope-facts";

        /// <summary>An installation was created, removed, reconfigured, moved or changed lifecycle state (P-012).</summary>
        public const string InstallChanged = "install";

        /// <summary>A service consumer of a changed provider is part of the closure (P-012, 02 s7).</summary>
        public const string ServiceConsumer = "service-consumer";

        /// <summary>A target was created (P-024).</summary>
        public const string TargetCreated = "target-created";

        /// <summary>A target was retired (P-024).</summary>
        public const string TargetRetired = "target-retired";

        /// <summary>A target's owner scope changed (P-010, P-025).</summary>
        public const string TargetMoved = "target-moved";

        /// <summary>A target's descriptor changed, so its eligibility and reach must be re-tested (P-015).</summary>
        public const string DescriptorChanged = "descriptor";

        /// <summary>A provider selection override was added, removed or changed (P-018).</summary>
        public const string OverrideChanged = "override";

        /// <summary>A rule's declared ordering keys changed (P-019).</summary>
        public const string RuleKeysChanged = "rule-keys";

        /// <summary>The capability-contract catalog changed, so every composition is re-checked (P-017, P-019).</summary>
        public const string ContractsChanged = "contracts";

        /// <summary>The world incarnation changed; the previous result is no longer a valid base (P-004).</summary>
        public const string WorldChanged = "world";

        /// <summary>The previous result was not an accepted derivation, so no assembly could be carried (P-027).</summary>
        public const string NoAcceptedBase = "no-base";

        /// <summary>The previous result carried no explanations, so provenance could not be carried (P-026).</summary>
        public const string ExplanationsUnavailable = "explanations-unavailable";
    }

    /// <summary>What one invalidation cost and why. See the file header for the three kinds of work.</summary>
    public sealed class InvalidationCounters : CostCounters
    {
        private readonly List<string> reasons = new List<string>();

        /// <summary>Scope facts compared while detecting the change (identity-level work, no reach enumeration).</summary>
        public int ScopesCompared { get; internal set; }

        /// <summary>Installation facts compared while detecting the change.</summary>
        public int InstallsCompared { get; internal set; }

        /// <summary>Target identity/scope/descriptor facts compared while detecting the change.</summary>
        public int TargetsCompared { get; internal set; }

        /// <summary>Scopes the dependency closure walked through an index (bounded by the affected subtree).</summary>
        public int ScopesVisited { get; internal set; }

        /// <summary>Targets the dependency closure enumerated through an index (bounded by the affected subtree).</summary>
        public int TargetsVisited { get; internal set; }

        /// <summary>Rules whose candidate population the closure enumerated.</summary>
        public int RulesVisited { get; internal set; }

        /// <summary>Ancestor-chain steps walked (boundaries, imports, rank depth).</summary>
        public int AncestorChainSteps { get; internal set; }

        /// <summary>Reach containment tests answered from the membership index.</summary>
        public int ReachTests { get; internal set; }

        /// <summary>Scopes marked dirty.</summary>
        public int DirtyScopes { get; internal set; }

        /// <summary>Targets marked dirty: their candidates are re-evaluated.</summary>
        public int DirtyTargets { get; internal set; }

        /// <summary>Rules with at least one dirty candidate; the rules the incremental engine evaluates.</summary>
        public int DirtyRules { get; internal set; }

        /// <summary>Rules skipped because no dirty target was in their reach domain (the P-023 locality claim).</summary>
        public int SkippedRules { get; internal set; }

        /// <summary>Targets whose previous assembly is carried over unchanged (P-025 "unrelated targets").</summary>
        public int CarriedTargets { get; internal set; }

        /// <summary>Candidate evaluations performed by the incremental derivation.</summary>
        public int CandidateEvaluations { get; internal set; }

        /// <summary>Service consumer installations the closure pulled in (P-012).</summary>
        public int ServiceConsumersAffected { get; internal set; }

        /// <summary>True when the change legitimately invalidates the world, e.g. a mode switch (P-014).</summary>
        public bool WholeWorld { get; internal set; }

        /// <summary>Stable reason keys of this invalidation, sorted and duplicate-free.</summary>
        public IReadOnlyList<string> Reasons => reasons;

        internal void AddReason(string reason)
        {
            if (reason.Length == 0 || reasons.Contains(reason))
            {
                return;
            }

            reasons.Add(reason);
            reasons.Sort(StringComparer.Ordinal);
        }

        /// <summary>Canonical invalidation text; independent of the inherited P-022 cost text.</summary>
        public string DescribeInvalidation()
        {
            StringBuilder text = new StringBuilder();
            text.Append("reasons=");
            if (reasons.Count == 0)
            {
                text.Append(InvalidationReasons.None);
            }
            else
            {
                for (int i = 0; i < reasons.Count; i++)
                {
                    if (i > 0)
                    {
                        text.Append(',');
                    }

                    text.Append(reasons[i]);
                }
            }

            text.Append(";compared=").Append(ScopesCompared.ToString(CultureInfo.InvariantCulture))
                .Append('/').Append(InstallsCompared.ToString(CultureInfo.InvariantCulture))
                .Append('/').Append(TargetsCompared.ToString(CultureInfo.InvariantCulture))
                .Append(";closureScopes=").Append(ScopesVisited.ToString(CultureInfo.InvariantCulture))
                .Append(";closureTargets=").Append(TargetsVisited.ToString(CultureInfo.InvariantCulture))
                .Append(";closureRules=").Append(RulesVisited.ToString(CultureInfo.InvariantCulture))
                .Append(";ancestors=").Append(AncestorChainSteps.ToString(CultureInfo.InvariantCulture))
                .Append(";reachTests=").Append(ReachTests.ToString(CultureInfo.InvariantCulture))
                .Append(";dirtyScopes=").Append(DirtyScopes.ToString(CultureInfo.InvariantCulture))
                .Append(";dirtyTargets=").Append(DirtyTargets.ToString(CultureInfo.InvariantCulture))
                .Append(";dirtyRules=").Append(DirtyRules.ToString(CultureInfo.InvariantCulture))
                .Append(";skippedRules=").Append(SkippedRules.ToString(CultureInfo.InvariantCulture))
                .Append(";carried=").Append(CarriedTargets.ToString(CultureInfo.InvariantCulture))
                .Append(";evaluations=").Append(CandidateEvaluations.ToString(CultureInfo.InvariantCulture))
                .Append(";serviceConsumers=").Append(ServiceConsumersAffected.ToString(CultureInfo.InvariantCulture))
                .Append(";wholeWorld=").Append(WholeWorld ? "1" : "0");
            return text.ToString();
        }

        /// <summary>The P-022 cost text followed by the invalidation report.</summary>
        public override string Describe() => base.Describe() + ";" + DescribeInvalidation();

        public override string ToString() => "Invalidation(" + Describe() + ")";
    }
}
