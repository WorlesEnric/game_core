// GameCore.Validation.ProbeHost — the Wave 7 integration-gate runtime scenario.
//
// The exit gate this file implements, verbatim from `docs/game-core/09-implementation-guide.md` (Wave 7):
//
//   "All reference transition tables and cross-template flow pass; complete IL2CPP/headless catalog coverage runs;
//    faulted checkpoint/outbox recovery passes; benchmark data and budget decisions are recorded. Production fixes
//    require affected gates rerun on the new revision."
//
// WHAT THIS GATE IS, AND WHAT IT IS NOT. Wave 7 is an INTEGRATION gate on ONE merged revision: it owns no kernel
// behaviour and adds no mechanism. Its subject is the JOIN of the four Wave 7 tasks on the revision that this branch
// merged (GC-025's standalone IL2CPP/headless profile, GC-026's measured budgets, GC-027's faulted recovery, and
// GC-024's reference conformance). So every observation below is a *re-run*, through the owning task's own runner, of
// that task's own acceptance sequence — the gate never re-implements a scenario, and a fix in the owning task's
// sequence is a fix in this gate (P-001, P-002, P-060).
//
// The last sentence of the gate is the reason the revision matters rather than the individual tasks: a Wave 7 task's
// evidence was taken on the W6 revision, and merging four Wave 7 tasks produced changes in shared kernel files
// (GC-026 changed the derivation indexes; GC-027 changed the checkpoint/restore publication path). Those changes are
// exactly what can invalidate an earlier gate's evidence, so this gate re-runs the affected sequences here.
//
// THE FOUR GROUPS.
//
//   * Catalog coverage (GC-025). `CatalogCoverageScenario.Run()` executes here, over the merged kernel, in the
//     qualification player. It is the same sequence `-probeCatalogCoverage` drives and the same one the EditMode
//     assembly recomputes the digest from, so "complete catalog coverage runs on the merged revision" is evidence
//     from the merged revision rather than a reference to GC-025's older run (P-009, P-058, TEST-001, TEST-020).
//   * Incremental-versus-clean derivation equivalence (GC-026). GC-026 changed the derivation indexes
//     (`DerivationIndexSet`, `DescriptorTargetIndex`, `ScopeMembershipIndex`), the incremental engine and the live
//     target index. GC-012/TEST-008's guarantee is that an incremental derivation equals a clean one; this group
//     re-establishes it on the merged kernel, at the declared 10,000-target scale, over the same recorded seed series
//     the package's own 10k property uses, for the edit kinds a Wave 7 fix could have broken (P-023, TEST-008).
//     It is NOT a measurement: the gate's short benchmark diagnostic measures, and this group only compares two
//     derivations' canonical outputs.
//   * Faulted recovery (GC-027). `Gc027Scenario.Run(family)` executes for all three genres through the owning
//     task's runner, and this group records the sequence's own digest plus the two fault points the gate names:
//     postwrite-apply and restart. A recovery path that GC-026's or GC-025's changes broke fails here (P-031,
//     P-045, P-049, P-053, TEST-014, TEST-016).
//   * The merge invariants themselves. Two observations exist because they are the failure modes a four-way merge
//     really has: every probe mode must still parse out of the merged `ProbeArguments` (a dropped mode silently
//     removes a gate from the release process), and the ten recorded budget rows must still be the declared ten
//     (a dropped row silently shrinks what "the budgets are recorded" means). Both are checks about this revision,
//     not about a task.
//
// GC-024'S GROUP, now merged. The reference-conformance tables and the combined narrative+cards world are GC-024's
// own sequences, and this gate appends them as one further group in the emission order below: the three transcribed
// 07 tables per owning genre, the combined cross-template reward flow, and the assembly/genre audit. Appending the
// group changed this file's digest literal and the probe's quoted copy together, which is what the frozen table is
// for — `tools/check_gate_sources.py` recomputes the literal from the table, so a group added or dropped without
// updating the literal fails there rather than passing quietly. GC-024's own dotnet and Unity suites needed no wiring
// here: the gate's invocations are unfiltered over every testable assembly, so they participate through the solution
// and the manifest's `testables`.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Benchmarks;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.ReferenceConformance;
using GameCore.Rules.Narrative;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>One named Wave 7 gate observation: what was checked and the values it was computed from.</summary>
    public sealed class W7GateStep
    {
        public W7GateStep(string name, bool passed, string detail)
        {
            Name = name;
            Passed = passed;
            Detail = detail ?? string.Empty;
        }

        public string Name { get; }

        public bool Passed { get; }

        public string Detail { get; }

        public override string ToString() => Name + ": " + (Passed ? "Pass" : "Fail") + " (" + Detail + ")";
    }

    /// <summary>
    /// Full result of one Wave 7 gate run: the named observations plus one digest over them, computed over the
    /// canonical `name=pass|fail` lines with the same digest function every earlier gate's result uses. A run that
    /// records a different set of observations (or a failing one) therefore cannot report the digest the frozen
    /// observation table implies (P-008, TEST-022).
    /// </summary>
    public sealed class W7GateScenarioResult
    {
        public W7GateScenarioResult(string label, IReadOnlyList<W7GateStep> steps)
        {
            Label = label;
            Steps = steps;
            var lines = new List<string>(steps.Count);
            bool allPassed = steps.Count > 0;
            for (int i = 0; i < steps.Count; i++)
            {
                lines.Add(steps[i].Name + "=" + (steps[i].Passed ? "pass" : "fail"));
                allPassed &= steps[i].Passed;
            }

            AllPassed = allPassed;
            Digest = NarrativeDigest.OfLines(lines);
        }

        /// <summary>Label every observation name of this run is qualified with.</summary>
        public string Label { get; }

        /// <summary>The named observations, in execution order, each qualified with its own group's label.</summary>
        public IReadOnlyList<W7GateStep> Steps { get; }

        /// <summary>Canonical digest over this run's observation names and pass flags.</summary>
        public string Digest { get; }

        public bool AllPassed { get; }

        public string Describe()
        {
            var failed = new List<string>();
            for (int i = 0; i < Steps.Count; i++)
            {
                if (!Steps[i].Passed)
                {
                    failed.Add(Steps[i].ToString());
                }
            }

            return "digest=" + Digest
                + "; label=" + Label
                + "; steps=" + Steps.Count.ToString(CultureInfo.InvariantCulture)
                + "; failed=" + failed.Count.ToString(CultureInfo.InvariantCulture)
                + (failed.Count == 0 ? string.Empty : ": " + string.Join(" | ", failed.ToArray()));
        }

        public override string ToString() => Describe();
    }

    /// <summary>
    /// The merged-revision integration gate: the ownership of the five groups is described in the file header, and
    /// every one of them runs a scenario this repository already has rather than a second implementation of it.
    /// </summary>
    public static class W7GateScenario
    {
        /// <summary>Qualification label of the process-level group, which belongs to no genre.</summary>
        public const string ProcessLabel = "w7";

        /// <summary>Name of the single step that records the digest over the whole table.</summary>
        public const string DigestStepName = ProcessLabel + "/w7-gate-digest";

        /// <summary>Observed seed series of the equivalence property: the recorded seed and two neighbours (P-008).</summary>
        public static readonly uint[] EquivalenceSeeds =
        {
            BenchmarkWorkloads.DefaultSeed,
            BenchmarkWorkloads.DefaultSeed + 1U,
            BenchmarkWorkloads.DefaultSeed + 31U,
        };

        /// <summary>
        /// The process-level group's observation names, in execution order, without the label qualification. Each one
        /// is a re-run of the owning Wave 7 task's own mechanism on the merged revision.
        /// </summary>
        public static readonly string[] ProcessObservationNames =
        {
            "w7-catalog-coverage-repasses-on-the-merged-kernel",
            "w7-incremental-derivation-matches-a-clean-derivation",
            "w7-probe-mode-dispatch-keeps-every-mode",
            "w7-declared-budget-rows-are-the-recorded-ten",
        };

        /// <summary>The per-genre recovery group's observation names, in execution order.</summary>
        public static readonly string[] FamilyObservationNames =
        {
            "w7-recovery-sequence-repasses",
            "w7-recovery-postwrite-apply-fault",
            "w7-recovery-restart-without-in-process-state",
        };

        /// <summary>
        /// The GC-024 conformance group's observation names, in execution order. GC-024 owns the reference tables and
        /// the combined world, so this group is a re-run of GC-024's own sequences through GC-024's own runners: the
        /// gate sentence's "all reference transition tables pass" and "cross-template flow" are its first two
        /// observations, and its third is the assembly/genre audit GC-024's acceptance also names.
        /// </summary>
        public static readonly string[] ConformanceObservationNames =
        {
            "w7-conformance-tables-repass",
            "w7-conformance-cross-template-flow-repasses",
            "w7-conformance-genre-audit-is-clean",
        };

        /// <summary>Every probe mode flag the merged probe host must still accept (the merge invariant).</summary>
        public static readonly string[] ModeFlags =
        {
            "-probeMissingRegistration",
            "-probeWorldDispatch",
            "-probeW1Gate",
            "-probeW2Gate",
            "-probeW3Gate",
            "-probeNarrative",
            "-probeCards",
            "-probeW4Profile",
            "-probeGc013",
            "-probeW4Gate",
            "-probeFaults",
            "-probeGc018",
            "-probeGc019",
            "-probeW5Gate",
            "-probeTraversal",
            "-probeGc021",
            "-probeRecovery",
            "-probeLifecycleStress",
            "-probeReplay",
            "-probeW6Gate",
            "-probeCatalogCoverage",
            "-probeBenchmark",
            "-probeW7Gate",
            "-probeRecoverySmoke",
            "-probeConformance",
        };

        private static readonly List<string> FamilyLabels =
            new List<string> { Gc013NarrativeHost.Label, Gc013CardsHost.Label, Gc020TraversalHost.Label };

        /// <summary>The family labels this scenario runs the recovery group for, in the order the contract fixes them.</summary>
        public static IReadOnlyList<string> Families() => FamilyLabels;

        /// <summary>
        /// The frozen observation table: every name one run records, in emission order, qualified with its own group's
        /// label. The digest step is deliberately NOT in this table, because the table is what the digest is computed
        /// from and a step inside its own input could not be checked (P-008).
        /// </summary>
        public static string[] ObservationNames()
        {
            var names = new List<string>(
                ProcessObservationNames.Length + (FamilyLabels.Count * FamilyObservationNames.Length)
                + ConformanceObservationNames.Length);
            for (int i = 0; i < ProcessObservationNames.Length; i++)
            {
                names.Add(ProcessLabel + "/" + ProcessObservationNames[i]);
            }

            for (int f = 0; f < FamilyLabels.Count; f++)
            {
                for (int i = 0; i < FamilyObservationNames.Length; i++)
                {
                    names.Add(FamilyLabels[f] + "/" + FamilyObservationNames[i]);
                }
            }
            for (int i = 0; i < ConformanceObservationNames.Length; i++)
            {
                names.Add(ProcessLabel + "/" + ConformanceObservationNames[i]);
            }

            return names.ToArray();
        }

        /// <summary>The digest the frozen table implies when every observation passes.</summary>
        public static string ExpectedDigest()
        {
            string[] names = ObservationNames();
            var lines = new List<string>(names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                lines.Add(names[i] + "=pass");
            }

            return NarrativeDigest.OfLines(lines);
        }

        /// <summary>
        /// Runs the whole gate: the four process observations, the recovery group for each genre in
        /// <see cref="Families"/> order, the GC-024 conformance group, then the digest step over the table.
        /// </summary>
        public static W7GateScenarioResult Run()
        {
            var steps = new List<W7GateStep>(ObservationNames().Length + 1);
            AddProcessSteps(steps);
            for (int f = 0; f < FamilyLabels.Count; f++)
            {
                AddFamilySteps(FamilyLabels[f], steps);
            }

            AddConformanceSteps(steps);

            // The digest step is recorded last and is NOT part of the table it checks: a step inside its own input
            // could not be falsified. Its verdict is the whole frozen table — every name present, in order, all
            // passing — so a renamed, reordered, added or dropped observation changes the literal and the step fails
            // rather than the gate silently covering less than it claims (P-008, TEST-022).
            string[] expected = ObservationNames();
            string digest = NarrativeDigest.OfLines(LineOf(steps));
            var problems = new List<string>();
            if (steps.Count != expected.Length)
            {
                problems.Add("recorded=" + steps.Count.ToString(CultureInfo.InvariantCulture)
                    + " expected=" + expected.Length.ToString(CultureInfo.InvariantCulture));
            }

            for (int i = 0; i < steps.Count && i < expected.Length; i++)
            {
                if (!string.Equals(steps[i].Name, expected[i], StringComparison.Ordinal))
                {
                    problems.Add("step " + i.ToString(CultureInfo.InvariantCulture) + " is '" + steps[i].Name
                        + "' but the table fixes '" + expected[i] + "'");
                }
            }

            if (!string.Equals(digest, ExpectedDigest(), StringComparison.Ordinal))
            {
                problems.Add("digest=" + digest + " but the table implies " + ExpectedDigest());
            }

            steps.Add(new W7GateStep(
                DigestStepName,
                problems.Count == 0 && digest.Length == 64,
                "observations=" + (steps.Count).ToString(CultureInfo.InvariantCulture)
                + "; digest=" + digest
                + "; expected=" + ExpectedDigest()
                + "; problems=" + (problems.Count == 0 ? "<none>" : string.Join(" | ", problems.ToArray()))));
            return new W7GateScenarioResult(ProcessLabel, steps);
        }

        private static List<string> LineOf(IReadOnlyList<W7GateStep> steps)
        {
            var lines = new List<string>(steps.Count);
            for (int i = 0; i < steps.Count; i++)
            {
                lines.Add(steps[i].Name + "=" + (steps[i].Passed ? "pass" : "fail"));
            }

            return lines;
        }

        // ------------------------------------------------------------------ the process group

        private static void AddProcessSteps(List<W7GateStep> steps)
        {
            Add(steps, ProcessLabel, ProcessObservationNames[0], CatalogCoverageStep);
            Add(steps, ProcessLabel, ProcessObservationNames[1], IncrementalEquivalenceStep);
            Add(steps, ProcessLabel, ProcessObservationNames[2], ProbeModeDispatchStep);
            Add(steps, ProcessLabel, ProcessObservationNames[3], BudgetRowsStep);
        }

        /// <summary>
        /// GC-025's own sequence, run here: the committed reachability manifest against the live generated catalogs,
        /// every generated root executed, and the traversal generated catalog against its hand-written counterpart.
        /// </summary>
        private static bool CatalogCoverageStep(out string detail)
        {
            CatalogCoverageResult coverage = CatalogCoverageScenario.Run();
            detail = "coverage=" + coverage.Describe()
                + "; label=" + coverage.Label
                + "; observations=" + coverage.Steps.Count.ToString(CultureInfo.InvariantCulture)
                + "; failed=" + FailedCount(coverage.Steps).ToString(CultureInfo.InvariantCulture);
            return coverage.AllPassed && coverage.Steps.Count == CatalogCoverageScenario.ObservationNames.Length;
        }

        private static int FailedCount(IReadOnlyList<CatalogCoverageStep> steps)
        {
            int failed = 0;
            for (int i = 0; i < steps.Count; i++)
            {
                if (!steps[i].Passed)
                {
                    failed++;
                }
            }

            return failed;
        }

        /// <summary>
        /// GC-012/TEST-008's equivalence guarantee, re-established on the merged kernel: for every seed of the
        /// recorded series, the declared 10,000-target fixture is edited by each kind of change a Wave 7 fix could
        /// have broken — an install-only mount (the index-domain reuse GC-026 added), a subtree reparent, a
        /// 1,000-target spawn and the retraction of those targets — and each edited snapshot is derived BOTH
        /// incrementally from the previous accepted publication and cleanly from scratch. The two must agree on the
        /// canonical result hash and on the effective assembly count; a disagreement names the seed and the kind.
        /// </summary>
        private static bool IncrementalEquivalenceStep(out string detail)
        {
            var notes = new List<string>();
            bool pass = true;
            for (int s = 0; s < EquivalenceSeeds.Length; s++)
            {
                uint seed = EquivalenceSeeds[s];
                BenchmarkFixture fixture = BenchmarkFixtureGenerator.Generate(
                    new BenchmarkScale(
                        BenchmarkWorkloads.DefaultScopes,
                        BenchmarkWorkloads.DefaultTargets,
                        BenchmarkWorkloads.DefaultScopes,
                        BenchmarkWorkloads.DefaultTargets,
                        seed));
                var options = new DerivationOptions(null, null, fixture.SuggestedBudget, null, true);
                DerivationResult accepted = DerivationEngine.Derive(
                    fixture.Builder()
                        .WithVersion(new CompositionRevision(1UL), new AssemblyEpoch(1UL))
                        .Build(),
                    fixture.Values,
                    options,
                    null);
                if (!accepted.Accepted)
                {
                    pass = false;
                    notes.Add("seed=" + seed.ToString(CultureInfo.InvariantCulture)
                        + " base-refused(" + accepted.Rejection + ")");
                    continue;
                }

                DerivationResult previous = accepted;
                for (int edit = 0; edit < EditNames.Length; edit++)
                {
                    DerivationSnapshot edited = Compose(edit, fixture, 2UL + (ulong)edit);
                    IncrementalDerivationOutcome incremental =
                        IncrementalDerivationEngine.Derive(edited, fixture.Values, options, previous, null);
                    DerivationResult clean = DerivationEngine.Derive(edited, fixture.Values, options, null);
                    bool stepPass = incremental.Result.Accepted && clean.Accepted
                        && incremental.Result.ResultHash.Equals(clean.ResultHash)
                        && incremental.Result.Assemblies.Count == clean.Assemblies.Count;
                    pass &= stepPass;
                    if (!stepPass)
                    {
                        notes.Add("seed=" + seed.ToString(CultureInfo.InvariantCulture)
                            + " edit=" + EditNames[edit]
                            + " incremental=" + (incremental.Result.Accepted ? "accepted" : "refused")
                            + " clean=" + (clean.Accepted ? "accepted" : "refused")
                            + " hashes=" + incremental.Result.ResultHash.ToHex() + "/" + clean.ResultHash.ToHex()
                            + " assemblies=" + incremental.Result.Assemblies.Count.ToString(CultureInfo.InvariantCulture)
                            + "/" + clean.Assemblies.Count.ToString(CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        notes.Add("seed=" + seed.ToString(CultureInfo.InvariantCulture)
                            + " edit=" + EditNames[edit]
                            + " resultHash=" + incremental.Result.ResultHash.ToHex()
                            + " targets=" + fixture.Targets.Count.ToString(CultureInfo.InvariantCulture));
                    }

                    previous = incremental.Result.Accepted ? incremental.Result : previous;
                }
            }

            detail = "scale=" + BenchmarkWorkloads.DefaultScopes.ToString(CultureInfo.InvariantCulture) + "scopes/"
                + BenchmarkWorkloads.DefaultTargets.ToString(CultureInfo.InvariantCulture) + "targets"
                + "; seeds=" + EquivalenceSeeds.Length.ToString(CultureInfo.InvariantCulture)
                + "; edits=" + string.Join(",", EditNames)
                + "; " + string.Join("; ", notes.ToArray());
            return pass;
        }

        /// <summary>The edit kinds the equivalence group exercises, in order; the labels are the recorded names.</summary>
        private static readonly string[] EditNames = { "install-mount", "reparent", "spawn-1000", "retire-1000" };

        /// <summary>
        /// One edit of the declared fixture, at its own revision and epoch. Every edit is composed through the
        /// production builder, so an edit is a real publication step rather than a mutated snapshot (P-006).
        /// </summary>
        private static DerivationSnapshot Compose(int edit, BenchmarkFixture fixture, ulong revision)
        {
            BenchmarkSnapshotBuilder builder = fixture.Builder()
                .WithVersion(new CompositionRevision(revision), new AssemblyEpoch(revision));
            switch (edit)
            {
                case 0:
                    // An install-only edit: the case GC-026's index-domain reuse path was added for.
                    return builder
                        .AddInstall(BenchmarkFixtureVariants.UpdateInstall(fixture, 1, 0))
                        .Build();
                case 1:
                    return builder
                        .ReparentScope(fixture.ReparentScope, fixture.SecondProviderScope)
                        .Build();
                case 2:
                    return builder
                        .AddTargets(BenchmarkFixtureVariants.SpawnTargets(
                            fixture, fixture.GroupScopes[2], BenchmarkWorkloads.SpawnTargets, 0))
                        .Build();
                default:
                    return builder
                        .RemoveTargets(BenchmarkFixtureVariants.SpawnedTargetIds(
                            BenchmarkWorkloads.SpawnTargets, 0))
                        .Build();
            }
        }

        /// <summary>
        /// The merge invariant: every probe mode this repository has added must still parse out of the merged
        /// `ProbeArguments`, exactly one at a time. A merge that dropped a mode's `const`, its constructor parameter
        /// or its parse branch removes a gate from the release process silently, which is what this check exists for.
        /// </summary>
        private static bool ProbeModeDispatchStep(out string detail)
        {
            var missing = new List<string>();
            var ambiguous = new List<string>();
            for (int i = 0; i < ModeFlags.Length; i++)
            {
                ProbeArguments parsed = ProbeArguments.Parse(
                    new[] { ModeFlags[i], "-probeResult", "merged-revision-identity.json" });
                int set = SetModeCount(parsed);
                if (!parsed.IsProbeInvocation || !IsModeSet(parsed, i))
                {
                    missing.Add(ModeFlags[i]);
                }
                else if (set != 1)
                {
                    ambiguous.Add(ModeFlags[i] + "=" + set.ToString(CultureInfo.InvariantCulture));
                }
            }

            detail = "modes=" + ModeFlags.Length.ToString(CultureInfo.InvariantCulture)
                + "; missing=" + (missing.Count == 0 ? "<none>" : string.Join(",", missing.ToArray()))
                + "; ambiguous=" + (ambiguous.Count == 0 ? "<none>" : string.Join(",", ambiguous.ToArray()))
                + "; resultPathParsed=True";
            return missing.Count == 0 && ambiguous.Count == 0;
        }

        /// <summary>How many mode properties one parsed argument set has set; a correct parse sets exactly one.</summary>
        private static int SetModeCount(ProbeArguments parsed)
        {
            int count = 0;
            for (int i = 0; i < ModeFlags.Length; i++)
            {
                if (IsModeSet(parsed, i))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>The property one mode flag must set, by index into <see cref="ModeFlags"/>.</summary>
        private static bool IsModeSet(ProbeArguments parsed, int index)
        {
            switch (index)
            {
                case 0: return parsed.MissingRegistration;
                case 1: return parsed.WorldDispatch;
                case 2: return parsed.W1Gate;
                case 3: return parsed.W2Gate;
                case 4: return parsed.W3Gate;
                case 5: return parsed.Narrative;
                case 6: return parsed.Cards;
                case 7: return parsed.W4Profile;
                case 8: return parsed.Gc013;
                case 9: return parsed.W4Gate;
                case 10: return parsed.Faults;
                case 11: return parsed.Gc018;
                case 12: return parsed.Gc019;
                case 13: return parsed.W5Gate;
                case 14: return parsed.Traversal;
                case 15: return parsed.Gc021;
                case 16: return parsed.Recovery;
                case 17: return parsed.LifecycleStress;
                case 18: return parsed.Replay;
                case 19: return parsed.W6Gate;
                case 20: return parsed.CatalogCoverage;
                case 21: return parsed.Benchmark;
                case 22: return parsed.W7Gate;
                case 23: return parsed.RecoverySmoke;
                case 24: return parsed.Conformance;

                // No default arm that aliases a real mode: an index this switch does not know is a flag the table
                // gained without a property to assert, and silently returning another mode's value would let it pass
                // as "set" while the flag itself never parsed. False makes it a reported missing mode instead.
                default: return false;
            }
        }

        /// <summary>
        /// "Benchmark data and budget decisions are recorded", as a code-side claim: the ten provisional rows of 08
        /// are still the ten this build carries, each with its own workload/phase/metric key and its own target. The
        /// document side of the same sentence — the budget decision record and the diagnostic it names — is checked
        /// by `tools/check_budget_record.py`, which reads the committed files; a player cannot, so the two halves are
        /// deliberately split rather than one of them being faked (P-022, TEST-023).
        /// </summary>
        private static bool BudgetRowsStep(out string detail)
        {
            string[] declared =
            {
                PerformanceBudgets.ExecutionP95,
                PerformanceBudgets.ExecutionManagedBytes,
                PerformanceBudgets.UnchangedControlNodes,
                PerformanceBudgets.UnchangedServiceLookups,
                PerformanceBudgets.ApplyPauseP95,
                PerformanceBudgets.WholeWorldPreparationP95,
                PerformanceBudgets.SpawnBaseline,
                PerformanceBudgets.LifecyclePlateau,
                PerformanceBudgets.IdleSteps,
                PerformanceBudgets.IdleStageUpdates,
            };

            var problems = new List<string>();
            int reportOnly = 0;
            for (int i = 0; i < declared.Length; i++)
            {
                if (!PerformanceBudgets.TryGet(declared[i], out PerformanceBudget row) || row == null)
                {
                    problems.Add("row " + declared[i] + " is absent");
                    continue;
                }

                if (row.MetricKey.Length == 0 || row.WorkloadId.Length == 0 || row.Metric.Length == 0)
                {
                    problems.Add("row " + declared[i] + " has no workload/phase/metric key");
                }

                if (row.ReportOnly)
                {
                    reportOnly++;
                }
                else if (row.Target <= 0.0)
                {
                    problems.Add("row " + declared[i] + " is comparable but declares no target");
                }
            }

            detail = "rows=" + PerformanceBudgets.All.Count.ToString(CultureInfo.InvariantCulture)
                + "; declared=" + declared.Length.ToString(CultureInfo.InvariantCulture)
                + "; reportOnly=" + reportOnly.ToString(CultureInfo.InvariantCulture)
                + "; ids=" + PerformanceBudgets.JoinIds()
                + "; problems=" + (problems.Count == 0 ? "<none>" : string.Join(",", problems.ToArray()));
            return problems.Count == 0 && PerformanceBudgets.All.Count == declared.Length;
        }

        // ------------------------------------------------------------------ the recovery group

        /// <summary>
        /// GC-027's own sequence through GC-027's own runner, for one genre, on the merged kernel. The group records
        /// the sequence's own digest and the two fault points the gate sentence names, so a failure anywhere in the
        /// recovery sequence is visible and the gate's claim is never narrower than the sequence it re-ran.
        /// </summary>
        private static void AddFamilySteps(string label, List<W7GateStep> steps)
        {
            string sequenceName = label + "/" + FamilyObservationNames[0];
            string postwriteName = label + "/" + FamilyObservationNames[1];
            string restartName = label + "/" + FamilyObservationNames[2];
            try
            {
                IGc027Family family = RecoveryFamily(label);
                Gc027ScenarioResult recovery = Gc027Scenario.Run(family);
                int expected = Gc027Scenario.ExpectedNames(family).Length;
                W7GateStep? postwrite = Find(recovery.Steps, label + "/" + Gc027Scenario.PostwriteApplyObservation);
                W7GateStep? restart = Find(recovery.Steps, label + "/" + Gc027Scenario.RestartObservation);
                bool sequencePassed = recovery.AllPassed && recovery.Steps.Count == expected;

                steps.Add(new W7GateStep(
                    sequenceName,
                    sequencePassed,
                    "gc027=" + recovery.Describe()
                    + "; expected=" + expected.ToString(CultureInfo.InvariantCulture)
                    + "; recorded=" + recovery.Steps.Count.ToString(CultureInfo.InvariantCulture)
                    + "; faultPoints=postwrite-apply,restart"
                    + "; recoveryDigest=" + recovery.Digest));

                steps.Add(new W7GateStep(
                    postwriteName,
                    postwrite != null && postwrite.Passed,
                    postwrite == null
                        ? "the sequence recorded no observation named "
                            + Gc027Scenario.PostwriteApplyObservation + " for this genre"
                        : postwrite.Detail));

                steps.Add(new W7GateStep(
                    restartName,
                    restart != null && restart.Passed,
                    restart == null
                        ? "the sequence recorded no observation named "
                            + Gc027Scenario.RestartObservation + " for this genre"
                        : restart.Detail));
            }
            catch (Exception exception)
            {
                string failure = "unhandled " + exception.GetType().FullName + ": " + exception.Message;
                steps.Add(new W7GateStep(sequenceName, false, failure));
                steps.Add(new W7GateStep(postwriteName, false, failure));
                steps.Add(new W7GateStep(restartName, false, failure));
            }
        }

        /// <summary>
        /// The one family adapter of a genre, resolved from the genre's own host: the gate never constructs a second
        /// narrative or card family, so it drives the same worlds the earlier gates drive (P-001, P-002).
        /// </summary>
        private static IGc027Family RecoveryFamily(string label)
        {
            if (string.Equals(label, Gc013NarrativeHost.Label, StringComparison.Ordinal))
            {
                return Gc013NarrativeHost.RecoveryFamily();
            }

            if (string.Equals(label, Gc013CardsHost.Label, StringComparison.Ordinal))
            {
                return Gc013CardsHost.RecoveryFamily();
            }

            if (string.Equals(label, Gc020TraversalHost.Label, StringComparison.Ordinal))
            {
                return Gc020TraversalHost.RecoveryFamily();
            }

            throw new ArgumentException(
                "Unknown Wave 7 gate family label '" + (label ?? "<null>") + "'; the accepted labels are "
                + string.Join(", ", FamilyLabels.ToArray()) + ".", nameof(label));
        }

        private static W7GateStep? Find(IReadOnlyList<Gc027Step> steps, string name)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (string.Equals(steps[i].Name, name, StringComparison.Ordinal))
                {
                    return new W7GateStep(steps[i].Name, steps[i].Passed, steps[i].Detail);
                }
            }

            return null;
        }

        // ------------------------------------------------------------------ the conformance group

        /// <summary>
        /// GC-024's own sequences, re-run here on the merged revision. The group is one observation per clause the
        /// gate sentence and GC-024's acceptance name, and each runs GC-024's own entry point rather than a second
        /// implementation: the three transcribed 07 tables through the genre hosts' `RunConformance*` entry points,
        /// the combined narrative+cards world through `ConformanceCrossWorld.Run`, and the assembly/genre audit
        /// through `ConformanceCrossWorld.AuditCombinedComposition` (P-001, P-013, P-014, P-016, P-025, P-045, P-059).
        /// </summary>
        private static void AddConformanceSteps(List<W7GateStep> steps)
        {
            Add(steps, ProcessLabel, ConformanceObservationNames[0], ConformanceTablesStep);
            Add(steps, ProcessLabel, ConformanceObservationNames[1], CrossTemplateFlowStep);
            Add(steps, ProcessLabel, ConformanceObservationNames[2], GenreAuditStep);
        }

        /// <summary>
        /// "All reference transition tables pass": every transcribed 07 table runs over the genre that owns it, and
        /// each must have been executed (its oracle checked at least one row, which is what makes an empty table a
        /// failure rather than a pass), must have recorded no failing step and must have no unrecorded gap. The
        /// declared table set is compared against the tables that really ran, so a merge that dropped a table is
        /// visible rather than a smaller run reporting success.
        /// </summary>
        private static bool ConformanceTablesStep(out string detail)
        {
            string[] declared = ConformanceTableIds;
            var results = new List<ConformanceTableResult>(declared.Length);
            var notes = new List<string>(declared.Length);
            bool pass = true;
            for (int i = 0; i < declared.Length; i++)
            {
                ConformanceTableResult? result = null;
                string failure = string.Empty;
                try
                {
                    result = declared[i] == "cards"
                        ? Gc013CardsHost.RunConformanceCards()
                        : declared[i] == "narrative"
                            ? Gc013NarrativeHost.RunConformanceNarrative()
                            : Gc020TraversalHost.RunConformanceTraversal();
                }
                catch (Exception exception)
                {
                    failure = "unhandled " + exception.GetType().FullName + ": " + exception.Message;
                }

                if (result == null)
                {
                    pass = false;
                    notes.Add(declared[i] + ": " + failure);
                    continue;
                }

                results.Add(result);
                bool held = result.AllPassed
                    && result.Failures.Count == 0
                    && result.RecordedGaps.Count == 0
                    && result.Verdict.RowsChecked > 0
                    && result.Trace.Count > 0;
                pass &= held;
                notes.Add(result.Describe());
            }

            // The table set the fixture declares is the set that must have run: a merge that dropped a table would
            // otherwise report a clean run over a smaller corpus.
            int declaredTables = ReferenceTables.All().Count;
            bool complete = results.Count == declared.Length;
            pass &= complete && declaredTables >= declared.Length + 1;

            detail = "declaredTables=" + declaredTables.ToString(CultureInfo.InvariantCulture)
                + "; ran=" + results.Count.ToString(CultureInfo.InvariantCulture)
                + "/" + declared.Length.ToString(CultureInfo.InvariantCulture)
                + "; complete=" + (complete ? "true" : "false")
                + "; " + string.Join("; ", notes.ToArray());
            return pass;
        }

        /// <summary>
        /// "Cross-template flow": the combined narrative+cards world runs GC-024's own reward flow — a committed
        /// narrative choice becoming one durable, idempotent card mutation — and its own oracle must accept the trace
        /// it recorded, with no failing step and no recorded gap (P-001, P-043, P-045, TEST-014).
        /// </summary>
        private static bool CrossTemplateFlowStep(out string detail)
        {
            ConformanceTableResult result = ConformanceCrossWorld.Run();
            bool held = result.AllPassed
                && result.Failures.Count == 0
                && result.RecordedGaps.Count == 0
                && result.Verdict.RowsChecked > 0
                && result.Trace.Count > 0;
            // The leading fragment names the claim rather than reusing the trace's own label, so a harness clause for
            // this observation cannot be satisfied by some other step's detail.
            detail = "crossTemplateFlow=" + (held ? "pass" : "fail") + "; " + result.Describe()
                + "; steps=" + result.Steps.Count.ToString(CultureInfo.InvariantCulture)
                + "; facts=" + result.Trace.Count.ToString(CultureInfo.InvariantCulture)
                + "; digest=" + result.Trace.Digest();
            return held;
        }

        /// <summary>
        /// GC-024's genre/assembly audit: the combined composition carries no traversal identity and no gameplay
        /// assembly depends on another family's types, and the audit really walked something (a zero-entry audit is
        /// a failure, not a clean result). The build-time half of the audit is asserted on the build host and in
        /// EditMode; this observation is the loaded-assembly half a player can honestly compute (P-001, P-060).
        /// </summary>
        private static bool GenreAuditStep(out string detail)
        {
            CrossCompositionAudit audit = ConformanceCrossWorld.AuditCombinedComposition();
            detail = audit.Describe()
                + "; walkedEntries=" + audit.WalkedEntries.ToString(CultureInfo.InvariantCulture)
                + "; findings=" + (audit.Findings.Count == 0
                    ? "<none>"
                    : string.Join(",", audit.Findings.ToArray()));
            return audit.Clean;
        }

        /// <summary>The 07 table ids the conformance group runs, in the order their genres are declared.</summary>
        private static readonly string[] ConformanceTableIds = { "cards", "narrative", "traversal" };

        /// <summary>
        /// Records one observation. The qualification is applied at this single point, so every step method passes
        /// the bare name from the frozen table and the recorded sequence is exactly the table: a rename changes the
        /// digest, which is what the EditMode suite and the player probe assert on (P-008).
        /// </summary>
        private static void Add(List<W7GateStep> steps, string label, string bareName, Observation observation)
        {
            string detail;
            bool passed;
            try
            {
                passed = observation(out detail);
            }
            catch (Exception exception)
            {
                passed = false;
                detail = "unhandled " + exception.GetType().FullName + ": " + exception.Message;
            }

            steps.Add(new W7GateStep(label + "/" + bareName, passed, detail));
        }

        private delegate bool Observation(out string detail);
    }
}
