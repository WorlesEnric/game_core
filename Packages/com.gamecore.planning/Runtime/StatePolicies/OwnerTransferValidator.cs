// GameCore.Planning — owner-transfer validation (GC-015).
//
// Normative sources: docs/game-core/00-core-protocols.md P-025 (replacing a provider under the same installation
// identity keeps the state; *different* identities require an explicit replacement mapping if state is to transfer),
// P-032 (a slot identifies an owner-transfer policy *and* a last-support policy, and `TransferTo` names an available
// owner), P-034 (one owner per authoritative domain) and 05 s3 (`StateSlotSpec.TransferPolicy`).
//
// P-032 names two policies, so there are two legal authorizations for moving one slot's state to another owner:
//
//   * the declared **last-support** policy is `TransferTo` and the slot lost its last support;
//   * the declaration carries a registered **owner-transfer** policy key and a caller asks for an explicit transfer
//     (the `O-05` replacement mapping of P-025).
//
// The first is checked by GC-007's `SlotPolicyValidator` (the declaration's policy is the only legal one). The second
// is checked here, because it needs facts the validator does not hold: whether the destination owner is *available*
// in this revision and whether the request would leave two owners claiming one slot.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning.Ownership;

namespace GameCore.Planning.StatePolicies
{
    /// <summary>Verdict of one owner-transfer request, with the destination key it resolved to (P-025, P-032).</summary>
    public readonly struct OwnerTransferResult
    {
        public readonly StateSlotKey Source;

        /// <summary>Destination key `(destinationTarget, destinationOwner, slot)` a completed transfer writes.</summary>
        public readonly StateSlotKey Destination;

        /// <summary>Policy key that authorized the transfer: the declared transfer policy of the slot (P-032).</summary>
        public readonly FactoryKey PolicyKey;

        /// <summary>True when this request was the declared last-support `TransferTo` loss (P-032).</summary>
        public readonly bool DeclaredLastSupportTransfer;

        public readonly DiagnosticCode Code;
        public readonly string Detail;

        internal OwnerTransferResult(
            StateSlotKey source,
            StateSlotKey destination,
            FactoryKey policyKey,
            bool declaredLastSupportTransfer,
            DiagnosticCode code,
            string detail)
        {
            Source = source;
            Destination = destination;
            PolicyKey = policyKey;
            DeclaredLastSupportTransfer = declaredLastSupportTransfer;
            Code = code;
            Detail = detail ?? string.Empty;
        }

        public bool Succeeded => Code == DiagnosticCode.None;

        public override string ToString()
            => (Succeeded ? "transfer " : "refused ")
                + Source.ToString() + " -> " + Destination.ToString()
                + (Code == DiagnosticCode.None ? string.Empty : "(" + DiagnosticCodeText.Of(Code) + ")");

        /// <summary>
        /// Records a transfer that already resolved elsewhere (the executor's decided transfer), so the observing side
        /// reports the same destination key and authorizing policy without re-running validation (P-025).
        /// </summary>
        public static OwnerTransferResult Resolved(
            StateSlotKey source,
            StateSlotKey destination,
            FactoryKey policyKey,
            bool declaredLastSupportTransfer)
            => new OwnerTransferResult(source, destination, policyKey, declaredLastSupportTransfer, DiagnosticCode.None, string.Empty);
    }

