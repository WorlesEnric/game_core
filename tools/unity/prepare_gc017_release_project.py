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
    manifest.pop("testables")
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    (DESTINATION / "Packages/packages-lock.json").unlink()

    # The fault scenario is a qualification fixture, not a shipping entry point.
    # The ordinary narrative/cards probe modes remain identical to validation.
    shutil.rmtree(DESTINATION / "Assets/GameCore.Validation/Tests")
    (DESTINATION / "Assets/GameCore.Validation/Tests.meta").unlink()
    for name in ("FaultScenario", "FaultScenarioHost", "FaultScenarioStep", "ProbeFaults"):
        for suffix in (".cs", ".cs.meta"):
            (DESTINATION / RUNTIME / (name + suffix)).unlink()

    runner = DESTINATION / RUNTIME / "ProbeRunner.cs"
    replace_once(
        runner,
        '                                                : arguments.Faults\n'
        '                                                    ? new ProbeReport(\n'
        '                                                        "Faults",\n'
        '                                                        ProbeEnvironment.DeclaredUnityVersion,\n'
        '                                                        ProbeEnvironment.DeclaredTarget,\n'
        '                                                        "GC-017")\n'
        '                                                : new ProbeReport(',
        '                                                : new ProbeReport(',
    )
    replace_once(
        runner,
        '                else if (arguments.Faults)\n'
        '                {\n'
        '                    ProbeFaults.Run(report);\n'
        '                    report.CompletePositive();\n'
        '                }\n',
    )
    arguments = DESTINATION / RUNTIME / "ProbeArguments.cs"
    for old in (
        '        private const string FaultsArgumentName = "-probeFaults";\n',
        '            bool faults,\n',
        '            Faults = faults;\n',
        '            bool faults = false;\n',
        '        public bool Faults { get; }\n',
        '                else if (argument == FaultsArgumentName)\n'
        '                {\n'
        '                    faults = true;\n'
        '                }\n',
    ):
        replace_once(arguments, old)
    replace_once(arguments, '            || Gc013 || W4Gate || Faults\n',
                 '            || Gc013 || W4Gate\n')
    replace_once(arguments, '                w4Gate, faults, resultPath);',
                 '                w4Gate, resultPath);')
    print(f"Marker-free release project: {DESTINATION}")
    print("Build: UNITY_PROJECT=<above> ARTIFACTS=artifacts/faults/release tools/unity/build_probe.sh")


if __name__ == "__main__":
    main()
