using System;
using System.Diagnostics;
using GameCore.Benchmarks;
using GameCore.Derivation;

var fixture = BenchmarkFixtureGenerator.Generate(BenchmarkScale.Default);
var options = new DerivationOptions(null, null, fixture.SuggestedBudget, null, true);
var baseline = fixture.Builder().Build();
var clock = Stopwatch.StartNew();
var previous = DerivationEngine.Derive(baseline, fixture.Values, options, null);
Console.WriteLine($"base {clock.ElapsedMilliseconds} ms accepted={previous.Accepted}");
foreach (var size in new[] { 1, 100, 10000 })
{
    var next = fixture.Builder()
        .WithVersion(new GameCore.Contracts.CompositionRevision(2), new GameCore.Contracts.AssemblyEpoch(2))
        .AddInstall(BenchmarkFixtureVariants.UpdateInstall(fixture, size, 0)).Build();
    var counters = new InvalidationCounters();
    var changes = DerivationChangeSet.Diff(baseline, next, counters);
    clock.Restart();
    var outcome = IncrementalDerivationEngine.Derive(next, fixture.Values, options, previous, changes);
    var deriveMs = clock.ElapsedMilliseconds;
    Console.WriteLine($"size={size} phases={IncrementalDerivationEngine.PhaseReport} derive={deriveMs}ms dirty={outcome.Invalidation.DirtyTargets.Count} affected={outcome.Result.Delta?.AffectedTargets.Count} accepted={outcome.Result.Accepted} nodes={outcome.Counters.Telemetry.Get(GameCore.Contracts.TelemetryCounter.ControlNodesVisited)}");
    IncrementalDerivationEngine.ResetPhaseReport();
}
