// GameCore.Planning — the state-policy executor (GC-015).
//
// Normative sources: docs/game-core/00-core-protocols.md P-020 (reconfiguration preserves mutable state; a reset
// happens only through an explicit registered disposition), P-022 (temporary storage is a hard configured limit),
// P-029 ("copy only state slots needed for migration into bounded scratch storage; run pure fallible migrations
// there *before* the first live write. A preparation/migration failure releases staged leases in reverse dependency
// order and leaves the old assembly intact"), P-032 (every slot's init/version-change/owner-transfer/last-support
// policy; `Preserve` is the compatibility default; `Reset` needs an explicit reason; the final support loss follows
// the declared policy) and 05 s4 (`StateDisposition`, `PlannedMigration`).
//
// The executor is the one place a slot policy becomes a concrete decision. It is pure: it reads declared policies and
// *copies* of live values, and its only side effect is on the caller-owned bounded scratch. A refusal leaves the
// assembly and its state exactly as they were because nothing live is ever touched here:
//
//   * `Preserve` / `RetainDormant` write no value at all;
//   * `RemoveDerived` and `TransferTo` are applied by the publication's apply stage, not here;
//   * `Migrate` and `Reset` stage their resulting value on scratch, and a refused stage releases every reservation
//     this execution made, in reverse order.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Planning.Ownership;

namespace GameCore.Planning.StatePolicies
{
    /// <summary>What the executor decided for one state slot, with the policy that authorized it (P-032).</summary>
    public sealed class StatePolicyDecision
    {
        internal StatePolicyDecision(
            StateSlotKey live,
            StateSlotKey destination,
            StatePolicyIntent intent,
            StateDispositionKind kind,
            uint fromVersion,
            uint toVersion,
            FactoryKey policyKey,
            string reason,
            bool writesValue,
            bool declaredLastSupportTransfer,
            DiagnosticCode code,
            string detail)
        {
            Live = live;
            Destination = destination;
            Intent = intent;
            Kind = kind;
            FromVersion = fromVersion;
            ToVersion = toVersion;
            PolicyKey = policyKey;
            Reason = reason ?? string.Empty;
            WritesValue = writesValue;
            DeclaredLastSupportTransfer = declaredLastSupportTransfer;
            Code = code;
            Detail = detail ?? string.Empty;
        }

        /// <summary>The live slot this decision is about.</summary>
        public StateSlotKey Live { get; }

        /// <summary>Key the value lives under after the decision; equal to <see cref="Live"/> unless transferred.</summary>
        public StateSlotKey Destination { get; }

        public StatePolicyIntent Intent { get; }

        /// <summary>Contract projection the publication applies (05 s4).</summary>
        public StateDispositionKind Kind { get; }

        /// <summary>Schema version the live value was read at.</summary>
        public uint FromVersion { get; }

        /// <summary>Schema version the value has after the decision.</summary>
        public uint ToVersion { get; }

        /// <summary>Migration, transfer or initialization policy key this decision used; default when none did.</summary>
        public FactoryKey PolicyKey { get; }

        /// <summary>Explicit reason of a reset; empty otherwise.</summary>
        public string Reason { get; }

        /// <summary>True when the bounded scratch holds the value the apply stage writes.</summary>
        public bool WritesValue { get; }

        /// <summary>
        /// True when this transfer is the final support loss of a slot whose declared last-support policy is
        /// `TransferTo`, rather than an explicit owner transfer (P-032).
        /// </summary>
        public bool DeclaredLastSupportTransfer { get; }

        public DiagnosticCode Code { get; }

        public string Detail { get; }

        public bool Succeeded => Code == DiagnosticCode.None;

        /// <summary>True when this decision changes nothing about the live value.</summary>
        public bool IsPreserve => Intent == StatePolicyIntent.Preserve;

        /// <summary>True when the value moves to another owner (and therefore another storage key).</summary>
        public bool MovesToAnotherOwner => !Destination.Owner.Equals(Live.Owner) || !Destination.Target.Equals(Live.Target);

        public override string ToString()
            => Intent.ToString() + ":" + Live.ToString()
                + (Code == DiagnosticCode.None ? string.Empty : "(" + DiagnosticCodeText.Of(Code) + ")" + " " + Detail);
    }

