// GameCore.Derivation — catalog and precondition validation of one derivation input (GC-006).
//
// P-028: before preparation, validate catalog/version compatibility, acyclic scope/service/stage graphs,
// eligibility/strata, conflicts, support/removal policies, generated factories, ... budgets and the expected
// revision. Validation is exhaustive enough to report related diagnostics but cannot promise later native
// operations succeed. P-021 supplies the stratum and bounded-output rules; P-019 supplies "mixed policies for the
// same contract/slot are catalog errors"; P-019/P-015 supply the generated reducer and predicate registrations.
//
// Every problem found here rejects the whole proposal; nothing partially derived is ever returned.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>One validation problem, structured for diagnostics and evidence (never a formatted string only).</summary>
    public sealed class DerivationValidationProblem
    {
        public DerivationValidationProblem(
            DiagnosticCode code,
            string subject,
            RuleId rule,
            PluginInstanceId provider,
            TargetId target,
            CapabilityId capability,
            FactoryKey key,
            int observed,
            int expected,
            string summary)
        {
            Code = code;
            Subject = subject ?? string.Empty;
            Rule = rule;
            Provider = provider;
            Target = target;
            Capability = capability;
            Key = key;
            Observed = observed;
            Expected = expected;
            Summary = summary ?? string.Empty;
        }

        public DiagnosticCode Code { get; }

        /// <summary>Stable machine-readable subject, e.g. `stratum`, `slot-policy`, `reducer`.</summary>
        public string Subject { get; }

        public RuleId Rule { get; }

        public PluginInstanceId Provider { get; }

        public TargetId Target { get; }

        public CapabilityId Capability { get; }

        public FactoryKey Key { get; }

        public int Observed { get; }

        public int Expected { get; }

        public string Summary { get; }

        public override string ToString() => Subject + ": " + Summary;
    }

    /// <summary>Result of validating one derivation input: a canonical, deterministic list of problems.</summary>
    public sealed class DerivationValidation
    {
        public DerivationValidation(IReadOnlyList<DerivationValidationProblem>? problems)
        {
            Problems = ContractCollections.Freeze(problems);
        }

        public IReadOnlyList<DerivationValidationProblem> Problems { get; }

        public bool IsValid => Problems.Count == 0;

        /// <summary>
        /// Validates one snapshot before any candidate is evaluated. <paramref name="expectedRevision"/> is the
        /// revision the caller believed published; a mismatch is the P-027/P-028 stale-plan rejection.
        /// </summary>
        public static DerivationValidation Validate(
            DerivationSnapshot snapshot,
            IDerivationValueSource values,
            CompositionRevision? expectedRevision,
            AssemblyEpoch? expectedEpoch)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            List<DerivationValidationProblem> problems = new List<DerivationValidationProblem>();

            if (expectedRevision.HasValue && !expectedRevision.Value.Equals(snapshot.Revision))
            {
                problems.Add(new DerivationValidationProblem(
                    DiagnosticCode.StalePlan,
                    "expected-revision",
                    default(RuleId),
                    default(PluginInstanceId),
                    default(TargetId),
                    default(CapabilityId),
                    default(FactoryKey),
                    (int)snapshot.Revision.Value,
                    (int)expectedRevision.Value.Value,
                    "The snapshot is at revision " + snapshot.Revision.Value + ", not the expected "
                    + expectedRevision.Value.Value + "; a stale plan rejects without mutation (P-028)."));
            }

            if (expectedEpoch.HasValue && !expectedEpoch.Value.Equals(snapshot.Epoch))
            {
                problems.Add(new DerivationValidationProblem(
                    DiagnosticCode.StalePlan,
                    "expected-epoch",
                    default(RuleId),
                    default(PluginInstanceId),
                    default(TargetId),
                    default(CapabilityId),
                    default(FactoryKey),
                    (int)snapshot.Epoch.Value,
                    (int)expectedEpoch.Value.Value,
                    "The snapshot is at assembly epoch " + snapshot.Epoch.Value + ", not the expected "
                    + expectedEpoch.Value.Value + " (P-006, P-028)."));
            }

            // A rule declared outside the 32 strata of P-021 is a catalog error, not a silently skipped rule.
            IReadOnlyList<RuleSource> outOfRange = snapshot.OutOfRangeRules;
            for (int i = 0; i < outOfRange.Count; i++)
            {
                DerivationRule rule = outOfRange[i].Rule;
                problems.Add(new DerivationValidationProblem(
                    DiagnosticCode.UnsupportedVersion,
                    "stratum",
                    rule.RuleId,
                    outOfRange[i].Install.Instance,
                    default(TargetId),
                    rule.OutputCapability.Capability,
                    default(FactoryKey),
                    rule.OutputStratum,
                    DerivationSnapshot.StratumCount - 1,
                    "A capability contract occupies strata 0..31 (P-021); this rule declares "
                    + rule.OutputStratum + "."));
            }

            IReadOnlyList<DerivationInstall> active = snapshot.ActiveInstalls;
            for (int i = 0; i < active.Count; i++)
            {
                DerivationInstall install = active[i];
                if (!snapshot.TryGetScope(install.Scope, out DerivationScope? installScope) || installScope == null)
                {
                    problems.Add(new DerivationValidationProblem(
                        DiagnosticCode.MissingDependency,
                        "install-scope",
                        default(RuleId),
                        install.Instance,
                        default(TargetId),
                        default(CapabilityId),
                        default(FactoryKey),
                        0,
                        0,
                        "An active installation must be installed at a scope of this snapshot (P-010)."));
                    continue;
                }

                IReadOnlyList<DerivationRule> rules = install.Manifest.DerivationRules;
                for (int r = 0; r < rules.Count; r++)
                {
                    ValidateRule(snapshot, values, install, rules[r], problems);
                }
            }

            // Target descriptors: an unknown asset adapter or a duplicate opt-in would make eligibility
            // ambiguous, so they are reported before any candidate runs (P-015, P-028).
            IReadOnlyList<DerivationTarget> targets = snapshot.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                DerivationTarget target = targets[i];
                if (!snapshot.TryGetScope(target.Scope, out DerivationScope? enclosing) || enclosing == null)
                {
                    problems.Add(new DerivationValidationProblem(
                        DiagnosticCode.MissingDependency,
                        "target-scope",
                        default(RuleId),
                        default(PluginInstanceId),
                        target.Target,
                        default(CapabilityId),
                        default(FactoryKey),
                        0,
                        0,
                        "A live target has exactly one owner scope inside this world (P-010)."));
                }
            }

            // P-018: an explicit `SelectProvider` override is legal only for `Replace` or `Exclusive` slots.
            // A versioned override for another policy would be silently ignored composition data, which the
            // protocol forbids, so it is reported here instead.
            IReadOnlyList<ProviderSelectionOverride> overrides = snapshot.Overrides;
            for (int i = 0; i < overrides.Count; i++)
            {
                ProviderSelectionOverride item = overrides[i];
                CapabilityContract? contract = snapshot.Contracts.Find(item.Capability);
                if (contract == null)
                {
                    problems.Add(new DerivationValidationProblem(
                        DiagnosticCode.MissingDependency,
                        "selection-contract",
                        default(RuleId),
                        default(PluginInstanceId),
                        default(TargetId),
                        item.Capability,
                        default(FactoryKey),
                        0,
                        0,
                        "A selection override names a capability with a declared contract (P-018)."));
                    continue;
                }

                for (int slot = 0; slot < contract.SlotPolicies.Count; slot++)
                {
                    CompositionPolicy policy = contract.SlotPolicies[slot].Policy;
                    if (policy == CompositionPolicy.Replace || policy == CompositionPolicy.Exclusive)
                    {
                        continue;
                    }

                    problems.Add(new DerivationValidationProblem(
                        DiagnosticCode.CapabilityConflict,
                        "selection-illegal-policy",
                        default(RuleId),
                        default(PluginInstanceId),
                        default(TargetId),
                        item.Capability,
                        default(FactoryKey),
                        (int)policy,
                        (int)CompositionPolicy.Replace,
                        "SelectProvider is legal only for Replace or Exclusive slots (P-018); this contract "
                        + "declares " + policy.ToString() + "."));
                }
            }

            problems.Sort(CompareProblems);
            return new DerivationValidation(problems);
        }

        private static void ValidateRule(
            DerivationSnapshot snapshot,
            IDerivationValueSource values,
            DerivationInstall install,
            DerivationRule rule,
            List<DerivationValidationProblem> problems)
        {
            string ruleName = "rule " + rule.RuleId.ToString() + " of " + install.Instance.ToString();

            if (rule.RuleId.IsDefault)
            {
                problems.Add(new DerivationValidationProblem(
                    DiagnosticCode.MissingDependency,
                    "rule-identity",
                    rule.RuleId,
                    install.Instance,
                    default(TargetId),
                    rule.OutputCapability.Capability,
                    default(FactoryKey),
                    0,
                    0,
                    "A derivation rule needs a stable rule identity (P-004): " + ruleName + "."));
                return;
            }

            if (rule.MaxOutputSlots == 0U)
            {
                problems.Add(new DerivationValidationProblem(
                    DiagnosticCode.MissingDependency,
                    "output-slots",
                    rule.RuleId,
                    install.Instance,
                    default(TargetId),
                    rule.OutputCapability.Capability,
                    default(FactoryKey),
                    0,
                    0,
                    "One rule emits at most the manifest's fixed bounded output slots per target; the bound must "
                    + "be positive (P-021): " + ruleName + "."));
            }

            CapabilityContract? contract = snapshot.Contracts.Find(rule.OutputCapability.Capability);
            if (contract == null)
            {
                problems.Add(new DerivationValidationProblem(
                    DiagnosticCode.MissingDependency,
                    "capability-contract",
                    rule.RuleId,
                    install.Instance,
                    default(TargetId),
                    rule.OutputCapability.Capability,
                    default(FactoryKey),
                    0,
                    0,
                    "A rule's output capability must have a declared contract carrying its slots, policies and "
                    + "stratum (P-017): " + ruleName + "."));
                return;
            }

            if (contract.Stratum != rule.OutputStratum)
            {
                problems.Add(new DerivationValidationProblem(
                    DiagnosticCode.UnsupportedVersion,
                    "stratum-agreement",
                    rule.RuleId,
                    install.Instance,
                    default(TargetId),
                    rule.OutputCapability.Capability,
                    default(FactoryKey),
                    rule.OutputStratum,
                    contract.Stratum,
                    "A rule's declared stratum must be its contract's stratum (P-021): " + ruleName + "."));
            }

            if (rule.OutputCapability.Version == 0U)
            {
                problems.Add(new DerivationValidationProblem(
                    DiagnosticCode.UnsupportedVersion,
                    "capability-version",
                    rule.RuleId,
                    install.Instance,
                    default(TargetId),
                    rule.OutputCapability.Capability,
                    default(FactoryKey),
                    0,
                    1,
                    "A capability contract version is a positive integer (P-004): " + ruleName + "."));
            }

            if (rule.MaxOutputSlots > (uint)contract.OutputSlots.Count)
            {
                problems.Add(new DerivationValidationProblem(
                    DiagnosticCode.MissingDependency,
                    "output-slot-bound",
                    rule.RuleId,
                    install.Instance,
                    default(TargetId),
                    rule.OutputCapability.Capability,
                    default(FactoryKey),
                    (int)rule.MaxOutputSlots,
                    contract.OutputSlots.Count,
                    "A rule cannot emit more slots than its contract declares (P-017, P-021): " + ruleName + "."));
            }

            if (contract.SlotPolicies.Count != contract.OutputSlots.Count)
            {
                problems.Add(new DerivationValidationProblem(
                    DiagnosticCode.MissingDependency,
                    "slot-policy-coverage",
                    rule.RuleId,
                    install.Instance,
                    default(TargetId),
                    rule.OutputCapability.Capability,
                    default(FactoryKey),
                    contract.SlotPolicies.Count,
                    contract.OutputSlots.Count,
                    "Every output slot chooses exactly one policy; a contract with a policy-less slot is a catalog "
                    + "error (P-019): " + ruleName + "."));
            }

            for (int slot = 0; slot < contract.OutputSlots.Count; slot++)
            {
                OutputSlotSchema schema = contract.OutputSlots[slot];
                SlotCompositionPolicy? policy = FindPolicy(contract, schema);
                if (policy == null)
                {
                    problems.Add(new DerivationValidationProblem(
                        DiagnosticCode.MissingDependency,
                        "slot-policy",
                        rule.RuleId,
                        install.Instance,
                        default(TargetId),
                        rule.OutputCapability.Capability,
                        default(FactoryKey),
                        slot,
                        0,
                        "Output slot " + slot + " of this contract declares no composition policy (P-019): "
                        + ruleName + "."));
                    continue;
                }

                if (policy.Policy != rule.Policy)
                {
                    problems.Add(new DerivationValidationProblem(
                        DiagnosticCode.CapabilityConflict,
                        "slot-policy-mismatch",
                        rule.RuleId,
                        install.Instance,
                        default(TargetId),
                        rule.OutputCapability.Capability,
                        default(FactoryKey),
                        (int)rule.Policy,
                        (int)policy.Policy,
                        "Mixed policies for the same contract/slot are catalog errors (P-019): " + ruleName
                        + " declares " + rule.Policy + " while the contract declares " + policy.Policy + "."));
                }
            }

            if (rule.Policy == CompositionPolicy.Additive)
            {
                // Either a registered reducer or the declared canonical set union; a reducer key that resolves to
                // nothing is reported now, not at the first emission (P-019, P-028).
                for (int slot = 0; slot < contract.SlotPolicies.Count; slot++)
                {
                    FactoryKey reducer = contract.SlotPolicies[slot].Reducer;
                    if (reducer.RegistrationKey.IsDefault || values.IsReductionRegistered(reducer))
                    {
                        continue;
                    }

                    problems.Add(new DerivationValidationProblem(
                        DiagnosticCode.MissingDependency,
                        "reducer",
                        rule.RuleId,
                        install.Instance,
                        default(TargetId),
                        rule.OutputCapability.Capability,
                        reducer,
                        0,
                        0,
                        "The declared Additive reducer of this contract is not registered; the kernel never "
                        + "infers numerical semantics (P-019): " + ruleName + "."));
                }
            }

            // P-021: a rule reads static descriptors and capabilities in strictly lower strata only.
            IReadOnlyList<CapabilityRef> inputs = rule.InputCapabilities;
            for (int i = 0; i < inputs.Count; i++)
            {
                CapabilityContract? inputContract = snapshot.Contracts.Find(inputs[i].Capability);
                if (inputContract == null)
                {
                    problems.Add(new DerivationValidationProblem(
                        DiagnosticCode.MissingDependency,
                        "input-contract",
                        rule.RuleId,
                        install.Instance,
                        default(TargetId),
                        inputs[i].Capability,
                        default(FactoryKey),
                        0,
                        0,
                        "A declared lower-stratum input must have a declared capability contract (P-021): "
                        + ruleName + "."));
                    continue;
                }

                if (inputContract.Stratum >= rule.OutputStratum)
                {
                    problems.Add(new DerivationValidationProblem(
                        DiagnosticCode.Cycle,
                        "stratum-order",
                        rule.RuleId,
                        install.Instance,
                        default(TargetId),
                        inputs[i].Capability,
                        default(FactoryKey),
                        inputContract.Stratum,
                        rule.OutputStratum - 1,
                        "A rule may read strictly lower strata only, so a same-stratum or higher-stratum read is "
                        + "a catalog error even if a sample converges (P-021): " + ruleName + "."));
                }
            }

            for (int i = 0; i < rule.InputCapabilities.Count; i++)
            {
                if (rule.InputCapabilities[i].Capability.Equals(rule.OutputCapability.Capability))
                {
                    problems.Add(new DerivationValidationProblem(
                        DiagnosticCode.Cycle,
                        "self-input",
                        rule.RuleId,
                        install.Instance,
                        default(TargetId),
                        rule.OutputCapability.Capability,
                        default(FactoryKey),
                        0,
                        0,
                        "A rule cannot derive its own output from its own absence or presence (P-021): " + ruleName + "."));
                    break;
                }
            }

            if (!rule.StaticPredicate.RegistrationKey.IsDefault && !values.IsPredicateRegistered(rule.StaticPredicate))
            {
                problems.Add(new DerivationValidationProblem(
                    DiagnosticCode.MissingDependency,
                    "predicate",
                    rule.RuleId,
                    install.Instance,
                    default(TargetId),
                    rule.OutputCapability.Capability,
                    rule.StaticPredicate,
                    0,
                    0,
                    "A declared static predicate key must be registered by a generated registration; a miss is "
                    + "reported and never substituted (P-009, P-028): " + ruleName + "."));
            }
        }

        private static SlotCompositionPolicy? FindPolicy(CapabilityContract contract, OutputSlotSchema schema)
        {
            IReadOnlyList<SlotCompositionPolicy> policies = contract.SlotPolicies;
            for (int i = 0; i < policies.Count; i++)
            {
                if (policies[i].Slot.Equals(schema.Slot))
                {
                    return policies[i];
                }
            }

            return null;
        }

        private static int CompareProblems(DerivationValidationProblem left, DerivationValidationProblem right)
        {
            int subject = string.CompareOrdinal(left.Subject, right.Subject);
            if (subject != 0)
            {
                return subject;
            }

            int rule = left.Rule.Value.CompareTo(right.Rule.Value);
            if (rule != 0)
            {
                return rule;
            }

            int provider = left.Provider.Value.CompareTo(right.Provider.Value);
            if (provider != 0)
            {
                return provider;
            }

            int capability = left.Capability.Value.CompareTo(right.Capability.Value);
            if (capability != 0)
            {
                return capability;
            }

            return string.CompareOrdinal(left.Summary, right.Summary);
        }
    }
}
