// Independent pure oracle (GC-002). Handle identity, world-incarnation collision and stale-handle detection
// from P-004/P-005. Validation order follows the normative sentence: world, generation, liveness, expected
// category, then activation epoch when execution authority is required.
#nullable enable
using System;
using GameCore.Contracts;

namespace GameCore.ProtocolFixtures.Oracle
{
    /// <summary>Category a dereference expects (P-005).</summary>
    public enum HandleCategory
    {
        Target = 0,
        Scope = 1,
        Plugin = 2,
    }

    /// <summary>Liveness of the world-owned slot the handle addresses.</summary>
    public enum SlotLiveness
    {
        Live = 0,
        Retired = 1,
    }

    /// <summary>Why a dereference was refused.</summary>
    public enum HandleValidationCode
    {
        None = 0,
        MissingSlot = 1,
        WrongWorld = 2,
        StaleGeneration = 3,
        NotLive = 4,
        WrongCategory = 5,
        StaleActivationEpoch = 6,
    }

    /// <summary>Outcome of one dereference validation.</summary>
    public readonly struct HandleValidationOutcome
    {
        public HandleValidationOutcome(bool isValid, HandleValidationCode code)
        {
            IsValid = isValid;
            Code = code;
        }

        public bool IsValid { get; }

        public HandleValidationCode Code { get; }

        public string Describe() => IsValid ? "valid" : Code.ToString();
    }

    /// <summary>The world's own slot record: generation, liveness and category (P-005).</summary>
    public readonly struct LiveSlotRecord
    {
        public LiveSlotRecord(SlotLiveness liveness, ulong generation, HandleCategory category)
        {
            Liveness = liveness;
            Generation = generation;
            Category = category;
        }

        public SlotLiveness Liveness { get; }

        public ulong Generation { get; }

        public HandleCategory Category { get; }
    }

    /// <summary>Handle validation, world-incarnation separation and stale-handle detection.</summary>
    public static class IdentityOracle
    {
        /// <summary>
        /// Validates one target dereference against the live world. <paramref name="slotKnown"/> false means the
        /// world has no record for that slot at all, which is distinct from a retired slot.
        /// </summary>
        public static HandleValidationOutcome ValidateTarget(
            TargetHandle handle,
            WorldId liveWorld,
            bool slotKnown,
            LiveSlotRecord slot,
            HandleCategory expectedCategory,
            ulong requiredActivationEpoch,
            ulong providedActivationEpoch)
        {
            if (!SameIncarnation(handle.World, liveWorld))
            {
                return new HandleValidationOutcome(false, HandleValidationCode.WrongWorld);
            }

            if (!slotKnown)
            {
                return new HandleValidationOutcome(false, HandleValidationCode.MissingSlot);
            }

            if (handle.Generation != slot.Generation)
            {
                return new HandleValidationOutcome(false, HandleValidationCode.StaleGeneration);
            }

            if (slot.Liveness != SlotLiveness.Live)
            {
                return new HandleValidationOutcome(false, HandleValidationCode.NotLive);
            }

            if (slot.Category != expectedCategory)
            {
                return new HandleValidationOutcome(false, HandleValidationCode.WrongCategory);
            }

            if (requiredActivationEpoch != 0UL && requiredActivationEpoch != providedActivationEpoch)
            {
                return new HandleValidationOutcome(false, HandleValidationCode.StaleActivationEpoch);
            }

            return new HandleValidationOutcome(true, HandleValidationCode.None);
        }

        /// <summary>Worlds are equal only when the fresh 128-bit session id is equal (P-004).</summary>
        public static bool SameIncarnation(WorldId left, WorldId right) => left.Session.Equals(right.Session);

        /// <summary>
        /// True when two handles would resolve to the same storage. Two worlds may reuse the same local slot
        /// and generation without their handles colliding (TEST-002).
        /// </summary>
        public static bool HandlesCollide(TargetHandle left, TargetHandle right) =>
            SameIncarnation(left.World, right.World) &&
            left.Slot == right.Slot &&
            left.Generation == right.Generation;

        /// <summary>
        /// A restored session must never accept a handle from the previous incarnation, even when the stable
        /// ids and slots are re-created (P-004, P-005).
        /// </summary>
        public static bool RestoredWorldRejectsOldHandle(TargetHandle oldHandle, WorldId restoredWorld) =>
            !SameIncarnation(oldHandle.World, restoredWorld);
    }
}
