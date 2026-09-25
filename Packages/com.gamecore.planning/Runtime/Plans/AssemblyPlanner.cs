// GameCore.Planning — `AssemblyPlanner`: one immutable proposal plus one frozen ownership/stage descriptor become
// one concrete, hashable change plan (GC-008).
//
// Normative sources: 00 P-002 (the planner is a pure consumer of immutable snapshots and performs no live ECS
// write), P-017/P-018/P-019 (contribution identity, precedence, per-slot policies and conflicts), P-021 (stratum
// and bounded output slots), P-024 (a target's effective assembly is complete before it is visible), P-027/P-028
// (plan contents, expected-revision validation, semantic hash) and 05 s4 (the plan/result shapes this file fills).
//
// What the planner decides, in order:
//   1. the descriptor is validated, so a plan never encodes an ambiguous ownership or execution graph;
//   2. the proposal was declared against the revision/epoch the caller planned from, else `StalePlan` (P-028);
//   3. eligibility: every mount's declarations apply to the targets whose recipe they name (P-013, P-015);
//   4. per contribution identity, candidates are ranked by P-018's order; an `Exclusive`/`Incompatible` slot with
//      two candidates rejects with `CapabilityConflict` and the conflicting providers as the witness, and a losing
//      candidate stays provenance rather than becoming active support;
//   5. retractions remove exactly the unmounting provider's support (P-033);
//   6. state dispositions and required migrations are computed per slot (P-032), the execution order is compiled
//      from the descriptor (P-040), and the whole plan is hashed from semantic inputs only.
//
// The plan is returned in the `Prepared` state when it is valid: resources are staged behind closed gates
// (P-029) but nothing is visible, and `AssemblyPublisher` owns everything from the apply fence onward.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Planning
{
    /// <summary>
    /// One plan plus the concrete operations a publisher applies. The plan itself is the contract DTO (05 s4); this
    /// type is the engine-free projection of it that the Unity adapter consumes: which binding rows to install or
    /// retract, which state slots to migrate, which schedule to install and which derivation rules stay active.
    /// </summary>
    public sealed class PlannedPublication
    {
        public PlannedPublication(
            ChangePlan plan,
            PlanStateMachine state,
            ContentHash descriptorRevision,
            CompiledSchedule schedule,
            IReadOnlyList<TargetBindingRow>? installs,
            IReadOnlyList<TargetBindingRow>? removals,
            TargetBindingTable after,
            IReadOnlyList<DerivedBindingRule>? rules,
            IReadOnlyList<TargetId>? affectedTargets,
            IReadOnlyList<OwnerGrant>? ownerGrants,
            IReadOnlyList<StateDisposition>? dispositions,
            IReadOnlyList<PlannedMigration>? migrations,
            InertAcquisitionSet acquisitions,
            MigrationScratch scratch)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            State = state ?? throw new ArgumentNullException(nameof(state));
            DescriptorRevision = descriptorRevision;
            Schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
            Installs = ContractCollections.Freeze(installs);
            Removals = ContractCollections.Freeze(removals);
            After = after ?? throw new ArgumentNullException(nameof(after));
            Rules = ContractCollections.Freeze(rules);
            AffectedTargets = ContractCollections.Freeze(affectedTargets);
            OwnerGrants = ContractCollections.Freeze(ownerGrants);
            Dispositions = ContractCollections.Freeze(dispositions);
            Migrations = ContractCollections.Freeze(migrations);
            Acquisitions = acquisitions ?? throw new ArgumentNullException(nameof(acquisitions));
            Scratch = scratch ?? throw new ArgumentNullException(nameof(scratch));
        }

        public ChangePlan Plan { get; }

        /// <summary>Plan states; the planner leaves a valid plan `Prepared` and an invalid one terminal (P-027).</summary>
        public PlanStateMachine State { get; }

        public ContentHash DescriptorRevision { get; }

        public CompiledSchedule Schedule { get; }

        /// <summary>Binding rows this publication installs or replaces, in canonical order (P-017).</summary>
        public IReadOnlyList<TargetBindingRow> Installs { get; }

        /// <summary>Binding rows this publication retracts, identified by contribution identity (P-033).</summary>
        public IReadOnlyList<TargetBindingRow> Removals { get; }

        /// <summary>The effective binding table after this publication (what the publisher exposes).</summary>
        public TargetBindingTable After { get; }

        /// <summary>Derivation rules active after this publication; a future spawn derives from these (P-024).</summary>
        public IReadOnlyList<DerivedBindingRule> Rules { get; }

        public IReadOnlyList<TargetId> AffectedTargets { get; }

        public IReadOnlyList<OwnerGrant> OwnerGrants { get; }

        public IReadOnlyList<StateDisposition> Dispositions { get; }

        public IReadOnlyList<PlannedMigration> Migrations { get; }

        public InertAcquisitionSet Acquisitions { get; }

        public MigrationScratch Scratch { get; }

        public bool IsRejected => State.Phase == PlanPhase.Rejected || State.Phase == PlanPhase.Cancelled;

        /// <summary>True when the plan is validated and prepared, i.e. the publisher may consider applying it.</summary>
        public bool IsPrepared => State.Phase == PlanPhase.Prepared;

        /// <summary>Targets whose effective binding set changes; used by the publisher's fence and diagnostics.</summary>
        public int ChangedTargetCount => AffectedTargets.Count;

        public override string ToString() =>
            State.Describe() + "; installs=" + Installs.Count.ToString(CultureInfo.InvariantCulture)
            + "; removals=" + Removals.Count.ToString(CultureInfo.InvariantCulture)
            + "; targets=" + AffectedTargets.Count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Terminal or fault report of one publication attempt; the outcome distinctions are load-bearing (05 s4).</summary>
    public sealed class PublicationRecord
    {
        public PublicationRecord(
            OperationId operation,
            Outcome outcome,
            DiagnosticCode code,
            string detail,
            SnapshotToken? publishedSnapshot,
            CompositionRevision oldRevision,
            CompositionRevision newRevision,
            AssemblyEpoch oldEpoch,
            AssemblyEpoch newEpoch,
            IReadOnlyList<Id128>? cleanupReferences,
            IReadOnlyList<Id128>? quarantineReferences)
        {
            Operation = operation;
            Outcome = outcome;
            Code = code;
            Detail = detail ?? string.Empty;
            PublishedSnapshot = publishedSnapshot;
            OldRevision = oldRevision;
            NewRevision = newRevision;
            OldEpoch = oldEpoch;
            NewEpoch = newEpoch;
            CleanupReferences = ContractCollections.Freeze(cleanupReferences);
            QuarantineReferences = ContractCollections.Freeze(quarantineReferences);
        }

        public OperationId Operation { get; }

        public Outcome Outcome { get; }

        public DiagnosticCode Code { get; }

        public string Detail { get; }

        /// <summary>Token of the new image; null unless the publication committed a new epoch (P-030).</summary>
        public SnapshotToken? PublishedSnapshot { get; }

        public CompositionRevision OldRevision { get; }

        public CompositionRevision NewRevision { get; }

        public AssemblyEpoch OldEpoch { get; }

        public AssemblyEpoch NewEpoch { get; }

        /// <summary>Resources a published-with-cleanup-errors result retained (P-048).</summary>
        public IReadOnlyList<Id128> CleanupReferences { get; }

        public IReadOnlyList<Id128> QuarantineReferences { get; }

        /// <summary>True when live writes may have happened: only `Faulted` and `Published*` reach this state (P-031).</summary>
        public bool CrossedLiveWriteBoundary =>
            Outcome == Outcome.Faulted
            || Outcome == Outcome.Published
            || Outcome == Outcome.PublishedWithCleanupErrors;

        public bool Published => Outcome == Outcome.Published || Outcome == Outcome.PublishedWithCleanupErrors;

        public override string ToString() =>
            Outcome.ToString() + (Code == DiagnosticCode.None ? string.Empty : "(" + DiagnosticCodeText.Of(Code) + ")")
            + ": " + Detail;
    }

    /// <summary>
    /// Pure planner of one publication (P-002). Every input is an immutable snapshot; the only side effect is on the
    /// caller-owned scratch and acquisition set, both of which are inert until publication.
    /// </summary>
    public static class AssemblyPlanner
    {
        public static PlannedPublication Build(
            CompositionProposal proposal,
            OwnershipStageDescriptor descriptor,
            CompositionRevision currentRevision,
            AssemblyEpoch currentEpoch,
            TargetBindingTable current,
            IReadOnlyList<DerivedBindingRule>? currentRules,
            IReadOnlyList<TargetDefinition>? targets,
            IReadOnlyList<LiveSlotState>? liveSlots,
            MigrationRegistry migrations,
            MigrationScratch scratch,
            InertAcquisitionSet acquisitions,
            PlanBudget budget)
        {
            if (proposal == null)
            {
                throw new ArgumentNullException(nameof(proposal));
            }

            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            if (migrations == null)
            {
                throw new ArgumentNullException(nameof(migrations));
            }

            if (scratch == null)
            {
                throw new ArgumentNullException(nameof(scratch));
            }

            if (acquisitions == null)
            {
                throw new ArgumentNullException(nameof(acquisitions));
            }

            if (budget == null)
            {
                throw new ArgumentNullException(nameof(budget));
            }

            IReadOnlyList<TargetDefinition> definitions = targets ?? Array.Empty<TargetDefinition>();
            IReadOnlyList<LiveSlotState> slots = liveSlots ?? Array.Empty<LiveSlotState>();
            IReadOnlyList<DerivedBindingRule> rules = currentRules ?? Array.Empty<DerivedBindingRule>();

            // 1. A descriptor that does not validate is a planning failure, not an apply failure (P-028).
            if (!descriptor.TryValidate(out DiagnosticCode descriptorCode, out string descriptorDetail))
            {
                return Reject(proposal, descriptor, current, rules, acquisitions, scratch, budget, descriptorCode, descriptorDetail);
            }

            // 2. The proposal must have been declared against the snapshot the caller planned from (P-028).
            if (!proposal.ExpectedRevision.Equals(currentRevision) || !proposal.BaseEpoch.Equals(currentEpoch))
            {
                return Reject(
                    proposal,
                    descriptor,
                    current,
                    rules,
                    acquisitions,
                    scratch,
                    budget,
                    DiagnosticCode.StalePlan,
                    "the proposal was declared against revision "
                    + proposal.ExpectedRevision.Value.ToString(CultureInfo.InvariantCulture)
                    + "/epoch " + proposal.BaseEpoch.Value.ToString(CultureInfo.InvariantCulture)
                    + " but the snapshot is at revision "
                    + currentRevision.Value.ToString(CultureInfo.InvariantCulture)
                    + "/epoch " + currentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                    + "; regenerate with a new operation id (P-028).");
            }

            var installs = new List<TargetBindingRow>();
            var removals = new List<TargetBindingRow>();
            var addedContributions = new List<ContributionKey>();
            var removedContributions = new List<ContributionKey>();
            var changedContributions = new List<ContributionKey>();
            var supports = new List<SupportRecord>();
            var explanations = new List<ExplanationReference>();
            var affected = new List<TargetId>();
            var rulesAfter = new Dictionary<string, DerivedBindingRule>(StringComparer.Ordinal);

            // 3. Retract exactly the unmounting providers' support (P-033). A provider's rows are found by identity,
            //    so a retraction can never delete another plugin's contribution or the base recipe's component.
            for (int i = 0; i < proposal.Unmounts.Count; i++)
            {
                ProposedUnmount unmount = proposal.Unmounts[i];
                for (int r = 0; r < current.Rows.Count; r++)
                {
                    TargetBindingRow row = current.Rows[r];
                    if (!row.Provider.Equals(unmount.Provider))
                    {
                        continue;
                    }

                    removals.Add(row);
                    removedContributions.Add(new ContributionKey(
                        row.Provider,
                        new RuleId(row.Capability.Value),
                        row.Target,
                        row.Capability,
                        row.OutputSlot));
                    AddAffected(affected, row.Target);
                }
            }

            for (int i = 0; i < rules.Count; i++)
            {
                bool retired = false;
                for (int u = 0; u < proposal.Unmounts.Count; u++)
                {
                    if (rules[i].Provider.Equals(proposal.Unmounts[u].Provider))
                    {
                        retired = true;
                        break;
                    }
                }

                if (!retired)
                {
                    rulesAfter[rules[i].RuleIdentity()] = rules[i];
                }
            }

            // 4. Rank every candidate per contribution identity (P-017, P-018) and detect policy conflicts (P-019).
            var candidates = new Dictionary<string, List<Candidate>>();
            var order = new List<string>();
            for (int m = 0; m < proposal.Mounts.Count; m++)
            {
                ProposedMount mount = proposal.Mounts[m];
                for (int c = 0; c < mount.Capabilities.Count; c++)
                {
                    ProposedCapability declared = mount.Capabilities[c];
                    if (declared.Capability.Capability.IsDefault || declared.TargetRecipes.Count == 0)
                    {
                        return Reject(
                            proposal,
                            descriptor,
                            current,
                            rules,
                            acquisitions,
                            scratch,
                            budget,
                            DiagnosticCode.MissingDependency,
                            "a mount declares capability " + declared.Capability.ToString()
                            + " with no stable capability identity or no target recipe (P-004, P-015).");
                    }

                    for (int t = 0; t < definitions.Count; t++)
                    {
                        TargetDefinition definition = definitions[t];
                        if (!declared.AppliesTo(definition))
                        {
                            continue;
                        }

                        string identity = IdentityOf(definition.Target, declared.Capability.Capability, declared.OutputSlot);
                        if (!candidates.TryGetValue(identity, out List<Candidate>? list) || list == null)
                        {
                            list = new List<Candidate>();
                            candidates.Add(identity, list);
                            order.Add(identity);
                        }

                        list.Add(Candidate.FromDeclaration(mount, declared, definition));
                    }
                }
            }

            // 4b. The rows already effective are candidates too. Without them a new declaration would silently
            //     displace a higher-priority provider, which is exactly the precedence rule P-018 forbids.
            for (int r = 0; r < current.Rows.Count; r++)
            {
                TargetBindingRow existingRow = current.Rows[r];
                string identity = IdentityOf(existingRow.Target, existingRow.Capability, existingRow.OutputSlot);
                if (!candidates.TryGetValue(identity, out List<Candidate>? group) || group == null)
                {
                    continue;
                }

                if (!TryFindDefinition(definitions, existingRow.Target, out TargetDefinition known))
                {
                    continue;
                }

                if (!TryFindDeclaration(proposal, existingRow, known, out ProposedCapability? declaredForRow)
                    || declaredForRow == null)
                {
                    continue;
                }

                group.Add(Candidate.FromExistingRow(existingRow, declaredForRow, known));
            }

            for (int i = 0; i < order.Count; i++)
            {
                List<Candidate> group = candidates[order[i]];
                group.Sort(CompareCandidates);
                Candidate winner = group[0];
                if (winner.Declaration.Policy == CompositionPolicy.Exclusive
                    || winner.Declaration.Policy == CompositionPolicy.Incompatible)
                {
                    if (group.Count > 1)
                    {
                        // Priority cannot destroy an exclusive or incompatible capability (P-019).
                        var conflicting = new List<string>();
                        for (int g = 0; g < group.Count; g++)
                        {
                            conflicting.Add(group[g].RankText());
                        }

                        return Reject(
                            proposal,
                            descriptor,
                            current,
                            rules,
                            acquisitions,
                            scratch,
                            budget,
                            DiagnosticCode.CapabilityConflict,
                            "slot " + winner.Declaration.Capability.ToString()
                            + "#" + winner.Declaration.OutputSlot.ToString(CultureInfo.InvariantCulture)
                            + " on target " + winner.Definition.Target.ToString()
                            + " is declared " + winner.Declaration.Policy.ToString()
                            + " but " + group.Count.ToString(CultureInfo.InvariantCulture)
                            + " eligible candidates exist: " + string.Join(", ", conflicting.ToArray())
                            + " (P-019).");
                    }
                }

                // P-019: an `Additive` slot's effective value is what its registered reducer folded over every
                // eligible contribution, and every one of them stays a supporter (P-017) rather than all but the
                // top-ranked candidate being dropped. The planner deliberately does not fold numbers itself — a
                // reducer is contract-owned pure code — so it consumes the composed value and the support set the
                // derivation already produced, and rejects a declaration that carries neither instead of inventing
                // a winner-take-all value.
                int effectiveValue = winner.Value;
                IReadOnlyList<CapabilitySupport> rowSupports = TargetBindingRow.SingleSupport(
                    winner.Provider,
                    winner.ProviderGeneration,
                    winner.Declaration.Rule,
                    winner.Value,
                    winner.Priority);
                if (winner.Declaration.Policy == CompositionPolicy.Additive)
                {
                    if (winner.Declaration.Supporters.Count == 0)
                    {
                        return Reject(
                            proposal,
                            descriptor,
                            current,
                            rules,
                            acquisitions,
                            scratch,
                            budget,
                            DiagnosticCode.MissingDependency,
                            "slot " + winner.Declaration.Capability.ToString()
                            + "#" + winner.Declaration.OutputSlot.ToString(CultureInfo.InvariantCulture)
                            + " on target " + winner.Definition.Target.ToString()
                            + " is declared Additive but carries no support set; the composed value of P-019 is the"
                            + " registered reducer's fold over its contributions, so the planner refuses to publish"
                            + " one candidate's raw value in its place.");
                    }

                    for (int g = 1; g < group.Count; g++)
                    {
                        if (!CapabilitySupport.SetEquals(
                                winner.Declaration.Supporters,
                                group[g].Declaration.Supporters))
                        {
                            return Reject(
                                proposal,
                                descriptor,
                                current,
                                rules,
                                acquisitions,
                                scratch,
                                budget,
                                DiagnosticCode.CapabilityConflict,
                                "two declarations of the Additive slot " + winner.Declaration.Capability.ToString()
                                + "#" + winner.Declaration.OutputSlot.ToString(CultureInfo.InvariantCulture)
                                + " on target " + winner.Definition.Target.ToString()
                                + " carry different support sets; the planner does not fold values itself, so it"
                                + " cannot compose them and will not pick one (P-018, P-019).");
                        }
                    }

                    effectiveValue = winner.Declaration.Value;
                    rowSupports = winner.Declaration.Supporters;
                }

                TargetBindingRow row = new TargetBindingRow(
                    winner.Definition.Target,
                    winner.Declaration.Capability.Capability,
                    winner.Declaration.Capability.Version,
                    winner.Declaration.OutputSlot,
                    effectiveValue,
                    winner.Provider,
                    winner.ProviderGeneration,
                    winner.Priority,
                    winner.Declaration.Schema,
                    rowSupports,
                    winner.Declaration.Rule);

                bool isNewRow = !current.TryGet(
                    winner.Definition.Target,
                    row.Capability,
                    row.OutputSlot,
                    out TargetBindingRow existing);
                if (isNewRow)
                {
                    addedContributions.Add(ContributionKeyOf(row));
                }
                else if (!existing.HasSameContent(row))
                {
                    changedContributions.Add(ContributionKeyOf(row));
                }
                else
                {
                    // Re-deriving the same effective row is not a change: the target keeps its row and the plan stays
                    // a no-op for it, so a repeated proposal publishes no new epoch (P-006).
                    supports.Add(new SupportRecord(
                        new StateSlotKey(
                            winner.Definition.Target,
                            OwnerOf(descriptor, winner.Declaration),
                            SlotOf(descriptor, winner.Declaration)),
                        winner.Provider,
                        winner.Declaration.Rule,
                        winner.Declaration.Capability.Capability));
                    AddRule(rulesAfter, winner, row);
                    continue;
                }

                installs.Add(row);
                supports.Add(new SupportRecord(
                    new StateSlotKey(winner.Definition.Target, OwnerOf(descriptor, winner.Declaration), SlotOf(descriptor, winner.Declaration)),
                    winner.Provider,
                    winner.Declaration.Rule,
                    winner.Declaration.Capability.Capability));
                explanations.Add(new ExplanationReference(
                    winner.Definition.Target,
                    winner.Declaration.Capability.Capability,
                    ExplanationKeyOf(row)));
                AddAffected(affected, winner.Definition.Target);

                AddRule(rulesAfter, winner, row);
            }

            // 5. State dispositions and required migrations for every slot the descriptor owns (P-032).
            var plannedMigrations = new List<PlannedMigration>();
            var dispositions = new List<StateDisposition>();
            var ownerGrants = new List<OwnerGrant>();
            for (int i = 0; i < descriptor.Slots.Count; i++)
            {
                OwnedSlotSpec spec = descriptor.Slots[i];
                ownerGrants.Add(new OwnerGrant(spec.Owner, new StateSlotKey(default(TargetId), spec.Owner, spec.Slot), spec.OwnerVersion));
            }

            for (int i = 0; i < slots.Count; i++)
            {
                LiveSlotState live = slots[i];
                if (!descriptor.TryGetSlot(live.Slot.Slot, out OwnedSlotSpec? spec) || spec == null)
                {
                    // A live slot the descriptor does not declare cannot be migrated by this revision (P-032).
                    return Reject(
                        proposal,
                        descriptor,
                        current,
                        rules,
                        acquisitions,
                        scratch,
                        budget,
                        DiagnosticCode.MigrationRequired,
                        "live state slot " + live.Slot.ToString()
                        + " is not declared by the descriptor; a missing compatible policy is a validation error (P-032).");
                }

                if (live.SchemaVersion == spec.Schema.Version)
                {
                    dispositions.Add(new StateDisposition(live.Slot, StateDispositionKind.Retain, default(TargetId), default(FactoryKey)));
                    continue;
                }

                if (spec.LastSupport == LastSupportPolicy.RemoveDerived)
                {
                    dispositions.Add(new StateDisposition(live.Slot, StateDispositionKind.Retract, default(TargetId), default(FactoryKey)));
                }
                else
                {
                    dispositions.Add(new StateDisposition(
                        live.Slot,
                        StateDispositionKind.Migrate,
                        default(TargetId),
                        spec.VersionChangePolicy));
                }

                plannedMigrations.Add(new PlannedMigration(
                    live.Slot.Target,
                    live.Slot,
                    live.SchemaVersion,
                    spec.Schema.Version,
                    spec.VersionChangePolicy));
            }

            // 6. Every required migration must resolve to a registered handler whose source version matches, because a
            //    missing compatible policy is a validation error and never implicit zero initialisation (P-032).
            for (int i = 0; i < plannedMigrations.Count; i++)
            {
                PlannedMigration migration = plannedMigrations[i];
                if (migration.MigrationKey.RegistrationKey.IsDefault)
                {
                    // The descriptor declares no version-change policy for this slot; P-032 makes that a validation
                    // error rather than an implicit zero initialisation.
                    return Reject(
                        proposal,
                        descriptor,
                        current,
                        rules,
                        acquisitions,
                        scratch,
                        budget,
                        DiagnosticCode.MigrationRequired,
                        "slot " + migration.Slot.ToString() + " is at schema version "
                        + migration.FromVersion.ToString(CultureInfo.InvariantCulture) + " and must become "
                        + migration.ToVersion.ToString(CultureInfo.InvariantCulture)
                        + ", but the descriptor declares no version-change migration policy for it (P-032).");
                }

                if (!migrations.TryFind(migration.MigrationKey, out ISlotMigration? handler) || handler == null)
                {
                    return Reject(
                        proposal,
                        descriptor,
                        current,
                        rules,
                        acquisitions,
                        scratch,
                        budget,
                        DiagnosticCode.MigrationRequired,
                        "slot " + migration.Slot.ToString() + " requires migration key "
                        + migration.MigrationKey.ToString() + ", which the registry does not register (P-032).");
                }

                if (handler.FromVersion != migration.FromVersion || handler.ToVersion != migration.ToVersion)
                {
                    return Reject(
                        proposal,
                        descriptor,
                        current,
                        rules,
                        acquisitions,
                        scratch,
                        budget,
                        DiagnosticCode.UnsupportedVersion,
                        "slot " + migration.Slot.ToString() + " requires migration "
                        + migration.FromVersion.ToString(CultureInfo.InvariantCulture) + "->"
                        + migration.ToVersion.ToString(CultureInfo.InvariantCulture)
                        + " but the registered handler declares "
                        + handler.FromVersion.ToString(CultureInfo.InvariantCulture) + "->"
                        + handler.ToVersion.ToString(CultureInfo.InvariantCulture)
                        + " (P-032).");
                }
            }

            // 7. Bounded scratch: a plan that cannot reserve its migration storage is rejected before any write.
            for (int i = 0; i < plannedMigrations.Count; i++)
            {
                if (!scratch.TryReserve(plannedMigrations[i].Slot, out DiagnosticCode scratchCode))
                {
                    return Reject(
                        proposal,
                        descriptor,
                        current,
                        rules,
                        acquisitions,
                        scratch,
                        budget,
                        scratchCode,
                        "migration scratch for slot " + plannedMigrations[i].Slot.ToString()
                        + " exceeds the configured temporary-storage budget (P-022).");
                }
            }

            CompiledSchedule schedule = CompileSchedule(descriptor);
            TargetBindingTable after = current.Merge(installs, removals);
            ulong prepareBytes = acquisitions.StagedBytes + scratch.HighWaterBytes;
            ulong applyBytes = (ulong)(installs.Count + removals.Count) * budget.ScratchBytesPerSlot;

            // 8. Hard budgets: the protocol's byte limits are guardrails, and exceeding one rejects the whole plan
            //    with the top fan-out reported instead of publishing a truncated closure (P-022).
            if (prepareBytes > budget.PrepareBytesLimit || applyBytes > budget.ApplyBytesLimit)
            {
                return Reject(
                    proposal,
                    descriptor,
                    current,
                    rules,
                    acquisitions,
                    scratch,
                    budget,
                    DiagnosticCode.BudgetExceeded,
                    "the plan estimates " + prepareBytes.ToString(CultureInfo.InvariantCulture)
                    + " prepare bytes and " + applyBytes.ToString(CultureInfo.InvariantCulture)
                    + " apply bytes against limits "
                    + budget.PrepareBytesLimit.ToString(CultureInfo.InvariantCulture) + " and "
                    + budget.ApplyBytesLimit.ToString(CultureInfo.InvariantCulture)
                    + "; a caller may raise the configured budget explicitly and retry (P-022).");
            }

            var validity = new ValidityAndCost(
                null,
                null,
                new AffectedCounts(
                    affected.Count,
                    proposal.Mounts.Count + proposal.Unmounts.Count,
                    addedContributions.Count,
                    removedContributions.Count,
                    schedule.Entries.Count),
                new PlanCostEstimate(prepareBytes, applyBytes),
                new HardBudgetUsage(budget.PrepareBytesLimit, budget.ApplyBytesLimit, prepareBytes, applyBytes));

            ContentHash planHash = PlanHashOf(
                proposal,
                descriptor,
                installs,
                removals,
                plannedMigrations,
                schedule);

            ChangePlan plan = new ChangePlan(
                proposal.Operation,
                proposal.InputHash,
                currentRevision,
                currentEpoch,
                proposal.CatalogHash,
                planHash,
                CompositionDeltaOf(proposal, descriptor),
                new DerivationDelta(addedContributions, removedContributions, changedContributions, null, supports, explanations),
                new RuntimeDelta(
                    RecipeOperationsOf(affected),
                    LayoutOperationsOf(installs),
                    ownerGrants,
                    dispositions,
                    new ExecutionPlan(NodesOf(schedule), schedule.Edges, schedule.Hash),
                    schedule.Buffers),
                acquisitions.ToStaging(budget.ScratchCapacityBytes, null),
                validity);

            // A validated plan is immediately prepared: the descriptor, the ranking and the bounded scratch account
            // are exactly the staged work P-029 requires, and a plan that failed either step is terminal already.
            var state = new PlanStateMachine(plan);
            state.TryValidate(out _);
            state.TryPrepare(out _);

            return new PlannedPublication(
                plan, state, descriptor.Revision, schedule, installs, removals, after, CanonicalRules(rulesAfter), affected,
                ownerGrants, dispositions, plannedMigrations, acquisitions, scratch);
        }

        /// <summary>
        /// Records the winning candidate as the rule of its `(recipe, scope, capability, output slot)` identity. One
        /// identity holds one rule, so the rule set a future spawn derives from is the effective assembly rather than
        /// an accumulating history (P-017, P-024). The rule carries the row's support set, so a spawned target
        /// inherits the composed value *and* how many contributions it was composed from (P-017, P-019).
        /// </summary>
        private static void AddRule(Dictionary<string, DerivedBindingRule> rules, Candidate winner, TargetBindingRow row)
        {
            var rule = new DerivedBindingRule(
                winner.Definition.Recipe,
                winner.Definition.Scope,
                row.Capability,
                row.CapabilityVersion,
                row.OutputSlot,
                row.Value,
                row.Provider,
                row.ProviderGeneration,
                row.Priority,
                winner.Declaration.Policy,
                row.Schema,
                row.Supports,
                winner.Declaration.Rule);

            rules[rule.RuleIdentity()] = rule;
        }

        /// <summary>
        /// Canonically ordered rule list: identities sorted ordinally, so two plans that derive the same rules emit
        /// the same sequence regardless of declaration order or dictionary enumeration (P-008).
        /// </summary>
        private static IReadOnlyList<DerivedBindingRule> CanonicalRules(Dictionary<string, DerivedBindingRule> rules)
        {
            var identities = new List<string>(rules.Keys);
            identities.Sort(StringComparer.Ordinal);
            var ordered = new List<DerivedBindingRule>(identities.Count);
            for (int i = 0; i < identities.Count; i++)
            {
                ordered.Add(rules[identities[i]]);
            }

            return ordered;
        }

        /// <summary>
        /// Compiles the descriptor's stages and systems into the publication's execution order (P-040): stages in
        /// ascending fence index, systems inside a stage ordered by validated inner edges with ascending key as the
        /// stable tie-break, and the stage's own predecessors inherited by every entry.
        /// </summary>
        public static CompiledSchedule CompileSchedule(OwnershipStageDescriptor descriptor)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            var entries = new List<SystemDispatchEntry>();
            var edges = new List<PlanEdge>();
            int dispatchIndex = 0;

            for (int s = 0; s < descriptor.Stages.Count; s++)
            {
                DescriptorStage stage = descriptor.Stages[s];
                List<DescriptorSystem> ordered = OrderSystems(stage, out DiagnosticCode code, out string detail);
                if (ordered.Count != stage.Systems.Count)
                {
                    throw new InvalidOperationException(
                        "the descriptor's stage " + stage.Stage.ToString() + " has no valid inner order: " + detail + " (" + code + ")");
                }

                for (int i = 0; i < ordered.Count; i++)
                {
                    DescriptorSystem system = ordered[i];
                    entries.Add(new SystemDispatchEntry(stage.Stage, system.Key, system.Kind, dispatchIndex));
                    dispatchIndex++;
                }

                for (int p = 0; p < stage.PredecessorStages.Count; p++)
                {
                    int predecessor = stage.PredecessorStages[p];
                    if (predecessor < 0 || predecessor >= descriptor.Stages.Count)
                    {
                        continue;
                    }

                    edges.Add(new PlanEdge(
                        PlanNode.ForStage(descriptor.Stages[predecessor].Stage),
                        PlanNode.ForStage(stage.Stage),
                        true));
                }
            }

            var hashBuilder = new StringBuilder();
            hashBuilder.Append("schedule\n");
            for (int i = 0; i < entries.Count; i++)
            {
                SystemDispatchEntry entry = entries[i];
                hashBuilder.Append("entry=").Append(PlanHashing.IdText(entry.Stage.Value)).Append(';')
                    .Append(PlanHashing.IdText(entry.SystemKey.RegistrationKey)).Append(';')
                    .Append(entry.SystemKey.KeyVersion.ToString(CultureInfo.InvariantCulture)).Append(';')
                    .Append(entry.Kind.ToString()).Append(';')
                    .Append(entry.DispatchIndex.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }

            for (int i = 0; i < descriptor.Buffers.Count; i++)
            {
                BufferBinding binding = descriptor.Buffers[i];
                hashBuilder.Append("buffer=").Append(PlanHashing.IdText(binding.Buffer.Value)).Append(';')
                    .Append(PlanHashing.IdText(binding.ConsumerStage.Value)).Append('\n');
            }

            return new CompiledSchedule(entries, descriptor.Buffers, edges, PlanHashing.Of(hashBuilder.ToString()));
        }

        /// <summary>
        /// Canonical inner order of one stage (P-040): explicit required edges must be acyclic, and otherwise ready
        /// systems are emitted by ascending registration key so registration timing never decides gameplay order.
        /// </summary>
        public static List<DescriptorSystem> OrderSystems(DescriptorStage stage, out DiagnosticCode code, out string detail)
        {
            if (stage == null)
            {
                throw new ArgumentNullException(nameof(stage));
            }

            var remaining = new List<DescriptorSystem>(stage.Systems.Count);
            for (int i = 0; i < stage.Systems.Count; i++)
            {
                remaining.Add(stage.Systems[i]);
            }

            var ordered = new List<DescriptorSystem>(remaining.Count);
            while (remaining.Count != 0)
            {
                DescriptorSystem? ready = null;
                for (int i = 0; i < remaining.Count; i++)
                {
                    if (!HasUnemittedDependency(remaining[i], remaining))
                    {
                        if (ready == null || remaining[i].Key.RegistrationKey.CompareTo(ready.Key.RegistrationKey) < 0)
                        {
                            ready = remaining[i];
                        }
                    }
                }

                if (ready == null)
                {
                    code = DiagnosticCode.Cycle;
                    detail = "stage " + stage.Stage.ToString()
                        + " declares inner system edges that cannot be satisfied; a cycle must reject with its witness (P-040).";
                    return ordered;
                }

                ordered.Add(ready);
                remaining.Remove(ready);
            }

            code = DiagnosticCode.None;
            detail = string.Empty;
            return ordered;
        }

        private static bool HasUnemittedDependency(DescriptorSystem system, List<DescriptorSystem> remaining)
        {
            for (int i = 0; i < system.RequiredAfter.Count; i++)
            {
                for (int r = 0; r < remaining.Count; r++)
                {
                    if (remaining[r].Key.Equals(system.RequiredAfter[i]))
                    {
                        // The dependency has not been emitted yet, so this system is not ready.
                        return true;
                    }
                }
            }

            return false;
        }

        private static PlannedPublication Reject(
            CompositionProposal proposal,
            OwnershipStageDescriptor descriptor,
            TargetBindingTable current,
            IReadOnlyList<DerivedBindingRule> rules,
            InertAcquisitionSet acquisitions,
            MigrationScratch scratch,
            PlanBudget budget,
            DiagnosticCode code,
            string detail)
        {
            ContentHash planHash = PlanHashOf(
                proposal,
                descriptor,
                null,
                null,
                null,
                CompiledSchedule.Empty);

            var validity = new ValidityAndCost(
                new[] { Diagnostic.Create(code, OperationPhase.Planning, proposal.Operation, detail) },
                null,
                new AffectedCounts(0, proposal.Mounts.Count + proposal.Unmounts.Count, 0, 0, 0),
                new PlanCostEstimate(acquisitions.StagedBytes, 0UL),
                new HardBudgetUsage(budget.PrepareBytesLimit, budget.ApplyBytesLimit, acquisitions.StagedBytes, 0UL));

            ChangePlan plan = new ChangePlan(
                proposal.Operation,
                proposal.InputHash,
                proposal.ExpectedRevision,
                proposal.BaseEpoch,
                proposal.CatalogHash,
                planHash,
                CompositionDeltaOf(proposal, descriptor),
                new DerivationDelta(null, null, null, null, null, null),
                new RuntimeDelta(null, null, null, null, null, null),
                acquisitions.ToStaging(budget.ScratchCapacityBytes, null),
                validity);

            var state = new PlanStateMachine(plan);
            state.TryReject(code, detail);
            return new PlannedPublication(
                plan,
                state,
                descriptor.Revision,
                CompiledSchedule.Empty,
                null,
                null,
                current,
                rules,
                null,
                null,
                null,
                null,
                acquisitions,
                scratch);
        }

        private static CompositionDelta CompositionDeltaOf(CompositionProposal proposal, OwnershipStageDescriptor descriptor)
        {
            var installs = new List<InstallEdit>(proposal.Mounts.Count);
            for (int i = 0; i < proposal.Mounts.Count; i++)
            {
                ProposedMount mount = proposal.Mounts[i];
                installs.Add(new InstallEdit(
                    CompositionEditKind.Add,
                    mount.Instance,
                    default(ScopeId),
                    mount.Scope,
                    InstallationState.Registered,
                    InstallationState.Active));
            }

            for (int i = 0; i < proposal.Unmounts.Count; i++)
            {
                ProposedUnmount unmount = proposal.Unmounts[i];
                installs.Add(new InstallEdit(
                    CompositionEditKind.Remove,
                    unmount.Instance,
                    unmount.Scope,
                    default(ScopeId),
                    InstallationState.Active,
                    InstallationState.Retiring));
            }

            return new CompositionDelta(null, installs, null, null, null);
        }

        private static IReadOnlyList<RecipeOperation> RecipeOperationsOf(IReadOnlyList<TargetId> affected)
        {
            var operations = new List<RecipeOperation>(affected.Count);
            for (int i = 0; i < affected.Count; i++)
            {
                operations.Add(new RecipeOperation(CompositionEditKind.Update, affected[i], default(DefinitionRef), default(FactoryKey)));
            }

            return operations;
        }

        private static IReadOnlyList<LayoutOperation> LayoutOperationsOf(IReadOnlyList<TargetBindingRow> installs)
        {
            var operations = new List<LayoutOperation>();
            var seen = new HashSet<string>();
            for (int i = 0; i < installs.Count; i++)
            {
                TargetBindingRow row = installs[i];
                string identity = row.Target.ToString() + "|" + row.Schema.ToString();
                if (seen.Add(identity))
                {
                    operations.Add(new LayoutOperation(CompositionEditKind.Update, row.Target, row.Schema));
                }
            }

            return operations;
        }

        private static IReadOnlyList<PlanNode> NodesOf(CompiledSchedule schedule)
        {
            var nodes = new List<PlanNode>();
            var stages = new HashSet<Id128>();
            for (int i = 0; i < schedule.Entries.Count; i++)
            {
                SystemDispatchEntry entry = schedule.Entries[i];
                if (stages.Add(entry.Stage.Value))
                {
                    nodes.Add(PlanNode.ForStage(entry.Stage));
                }

                nodes.Add(PlanNode.ForSystem(entry.Stage, entry.SystemKey));
            }

            return nodes;
        }

        private static OwnerId OwnerOf(OwnershipStageDescriptor descriptor, ProposedCapability capability)
        {
            // The binding's support record names the slot owner when the descriptor declares one for the capability's
            // schema; otherwise the slot key stays default and the binding is a pure derived value (P-032).
            for (int i = 0; i < descriptor.Slots.Count; i++)
            {
                if (descriptor.Slots[i].Schema.Equals(capability.Schema))
                {
                    return descriptor.Slots[i].Owner;
                }
            }

            return default(OwnerId);
        }

        private static SlotId SlotOf(OwnershipStageDescriptor descriptor, ProposedCapability capability)
        {
            for (int i = 0; i < descriptor.Slots.Count; i++)
            {
                if (descriptor.Slots[i].Schema.Equals(capability.Schema))
                {
                    return descriptor.Slots[i].Slot;
                }
            }

            return default(SlotId);
        }

        private static ContributionKey ContributionKeyOf(TargetBindingRow row) =>
            new ContributionKey(row.Provider, new RuleId(row.Capability.Value), row.Target, row.Capability, row.OutputSlot);

        private static Id128 ExplanationKeyOf(TargetBindingRow row)
        {
            var builder = new StringBuilder();
            builder.Append(PlanHashing.IdText(row.Provider.Value)).Append(';')
                .Append(PlanHashing.IdText(row.Target.Value)).Append(';')
                .Append(PlanHashing.IdText(row.Capability.Value)).Append(';')
                .Append(row.OutputSlot.ToString(CultureInfo.InvariantCulture));
            byte[] bytes = PlanHashing.Of(builder.ToString()).ToArray();
            return Id128Codec.ReadBigEndian(bytes, 0);
        }

        private static ContentHash PlanHashOf(
            CompositionProposal proposal,
            OwnershipStageDescriptor descriptor,
            IReadOnlyList<TargetBindingRow>? installs,
            IReadOnlyList<TargetBindingRow>? removals,
            IReadOnlyList<PlannedMigration>? migrations,
            CompiledSchedule schedule)
        {
            var builder = new StringBuilder();
            builder.Append("plan\n");
            builder.Append("operation=").Append(PlanHashing.IdText(proposal.Operation.World.Session)).Append(';')
                .Append(PlanHashing.IdText(proposal.Operation.IssuerId)).Append(';')
                .Append(proposal.Operation.IssuerSequence.ToString(CultureInfo.InvariantCulture)).Append('\n');
            builder.Append("input=").Append(PlanHashing.HashText(proposal.InputHash)).Append('\n');
            builder.Append("baseRevision=").Append(proposal.ExpectedRevision.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
            builder.Append("baseEpoch=").Append(proposal.BaseEpoch.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
            builder.Append("catalog=").Append(PlanHashing.HashText(proposal.CatalogHash)).Append('\n');
            builder.Append("descriptor=").Append(PlanHashing.HashText(descriptor.Fingerprint())).Append('\n');
            builder.Append("mode=").Append(proposal.Mode.ToString()).Append('\n');

            var mounts = new List<string>(proposal.Mounts.Count);
            for (int i = 0; i < proposal.Mounts.Count; i++)
            {
                ProposedMount mount = proposal.Mounts[i];
                var capabilities = new List<string>(mount.Capabilities.Count);
                for (int c = 0; c < mount.Capabilities.Count; c++)
                {
                    ProposedCapability capability = mount.Capabilities[c];
                    var recipes = new List<string>(capability.TargetRecipes.Count);
                    for (int r = 0; r < capability.TargetRecipes.Count; r++)
                    {
                        recipes.Add(PlanHashing.IdText(capability.TargetRecipes[r].Id.Value)
                            + ":" + PlanHashing.IdText(capability.TargetRecipes[r].Schema.Id.Value));
                    }

                    recipes.Sort(StringComparer.Ordinal);
                    var eligibleTargets = new List<string>(capability.EligibleTargets.Count);
                    for (int t = 0; t < capability.EligibleTargets.Count; t++)
                    {
                        eligibleTargets.Add(PlanHashing.IdText(capability.EligibleTargets[t].Value));
                    }

                    eligibleTargets.Sort(StringComparer.Ordinal);
                    capabilities.Add(
                        PlanHashing.IdText(capability.Rule.Value) + ";"
                        + PlanHashing.IdText(capability.Capability.Capability.Value) + ";"
                        + capability.Capability.Version.ToString(CultureInfo.InvariantCulture) + ";"
                        + capability.OutputSlot.ToString(CultureInfo.InvariantCulture) + ";"
                        + capability.Policy.ToString() + ";"
                        + capability.Value.ToString(CultureInfo.InvariantCulture) + ";"
                        + capability.Priority.ToString(CultureInfo.InvariantCulture) + ";"
                        + PlanHashing.IdText(capability.Schema.Id.Value) + ";"
                        + capability.Schema.Version.ToString(CultureInfo.InvariantCulture) + ";"
                        + string.Join("|", recipes.ToArray()) + ";"
                        + string.Join("|", eligibleTargets.ToArray()));
                }

                capabilities.Sort(StringComparer.Ordinal);
                mounts.Add(
                    PlanHashing.IdText(mount.Instance.Value) + ";"
                    + PlanHashing.IdText(mount.PluginType.Value) + ";"
                    + PlanHashing.IdText(mount.Provider.Value) + ";"
                    + PlanHashing.IdText(mount.Scope.Value) + ";"
                    + mount.ProviderGeneration.ToString(CultureInfo.InvariantCulture) + ";"
                    + string.Join("|", capabilities.ToArray()));
            }

            mounts.Sort(StringComparer.Ordinal);
            for (int i = 0; i < mounts.Count; i++)
            {
                builder.Append("mount=").Append(mounts[i]).Append('\n');
            }

            var unmounts = new List<string>(proposal.Unmounts.Count);
            for (int i = 0; i < proposal.Unmounts.Count; i++)
            {
                unmounts.Add(
                    PlanHashing.IdText(proposal.Unmounts[i].Instance.Value) + ";"
                    + PlanHashing.IdText(proposal.Unmounts[i].Provider.Value) + ";"
                    + PlanHashing.IdText(proposal.Unmounts[i].Scope.Value));
            }

            unmounts.Sort(StringComparer.Ordinal);
            for (int i = 0; i < unmounts.Count; i++)
            {
                builder.Append("unmount=").Append(unmounts[i]).Append('\n');
            }

            AppendRows(builder, "install", installs);
            AppendRows(builder, "removal", removals);

            if (migrations != null)
            {
                var text = new List<string>(migrations.Count);
                for (int i = 0; i < migrations.Count; i++)
                {
                    PlannedMigration migration = migrations[i];
                    text.Add(
                        PlanHashing.IdText(migration.Target.Value) + ";"
                        + PlanHashing.IdText(migration.Slot.Target.Value) + ";"
                        + PlanHashing.IdText(migration.Slot.Owner.Value) + ";"
                        + PlanHashing.IdText(migration.Slot.Slot.Value) + ";"
                        + migration.FromVersion.ToString(CultureInfo.InvariantCulture) + ";"
                        + migration.ToVersion.ToString(CultureInfo.InvariantCulture) + ";"
                        + PlanHashing.IdText(migration.MigrationKey.RegistrationKey) + ";"
                        + migration.MigrationKey.KeyVersion.ToString(CultureInfo.InvariantCulture));
                }

                text.Sort(StringComparer.Ordinal);
                for (int i = 0; i < text.Count; i++)
                {
                    builder.Append("migration=").Append(text[i]).Append('\n');
                }
            }

            builder.Append("schedule=").Append(PlanHashing.HashText(schedule.Hash)).Append('\n');
            return PlanHashing.Of(builder.ToString());
        }

        private static void AppendRows(StringBuilder builder, string prefix, IReadOnlyList<TargetBindingRow>? rows)
        {
            if (rows == null)
            {
                return;
            }

            var text = new List<string>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                TargetBindingRow row = rows[i];
                text.Add(
                    PlanHashing.IdText(row.Target.Value) + ";"
                    + PlanHashing.IdText(row.Capability.Value) + ";"
                    + row.CapabilityVersion.ToString(CultureInfo.InvariantCulture) + ";"
                    + row.OutputSlot.ToString(CultureInfo.InvariantCulture) + ";"
                    + row.Value.ToString(CultureInfo.InvariantCulture) + ";"
                    + PlanHashing.IdText(row.Provider.Value) + ";"
                    + row.ProviderGeneration.ToString(CultureInfo.InvariantCulture) + ";"
                    + row.Priority.ToString(CultureInfo.InvariantCulture) + ";"
                    + PlanHashing.IdText(row.Schema.Id.Value) + ";"
                    + row.Schema.Version.ToString(CultureInfo.InvariantCulture));
            }

            text.Sort(StringComparer.Ordinal);
            for (int i = 0; i < text.Count; i++)
            {
                builder.Append(prefix).Append('=').Append(text[i]).Append('\n');
            }
        }

        private static void AddAffected(List<TargetId> affected, TargetId target)
        {
            for (int i = 0; i < affected.Count; i++)
            {
                if (affected[i].Equals(target))
                {
                    return;
                }
            }

            affected.Add(target);
        }

        private static string IdentityOf(TargetId target, CapabilityId capability, uint outputSlot) =>
            PlanHashing.IdText(target.Value) + "|" + PlanHashing.IdText(capability.Value) + "|"
            + outputSlot.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// P-018's total rank: higher signed priority, then nearer scope (the frozen descriptor has no depth model, so
        /// ascending scope identity stands in for "nearer"), then ascending provider identity, rule identity and
        /// output slot. Registration order and worker timing never take part.
        /// </summary>
        private static int CompareCandidates(Candidate left, Candidate right)
        {
            int priority = right.Priority.CompareTo(left.Priority);
            if (priority != 0)
            {
                return priority;
            }

            int scope = left.Definition.Scope.Value.CompareTo(right.Definition.Scope.Value);
            if (scope != 0)
            {
                return scope;
            }

            int provider = left.Provider.Value.CompareTo(right.Provider.Value);
            if (provider != 0)
            {
                return provider;
            }

            int rule = left.Declaration.Rule.Value.CompareTo(right.Declaration.Rule.Value);
            if (rule != 0)
            {
                return rule;
            }

            return left.Declaration.OutputSlot.CompareTo(right.Declaration.OutputSlot);
        }

        private static bool TryFindDefinition(
            IReadOnlyList<TargetDefinition> definitions,
            TargetId target,
            out TargetDefinition definition)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                if (definitions[i].Target.Equals(target))
                {
                    definition = definitions[i];
                    return true;
                }
            }

            definition = default(TargetDefinition);
            return false;
        }

        /// <summary>
        /// Finds the declaration of this proposal that governs an existing row's slot, which is what gives an
        /// already-effective row its policy and rule identity for ranking (P-018).
        /// </summary>
        private static bool TryFindDeclaration(
            CompositionProposal proposal,
            TargetBindingRow row,
            TargetDefinition definition,
            out ProposedCapability? declared)
        {
            for (int m = 0; m < proposal.Mounts.Count; m++)
            {
                ProposedMount mount = proposal.Mounts[m];
                for (int c = 0; c < mount.Capabilities.Count; c++)
                {
                    ProposedCapability candidate = mount.Capabilities[c];
                    if (candidate.Capability.Capability.Equals(row.Capability)
                        && candidate.OutputSlot == row.OutputSlot
                        && candidate.AppliesTo(definition))
                    {
                        declared = candidate;
                        return true;
                    }
                }
            }

            declared = null;
            return false;
        }

        /// <summary>
        /// One ranked candidate for one contribution identity. A declared candidate carries its manifest values; an
        /// existing candidate carries the row that is currently effective, so a new declaration is ranked against it
        /// rather than silently replacing it.
        /// </summary>
        private sealed class Candidate
        {
            private Candidate(
                ProposedCapability declaration,
                TargetDefinition definition,
                int value,
                int priority,
                ProviderInstallationId provider,
                ulong providerGeneration)
            {
                Declaration = declaration;
                Definition = definition;
                Value = value;
                Priority = priority;
                Provider = provider;
                ProviderGeneration = providerGeneration;
            }

            public ProposedCapability Declaration { get; }

            public TargetDefinition Definition { get; }

            public int Value { get; }

            public int Priority { get; }

            public ProviderInstallationId Provider { get; }

            public ulong ProviderGeneration { get; }

            public static Candidate FromDeclaration(
                ProposedMount mount,
                ProposedCapability declaration,
                TargetDefinition definition) =>
                new Candidate(
                    declaration,
                    definition,
                    declaration.Value,
                    declaration.Priority,
                    mount.Provider,
                    mount.ProviderGeneration);

            public static Candidate FromExistingRow(
                TargetBindingRow row,
                ProposedCapability declaration,
                TargetDefinition definition) =>
                new Candidate(
                    declaration,
                    definition,
                    row.Value,
                    row.Priority,
                    row.Provider,
                    row.ProviderGeneration);

            public string RankText() =>
                "provider=" + PlanHashing.IdText(Provider.Value)
                + " priority=" + Priority.ToString(CultureInfo.InvariantCulture)
                + " value=" + Value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
