// GameCore.Gameplay.Rewards — the outbox state slot, its declaration and the "no pending work" migration (GC-024).
//
// Normative sources: docs/game-core/07-reference-compositions.md s5 ("`NarrativeCardRewards` declares
// `PreserveDormant` for its completed outbox, with a scratch-migration precondition that no pending work remains")
// and 00 P-032 (a state slot identifies an initialization, configuration-update, owner-transfer and last-support
// policy; a version change uses a registered `Migrate`; a missing compatible policy is a validation error, never
// implicit zero initialization), P-029 (copy only the slots a migration needs into bounded scratch and run a pure
// fallible migration there before the first live write, so a refused migration leaves the old assembly and its
// state untouched) and 05 s3 (`StateSlotSpec`).
//
// THE PRECONDITION IS A REGISTERED MIGRATION, NOT A DECLARED PRECONDITION FIELD. There is no precondition field on
// `StateSlotSpec` (Packages/com.gamecore.contracts/Runtime/Manifest/Declarations.cs:213-322 declares SlotId, Owner,
// Schema, PhysicalLayoutKey, FieldOwnership, InitPolicy, ConfigChangePolicy, VersionChangePolicy, LastSupport,
// TransferPolicy, MigrationKeys, ResetSupported and ResetReason — that is all), and `ValidityAndCost.Preconditions`
// (Packages/com.gamecore.contracts/Runtime/Plans/PlanDeltas.cs:671, documented as "declared precondition keys that
// application rechecks") is dead code: both of its construction sites pass null (AssemblyPlanner.cs:790 and
// :1134) and nothing under Packages/ reads it. The mechanism 07 s5's clause is actually carried by is therefore
// this file: the slot is declared at schema version 2 while the installation seeds the live row at version 1
// (`RewardsKeys.OutboxSeededSchemaVersion`), so a state-policy pass over that row requests a `Migrate` and runs
// `RewardsOutboxPreconditionMigration` on the *copied* value (MigrationScratch.cs:218-273,
// StatePolicyExecutor.DecideMigration at StatePolicies/StatePolicyExecutor.cs:671-757). A copy that says work is
// still pending is refused with `DiagnosticCode.MigrationRequired`, prewrite, and the old assembly keeps its state.
//
// WHAT THE SLOT'S VALUE IS. The stored int is the pending-work count: 0 means "no pending work". The installation
// writes it from `Bridge.Owner.Outbox.OpenObligations().Count` before every pass
// (`RewardsInstallation.WriteOutboxSlot`), so the migration's predicate runs on the real count the outbox reports.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning;

namespace GameCore.Gameplay.Rewards
{
    /// <summary>
    /// The registered v1 -> v2 migration of the outbox slot: 07 s5's scratch-migration precondition that no pending
    /// work remains. The body is pure and runs on the planner's *copy* of the live value, so a refusal cannot have
    /// touched live state (P-029); <see cref="Invocations"/> and <see cref="Refusals"/> are public so a scenario can
    /// prove the predicate ran on the copied value rather than on a value an assertion invented.
    /// </summary>
    public sealed class RewardsOutboxPreconditionMigration : ISlotMigration
    {
        /// <summary>Generated registration key of this migration; the plan carries the key, not the delegate (05 s4).</summary>
        public FactoryKey Key => RewardsKeys.OutboxMigration;

        /// <summary>The seeded live version this migration reads (P-032).</summary>
        public uint FromVersion => RewardsKeys.OutboxSeededSchemaVersion;

        /// <summary>The declared version it produces.</summary>
        public uint ToVersion => RewardsKeys.OutboxDeclaredSchemaVersion;

        /// <summary>Times the migration body ran (on a copied value).</summary>
        public int Invocations { get; private set; }

        /// <summary>Times it refused because the copied value reported pending work (07 s5's precondition).</summary>
        public int Refusals { get; private set; }

        /// <summary>
        /// Copies the pending-work count when there is none, and refuses when the copy says work is still pending.
        /// A non-negative count of 0 is the only accepted input: the value is a count of open obligations, so a
        /// negative one is corrupt and is refused rather than carried into version 2 (P-032, P-054).
        /// </summary>
        public bool TryMigrate(int source, out int migrated)
        {
            Invocations++;
            migrated = source;
            if (source == 0)
            {
                return true;
            }

            // The refusal is the precondition, and it is reported by counting rather than by throwing: the
            // pipeline turns a false return into a refused plan whose code is `MigrationRequired`.
            Refusals++;
            return false;
        }

        /// <summary>The migration key and version pair, for diagnostics.</summary>
        public override string ToString() =>
            "rewardsOutboxPrecondition(" + Key.ToString() + ", v"
            + FromVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) + "->v"
            + ToVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", invocations="
            + Invocations.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", refusals="
            + Refusals.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// The declared outbox slot of the reward installation: one owner, one schema at its declared version, one
    /// physically owned field, the two policy keys P-032 requires, and the `PreserveDormant` last-support policy
    /// 07 s5 names.
    /// </summary>
    public static class RewardsOutboxSlot
    {
        /// <summary>
        /// The one state slot this package declares, using the full 13-argument `StateSlotSpec` overload
        /// (`Declarations.cs:259-287`). The contract's mechanism table called this a 16-argument constructor; the
        /// source has thirteen parameters, and the 16-argument type is `CompositionEditPayload`
        /// (`Packages/com.gamecore.composition/Runtime/Operations/CompositionEditPayload.cs:57`).
        ///
        /// The field ownership list is deliberately non-default: a slot whose declared field ownership is empty
        /// would claim a layout with no owned field, and `FieldOwnership(RewardsKeys.OutboxSchema.Id,
        /// RewardsKeys.OutboxField.RegistrationKey)` is the one physical field the pending-work count occupies
        /// (P-032, 05 s3).
        /// </summary>
        public static StateSlotSpec Spec()
        {
            var ownership = new List<FieldOwnership>
            {
                new FieldOwnership(RewardsKeys.OutboxSchema, RewardsKeys.OutboxField.RegistrationKey),
            };

            return new StateSlotSpec(
                RewardsKeys.OutboxSlot,
                RewardsKeys.OutboxOwner,
                RewardsKeys.OutboxSchema,
                RewardsKeys.OutboxLayout,
                ownership,
                RewardsKeys.OutboxInitPolicy,
                RewardsKeys.OutboxConfigChangePolicy,
                RewardsKeys.OutboxMigration,
                LastSupportPolicy.PreserveDormant,
                RewardsKeys.OutboxTransferPolicy,
                new List<FactoryKey> { RewardsKeys.OutboxMigration },
                false,
                null);
        }

        /// <summary>The declared slots of this package, in canonical declaration order (exactly one).</summary>
        public static IReadOnlyList<StateSlotSpec> Specs() => new List<StateSlotSpec> { Spec() };

        /// <summary>The registered migration handler of this package, as a fresh instance (one registration key).</summary>
        public static ISlotMigration RegisteredMigration() => new RewardsOutboxPreconditionMigration();

        /// <summary>The protocol key of one live outbox row: `(target, RewardsKeys.OutboxOwner, RewardsKeys.OutboxSlot)`.</summary>
        public static StateSlotKey KeyOf(TargetId target) =>
            new StateSlotKey(target, RewardsKeys.OutboxOwner, RewardsKeys.OutboxSlot);

        /// <summary>True when a live slot key is this package's outbox slot, by identity and never by name.</summary>
        public static bool IsOutboxKey(StateSlotKey key) =>
            key.Slot.Equals(RewardsKeys.OutboxSlot) && key.Owner.Equals(RewardsKeys.OutboxOwner);
    }
}
