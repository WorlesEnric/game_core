// GameCore.Derivation — the indexed runtime derivation engine (GC-006).
//
// Algorithm (02 s4, P-018 to P-023): validate the input; walk strata 0..31 in order; for each stratum enumerate the
// indexed candidates of every active rule in canonical order; evaluate each candidate with the shared pure
// predicates; compose all (target, capability, outputSlot) groups; finalize that stratum's effective values
// before any higher stratum is evaluated; then diff against the previous result, check cross-capability
// incompatibility and return an immutable contribution delta.
//
// Rejection is total: a conflict, a same-stratum read or a quota overflow rejects the whole proposal and returns
// no partial assembly (GC-006 DoD, P-021, P-022, P-028). Every output carries provenance and bounded counters.
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>Optional inputs of one derivation: the revision to check and the host clock for the P-022 deadline.</summary>
    public sealed class DerivationOptions
    {
        public DerivationOptions(
            CompositionRevision? expectedRevision,
            AssemblyEpoch? expectedEpoch,
            PropagationBudget? budget,
            Func<long>? elapsedMilliseconds,
            bool collectExplanations)
        {
            ExpectedRevision = expectedRevision;
            ExpectedEpoch = expectedEpoch;
            Budget = budget ?? PropagationBudget.Reference;
            ElapsedMilliseconds = elapsedMilliseconds;
            CollectExplanations = collectExplanations;
        }

        public static DerivationOptions Default { get; } =
            new DerivationOptions(null, null, PropagationBudget.Reference, null, true);

        public CompositionRevision? ExpectedRevision { get; }

        public AssemblyEpoch? ExpectedEpoch { get; }

        public PropagationBudget Budget { get; }

        /// <summary>Host-supplied elapsed preparation time; null means the deadline is not enforced in this run.</summary>
        public Func<long>? ElapsedMilliseconds { get; }

        /// <summary>
        /// False builds the assemblies and the delta without the per-(target, capability) explanation records,
        /// which is the useful mode for a large repeated sweep. The candidate decision set is always computed.
        /// </summary>
        public bool CollectExplanations { get; }
    }

    /// <summary>Why a derivation was rejected. Every rejection is total: no partial closure is ever published.</summary>
    public enum DerivationRejectionKind
    {
        None = 0,

        /// <summary>The catalog or precondition validation failed (P-028, P-021, P-019).</summary>
        ValidationFailed = 1,

        /// <summary>A hard P-022 limit was exceeded.</summary>
        BudgetExceeded = 2,

        /// <summary>A slot composition conflict: exclusive/incompatible members or an ambiguous order (P-018, P-019).</summary>
        CompositionConflict = 3,

        /// <summary>Two mutually incompatible capabilities would be active on one target (P-019).</summary>
        IncompatibleCapabilities = 4,
    }

    /// <summary>
    /// Immutable derivation output: the resulting assemblies, complete provenance, bounded counters and the delta
    /// against the previous result. A rejected result carries diagnostics and no assembly at all.
    /// </summary>
    public sealed class DerivationResult
    {
        private DerivationResult(
            bool accepted,
            DerivationRejectionKind rejection,
            DerivationSnapshot snapshot,
            IReadOnlyList<TargetAssembly>? assemblies,
            IReadOnlyList<CapabilityContribution>? contributions,
            IReadOnlyList<CandidateDecision>? decisions,
            IReadOnlyList<DerivationExplanation>? explanations,
            IReadOnlyList<DerivationValidationProblem>? validationProblems,
            IReadOnlyList<CompositionFailure>? compositionFailures,
            CostCounters counters,
            DerivationDelta? delta,
            ContentHash resultHash)
        {
            Accepted = accepted;
            Rejection = rejection;
            Snapshot = snapshot;
            Assemblies = ContractCollections.Freeze(assemblies);
            Contributions = ContractCollections.Freeze(contributions);
            Decisions = ContractCollections.Freeze(decisions);
            Explanations = ContractCollections.Freeze(explanations);
            ValidationProblems = ContractCollections.Freeze(validationProblems);
            CompositionFailures = ContractCollections.Freeze(compositionFailures);
            Counters = counters;
            Delta = delta;
            ResultHash = resultHash;
        }

        public bool Accepted { get; }

        public DerivationRejectionKind Rejection { get; }

        public DerivationSnapshot Snapshot { get; }

        /// <summary>Effective assembly per target, canonical target order; empty for a rejected result.</summary>
        public IReadOnlyList<TargetAssembly> Assemblies { get; }

        /// <summary>Every active contribution in canonical key order (P-017).</summary>
        public IReadOnlyList<CapabilityContribution> Contributions { get; }

        /// <summary>Every candidate evaluation, including excluded, shadowed and losing candidates (P-026).</summary>
        public IReadOnlyList<CandidateDecision> Decisions { get; }

        /// <summary>Interned per (target, capability) explanations; empty when options suppressed them.</summary>
        public IReadOnlyList<DerivationExplanation> Explanations { get; }

        public IReadOnlyList<DerivationValidationProblem> ValidationProblems { get; }

        public IReadOnlyList<CompositionFailure> CompositionFailures { get; }

        public CostCounters Counters { get; }

        /// <summary>Delta against the previous published derivation; null when none was supplied.</summary>
        public DerivationDelta? Delta { get; }

        /// <summary>Canonical hash of the accepted result; <see cref="ContentHash.Empty"/> when rejected.</summary>
        public ContentHash ResultHash { get; }

        /// <summary>The stable code a caller reports for this result: `None` when it was accepted (00 s9).</summary>
        public DiagnosticCode DiagnosticCode
        {
            get
            {
                switch (Rejection)
                {
                    case DerivationRejectionKind.None:
                        return DiagnosticCode.None;
                    case DerivationRejectionKind.BudgetExceeded:
                        return DiagnosticCode.BudgetExceeded;
                    case DerivationRejectionKind.CompositionConflict:
                        return CompositionFailures.Count > 0
                            ? CompositionFailures[0].Code
                            : DiagnosticCode.CapabilityConflict;
                    case DerivationRejectionKind.IncompatibleCapabilities:
                        return DiagnosticCode.CapabilityConflict;
                    default:
                        return ValidationProblems.Count > 0
                            ? ValidationProblems[0].Code
                            : DiagnosticCode.MissingDependency;
                }
            }
        }

        /// <summary>Target assembly of one target, or null when it has none in an accepted result.</summary>
        public TargetAssembly? AssemblyOf(TargetId target)
        {
            for (int i = 0; i < Assemblies.Count; i++)
            {
                if (Assemblies[i].Target.Equals(target))
                {
                    return Assemblies[i];
                }
            }

            return null;
        }

        /// <summary>Explanation of one (target, capability) pair, or null when the pair has none.</summary>
        public DerivationExplanation? ExplanationOf(TargetId target, CapabilityId capability)
        {
            for (int i = 0; i < Explanations.Count; i++)
            {
                if (Explanations[i].Target.Equals(target) && Explanations[i].Capability.Equals(capability))
                {
                    return Explanations[i];
                }
            }

            return null;
        }

        /// <summary>Every decision recorded for one (target, capability) pair, in canonical order.</summary>
        public IReadOnlyList<CandidateDecision> DecisionsOf(TargetId target, CapabilityId capability)
        {
            List<CandidateDecision> found = new List<CandidateDecision>();
            for (int i = 0; i < Decisions.Count; i++)
            {
                if (Decisions[i].Target.Equals(target) && Decisions[i].Capability.Equals(capability))
                {
                    found.Add(Decisions[i]);
                }
            }

            return found;
        }

        internal static DerivationResult Reject(
            DerivationRejectionKind kind,
            DerivationSnapshot snapshot,
            IReadOnlyList<DerivationValidationProblem>? validationProblems,
            IReadOnlyList<CompositionFailure>? compositionFailures,
            CostCounters counters) =>
            new DerivationResult(
                false,
                kind,
                snapshot,
                null,
                null,
                null,
                null,
                validationProblems,
                compositionFailures,
                counters,
                null,
                ContentHash.Empty);

        internal static DerivationResult Accept(
            DerivationSnapshot snapshot,
            IReadOnlyList<TargetAssembly> assemblies,
            IReadOnlyList<CapabilityContribution> contributions,
            IReadOnlyList<CandidateDecision> decisions,
            IReadOnlyList<DerivationExplanation> explanations,
            CostCounters counters,
            DerivationDelta? delta,
            ContentHash resultHash) =>
            new DerivationResult(
                true,
                DerivationRejectionKind.None,
                snapshot,
                assemblies,
                contributions,
                decisions,
                explanations,
                null,
                null,
                counters,
                delta,
                resultHash);
    }

    /// <summary>The indexed derivation engine (P-023). It never reads live world state.</summary>
    public static class DerivationEngine
    {
        /// <summary>Cost of touching one target's assembly at the fenced apply boundary (P-022 estimate unit).</summary>
        public const long TargetApplyCostMicroseconds = 2L;

        /// <summary>Cost of writing one effective slot at the fenced apply boundary (P-022 estimate unit).</summary>
        public const long SlotApplyCostMicroseconds = 3L;

        /// <summary>Cost of one state-migration step at the fenced apply boundary (P-022 estimate unit).</summary>
        public const long MigrationCostMicroseconds = 5L;

        /// <summary>How many top fan-out causes a `BudgetExceeded` diagnostic retains (P-022).</summary>
        public const int MaxFanOutCauses = 8;

        /// <summary>How many raw visit identities a `BudgetExceeded` diagnostic samples before ranking (P-022).</summary>
        public const int FanOutCauseSampleLimit = 4096;

        /// <summary>
        /// Derives the effective assembly of one snapshot. <paramref name="previous"/> is the last published result
        /// of the same world and is used only to compute the delta; it never influences effective values.
        /// </summary>
        public static DerivationResult Derive(
            DerivationSnapshot snapshot,
            IDerivationValueSource values,
            DerivationOptions? options,
            DerivationResult? previous)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            DerivationOptions effective = options ?? DerivationOptions.Default;
            CostCounters counters = new CostCounters();

            DerivationValidation validation = DerivationValidation.Validate(
                snapshot, values, effective.ExpectedRevision, effective.ExpectedEpoch);
            if (!validation.IsValid)
            {
                counters.WithinBudget = false;
                return DerivationResult.Reject(
                    DerivationRejectionKind.ValidationFailed, snapshot, validation.Problems, null, counters);
            }

            List<CandidateDecision> decisions = new List<CandidateDecision>();
            List<CompositionFailure> failures = new List<CompositionFailure>();
            Dictionary<Id128, DerivationTarget> targetsById = new Dictionary<Id128, DerivationTarget>();
            IReadOnlyList<DerivationTarget> allTargets = snapshot.Targets;
            for (int i = 0; i < allTargets.Count; i++)
            {
                targetsById.Add(allTargets[i].Target.Value, allTargets[i]);
            }

            CapabilityLedger ledger = new CapabilityLedger();
            Dictionary<SlotGroupKey, EffectiveSlot> accepted = new Dictionary<SlotGroupKey, EffectiveSlot>();
            Dictionary<Id128, List<EffectiveSlot>> slotsByTarget = new Dictionary<Id128, List<EffectiveSlot>>();
            Dictionary<Id128, List<CapabilityContribution>> contributionsByTarget =
                new Dictionary<Id128, List<CapabilityContribution>>();
            Dictionary<PairKey, List<ContributionKey>> evaluatedByPair = new Dictionary<PairKey, List<ContributionKey>>();
            List<Id128> fanOutCauses = new List<Id128>();

            for (int stratum = 0; stratum < DerivationSnapshot.StratumCount; stratum++)
            {
                IReadOnlyList<RuleSource> rules = snapshot.RulesAtStratum(stratum);
                if (rules.Count == 0)
                {
                    continue;
                }

                Dictionary<SlotGroupKey, List<RankedCandidate>> groups =
                    new Dictionary<SlotGroupKey, List<RankedCandidate>>();

                for (int r = 0; r < rules.Count; r++)
                {
                    RuleSource source = rules[r];
                    DerivationRule rule = source.Rule;
                    counters.RulesEvaluated++;
                    // The candidate population is the reach domain intersected with the selector-schema /
                    // output-capability index (P-023): the rule never scans unrelated targets.
                    IReadOnlyList<DerivationTarget> candidates = snapshot.TargetsInReach(
                        source.Install.Scope, rule.Reach, rule.SelectorContracts, rule.OutputCapability.Capability);
                    counters.IndexBucketsVisited++;
                    counters.IndexTargetsVisited += candidates.Count;

                    for (int c = 0; c < candidates.Count; c++)
                    {
                        DerivationTarget target = candidates[c];
                        counters.ExaminedCandidates++;
                        if (fanOutCauses.Count < FanOutCauseSampleLimit)
                        {
                            // A bounded sample: the diagnostic keeps the top causes of the visit stream without
                            // retaining one entry per examined candidate (P-022).
                            fanOutCauses.Add(source.Install.Instance.Value);
                            fanOutCauses.Add(target.Target.Value);
                        }

                        BudgetDimension dimension;
                        long observed;
                        long limit;
                        if (!WithinBudget(counters, effective.Budget, out dimension, out observed, out limit))
                        {
                            MarkExceeded(counters, dimension, observed, limit, TopFanOutCauses(fanOutCauses));
                            return DerivationResult.Reject(
                                DerivationRejectionKind.BudgetExceeded, snapshot, null, null, counters);
                        }

                        CandidateEvaluation evaluation = DerivationPolicy.Evaluate(
                            snapshot, values, source.Install, rule, target, ledger);

                        PairKey pairKey = new PairKey(target.Target, rule.OutputCapability.Capability);
                        if (!evaluatedByPair.TryGetValue(pairKey, out List<ContributionKey>? evaluated))
                        {
                            evaluated = new List<ContributionKey>();
                            evaluatedByPair.Add(pairKey, evaluated);
                        }

                        uint declaredSlots = (uint)SlotsOf(snapshot, rule);
                        uint boundedSlots = rule.MaxOutputSlots < declaredSlots ? rule.MaxOutputSlots : declaredSlots;

                        if (evaluation.Status != CandidateStatus.Emitted)
                        {
                            decisions.Add(Decision(source, rule, target, 0U, evaluation, snapshot, stratum));
                            continue;
                        }

                        for (uint slot = 0; slot < boundedSlots; slot++)
                        {
                            ContributionKey key = new ContributionKey(
                                new ProviderInstallationId(source.Install.Instance.Value),
                                rule.RuleId,
                                target.Target,
                                rule.OutputCapability.Capability,
                                slot);
                            evaluated.Add(key);

                            decisions.Add(Decision(source, rule, target, slot, evaluation, snapshot, stratum));

                            CapabilityContract? outputContract = snapshot.Contracts.Find(rule.OutputCapability.Capability);
                            OutputSlotSchema? schema;
                            SlotCompositionPolicy? policy;
                            if (outputContract == null
                                || !snapshot.Contracts.TryGetSlot(outputContract, slot, out schema, out policy)
                                || schema == null
                                || policy == null)
                            {
                                // Validation already rejected an unbounded or policy-less slot; this guard keeps a
                                // malformed contract from producing a partial closure.
                                failures.Add(new CompositionFailure(
                                    DiagnosticCode.MissingDependency,
                                    target.Target,
                                    rule.OutputCapability.Capability,
                                    slot,
                                    new[] { key },
                                    new[] { rule.RuleId.Value },
                                    "The rule's declared output slot has no contract schema/policy (P-017, P-019)."));
                                return DerivationResult.Reject(
                                    DerivationRejectionKind.CompositionConflict, snapshot, null, failures, counters);
                            }

                            CapabilityContribution contribution = new CapabilityContribution(
                                key,
                                schema.Schema,
                                policy.Policy,
                                rule.PayloadDefinition,
                                PayloadCodec.HashOf(rule.PayloadDefinition),
                                ContributionDisposition.Active);

                            SlotGroupKey groupKey = new SlotGroupKey(
                                target.Target, rule.OutputCapability.Capability, slot);
                            if (!groups.TryGetValue(groupKey, out List<RankedCandidate>? list))
                            {
                                list = new List<RankedCandidate>();
                                groups.Add(groupKey, list);
                            }

                            list.Add(new RankedCandidate(
                                contribution,
                                source.Install.Record.Priority,
                                rule.Priority,
                                snapshot.Depth(source.Install.Scope),
                                source.Install.Scope));
                        }
                    }
                }

                if (groups.Count == 0)
                {
                    continue;
                }

                List<SlotGroupKey> orderedGroups = new List<SlotGroupKey>(groups.Keys);
                orderedGroups.Sort(SlotGroupKey.Compare);

                for (int g = 0; g < orderedGroups.Count; g++)
                {
                    SlotGroupKey groupKey = orderedGroups[g];
                    List<RankedCandidate> eligible = groups[groupKey];
                    eligible.Sort(RankedCandidate.Compare);

                    DerivationTarget groupTarget = targetsById[groupKey.Target.Value];
                    CapabilityContract contract = snapshot.Contracts.Find(groupKey.Capability)!;
                    OutputSlotSchema? schema;
                    SlotCompositionPolicy? policy;
                    snapshot.Contracts.TryGetSlot(contract, groupKey.Slot, out schema, out policy);

                    IReadOnlyList<ContributionKey> evaluated = evaluatedByPair.TryGetValue(
                        new PairKey(groupKey.Target, groupKey.Capability), out List<ContributionKey>? list)
                        ? list
                        : (IReadOnlyList<ContributionKey>)Array.Empty<ContributionKey>();

                    EffectiveSlot? composed;
                    CompositionFailure? failure;
                    if (!SlotComposer.TryCompose(
                            snapshot,
                            values,
                            groupKey.Target,
                            groupTarget.Scope,
                            contract.Capability,
                            contract.Stratum,
                            groupKey.Slot,
                            schema!,
                            policy!,
                            eligible,
                            snapshot.Overrides,
                            evaluated,
                            out composed,
                            out failure))
                    {
                        if (failure != null)
                        {
                            failures.Add(failure);
                        }

                        return DerivationResult.Reject(
                            DerivationRejectionKind.CompositionConflict, snapshot, null, failures, counters);
                    }

                    if (composed == null)
                    {
                        continue;
                    }

                    counters.EmittedContributions += composed.Support.Count + composed.Shadowed.Count;
                    counters.ShadowedCandidates += composed.Shadowed.Count;

                    BudgetDimension dimension;
                    long observed;
                    long limit;
                    if (!WithinBudget(counters, effective.Budget, out dimension, out observed, out limit))
                    {
                        MarkExceeded(counters, dimension, observed, limit, TopFanOutCauses(fanOutCauses));
                        return DerivationResult.Reject(
                            DerivationRejectionKind.BudgetExceeded, snapshot, null, null, counters);
                    }

                    accepted[groupKey] = composed;
                    if (!slotsByTarget.TryGetValue(groupKey.Target.Value, out List<EffectiveSlot>? targetSlots))
                    {
                        targetSlots = new List<EffectiveSlot>();
                        slotsByTarget.Add(groupKey.Target.Value, targetSlots);
                    }

                    targetSlots.Add(composed);

                    if (!contributionsByTarget.TryGetValue(groupKey.Target.Value, out List<CapabilityContribution>? targetContributions))
                    {
                        targetContributions = new List<CapabilityContribution>();
                        contributionsByTarget.Add(groupKey.Target.Value, targetContributions);
                    }

                    for (int i = 0; i < composed.Support.Count; i++)
                    {
                        targetContributions.Add(composed.Support[i]);
                    }

                    for (int i = 0; i < composed.Shadowed.Count; i++)
                    {
                        targetContributions.Add(composed.Shadowed[i]);
                    }
                }

                // This stratum is final before any higher stratum is evaluated (P-021): composition within a
                // stratum can never feed eligibility in that same stratum, and a rule can never read its own.
                for (int i = 0; i < orderedGroups.Count; i++)
                {
                    SlotGroupKey groupKey = orderedGroups[i];
                    if (accepted.ContainsKey(groupKey))
                    {
                        ledger.Finalize(groupKey.Target, groupKey.Capability);
                    }
                }
            }

            if (!TryCheckDeadline(snapshot, effective, counters, out DerivationResult? deadlineRejection))
            {
                return deadlineRejection!;
            }

            // Cross-capability incompatibility of P-019 over each target's effective capability set.
            List<Id128> effectiveTargetIds = new List<Id128>(slotsByTarget.Keys);
            effectiveTargetIds.Sort(Id128Codec.CompareBigEndian);
            foreach (Id128 targetId in effectiveTargetIds)
            {
                List<CapabilityId> effectiveCapabilities = EffectiveCapabilitiesOf(slotsByTarget[targetId]);
                CompositionFailure? conflict;
                if (!SlotComposer.TryCheckIncompatibility(
                        snapshot, new TargetId(targetId), effectiveCapabilities, out conflict))
                {
                    if (conflict != null)
                    {
                        failures.Add(conflict);
                    }

                    return DerivationResult.Reject(
                        DerivationRejectionKind.IncompatibleCapabilities, snapshot, null, failures, counters);
                }
            }

            // Assemble every target: a target nothing derived for keeps exactly its base recipe (P-015).
            List<TargetAssembly> assemblies = new List<TargetAssembly>(snapshot.Targets.Count);
            List<CapabilityContribution> allContributions = new List<CapabilityContribution>();
            for (int i = 0; i < snapshot.Targets.Count; i++)
            {
                DerivationTarget target = snapshot.Targets[i];
                List<EffectiveSlot> slots = slotsByTarget.TryGetValue(target.Target.Value, out List<EffectiveSlot>? foundSlots)
                    ? foundSlots
                    : new List<EffectiveSlot>();
                slots.Sort(CompareSlots);

                List<CapabilityContribution> contributions = contributionsByTarget.TryGetValue(
                    target.Target.Value, out List<CapabilityContribution>? foundContributions)
                    ? foundContributions
                    : new List<CapabilityContribution>();
                contributions.Sort(CompareContributions);
                allContributions.AddRange(contributions);

                List<CapabilityId> effectiveCapabilities = EffectiveCapabilitiesOf(slots);
                assemblies.Add(new TargetAssembly(
                    target.Target,
                    target.Scope,
                    target.Descriptor.Recipe,
                    slots,
                    contributions,
                    effectiveCapabilities,
                    AssemblyHash.Compute(target, slots)));
            }

            assemblies.Sort(CompareAssemblies);
            allContributions.Sort(CompareContributions);

            // Losers are recorded as shadowed after composition, so provenance never claims a losing candidate
            // supplied the effective value (P-017, P-019).
            ReclassifyShadowed(decisions, accepted);

            DerivationDelta? delta = previous == null
                ? null
                : DerivationDeltaBuilder.Build(previous, assemblies, allContributions);
            counters.AffectedTargets = delta == null ? assemblies.Count : delta.AffectedTargets.Count;

            long applyEstimate = EstimateApplyCostMicroseconds(assemblies, delta);
            if (applyEstimate > effective.Budget.MaxApplyCostEstimateMicroseconds)
            {
                MarkExceeded(
                    counters,
                    BudgetDimension.ApplyCostEstimate,
                    applyEstimate,
                    effective.Budget.MaxApplyCostEstimateMicroseconds,
                    TopFanOutCauses(fanOutCauses));
                return DerivationResult.Reject(
                    DerivationRejectionKind.BudgetExceeded, snapshot, null, null, counters);
            }

            List<DerivationExplanation> explanations = effective.CollectExplanations
                ? BuildExplanations(snapshot, assemblies, decisions, accepted, slotsByTarget)
                : new List<DerivationExplanation>();

            decisions.Sort(CompareDecisions);
            ContentHash resultHash = AssemblyHash.ComputeResult(snapshot, assemblies);
            return DerivationResult.Accept(
                snapshot,
                assemblies,
                allContributions,
                decisions,
                explanations,
                counters,
                delta,
                resultHash);
        }

        /// <summary>Estimated safe-point apply cost in microseconds: the provisional P-022 guardrail metric.</summary>
        public static long EstimateApplyCostMicroseconds(
            IReadOnlyList<TargetAssembly> assemblies,
            DerivationDelta? delta)
        {
            long estimate = 0L;
            for (int i = 0; i < assemblies.Count; i++)
            {
                estimate += TargetApplyCostMicroseconds;
                estimate += assemblies[i].Slots.Count * SlotApplyCostMicroseconds;
            }

            if (delta != null)
            {
                estimate += (delta.Added.Count + delta.Removed.Count + delta.Changed.Count) * MigrationCostMicroseconds;
            }

            return estimate;
        }

        /// <summary>Effective capability set of a composed slot list, ascending and duplicate-free (P-017).</summary>
        public static List<CapabilityId> EffectiveCapabilitiesOf(IReadOnlyList<EffectiveSlot> slots)
        {
            List<CapabilityId> capabilities = new List<CapabilityId>(slots.Count);
            HashSet<Id128> seen = new HashSet<Id128>();
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].IsEmpty)
                {
                    continue;
                }

                if (seen.Add(slots[i].Capability.Value))
                {
                    capabilities.Add(slots[i].Capability);
                }
            }

            capabilities.Sort(CompareCapabilityIds);
            return capabilities;
        }

        private static bool TryCheckDeadline(
            DerivationSnapshot snapshot,
            DerivationOptions effective,
            CostCounters counters,
            out DerivationResult? rejection)
        {
            rejection = null;
            if (effective.ElapsedMilliseconds == null)
            {
                return true;
            }

            long elapsed = effective.ElapsedMilliseconds();
            if (elapsed <= effective.Budget.PreparationDeadlineMilliseconds)
            {
                return true;
            }

            MarkExceeded(
                counters,
                BudgetDimension.PreparationDeadline,
                elapsed,
                effective.Budget.PreparationDeadlineMilliseconds,
                null);
            rejection = DerivationResult.Reject(
                DerivationRejectionKind.BudgetExceeded, snapshot, null, null, counters);
            return false;
        }

        private static void ReclassifyShadowed(
            List<CandidateDecision> decisions,
            Dictionary<SlotGroupKey, EffectiveSlot> accepted)
        {
            HashSet<ContributionKey> active = new HashSet<ContributionKey>();
            foreach (KeyValuePair<SlotGroupKey, EffectiveSlot> pair in accepted)
            {
                for (int i = 0; i < pair.Value.Support.Count; i++)
                {
                    active.Add(pair.Value.Support[i].Key);
                }
            }

            for (int i = 0; i < decisions.Count; i++)
            {
                CandidateDecision decision = decisions[i];
                if (decision.Status != CandidateStatus.Emitted)
                {
                    continue;
                }

                ContributionKey key = new ContributionKey(
                    decision.Provider,
                    decision.Rule,
                    decision.Target,
                    decision.Capability,
                    decision.OutputSlot);
                if (!active.Contains(key))
                {
                    decisions[i] = decision.WithStatus(CandidateStatus.Shadowed);
                }
            }
        }

        private static int SlotsOf(DerivationSnapshot snapshot, DerivationRule rule)
        {
            CapabilityContract? contract = snapshot.Contracts.Find(rule.OutputCapability.Capability);
            return contract == null ? 0 : contract.OutputSlots.Count;
        }

        private static bool WithinBudget(
            CostCounters counters,
            PropagationBudget budget,
            out BudgetDimension dimension,
            out long observed,
            out long limit)
        {
            counters.TemporaryBytes = (counters.ExaminedCandidates * PropagationBudget.CandidateByteCost)
                + (counters.EmittedContributions * PropagationBudget.ContributionByteCost);

            if (counters.ExaminedCandidates > budget.MaxExaminedCandidates)
            {
                dimension = BudgetDimension.ExaminedCandidates;
                observed = counters.ExaminedCandidates;
                limit = budget.MaxExaminedCandidates;
                return false;
            }

            if (counters.EmittedContributions > budget.MaxEmittedContributions)
            {
                dimension = BudgetDimension.EmittedContributions;
                observed = counters.EmittedContributions;
                limit = budget.MaxEmittedContributions;
                return false;
            }

            if (counters.TemporaryBytes > budget.MaxTemporaryBytes)
            {
                dimension = BudgetDimension.TemporaryBytes;
                observed = counters.TemporaryBytes;
                limit = budget.MaxTemporaryBytes;
                return false;
            }

            dimension = BudgetDimension.None;
            observed = 0L;
            limit = 0L;
            return true;
        }

        private static void MarkExceeded(
            CostCounters counters,
            BudgetDimension dimension,
            long observed,
            long limit,
            IReadOnlyList<Id128>? causes)
        {
            counters.WithinBudget = false;
            counters.ExceededDimension = dimension;
            counters.ExceededCount = observed;
            counters.ExceededLimit = limit;
            counters.TopFanOutCauses = causes ?? Array.Empty<Id128>();
        }

        private static IReadOnlyList<Id128> TopFanOutCauses(List<Id128> causes)
        {
            if (causes.Count == 0)
            {
                return Array.Empty<Id128>();
            }

            List<Id128> sorted = new List<Id128>(causes);
            sorted.Sort(Id128Codec.CompareBigEndian);

            List<Id128> unique = new List<Id128>(MaxFanOutCauses);
            for (int i = 0; i < sorted.Count && unique.Count < MaxFanOutCauses; i++)
            {
                if (unique.Count == 0 || !unique[unique.Count - 1].Equals(sorted[i]))
                {
                    unique.Add(sorted[i]);
                }
            }

            return unique.AsReadOnly();
        }

        private static CandidateDecision Decision(
            RuleSource source,
            DerivationRule rule,
            DerivationTarget target,
            uint slot,
            CandidateEvaluation evaluation,
            DerivationSnapshot snapshot,
            int stratum) =>
            new CandidateDecision(
                EvidenceKeys.Derive(EvidenceKeys.CandidateKey(
                    new ProviderInstallationId(source.Install.Instance.Value), rule.RuleId, target.Target, rule.OutputCapability.Capability, slot)),
                new ProviderInstallationId(source.Install.Instance.Value),
                rule.RuleId,
                target.Target,
                rule.OutputCapability.Capability,
                slot,
                source.Install.Scope,
                snapshot.Depth(source.Install.Scope),
                stratum,
                evaluation.Status,
                evaluation.Reason,
                evaluation.Diagnostic,
                evaluation.ModeGate,
                source.Install.Record.Priority,
                rule.Priority,
                evaluation.Exclusions,
                evaluation.Boundaries,
                evaluation.MissingInputs,
                evaluation.EvidenceKeys);

        private static List<DerivationExplanation> BuildExplanations(
            DerivationSnapshot snapshot,
            IReadOnlyList<TargetAssembly> assemblies,
            IReadOnlyList<CandidateDecision> decisions,
            Dictionary<SlotGroupKey, EffectiveSlot> accepted,
            Dictionary<Id128, List<EffectiveSlot>> slotsByTarget)
        {
            List<DerivationExplanation> explanations = new List<DerivationExplanation>();
            for (int a = 0; a < assemblies.Count; a++)
            {
                TargetAssembly assembly = assemblies[a];
                if (!snapshot.TryGetTarget(assembly.Target, out DerivationTarget? target) || target == null)
                {
                    continue;
                }

                List<CapabilityId> pairs = new List<CapabilityId>();
                HashSet<Id128> seen = new HashSet<Id128>();
                for (int d = 0; d < decisions.Count; d++)
                {
                    if (decisions[d].Target.Equals(assembly.Target) && seen.Add(decisions[d].Capability.Value))
                    {
                        pairs.Add(decisions[d].Capability);
                    }
                }

                if (slotsByTarget.TryGetValue(assembly.Target.Value, out List<EffectiveSlot>? targetSlots))
                {
                    for (int s = 0; s < targetSlots.Count; s++)
                    {
                        if (seen.Add(targetSlots[s].Capability.Value))
                        {
                            pairs.Add(targetSlots[s].Capability);
                        }
                    }
                }

                pairs.Sort(CompareCapabilityIds);

                for (int p = 0; p < pairs.Count; p++)
                {
                    CapabilityId capability = pairs[p];
                    List<CandidateDecision> pairDecisions = new List<CandidateDecision>();
                    for (int d = 0; d < decisions.Count; d++)
                    {
                        if (decisions[d].Target.Equals(assembly.Target) && decisions[d].Capability.Equals(capability))
                        {
                            pairDecisions.Add(decisions[d]);
                        }
                    }

                    pairDecisions.Sort(CompareDecisions);

                    List<CapabilityContribution> winners = new List<CapabilityContribution>();
                    List<CapabilityContribution> shadowed = new List<CapabilityContribution>();
                    List<EffectiveSlot> slots = new List<EffectiveSlot>();
                    foreach (KeyValuePair<SlotGroupKey, EffectiveSlot> pair in accepted)
                    {
                        if (!pair.Key.Target.Equals(assembly.Target) || !pair.Key.Capability.Equals(capability))
                        {
                            continue;
                        }

                        slots.Add(pair.Value);
                        winners.AddRange(pair.Value.Support);
                        shadowed.AddRange(pair.Value.Shadowed);
                    }

                    slots.Sort(CompareSlots);
                    winners.Sort(CompareContributions);
                    shadowed.Sort(CompareContributions);

                    int stratum = -1;
                    if (pairDecisions.Count > 0)
                    {
                        stratum = pairDecisions[0].Stratum;
                    }

                    CapabilityContract? contract = snapshot.Contracts.Find(capability);
                    if (stratum < 0)
                    {
                        stratum = contract == null ? -1 : contract.Stratum;
                    }

                    List<Id128> descriptorEvidence = new List<Id128>();
                    IReadOnlyList<SchemaRef> schemas = target.Descriptor.SupportedSchemas;
                    for (int s = 0; s < schemas.Count; s++)
                    {
                        descriptorEvidence.Add(EvidenceKeys.Derive(
                            EvidenceKeys.EvidenceKey("declaredSchema", schemas[s].ToString())));
                    }

                    IReadOnlyList<CapabilityRef> declared = target.Descriptor.SupportedCapabilities;
                    for (int s = 0; s < declared.Count; s++)
                    {
                        descriptorEvidence.Add(EvidenceKeys.Derive(
                            EvidenceKeys.EvidenceKey("declaredCapability", declared[s].ToString())));
                    }

                    IReadOnlyList<Id128> tags = target.Descriptor.Tags;
                    for (int s = 0; s < tags.Count; s++)
                    {
                        descriptorEvidence.Add(EvidenceKeys.Derive(
                            EvidenceKeys.EvidenceKey("tag", tags[s].ToString())));
                    }

                    descriptorEvidence.Add(EvidenceKeys.Derive(
                        EvidenceKeys.EvidenceKey("recipe", target.Descriptor.Recipe.ToString())));
                    descriptorEvidence.Sort(Id128Codec.CompareBigEndian);

                    ContentHash slotHash = ContentHash.Compute(Encoding.UTF8.GetBytes(SlotHashText(slots)));
                    explanations.Add(new DerivationExplanation(
                        assembly.Target,
                        capability,
                        new SnapshotToken(snapshot.World, snapshot.Epoch, LogicalStepId.Zero),
                        snapshot.Mode,
                        stratum,
                        snapshot.ScopePath(assembly.Scope),
                        pairDecisions,
                        winners,
                        shadowed,
                        slots,
                        descriptorEvidence,
                        target.Descriptor.Exclusions,
                        assembly.EffectiveCapabilities,
                        assembly.RecipeHash,
                        slotHash));
                }
            }

            explanations.Sort(CompareExplanations);
            return explanations;
        }

        private static string SlotHashText(IReadOnlyList<EffectiveSlot> slots)
        {
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < slots.Count; i++)
            {
                text.Append(slots[i].Hash.ToHex()).Append('\n');
            }

            return text.ToString();
        }

        internal static int CompareCapabilityIds(CapabilityId left, CapabilityId right) =>
            left.Value.CompareTo(right.Value);

        internal static int CompareContributions(CapabilityContribution left, CapabilityContribution right)
        {
            int provider = left.Key.Provider.Value.CompareTo(right.Key.Provider.Value);
            if (provider != 0)
            {
                return provider;
            }

            int rule = left.Key.Rule.Value.CompareTo(right.Key.Rule.Value);
            if (rule != 0)
            {
                return rule;
            }

            int target = left.Key.Target.Value.CompareTo(right.Key.Target.Value);
            if (target != 0)
            {
                return target;
            }

            int capability = left.Key.Capability.Value.CompareTo(right.Key.Capability.Value);
            if (capability != 0)
            {
                return capability;
            }

            int slot = left.Key.OutputSlot.CompareTo(right.Key.OutputSlot);
            return slot != 0 ? slot : ((int)left.Disposition).CompareTo((int)right.Disposition);
        }

        internal static int CompareSlots(EffectiveSlot left, EffectiveSlot right)
        {
            int capability = left.Capability.Value.CompareTo(right.Capability.Value);
            if (capability != 0)
            {
                return capability;
            }

            int version = left.Version.CompareTo(right.Version);
            return version != 0 ? version : left.Slot.CompareTo(right.Slot);
        }

        internal static int CompareAssemblies(TargetAssembly left, TargetAssembly right) =>
            left.Target.Value.CompareTo(right.Target.Value);

        internal static int CompareDecisions(CandidateDecision left, CandidateDecision right)
        {
            int target = left.Target.Value.CompareTo(right.Target.Value);
            if (target != 0)
            {
                return target;
            }

            int capability = left.Capability.Value.CompareTo(right.Capability.Value);
            if (capability != 0)
            {
                return capability;
            }

            int provider = left.Provider.Value.CompareTo(right.Provider.Value);
            if (provider != 0)
            {
                return provider;
            }

            int rule = left.Rule.Value.CompareTo(right.Rule.Value);
            return rule != 0 ? rule : left.OutputSlot.CompareTo(right.OutputSlot);
        }

        internal static int CompareExplanations(DerivationExplanation left, DerivationExplanation right)
        {
            int target = left.Target.Value.CompareTo(right.Target.Value);
            return target != 0 ? target : left.Capability.Value.CompareTo(right.Capability.Value);
        }
    }

    /// <summary>One (target, capability, output slot) composition group; the canonical group key of the engine.</summary>
    internal readonly struct SlotGroupKey : IEquatable<SlotGroupKey>
    {
        public readonly TargetId Target;
        public readonly CapabilityId Capability;
        public readonly uint Slot;

        public SlotGroupKey(TargetId target, CapabilityId capability, uint slot)
        {
            Target = target;
            Capability = capability;
            Slot = slot;
        }

        public bool Equals(SlotGroupKey other) =>
            Target.Equals(other.Target) && Capability.Equals(other.Capability) && Slot == other.Slot;

        public override bool Equals(object? obj) => obj is SlotGroupKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + Target.GetHashCode();
                hash = (hash * 31) + Capability.GetHashCode();
                hash = (hash * 31) + Slot.GetHashCode();
                return hash;
            }
        }

        public static int Compare(SlotGroupKey left, SlotGroupKey right)
        {
            int target = left.Target.Value.CompareTo(right.Target.Value);
            if (target != 0)
            {
                return target;
            }

            int capability = left.Capability.Value.CompareTo(right.Capability.Value);
            return capability != 0 ? capability : left.Slot.CompareTo(right.Slot);
        }
    }

    /// <summary>One (target, capability) pair key: the explanation and evaluated-candidate index.</summary>
    internal readonly struct PairKey : IEquatable<PairKey>
    {
        public readonly TargetId Target;
        public readonly CapabilityId Capability;

        public PairKey(TargetId target, CapabilityId capability)
        {
            Target = target;
            Capability = capability;
        }

        public bool Equals(PairKey other) =>
            Target.Equals(other.Target) && Capability.Equals(other.Capability);

        public override bool Equals(object? obj) => obj is PairKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Target.GetHashCode() * 397) ^ Capability.GetHashCode();
            }
        }
    }
}
