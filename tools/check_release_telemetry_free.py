#!/usr/bin/env python3
"""Is the GC-023 telemetry switch really free when it is off?

The claim: a shipping build performs no counting work at all, because every on-device count is an argument-evaluating
call to a `[Conditional("GAMECORE_TELEMETRY")]` helper, and the compiler removes the call and its argument evaluation
when the symbol is undefined. Two independent checks, each of which can fail on its own, and each with a
non-vacuous second half:

  1. **Behaviour, both configurations.** `dotnet/tools/GameCore.TelemetryProbe` calls the helpers with side-effecting
     arguments and reads the counters back. `-c Release` (symbol undefined) must print `sideEffects=0;observed=0`;
     `-c Qualification` (symbol defined) must print non-zero values. The second half is what makes the first half mean
     something: a probe that counted nothing because it was never called would print zeros in both configurations.

  2. **The real instrumented sources, both configurations.** `dotnet/src/GameCore.Telemetry.ReleaseCheck` compiles
     `Packages/com.gamecore.derivation/Runtime/**` — the richest GC-023 instrumentation in the kernel — without the
     symbol and with it. The Release assembly must contain no reference to the counting helper at all (no call site
     survived); the Qualification assembly must contain them. A new unguarded counting call in the derivation package
     fails this check on the next run.

  3. **The switch itself.** The symbol must be produced by the asmdef `versionDefines` entry on the marker package
     `com.gamecore.telemetry-qualification`, and that marker must be referenced by the validation project's manifest
     only, so a shipping Unity project cannot define it. `GAMECORE_TELEMETRY` must never appear in a runtime asmdef's
     `defineConstraints` (which would make an assembly simply not compile) and never in `dotnet/Directory.Build.props`.

With --release-player-project, inspect both built Unity IL2CPP generated-code trees and their replay result JSONs.
The release project must omit the marker, emit zero counting references in the runtime assemblies, compile
IsCompiledIn=false and report zero duration samples; the qualification player must establish the opposite.

Evidence: `--json <path>` writes the machine-readable result. Exit 0 only when every check passes.

Usage:
    python3 tools/check_release_telemetry_free.py [--json artifacts/gc-023/telemetry-release-surface.json]
        [--no-build] [--dotnet PATH]
        [--release-player-project unity/GameCore.ReleaseCheck --artifacts artifacts/gc-023]
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]

SYMBOL = "GAMECORE_TELEMETRY"
MARKER_PACKAGE = "com.gamecore.telemetry-qualification"
PROBE_PROJECT = "dotnet/tools/GameCore.TelemetryProbe/GameCore.TelemetryProbe.csproj"
PROBE_ASSEMBLY = "dotnet/tools/GameCore.TelemetryProbe/bin/{config}/net8.0/GameCore.TelemetryProbe.dll"
RELEASE_CHECK_PROJECT = (
    "dotnet/src/GameCore.Telemetry.ReleaseCheck/GameCore.Telemetry.ReleaseCheck.csproj"
)
RELEASE_CHECK_ASSEMBLY = (
    "dotnet/src/GameCore.Telemetry.ReleaseCheck/bin/{config}/netstandard2.1/GameCore.Derivation.ReleaseCheck.dll"
)

# The counting helpers and the type that holds them. A Release assembly may name none of these.
COUNTING_HELPERS = (
    "TelemetryCounting",
    "IsCompiledIn",
)

# Every runtime asmdef that must be able to define the symbol, and the ones that must not mention it.
QUALIFICATION_ASMDEFS = (
    # The contracts assembly carries the switch too: `TelemetrySchema.IsCompiledIn` reports the build shape that
    # assembly was compiled in, and every instrumented assembly takes the symbol from the same marker package, so
    # the answer is the same everywhere.
    "Packages/com.gamecore.contracts/Runtime/GameCore.Contracts.asmdef",
    "Packages/com.gamecore.derivation/Runtime/GameCore.Derivation.asmdef",
    "Packages/com.gamecore.composition/Runtime/GameCore.Composition.asmdef",
    "Packages/com.gamecore.planning/Runtime/GameCore.Planning.asmdef",
    "Packages/com.gamecore.unity.runtime/Runtime/GameCore.Unity.Runtime.asmdef",
)

VALIDATION_MANIFEST = "unity/GameCore.Validation/Packages/manifest.json"

RELEASE_RUNTIME_ASSEMBLIES = (
    "GameCore.Derivation",
    "GameCore.Composition",
    "GameCore.Planning",
    "GameCore.Unity.Runtime",
)


def check_player(release_project: Path, qualification_project: Path, artifacts: Path,
                 problems: list[str], facts: dict) -> None:
    """Inspect both IL2CPP builds and exercise the marker-free player's recorded replay result."""
    for project, is_release in ((release_project, True), (qualification_project, False)):
        manifest = json.loads((project / "Packages/manifest.json").read_text(encoding="utf-8"))
        marker_present = MARKER_PACKAGE in manifest.get("dependencies", {})
        if marker_present == is_release:
            problems.append(f"{project}: telemetry marker does not match its expected build shape")
        player = project / "Builds/Linux64/GameCoreProbe.x86_64"
        generated = project / "Builds/Linux64/GameCoreProbe_BackUpThisFolder_ButDontShipItWithYourGame/il2cppOutput"
        label = "releasePlayer" if is_release else "qualificationPlayer"
        if not player.is_file() or not generated.is_dir():
            problems.append(f"{project}: built IL2CPP player or generated C++ is missing")
            continue
        sites = 0
        for assembly in RELEASE_RUNTIME_ASSEMBLIES:
            sources = sorted(generated.glob(assembly + "*.cpp"))
            if not sources:
                problems.append(f"{project}: no generated C++ for {assembly}")
            for source in sources:
                sites += len(re.findall(r"TelemetryCounting_(?:Count|Add|Observe)_m", source.read_text(encoding="utf-8")))
        facts[label] = {"project": str(project), "player": str(player), "countingMentions": sites,
                        "markerPresent": marker_present}
        if is_release and sites:
            problems.append(f"{project}: {sites} counting mentions survived in runtime IL2CPP output")
        if not is_release and not sites:
            problems.append(f"{project}: qualification IL2CPP output has no counting mentions")

        contracts = "\n".join(source.read_text(encoding="utf-8") for source in generated.glob("GameCore.Contracts*.cpp"))
        compiled_in = re.search(r"TelemetrySchema_get_IsCompiledIn_m\w+\s*\([^)]*\)\s*\{\s*\{\s*return \(bool\)([01]);", contracts)
        if compiled_in is None or (compiled_in.group(1) == "1") == is_release:
            problems.append(f"{project}: generated telemetry switch is missing or has the wrong value")

        result_path = artifacts / ("release" if is_release else "toolchain") / "probe-replay.json"
        if not result_path.exists():
            problems.append(f"{result_path}: real player replay result is missing")
            continue
        result = json.loads(result_path.read_text(encoding="utf-8"))
        wake = next((step for step in result.get("probes", [])
                     if step.get("name") == "replay-world-wake-commits-one-step-and-samples-durations"), None)
        detail = wake.get("detail", "") if wake else ""
        samples = re.search(r"jobWaitSamples=(\d+).*stageSamples=(\d+)", detail)
        facts[label]["replayResult"] = result.get("result")
        facts[label]["durationSamples"] = list(map(int, samples.groups())) if samples else None
        if result.get("result") != "Pass" or not samples:
            problems.append(f"{result_path}: player replay failed or duration counters are missing")
        elif (int(samples.group(1)) == 0 or int(samples.group(2)) == 0) != is_release:
            problems.append(f"{result_path}: duration samples do not match the telemetry build shape")


