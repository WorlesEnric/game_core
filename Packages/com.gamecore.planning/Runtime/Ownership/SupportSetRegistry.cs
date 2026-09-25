// GameCore.Planning — effective support sets and derived component lifetime (GC-007).
//
// Normative sources: docs/game-core/00-core-protocols.md P-032, P-033 and
// docs/game-core/05-contracts-and-data-model.md s4 (`SupportRecord`).
//
// Derived component existence is governed by the union of the effective support sets plus the slot policy: removing
// one provider removes exactly its support and nothing else, and the final removal is legal only when no other
// capability or recipe requires the component. A retraction that would remove state the declaration does not
// permit removing is rejected *before* the set changes, so a rejected retraction never leaves a half-applied state.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Planning.Ownership
{
    /// <summary>How one support-set edit resolved (P-033).</summary>
    public enum SupportSetOutcome
    {
        /// <summary>The support was recorded; the derived component stays active.</summary>
        SupportAdded = 0,

        /// <summary>The identical support was already present; the set is unchanged.</summary>
        DuplicateSupport = 1,

        /// <summary>The support was removed and other supports still require the component.</summary>
        RetractedWhileSupported = 2,

        /// <summary>The support was the last one and the declared slot policy decided what happens next.</summary>
        RetractedFinalSupport = 3,

        /// <summary>The support was the last one, but a base recipe still requires the component.</summary>
        RetainedByRecipeRequirement = 4,

        /// <summary>The support (or the slot) is not present; nothing changed.</summary>
        UnknownSupport = 5,

        /// <summary>The declaration does not permit the removal this retraction would perform; nothing changed.</summary>
        PolicyRejected = 6,
    }

    /// <summary>What the derived component's lifetime becomes after a support-set edit (P-032, P-033).</summary>
    public enum DerivedLifetimeDecision
    {
        /// <summary>The component stays present with at least one active support.</summary>
        KeepActive = 0,

        /// <summary>Disposable derived data: the component may be removed.</summary>
        RemoveDerived = 1,

        /// <summary>Persistent state stays in ECS with no active writer, excluded from active queries.</summary>
        RetainDormant = 2,

        /// <summary>A declared compatible owner takes the state; the transfer must complete before removal.</summary>
        TransferPending = 3,

        /// <summary>The edit is refused: the declaration does not permit it.</summary>
        Rejected = 4,
    }

    /// <summary>Result of one support-set edit, including the lifetime decision it produced.</summary>
    public readonly struct SupportSetDelta
    {
        public readonly StateSlotKey Slot;
        public readonly SupportSetOutcome Outcome;
        public readonly DerivedLifetimeDecision Lifetime;

        /// <summary>Supports remaining after this edit; unchanged for a rejection.</summary>
        public readonly int RemainingSupportCount;

        public readonly DiagnosticCode Code;
        public readonly string Detail;

        internal SupportSetDelta(
            StateSlotKey slot,
            SupportSetOutcome outcome,
            DerivedLifetimeDecision lifetime,
            int remainingSupportCount,
            DiagnosticCode code,
            string detail)
        {
            Slot = slot;
            Outcome = outcome;
            Lifetime = lifetime;
            RemainingSupportCount = remainingSupportCount;
            Code = code;
            Detail = detail ?? string.Empty;
        }

        /// <summary>True when the edit was applied; a rejection leaves the set untouched.</summary>
        public bool Applied => Code == DiagnosticCode.None;

        /// <summary>True when this edit decided that the derived component's life ended (or went dormant).</summary>
        public bool EndedActiveLife
            => Outcome == SupportSetOutcome.RetractedFinalSupport
                && Lifetime != DerivedLifetimeDecision.KeepActive;

        public override string ToString()
            => Outcome + ":" + Lifetime + "@" + Slot.ToString()
                + (Code == DiagnosticCode.None ? string.Empty : "(" + DiagnosticCodeText.Of(Code) + ")");
    }

    /// <summary>
    /// Effective support sets of one world image, keyed by <c>(TargetId, OwnerId, SlotId)</c> (P-032). Providers add
    /// and retract their own <see cref="SupportRecord"/>; the set is never rebuilt from defaults, and a final
    /// retraction consults the declared last-support policy instead of guessing.
    /// </summary>
    public sealed class SupportSetRegistry
    {
        private readonly Dictionary<StateSlotKey, List<SupportRecord>> supports =
            new Dictionary<StateSlotKey, List<SupportRecord>>();

        private readonly List<SlotAuthorityDeclaration> slots;

        private readonly HashSet<StateSlotKey> recipeRequired;

        public SupportSetRegistry(
            IReadOnlyList<SlotAuthorityDeclaration>? slots,
            IReadOnlyList<StateSlotKey>? recipeRequiredSlots)
        {
            this.slots = slots == null ? new List<SlotAuthorityDeclaration>() : new List<SlotAuthorityDeclaration>(slots);
            recipeRequired = recipeRequiredSlots == null
                ? new HashSet<StateSlotKey>()
                : new HashSet<StateSlotKey>(recipeRequiredSlots);
        }

        /// <summary>Slots that currently have at least one active support.</summary>
        public int SupportedSlotCount => supports.Count;

        /// <summary>Total recorded supports across every slot.</summary>
        public int TotalSupportCount
        {
            get
            {
                int total = 0;
                foreach (KeyValuePair<StateSlotKey, List<SupportRecord>> pair in supports)
                {
                    total += pair.Value.Count;
                }

                return total;
            }
        }

        public int AddedCount { get; private set; }

        public int DuplicateCount { get; private set; }

        public int RetractedCount { get; private set; }

        public int RemovedDerivedCount { get; private set; }

        public int RetainedDormantCount { get; private set; }

        public int TransferPendingCount { get; private set; }

        public int RetainedByRecipeCount { get; private set; }

        public int UnknownSupportCount { get; private set; }

        public int PolicyRejectedCount { get; private set; }

        /// <summary>Records one provider support; the identical record twice is a duplicate, not a second support.</summary>
        public SupportSetDelta Add(SupportRecord support)
        {
            if (!TryGetSlot(support.Slot, out _, out DiagnosticCode slotCode, out string slotDetail))
            {
                UnknownSupportCount++;
                return new SupportSetDelta(support.Slot, SupportSetOutcome.UnknownSupport, DerivedLifetimeDecision.Rejected, 0, slotCode, slotDetail);
            }

            if (!supports.TryGetValue(support.Slot, out List<SupportRecord>? records) || records == null)
            {
                records = new List<SupportRecord>();
                supports.Add(support.Slot, records);
            }

            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].Equals(support))
                {
                    DuplicateCount++;
                    return new SupportSetDelta(
                        support.Slot,
                        SupportSetOutcome.DuplicateSupport,
                        DerivedLifetimeDecision.KeepActive,
                        records.Count,
                        DiagnosticCode.OwnershipConflict,
                        "the identical support is already recorded for this slot; an identity contributes once (P-017, P-033)");
                }
            }

            records.Add(support);
            AddedCount++;
            return new SupportSetDelta(
                support.Slot,
                SupportSetOutcome.SupportAdded,
                DerivedLifetimeDecision.KeepActive,
                records.Count,
                DiagnosticCode.None,
                string.Empty);
        }

        /// <summary>
        /// Removes exactly one support. When it is the final one, the slot's declared last-support policy decides the
        /// outcome; a policy that does not permit the removal rejects before the set changes (P-032, P-033).
        /// </summary>
        public SupportSetDelta Retract(SupportRecord support)
        {
            if (!TryGetSlot(support.Slot, out SlotAuthorityDeclaration? declaration, out DiagnosticCode slotCode, out string slotDetail)
                || declaration == null)
            {
                UnknownSupportCount++;
                return new SupportSetDelta(support.Slot, SupportSetOutcome.UnknownSupport, DerivedLifetimeDecision.Rejected, 0, slotCode, slotDetail);
            }

            if (!supports.TryGetValue(support.Slot, out List<SupportRecord>? records) || records == null)
            {
                UnknownSupportCount++;
                return new SupportSetDelta(
                    support.Slot,
                    SupportSetOutcome.UnknownSupport,
                    DerivedLifetimeDecision.KeepActive,
                    0,
                    DiagnosticCode.MissingDependency,
                    "no support is recorded for this slot");
            }

            int index = IndexOf(records, support);
            if (index < 0)
            {
                UnknownSupportCount++;
                return new SupportSetDelta(
                    support.Slot,
                    SupportSetOutcome.UnknownSupport,
                    DerivedLifetimeDecision.KeepActive,
                    records.Count,
                    DiagnosticCode.MissingDependency,
                    "the retraction names a support that this slot does not have; a provider removes exactly its own support (P-033)");
            }

            if (records.Count > 1)
            {
                records.RemoveAt(index);
                RetractedCount++;
                return new SupportSetDelta(
                    support.Slot,
                    SupportSetOutcome.RetractedWhileSupported,
                    DerivedLifetimeDecision.KeepActive,
                    records.Count,
                    DiagnosticCode.None,
                    string.Empty);
            }

            if (recipeRequired.Contains(support.Slot))
            {
                records.RemoveAt(index);
                RetractedCount++;
                RetainedByRecipeCount++;
                return new SupportSetDelta(
                    support.Slot,
                    SupportSetOutcome.RetainedByRecipeRequirement,
                    DerivedLifetimeDecision.KeepActive,
                    0,
                    DiagnosticCode.None,
                    "a base recipe still requires this component, so the final removal is not allowed (P-033)");
            }

            DerivedLifetimeDecision decision = DecideLastSupport(declaration, out DiagnosticCode code, out string detail);
            if (decision == DerivedLifetimeDecision.Rejected)
            {
                // Reject before mutation: the set and the derived state stay exactly as they were (P-032).
                // No mutation on rejection.
                PolicyRejectedCount++;
                return new SupportSetDelta(
                    support.Slot,
                    SupportSetOutcome.PolicyRejected,
                    DerivedLifetimeDecision.Rejected,
                    records.Count,
                    code,
                    detail);
            }

            records.RemoveAt(index);
            RetractedCount++;
            switch (decision)
            {
                case DerivedLifetimeDecision.RemoveDerived:
                    RemovedDerivedCount++;
                    break;
                case DerivedLifetimeDecision.RetainDormant:
                    RetainedDormantCount++;
                    break;
                case DerivedLifetimeDecision.TransferPending:
                    TransferPendingCount++;
                    break;
                default:
                    break;
            }

            return new SupportSetDelta(
                support.Slot,
                SupportSetOutcome.RetractedFinalSupport,
                decision,
                0,
                DiagnosticCode.None,
                string.Empty);
        }

        /// <summary>Supports of one slot in insertion order; empty when the slot has none.</summary>
        public IReadOnlyList<SupportRecord> Supports(StateSlotKey slot)
            => supports.TryGetValue(slot, out List<SupportRecord>? records) && records != null
                ? records
                : (IReadOnlyList<SupportRecord>)Array.Empty<SupportRecord>();

        public int SupportCount(StateSlotKey slot)
            => supports.TryGetValue(slot, out List<SupportRecord>? records) && records != null ? records.Count : 0;

        public bool HasSupport(StateSlotKey slot) => SupportCount(slot) > 0;

        /// <summary>Slots with at least one support, in canonical key order.</summary>
        public IReadOnlyList<StateSlotKey> SlotsWithSupport()
        {
            var keys = new List<StateSlotKey>();
            foreach (KeyValuePair<StateSlotKey, List<SupportRecord>> pair in supports)
            {
                if (pair.Value.Count != 0)
                {
                    keys.Add(pair.Key);
                }
            }

            keys.Sort(CompareSlotKeys);
            return keys;
        }

        /// <summary>The lifetime the current support set implies for one slot (P-032, P-033).</summary>
        public DerivedLifetimeDecision DecideLifetime(StateSlotKey slot)
        {
            if (SupportCount(slot) > 0)
            {
                return DerivedLifetimeDecision.KeepActive;
            }

            if (recipeRequired.Contains(slot))
            {
                return DerivedLifetimeDecision.KeepActive;
            }

            if (!TryGetSlot(slot, out SlotAuthorityDeclaration? declaration, out _, out string _) || declaration == null)
            {
                return DerivedLifetimeDecision.Rejected;
            }

            return DecideLastSupport(declaration, out _, out string _);
        }

        public bool IsRecipeRequired(StateSlotKey slot) => recipeRequired.Contains(slot);

        /// <summary>Marks a slot whose component a base recipe requires; its final removal is never allowed (P-033).</summary>
        public bool MarkRecipeRequired(StateSlotKey slot) => recipeRequired.Add(slot);

        private static DerivedLifetimeDecision DecideLastSupport(
            SlotAuthorityDeclaration declaration,
            out DiagnosticCode code,
            out string detail)
        {
            switch (declaration.LastSupport)
            {
                case LastSupportPolicy.RemoveDerived:
                    if (!declaration.Options.DisposableDerived)
                    {
                        code = DiagnosticCode.Ineligible;
                        detail = "slot " + declaration.SlotId
                            + " declares RemoveDerived on state that is not disposable derived data (P-032)";
                        return DerivedLifetimeDecision.Rejected;
                    }

                    code = DiagnosticCode.None;
                    detail = string.Empty;
                    return DerivedLifetimeDecision.RemoveDerived;

                case LastSupportPolicy.PreserveDormant:
                    if (!declaration.Options.PreserveDormantPermitted)
                    {
                        code = DiagnosticCode.Ineligible;
                        detail = "slot " + declaration.SlotId
                            + " declares PreserveDormant but the declaration does not permit dormant retention (P-032)";
                        return DerivedLifetimeDecision.Rejected;
                    }

                    code = DiagnosticCode.None;
                    detail = string.Empty;
                    return DerivedLifetimeDecision.RetainDormant;

                case LastSupportPolicy.TransferTo:
                    if (!declaration.HasTransferPolicy)
                    {
                        code = DiagnosticCode.MissingDependency;
                        detail = "slot " + declaration.SlotId
                            + " declares TransferTo without a registered owner-transfer policy (P-032)";
                        return DerivedLifetimeDecision.Rejected;
                    }

                    code = DiagnosticCode.None;
                    detail = string.Empty;
                    return DerivedLifetimeDecision.TransferPending;

                default:
                    code = DiagnosticCode.UnsupportedVersion;
                    detail = "slot " + declaration.SlotId + " declares an unknown last-support policy";
                    return DerivedLifetimeDecision.Rejected;
            }
        }

        private bool TryGetSlot(
            StateSlotKey key,
            out SlotAuthorityDeclaration? declaration,
            out DiagnosticCode code,
            out string detail)
        {
            declaration = null;
            for (int i = 0; i < slots.Count; i++)
            {
                if (!slots[i].SlotId.Equals(key.Slot))
                {
                    continue;
                }

                if (!slots[i].Owner.Equals(key.Owner))
                {
                    code = DiagnosticCode.OwnershipConflict;
                    detail = "state slot key " + key
                        + " names owner " + key.Owner + " but the declaration owns it as " + slots[i].Owner
                        + " (P-034)";
                    return false;
                }

                declaration = slots[i];
                code = DiagnosticCode.None;
                detail = string.Empty;
                return true;
            }

            code = DiagnosticCode.MissingDependency;
            detail = "no slot declaration covers " + key + "; a missing compatible policy is a validation error (P-032)";
            return false;
        }

        private static int IndexOf(List<SupportRecord> records, SupportRecord support)
        {
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].Equals(support))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int CompareSlotKeys(StateSlotKey left, StateSlotKey right)
        {
            int byTarget = left.Target.Value.CompareTo(right.Target.Value);
            if (byTarget != 0)
            {
                return byTarget;
            }

            int byOwner = left.Owner.Value.CompareTo(right.Owner.Value);
            return byOwner != 0 ? byOwner : left.Slot.Value.CompareTo(right.Slot.Value);
        }
    }
}
