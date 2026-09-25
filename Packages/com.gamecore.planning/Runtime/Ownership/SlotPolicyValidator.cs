// GameCore.Planning — slot disposition policy validation (GC-007).
//
// Normative sources: docs/game-core/00-core-protocols.md P-020, P-028, P-032, P-052 and
// docs/game-core/05-contracts-and-data-model.md s4 (`SlotDelta`, `StateDisposition`).
//
// Every state slot declares its initialization, configuration-update, version-change, owner-transfer and
// last-support policies. Existing state defaults to Preserve when the owner and schema stay compatible; a version
// change needs a registered migration; `Reset` needs an explicit reason; the final support loss must follow the
// declared policy. This validator therefore has two levels on purpose:
//
//   * `ValidateDeclaration` checks that the declared policies are complete and internally consistent. It needs no
//     migration executor, which is what lets an early slice validate a plan before GCs that implement migration
//     executors exist.
//   * `Validate` checks one concrete disposition request against that declaration plus whatever migration executors
//     are registered. A version change whose declared key has no registered executor is `MigrationRequired`, named,
//     never an exception and never an implicit reset.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Planning.Ownership
{
    /// <summary>Lookup helpers over a slot declaration list, shared by the support and policy validators.</summary>
    public static class SlotAuthoritySet
    {
        /// <summary>
        /// Finds the declaration of one state-slot key and checks that its owner agrees with the key (P-034). A key
        /// whose owner differs from the declaration is an ownership conflict, not a missing policy.
        /// </summary>
        public static bool TryFindFor(
            IReadOnlyList<SlotAuthorityDeclaration>? slots,
            StateSlotKey key,
            out SlotAuthorityDeclaration? declaration,
            out DiagnosticCode code,
            out string detail)
        {
            declaration = null;
            if (slots != null)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    if (!slots[i].SlotId.Equals(key.Slot))
                    {
                        continue;
                    }

                    if (!slots[i].Owner.Equals(key.Owner))
                    {
                        code = DiagnosticCode.OwnershipConflict;
                        detail = "state slot key " + key + " names owner " + key.Owner
                            + " but the declaration owns it as " + slots[i].Owner + " (P-034)";
                        return false;
                    }

                    declaration = slots[i];
                    code = DiagnosticCode.None;
                    detail = string.Empty;
                    return true;
                }
            }

            code = DiagnosticCode.MissingDependency;
            detail = "no slot declaration covers " + key
                + "; a missing compatible policy is a validation error, not implicit initialization (P-032)";
            return false;
        }
    }

    /// <summary>The kind of change a plan is asking to apply to one slot (P-032).</summary>
    public enum SlotChangeKind
    {
        /// <summary>Reconfiguration: the effective configuration changes, mutable state does not (P-020).</summary>
        ConfigurationUpdate = 0,

        /// <summary>A schema/version change that needs a registered migration (P-032).</summary>
        SchemaVersionChange = 1,

        /// <summary>An explicit reset, legal only when the declaration supports it with a reason (P-032).</summary>
        Reset = 2,

        /// <summary>An owner transfer to a named available owner (P-032).</summary>
        OwnerTransfer = 3,

        /// <summary>The last support of a slot was removed and its declared policy applies (P-032, P-033).</summary>
        LastSupportLoss = 4,
    }

    /// <summary>One concrete disposition request against a declared slot (05 s4 `StateDisposition`).</summary>
    public readonly struct SlotPolicyRequest
    {
        public readonly SlotChangeKind Kind;

        /// <summary>Target schema of a version change; default when the request does not change a schema.</summary>
        public readonly SchemaRef ToSchema;

        /// <summary>Migration key the proposal selects; default means the proposal selected none.</summary>
        public readonly FactoryKey MigrationKey;

        /// <summary>Named destination owner of a transfer, or the target the state moves to.</summary>
        public readonly TargetId TransferTo;

        /// <summary>The last-support policy the caller is applying; must equal the declared policy (P-032).</summary>
        public readonly LastSupportPolicy AppliedLastSupport;

        /// <summary>Explicit reason of a reset; empty means no reason was recorded.</summary>
        public readonly string Reason;

        public SlotPolicyRequest(
            SlotChangeKind kind,
            SchemaRef toSchema,
            FactoryKey migrationKey,
            TargetId transferTo,
            LastSupportPolicy appliedLastSupport,
            string? reason)
        {
            Kind = kind;
            ToSchema = toSchema;
            MigrationKey = migrationKey;
            TransferTo = transferTo;
            AppliedLastSupport = appliedLastSupport;
            Reason = reason ?? string.Empty;
        }

        public static SlotPolicyRequest ConfigurationUpdate()
            => new SlotPolicyRequest(SlotChangeKind.ConfigurationUpdate, default(SchemaRef), default(FactoryKey), default(TargetId), LastSupportPolicy.RemoveDerived, null);

        public static SlotPolicyRequest VersionChange(SchemaRef toSchema, FactoryKey migrationKey)
            => new SlotPolicyRequest(SlotChangeKind.SchemaVersionChange, toSchema, migrationKey, default(TargetId), LastSupportPolicy.RemoveDerived, null);

        public static SlotPolicyRequest Reset(string? reason)
            => new SlotPolicyRequest(SlotChangeKind.Reset, default(SchemaRef), default(FactoryKey), default(TargetId), LastSupportPolicy.RemoveDerived, reason);

        public static SlotPolicyRequest OwnerTransfer(TargetId transferTo)
            => new SlotPolicyRequest(SlotChangeKind.OwnerTransfer, default(SchemaRef), default(FactoryKey), transferTo, LastSupportPolicy.TransferTo, null);

        public static SlotPolicyRequest LastSupportLoss(LastSupportPolicy appliedPolicy, TargetId transferTo)
            => new SlotPolicyRequest(SlotChangeKind.LastSupportLoss, default(SchemaRef), default(FactoryKey), transferTo, appliedPolicy, null);

        public override string ToString() => Kind.ToString();
    }

    /// <summary>Registered migration executors, keyed by declared migration key and schema pair (P-032, P-054).</summary>
    public interface ISlotMigrationRegistry
    {
        bool IsRegistered(FactoryKey migrationKey, SchemaRef from, SchemaRef to);
    }

    /// <summary>In-memory migration registry used by tests and by an early slice with no executors at all.</summary>
    public sealed class SlotMigrationRegistry : ISlotMigrationRegistry
    {
        private readonly List<Registration> registrations = new List<Registration>();

        public int Count => registrations.Count;

        /// <summary>Registers one executor. A repeated identical registration coalesces; it is never a second path.</summary>
        public void Register(FactoryKey migrationKey, SchemaRef from, SchemaRef to)
        {
            if (migrationKey.RegistrationKey.IsDefault)
            {
                throw new ArgumentException("A migration key cannot be a default zero key (P-032).", nameof(migrationKey));
            }

            for (int i = 0; i < registrations.Count; i++)
            {
                Registration existing = registrations[i];
                if (existing.Key.Equals(migrationKey) && existing.From.Id.Value.Equals(from.Id.Value) && existing.To.Id.Value.Equals(to.Id.Value))
                {
                    return;
                }
            }

            registrations.Add(new Registration(migrationKey, from, to));
        }

        public bool IsRegistered(FactoryKey migrationKey, SchemaRef from, SchemaRef to)
        {
            for (int i = 0; i < registrations.Count; i++)
            {
                Registration registration = registrations[i];
                if (registration.Key.Equals(migrationKey)
                    && registration.From.Id.Value.Equals(from.Id.Value)
                    && registration.From.Version == from.Version
                    && registration.To.Id.Value.Equals(to.Id.Value)
                    && registration.To.Version == to.Version)
                {
                    return true;
                }
            }

            return false;
        }

        private readonly struct Registration
        {
            internal readonly FactoryKey Key;
            internal readonly SchemaRef From;
            internal readonly SchemaRef To;

            internal Registration(FactoryKey key, SchemaRef from, SchemaRef to)
            {
                Key = key;
                From = from;
                To = to;
            }
        }
    }

    /// <summary>What the validated request resolves to.</summary>
    public enum SlotPolicyOutcome
    {
        /// <summary>The declaration itself is complete; no disposition was requested.</summary>
        Validated = 0,

        /// <summary>Reconfiguration keeps mutable state; nothing is reset (P-020).</summary>
        PreserveRuntimeState = 1,

        /// <summary>A registered migration is applied to bounded scratch state (P-032).</summary>
        Migrate = 2,

        /// <summary>An explicitly permitted reset with its recorded reason (P-032).</summary>
        Reset = 3,

        /// <summary>The state moves to a named available owner (P-032).</summary>
        Transfer = 4,

        /// <summary>Disposable derived state is removed (P-032, P-033).</summary>
        RemoveDerived = 5,

        /// <summary>Persistent state stays in ECS, dormant and excluded from active queries (P-032).</summary>
        RetainDormant = 6,

        /// <summary>The request is refused; nothing was applied.</summary>
        Rejected = 7,
    }

    /// <summary>Result of validating one declaration or one disposition request.</summary>
    public sealed class SlotPolicyResult
    {
        internal SlotPolicyResult(SlotId slot, SlotPolicyOutcome outcome, DiagnosticCode code, string detail)
        {
            Slot = slot;
            Outcome = outcome;
            Code = code;
            Detail = detail ?? string.Empty;
        }

        public SlotId Slot { get; }

        public SlotPolicyOutcome Outcome { get; }

        /// <summary><see cref="DiagnosticCode.None"/> when the request was accepted.</summary>
        public DiagnosticCode Code { get; }

        public string Detail { get; }

        public bool Succeeded => Code == DiagnosticCode.None && Outcome != SlotPolicyOutcome.Rejected;

        public override string ToString()
            => Outcome + ":" + Slot.ToString() + (Code == DiagnosticCode.None ? string.Empty : "(" + DiagnosticCodeText.Of(Code) + ")");
    }

    /// <summary>Pure slot-policy validation (P-020, P-032, P-033).</summary>
    public static class SlotPolicyValidator
    {
        /// <summary>
        /// Validates the declared policies of one slot with no migration executor and no request involved: complete
        /// policy keys, a transfer policy when the declaration says `TransferTo`, an explicit reason when it permits
        /// a reset, and a last-support policy that can actually apply to this kind of state.
        /// </summary>
        public static SlotPolicyResult ValidateDeclaration(SlotAuthorityDeclaration slot)
        {
            if (slot == null)
            {
                throw new ArgumentNullException(nameof(slot));
            }

            if (slot.SlotId.Value.IsDefault)
            {
                return Reject(slot.SlotId, DiagnosticCode.MissingDependency, "a default zero state-slot id is not a catalog identity");
            }

            if (slot.Owner.Value.IsDefault)
            {
                return Reject(slot.SlotId, DiagnosticCode.MissingDependency, "state slot " + slot.SlotId + " declares a default zero owner (P-034)");
            }

            if (slot.Schema.Id.Value.IsDefault)
            {
                return Reject(slot.SlotId, DiagnosticCode.MissingDependency, "state slot " + slot.SlotId + " declares a default zero schema");
            }

            if (slot.Options.ResetPermitted && string.IsNullOrEmpty(slot.Options.ResetReason))
            {
                return Reject(
                    slot.SlotId,
                    DiagnosticCode.OwnershipConflict,
                    "state slot " + slot.SlotId + " permits a reset without recording the explicit reason P-032 requires");
            }

            for (int i = 0; i < slot.MigrationKeys.Count; i++)
            {
                if (slot.MigrationKeys[i].RegistrationKey.IsDefault)
                {
                    return Reject(slot.SlotId, DiagnosticCode.MissingDependency, "state slot " + slot.SlotId + " declares a default zero migration key");
                }
            }

            switch (slot.LastSupport)
            {
                case LastSupportPolicy.RemoveDerived:
                    if (!slot.Options.DisposableDerived)
                    {
                        return Reject(
                            slot.SlotId,
                            DiagnosticCode.Ineligible,
                            "state slot " + slot.SlotId
                            + " declares RemoveDerived for state that is not disposable derived data; persistent state declares PreserveDormant or TransferTo (P-032)");
                    }

                    break;

                case LastSupportPolicy.PreserveDormant:
                    if (!slot.Options.PreserveDormantPermitted)
                    {
                        return Reject(
                            slot.SlotId,
                            DiagnosticCode.Ineligible,
                            "state slot " + slot.SlotId
                            + " declares PreserveDormant but the declaration does not permit dormant retention (P-032)");
                    }

                    break;

                case LastSupportPolicy.TransferTo:
                    if (!slot.HasTransferPolicy)
                    {
                        return Reject(
                            slot.SlotId,
                            DiagnosticCode.MissingDependency,
                            "state slot " + slot.SlotId + " declares TransferTo without a registered owner-transfer policy (P-032)");
                    }

                    break;

                default:
                    return Reject(slot.SlotId, DiagnosticCode.UnsupportedVersion, "state slot " + slot.SlotId + " declares an unknown last-support policy");
            }

            return new SlotPolicyResult(slot.SlotId, SlotPolicyOutcome.Validated, DiagnosticCode.None, string.Empty);
        }

        /// <summary>
        /// Validates one disposition request against a declared slot. The declaration check runs first, so an
        /// incomplete policy set is reported as such instead of being masked by the request.
        /// </summary>
        public static SlotPolicyResult Validate(
            SlotAuthorityDeclaration slot,
            SlotPolicyRequest request,
            ISlotMigrationRegistry? migrations)
        {
            SlotPolicyResult declared = ValidateDeclaration(slot);
            if (!declared.Succeeded)
            {
                return declared;
            }

            switch (request.Kind)
            {
                case SlotChangeKind.ConfigurationUpdate:
                    // Reconfiguration changes effective configuration only; it never resets mutable state (P-020).
                    return new SlotPolicyResult(slot.SlotId, SlotPolicyOutcome.PreserveRuntimeState, DiagnosticCode.None, string.Empty);

                case SlotChangeKind.SchemaVersionChange:
                    return ValidateVersionChange(slot, request, migrations);

                case SlotChangeKind.Reset:
                    if (!slot.Options.ResetPermitted)
                    {
                        return Reject(
                            slot.SlotId,
                            DiagnosticCode.OwnershipConflict,
                            "state slot " + slot.SlotId + " does not declare a permitted reset; an unpermitted reset is never implicit (P-032)");
                    }

                    if (string.IsNullOrEmpty(request.Reason))
                    {
                        return Reject(
                            slot.SlotId,
                            DiagnosticCode.OwnershipConflict,
                            "a reset of state slot " + slot.SlotId + " requires an explicit proposal reason (P-032)");
                    }

                    return new SlotPolicyResult(slot.SlotId, SlotPolicyOutcome.Reset, DiagnosticCode.None, string.Empty);

                case SlotChangeKind.OwnerTransfer:
                    return ValidateOwnerTransfer(slot, request);

                case SlotChangeKind.LastSupportLoss:
                    return ValidateLastSupportLoss(slot, request);

                default:
                    return Reject(slot.SlotId, DiagnosticCode.UnsupportedVersion, "unknown slot change kind " + request.Kind);
            }
        }

        private static SlotPolicyResult ValidateVersionChange(
            SlotAuthorityDeclaration slot,
            SlotPolicyRequest request,
            ISlotMigrationRegistry? migrations)
        {
            if (request.ToSchema.Id.Value.IsDefault)
            {
                return Reject(slot.SlotId, DiagnosticCode.MissingDependency, "a schema/version change must name its target schema (P-032)");
            }

            if (!request.ToSchema.Id.Value.Equals(slot.Schema.Id.Value))
            {
                return Reject(
                    slot.SlotId,
                    DiagnosticCode.UnsupportedVersion,
                    "target schema " + request.ToSchema + " is a different identity than " + slot.Schema
                    + "; a schema identity change is a new slot declaration, not a migration (P-032)");
            }

            if (request.ToSchema.Version == slot.Schema.Version)
            {
                // A compatible schema with an unchanged version keeps existing state by default (P-032).
                return new SlotPolicyResult(slot.SlotId, SlotPolicyOutcome.PreserveRuntimeState, DiagnosticCode.None, string.Empty);
            }

            FactoryKey key = request.MigrationKey;
            if (key.RegistrationKey.IsDefault)
            {
                return Reject(
                    slot.SlotId,
                    DiagnosticCode.MigrationRequired,
                    "state slot " + slot.SlotId + " changes schema version " + slot.Schema.Version + " -> "
                    + request.ToSchema.Version + " but the proposal named no migration key (P-032)");
            }

            if (!DeclaresMigration(slot, key))
            {
                return Reject(
                    slot.SlotId,
                    DiagnosticCode.MigrationRequired,
                    "state slot " + slot.SlotId + " does not declare migration key " + key
                    + "; only a declared, registered migration may move state across versions (P-032)");
            }

            if (migrations == null || !migrations.IsRegistered(key, slot.Schema, request.ToSchema))
            {
                return Reject(
                    slot.SlotId,
                    DiagnosticCode.MigrationRequired,
                    "migration " + key + " for " + slot.Schema + " -> " + request.ToSchema
                    + " is declared but has no registered executor; the plan cannot be applied (P-032, P-052)");
            }

            return new SlotPolicyResult(slot.SlotId, SlotPolicyOutcome.Migrate, DiagnosticCode.None, string.Empty);
        }

        private static SlotPolicyResult ValidateOwnerTransfer(SlotAuthorityDeclaration slot, SlotPolicyRequest request)
        {
            if (slot.LastSupport != LastSupportPolicy.TransferTo)
            {
                return Reject(
                    slot.SlotId,
                    DiagnosticCode.OwnershipConflict,
                    "state slot " + slot.SlotId + " declares last-support policy " + slot.LastSupport
                    + "; an owner transfer requires the declared TransferTo policy (P-032)");
            }

            if (!slot.HasTransferPolicy)
            {
                return Reject(slot.SlotId, DiagnosticCode.MissingDependency, "state slot " + slot.SlotId + " declares no registered owner-transfer policy (P-032)");
            }

            if (request.TransferTo.Value.IsDefault)
            {
                return Reject(slot.SlotId, DiagnosticCode.MissingDependency, "an owner transfer of state slot " + slot.SlotId + " must name its destination (P-032)");
            }

            return new SlotPolicyResult(slot.SlotId, SlotPolicyOutcome.Transfer, DiagnosticCode.None, string.Empty);
        }

        private static SlotPolicyResult ValidateLastSupportLoss(SlotAuthorityDeclaration slot, SlotPolicyRequest request)
        {
            if (request.AppliedLastSupport != slot.LastSupport)
            {
                return Reject(
                    slot.SlotId,
                    DiagnosticCode.OwnershipConflict,
                    "the request applies last-support policy " + request.AppliedLastSupport
                    + " but state slot " + slot.SlotId + " declares " + slot.LastSupport
                    + "; the declared policy is the only legal one (P-032)");
            }

            switch (slot.LastSupport)
            {
                case LastSupportPolicy.RemoveDerived:
                    if (!slot.Options.DisposableDerived)
                    {
                        return Reject(
                            slot.SlotId,
                            DiagnosticCode.Ineligible,
                            "state slot " + slot.SlotId + " declares RemoveDerived for state that is not disposable derived data (P-032)");
                    }

                    return new SlotPolicyResult(slot.SlotId, SlotPolicyOutcome.RemoveDerived, DiagnosticCode.None, string.Empty);

                case LastSupportPolicy.PreserveDormant:
                    if (!slot.Options.PreserveDormantPermitted)
                    {
                        return Reject(
                            slot.SlotId,
                            DiagnosticCode.Ineligible,
                            "state slot " + slot.SlotId + " declares PreserveDormant but the declaration does not permit dormant retention (P-032)");
                    }

                    return new SlotPolicyResult(slot.SlotId, SlotPolicyOutcome.RetainDormant, DiagnosticCode.None, string.Empty);

                case LastSupportPolicy.TransferTo:
                    if (!slot.HasTransferPolicy)
                    {
                        return Reject(slot.SlotId, DiagnosticCode.MissingDependency, "state slot " + slot.SlotId + " declares TransferTo without a registered owner-transfer policy (P-032)");
                    }

                    if (request.TransferTo.Value.IsDefault)
                    {
                        return Reject(slot.SlotId, DiagnosticCode.MissingDependency, "the final support loss of state slot " + slot.SlotId + " must name the owner that takes over (P-047)");
                    }

                    return new SlotPolicyResult(slot.SlotId, SlotPolicyOutcome.Transfer, DiagnosticCode.None, string.Empty);

                default:
                    return Reject(slot.SlotId, DiagnosticCode.UnsupportedVersion, "state slot " + slot.SlotId + " declares an unknown last-support policy");
            }
        }

        private static bool DeclaresMigration(SlotAuthorityDeclaration slot, FactoryKey key)
        {
            for (int i = 0; i < slot.MigrationKeys.Count; i++)
            {
                if (slot.MigrationKeys[i].Equals(key))
                {
                    return true;
                }
            }

            return false;
        }

        private static SlotPolicyResult Reject(SlotId slot, DiagnosticCode code, string detail)
            => new SlotPolicyResult(slot, SlotPolicyOutcome.Rejected, code, detail);
    }
}
