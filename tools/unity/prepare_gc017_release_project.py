#!/usr/bin/env python3
"""Clone the validation project for a real, marker-free GC-017 release player build.

Run from any directory. The output lives beside GameCore.Validation so its local
file:../../../Packages dependencies keep resolving. Build it with build_probe.sh
using UNITY_PROJECT=<printed path>, then pass its Builds/Linux64 directory to
check_player_fault_free.py. The generated project is disposable and gitignored.

What the clone removes, and why the list is one flat, auditable set:

  * the two qualification marker packages (the fault switch and GC-023's telemetry switch) and the two Unity test
    packages, plus every `testables` entry, so the clone really is a shipping-shaped configuration;
  * the `Tests/` tree, which no shipping player compiles;
  * every qualification-only probe/scenario runtime file. Each named file is either a fault-injection fixture whose
    types do not exist without the marker package (`FaultScenario*`, `ProbeFaults`, `W5Gate*`, `ProbeW5Gate`), a
    qualification stress/gate fixture a shipping player has no reason to carry (`ProbeLifecycleStress`, the Wave 6
    gate's seven files), or a world scenario that only the qualification project drives (`Gc021Scenario`,
    `Gc021Family`, `ProbeGc021`). Their production seams — the delivery core, the traversal package, the optional
    engine stages, the four family hosts and the replay package — all stay;
  * the matching mode wiring in `ProbeRunner.cs` and `ProbeArguments.cs`, one whole block per mode, so the clone
    compiles without the removed types and no shipping entry point can reach them.

The modes the clone keeps are the ones a shipping build really carries and the release-surface checks really drive:
the GC-001 positive and expected-negative modes, world dispatch, the narrative/cards/GC-018/GC-019 family probes, the
GC-020 traversal course and GC-023's replay shape proof.
"""

import json
import shutil
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "unity/GameCore.Validation"
DESTINATION = ROOT / "unity/GameCore.ReleaseCheck"
RUNTIME = Path("Assets/GameCore.Validation/Runtime")


def replace_once(path: Path, old: str, new: str = "") -> None:
    content = path.read_text(encoding="utf-8")
    if content.count(old) != 1:
        raise RuntimeError(f"expected exactly one matching release-only edit in {path}: {old!r}")
    path.write_text(content.replace(old, new), encoding="utf-8")


