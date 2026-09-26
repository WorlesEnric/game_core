#!/usr/bin/env python3
"""Clone the validation project for a real, marker-free GC-017 release player build.

Run from any directory. The output lives beside GameCore.Validation so its local
file:../../../Packages dependencies keep resolving. Build it with build_probe.sh
using UNITY_PROJECT=<printed path>, then pass its Builds/Linux64 directory to
check_player_fault_free.py. The generated project is disposable and gitignored.

What the clone removes, and why the list is one flat, auditable set:

  * the two qualification marker packages (the fault switch and GC-023's telemetry switch), the replay fixture package
    and the two Unity test packages, plus every `testables` entry, so the clone really is a shipping-shaped
    configuration;
  * the `Tests/` tree, which no shipping player compiles;
  * every qualification-only runtime file. Each named file is either a fault-injection fixture whose types do not exist
    without the marker package (`FaultScenario*`, `ProbeFaults`, `W5Gate*`, `ProbeW5Gate`), a replay fixture
    (`ReplayParallelJobs`, `ReplayScenario`, `ProbeReplay` — GC-023's recorded-input replay and its real-Burst-jobs
    half are qualification evidence, not shipping behaviour), a qualification stress/gate fixture a shipping player has
    no reason to carry (`ProbeLifecycleStress` and GC-022's four stress runtime files, the Wave 6 gate's seven files),
    or a world scenario that only the qualification project drives (`Gc021Scenario`, `Gc021Family`, `ProbeGc021`).
    Their production seams — the delivery core, the traversal package, the optional engine stages and the four family
    hosts — all stay;
  * `Editor/LifecyclePlayModeMatrix.cs`, an editor-only qualification harness: it drives the application world and the
    traversal genre's reload route through the four {domain, scene} combinations, and the release player build compiles
    the Editor assembly, so it is removed for the same reason the `Tests/` tree above is. The reload-matrix runs happen
    in the qualification project, which keeps it;
  * the matching mode wiring in `ProbeRunner.cs` and `ProbeArguments.cs` — one whole block per mode and one exact
    needle per member — so the clone compiles without the removed types and no shipping entry point can reach them,
    rather than leaving unreachable probe hooks in IL2CPP;
  * one asmdef reference: `GameCore.Replay` leaves `GameCore.Validation.ProbeHost.asmdef`, because every file that
    consumed it is gone.

The modes the clone keeps are the ones a shipping build really carries and the release-surface checks really drive:
the GC-001 positive and expected-negative modes, world dispatch, the narrative/cards/GC-018/GC-019 family probes and
the GC-020 traversal course (including its local physics scene and its committed animation/audio output).

`ARG_NEEDLES` is generated from the merged `ProbeArguments.cs`: one needle for each const name, constructor parameter,
assignment, local, property (with its doc comment) and parse branch of a removed mode. Every needle must match exactly
once or `replace_once` raises, so a drifted source is a loud failure rather than a silently half-edited file.
"""

import json
import shutil
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "unity/GameCore.Validation"
DESTINATION = ROOT / "unity/GameCore.ReleaseCheck"
RUNTIME = Path("Assets/GameCore.Validation/Runtime")
EDITOR = Path("Assets/GameCore.Validation/Editor")


def replace_once(path: Path, old: str, new: str = "") -> None:
    content = path.read_text(encoding="utf-8")
    if content.count(old) != 1:
        raise RuntimeError(f"expected exactly one matching release-only edit in {path}: {old!r}")
    path.write_text(content.replace(old, new), encoding="utf-8")


