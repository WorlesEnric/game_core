// GameCore.Planning tests — target slots, generations and stale-handle rejection (GC-008, P-004, P-005).
//
// The ledger is the engine-free half of the target registry: it owns slot allocation, generation advance and
// handle validation, so these cases prove the stale-handle rules without a Unity world. The key properties are
// that retiring a slot invalidates every handle taken before the retire, that the next allocation of that slot
// carries a higher generation, that a duplicate live identity is a conflict rather than a second slot, and that
// an exhausted generation counter is refused instead of wrapping.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using NUnit.Framework;

namespace GameCore.Planning.Tests
{
    [TestFixture]
    public sealed class TargetSlotLedgerTests
    {
        private static TargetSlotLedger Ledger(uint capacity = 4U) =>
            new TargetSlotLedger(PlansFixtureKeys.World(1UL), capacity);

        [Test]
        public void AFreshSlotAllocatesGenerationOneAndResolves()
        {
            TargetSlotLedger ledger = Ledger();
            TargetId target = PlansFixtureKeys.Target(1UL);

            Assert.That(ledger.TryAllocate(target, out TargetHandle handle, out DiagnosticCode code), Is.True);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(handle.World.Session, Is.EqualTo(ledger.World.Session));
            Assert.That(handle.Slot, Is.EqualTo(0U));
            Assert.That(handle.Generation, Is.EqualTo(1UL), "a fresh slot starts at generation 1 (P-005)");
            Assert.That(ledger.LiveCount, Is.EqualTo(1));

            Assert.That(ledger.TryResolve(handle, out TargetId resolved, out DiagnosticCode resolveCode), Is.True);
            Assert.That(resolved, Is.EqualTo(target));
            Assert.That(resolveCode, Is.EqualTo(DiagnosticCode.None));
        }

        [Test]
        public void AHandleTakenBeforeARetireIsRejectedAfterwards()
        {
            TargetSlotLedger ledger = Ledger();
            TargetId target = PlansFixtureKeys.Target(1UL);
            ledger.TryAllocate(target, out TargetHandle stale, out _);

            Assert.That(ledger.Retire(stale, out TargetId retired, out DiagnosticCode retireCode), Is.True);
            Assert.That(retired, Is.EqualTo(target));
            Assert.That(retireCode, Is.EqualTo(DiagnosticCode.None));
            Assert.That(ledger.LiveCount, Is.EqualTo(0));

            Assert.That(ledger.TryResolve(stale, out _, out DiagnosticCode code), Is.False,
                "destroy/recreate invalidates handles even when the stable identity is restored (P-005)");
            Assert.That(code, Is.EqualTo(DiagnosticCode.StaleHandle));
            Assert.That(ledger.StaleRejectionCount, Is.EqualTo(1));

            // The same stable identity can be restored, but only with a new generation: the slot now carries 2.
            Assert.That(ledger.TryAllocate(target, out TargetHandle fresh, out _), Is.True);
            Assert.That(fresh.Generation, Is.EqualTo(2UL));
            Assert.That(fresh.Equals(stale), Is.False);
            Assert.That(ledger.TryResolve(stale, out _, out _), Is.False);
            Assert.That(ledger.TryResolve(fresh, out TargetId again, out _), Is.True);
            Assert.That(again, Is.EqualTo(target));
        }

        [Test]
        public void StaleAndForeignHandlesNeverResolve()
        {
            TargetSlotLedger ledger = Ledger();
            ledger.TryAllocate(PlansFixtureKeys.Target(1UL), out TargetHandle handle, out _);

            var foreignWorld = new TargetHandle(PlansFixtureKeys.World(2UL), handle.Slot, handle.Generation);
            Assert.That(ledger.TryResolve(foreignWorld, out _, out DiagnosticCode foreign), Is.False);
            Assert.That(foreign, Is.EqualTo(DiagnosticCode.StaleHandle), "a handle is scoped to its world incarnation (P-004)");

            var outOfRange = new TargetHandle(ledger.World, 99U, 1UL);
            Assert.That(ledger.TryResolve(outOfRange, out _, out DiagnosticCode range), Is.False);
            Assert.That(range, Is.EqualTo(DiagnosticCode.StaleHandle));

            var wrongGeneration = new TargetHandle(ledger.World, handle.Slot, 5UL);
            Assert.That(ledger.TryResolve(wrongGeneration, out _, out DiagnosticCode generation), Is.False);
            Assert.That(generation, Is.EqualTo(DiagnosticCode.StaleHandle));

            Assert.That(ledger.StaleRejectionCount, Is.EqualTo(3));
        }

