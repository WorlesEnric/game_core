// GameCore.Planning — bounded migration scratch and the pure migration contract (GC-008).
//
// Normative sources: 00 P-022 (budgets: temporary bytes are a hard, configured limit), P-029 (after admission
// closes and jobs drain, copy only the state slots needed for migration into bounded scratch storage and run
// pure fallible migrations there *before* the first live write; a preparation/migration failure releases staged
// leases in reverse dependency order and leaves the old assembly intact), P-032 (a version change uses a
// registered `Migrate`; a missing compatible policy is a validation error, not implicit zero initialization) and
// 05 s5 (`Migrate_*`: bounded copied old state to scratch new state, pure, versioned, no I/O and no ECS writes;
// failure leaves old live state).
//
// Everything here is engine-free and in-memory: the scratch is the *planning* account of temporary bytes, and the
// migration functions are pure `int -> int` transforms the fixture (and later generated handlers) register by
// key and version. The publisher is the only caller that touches live ECS state, and it runs these functions on
// values it has already copied out of the world.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Planning
{
    /// <summary>
    /// One registered, versioned, pure slot migration (05 s5 `Migrate_*`). A migration never reads the world, never
    /// allocates a lease and never writes ECS state; it transforms one copied value or reports failure.
    /// </summary>
    public interface ISlotMigration
    {
        /// <summary>Generated registration key of this migration; the plan carries the key, not the delegate (05 s4).</summary>
        FactoryKey Key { get; }

        uint FromVersion { get; }

        uint ToVersion { get; }

        /// <summary>Pure transform of one copied state value; false means the migration rejects its input.</summary>
        bool TryMigrate(int source, out int migrated);
    }

    /// <summary>Registered migration handlers of one catalog revision; a miss is reported, never substituted (P-009).</summary>
    public sealed class MigrationRegistry
    {
        private readonly Dictionary<Id128, ISlotMigration> byKey = new Dictionary<Id128, ISlotMigration>();
        private readonly List<ISlotMigration> ordered = new List<ISlotMigration>();
        private readonly List<FactoryKey> duplicates = new List<FactoryKey>();

        public MigrationRegistry(IReadOnlyList<ISlotMigration>? migrations)
        {
            if (migrations == null)
            {
                return;
            }

            for (int i = 0; i < migrations.Count; i++)
            {
                ISlotMigration migration = migrations[i];
                if (migration == null)
                {
                    continue;
                }

                if (byKey.ContainsKey(migration.Key.RegistrationKey))
                {
                    // One registration key resolves to one handler; a duplicate is a catalog defect (P-009).
                    duplicates.Add(migration.Key);
                    continue;
                }

                byKey.Add(migration.Key.RegistrationKey, migration);
                ordered.Add(migration);
            }
        }

        public int Count => ordered.Count;

        public IReadOnlyList<FactoryKey> DuplicateKeys => duplicates;

        public IReadOnlyList<ISlotMigration> Migrations => ordered;

        /// <summary>Resolves one migration handler by key; a miss is a value, not a fallback (P-009, P-032).</summary>
        public bool TryFind(FactoryKey key, out ISlotMigration? migration)
        {
            if (byKey.TryGetValue(key.RegistrationKey, out ISlotMigration found))
            {
                migration = found;
                return true;
            }

            migration = null;
            return false;
        }
    }

    /// <summary>Result of one scratch migration: the migrated value or the reason it did not run.</summary>
    public readonly struct MigrationOutcome
    {
        public readonly StateSlotKey Slot;
        public readonly FactoryKey Key;
        public readonly bool Applied;
        public readonly int Value;
        public readonly uint FromVersion;
        public readonly uint ToVersion;
        public readonly DiagnosticCode Code;

        public MigrationOutcome(
            StateSlotKey slot,
            FactoryKey key,
            bool applied,
            int value,
            uint fromVersion,
            uint toVersion,
            DiagnosticCode code)
        {
            Slot = slot;
            Key = key;
            Applied = applied;
            Value = value;
            FromVersion = fromVersion;
            ToVersion = toVersion;
            Code = code;
        }

        public override string ToString() =>
            Slot.ToString() + (Applied ? "=" : "!=")
            + Value.ToString(CultureInfo.InvariantCulture)
            + ":" + FromVersion.ToString(CultureInfo.InvariantCulture)
            + "->" + ToVersion.ToString(CultureInfo.InvariantCulture)
            + (Code == DiagnosticCode.None ? string.Empty : "(" + DiagnosticCodeText.Of(Code) + ")");
    }

    /// <summary>
    /// Bounded migration scratch of one plan (P-022, P-029). Temporary bytes are a hard limit: reserving beyond
    /// the configured capacity is `BudgetExceeded` and never a partial migration. A reservation that is released
    /// leaves no residue, and the high-water mark is reported so the plan's cost estimate is measurable.
    /// </summary>
    public sealed class MigrationScratch
    {
        private readonly Dictionary<StateSlotKey, int> values = new Dictionary<StateSlotKey, int>();
        private readonly Dictionary<StateSlotKey, int> slotOrder = new Dictionary<StateSlotKey, int>();
        private readonly ulong bytesPerSlot;
        private int nextOrdinal;

        public MigrationScratch(ulong capacityBytes, ulong bytesPerSlot)
        {
            if (bytesPerSlot == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(bytesPerSlot), "A slot reservation must cost at least one byte.");
            }

            CapacityBytes = capacityBytes;
            this.bytesPerSlot = bytesPerSlot;
        }

        public ulong CapacityBytes { get; }

        public ulong BytesPerSlot => bytesPerSlot;

        public ulong ReservedBytes => (ulong)values.Count * bytesPerSlot;

        public ulong HighWaterBytes { get; private set; }

        /// <summary>Slots currently holding a staged migration result.</summary>
        public int ReservedSlots => values.Count;

        /// <summary>Reservations refused for exceeding the configured scratch budget.</summary>
        public int BudgetExceededCount { get; private set; }

        /// <summary>Migrations refused because the registry has no handler or a mismatched version pair.</summary>
        public int RefusedMigrationCount { get; private set; }

        public bool IsEmpty => values.Count == 0;

        /// <summary>Slots with a staged result, in reservation order.</summary>
        public IReadOnlyList<StateSlotKey> ReservedSlotsInOrder()
        {
            var keys = new List<StateSlotKey>(slotOrder.Count);
            foreach (KeyValuePair<StateSlotKey, int> entry in slotOrder)
            {
                keys.Add(entry.Key);
            }

            keys.Sort(CompareByOrdinal);
            return keys;
        }

        /// <summary>Reserves scratch for one slot; a repeated reservation of the same slot costs nothing extra.</summary>
        public bool TryReserve(StateSlotKey slot, out DiagnosticCode code)
        {
            if (values.ContainsKey(slot))
            {
                code = DiagnosticCode.None;
                return true;
            }

            if (ReservedBytes + bytesPerSlot > CapacityBytes)
            {
                BudgetExceededCount++;
                code = DiagnosticCode.BudgetExceeded;
                return false;
            }

            values.Add(slot, 0);
            slotOrder.Add(slot, nextOrdinal++);
            if (ReservedBytes > HighWaterBytes)
            {
                HighWaterBytes = ReservedBytes;
            }

            code = DiagnosticCode.None;
            return true;
        }

        /// <summary>
        /// Runs one registered migration on a copied value into scratch. A missing handler or a version pair that
        /// does not match the request is `MigrationRequired`/`UnsupportedVersion`; the caller treats either as a
        /// prewrite failure and keeps the old assembly (P-029, P-032).
        /// </summary>
        public bool TryMigrate(
            StateSlotKey slot,
            FactoryKey migrationKey,
            uint fromVersion,
            int sourceValue,
            MigrationRegistry registry,
            out MigrationOutcome outcome)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (!registry.TryFind(migrationKey, out ISlotMigration? migration) || migration == null)
            {
                // P-032: no compatible registered policy is a validation error, never implicit reinitialisation.
                RefusedMigrationCount++;
                outcome = Failed(slot, migrationKey, sourceValue, fromVersion, DiagnosticCode.MigrationRequired);
                return false;
            }

            if (migration.FromVersion != fromVersion)
            {
                RefusedMigrationCount++;
                outcome = Failed(slot, migrationKey, sourceValue, fromVersion, DiagnosticCode.UnsupportedVersion);
                return false;
            }

            // The reservation is taken only once a compatible handler exists, and the pure transform runs only
            // after it: a refused migration stages nothing and never leaves an implicit zero value behind
            // (P-029, P-032).
            if (!TryReserve(slot, out DiagnosticCode reserveCode))
            {
                outcome = Failed(slot, migrationKey, sourceValue, fromVersion, reserveCode);
                return false;
            }

            if (!migration.TryMigrate(sourceValue, out int migrated))
            {
                RefusedMigrationCount++;
                Release(slot);
                outcome = Failed(slot, migrationKey, sourceValue, fromVersion, DiagnosticCode.MigrationRequired);
                return false;
            }

            values[slot] = migrated;
            outcome = new MigrationOutcome(
                slot,
                migrationKey,
                true,
                migrated,
                migration.FromVersion,
                migration.ToVersion,
                DiagnosticCode.None);
            return true;
        }

        /// <summary>Reads a staged result; false means this slot has no reserved scratch value.</summary>
        public bool TryRead(StateSlotKey slot, out int value) => values.TryGetValue(slot, out value);

        /// <summary>Releases one reservation; false when the slot held none.</summary>
        public bool Release(StateSlotKey slot)
        {
            if (!values.Remove(slot))
            {
                return false;
            }

            slotOrder.Remove(slot);
            return true;
        }

        /// <summary>
        /// Releases every reservation in reverse reservation order and returns how many were released. A rejected
        /// plan's scratch is freed before its staged leases are (P-029), so the two accounts are separate.
        /// </summary>
        public int ReleaseAll()
        {
            IReadOnlyList<StateSlotKey> ordered = ReservedSlotsInOrder();
            int released = 0;
            for (int i = ordered.Count - 1; i >= 0; i--)
            {
                if (Release(ordered[i]))
                {
                    released++;
                }
            }

            slotOrder.Clear();
            nextOrdinal = 0;
            return released;
        }

        private static MigrationOutcome Failed(
            StateSlotKey slot,
            FactoryKey key,
            int sourceValue,
            uint fromVersion,
            DiagnosticCode code) =>
            new MigrationOutcome(slot, key, false, sourceValue, fromVersion, fromVersion, code);

        private int CompareByOrdinal(StateSlotKey left, StateSlotKey right) =>
            slotOrder[left].CompareTo(slotOrder[right]);
    }
}
