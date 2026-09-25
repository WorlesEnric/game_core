// GameCore.Derivation — the full-recompute reference oracle (GC-006).
//
// P-023 requires an independent full recomputation oracle that must match the incremental effective assembly and
// provenance for randomized operation sequences. 02 s4: the oracle ignores indexes and recomputes every candidate
// using the same pure rule contracts but a separate traversal implementation. It is deliberately slow.
//
// What is shared with the engine is *semantics only*: the candidate predicates of `DerivationPolicy` and the
// composition kernel of `SlotComposer`. Traversal, indexing, budget accounting and assembly construction are
// deliberately not shared: those are the parts a differential test is supposed to falsify.
//
// The oracle also evaluates rules against targets the indexed engine never visits (a target advertising neither a
// selector schema nor the rule's output capability). That is the point: the oracle's decision set is a superset, so
// a test can assert both semantic equality and that the engine invented no decision the oracle cannot reproduce.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>Slow, index-free recomputation of the effective assembly (P-023).</summary>
    public static class DerivationOracle
    {
        /// <summary>
        /// Recomputes the whole derivation by brute force: every stratum, every target, every active rule, with its
        /// own group collection and assembly loop.
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
            CapabilityLedger ledger = new CapabilityLedger();
            Dictionary<SlotGroupKey, EffectiveSlot> accepted = new Dictionary<SlotGroupKey, EffectiveSlot>();
            List<TargetId> canonicalTargets = CanonicalTargets(snapshot);

            for (int stratum = 0; stratum < DerivationSnapshot.StratumCount; stratum++)
            {
                Dictionary<SlotGroupKey, List<RankedCandidate>> groups =
                    new Dictionary<SlotGroupKey, List<RankedCandidate>>();

                // Brute force: the oracle never consults a scope, schema or capability index.
                for (int t = 0; t < canonicalTargets.Count; t++)
                {
                    if (!snapshot.TryGetTarget(canonicalTargets[t], out DerivationTarget? target) || target == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < snapshot.ActiveInstalls.Count; i++)
                    {
                        DerivationInstall install = snapshot.ActiveInstalls[i];
                        IReadOnlyList<DerivationRule> rules = install.Manifest.DerivationRules;
                        for (int r = 0; r < rules.Count; r++)
                        {
                            DerivationRule rule = rules[r];
                            if (rule.OutputStratum != stratum)
                            {
                                // A rule declared outside 0..31 was already rejected by validation, so no stratum
                                // loop evaluates it (P-021).
                                continue;
                            }

                            counters.ExaminedCandidates++;
                            counters.IndexTargetsVisited++;

                            CandidateEvaluation evaluation = DerivationPolicy.Evaluate(
                                snapshot, values, install, rule, target, ledger);

                            uint declaredSlots = DeclaredSlots(snapshot, rule);
                            uint boundedSlots = rule.MaxOutputSlots < declaredSlots ? rule.MaxOutputSlots : declaredSlots;

                            if (evaluation.Status != CandidateStatus.Emitted)
                            {
                                decisions.Add(Decision(install, rule, target, 0U, evaluation, snapshot, stratum));
                                continue;
                            }

                            CapabilityContract? contract = snapshot.Contracts.Find(rule.OutputCapability.Capability);
                            for (uint slot = 0; slot < boundedSlots; slot++)
                            {
                                decisions.Add(Decision(install, rule, target, slot, evaluation, snapshot, stratum));

                                OutputSlotSchema? schema;
                                SlotCompositionPolicy? policy;
                                if (contract == null
                                    || !snapshot.Contracts.TryGetSlot(contract, slot, out schema, out policy)
                                    || schema == null
                                    || policy == null)
                                {
                                    failures.Add(new CompositionFailure(
                                        DiagnosticCode.MissingDependency,
                                        target.Target,
                                        rule.OutputCapability.Capability,
                                        slot,
                                        null,
                                        new[] { rule.RuleId.Value },
                                        "The rule's declared output slot has no contract schema/policy (P-017, P-019)."));
                                    return DerivationResult.Reject(
                                        DerivationRejectionKind.CompositionConflict, snapshot, null, failures, counters);
                                }

                                SlotGroupKey groupKey = new SlotGroupKey(
                                    target.Target, rule.OutputCapability.Capability, slot);
                                if (!groups.TryGetValue(groupKey, out List<RankedCandidate>? group))
                                {
                                    group = new List<RankedCandidate>();
                                    groups.Add(groupKey, group);
                                }

                                group.Add(new RankedCandidate(
                                    new CapabilityContribution(
                                        new ContributionKey(
                                            install.Instance, rule.RuleId, target.Target, groupKey.Capability, slot),
                                        schema.Schema,
                                        policy.Policy,
                                        rule.PayloadDefinition,
                                        PayloadCodec.HashOf(rule.PayloadDefinition),
                                        ContributionDisposition.Active),
                                    install.Record.Priority,
                                    rule.Priority,
                                    snapshot.Depth(install.Scope),
                                    install.Scope));
                            }
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
                    if (!snapshot.TryGetTarget(groupKey.Target, out DerivationTarget? groupTarget) || groupTarget == null)
                    {
                        continue;
                    }

                    CapabilityContract contract = snapshot.Contracts.Find(groupKey.Capability)!;
                    OutputSlotSchema? schema;
                    SlotCompositionPolicy? policy;
                    snapshot.Contracts.TryGetSlot(contract, groupKey.Slot, out schema, out policy);

                    List<RankedCandidate> group = groups[groupKey];
                    group.Sort(RankedCandidate.Compare);

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
                            group,
                            snapshot.Overrides,
                            EvaluatedKeys(group),
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

                    if (composed != null)
                    {
                        accepted[groupKey] = composed;
                    }
                }

                // Finalize this stratum before the next one: a rule reads strictly lower strata only (P-021).
                for (int i = 0; i < orderedGroups.Count; i++)
                {
                    ledger.Finalize(orderedGroups[i].Target, orderedGroups[i].Capability);
                }
            }

            Dictionary<Id128, List<EffectiveSlot>> slotsByTarget = new Dictionary<Id128, List<EffectiveSlot>>();
            foreach (KeyValuePair<SlotGroupKey, EffectiveSlot> pair in accepted)
            {
                if (!slotsByTarget.TryGetValue(pair.Key.Target.Value, out List<EffectiveSlot>? list))
                {
                    list = new List<EffectiveSlot>();
                    slotsByTarget.Add(pair.Key.Target.Value, list);
                }

                list.Add(pair.Value);
            }

            // Cross-capability incompatibility of P-019, recomputed independently per target.
            List<Id128> targetIds = new List<Id128>(slotsByTarget.Keys);
            targetIds.Sort(Id128Codec.CompareBigEndian);
            for (int i = 0; i < targetIds.Count; i++)
            {
                List<CapabilityId> effectiveCapabilities = DerivationEngine.EffectiveCapabilitiesOf(slotsByTarget[targetIds[i]]);
                CompositionFailure? conflict;
                if (!SlotComposer.TryCheckIncompatibility(
                        snapshot, new TargetId(targetIds[i]), effectiveCapabilities, out conflict))
                {
                    if (conflict != null)
                    {
                        failures.Add(conflict);
                    }

                    return DerivationResult.Reject(
                        DerivationRejectionKind.IncompatibleCapabilities, snapshot, null, failures, counters);
                }
            }

            // The oracle's own assembly loop.
            List<TargetAssembly> assemblies = new List<TargetAssembly>();
            List<CapabilityContribution> contributions = new List<CapabilityContribution>();
            for (int t = 0; t < canonicalTargets.Count; t++)
            {
                TargetId targetId = canonicalTargets[t];
                if (!snapshot.TryGetTarget(targetId, out DerivationTarget? target) || target == null)
                {
                    continue;
                }

                List<EffectiveSlot> slots = slotsByTarget.TryGetValue(targetId.Value, out List<EffectiveSlot>? found)
                    ? found
                    : new List<EffectiveSlot>();
                slots.Sort(DerivationEngine.CompareSlots);

                List<CapabilityContribution> targetContributions = new List<CapabilityContribution>();
                for (int s = 0; s < slots.Count; s++)
                {
                    for (int i = 0; i < slots[s].Support.Count; i++)
                    {
                        targetContributions.Add(slots[s].Support[i]);
                        contributions.Add(slots[s].Support[i]);
                    }

                    for (int i = 0; i < slots[s].Shadowed.Count; i++)
                    {
                        targetContributions.Add(slots[s].Shadowed[i]);
                        contributions.Add(slots[s].Shadowed[i]);
                    }
                }

                targetContributions.Sort(DerivationEngine.CompareContributions);
                assemblies.Add(new TargetAssembly(
                    target.Target,
                    target.Scope,
                    target.Descriptor.Recipe,
                    slots,
                    targetContributions,
                    DerivationEngine.EffectiveCapabilitiesOf(slots),
                    AssemblyHash.Compute(target, slots)));
            }

            contributions.Sort(DerivationEngine.CompareContributions);

            // Losers become shadowed here too, so two decision sets are comparable (P-019).
            Reclassify(decisions, accepted);
            decisions.Sort(DerivationEngine.CompareDecisions);

            counters.EmittedContributions = contributions.Count;
            counters.AffectedTargets = assemblies.Count;
            counters.RulesEvaluated = CountDeclaredRules(snapshot);

            DerivationDelta? delta = previous == null
                ? null
                : DerivationDeltaBuilder.Build(previous, assemblies, contributions);

            return DerivationResult.Accept(
                snapshot,
                assemblies,
                contributions,
                decisions,
                Array.Empty<DerivationExplanation>(),
                counters,
                delta,
                AssemblyHash.ComputeResult(snapshot, assemblies));
        }

        private static List<TargetId> CanonicalTargets(DerivationSnapshot snapshot)
        {
            List<TargetId> targets = new List<TargetId>(snapshot.Targets.Count);
            for (int i = 0; i < snapshot.Targets.Count; i++)
            {
                targets.Add(snapshot.Targets[i].Target);
            }

            targets.Sort((left, right) => left.Value.CompareTo(right.Value));
            return targets;
        }

        private static IReadOnlyList<ContributionKey> EvaluatedKeys(IReadOnlyList<RankedCandidate> group)
        {
            List<ContributionKey> keys = new List<ContributionKey>(group.Count);
            for (int i = 0; i < group.Count; i++)
            {
                keys.Add(group[i].Key);
            }

            keys.Sort((left, right) =>
            {
                int provider = left.Provider.Value.CompareTo(right.Provider.Value);
                return provider != 0 ? provider : left.Rule.Value.CompareTo(right.Rule.Value);
            });

            return keys;
        }

        private static int DeclaredSlots(DerivationSnapshot snapshot, DerivationRule rule)
        {
            CapabilityContract? contract = snapshot.Contracts.Find(rule.OutputCapability.Capability);
            return contract == null ? 0 : contract.OutputSlots.Count;
        }

        private static int CountDeclaredRules(DerivationSnapshot snapshot)
        {
            int count = 0;
            for (int i = 0; i < snapshot.ActiveInstalls.Count; i++)
            {
                count += snapshot.ActiveInstalls[i].Manifest.DerivationRules.Count;
            }

            return count;
        }

        private static void Reclassify(
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

        private static CandidateDecision Decision(
            DerivationInstall install,
            DerivationRule rule,
            DerivationTarget target,
            uint slot,
            CandidateEvaluation evaluation,
            DerivationSnapshot snapshot,
            int stratum) =>
            new CandidateDecision(
                EvidenceKeys.Derive(EvidenceKeys.CandidateKey(
                    install.Instance, rule.RuleId, target.Target, rule.OutputCapability.Capability, slot)),
                install.Instance,
                rule.RuleId,
                target.Target,
                rule.OutputCapability.Capability,
                slot,
                install.Scope,
                snapshot.Depth(install.Scope),
                stratum,
                evaluation.Status,
                evaluation.Reason,
                evaluation.Diagnostic,
                evaluation.ModeGate,
                install.Record.Priority,
                rule.Priority,
                evaluation.Exclusions,
                evaluation.Boundaries,
                evaluation.MissingInputs,
                evaluation.EvidenceKeys);
    }
}
