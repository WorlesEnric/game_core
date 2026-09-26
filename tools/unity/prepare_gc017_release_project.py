#!/usr/bin/env python3
"""Clone the validation project for a real, marker-free GC-017 release player build.

Run from any directory. The output lives beside GameCore.Validation so its local
file:../../../Packages dependencies keep resolving. Build it with build_probe.sh
using UNITY_PROJECT=<printed path>, then pass its Builds/Linux64 directory to
check_player_fault_free.py. The generated project is disposable and gitignored.
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
        "com.unity.test-framework",
        "com.unity.test-framework.performance",
    ):
        del manifest["dependencies"][dependency]
    # The fault scenario and the Wave 5 integration gate are qualification fixtures, not shipping entry points:
    # both name the latch types that a configuration without the marker does not compile at all. The ordinary
    # narrative/cards/GC-018/GC-019 probe modes remain identical to validation.
    shutil.rmtree(DESTINATION / "Assets/GameCore.Validation/Tests")
    (DESTINATION / "Assets/GameCore.Validation/Tests.meta").unlink()
    for name in (
        "FaultScenario",
        "FaultScenarioHost",
        "FaultScenarioStep",
        "ProbeFaults",
        "W5GateScenario",
        "W5GateFamily",
        "W5GateNarrativeHost",
        "W5GateCardsHost",
        "ProbeW5Gate",
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

    arguments = DESTINATION / RUNTIME / "ProbeArguments.cs"
    for old in (
        '        private const string FaultsArgumentName = "-probeFaults";\n',
        '        private const string W5GateArgumentName = "-probeW5Gate";\n',
        '            bool faults,\n',
        '            bool w5Gate,\n',
        '            Faults = faults;\n',
        '            W5Gate = w5Gate;\n',
        '            bool faults = false;\n',
        '            bool w5Gate = false;\n',
        '        public bool Faults { get; }\n',
        '        public bool W5Gate { get; }\n',
        '                else if (argument == FaultsArgumentName)\n'
        '                {\n'
        '                    faults = true;\n'
        '                }\n',
        '                else if (argument == W5GateArgumentName)\n'
        '                {\n'
        '                    w5Gate = true;\n'
        '                }\n',
    ):
        replace_once(arguments, old)
    replace_once(
        arguments,
        '            || Gc013 || W4Gate || Faults || Gc018 || Gc019 || W5Gate\n',
        '            || Gc013 || W4Gate || Gc018 || Gc019\n',
    )
    replace_once(
        arguments,
        '                w4Gate, faults, gc018, gc019, w5Gate, resultPath);',
        '                w4Gate, gc018, gc019, resultPath);',
    )
    print(f"Marker-free release project: {DESTINATION}")
    print("Build: UNITY_PROJECT=<above> ARTIFACTS=artifacts/faults/release tools/unity/build_probe.sh")


if __name__ == "__main__":
    main()