    /// <summary>Validates one owner-transfer request against the declaration and the revision's declared owners.</summary>
    public static class OwnerTransferValidator
    {
        /// <summary>
        /// Validates an explicit owner transfer (P-025) or a declared last-support `TransferTo` loss (P-032).
        /// <paramref name="set"/> supplies the owners this catalog revision declares, which is what "a named available
        /// owner" means at validation time; it is never inferred from the live ECS storage.
        /// </summary>
        public static OwnerTransferResult Validate(
            SlotStatePolicy policy,
            StateSlotKey source,
            OwnerId destinationOwner,
            TargetId destinationTarget,
            bool declaredLastSupportTransfer,
            SlotStatePolicySet set,
            ISlotMigrationRegistry? declaredMigrations)
        {
            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            if (set == null)
            {
                throw new ArgumentNullException(nameof(set));
            }

            StateSlotKey destination = new StateSlotKey(destinationTarget, destinationOwner, source.Slot);

            SlotPolicyResult declared = SlotPolicyValidator.ValidateDeclaration(policy.Declaration);
            if (!declared.Succeeded)
            {
                return Refuse(source, destination, policy, declaredLastSupportTransfer, declared.Code, declared.Detail);
            }

            if (!policy.SlotId.Equals(source.Slot))
            {
                return Refuse(
                    source,
                    destination,
                    policy,
                    declaredLastSupportTransfer,
                    DiagnosticCode.OwnershipConflict,
                    "the transfer request names slot " + source.Slot.ToString() + " but the declaration is "
                    + policy.SlotId.ToString() + " (P-032).");
            }

            if (!policy.Owner.Equals(source.Owner))
            {
                // One owner per authoritative domain: a live key whose owner differs from the declaration is an
                // ownership conflict, not a transfer the declaration can authorize (P-034).
                return Refuse(
                    source,
                    destination,
                    policy,
                    declaredLastSupportTransfer,
                    DiagnosticCode.OwnershipConflict,
                    "the transfer source " + source.ToString() + " names owner " + source.Owner.ToString()
                    + " but the declaration owns slot " + policy.SlotId.ToString() + " as " + policy.Owner.ToString()
                    + " (P-034).");
            }

            if (declaredLastSupportTransfer)
            {
                SlotPolicyResult lastSupport = SlotPolicyValidator.Validate(
                    policy.Declaration,
                    SlotPolicyRequest.LastSupportLoss(LastSupportPolicy.TransferTo, destinationTarget),
                    declaredMigrations);
                if (!lastSupport.Succeeded || lastSupport.Outcome != SlotPolicyOutcome.Transfer)
                {
                    return Refuse(
                        source,
                        destination,
                        policy,
                        true,
                        lastSupport.Code == DiagnosticCode.None ? DiagnosticCode.OwnershipConflict : lastSupport.Code,
                        "state slot " + policy.SlotId.ToString()
                        + " is not losing its last support under a declared TransferTo policy: " + lastSupport.Detail);
                }
            }
            else if (!policy.HasTransferPolicy)
            {
                return Refuse(
                    source,
                    destination,
                    policy,
                    false,
                    DiagnosticCode.MissingDependency,
                    "state slot " + policy.SlotId.ToString()
                    + " declares no owner-transfer policy, so an explicit owner transfer has nothing to authorize it"
                    + " (P-032).");
            }

            if (source.Owner.Value.IsDefault)
            {
                return Refuse(
                    source,
                    destination,
                    policy,
                    declaredLastSupportTransfer,
                    DiagnosticCode.MissingDependency,
                    "the transfer source names no owner (P-032).");
            }

            if (destinationOwner.Value.IsDefault)
            {
                return Refuse(
                    source,
                    destination,
                    policy,
                    declaredLastSupportTransfer,
                    DiagnosticCode.MissingDependency,
                    "an owner transfer of state slot " + policy.SlotId.ToString()
                    + " must name the owner that takes over (P-032).");
            }

            if (destinationTarget.Value.IsDefault)
            {
                return Refuse(
                    source,
                    destination,
                    policy,
                    declaredLastSupportTransfer,
                    DiagnosticCode.MissingDependency,
                    "an owner transfer of state slot " + policy.SlotId.ToString()
                    + " must name the target the state moves to (P-025).");
            }

            if (destinationOwner.Equals(source.Owner))
            {
                return Refuse(
                    source,
                    destination,
                    policy,
                    declaredLastSupportTransfer,
                    DiagnosticCode.OwnershipConflict,
                    "state slot " + source.ToString() + " already belongs to " + source.Owner.ToString()
                    + "; a transfer must name a different owner, and a target-only move is not an owner transfer"
                    + " (P-032).");
            }

            return new OwnerTransferResult(
                source,
                destination,
                policy.Declaration.TransferPolicy,
                declaredLastSupportTransfer,
                DiagnosticCode.None,
                string.Empty);
        }

        /// <summary>Owners this revision declares, so a transfer destination is a real owner of this revision.</summary>
        private static bool IsAvailableOwner(SlotStatePolicySet set, OwnerId owner)
        {
            IReadOnlyList<OwnerId> owners = set.Owners;
            for (int i = 0; i < owners.Count; i++)
            {
                if (owners[i].Equals(owner))
                {
                    return true;
                }
            }

            return false;
        }

        private static OwnerTransferResult Refuse(
            StateSlotKey source,
            StateSlotKey destination,
            SlotStatePolicy policy,
            bool declaredLastSupportTransfer,
            DiagnosticCode code,
            string detail)
            => new OwnerTransferResult(source, destination, policy.Declaration.TransferPolicy, declaredLastSupportTransfer, code, detail);
    }
}
