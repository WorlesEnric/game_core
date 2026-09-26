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
    clock.Restart();
    var changes = DerivationChangeSet.Diff(baseline, next, counters);
    var diffMs = clock.ElapsedMilliseconds;
    clock.Restart();
    var oldIndex = DerivationIndexSet.Build(baseline, counters);
    var newIndex = DerivationIndexSet.Build(next, counters);
    var indexMs = clock.ElapsedMilliseconds;
    clock.Restart();
    var closure = InvalidationClosure.Compute(baseline, next, changes, oldIndex, newIndex, counters);
    var closureMs = clock.ElapsedMilliseconds;
    clock.Restart();
    var outcome = IncrementalDerivationEngine.Derive(next, fixture.Values, options, previous, changes);
    var deriveMs = clock.ElapsedMilliseconds;
    Console.WriteLine($"size={size} diff={diffMs}ms indexes={indexMs}ms closure={closureMs}ms deriveIncludingIndexes={deriveMs}ms dirty={closure.DirtyTargets.Count} affected={outcome.Result.Delta?.AffectedTargets.Count} accepted={outcome.Result.Accepted} nodes={outcome.Counters.Telemetry.Get(GameCore.Contracts.TelemetryCounter.ControlNodesVisited)}");
    var noExplain = new DerivationOptions(null, null, fixture.SuggestedBudget, null, false);
    var plainBase = DerivationEngine.Derive(baseline, fixture.Values, noExplain, null);
    clock.Restart();
    var plain = IncrementalDerivationEngine.Derive(next, fixture.Values, noExplain, plainBase, changes);
    Console.WriteLine($"size={size} withoutExplanations={clock.ElapsedMilliseconds}ms accepted={plain.Result.Accepted}");
}
