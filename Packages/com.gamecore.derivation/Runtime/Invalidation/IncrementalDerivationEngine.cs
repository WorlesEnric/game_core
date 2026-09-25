// GameCore.Derivation — the incremental derivation engine (GC-013, P-023/P-025).
//
// One entry point, `Derive`, that produces exactly the result a full recomputation would produce while examining
// only the invalidated part of the world:
//
//   1. validate the input exactly as the full engine does (P-028);
//   2. diff the two snapshots into a `DerivationChangeSet` and compute its invalidation closure (P-023);
//   3. if the change legitimately invalidates the world (mode switch, catalog change, new incarnation, no usable
//      base) delegate to `DerivationEngine.Derive` and report that cost;
//   4. otherwise re-derive the dirty targets only, carrying every other target's assembly, contributions,
//      decisions and explanations forward unchanged.
//
// Locality comes from inverting the candidate enumeration: instead of every rule asking "which targets are in my
// reach?", each *dirty* target asks the install-path index "which rules can reach me?" (every rule of every active
// installation at the target's scope or one of its ancestors, which is the complete set under every P-013 reach
// selector). A rule with no dirty candidate is never enumerated at all, so an edit that touches one branch never
// walks a sibling branch (TEST-008).
//
// Parity is what makes this safe, and it is enforced by the differential sweep: the semantic projection, the
// decision set, the explanations and the delta of an incremental derivation must equal the oracle's. The carried
// targets are exactly those the change set proves cannot change: their owner scope, their descriptor, every rule
// that can reach them, the mode, the contract table and the override set are all unchanged.
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>Which path one incremental derivation took, and what it cost (P-023, 02 s9).</summary>
    public sealed class IncrementalDerivationOutcome
    {
        public IncrementalDerivationOutcome(
            DerivationResult result,
            InvalidationClosureResult invalidation,
            bool usedFullRecompute,
            InvalidationCounters counters)
        {
            Result = result;
            Invalidation = invalidation;
            UsedFullRecompute = usedFullRecompute;
            Counters = counters;
        }

        /// <summary>The derivation result; identical to a full recomputation's for the same snapshot (P-023).</summary>
        public DerivationResult Result { get; }

        /// <summary>The closure that decided which targets were re-derived (P-023, TEST-008).</summary>
        public InvalidationClosureResult Invalidation { get; }

        /// <summary>True when the change set invalidated the world and the full engine ran instead.</summary>
        public bool UsedFullRecompute { get; }

        /// <summary>Work counters of the incremental path, including the carried-target count.</summary>
        public InvalidationCounters Counters { get; }

        /// <summary>One-line audit text of this derivation (P-052).</summary>
        public string Describe() =>
            "incremental{fullRecompute=" + (UsedFullRecompute ? "1" : "0")
            + ";" + Invalidation.Describe() + ";" + Counters.Describe() + "}";

        public override string ToString() => Describe();
    }

    /// <summary>The incremental derivation engine: the same result as a full recomputation, over the dirty set (P-023).</summary>
    public static class IncrementalDerivationEngine
    {
        /// <summary>
        /// Derives <paramref name="snapshot"/> incrementally from <paramref name="previous"/>. A null change set
        /// asks for the canonical snapshot diff; an accepted previous result of the same world is required for the
        /// incremental path, and anything else falls back to the full engine (reported as a reason).
        /// </summary>
        public static IncrementalDerivationOutcome Derive(
            DerivationSnapshot snapshot,
            IDerivationValueSource values,
            DerivationOptions? options,
            DerivationResult? previous,
            DerivationChangeSet? changeSet,
            DerivedRecipeCache? recipeCache = null)
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
            InvalidationCounters counters = new InvalidationCounters();

            if (previous == null || !previous.Accepted)
            {
                counters.AddReason(InvalidationReasons.NoAcceptedBase);
                DerivationResult full = DerivationEngine.Derive(snapshot, values, effective, previous);
                InvalidationClosureResult seed = FullWorldClosure(snapshot, counters, InvalidationReasons.NoAcceptedBase);
                counters.CarriedTargets = 0;
                counters.CandidateEvaluations = full.Counters.ExaminedCandidates;
                return new IncrementalDerivationOutcome(full, seed, true, counters);
            }

            DerivationChangeSet declared = changeSet
                ?? DerivationChangeSet.Diff(previous.Snapshot, snapshot, counters);

            // A carried explanation needs the previous run to have collected them. A previous run with accepted
            // assemblies but *no* decisions provably had nothing to explain (its dirty targets lost every reaching
            // rule), which is a legitimate empty provenance set rather than a missing one, so it stays usable as
            // a base: only a run that made decisions yet recorded no explanations lacks the provenance to carry.
            bool explanationsUsable = !effective.CollectExplanations
                || previous.Explanations.Count != 0
                || previous.Decisions.Count == 0
                || previous.Assemblies.Count == 0;

            if (!explanationsUsable)
            {
                counters.AddReason(InvalidationReasons.ExplanationsUnavailable);
            }

            if (!explanationsUsable || declared.WorldChanged || declared.ModeChanged || declared.ContractsChanged)
            {
                DerivationResult full = DerivationEngine.Derive(snapshot, values, effective, previous);
                InvalidationClosureResult whole = InvalidationClosure.Compute(
                    previous.Snapshot, snapshot, declared, null, null, counters);
                counters.CarriedTargets = 0;
                counters.CandidateEvaluations = full.Counters.ExaminedCandidates;
                return new IncrementalDerivationOutcome(full, whole, true, counters);
            }

            DerivationIndexSet previousIndexes = DerivationIndexSet.Build(previous.Snapshot);
            DerivationIndexSet nextIndexes = DerivationIndexSet.Build(snapshot);
            InvalidationClosureResult closure = InvalidationClosure.Compute(
                previous.Snapshot, snapshot, declared, previousIndexes, nextIndexes, counters);

            if (!closure.WholeWorld && closure.IsEmpty)
            {
                // Nothing can have changed: carry the whole previous result. The delta is empty by construction,
                // which is what makes a no-op proposal publish nothing (P-006).
                DerivationResult carried = Carry(snapshot, previous, effective, counters);
                return new IncrementalDerivationOutcome(carried, closure, false, counters);
            }

            // Validation runs on the whole input, because a catalog problem is a property of the snapshot rather
            // than of the dirty set (P-028).
            DerivationValidation validation = DerivationValidation.Validate(
                snapshot, values, effective.ExpectedRevision, effective.ExpectedEpoch);
            if (!validation.IsValid)
            {
                counters.WithinBudget = false;
                DerivationResult rejected = DerivationResult.Reject(
                    DerivationRejectionKind.ValidationFailed, snapshot, validation.Problems, null, counters);
                return new IncrementalDerivationOutcome(rejected, closure, false, counters);
            }

            return DeriveDirty(
                snapshot, values, effective, previous, nextIndexes, closure, counters, recipeCache);
        }

        private static InvalidationClosureResult FullWorldClosure(
            DerivationSnapshot snapshot,
            InvalidationCounters counters,
            string reason)
        {
            counters.WholeWorld = true;
            counters.AddReason(reason);
            List<TargetId> targets = new List<TargetId>(snapshot.Targets.Count);
            for (int i = 0; i < snapshot.Targets.Count; i++)
            {
                targets.Add(snapshot.Targets[i].Target);
            }

            List<ScopeId> scopes = new List<ScopeId>(snapshot.Scopes.Count);
            for (int i = 0; i < snapshot.Scopes.Count; i++)
            {
                scopes.Add(snapshot.Scopes[i].Scope);
            }

            counters.DirtyTargets = targets.Count;
            counters.DirtyScopes = scopes.Count;
            return new InvalidationClosureResult(
                targets, scopes, true, DerivationChangeSet.Empty, counters);
        }

        private static DerivationResult Carry(
            DerivationSnapshot snapshot,
            DerivationResult previous,
            DerivationOptions options,
            InvalidationCounters counters)
        {
            // Every target is carried: no candidate is examined, and the delta the caller sees is empty.
            counters.CarriedTargets = snapshot.Targets.Count;
            counters.CandidateEvaluations = 0;
            List<TargetAssembly> assemblies = new List<TargetAssembly>(snapshot.Targets.Count);
            List<CapabilityContribution> contributions = new List<CapabilityContribution>();
            List<CandidateDecision> decisions = new List<CandidateDecision>();
            List<DerivationExplanation> explanations = new List<DerivationExplanation>();
            SnapshotToken token = new SnapshotToken(snapshot.World, snapshot.Epoch, LogicalStepId.Zero);
            for (int i = 0; i < snapshot.Targets.Count; i++)
            {
                TargetId target = snapshot.Targets[i].Target;
                TargetAssembly? carried = previous.AssemblyOf(target);
                if (carried == null)
                {
                    // The change set promised this target was unaffected, so a missing assembly is a bug in the
                    // change detection; make it observable instead of publishing a partial closure.
                    throw new InvalidOperationException(
                        "the change set reported no change for " + target.ToString()
                        + " but the previous derivation has no assembly for it (P-023).");
                }

                assemblies.Add(carried);
                for (int c = 0; c < carried.Contributions.Count; c++)
                {
                    contributions.Add(carried.Contributions[c]);
                }

                for (int d = 0; d < previous.Decisions.Count; d++)
                {
                    if (previous.Decisions[d].Target.Equals(target))
                    {
                        decisions.Add(previous.Decisions[d]);
                    }
                }

                for (int e = 0; e < previous.Explanations.Count; e++)
                {
                    if (previous.Explanations[e].Target.Equals(target))
                    {
                        explanations.Add(previous.Explanations[e].WithToken(token));
                    }
                }
            }

            assemblies.Sort(DerivationEngine.CompareAssemblies);
            contributions.Sort(DerivationEngine.CompareContributions);
            decisions.Sort(DerivationEngine.CompareDecisions);
            explanations.Sort(DerivationEngine.CompareExplanations);

            DerivationDelta? delta = DerivationDeltaBuilder.Build(previous, assemblies, contributions);
            counters.AffectedTargets = delta == null ? assemblies.Count : delta.AffectedTargets.Count;
            long applyEstimate = DerivationEngine.EstimateApplyCostMicroseconds(assemblies, delta);
            if (applyEstimate > options.Budget.MaxApplyCostEstimateMicroseconds)
            {
                DerivationEngine.MarkExceeded(
                    counters, BudgetDimension.ApplyCostEstimate, applyEstimate, options.Budget.MaxApplyCostEstimateMicroseconds, null);
                return DerivationResult.Reject(
                    DerivationRejectionKind.BudgetExceeded, snapshot, null, null, counters);
            }

            ContentHash resultHash = AssemblyHash.ComputeResult(snapshot, assemblies);
            return DerivationResult.Accept(
                snapshot, assemblies, contributions, decisions, explanations, counters, delta, resultHash);
        }

        private static IncrementalDerivationOutcome DeriveDirty(
            DerivationSnapshot snapshot,
            IDerivationValueSource values,
            DerivationOptions effective,
            DerivationResult previous,
            DerivationIndexSet nextIndexes,
            InvalidationClosureResult closure,
            InvalidationCounters counters,
            DerivedRecipeCache? recipeCache)
        {
            // The dirty target set, plus the rules that can reach them: a rule with no dirty candidate is never
            // enumerated, which is the whole locality claim (TEST-008).
            HashSet<Id128> dirtyLookup = new HashSet<Id128>();
            List<DerivationTarget> dirtyTargets = new List<DerivationTarget>(closure.DirtyTargets.Count);
            for (int i = 0; i < closure.DirtyTargets.Count; i++)
            {
                TargetId id = closure.DirtyTargets[i];
                if (!snapshot.TryGetTarget(id, out DerivationTarget? target) || target == null)
                {
                    continue;
                }

                dirtyLookup.Add(id.Value);
                dirtyTargets.Add(target);
            }

            Dictionary<Id128, List<DerivationTarget>> dirtyTargetsByRule = new Dictionary<Id128, List<DerivationTarget>>();
            counters.CarriedTargets = snapshot.Targets.Count - dirtyTargets.Count;

            List<CandidateDecision> decisions = new List<CandidateDecision>();
            List<CandidateDecision> freshDecisions = new List<CandidateDecision>();
            List<CompositionFailure> failures = new List<CompositionFailure>();
            CapabilityLedger ledger = new CapabilityLedger();
            Dictionary<SlotGroupKey, EffectiveSlot> accepted = new Dictionary<SlotGroupKey, EffectiveSlot>();
            Dictionary<Id128, List<EffectiveSlot>> slotsByTarget = new Dictionary<Id128, List<EffectiveSlot>>();
            Dictionary<Id128, List<CapabilityContribution>> contributionsByTarget =
                new Dictionary<Id128, List<CapabilityContribution>>();
            Dictionary<PairKey, List<ContributionKey>> evaluatedByPair = new Dictionary<PairKey, List<ContributionKey>>();
            List<Id128> fanOutCauses = new List<Id128>();

            // Clean targets keep their finalized lower strata: their eligibility inputs are provably unchanged, so
            // re-finalizing them costs nothing and re-evaluating them would be the rescan P-023 forbids.
            SeedLedgerFromPrevious(snapshot, previous, ledger, dirtyLookup);
            SeedDecisionsFromPrevious(snapshot, previous, dirtyLookup, decisions);

            for (int i = 0; i < dirtyTargets.Count; i++)
            {
                DerivationTarget target = dirtyTargets[i];
                // The P-024 variant cache resolves the reaching rules for this recipe/scope inheritance; without a
                // cache the same resolution is made directly from the install-path index.
                IReadOnlyList<IndexedRule> rules;
                if (recipeCache != null)
                {
                    rules = recipeCache.Resolve(snapshot, nextIndexes, target.Target, out _);
                }
                else
                {
                    rules = nextIndexes.RulesReaching(target.Target, out _);
                }

                for (int r = 0; r < rules.Count; r++)
                {
                    IndexedRule rule = rules[r];
                    if (!IsInPopulation(rule.Rule, target))
                    {
                        continue;
                    }

                    if (!dirtyTargetsByRule.TryGetValue(rule.Rule.RuleId.Value, out List<DerivationTarget>? list))
                    {
                        list = new List<DerivationTarget>();
                        dirtyTargetsByRule.Add(rule.Rule.RuleId.Value, list);
                    }

                    // Two installations may declare the same rule identity; the map is keyed by rule id, so the
                    // target must be recorded once per rule id, not once per declaring install, or the stratum
                    // loop would evaluate it once per RuleSource and duplicate the decision (P-026).
                    if (!ContainsTarget(list, target.Target))
                    {
                        list.Add(target);
                    }
                }
            }

            // The rules the stratum loop will evaluate: exactly the ones with at least one dirty candidate.
            counters.DirtyRules = dirtyTargetsByRule.Count;

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
                    if (!dirtyTargetsByRule.TryGetValue(rule.RuleId.Value, out List<DerivationTarget>? candidates)
                        || candidates == null)
                    {
                        // No dirty candidate: nothing this rule emits can have changed, so it is not enumerated.
                        counters.SkippedRules++;
                        continue;
                    }

                    counters.RulesEvaluated++;
                    counters.IndexBucketsVisited++;
                    List<DerivationTarget> ordered = nextIndexes.CandidateTargets(
                        source.Install.Scope, rule.Reach, rule.SelectorContracts, rule.OutputCapability.Capability, candidates);
                    counters.IndexTargetsVisited += ordered.Count;

                    for (int c = 0; c < ordered.Count; c++)
                    {
                        DerivationTarget target = ordered[c];
                        counters.ExaminedCandidates++;
                        counters.CandidateEvaluations++;
                        if (fanOutCauses.Count < DerivationEngine.FanOutCauseSampleLimit)
                        {
                            fanOutCauses.Add(source.Install.Instance.Value);
                            fanOutCauses.Add(target.Target.Value);
                        }

                        BudgetDimension dimension;
                        long observed;
                        long limit;
                        if (!DerivationEngine.WithinBudget(counters, effective.Budget, out dimension, out observed, out limit))
                        {
                            DerivationEngine.MarkExceeded(
                                counters, dimension, observed, limit, DerivationEngine.TopFanOutCauses(fanOutCauses));
                            return Rejected(snapshot, DerivationRejectionKind.BudgetExceeded, closure, counters, null);
                        }

                        CandidateEvaluation evaluation = DerivationPolicy.Evaluate(
                            snapshot, values, source.Install, rule, target, ledger);

                        PairKey pairKey = new PairKey(target.Target, rule.OutputCapability.Capability);
                        if (!evaluatedByPair.TryGetValue(pairKey, out List<ContributionKey>? evaluated))
                        {
                            evaluated = new List<ContributionKey>();
                            evaluatedByPair.Add(pairKey, evaluated);
                        }

                        uint declaredSlots = (uint)DerivationEngine.SlotsOf(snapshot, rule);
                        uint boundedSlots = rule.MaxOutputSlots < declaredSlots ? rule.MaxOutputSlots : declaredSlots;

                        if (evaluation.Status != CandidateStatus.Emitted)
                        {
                            freshDecisions.Add(DerivationEngine.Decision(source, rule, target, 0U, evaluation, snapshot, stratum));
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
                            freshDecisions.Add(DerivationEngine.Decision(source, rule, target, slot, evaluation, snapshot, stratum));

                            CapabilityContract? outputContract = snapshot.Contracts.Find(rule.OutputCapability.Capability);
                            OutputSlotSchema? schema;
                            SlotCompositionPolicy? policy;
                            if (outputContract == null
                                || !snapshot.Contracts.TryGetSlot(outputContract, slot, out schema, out policy)
                                || schema == null
                                || policy == null)
                            {
                                failures.Add(new CompositionFailure(
                                    DiagnosticCode.MissingDependency,
                                    target.Target,
                                    rule.OutputCapability.Capability,
                                    slot,
                                    new[] { key },
                                    new[] { rule.RuleId.Value },
                                    "The rule's declared output slot has no contract schema/policy (P-017, P-019)."));
                                return Rejected(
                                    snapshot, DerivationRejectionKind.CompositionConflict, closure, counters, failures);
                            }

                            CapabilityContribution contribution = new CapabilityContribution(
                                key,
                                schema.Schema,
                                policy.Policy,
                                rule.PayloadDefinition,
                                PayloadCodec.HashOf(rule.PayloadDefinition),
                                ContributionDisposition.Active);

                            SlotGroupKey groupKey = new SlotGroupKey(target.Target, rule.OutputCapability.Capability, slot);
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

                    if (!snapshot.TryGetTarget(groupKey.Target, out DerivationTarget? groupTarget) || groupTarget == null)
                    {
                        continue;
                    }

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

                        return Rejected(
                            snapshot, DerivationRejectionKind.CompositionConflict, closure, counters, failures);
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
                    if (!DerivationEngine.WithinBudget(counters, effective.Budget, out dimension, out observed, out limit))
                    {
                        DerivationEngine.MarkExceeded(
                            counters, dimension, observed, limit, DerivationEngine.TopFanOutCauses(fanOutCauses));
                        return Rejected(snapshot, DerivationRejectionKind.BudgetExceeded, closure, counters, null);
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

                // The stratum is final before any higher stratum is evaluated (P-021).
                for (int i = 0; i < orderedGroups.Count; i++)
                {
                    SlotGroupKey groupKey = orderedGroups[i];
                    if (accepted.ContainsKey(groupKey))
                    {
                        ledger.Finalize(groupKey.Target, groupKey.Capability);
                    }
                }
            }

            if (!DerivationEngine.TryCheckDeadline(snapshot, effective, counters, out DerivationResult? deadlineRejection))
            {
                return new IncrementalDerivationOutcome(deadlineRejection!, closure, false, counters);
            }

            // Cross-capability incompatibility, checked for the dirty targets: a clean target's effective set and
            // the contract table are both unchanged, so it was already validated (P-019).
            List<Id128> effectiveTargetIds = new List<Id128>(slotsByTarget.Keys);
            effectiveTargetIds.Sort(Id128Codec.CompareBigEndian);
            for (int i = 0; i < effectiveTargetIds.Count; i++)
            {
                List<CapabilityId> effectiveCapabilities =
                    DerivationEngine.EffectiveCapabilitiesOf(slotsByTarget[effectiveTargetIds[i]]);
                CompositionFailure? conflict;
                if (!SlotComposer.TryCheckIncompatibility(
                        snapshot, new TargetId(effectiveTargetIds[i]), effectiveCapabilities, out conflict))
                {
                    if (conflict != null)
                    {
                        failures.Add(conflict);
                    }

                    return Rejected(
                        snapshot, DerivationRejectionKind.IncompatibleCapabilities, closure, counters, failures);
                }
            }

            // Assemble: a dirty target from this run, a clean target from the previous run unchanged.
            SnapshotToken token = new SnapshotToken(snapshot.World, snapshot.Epoch, LogicalStepId.Zero);
            List<TargetAssembly> assemblies = new List<TargetAssembly>(snapshot.Targets.Count);
            List<CapabilityContribution> allContributions = new List<CapabilityContribution>();
            List<DerivationExplanation> explanations = new List<DerivationExplanation>();
            for (int i = 0; i < snapshot.Targets.Count; i++)
            {
                DerivationTarget target = snapshot.Targets[i];
                if (!dirtyLookup.Contains(target.Target.Value))
                {
                    TargetAssembly? carried = previous.AssemblyOf(target.Target);
                    if (carried == null)
                    {
                        throw new InvalidOperationException(
                            "the change set reported no change for " + target.Target.ToString()
                            + " but the previous derivation has no assembly for it (P-023).");
                    }

                    assemblies.Add(carried);
                    for (int c = 0; c < carried.Contributions.Count; c++)
                    {
                        allContributions.Add(carried.Contributions[c]);
                    }

                    continue;
                }

                List<EffectiveSlot> slots = slotsByTarget.TryGetValue(target.Target.Value, out List<EffectiveSlot>? foundSlots)
                    ? foundSlots
                    : new List<EffectiveSlot>();
                slots.Sort(DerivationEngine.CompareSlots);

                List<CapabilityContribution> contributions = contributionsByTarget.TryGetValue(
                    target.Target.Value, out List<CapabilityContribution>? foundContributions)
                    ? foundContributions
                    : new List<CapabilityContribution>();
                contributions.Sort(DerivationEngine.CompareContributions);
                for (int c = 0; c < contributions.Count; c++)
                {
                    allContributions.Add(contributions[c]);
                }

                List<CapabilityId> effectiveCapabilities = DerivationEngine.EffectiveCapabilitiesOf(slots);
                assemblies.Add(new TargetAssembly(
                    target.Target,
                    target.Scope,
                    target.Descriptor.Recipe,
                    slots,
                    contributions,
                    effectiveCapabilities,
                    AssemblyHash.Compute(target, slots)));
            }

            assemblies.Sort(DerivationEngine.CompareAssemblies);
            allContributions.Sort(DerivationEngine.CompareContributions);

            // Losers are recorded as shadowed after composition for this run's decisions; carried decisions were
            // reclassified by the run that produced them and this run's accepted map covers the dirty targets
            // only, so they must be excluded from the pass (P-017, P-019, P-026).
            DerivationEngine.ReclassifyShadowed(freshDecisions, accepted);
            decisions.AddRange(freshDecisions);
            decisions.Sort(DerivationEngine.CompareDecisions);

            if (effective.CollectExplanations)
            {
                List<TargetAssembly> dirtyAssemblies = new List<TargetAssembly>(dirtyTargets.Count);
                for (int i = 0; i < assemblies.Count; i++)
                {
                    if (dirtyLookup.Contains(assemblies[i].Target.Value))
                    {
                        dirtyAssemblies.Add(assemblies[i]);
                    }
                }

                explanations.AddRange(DerivationEngine.BuildExplanations(
                    snapshot, dirtyAssemblies, decisions, accepted, slotsByTarget));
                for (int e = 0; e < previous.Explanations.Count; e++)
                {
                    DerivationExplanation explanation = previous.Explanations[e];
                    if (!dirtyLookup.Contains(explanation.Target.Value)
                        && snapshot.TryGetTarget(explanation.Target, out _))
                    {
                        explanations.Add(explanation.WithToken(token));
                    }
                }

                explanations.Sort(DerivationEngine.CompareExplanations);
            }

            DerivationDelta? delta = DerivationDeltaBuilder.Build(previous, assemblies, allContributions);
            counters.AffectedTargets = delta == null ? assemblies.Count : delta.AffectedTargets.Count;

            long applyEstimate = DerivationEngine.EstimateApplyCostMicroseconds(assemblies, delta);
            if (applyEstimate > effective.Budget.MaxApplyCostEstimateMicroseconds)
            {
                DerivationEngine.MarkExceeded(
                    counters,
                    BudgetDimension.ApplyCostEstimate,
                    applyEstimate,
                    effective.Budget.MaxApplyCostEstimateMicroseconds,
                    DerivationEngine.TopFanOutCauses(fanOutCauses));
                return Rejected(snapshot, DerivationRejectionKind.BudgetExceeded, closure, counters, null);
            }

            ContentHash resultHash = AssemblyHash.ComputeResult(snapshot, assemblies);
            DerivationResult result = DerivationResult.Accept(
                snapshot, assemblies, allContributions, decisions, explanations, counters, delta, resultHash);
            return new IncrementalDerivationOutcome(result, closure, false, counters);
        }

        private static IncrementalDerivationOutcome Rejected(
            DerivationSnapshot snapshot,
            DerivationRejectionKind kind,
            InvalidationClosureResult closure,
            InvalidationCounters counters,
            IReadOnlyList<CompositionFailure>? failures) =>
            new IncrementalDerivationOutcome(
                DerivationResult.Reject(kind, snapshot, null, failures, counters), closure, false, counters);

        /// <summary>
        /// Candidate population test of one rule for one target, matching the snapshot's own
        /// <see cref="DerivationSnapshot.TargetsInReach"/> predicate exactly (P-015, P-023).
        /// </summary>
        private static bool IsInPopulation(DerivationRule rule, DerivationTarget target)
        {
            IReadOnlyList<SchemaRef> selectors = rule.SelectorContracts;
            if (selectors.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < selectors.Count; i++)
            {
                if (target.DeclaresSchemaId(selectors[i].Id))
                {
                    return true;
                }
            }

            return target.DeclaresCapabilityId(rule.OutputCapability.Capability);
        }

        private static bool ContainsTarget(List<DerivationTarget> targets, TargetId target)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].Target.Equals(target))
                {
                    return true;
                }
            }

            return false;
        }

        private static void SeedLedgerFromPrevious(
            DerivationSnapshot snapshot,
            DerivationResult previous,
            CapabilityLedger ledger,
            HashSet<Id128> dirtyLookup)
        {
            for (int i = 0; i < snapshot.Targets.Count; i++)
            {
                TargetId target = snapshot.Targets[i].Target;
                if (dirtyLookup.Contains(target.Value))
                {
                    continue;
                }

                TargetAssembly? assembly = previous.AssemblyOf(target);
                if (assembly == null)
                {
                    continue;
                }

                for (int c = 0; c < assembly.EffectiveCapabilities.Count; c++)
                {
                    ledger.Finalize(target, assembly.EffectiveCapabilities[c]);
                }
            }
        }

        private static void SeedDecisionsFromPrevious(
            DerivationSnapshot snapshot,
            DerivationResult previous,
            HashSet<Id128> dirtyLookup,
            List<CandidateDecision> decisions)
        {
            for (int d = 0; d < previous.Decisions.Count; d++)
            {
                CandidateDecision decision = previous.Decisions[d];
                if (dirtyLookup.Contains(decision.Target.Value)
                    || !snapshot.TryGetTarget(decision.Target, out _))
                {
                    continue;
                }

                decisions.Add(decision);
            }
        }

        /// <summary>Canonical text of one incremental outcome, for evidence files and diagnostics (P-052).</summary>
        public static string TraceText(IncrementalDerivationOutcome outcome)
        {
            if (outcome == null)
            {
                throw new ArgumentNullException(nameof(outcome));
            }

            StringBuilder text = new StringBuilder();
            text.Append(outcome.Describe()).Append('\n');
            text.Append("change=").Append(outcome.Invalidation.ChangeSet.Describe()).Append('\n');
            text.Append("result=").Append(outcome.Result.ResultHash.ToHex()).Append('\n');
            text.Append("tests=").Append(outcome.Result.Assemblies.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append('/').Append(outcome.Result.Decisions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append('/').Append(outcome.Result.Explanations.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append('\n');
            return text.ToString();
        }
    }
}
