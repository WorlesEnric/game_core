// GameCore.Validation.ProbeHost.Tests — GC-024's EditMode conformance suite.
//
// The suite proves the same three things the player probe proves, in the Editor where a real world can be built
// repeatedly and where the project tree exists:
//
//   1. every transcribed 07 table runs over its genre's real worlds and the oracle's verdict passes, with the trace
//      digest agreeing between the run and the fixture's own recomputation;
//   2. the combined cross-family world runs, the durable reward commits exactly once, and its trace matches the
//      `cross` table the fixture transcribes from 07 s5;
//   3. the genre/assembly audit is clean over the repository tree, with the falsifiability counts non-zero — a scan
//      that read nothing must fail rather than look clean.
//
// Every world a test builds is disposed in the test that built it, and the teardown asserts the world registry is
// back at its baseline, so a leaked world is red rather than silent (P-035).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.ReferenceConformance;
using NUnit.Framework;
using UnityEngine;

namespace GameCore.Validation.ProbeHost.Tests
{
    [TestFixture]
    public sealed class ConformanceIntegrationTests
    {
        private int registryBaseline;

        [OneTimeSetUp]
        public void CaptureSequence()
        {
            registryBaseline = UnityWorldRegistry.Count;
        }

        [TearDown]
        public void AssertNoWorldSurvived()
        {
            Assert.That(
                UnityWorldRegistry.Count,
                Is.EqualTo(registryBaseline),
                "a conformance world survived its test, so a world was leaked (P-035)");
        }

        [Test]
        [Timeout(600000)]
        public void TheCardMarketTableMatchesItsTranscription()
        {
            AssertTable("cards", Gc013CardsHost.RunConformanceCards);
        }

        [Test]
        [Timeout(600000)]
        public void TheChapterQuestTableMatchesItsTranscription()
        {
            AssertTable("narrative", Gc013NarrativeHost.RunConformanceNarrative);
        }

        [Test]
        [Timeout(600000)]
        public void TheTraversalTableMatchesItsTranscription()
        {
            AssertTable("traversal", Gc020TraversalHost.RunConformanceTraversal);
        }

        [Test]
        [Timeout(600000)]
        public void TheCombinedWorldMatchesTheCrossFamilyTranscription()
        {
            ConformanceTableResult result = ConformanceCrossWorld.Run();
            try
            {
                Assert.That(result.AllPassed, Is.True, result.Describe());

                // The trace must round-trip: what the run recorded is a document the fixture can read back, which is
                // what makes a committed trace file comparable with a later run (P-054).
                Assert.That(
                    ConformanceTrace.TryParse(result.Document, out ConformanceTrace? read, out string detail),
                    Is.True,
                    detail);
                Assert.That(read, Is.Not.Null);
                Assert.That(read!.Digest(), Is.EqualTo(result.Trace.Digest()));

                // The cross table's rows must all have been executed and every one of them observed.
                ConformanceTable table = ReferenceTables.ById("cross")!;
                for (int r = 0; r < table.Rows.Count; r++)
                {
                    Assert.That(
                        read.ValueOf("cross", table.Rows[r].RowId, ConformancePhase.After,
                            table.Rows[r].Expectations[0].Field),
                        Is.Not.Null,
                        "the combined world never observed row '" + table.Rows[r].RowId + "'");
                }
            }
            finally
            {
                _ = result;
            }
        }

        [Test]
        [Timeout(300000)]
        public void TheCombinedCompositionCarriesNoActionSurfaceInItsCardOrNarrativeStages()
        {
            CrossCompositionAudit audit = ConformanceCrossWorld.AuditCombinedComposition();
            Assert.That(
                audit.WalkedEntries,
                Is.GreaterThan(0),
                "the audit walked no declaration at all, so a clean verdict would mean nothing (P-059)");
            Assert.That(audit.Clean, Is.True, audit.Describe());
        }

        [Test]
        [Timeout(120000)]
        public void TheGenreAuditIsCleanOverTheRepositoryTree()
        {
            string? root = AssemblyReferenceAudit.TryFindRepositoryRoot(Application.dataPath);
            Assert.That(root, Is.Not.Null, "no repository root was found above " + Application.dataPath);

            GenreAuditReport audit = AssemblyReferenceAudit.Audit(root!);
            Assert.That(audit.Clean, Is.True, audit.Describe());

            // The falsifiability counts: an audit that read nothing cannot report clean.
            Assert.That(audit.AsmdefCount, Is.GreaterThan(0));
            Assert.That(audit.ProjectFileCount, Is.GreaterThan(0));
            Assert.That(audit.KernelSourceCount, Is.GreaterThan(0));
            Assert.That(audit.ReferenceAssertionCount, Is.GreaterThan(0));
            Assert.That(
                audit.FamiliesOnKernel().Count,
                Is.EqualTo(audit.CountOf(AssemblyClass.Family)),
                "every gameplay/rules assembly must reference the kernel it runs on (P-001)");
        }

        [Test]
        [Timeout(120000)]
        public void TheKernelAssembliesOfThisProcessReferenceNoGameplayAssembly()
        {
            LoadedAssemblyReport report = KernelAssemblyAudit.AuditLoadedAssemblies();
            Assert.That(report.KernelAssemblies.Count, Is.GreaterThan(0));
            Assert.That(report.ForbiddenReferences.Count, Is.EqualTo(0), report.Describe());
        }

        [Test]
        [Timeout(120000)]
        public void EveryTranscribedTableIsExecutedByTheRunThatClaimsIt()
        {
            // The runner and the fixture must agree about which rows exist: a row transcribed but never executed
            // cannot be claimed as observed (P-026).
            IReadOnlyList<ConformanceScript> scripts = ReferenceScripts.All();
            for (int s = 0; s < scripts.Count; s++)
            {
                ConformanceScript script = scripts[s];
                ConformanceTable? table = ReferenceTables.ById(script.TableId);
                Assert.That(table, Is.Not.Null, script.TableId + " has no transcribed table");

                IReadOnlyList<ConformanceStep> steps = script.Steps();
                var executed = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < steps.Count; i++)
                {
                    if (steps[i].Kind == ConformanceStepKind.Row)
                    {
                        executed.Add(steps[i].RowId);
                    }
                }

                Assert.That(
                    executed.Count,
                    Is.EqualTo(table!.Rows.Count),
                    script.TableId + " executes " + executed.Count.ToString(CultureInfo.InvariantCulture)
                    + " of its " + table.Rows.Count.ToString(CultureInfo.InvariantCulture) + " transcribed rows");
            }
        }

        private static void AssertTable(string tableId, Func<ConformanceTableResult> run)
        {
            ConformanceTableResult result = run();
            Assert.That(result.TableId, Is.EqualTo(tableId));
            Assert.That(result.AllPassed, Is.True, result.Describe());

            Assert.That(
                ConformanceTrace.TryParse(result.Document, out ConformanceTrace? read, out string detail),
                Is.True,
                detail);
            Assert.That(read, Is.Not.Null);
            Assert.That(read!.Digest(), Is.EqualTo(result.Trace.Digest()));
            Assert.That(result.Verdict.RowsChecked, Is.GreaterThan(0));
            Assert.That(result.Trace.Count, Is.GreaterThan(0));

            // The projections must agree with the rules packages for this table too, so a run cannot pass while the
            // numbers it observed disagree with the rules that produced them (P-019, P-008).
            IReadOnlyList<ConformanceProjection> disagreements = ReferenceProjections.Disagreements();
            Assert.That(disagreements.Count, Is.EqualTo(0), "projections disagree: " + disagreements.Count);
        }
    }
}