    /// <summary>
    /// One value this pass staged on bounded scratch: a migrated or reset value the apply stage writes (P-029).
    /// The publication re-stages it into its own scratch account, because the pass's scratch is temporary state of
    /// the planning step, not live storage.
    /// </summary>
    public readonly struct StagedSlotValue
    {
        public readonly StateSlotKey Slot;
        public readonly int Value;

        public StagedSlotValue(StateSlotKey slot, int value)
        {
            Slot = slot;
            Value = value;
        }

        public override string ToString()
            => Slot.ToString() + "=" + Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Result of one execution: the decisions, the contract dispositions and migrations the publication applies, and
    /// the counts a caller reports. A failed execution carries no dispositions and no migrations at all, because an
    /// unconsumed decision list is not a partial publication (P-029, P-030).
    /// </summary>
    public sealed class StatePolicyPlan
    {
        internal StatePolicyPlan(
            bool succeeded,
            DiagnosticCode code,
            string detail,
            IReadOnlyList<StatePolicyDecision>? decisions,
            IReadOnlyList<StateDisposition>? dispositions,
            IReadOnlyList<PlannedMigration>? migrations,
            IReadOnlyList<StagedSlotValue>? stagedValues,
            ulong scratchHighWaterBytes)
        {
            Succeeded = succeeded;
            Code = code;
            Detail = detail ?? string.Empty;
            Decisions = decisions ?? (IReadOnlyList<StatePolicyDecision>)Array.Empty<StatePolicyDecision>();
            Dispositions = dispositions ?? (IReadOnlyList<StateDisposition>)Array.Empty<StateDisposition>();
            Migrations = migrations ?? (IReadOnlyList<PlannedMigration>)Array.Empty<PlannedMigration>();
            StagedValues = stagedValues ?? (IReadOnlyList<StagedSlotValue>)Array.Empty<StagedSlotValue>();
            ScratchHighWaterBytes = scratchHighWaterBytes;

            for (int i = 0; i < Decisions.Count; i++)
            {
                switch (Decisions[i].Intent)
                {
                    case StatePolicyIntent.Preserve:
                        PreservedCount++;
                        break;
                    case StatePolicyIntent.PreserveDormant:
                        RetainedDormantCount++;
                        break;
                    case StatePolicyIntent.RemoveDerived:
                        RemovedDerivedCount++;
                        break;
                    case StatePolicyIntent.TransferTo:
                        TransferredCount++;
                        break;
                    case StatePolicyIntent.Migrate:
                        MigratedCount++;
                        break;
                    case StatePolicyIntent.Reset:
                        ResetCount++;
                        break;
                    default:
                        break;
                }

                if (!Decisions[i].Succeeded)
                {
                    RefusedCount++;
                }
            }
        }

        /// <summary>
        /// A refused execution with no decisions at all: what a caller reports when the revision's policy surface
        /// could not even be built, so no slot was examined (P-032).
        /// </summary>
        public static StatePolicyPlan Refused(DiagnosticCode code, string detail)
            => new StatePolicyPlan(false, code, detail, null, null, null, null, 0UL);

        public bool Succeeded { get; }

        public DiagnosticCode Code { get; }

        public string Detail { get; }

        /// <summary>Every decision this execution attempted, in canonical live-slot order (P-008).</summary>
        public IReadOnlyList<StatePolicyDecision> Decisions { get; }

        /// <summary>The dispositions the publication applies; empty when the execution refused (P-029).</summary>
        public IReadOnlyList<StateDisposition> Dispositions { get; }

        /// <summary>The version changes the publication runs on scratch; empty when the execution refused (P-032).</summary>
        public IReadOnlyList<PlannedMigration> Migrations { get; }

        /// <summary>Values this pass staged (migrations and resets), so the publication re-stages them (P-029).</summary>
        public IReadOnlyList<StagedSlotValue> StagedValues { get; }

        /// <summary>Reads the value this pass staged for one slot; false when it staged none for it.</summary>
        public bool TryGetStagedValue(StateSlotKey slot, out int value)
        {
            for (int i = 0; i < StagedValues.Count; i++)
            {
                if (StagedValues[i].Slot.Equals(slot))
                {
                    value = StagedValues[i].Value;
                    return true;
                }
            }

            value = 0;
            return false;
        }

        /// <summary>Peak temporary storage this execution used, so its cost is measurable (P-022).</summary>
        public ulong ScratchHighWaterBytes { get; }

        public int PreservedCount { get; private set; }

        public int RetainedDormantCount { get; private set; }

        public int RemovedDerivedCount { get; private set; }

        public int TransferredCount { get; private set; }

        public int MigratedCount { get; private set; }

        public int ResetCount { get; private set; }

        public int RefusedCount { get; private set; }

        /// <summary>
        /// True when at least one decision changes live storage, i.e. the publication is an effective change even if no
        /// binding row moved (P-006).
        /// </summary>
        public bool HasEffectiveDisposition
        {
            get
            {
                for (int i = 0; i < Decisions.Count; i++)
                {
                    if (!Decisions[i].IsPreserve)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public override string ToString()
            => (Succeeded ? "state policies applied" : "state policies refused")
                + ": preserved=" + PreservedCount.ToString(CultureInfo.InvariantCulture)
                + ", dormant=" + RetainedDormantCount.ToString(CultureInfo.InvariantCulture)
                + ", removed=" + RemovedDerivedCount.ToString(CultureInfo.InvariantCulture)
                + ", transferred=" + TransferredCount.ToString(CultureInfo.InvariantCulture)
                + ", migrated=" + MigratedCount.ToString(CultureInfo.InvariantCulture)
                + ", reset=" + ResetCount.ToString(CultureInfo.InvariantCulture)
                + (Code == DiagnosticCode.None ? string.Empty : "(" + DiagnosticCodeText.Of(Code) + "): " + Detail);
    }

    /// <summary>Executes the declared state policies of one revision over copies of the live values (GC-015).</summary>
    public static class StatePolicyExecutor
    {
        /// <summary>
        /// Executes one policy pass. Every live slot of the revision produces exactly one decision; a caller-supplied
        /// request overrides the compatibility default for its slot, and a request for a slot that is not live is
        /// refused unless it is a `Reset` of declared state (which may create the row).
        /// </summary>
        /// <param name="policies">Declared policies of this catalog revision (never inferred from live storage).</param>
        /// <param name="liveSlots">Canonically ordered copies of the live values (P-029: copies, not writes).</param>
        /// <param name="requests">Explicit intents by slot; null means every slot takes the compatibility default.</param>
        /// <param name="migrations">Registered migration handlers of this revision (P-032).</param>
        /// <param name="declaredMigrations">The declaration-level registry GC-007's validator checks against.</param>
        /// <param name="initialValues">Registered initialization policies a `Reset` reads its value from (P-032).</param>
        /// <param name="scratch">Bounded temporary storage the plan owns (P-022, P-029).</param>
        public static StatePolicyPlan Execute(
            SlotStatePolicySet policies,
            IReadOnlyList<LiveSlotState>? liveSlots,
            IReadOnlyList<StatePolicyRequest>? requests,
            MigrationRegistry migrations,
            ISlotMigrationRegistry? declaredMigrations,
            IInitializationPolicyRegistry? initialValues,
            MigrationScratch scratch)
        {
            if (policies == null)
            {
                throw new ArgumentNullException(nameof(policies));
            }

            if (migrations == null)
            {
                throw new ArgumentNullException(nameof(migrations));
            }

            if (scratch == null)
            {
                throw new ArgumentNullException(nameof(scratch));
            }

            var liveKeys = new List<StateSlotKey>();
            if (liveSlots != null)
            {
                for (int i = 0; i < liveSlots.Count; i++)
                {
                    liveKeys.Add(liveSlots[i].Slot);
                }
            }

            var context = new ExecutionContext(
                policies, liveKeys, requests, migrations, declaredMigrations, initialValues, scratch);
            if (liveSlots != null)
            {
                for (int i = 0; i < liveSlots.Count; i++)
                {
                    LiveSlotState live = liveSlots[i];
                    if (!context.TryDecide(live, out StatePolicyDecision? decision, out DiagnosticCode code, out string detail)
                        || decision == null)
                    {
                        return context.Refuse(code, detail, decision);
                    }
                }
            }

            if (!context.DecideRemainingRequests())
            {
                return context.Refuse(context.LastCode, context.LastDetail, context.LastRefusal);
            }

            return context.Complete();
        }

        /// <summary>
        /// The compatibility default of one live slot with no explicit request: an unchanged schema is `Preserve`, a
        /// disposable derived slot losing its last support is `RemoveDerived`, and anything else crosses the version
        /// through the declared migration (P-032).
        /// </summary>
        public static StatePolicyRequest DefaultRequestFor(SlotStatePolicy policy, LiveSlotState live)
        {
            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            if (live.SchemaVersion == policy.Schema.Version)
            {
                return StatePolicyRequest.Preserve(live.Slot);
            }

            if (policy.LastSupport == LastSupportPolicy.RemoveDerived && policy.Options.DisposableDerived)
            {
                return StatePolicyRequest.RemoveDerived(live.Slot);
            }

            return StatePolicyRequest.Migrate(live.Slot, policy.VersionChangePolicy);
        }

        private sealed class ExecutionContext
        {
            private readonly SlotStatePolicySet policies;
            private readonly List<StateSlotKey> liveKeys;
            private readonly List<StatePolicyRequest>? requests;
            private readonly bool[] consumedRequests;
            private readonly MigrationRegistry migrations;
            private readonly ISlotMigrationRegistry? declaredMigrations;
            private readonly IInitializationPolicyRegistry? initialValues;
            private readonly MigrationScratch scratch;
            private readonly List<StatePolicyDecision> decisions = new List<StatePolicyDecision>();
            private readonly List<StateDisposition> dispositions = new List<StateDisposition>();
            private readonly List<PlannedMigration> plannedMigrations = new List<PlannedMigration>();
            private readonly List<StateSlotKey> staged = new List<StateSlotKey>();

            /// <summary>Refusal this context last produced, so a caller can report it without re-deriving it.</summary>
            internal DiagnosticCode LastCode { get; private set; } = DiagnosticCode.None;

            internal string LastDetail { get; private set; } = string.Empty;

            internal StatePolicyDecision? LastRefusal { get; private set; }

            internal ExecutionContext(
                SlotStatePolicySet policies,
                List<StateSlotKey> liveKeys,
                IReadOnlyList<StatePolicyRequest>? requests,
                MigrationRegistry migrations,
                ISlotMigrationRegistry? declaredMigrations,
                IInitializationPolicyRegistry? initialValues,
                MigrationScratch scratch)
            {
                this.policies = policies;
                this.liveKeys = liveKeys;
                this.requests = requests == null ? null : new List<StatePolicyRequest>(requests);
                consumedRequests = new bool[this.requests == null ? 0 : this.requests.Count];
                this.migrations = migrations;
                this.declaredMigrations = declaredMigrations;
                this.initialValues = initialValues;
                this.scratch = scratch;
            }

            internal bool TryDecide(
                LiveSlotState live,
                out StatePolicyDecision? decision,
                out DiagnosticCode code,
                out string detail)
            {
                decision = null;
                if (!policies.TryFind(live.Slot, out SlotStatePolicy? policy, out code, out detail) || policy == null)
                {
                    detail = "live state slot " + live.Slot.ToString()
                        + " is not declared by this catalog revision; a missing compatible policy is a validation"
                        + " error, never implicit zero initialization (P-032).";
                    return false;
                }

                StatePolicyRequest request = TakeRequest(live.Slot) ?? DefaultRequestFor(policy, live);
                decision = Decide(policy, live, request, out code, out detail);
                decisions.Add(decision);
                if (!decision.Succeeded)
                {
                    return false;
                }

                Project(decision, live);
                return true;
            }

            /// <summary>
            /// A request whose slot has no live row. Only a `Reset` of declared state may create the row; every other
            /// intent would claim a change to storage that does not exist.
            /// </summary>
            internal bool DecideRemainingRequests()
            {
                if (requests == null)
                {
                    return true;
                }

                for (int i = 0; i < requests.Count; i++)
                {
                    if (consumedRequests[i])
                    {
                        continue;
                    }

                    StatePolicyRequest request = requests[i];
                    if (request.Intent != StatePolicyIntent.Reset)
                    {
                        // Every other intent changes state that already exists, so a slot with no live row cannot
                        // satisfy it: the plan was prepared against another set of live slots (P-028, P-032).
                        return Fail(
                            DiagnosticCode.StalePlan,
                            "the state-policy request " + request.ToString()
                            + " names a slot that holds no live state, so there is nothing for it to change; the plan"
                            + " was prepared against another set of live slots (P-028).",
                            null);
                    }

                    if (!liveKeys.Contains(request.Slot))
                    {
                        // A declared reset with no live row is the one intent that may create the row, because the
                        // declared initialization policy supplies its value rather than any existing state (P-032).
                        if (!TryDecideNewRow(request, out StatePolicyDecision? created, out DiagnosticCode createCode, out string createDetail)
                            || created == null)
                        {
                            return Fail(createCode, createDetail, created);
                        }
                    }

                    consumedRequests[i] = true;
                }

                return true;
            }

            /// <summary>
            /// Resets one declared slot that has no live row yet: the initialization policy it reads is the declared
            /// one, so the new row is initialized by policy rather than by an implicit zero (05 s3, P-032).
            /// </summary>
            private bool TryDecideNewRow(
                StatePolicyRequest request,
                out StatePolicyDecision? decision,
                out DiagnosticCode code,
                out string detail)
            {
                decision = null;
                if (!policies.TryFind(request.Slot, out SlotStatePolicy? policy, out code, out detail) || policy == null)
                {
                    return false;
                }

                var live = new LiveSlotState(request.Slot, 0U, 0);
                decision = DecideReset(policy, live, request, out code, out detail);
                decisions.Add(decision);
                if (!decision.Succeeded)
                {
                    return false;
                }

                liveKeys.Add(request.Slot);
                Project(decision, live);
                return true;
            }

            private bool Fail(DiagnosticCode code, string detail, StatePolicyDecision? refused)
            {
                LastCode = code;
                LastDetail = detail;
                LastRefusal = refused;
                return false;
            }

            /// <summary>Executes one request against one live slot; every rule is the declaration's (P-032).</summary>
            internal StatePolicyDecision Decide(
                SlotStatePolicy policy,
                LiveSlotState live,
                StatePolicyRequest request,
                out DiagnosticCode code,
                out string detail)
            {
                code = DiagnosticCode.None;
                detail = string.Empty;
                SlotPolicyResult declared = SlotPolicyValidator.ValidateDeclaration(policy.Declaration);
                if (!declared.Succeeded)
                {
                    code = declared.Code;
                    detail = declared.Detail;
                    return Deny(policy, live, request, code, detail);
                }

                switch (request.Intent)
                {
                    case StatePolicyIntent.Preserve:
                        // Reconfiguration changes effective configuration only; mutable state is never reset (P-020).
                        return Allow(policy, live, StatePolicyIntent.Preserve, StateDispositionKind.Retain,
                            policy.Schema.Version, default(FactoryKey), string.Empty, false);

                    case StatePolicyIntent.PreserveDormant:
                        return DecideLastSupport(
                            policy,
                            live,
                            request,
                            LastSupportPolicy.PreserveDormant,
                            SlotPolicyOutcome.RetainDormant,
                            StateDispositionKind.RetainDormant,
                            out code,
                            out detail);

                    case StatePolicyIntent.RemoveDerived:
                        return DecideLastSupport(
                            policy,
                            live,
                            request,
                            LastSupportPolicy.RemoveDerived,
                            SlotPolicyOutcome.RemoveDerived,
                            StateDispositionKind.Retract,
                            out code,
                            out detail);

                    case StatePolicyIntent.Migrate:
                        return DecideMigration(policy, live, request, out code, out detail);

                    case StatePolicyIntent.Reset:
                        return DecideReset(policy, live, request, out code, out detail);

                    case StatePolicyIntent.TransferTo:
                        return DecideTransfer(policy, live, request, out code, out detail);

                    default:
                        code = DiagnosticCode.UnsupportedVersion;
                        detail = "unknown state-policy intent " + request.Intent.ToString();
                        return Deny(policy, live, request, code, detail);
                }
            }

            internal StatePolicyPlan Complete()
            {
                var stagedValues = new List<StagedSlotValue>(staged.Count);
                for (int i = 0; i < staged.Count; i++)
                {
                    if (scratch.TryRead(staged[i], out int value))
                    {
                        stagedValues.Add(new StagedSlotValue(staged[i], value));
                    }
                }

                return new StatePolicyPlan(
                    true,
                    DiagnosticCode.None,
                    string.Empty,
                    decisions,
                    dispositions,
                    plannedMigrations,
                    stagedValues,
                    scratch.HighWaterBytes);
            }

            /// <summary>
            /// A refused execution releases exactly what it staged, in reverse reservation order, and reports no
            /// dispositions at all: the old assembly keeps its state and keeps running (P-029).
            /// </summary>
            internal StatePolicyPlan Refuse(DiagnosticCode code, string detail, StatePolicyDecision? refused)
            {
                for (int i = staged.Count - 1; i >= 0; i--)
                {
                    scratch.Release(staged[i]);
                }

                staged.Clear();
                dispositions.Clear();
                plannedMigrations.Clear();
                return new StatePolicyPlan(
                    false,
                    code,
                    detail.Length != 0
                        ? detail
                        : (refused != null ? refused.Detail : "the state-policy execution was refused"),
                    decisions,
                    null,
                    null,
                    null,
                    scratch.HighWaterBytes);
            }

            private StatePolicyDecision DecideLastSupport(
                SlotStatePolicy policy,
                LiveSlotState live,
                StatePolicyRequest request,
                LastSupportPolicy appliedPolicy,
                SlotPolicyOutcome expected,
                StateDispositionKind kind,
                out DiagnosticCode code,
                out string detail)
            {
                // P-032: the declared last-support policy is the only legal one, so the request states which policy it
                // is applying and the validator checks it against the declaration.
                SlotPolicyResult result = SlotPolicyValidator.Validate(
                    policy.Declaration,
                    SlotPolicyRequest.LastSupportLoss(appliedPolicy, default(TargetId)),
                    declaredMigrations);
                code = result.Code;
                detail = result.Detail;
                if (!result.Succeeded || result.Outcome != expected)
                {
                    if (code == DiagnosticCode.None)
                    {
                        code = DiagnosticCode.OwnershipConflict;
                    }

                    detail = "state slot " + policy.SlotId.ToString() + " declares last-support policy "
                        + policy.LastSupport.ToString() + " and the request applies " + appliedPolicy.ToString()
                        + "; the declared policy is the only legal one (P-032). " + detail;
                    return Deny(policy, live, request, code, detail);
                }

                return Allow(policy, live, request.Intent, kind, policy.Schema.Version, policy.Declaration.TransferPolicy, string.Empty, false);
            }

            private StatePolicyDecision DecideMigration(
                SlotStatePolicy policy,
                LiveSlotState live,
                StatePolicyRequest request,
                out DiagnosticCode code,
                out string detail)
            {
                if (live.SchemaVersion == policy.Schema.Version)
                {
                    // The state is already at the declared version: the request asks for a change that is not needed,
                    // and re-running a migration over its own output would be a rewrite, not a version change (P-032).
                    code = DiagnosticCode.None;
                    detail = string.Empty;
                    return Allow(policy, live, StatePolicyIntent.Preserve, StateDispositionKind.Retain,
                        live.SchemaVersion, default(FactoryKey), string.Empty, false);
                }

                if (live.SchemaVersion > policy.Schema.Version)
                {
                    // The live value is ahead of the declared schema: a downgrade is not a migration, and silently
                    // preserving it would claim compatibility the declaration does not have (P-032).
                    code = DiagnosticCode.UnsupportedVersion;
                    detail = "slot " + live.Slot.ToString() + " holds schema version "
                        + live.SchemaVersion.ToString(CultureInfo.InvariantCulture) + " but the declaration owns "
                        + policy.Schema.Version.ToString(CultureInfo.InvariantCulture)
                        + "; no registered migration moves state backwards (P-032).";
                    return Deny(policy, live, request, code, detail);
                }

                if (!policy.HasVersionChangePolicy)
                {
                    code = DiagnosticCode.MigrationRequired;
                    detail = "slot " + live.Slot.ToString() + " is at schema version "
                        + live.SchemaVersion.ToString(CultureInfo.InvariantCulture) + " and must become "
                        + policy.Schema.Version.ToString(CultureInfo.InvariantCulture)
                        + ", but the descriptor declares no version-change migration policy for it (P-032).";
                    return Deny(policy, live, request, code, detail);
                }

                FactoryKey migrationKey = policy.VersionChangePolicy;
                var target = new SchemaRef(policy.Schema.Id, policy.Schema.Version);

                // The validator checks the transition from the *live* schema version to the declared one, so a pass
                // over version-1 state is a real migration request and not a no-op from the destination to itself.
                var source = new SlotAuthorityDeclaration(
                    policy.SlotId,
                    policy.Owner,
                    new SchemaRef(policy.Schema.Id, live.SchemaVersion),
                    policy.Declaration.PhysicalLayoutKey,
                    policy.Declaration.FieldOwnership,
                    policy.Declaration.LastSupport,
                    policy.Declaration.TransferPolicy,
                    policy.Declaration.MigrationKeys,
                    policy.Options);
                SlotPolicyResult validated = SlotPolicyValidator.Validate(
                    source,
                    SlotPolicyRequest.VersionChange(target, migrationKey),
                    declaredMigrations);
                if (!validated.Succeeded || validated.Outcome != SlotPolicyOutcome.Migrate)
                {
                    code = validated.Code == DiagnosticCode.None ? DiagnosticCode.MigrationRequired : validated.Code;
                    detail = "the declared version change of slot " + live.Slot.ToString() + " resolved to "
                        + validated.Outcome.ToString() + ": " + validated.Detail;
                    return Deny(policy, live, request, code, detail);
                }

                if (!scratch.TryMigrate(
                        live.Slot,
                        migrationKey,
                        live.SchemaVersion,
                        live.Value,
                        migrations,
                        out MigrationOutcome outcome))
                {
                    code = outcome.Code == DiagnosticCode.None ? DiagnosticCode.MigrationRequired : outcome.Code;
                    detail = "migration " + migrationKey.ToString() + " rejected slot " + live.Slot.ToString()
                        + " (" + DiagnosticCodeText.Of(code) + "); the old assembly keeps its state and keeps running"
                        + " (P-029).";
                    return Deny(policy, live, request, code, detail);
                }

                staged.Add(live.Slot);
                return Allow(policy, live, StatePolicyIntent.Migrate, StateDispositionKind.Migrate,
                    policy.Schema.Version, migrationKey, string.Empty, true);
            }

            private StatePolicyDecision DecideReset(
                SlotStatePolicy policy,
                LiveSlotState live,
                StatePolicyRequest request,
                out DiagnosticCode code,
                out string detail)
            {
                SlotPolicyResult validated = SlotPolicyValidator.Validate(
                    policy.Declaration,
                    SlotPolicyRequest.Reset(request.Reason),
                    declaredMigrations);
                if (!validated.Succeeded || validated.Outcome != SlotPolicyOutcome.Reset)
                {
                    code = validated.Code == DiagnosticCode.None ? DiagnosticCode.OwnershipConflict : validated.Code;
                    detail = "state slot " + policy.SlotId.ToString()
                        + " cannot be reset: " + validated.Detail;
                    return Deny(policy, live, request, code, detail);
                }

                if (!policy.HasInitializationPolicy)
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "state slot " + policy.SlotId.ToString()
                        + " declares no initialization policy, so a reset has no declared value to write (P-032).";
                    return Deny(policy, live, request, code, detail);
                }

                if (initialValues == null
                    || !initialValues.TryGetInitialValue(policy.InitializationPolicy, policy.Schema, out int initial))
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "initialization policy " + policy.InitializationPolicy.ToString() + " of state slot "
                        + policy.SlotId.ToString()
                        + " has no registered value; a reset never substitutes an implicit zero (P-032).";
                    return Deny(policy, live, request, code, detail);
                }

                if (!scratch.TryStage(live.Slot, initial, out code))
                {
                    detail = "the reset of state slot " + live.Slot.ToString()
                        + " could not reserve bounded scratch (" + DiagnosticCodeText.Of(code) + "); temporary storage"
                        + " is a hard configured limit (P-022).";
                    return Deny(policy, live, request, code, detail);
                }

                staged.Add(live.Slot);
                return Allow(policy, live, StatePolicyIntent.Reset, StateDispositionKind.Reset,
                    policy.Schema.Version, policy.InitializationPolicy, request.Reason, true);
            }

            private StatePolicyDecision DecideTransfer(
                SlotStatePolicy policy,
                LiveSlotState live,
                StatePolicyRequest request,
                out DiagnosticCode code,
                out string detail)
            {
                OwnerTransferResult transfer = OwnerTransferValidator.Validate(
                    policy,
                    live.Slot,
                    request.DestinationOwner,
                    request.DestinationTarget.Value.IsDefault ? live.Slot.Target : request.DestinationTarget,
                    request.DeclaredLastSupportTransfer,
                    policies,
                    declaredMigrations);
                code = transfer.Code;
                detail = transfer.Detail;
                if (!transfer.Succeeded)
                {
                    return Deny(policy, live, request, code, detail);
                }

                for (int i = 0; i < liveKeys.Count; i++)
                {
                    if (liveKeys[i].Equals(transfer.Destination))
                    {
                        code = DiagnosticCode.OwnershipConflict;
                        detail = "the transfer destination " + transfer.Destination.ToString()
                            + " already holds live state, so the transfer would leave two owners claiming one slot"
                            + " (P-034).";
                        return Deny(policy, live, request, code, detail);
                    }
                }

                return Allow(
                    policy,
                    live,
                    StatePolicyIntent.TransferTo,
                    StateDispositionKind.Transfer,
                    policy.Schema.Version,
                    policy.Declaration.TransferPolicy,
                    string.Empty,
                    false,
                    transfer.Destination,
                    request.DeclaredLastSupportTransfer);
            }

            private StatePolicyRequest? TakeRequest(StateSlotKey live)
            {
                if (requests == null)
                {
                    return null;
                }

                for (int i = 0; i < requests.Count; i++)
                {
                    if (consumedRequests[i] || !requests[i].Slot.Equals(live))
                    {
                        continue;
                    }

                    consumedRequests[i] = true;
                    return requests[i];
                }

                return null;
            }

            private void Project(StatePolicyDecision decision, LiveSlotState live)
            {
                dispositions.Add(new StateDisposition(
                    decision.Live,
                    decision.Kind,
                    decision.Destination.Target.Equals(decision.Live.Target) ? default(TargetId) : decision.Destination.Target,
                    decision.Kind == StateDispositionKind.Migrate ? decision.PolicyKey : default(FactoryKey),
                    decision.Destination.Owner));

                if (decision.Kind == StateDispositionKind.Migrate)
                {
                    plannedMigrations.Add(new PlannedMigration(
                        live.Slot.Target,
                        live.Slot,
                        live.SchemaVersion,
                        decision.ToVersion,
                        decision.PolicyKey));
                }
            }

            private static StatePolicyDecision Allow(
                SlotStatePolicy policy,
                LiveSlotState live,
                StatePolicyIntent intent,
                StateDispositionKind kind,
                uint toVersion,
                FactoryKey policyKey,
                string reason,
                bool writesValue,
                StateSlotKey? destination = null,
                bool declaredLastSupportTransfer = false)
                => new StatePolicyDecision(
                    live.Slot,
                    destination ?? live.Slot,
                    intent,
                    kind,
                    live.SchemaVersion,
                    toVersion,
                    policyKey,
                    reason,
                    writesValue,
                    declaredLastSupportTransfer,
                    DiagnosticCode.None,
                    string.Empty);

            private static StatePolicyDecision Deny(
                SlotStatePolicy policy,
                LiveSlotState live,
                StatePolicyRequest request,
                DiagnosticCode code,
                string detail)
                => new StatePolicyDecision(
                    live.Slot,
                    live.Slot,
                    request.Intent,
                    StateDispositionKind.Retain,
                    live.SchemaVersion,
                    policy.Schema.Version,
                    default(FactoryKey),
                    request.Reason,
                    false,
                    request.DeclaredLastSupportTransfer,
                    code,
                    detail);
        }
    }
}