        [Test]
        public void ADuplicateLiveIdentityIsAConflict()
        {
            TargetSlotLedger ledger = Ledger();
            TargetId target = PlansFixtureKeys.Target(7UL);

            Assert.That(ledger.TryAllocate(target, out _, out _), Is.True);
            Assert.That(ledger.TryAllocate(target, out _, out DiagnosticCode code), Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.OwnershipConflict),
                "one world rejects duplicate live stable identities in a category (P-004)");
            Assert.That(ledger.LiveCount, Is.EqualTo(1));
        }

        [Test]
        public void AFullLedgerRejectsWithABudgetCodeRatherThanOverwriting()
        {
            TargetSlotLedger ledger = Ledger(2U);
            Assert.That(ledger.TryAllocate(PlansFixtureKeys.Target(1UL), out _, out _), Is.True);
            Assert.That(ledger.TryAllocate(PlansFixtureKeys.Target(2UL), out _, out _), Is.True);

            Assert.That(ledger.TryAllocate(PlansFixtureKeys.Target(3UL), out _, out DiagnosticCode code), Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.BudgetExceeded));
            Assert.That(ledger.ExhaustedRejectionCount, Is.EqualTo(1));
            Assert.That(ledger.FreeCount, Is.EqualTo(0));

            // Retiring a target frees its slot for the next allocation, without reusing the old generation.
            Assert.That(ledger.RetireTarget(PlansFixtureKeys.Target(1UL), out TargetHandle retired, out _), Is.True);
            Assert.That(ledger.TryAllocate(PlansFixtureKeys.Target(3UL), out TargetHandle reused, out _), Is.True);
            Assert.That(reused.Slot, Is.EqualTo(retired.Slot));
            Assert.That(reused.Generation, Is.GreaterThan(retired.Generation));
        }

        [Test]
        public void EachAllocationOfASlotAdvancesItsGeneration()
        {
            TargetSlotLedger ledger = Ledger(1U);
            TargetId target = PlansFixtureKeys.Target(1UL);

            Assert.That(ledger.TryAllocate(target, out TargetHandle first, out _), Is.True);
            Assert.That(ledger.Retire(first, out _, out _), Is.True);
            Assert.That(ledger.TryAllocate(target, out TargetHandle second, out _), Is.True);
            Assert.That(second.Generation, Is.EqualTo(2UL), "the generation advances on each allocation of a slot (P-005)");
            Assert.That(ledger.Retire(second, out _, out _), Is.True);
            Assert.That(ledger.TryAllocate(target, out TargetHandle third, out _), Is.True);
            Assert.That(third.Generation, Is.EqualTo(3UL));
            Assert.That(ledger.GenerationOf(0U), Is.EqualTo(3UL));
        }

        [Test]
        public void AnExhaustedGenerationCounterIsRefusedRatherThanWrapped()
        {
            // 2^64 allocations cannot be run, so the boundary is asserted through the production predicate the ledger
            // itself uses at allocation time (TEST-002 asks for a boundary fixture, not an impractical loop).
            Assert.That(TargetSlotLedger.IsExhaustedGeneration(ulong.MaxValue, 1U), Is.True,
                "a slot whose generation counter ended must never be reused (P-005)");
            Assert.That(TargetSlotLedger.IsExhaustedGeneration(ulong.MaxValue - 1UL, 1U), Is.False,
                "the last representable generation is still allocatable");
            Assert.That(TargetSlotLedger.IsExhaustedGeneration(ulong.MaxValue, 0U), Is.False,
                "a never-allocated slot starts at generation 1 regardless of its stored counter");
        }

        [Test]
        public void HandlesAreListedInSlotOrderForTheFence()
        {
            TargetSlotLedger ledger = Ledger();
            ledger.TryAllocate(PlansFixtureKeys.Target(3UL), out _, out _);
            ledger.TryAllocate(PlansFixtureKeys.Target(1UL), out _, out _);

            IReadOnlyList<TargetHandle> handles = ledger.Handles();
            Assert.That(handles.Count, Is.EqualTo(2));
            Assert.That(handles[0].Slot, Is.LessThan(handles[1].Slot), "the fence enumerates slots in index order (P-030)");
            Assert.That(ledger.IsLive(PlansFixtureKeys.Target(3UL)), Is.True);
            Assert.That(ledger.IsLive(PlansFixtureKeys.Target(2UL)), Is.False);
        }
    }
}
