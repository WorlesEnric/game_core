// GameCore.Benchmarks tests — the workload catalogue and the runner's selector (GC-026).
//
// The ids are report keys: they appear in the raw sample document, in the CSV key column and in the summary table,
// and the harness requires every one of them in a result. Keeping the catalogue in one place is what stops a gate
// from quietly dropping a workload, so this suite pins the id set, the declared window of each kind (a steady
// workload is measured for a duration, a change over a repetition count) and the selector's two deliberate
// refusals: an unknown id is an error rather than a skip, and a selection is reported in catalogue order rather than
// request order.
//
// Sources in this folder run as plain-dotnet tests and as Unity EditMode tests.
#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameCore.Benchmarks.Tests
{
    [TestFixture]
    public sealed class BenchmarkWorkloadTests
    {
        private static readonly string[] DeclaredIds =
        {
            BenchmarkWorkloads.IdleCommandWorld,
            BenchmarkWorkloads.SteadyUnchanged,
            BenchmarkWorkloads.SteadyExecution,
            BenchmarkWorkloads.UpdateSizeOne,
            BenchmarkWorkloads.UpdateSizeHundred,
            BenchmarkWorkloads.UpdateSizeWhole,
            BenchmarkWorkloads.ModeSwitch,
            BenchmarkWorkloads.SpawnThousand,
            BenchmarkWorkloads.ReparentHundred,
            BenchmarkWorkloads.InactiveComparison,
            BenchmarkWorkloads.LifecycleCycles,
        };

        [Test]
        public void TheElevenDeclaredIdsExistInReportOrder()
        {
            Assert.That(BenchmarkWorkloads.All.Count, Is.EqualTo(11), "08's method names eleven workloads");
            Assert.That(DeclaredIds.Length, Is.EqualTo(11));
            Assert.That(BenchmarkWorkloads.IdleCommandWorld, Is.EqualTo("idle-command-world"));
            Assert.That(BenchmarkWorkloads.SteadyUnchanged, Is.EqualTo("steady-unchanged-10000-steps"));
            Assert.That(BenchmarkWorkloads.SteadyExecution, Is.EqualTo("steady-execution-10000-targets"));
            Assert.That(BenchmarkWorkloads.UpdateSizeOne, Is.EqualTo("update-size-1"));
            Assert.That(BenchmarkWorkloads.UpdateSizeHundred, Is.EqualTo("update-size-100"));
            Assert.That(BenchmarkWorkloads.UpdateSizeWhole, Is.EqualTo("update-size-10000"));
            Assert.That(BenchmarkWorkloads.ModeSwitch, Is.EqualTo("whole-world-mode-switch"));
            Assert.That(BenchmarkWorkloads.SpawnThousand, Is.EqualTo("spawn-1000"));
            Assert.That(BenchmarkWorkloads.ReparentHundred, Is.EqualTo("reparent-100"));
            Assert.That(BenchmarkWorkloads.InactiveComparison, Is.EqualTo("inactive-target-comparison"));
            Assert.That(BenchmarkWorkloads.LifecycleCycles, Is.EqualTo("lifecycle-cycles-1000"));

            for (int i = 0; i < DeclaredIds.Length; i++)
            {
                Assert.That(
                    BenchmarkWorkloads.All[i].Id,
                    Is.EqualTo(DeclaredIds[i]),
                    "workload " + i.ToString() + " is the declared one, in the declared report order");
            }
        }

        [Test]
        public void EveryIdIsUniqueAndEveryWorkloadDescribesItsOwnWindow()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < BenchmarkWorkloads.All.Count; i++)
            {
                BenchmarkWorkload workload = BenchmarkWorkloads.All[i];
                Assert.That(seen.Add(workload.Id), Is.True, "workload id " + workload.Id + " is unique");
                Assert.That(workload.Id.Length, Is.GreaterThan(0));
                Assert.That(workload.Dimension.Length, Is.GreaterThan(0), workload.Id + " declares the budget dimension it measures");
                Assert.That(
                    workload.Description.Length,
                    Is.GreaterThanOrEqualTo(40),
                    workload.Id + " explains the window 08 asks for rather than only naming itself");
                Assert.That(
                    workload.Description.Contains(workload.Id, StringComparison.Ordinal),
                    Is.False,
                    workload.Id + ": the description explains the workload, it does not restate the key");
                Assert.That(
                    workload.ToString().Contains(workload.Id, StringComparison.Ordinal),
                    Is.True);
                Assert.That(
                    workload.ToString().Contains("duration=", StringComparison.Ordinal),
                    Is.True);
                Assert.That(
                    workload.ToString().Contains("repetitions=", StringComparison.Ordinal),
                    Is.True);
            }

            Assert.That(BenchmarkWorkloads.TryGet(BenchmarkWorkloads.UpdateSizeHundred, out BenchmarkWorkload update), Is.True);
            Assert.That(update, Is.SameAs(BenchmarkWorkloads.All[4]), "TryGet returns the catalogue instance, not a copy");
            Assert.That(update.Dimension, Is.EqualTo("update-size"));

            Assert.That(BenchmarkWorkloads.TryGet(BenchmarkWorkloads.IdleCommandWorld, out BenchmarkWorkload idle), Is.True);
            Assert.That(idle.Dimension, Is.EqualTo("idle"));
            Assert.That(idle.Kind, Is.EqualTo(BenchmarkWorkloadKind.Steady));
            Assert.That(BenchmarkWorkloads.TryGet("IDLE-COMMAND-WORLD", out BenchmarkWorkload upper), Is.False);
            Assert.That(upper, Is.Null, "an id is matched exactly, never case-insensitively");
            Assert.That(BenchmarkWorkloads.TryGet(BenchmarkWorkloads.IdleCommandWorld + " ", out _), Is.False);
        }

        [Test]
        public void ASteadyWorkloadDeclaresADurationAndAChangeWorkloadDeclaresRepetitions()
        {
            int steady = 0;
            int change = 0;
            for (int i = 0; i < BenchmarkWorkloads.All.Count; i++)
            {
                BenchmarkWorkload workload = BenchmarkWorkloads.All[i];
                if (workload.Kind == BenchmarkWorkloadKind.Steady)
                {
                    steady++;
                    Assert.That(
                        workload.DefaultDurationSeconds,
                        Is.EqualTo(BenchmarkWorkloads.DefaultDurationSeconds),
                        workload.Id + " is measured for 08's duration window");
                    Assert.That(workload.DefaultDurationSeconds, Is.GreaterThan(0));
                    Assert.That(workload.DefaultRepetitions, Is.EqualTo(0), workload.Id + " is not a repeated change");
                }
                else
                {
                    change++;
                    Assert.That(
                        workload.DefaultRepetitions,
                        Is.GreaterThan(0),
                        workload.Id + " is measured over a repetition count or it has no samples");
                    Assert.That(workload.DefaultDurationSeconds, Is.EqualTo(0));
                }
            }

            Assert.That(steady, Is.EqualTo(3), "the three steady workloads are idle, unchanged composition and execution");
            Assert.That(change, Is.EqualTo(8), "the other eight are repeated changes");
        }

        [Test]
        public void TheRunnerParametersAreTheDeclaredOnes()
        {
            Assert.That(BenchmarkWorkloads.DefaultScopes, Is.EqualTo(1000));
            Assert.That(BenchmarkWorkloads.DefaultTargets, Is.EqualTo(10000));
            Assert.That(BenchmarkWorkloads.DefaultWarmupSeconds, Is.EqualTo(30), "08: warm up for 30 seconds");
            Assert.That(BenchmarkWorkloads.DefaultDurationSeconds, Is.EqualTo(120), "08: a 120-second measurement window");
            Assert.That(BenchmarkWorkloads.DefaultRuns, Is.EqualTo(5), "08: five independent runs");
            Assert.That(BenchmarkWorkloads.DefaultChangeRepetitions, Is.EqualTo(1000), "08: at least 1,000 repetitions for a small change");
            Assert.That(BenchmarkWorkloads.HeavyChangeRepetitions, Is.EqualTo(200));
            Assert.That(BenchmarkWorkloads.HeavyChangeRepetitions, Is.LessThan(BenchmarkWorkloads.DefaultChangeRepetitions), "a whole-world publication is repeated fewer times");
            Assert.That(BenchmarkWorkloads.SpawnTargets, Is.EqualTo(1000), "08: 1,000-target spawns under an already-active provider");
            Assert.That(BenchmarkWorkloads.LifecycleCycleCount, Is.EqualTo(1000), "08: 1,000 lifecycle cycles");
            Assert.That(BenchmarkWorkloads.HundredTargets, Is.EqualTo(100));
            Assert.That(BenchmarkWorkloads.HundredTargets, Is.EqualTo(BenchmarkFixture.HundredTargets));
            Assert.That(BenchmarkWorkloads.DefaultSeed, Is.EqualTo(20260926U), "the recorded fixture seed");

            Assert.That(BenchmarkWorkloads.TryGet(BenchmarkWorkloads.UpdateSizeOne, out BenchmarkWorkload one), Is.True);
            Assert.That(one.DefaultRepetitions, Is.EqualTo(BenchmarkWorkloads.DefaultChangeRepetitions));
            Assert.That(BenchmarkWorkloads.TryGet(BenchmarkWorkloads.UpdateSizeWhole, out BenchmarkWorkload whole), Is.True);
            Assert.That(whole.DefaultRepetitions, Is.EqualTo(BenchmarkWorkloads.HeavyChangeRepetitions), "a whole-world publication repeats fewer times");
            Assert.That(BenchmarkWorkloads.TryGet(BenchmarkWorkloads.SpawnThousand, out BenchmarkWorkload spawn), Is.True);
            Assert.That(spawn.DefaultRepetitions, Is.EqualTo(BenchmarkWorkloads.HeavyChangeRepetitions));
            Assert.That(BenchmarkWorkloads.TryGet(BenchmarkWorkloads.LifecycleCycles, out BenchmarkWorkload lifecycle), Is.True);
            Assert.That(lifecycle.DefaultRepetitions, Is.EqualTo(BenchmarkWorkloads.LifecycleCycleCount));
        }

        [Test]
        public void SelectingTheWholeCatalogueReturnsTheCatalogueItself()
        {
            Assert.That(BenchmarkWorkloads.Select(null, out string nullFailure), Is.SameAs(BenchmarkWorkloads.All));
            Assert.That(nullFailure, Is.EqualTo(string.Empty));
            Assert.That(BenchmarkWorkloads.Select(string.Empty, out string emptyFailure), Is.SameAs(BenchmarkWorkloads.All));
            Assert.That(emptyFailure, Is.EqualTo(string.Empty));
            Assert.That(BenchmarkWorkloads.Select("all", out string allFailure), Is.SameAs(BenchmarkWorkloads.All));
            Assert.That(allFailure, Is.EqualTo(string.Empty));
        }

        [Test]
        public void SelectingOneExactIdReturnsExactlyThatWorkload()
        {
            IReadOnlyList<BenchmarkWorkload> selected = BenchmarkWorkloads.Select("update-size-1", out string failure);

            Assert.That(failure, Is.EqualTo(string.Empty));
            Assert.That(selected.Count, Is.EqualTo(1));
            Assert.That(selected[0].Id, Is.EqualTo(BenchmarkWorkloads.UpdateSizeOne));
            Assert.That(selected[0], Is.SameAs(BenchmarkWorkloads.All[3]));

            IReadOnlyList<BenchmarkWorkload> trimmed = BenchmarkWorkloads.Select("  update-size-1  ", out string trimmedFailure);
            Assert.That(trimmedFailure, Is.EqualTo(string.Empty));
            Assert.That(trimmed.Count, Is.EqualTo(1), "whitespace around an id is trimmed rather than making it unknown");

            IReadOnlyList<BenchmarkWorkload> twice = BenchmarkWorkloads.Select(
                "update-size-1,update-size-1",
                out string twiceFailure);
            Assert.That(twiceFailure, Is.EqualTo(string.Empty));
            Assert.That(twice.Count, Is.EqualTo(1), "a repeated id is one workload, not two samples of the same work");

            IReadOnlyList<BenchmarkWorkload> padded = BenchmarkWorkloads.Select(" , ", out string paddedFailure);
            Assert.That(padded.Count, Is.EqualTo(0));
            Assert.That(paddedFailure.Length, Is.GreaterThan(0), "a selector that names nothing is refused");
            Assert.That(paddedFailure.Contains("named no workload", StringComparison.Ordinal), Is.True);
        }

        [Test]
        public void ASelectionIsReportedInCatalogueOrderNotRequestOrder()
        {
            IReadOnlyList<BenchmarkWorkload> reversed = BenchmarkWorkloads.Select(
                "update-size-100,update-size-1",
                out string failure);

            Assert.That(failure, Is.EqualTo(string.Empty));
            Assert.That(reversed.Count, Is.EqualTo(2));
            Assert.That(
                reversed[0].Id,
                Is.EqualTo(BenchmarkWorkloads.UpdateSizeOne),
                "the catalogue order is the report order, whatever order the selector named");
            Assert.That(reversed[1].Id, Is.EqualTo(BenchmarkWorkloads.UpdateSizeHundred));

            IReadOnlyList<BenchmarkWorkload> interesting = BenchmarkWorkloads.Select(
                "lifecycle-cycles-1000,idle-command-world,update-size-1",
                out string interestingFailure);
            Assert.That(interestingFailure, Is.EqualTo(string.Empty));
            Assert.That(interesting.Count, Is.EqualTo(3));
            Assert.That(interesting[0].Id, Is.EqualTo(BenchmarkWorkloads.IdleCommandWorld));
            Assert.That(interesting[1].Id, Is.EqualTo(BenchmarkWorkloads.UpdateSizeOne));
            Assert.That(interesting[2].Id, Is.EqualTo(BenchmarkWorkloads.LifecycleCycles));

            IReadOnlyList<BenchmarkWorkload> all = BenchmarkWorkloads.Select(BenchmarkWorkloads.JoinIds(), out string idsFailure);
            Assert.That(idsFailure, Is.EqualTo(string.Empty));
            Assert.That(all.Count, Is.EqualTo(BenchmarkWorkloads.All.Count));
            for (int i = 0; i < all.Count; i++)
            {
                Assert.That(all[i].Id, Is.EqualTo(BenchmarkWorkloads.All[i].Id));
            }
        }

        [Test]
        public void AnUnknownIdIsRefusedWithTheKnownIdsInsteadOfBeingSkipped()
        {
            IReadOnlyList<BenchmarkWorkload> selected = BenchmarkWorkloads.Select(
                "update-size-1,nope,update-size-100",
                out string failure);

            Assert.That(selected.Count, Is.EqualTo(0), "one unknown id refuses the whole selection rather than shortening it");
            Assert.That(failure.Contains("nope", StringComparison.Ordinal), Is.True);
            Assert.That(failure.Contains("unknown benchmark workload", StringComparison.Ordinal), Is.True);
            Assert.That(
                failure.Contains(BenchmarkWorkloads.JoinIds(), StringComparison.Ordinal),
                Is.True,
                "the refusal names every known id, so a caller can fix the selector in one run");
        }

        [Test]
        public void JoinIdsNamesEveryWorkloadInCatalogueOrder()
        {
            string joined = BenchmarkWorkloads.JoinIds();

            Assert.That(joined, Is.EqualTo(string.Join(",", DeclaredIds)), "the separator is a bare comma, as the CLI selector accepts");
            for (int i = 0; i < DeclaredIds.Length; i++)
            {
                Assert.That(joined.Contains(DeclaredIds[i], StringComparison.Ordinal), Is.True, "JoinIds names " + DeclaredIds[i]);
            }
        }

        [Test]
        public void AWorkloadRejectsAnEmptyIdOrAnImpossibleWindow()
        {
            Assert.Throws<ArgumentException>(
                () => new BenchmarkWorkload(string.Empty, BenchmarkWorkloadKind.Steady, "d", "x", 1, 0));
            Assert.Throws<ArgumentException>(
                () => new BenchmarkWorkload("id", BenchmarkWorkloadKind.Steady, "d", "x", 0, 0),
                "a steady workload is measured for a positive duration or it cannot be reported as steady");
            Assert.Throws<ArgumentException>(
                () => new BenchmarkWorkload("id", BenchmarkWorkloadKind.Change, "d", "x", 0, 0),
                "a change workload is measured over a positive repetition count or it has no samples");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new BenchmarkWorkload("id", BenchmarkWorkloadKind.Steady, "d", "x", -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new BenchmarkWorkload("id", BenchmarkWorkloadKind.Change, "d", "x", 0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new BenchmarkWorkload("id", BenchmarkWorkloadKind.Steady, "d", "x", 5, -1));

            var workload = new BenchmarkWorkload("id", BenchmarkWorkloadKind.Steady, null!, null!, 5, 0);
            Assert.That(workload.Dimension, Is.EqualTo(string.Empty), "a missing dimension is empty text rather than a crash");
            Assert.That(workload.Description, Is.EqualTo(string.Empty));
            Assert.That(workload.DefaultDurationSeconds, Is.EqualTo(5));
            Assert.That(workload.ToString(), Is.EqualTo("id(Steady, duration=5s, repetitions=0)"));

            var change = new BenchmarkWorkload("change", BenchmarkWorkloadKind.Change, "d", "x", 0, 7);
            Assert.That(change.ToString(), Is.EqualTo("change(Change, duration=0s, repetitions=7)"));
            Assert.That((int)BenchmarkWorkloadKind.Steady, Is.EqualTo(0), "the kind ids are part of the recorded document");
            Assert.That((int)BenchmarkWorkloadKind.Change, Is.EqualTo(1));
        }
    }
}