# Every member of every probe mode this clone removes, exactly as the merged ProbeArguments.cs spells it. The modes
# that stay (-probeTraversal included) are untouched by every needle below.
ARG_NEEDLES = (
    '        private const string FaultsArgumentName = "-probeFaults";\n',
    '            bool faults,\n',
    '            Faults = faults;\n',
    '            bool faults = false;\n',
    "        /// <summary>\n        /// Runs the GC-017 fault-boundary mode: every named observation of TEST-016's apply/cancellation matrix over\n        /// both families, each family over its committed generated catalog and over its hand-written\n        /// generated-style catalog, inside the stripped player.\n        /// </summary>\n        public bool Faults { get; }\n",
    '                else if (argument == FaultsArgumentName)\n                {\n                    faults = true;\n                }\n',
    '        private const string W5GateArgumentName = "-probeW5Gate";\n',
    '            bool w5Gate,\n',
    '            W5Gate = w5Gate;\n',
    '            bool w5Gate = false;\n',
    '        /// <summary>\n        /// Runs the Wave 5 integration gate: retained observation, deterministic faults, checkpoint restore and the\n        /// common adapters joined in one actual world per family — prewrite rejection with the old assembly intact,\n        /// a postwrite fail-stop with no further step or image, a checkpoint captured at the committed boundary and\n        /// restored into a new session, read-only pinned snapshots that leak no writable reference, and a late asset\n        /// callback from the retired world rejected (P-007, P-027..P-031, P-045, P-047..P-055).\n        /// </summary>\n        public bool W5Gate { get; }\n',
    '                else if (argument == W5GateArgumentName)\n                {\n                    w5Gate = true;\n                }\n',
    '        private const string Gc021ArgumentName = "-probeGc021";\n',
    '            bool gc021,\n',
    '            Gc021 = gc021;\n',
    '            bool gc021 = false;\n',
    "        /// <summary>\n        /// Runs the GC-021 durable-delivery mode: the delivery key derivation, a durable commit that is persisted\n        /// before it is applied, a deterministic crash at the seam's own after-delivery boundary, the redelivery that\n        /// applies the destination mutation exactly once, capacity exhaustion that is never a silent drop, the\n        /// volatile/durable distinction, a committed obligation that outlives the unload of its world, a checkpoint\n        /// that carries the outbox and its cursor, the absence of a universal effect API, and the reward bridge's one\n        /// committed choice becoming one durable, idempotent card mutation (P-003, P-043, P-045, P-050, P-053).\n        /// </summary>\n        public bool Gc021 { get; }\n",
    '                else if (argument == Gc021ArgumentName)\n                {\n                    gc021 = true;\n                }\n',
    '        private const string LifecycleStressArgumentName = "-probeLifecycleStress";\n',
    '            bool lifecycleStress,\n',
    '            LifecycleStress = lifecycleStress;\n',
    '            bool lifecycleStress = false;\n',
    "        /// <summary>\n        /// Runs the GC-022 lifecycle stress: the counted mount/unmount cycles over each family's committed generated\n        /// catalog and over its fixture identity set, with delayed completions, stalled jobs, a throwing disposer,\n        /// required-provider churn and headless cleanup, under native leak detection with full stack traces\n        /// (P-047, P-048, P-050).\n        /// </summary>\n        public bool LifecycleStress { get; }\n",
    '                else if (argument == LifecycleStressArgumentName)\n                {\n                    lifecycleStress = true;\n                }\n',
    '        private const string ReplayArgumentName = "-probeReplay";\n',
    '            bool replay,\n',
    '            Replay = replay;\n',
    '            bool replay = false;\n',
    '        /// <summary>\n        /// Runs the GC-023 replay mode: the recorded 10,000-step integer fixture replayed across the supported\n        /// worker counts and under a shuffled producer/completion order, the differential propagation sweep with its\n        /// reducer, the observation replay separated from the native-physics comparison, and the instrumented\n        /// counters of one real owned world with the raw benchmark trace written beside the probe result\n        /// (P-008, P-023, TEST-022, TEST-023).\n        /// </summary>\n        public bool Replay { get; }\n',
    '                else if (argument == ReplayArgumentName)\n                {\n                    replay = true;\n                }\n',
    '        private const string W6GateArgumentName = "-probeW6Gate";\n',
    '            bool w6Gate,\n',
    '            W6Gate = w6Gate;\n',
    '            bool w6Gate = false;\n',
    '        /// <summary>\n        /// Runs the Wave 6 integration-gate mode: the fixed-step traversal course with the cost counters and the\n        /// recorded-input replay, the durable reward delivery across an unload/reload of its receiving world, the\n        /// composition audit that keeps the optional physics/animation/audio surface out of cards and narrative, and\n        /// the create/mount/step/unmount/teardown loop over all three genres (W6-GATE).\n        /// </summary>\n        public bool W6Gate { get; }\n',
    '                else if (argument == W6GateArgumentName)\n                {\n                    w6Gate = true;\n                }\n',
    '        private const string BenchmarkArgumentName = "-probeBenchmark";\n',
    '            bool benchmark,\n',
    '            Benchmark = benchmark;\n',
    '            bool benchmark = false;\n',
    "        /// <summary>\n        /// Runs the GC-026 performance benchmark: the generated 1,000-scope/10,000-target fixture through the real\n        /// derivation and incremental engines for the declared update sizes, the whole-world mode switch, the spawn,\n        /// the reparent and the lifecycle cycles; two real owned worlds for the idle window, the unchanged-composition\n        /// window, the fenced apply pause of a real plan, one live spawn publication and the authority mutation\n        /// fixture; and the correctness gates (zero stable control-tree scans, zero string service lookups, no\n        /// duplicated authoritative state) asserted rather than merely measured, with the raw per-sample documents\n        /// written beside the probe result (P-007, P-022, P-023, P-026, P-034, P-043, P-048, P-052, P-060,\n        /// TEST-008, TEST-013, TEST-023).\n        /// </summary>\n        public bool Benchmark { get; }\n",
    '                else if (argument == BenchmarkArgumentName)\n                {\n                    benchmark = true;\n                }\n',
)

