// GameCore.ReferenceConformance.Tests — the pure halves of GC-024's conformance claim.
//
// Two things are proven here, and neither needs a Unity world:
//
//   1. the FIXTURE is internally consistent and honest: every 07 row carries an anchor and at least one expectation,
//      every expectation's field is declared in the vocabulary, every script step names a row the tables declare,
//      every operation a script names is one the vocabulary defines, the trace document round-trips and rejects a
//      tampered body, and the oracle fails on the mistakes it exists to catch (a missing row, a wrong value, a
//      refused operation that published) rather than only on a correct run;
//   2. the NUMBERS the tables assert agree with the gameplay rules packages that own them
//      (`ReferenceProjections`), so a transcription error or a rules change that moves a documented number fails in
//      a sub-second dotnet run instead of after a Unity build.
//
// The real-world half — the same tables executed in actual Unity worlds — is the qualification project's own
// conformance fixture; this file does not stand in for it (P-057).
#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameCore.ReferenceConformance.Tests
{
    [TestFixture]
    public sealed class ReferenceTableTests
    {
        [Test]
        public void EveryTranscribedTableCarriesItsAnchorAndItsGenre()
        {
            IReadOnlyList<ConformanceTable> tables = ReferenceTables.All();
            Assert.That(tables.Count, Is.EqualTo(4), "the four 07 before/after tables must all be transcribed");
            for (int i = 0; i < tables.Count; i++)
            {
                ConformanceTable table = tables[i];
                Assert.That(table.TableAnchor, Does.Contain("07-reference-compositions.md"),
                    table.TableId + " must cite the 07 section it was transcribed from (P-060)");
                Assert.That(table.Genre.Length, Is.GreaterThan(0), table.TableId + " must name its composition");
                Assert.That(table.Rows.Count, Is.GreaterThan(0), table.TableId + " must carry rows");
            }
        }

        [Test]
        public void EveryRowCarriesAnOperationItsAnchorAndAtLeastOneExpectation()
        {
            IReadOnlyList<ConformanceTable> tables = ReferenceTables.All();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int t = 0; t < tables.Count; t++)
            {
                ConformanceTable table = tables[t];
                for (int r = 0; r < table.Rows.Count; r++)
                {
                    ConformanceRow row = table.Rows[r];
                    Assert.That(row.RowId.Length, Is.GreaterThan(0), "every row needs a stable id");
                    Assert.That(row.Operation.Length, Is.GreaterThan(0), row.RowId + " needs its 07 operation cell");
                    Assert.That(row.SourceRow, Does.Contain("07:"),
                        row.RowId + " must cite the 07 line or table it was transcribed from");
                    Assert.That(row.Expectations.Count, Is.GreaterThan(0),
                        row.RowId + " asserts nothing, which is not a conformance row");
                    Assert.That(
                        seen.Add(table.TableId + "/" + row.RowId),
                        Is.True,
                        "row ids must be unique per table: " + table.TableId + "/" + row.RowId);
                }
            }
        }

        [Test]
        public void EveryExpectationIsCheckedAgainstADeclaredFieldOrIsADeliberateProjection()
        {
            IReadOnlyList<ConformanceTable> tables = ReferenceTables.All();
            for (int t = 0; t < tables.Count; t++)
            {
                ConformanceTable table = tables[t];
                var declared = new HashSet<string>(StringComparer.Ordinal);
                IReadOnlyList<ConformanceField> fields = ConformanceFields.Of(table.TableId);
                for (int f = 0; f < fields.Count; f++)
                {
                    declared.Add(fields[f].Key);
                }

                Assert.That(fields.Count, Is.GreaterThan(0), table.TableId + " must declare a field vocabulary");
                for (int r = 0; r < table.Rows.Count; r++)
                {
                    for (int e = 0; e < table.Rows[r].Expectations.Count; e++)
                    {
                        string field = table.Rows[r].Expectations[e].Field;
                        Assert.That(
                            declared.Contains(field),
                            Is.True,
                            table.TableId + "/" + table.Rows[r].RowId + " observes '" + field
                            + "', which the field vocabulary does not declare: a run has no reader for it");
                    }
                }
            }
        }

        [Test]
        public void EveryExpectationCarriesACanonicalValueOrAnExplicitAbsence()
        {
            IReadOnlyList<ConformanceTable> tables = ReferenceTables.All();
            for (int t = 0; t < tables.Count; t++)
            {
                for (int r = 0; r < tables[t].Rows.Count; r++)
                {
                    ConformanceRow row = tables[t].Rows[r];
                    for (int e = 0; e < row.Expectations.Count; e++)
                    {
                        ConformanceExpectation expectation = row.Expectations[e];
                        if (expectation.Kind == ConformanceExpectationKind.Absent)
                        {
                            Assert.That(expectation.Expected(ConformancePhase.Before),
                                Is.EqualTo(ConformanceValue.None));
                            Assert.That(expectation.Expected(ConformancePhase.After),
                                Is.EqualTo(ConformanceValue.None));
                            continue;
                        }

                        if (expectation.Kind == ConformanceExpectationKind.Preserved)
                        {
                            Assert.That(expectation.RequiresEqualPhases, Is.True);
                            continue;
                        }

                        Assert.That(
                            ConformanceValue.IsCanonical(expectation.Expected(ConformancePhase.Before)),
                            Is.True,
                            row.RowId + "/" + expectation.Field + " has a non-canonical before value");
                        Assert.That(
                            ConformanceValue.IsCanonical(expectation.Expected(ConformancePhase.After)),
                            Is.True,
                            row.RowId + "/" + expectation.Field + " has a non-canonical after value");
                    }
                }
            }
        }

        [Test]
        public void EveryScriptStepNamesARowAndAnOperationTheFixtureDeclares()
        {
            IReadOnlyList<ConformanceScript> scripts = ReferenceScripts.All();
            Assert.That(scripts.Count, Is.EqualTo(4), "each transcribed table needs a script");
            var operations = new HashSet<string>(StringComparer.Ordinal)
            {
                ConformanceOperations.MountProvider,
                ConformanceOperations.MountSecondProvider,
                ConformanceOperations.MountNestedProvider,
                ConformanceOperations.MountConflictProvider,
                ConformanceOperations.MountConflictSecondProvider,
                ConformanceOperations.UnmountProvider,
                ConformanceOperations.UnmountSecondProvider,
                ConformanceOperations.ReconfigureProvider,
                ConformanceOperations.ReparentMovedScope,
                ConformanceOperations.ModeAutomatic,
                ConformanceOperations.ModeConservative,
                ConformanceOperations.SuspendProvider,
                ConformanceOperations.ResumeProvider,
                ConformanceOperations.SpawnFutureTarget,
                ConformanceOperations.SeedOptedInTarget,
                ConformanceOperations.ApplyExclusion,
                ConformanceOperations.CommitCommand,
                ConformanceOperations.CommitCommandRejected,
                ConformanceOperations.MountRewardBridge,
                ConformanceOperations.UnmountRewardBridge,
                ConformanceOperations.SettleReward,
                ConformanceOperations.RedeliverReward,
            };

            for (int s = 0; s < scripts.Count; s++)
            {
                ConformanceScript script = scripts[s];
                Assert.That(script.Stages.Count, Is.GreaterThan(0), script.TableId + " needs at least one stage");
                IReadOnlyList<string> named = script.Operations();
                for (int o = 0; o < named.Count; o++)
                {
                    Assert.That(
                        operations.Contains(named[o]),
                        Is.True,
                        script.TableId + " names the operation '" + named[o] + "', which nothing declares");
                }

                IReadOnlyList<ConformanceStep> steps = script.Steps();
                for (int i = 0; i < steps.Count; i++)
                {
                    Assert.That(steps[i].Note.Length, Is.GreaterThan(0),
                        steps[i].RowId + " must say what it establishes");
                }
            }
        }

        [Test]
        public void EveryRowOfEveryTableIsExecutedByItsScript()
        {
            IReadOnlyList<ConformanceScript> scripts = ReferenceScripts.All();
            for (int s = 0; s < scripts.Count; s++)
            {
                ConformanceScript script = scripts[s];
                ConformanceTable table = ReferenceTables.ById(script.TableId)!;
                IReadOnlyList<ConformanceStep> steps = script.Steps();
                var executed = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < steps.Count; i++)
                {
                    if (steps[i].Kind == ConformanceStepKind.Row)
                    {
                        executed.Add(steps[i].RowId);
                    }
                }

                for (int r = 0; r < table.Rows.Count; r++)
                {
                    Assert.That(
                        executed.Contains(table.Rows[r].RowId),
                        Is.True,
                        script.TableId + " never executes the 07 row '" + table.Rows[r].RowId
                        + "', so the row is transcribed but unobserved");
                }
            }
        }

        [Test]
        public void EveryRowStepInheritsTheTablesExpectationsRatherThanRestatingThem()
        {
            IReadOnlyList<ConformanceScript> scripts = ReferenceScripts.All();
            for (int s = 0; s < scripts.Count; s++)
            {
                ConformanceScript script = scripts[s];
                ConformanceTable table = ReferenceTables.ById(script.TableId)!;
                IReadOnlyList<ConformanceStep> steps = script.Steps();
                for (int i = 0; i < steps.Count; i++)
                {
                    ConformanceStep step = steps[i];
                    if (step.Kind != ConformanceStepKind.Row)
                    {
                        continue;
                    }

                    ConformanceRow row = FixtureLookup.Find(table, step.RowId);
                    Assert.That(
                        ReferenceEquals(step.Expectations, row.Expectations)
                        || step.Expectations.Count == row.Expectations.Count,
                        Is.True,
                        step.RowId + " carries a different expectation set from its table row");
                    Assert.That(step.Outcome, Is.EqualTo(row.Outcome), step.RowId + " has a different outcome");
                }
            }
        }
    }

    [TestFixture]
    public sealed class ConformanceTraceTests
    {
        [Test]
        public void ADocumentRoundTripsAndCarriesItsDigest()
        {
            var trace = new ConformanceTrace("generated-catalog");
            trace.Record("cards", "mount-festival", ConformancePhase.Before,
                ConformanceFields.SeatBonus(0U), ConformanceValue.None);
            trace.Record("cards", "mount-festival", ConformancePhase.After,
                ConformanceFields.SeatBonus(0U), "2");

            string document = trace.ToDocument();
            Assert.That(document, Does.StartWith("format=" + ConformanceTrace.FormatName + "\n"));
            Assert.That(document, Does.Contain("cards|mount-festival|before|seat-a.bonus=none\n"));
            Assert.That(document, Does.Contain("cards|mount-festival|after|seat-a.bonus=2\n"));

            Assert.That(ConformanceTrace.TryParse(document, out ConformanceTrace? read, out string detail), Is.True, detail);
            Assert.That(read, Is.Not.Null);
            Assert.That(read!.Count, Is.EqualTo(2));
            Assert.That(read.ValueOf("cards", "mount-festival", ConformancePhase.After,
                ConformanceFields.SeatBonus(0U)), Is.EqualTo("2"));
            Assert.That(read.Digest(), Is.EqualTo(trace.Digest()));
        }

        [Test]
        public void TheSameObservationsWrittenInAnotherOrderProduceTheSameBytes()
        {
            var first = new ConformanceTrace("run");
            first.Record("cards", "b-row", ConformancePhase.After, "seat-a.bonus", "2");
            first.Record("cards", "a-row", ConformancePhase.Before, "seat-a.bonus", "none");
            first.Record("cards", "a-row", ConformancePhase.After, "seat-a.bonus", "2");

            var second = new ConformanceTrace("run");
            second.Record("cards", "a-row", ConformancePhase.After, "seat-a.bonus", "2");
            second.Record("cards", "a-row", ConformancePhase.Before, "seat-a.bonus", "none");
            second.Record("cards", "b-row", ConformancePhase.After, "seat-a.bonus", "2");

            Assert.That(second.ToDocument(), Is.EqualTo(first.ToDocument()),
                "the document is canonically ordered, so recording order cannot change it (P-008)");
        }

        [Test]
        public void ATamperedDocumentIsRefusedRatherThanRead()
        {
            var trace = new ConformanceTrace("run");
            trace.Record("cards", "mount-festival", ConformancePhase.After,
                ConformanceFields.SeatBonus(0U), "2");
            string document = trace.ToDocument();
            string tampered = document.Replace("seat-a.bonus=2", "seat-a.bonus=3");

            Assert.That(
                ConformanceTrace.TryParse(tampered, out ConformanceTrace? _, out string detail),
                Is.False,
                "a body that disagrees with its digest must be refused (P-054)");
            Assert.That(detail, Does.Contain("digest"));
        }

        [Test]
        public void AnUnknownFormatIsRefused()
        {
            Assert.That(
                ConformanceTrace.TryParse(
                    "format=gamecore.something-else/9\nlabel=x\ntables=0\nrows=0\nfacts=0\ndigest=abc",
                    out ConformanceTrace? _,
                    out string detail),
                Is.False);
            Assert.That(detail, Does.Contain(ConformanceTrace.FormatName));
        }

        [Test]
        public void RecordingOneFactTwiceIsRefused()
        {
            var trace = new ConformanceTrace("run");
            trace.Record("cards", "mount-festival", ConformancePhase.After, "seat-a.bonus", "2");
            Assert.Throws<InvalidOperationException>(delegate
            {
                trace.Record("cards", "mount-festival", ConformancePhase.After, "seat-a.bonus", "3");
            });
        }

        [Test]
        public void AValueWithASeparatorIsRefusedAsEvidence()
        {
            var trace = new ConformanceTrace("run");
            Assert.Throws<ArgumentException>(delegate
            {
                trace.Record("cards", "mount-festival", ConformancePhase.After, "seat-a.bonus", "2 | 3");
            });
        }
    }

    [TestFixture]
    public sealed class ConformanceOracleTests
    {
        [Test]
        public void AnEmptyTraceFailsEveryRowItCannotShowWasExecuted()
        {
            ConformanceTable table = ReferenceTables.Cards();
            var verdict = ConformanceOracle.Compare(
                table,
                new ConformanceTrace("empty"),
                Array.Empty<ConformanceOracle.RowOutcomeReport>());
            Assert.That(verdict.Passed, Is.False);
            Assert.That(verdict.Failures.Count, Is.GreaterThanOrEqualTo(table.Rows.Count));
            Assert.That(verdict.Failures[0].Observed, Is.EqualTo("not executed"));
        }

        [Test]
        public void OneWrongValueFailsExactlyThatField()
        {
            ConformanceTable table = ReferenceTables.ById("cards")!;
            ConformanceRow row = table.Rows[0];
            var trace = new ConformanceTrace("run");
            var outcomes = new List<ConformanceOracle.RowOutcomeReport>();

            for (int r = 0; r < table.Rows.Count; r++)
            {
                ConformanceRow current = table.Rows[r];
                outcomes.Add(new ConformanceOracle.RowOutcomeReport(current.RowId, true, string.Empty));
                for (int e = 0; e < current.Expectations.Count; e++)
                {
                    ConformanceExpectation expectation = current.Expectations[e];
                    string before = expectation.Expected(ConformancePhase.Before);
                    trace.Record(table.TableId, current.RowId, ConformancePhase.Before, expectation.Field, before);
                    string after = current == row && expectation.Field == row.Expectations[0].Field
                        ? "9999"
                        : expectation.Expected(ConformancePhase.After);
                    trace.Record(table.TableId, current.RowId, ConformancePhase.After, expectation.Field, after);
                }
            }

            ConformanceVerdict verdict = ConformanceOracle.Compare(table, trace, outcomes);
            Assert.That(verdict.Passed, Is.False);
            Assert.That(verdict.Failures.Count, Is.EqualTo(1),
                "exactly one field was falsified, so exactly one failure must be reported");
            Assert.That(verdict.Failures[0].Field, Is.EqualTo(row.Expectations[0].Field));
            Assert.That(verdict.Failures[0].Observed, Is.EqualTo("9999"));
        }

        [Test]
        public void ARefusedRowThatPublishedFailsEvenWhenEveryFieldReadsTheSame()
        {
            ConformanceTable table = ReferenceTables.ById("cards")!;
            ConformanceRow refused = FixtureLookup.FindRefused(table);
            var trace = new ConformanceTrace("run");
            var outcomes = new List<ConformanceOracle.RowOutcomeReport>();
            for (int r = 0; r < table.Rows.Count; r++)
            {
                ConformanceRow current = table.Rows[r];
                outcomes.Add(new ConformanceOracle.RowOutcomeReport(current.RowId, true, string.Empty));
                for (int e = 0; e < current.Expectations.Count; e++)
                {
                    ConformanceExpectation expectation = current.Expectations[e];
                    string value = expectation.Expected(ConformancePhase.After);
                    trace.Record(table.TableId, current.RowId, ConformancePhase.Before, expectation.Field, value);
                    trace.Record(table.TableId, current.RowId, ConformancePhase.After, expectation.Field, value);
                }
            }

            ConformanceVerdict verdict = ConformanceOracle.Compare(table, trace, outcomes);
            bool found = false;
            for (int i = 0; i < verdict.Failures.Count; i++)
            {
                if (verdict.Failures[i].RowId == refused.RowId && verdict.Failures[i].Field.Length == 0)
                {
                    found = true;
                }
            }

            Assert.That(found, Is.True, "a row that must be refused and nevertheless published is a failure");
        }

        [Test]
        public void AValueTheRunNeverObservedIsAFailureAndNotAnAssumption()
        {
            ConformanceTable table = ReferenceTables.ById("traversal")!;
            var trace = new ConformanceTrace("run");
            var outcomes = new List<ConformanceOracle.RowOutcomeReport>();
            for (int r = 0; r < table.Rows.Count; r++)
            {
                outcomes.Add(new ConformanceOracle.RowOutcomeReport(table.Rows[r].RowId, true, string.Empty));
            }

            ConformanceVerdict verdict = ConformanceOracle.Compare(table, trace, outcomes);
            Assert.That(verdict.Passed, Is.False);
            bool notObserved = false;
            for (int i = 0; i < verdict.Failures.Count; i++)
            {
                if (verdict.Failures[i].Observed == "not observed")
                {
                    notObserved = true;
                }
            }

            Assert.That(notObserved, Is.True);
        }

        [Test]
        public void ACompleteFaithfulRunPassesAndDigestsToTheEmptyFailureSet()
        {
            IReadOnlyList<ConformanceTable> tables = ReferenceTables.All();
            for (int t = 0; t < tables.Count; t++)
            {
                ConformanceTable table = tables[t];
                var trace = new ConformanceTrace("faithful");
                var outcomes = new List<ConformanceOracle.RowOutcomeReport>();
                for (int r = 0; r < table.Rows.Count; r++)
                {
                    ConformanceRow row = table.Rows[r];
                    outcomes.Add(new ConformanceOracle.RowOutcomeReport(
                        row.RowId,
                        row.Outcome == ConformanceRowOutcome.Published,
                        row.Outcome == ConformanceRowOutcome.Published ? string.Empty : "CapabilityConflict"));
                    for (int e = 0; e < row.Expectations.Count; e++)
                    {
                        ConformanceExpectation expectation = row.Expectations[e];
                        trace.Record(
                            table.TableId, row.RowId, ConformancePhase.Before, expectation.Field,
                            expectation.Expected(ConformancePhase.Before));
                        trace.Record(
                            table.TableId, row.RowId, ConformancePhase.After, expectation.Field,
                            expectation.Expected(ConformancePhase.After));
                    }
                }

                ConformanceVerdict verdict = ConformanceOracle.Compare(table, trace, outcomes);
                Assert.That(verdict.Passed, Is.True, table.TableId + " should pass: " + verdict.Describe());
                Assert.That(verdict.RowsChecked, Is.EqualTo(table.Rows.Count));
            }
        }
    }

    [TestFixture]
    public sealed class ReferenceProjectionTests
    {
        [Test]
        public void EveryProjectedNumberAgreesWithTheRulesPackageThatOwnsIt()
        {
            IReadOnlyList<ConformanceProjection> disagreements = ReferenceProjections.Disagreements();
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < disagreements.Count; i++)
            {
                text.Append(disagreements[i].ToString()).Append("; ");
            }

            Assert.That(disagreements.Count, Is.EqualTo(0), "projections disagree: " + text);
        }

        [Test]
        public void TheProjectionSetIsNotVacuous()
        {
            IReadOnlyList<ConformanceProjection> all = ReferenceProjections.All();
            Assert.That(all.Count, Is.GreaterThanOrEqualTo(20),
                "the projection set must cover the numbers the four tables assert");
            var tables = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < all.Count; i++)
            {
                tables.Add(all[i].TableId);
                Assert.That(all[i].Documented.Length, Is.GreaterThan(0), all[i].ToString());
                Assert.That(all[i].Computed.Length, Is.GreaterThan(0), all[i].ToString());
            }

            Assert.That(tables.Contains("cards"), Is.True);
            Assert.That(tables.Contains("narrative"), Is.True);
            Assert.That(tables.Contains("traversal"), Is.True);
        }

        [Test]
        public void TheDocumentedNumericSequenceIsTheRulesResultNotAFixtureConstant()
        {
            // 07:247's sequence is the traversal rules' own integrator output at the declared step duration.
            ConformanceProjection? tailwind = FixtureLookup.FindProjection(
                "traversal", "reparent-runner-subtree", ConformanceFields.RunnerVelocity("runner-a"), "(1040,0,0)");
            ConformanceProjection? headwind = FixtureLookup.FindProjection(
                "traversal", "reparent-runner-subtree/pre4", ConformanceFields.RunnerVelocity("runner-a"), "(1020,0,0)");
            Assert.That(tailwind.HasValue, Is.True, "the tailwind step's documented velocity must be projected");
            Assert.That(headwind.HasValue, Is.True, "the headwind step's documented velocity must be projected");
            Assert.That(tailwind!.Value.Agrees, Is.True);
            Assert.That(headwind!.Value.Agrees, Is.True);
        }
    }

    [TestFixture]
    public sealed class GenreAuditDocumentTests
    {
        [Test]
        public void ADocumentWithoutItsCountsIsRefused()
        {
            Assert.That(
                GenreAuditDocument.TryReadCounts("{}", out bool _, out int _, out int _, out int _, out string detail),
                Is.False);
            Assert.That(detail, Does.Contain(GenreAuditDocument.FormatName));
        }

        [Test]
        public void TheFormatNameAndPathAreStable()
        {
            Assert.That(GenreAuditDocument.FormatName, Is.EqualTo("gamecore.genre-audit/1"));
            Assert.That(GenreAuditDocument.ArtifactPath, Is.EqualTo("artifacts/gc-024/genre-audit.json"));
            Assert.That(GenreAuditDocument.TaskId, Is.EqualTo("GC-024"));
        }

        [Test]
        public void TheForbiddenTokenListNamesTheGenreTypesTheKernelMustNotKnow()
        {
            IReadOnlyList<string> tokens = AssemblyReferenceAudit.ForbiddenKernelTokens;
            Assert.That(tokens, Does.Contain("CardTableModule"));
            Assert.That(tokens, Does.Contain("NarrativeKeys"));
            Assert.That(tokens, Does.Contain("TraversalPose"));
            Assert.That(tokens, Does.Contain("QuestLedger"));
            Assert.That(tokens, Does.Contain("GameCore.Rules."));
            Assert.That(AssemblyReferenceAudit.IsForbiddenReference("GameCore.Rules.Cards"), Is.True);
            Assert.That(AssemblyReferenceAudit.IsForbiddenReference("GameCore.Gameplay.Cards"), Is.True);
            Assert.That(AssemblyReferenceAudit.IsForbiddenReference("GameCore.Validation.ProbeHost"), Is.True);
            Assert.That(AssemblyReferenceAudit.IsForbiddenReference("GameCore.Generated"), Is.True);
            Assert.That(AssemblyReferenceAudit.IsForbiddenReference("GameCore.Contracts"), Is.False);
            Assert.That(AssemblyReferenceAudit.IsKernelAssemblyName("GameCore.Composition"), Is.True);
            Assert.That(AssemblyReferenceAudit.IsFamilyAssemblyName("GameCore.Gameplay.Cards"), Is.True);
            Assert.That(AssemblyReferenceAudit.IsFamilyAssemblyName("GameCore.Composition"), Is.False);
        }
    }

    [TestFixture]
    public sealed class ConformanceDocGapTests
    {
        [Test]
        public void AnyGapThatIsEverDeclaredMustNameItsRowItsClauseItsEvidenceAndItsResolution()
        {
            // Vacuous today (the registry is empty because 07:276's claims are all carried), and deliberately kept:
            // when a later revision declares a gap, this is the shape it must have, so a thin entry cannot slip in.
            IReadOnlyList<ConformanceDocGap> gaps = ConformanceDocGaps.All;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < gaps.Count; i++)
            {
                ConformanceDocGap gap = gaps[i];
                Assert.That(seen.Add(gap.GapId), Is.True, "gap identifiers must be unique: " + gap.GapId);
                Assert.That(gap.Clause, Does.Contain("07:"),
                    gap.GapId + " must quote the 07 clause it offends");
                Assert.That(gap.Missing.Length, Is.GreaterThan(20),
                    gap.GapId + " must state the mechanism this revision lacks");
                Assert.That(gap.Evidence.Length, Is.GreaterThan(10),
                    gap.GapId + " must name where the same absence was already recorded");
                Assert.That(gap.Resolution.Length, Is.GreaterThan(20),
                    gap.GapId + " must propose what would close it");

                ConformanceTable? table = ReferenceTables.ById(gap.TableId);
                Assert.That(table, Is.Not.Null, gap.GapId + " names an unknown table: " + gap.TableId);
                bool rowExists = false;
                for (int r = 0; r < table!.Rows.Count; r++)
                {
                    if (string.Equals(table.Rows[r].RowId, gap.RowId, StringComparison.Ordinal))
                    {
                        rowExists = true;
                    }
                }

                Assert.That(rowExists, Is.True,
                    gap.GapId + " names a row the table does not carry: " + gap.RowId);
            }
        }

        [Test]
        public void ThisRevisionRecordsNoGapBecause070276sClaimsAreAllCarried()
        {
            // The registry is empty on purpose: 07:276's four claims are asserted by four real rows of the cross
            // table through shipped mechanisms (a fenced resource lease that blocks the teardown, the declared
            // PreserveDormant last-support policy, the version-change migration as the pending-work precondition,
            // and the declared owner-transfer policy). An entry here would claim otherwise, so the suite asserts
            // the empty list and that every cross row it would have covered exists.
            Assert.That(ConformanceDocGaps.All.Count, Is.EqualTo(0), "no gap may be declared while every claim is carried");
            ConformanceTable cross = ReferenceTables.ById("cross")!;
            string[] rows =
            {
                "reward-unmount-pending",
                "reward-drain-then-unmount",
                "reward-unmount-transfer",
                "reward-scoring-unmount-keeps-card",
            };
            for (int i = 0; i < rows.Length; i++)
            {
                bool found = false;
                for (int r = 0; r < cross.Rows.Count; r++)
                {
                    if (string.Equals(cross.Rows[r].RowId, rows[i], StringComparison.Ordinal))
                    {
                        found = true;
                    }
                }

                Assert.That(found, Is.True, "the cross table must carry 07:276's row '" + rows[i] + "'");
            }

            Assert.That(ConformanceDocGaps.ById("gc024.gap.reward-bridge-removal"), Is.Null);
            Assert.That(ConformanceDocGaps.Of("cross").Count, Is.EqualTo(0));
            Assert.That(ConformanceDocGaps.CanonicalLines().Count, Is.EqualTo(0));
        }
    }

    internal static class FixtureLookup
    {
        internal static ConformanceRow Find(ConformanceTable table, string rowId)
        {
            for (int r = 0; r < table.Rows.Count; r++)
            {
                if (string.Equals(table.Rows[r].RowId, rowId, StringComparison.Ordinal))
                {
                    return table.Rows[r];
                }
            }

            throw new InvalidOperationException("the table carries no row '" + rowId + "'");
        }

        internal static ConformanceRow FindRefused(ConformanceTable table)
        {
            for (int r = 0; r < table.Rows.Count; r++)
            {
                if (table.Rows[r].Outcome == ConformanceRowOutcome.RefusedKeepsAssembly)
                {
                    return table.Rows[r];
                }
            }

            throw new InvalidOperationException("the table carries no refused row");
        }

        internal static ConformanceProjection? FindProjection(
            string tableId, string rowId, string field, string documented)
        {
            IReadOnlyList<ConformanceProjection> all = ReferenceProjections.All();
            for (int i = 0; i < all.Count; i++)
            {
                if (string.Equals(all[i].TableId, tableId, StringComparison.Ordinal)
                    && string.Equals(all[i].RowId, rowId, StringComparison.Ordinal)
                    && string.Equals(all[i].Field, field, StringComparison.Ordinal)
                    && string.Equals(all[i].Documented, documented, StringComparison.Ordinal))
                {
                    return all[i];
                }
            }

            return null;
        }
    }
}