def main() -> None:
    if DESTINATION.exists():
        raise RuntimeError(f"remove the old disposable release project first: {DESTINATION}")

    for folder in ("Assets", "Catalogs", "ProjectSettings", "Packages"):
        shutil.copytree(SOURCE / folder, DESTINATION / folder)

    manifest_path = DESTINATION / "Packages/manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    for dependency in (
        "com.gamecore.fault-qualification",
        # GC-023 adds a second qualification marker: the telemetry switch. Removing it here is what makes the
        # marker-free clone a build in which every counting call site is compiled away, which is the shape
        # tools/check_release_telemetry_free.py and the player inspection both depend on.
        "com.gamecore.telemetry-qualification",
        "com.unity.test-framework",
        "com.unity.test-framework.performance",
    ):
        del manifest["dependencies"][dependency]
    # A shipping player has no testable packages; the qualification project runs their suites separately.
    manifest["testables"] = []
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    (DESTINATION / "Packages/packages-lock.json").unlink()

    shutil.rmtree(DESTINATION / "Assets/GameCore.Validation/Tests")
    (DESTINATION / "Assets/GameCore.Validation/Tests.meta").unlink()

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
        # family adapters, the family contract and the probe are one set — the scenario calls the adapters, so keeping
        # any of them without the others would not compile — and none of them is referenced by a surviving file.
        "ProbeLifecycleStress",
        "LifecycleStressScenario",
        "LifecycleStressFamily",
        "LifecycleStressNarrativeHost",
        "LifecycleStressCardsHost",
        # The Wave 6 integration gate is a qualification fixture like the four above: it builds course worlds with the
        # local physics scene, samples the cost counters a release build compiles out, and drives the 1,000-cycle loop.
        # Its production seams — the delivery core, the traversal package and the optional engine stages — all stay.
        "W6GateScenario",
        "W6GateFamily",
        "W6CompositionAudit",
        "W6FamilyNarrativeHost",
        "W6FamilyCardsHost",
        "W6FamilyTraversalHost",
        "ProbeW6Gate",
    ):
        for suffix in (".cs", ".cs.meta"):
            (DESTINATION / RUNTIME / (name + suffix)).unlink()

    runner = DESTINATION / RUNTIME / "ProbeRunner.cs"
    # The report identity is one independent `if` per mode, so removing a mode's branch is a whole block.
    replace_once(
        runner,
        '            if (arguments.Faults)\n'
        '            {\n'
        '                return Named("Faults", "GC-017");\n'
        '            }\n\n',
    )
    replace_once(
        runner,
        '            if (arguments.W5Gate)\n'
        '            {\n'
        '                return Named("W5Gate", "W5-GATE");\n'
        '            }\n\n',
    )
    replace_once(
        runner,
        '            if (arguments.Gc021)\n'
        '            {\n'
        '                return Named("Gc021", "GC-021");\n'
        '            }\n\n',
    )
    replace_once(
        runner,
        '            if (arguments.LifecycleStress)\n'
        '            {\n'
        '                return Named("LifecycleStress", "GC-022");\n'
        '            }\n\n',
    )
    replace_once(
        runner,
        '            if (arguments.W6Gate)\n'
        '            {\n'
        '                return Named("W6Gate", "W6-GATE");\n'
        '            }\n\n',
    )
    replace_once(
        runner,
        '                else if (arguments.Faults)\n'
        '                {\n'
        '                    ProbeFaults.Run(report);\n'
        '                    report.CompletePositive();\n'
        '                }\n',
    )
    replace_once(
        runner,
        '                else if (arguments.W5Gate)\n'
        '                {\n'
        '                    ProbeW5Gate.Run(report);\n'
        '                    report.CompletePositive();\n'
        '                }\n',
    )
    replace_once(
        runner,
        '                else if (arguments.Gc021)\n'
        '                {\n'
        '                    ProbeGc021.Run(report);\n'
        '                    report.CompletePositive();\n'
        '                }\n',
    )
    replace_once(
        runner,
        '                else if (arguments.LifecycleStress)\n'
        '                {\n'
        '                    ProbeLifecycleStress.Run(report);\n'
        '                    report.CompletePositive();\n'
        '                }\n',
    )
    replace_once(
        runner,
        '                else if (arguments.W6Gate)\n'
        '                {\n'
        '                    ProbeW6Gate.Run(report);\n'
        '                    report.CompletePositive();\n'
        '                }\n',
    )

    arguments = DESTINATION / RUNTIME / "ProbeArguments.cs"
    for old in (
        # ---------------------------------------------------------------- the boot/negative and family modes stay
        '        private const string FaultsArgumentName = "-probeFaults";\n',
        '        private const string W5GateArgumentName = "-probeW5Gate";\n',
        '        private const string Gc021ArgumentName = "-probeGc021";\n',
        '            bool faults,\n',
        '            bool w5Gate,\n',
        '            bool gc021,\n',
        '            Faults = faults;\n',
        '            W5Gate = w5Gate;\n',
        '            Gc021 = gc021;\n',
        '            bool faults = false;\n',
        '            bool w5Gate = false;\n',
        '            bool gc021 = false;\n',
        '        public bool Faults { get; }\n',
        '        public bool W5Gate { get; }\n',
        '        public bool Gc021 { get; }\n',
        '                else if (argument == FaultsArgumentName)\n'
        '                {\n'
        '                    faults = true;\n'
        '                }\n',
        '                else if (argument == W5GateArgumentName)\n'
        '                {\n'
        '                    w5Gate = true;\n'
        '                }\n',
        '                else if (argument == Gc021ArgumentName)\n'
        '                {\n'
        '                    gc021 = true;\n'
        '                }\n',
        '        private const string LifecycleStressArgumentName = "-probeLifecycleStress";\n',
        '            bool lifecycleStress,\n',
        '            LifecycleStress = lifecycleStress;\n',
        '            bool lifecycleStress = false;\n',
        '        /// <summary>\n'
        '        /// Runs the GC-022 lifecycle stress: the counted mount/unmount cycles over each family\'s committed generated\n'
        '        /// catalog and over its fixture identity set, with delayed completions, stalled jobs, a throwing disposer,\n'
        '        /// required-provider churn and headless cleanup, under native leak detection with full stack traces\n'
        '        /// (P-047, P-048, P-050).\n'
        '        /// </summary>\n'
        '        public bool LifecycleStress { get; }\n\n',
        '                else if (argument == LifecycleStressArgumentName)\n'
        '                {\n'
        '                    lifecycleStress = true;\n'
        '                }\n',
        '        private const string W6GateArgumentName = "-probeW6Gate";\n',
        '            bool w6Gate,\n',
        '            W6Gate = w6Gate;\n',
        '            bool w6Gate = false;\n',
        '        /// <summary>\n'
        '        /// Runs the Wave 6 integration-gate mode: the fixed-step traversal course with the cost counters and the\n'
        '        /// recorded-input replay, the durable reward delivery across an unload/reload of its receiving world, the\n'
        '        /// composition audit that keeps the optional physics/animation/audio surface out of cards and narrative, and\n'
        '        /// the create/mount/step/unmount/teardown loop over all three genres (W6-GATE).\n'
        '        /// </summary>\n'
        '        public bool W6Gate { get; }\n\n',
        '                else if (argument == W6GateArgumentName)\n'
        '                {\n'
        '                    w6Gate = true;\n'
        '                }\n',
    ):
        replace_once(arguments, old)

    replace_once(
        arguments,
        '            || Gc013 || W4Gate || Faults || Gc018 || Gc019 || W5Gate || Traversal || Gc021\n',
        '            || Gc013 || W4Gate || Gc018 || Gc019\n',
    )
    replace_once(
        arguments,
        '                w4Gate, faults, gc018, gc019, w5Gate, traversal, gc021, replay, w6Gate, resultPath);',
        '                w4Gate, gc018, gc019, replay, resultPath);',
    )
    for removed in (
        '            || LifecycleStress\n',
        '            || W6Gate\n',
        '                lifecycleStress,\n',
    ):
        replace_once(arguments, removed)

    print(f"Marker-free release project: {DESTINATION}")
    print("Build: UNITY_PROJECT=<above> ARTIFACTS=artifacts/faults/release tools/unity/build_probe.sh")


if __name__ == "__main__":
    main()