def main() -> None:
    if DESTINATION.exists():
        raise RuntimeError(f"remove the old disposable release project first: {DESTINATION}")

    for folder in ("Assets", "Catalogs", "ProjectSettings", "Packages"):
        shutil.copytree(SOURCE / folder, DESTINATION / folder)

    manifest_path = DESTINATION / "Packages/manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    for dependency in (
        "com.gamecore.fault-qualification",
        # Both qualification-only switches go: the fault latch and GC-023's telemetry counters are compiled out of a
        # shipping build, which is the shape check_release_telemetry_free.py and the player inspection both depend on.
        "com.gamecore.telemetry-qualification",
        # GC-023's replay fixture package: its recorded trace, its real-Burst-jobs half and its probe are qualification
        # evidence.
        "com.gamecore.replay",
        # GC-026's benchmark fixture package: the generated 1,000-scope/10,000-target fixture and the raw sample
        # writers are qualification measurement, and every file that consumed them leaves with the probe mode below.
        "com.gamecore.benchmarks",
        "com.unity.test-framework",
        "com.unity.test-framework.performance",
    ):
        del manifest["dependencies"][dependency]
    # A shipping player has no testable packages; the qualification project runs their suites separately.
    manifest["testables"] = []
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    (DESTINATION / "Packages/packages-lock.json").unlink()

    # Qualification-only compilation units: no shipping player and no shipping Editor assembly compiles them.
    shutil.rmtree(DESTINATION / "Assets/GameCore.Validation/Tests")
    (DESTINATION / "Assets/GameCore.Validation/Tests.meta").unlink()
    for suffix in (".cs", ".cs.meta"):
        (DESTINATION / EDITOR / ("LifecyclePlayModeMatrix" + suffix)).unlink()

    for name in (
        # GC-017: the fault scenario and its probe name latch types a marker-free compilation does not contain.
        "FaultScenario",
        "FaultScenarioHost",
        "FaultScenarioStep",
        "ProbeFaults",
        # The Wave 5 integration gate: same reason, plus it is a qualification join rather than a shipping path.
        "W5GateScenario",
        "W5GateFamily",
        "W5GateNarrativeHost",
        "W5GateCardsHost",
        "ProbeW5Gate",
        # GC-021's world scenario and probe are qualification fixtures like the two above. Its durable delivery core
        # is production and stays: it is the shipping seam, not a test.
        "Gc021Scenario",
        "Gc021Family",
        "ProbeGc021",
        # GC-022's 1,000-cycle stress fixture: the shipping player has no reason to carry it. The scenario, both
        # family adapters, the family contract and the probe are one set - the scenario calls the adapters, so keeping
        # any of them without the others would not compile - and no surviving file references them.
        "ProbeLifecycleStress",
        "LifecycleStressScenario",
        "LifecycleStressFamily",
        "LifecycleStressNarrativeHost",
        "LifecycleStressCardsHost",
        # GC-023's replay fixtures: the recorded-input replay, the real-Burst-jobs variant and the mode that drives
        # them are qualification evidence; the instrumented counters they exercise are a build switch, not a shipping
        # code path.
        "ReplayParallelJobs",
        "ReplayScenario",
        "ProbeReplay",
        # The Wave 6 integration gate is a qualification fixture like the four above: it builds course worlds with the
        # local physics scene, samples the cost counters a release build compiles out, and drives the 1,000-cycle loop.
        "W6GateScenario",
        "W6GateFamily",
        "W6CompositionAudit",
        "W6FamilyNarrativeHost",
        "W6FamilyCardsHost",
        "W6FamilyTraversalHost",
        "ProbeW6Gate",
        # GC-026's performance benchmark: the generated 1,000-scope/10,000-target fixture, the live-world half and the
        # mode that drives them are qualification measurement, not shipping behaviour. The benchmarks fixture package
        # is a `tests/` local package like the replay package, so it leaves through the manifest instead.
        "ProbeBenchmark",
        "BenchmarkScenario",
        "BenchmarkLiveWorld",
    ):
        for suffix in (".cs", ".cs.meta"):
            (DESTINATION / RUNTIME / (name + suffix)).unlink()

    runner = DESTINATION / RUNTIME / "ProbeRunner.cs"
    # The report identity is one independent `if` per mode, so removing a mode's branch is a whole block.
    for mode, task in (("Faults", "GC-017"), ("W5Gate", "W5-GATE"), ("Gc021", "GC-021"),
                       ("LifecycleStress", "GC-022"), ("Replay", "GC-023"), ("W6Gate", "W6-GATE"),
                       ("Benchmark", "GC-026")):
        replace_once(
            runner,
            '            if (arguments.' + mode + ')\n'
            '            {\n'
            '                return Named("' + mode + '", "' + task + '");\n'
            '            }\n\n',
        )
        replace_once(
            runner,
            '                else if (arguments.' + mode + ')\n'
            '                {\n'
            '                    Probe' + mode + '.Run(report);\n'
            '                    report.CompletePositive();\n'
            '                }\n',
        )

    arguments = DESTINATION / RUNTIME / "ProbeArguments.cs"
    for old in ARG_NEEDLES:
        replace_once(arguments, old)

    # The qualification-mode expression keeps every surviving mode, so -probeTraversal still counts as a probe.
    replace_once(
        arguments,
        '            || Gc013 || W4Gate || Faults || Gc018 || Gc019 || W5Gate || Traversal || Gc021\n',
        '            || Gc013 || W4Gate || Gc018 || Gc019 || Traversal\n',
    )
    for removed in (
        '            || LifecycleStress\n',
        '            || Replay\n',
        '            || W6Gate\n',
        '            || Benchmark\n',
    ):
        replace_once(arguments, removed)

    # The constructor call keeps the same members in the same order as the remaining parameters, so the clone's
    # argument list and its constructor signature agree exactly.
    replace_once(
        arguments,
        '                w4Gate, faults, gc018, gc019, w5Gate, traversal, gc021, replay, w6Gate, benchmark, resultPath);',
        '                w4Gate, gc018, gc019, traversal, resultPath);',
    )
    replace_once(arguments, '                lifecycleStress,\n')

    # GC-023's replay assembly and GC-026's benchmark assembly leave the probe host's references: every file that
    # consumed them is gone. The benchmark's live world reads the replay package's hash function, so both edges go.
    probe_asmdef = DESTINATION / RUNTIME / "GameCore.Validation.ProbeHost.asmdef"
    asmdef = json.loads(probe_asmdef.read_text(encoding="utf-8"))
    asmdef["references"].remove("GameCore.Replay")
    asmdef["references"].remove("GameCore.Benchmarks")
    probe_asmdef.write_text(json.dumps(asmdef, indent=2) + "\n", encoding="utf-8")

    print(f"Marker-free release project: {DESTINATION}")
    print("Build: UNITY_PROJECT=<above> ARTIFACTS=artifacts/faults/release tools/unity/build_probe.sh")


if __name__ == "__main__":
    main()