def run(command: list[str], cwd: Path, env: dict[str, str] | None = None) -> tuple[int, str]:
    merged = dict(os.environ)
    if env:
        merged.update(env)
    completed = subprocess.run(
        command, cwd=str(cwd), capture_output=True, text=True, env=merged, check=False
    )
    return completed.returncode, completed.stdout + completed.stderr


def build(dotnet: str, project: str, configuration: str) -> tuple[bool, str]:
    code, output = run(
        [dotnet, "build", project, "-c", configuration, "--nologo", "-v", "quiet"], REPO_ROOT
    )
    return code == 0, output


def assembly_contains(path: Path, needle: str) -> bool:
    """True when the compiled assembly's metadata contains the literal string.

    A member or type name reaches the metadata #Strings heap exactly when some code references it, so this is a
    direct reading of "the call site survived", not a guess about compiler behaviour.
    """
    if not path.exists():
        return False
    return needle.encode("utf-8") in path.read_bytes()


def check_probe(dotnet: str, problems: list[str], facts: dict) -> None:
    project = str(REPO_ROOT / PROBE_PROJECT)
    for configuration, expectation, wanted in (
        ("Release", "disabled", False),
        ("Qualification", "enabled", True),
    ):
        built, output = build(dotnet, project, configuration)
        facts[f"probe{configuration}Build"] = "Pass" if built else "Fail"
        if not built:
            problems.append(f"the telemetry probe did not build in {configuration}: {output.strip()[-600:]}")
            continue

        assembly = REPO_ROOT / PROBE_ASSEMBLY.format(config=configuration)
        references = assembly_contains(assembly, "TelemetryCounting")
        facts[f"probe{configuration}ReferencesCounting"] = references
        if wanted and not references:
            problems.append(
                f"the {configuration} probe assembly references no counting helper, so the check would pass "
                "against an empty comparison"
            )
        if not wanted and references:
            problems.append(
                f"the {configuration} probe assembly still references a counting helper, so a call site survived "
                "a compilation without the symbol"
            )

        code, output = run(
            [dotnet, "run", "--project", project, "-c", configuration, "--no-build", "--", expectation],
            REPO_ROOT,
        )
        line = next(
            (candidate for candidate in output.splitlines() if candidate.startswith("compiledIn=")), ""
        )
        facts[f"probe{configuration}Output"] = line
        facts[f"probe{configuration}ExitCode"] = code
        if code != 0:
            problems.append(
                f"the {configuration} probe reported the wrong shape (exit {code}): "
                f"{line or output.strip()[-300:]}"
            )


def check_release_check(dotnet: str, problems: list[str], facts: dict) -> None:
    project = str(REPO_ROOT / RELEASE_CHECK_PROJECT)
    for configuration, wanted in (("Release", False), ("Qualification", True)):
        built, output = build(dotnet, project, configuration)
        facts[f"derivation{configuration}Build"] = "Pass" if built else "Fail"
        if not built:
            problems.append(
                f"the derivation release-check did not build in {configuration}: {output.strip()[-600:]}"
            )
            continue

        assembly = REPO_ROOT / RELEASE_CHECK_ASSEMBLY.format(config=configuration)
        if not assembly.exists():
            problems.append(f"the {configuration} release-check assembly is missing: {assembly}")
            continue

        present = {helper: assembly_contains(assembly, helper) for helper in COUNTING_HELPERS}
        facts[f"derivation{configuration}Counting"] = present
        if wanted:
            if not present["TelemetryCounting"]:
                problems.append(
                    "the qualification derivation assembly references no counting helper, so the Release half "
                    "proves nothing"
                )
        else:
            for helper, found in present.items():
                if found:
                    problems.append(
                        f"the Release derivation assembly still names '{helper}': a counting call site survived "
                        "a build without " + SYMBOL
                    )


def check_switch(problems: list[str], facts: dict) -> None:
    manifest = json.loads((REPO_ROOT / VALIDATION_MANIFEST).read_text(encoding="utf-8"))
    declared = manifest.get("dependencies", {}).get(MARKER_PACKAGE)
    facts["validationManifestMarker"] = declared
    if declared is None:
        problems.append(
            f"the validation project's manifest does not reference {MARKER_PACKAGE}, so no compilation can define "
            f"{SYMBOL}"
        )

    for relative in QUALIFICATION_ASMDEFS:
        data = json.loads((REPO_ROOT / relative).read_text(encoding="utf-8"))
        defines = [
            entry.get("define")
            for entry in data.get("versionDefines", [])
            if isinstance(entry, dict)
        ]
        markers = [
            entry.get("name")
            for entry in data.get("versionDefines", [])
            if isinstance(entry, dict)
        ]
        facts.setdefault("asmdefs", {})[relative] = {"defines": defines, "markers": markers}
        if SYMBOL not in defines:
            problems.append(f"{relative} does not define {SYMBOL} from the marker package")
        if MARKER_PACKAGE not in markers:
            problems.append(f"{relative} does not take {SYMBOL} from {MARKER_PACKAGE}")
        if SYMBOL in (data.get("defineConstraints") or []):
            problems.append(
                f"{relative} constrains its compilation on {SYMBOL}; the assembly must compile without it"
            )

    props = (REPO_ROOT / "dotnet/Directory.Build.props").read_text(encoding="utf-8")
    facts["directoryBuildPropsDefinesSymbol"] = SYMBOL in props
    if SYMBOL in props:
        problems.append(
            f"dotnet/Directory.Build.props defines {SYMBOL}; the switch must stay per-project so the shipping "
            "shape stays compilable"
        )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--json", dest="json_path", default="")
    parser.add_argument("--no-build", action="store_true")
    parser.add_argument("--dotnet", default=shutil.which("dotnet") or "dotnet")
    parser.add_argument("--release-player-project", type=Path,
                        help="marker-free built Unity project; requires a built qualification player and both replay results")
    parser.add_argument("--artifacts", type=Path, default=REPO_ROOT / "artifacts/gc-023",
                        help="directory containing release/ and toolchain/ replay results")
    arguments = parser.parse_args()

    problems: list[str] = []
    facts: dict = {"symbol": SYMBOL, "markerPackage": MARKER_PACKAGE}

    if arguments.no_build:
        facts["builds"] = "skipped (--no-build)"
    else:
        check_probe(arguments.dotnet, problems, facts)
        check_release_check(arguments.dotnet, problems, facts)

    check_switch(problems, facts)
    if arguments.release_player_project is not None:
        check_player(arguments.release_player_project.resolve(),
                     REPO_ROOT / "unity/GameCore.Validation", Path(arguments.artifacts).resolve(), problems, facts)

    result = {
        "artifact": "gamecore.gc023.telemetry-release-surface/1",
        "status": "Pass" if not problems else "Fail",
        "problems": problems,
        "facts": facts,
    }
    text = json.dumps(result, indent=2) + "\n"
    print(text)
    if arguments.json_path:
        path = Path(arguments.json_path)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
    return 0 if not problems else 1


if __name__ == "__main__":
    sys.exit(main())
