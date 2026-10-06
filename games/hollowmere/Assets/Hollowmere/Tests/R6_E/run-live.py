#!/usr/bin/env python3
"""Prepare trusted context, run the graphical regression, or retain its nonsecret evidence.

Build the real companion and Scratch crate before use. Run prepare after the final source
checkpoint, then start r6-e-scratch --context OUTPUT --companion BINARY --cache CACHE
--evidence .evidence/r6-e/RUN [--state-root /tmp/r6-e-service]. Wait for R6_E_READY before
running graphical RUN/graphical/live-config.json. Keep the scratch process alive through
undo, then retain CONFIG Evidence~/RUN. No installed services or credential files are read.
"""
import argparse
import gzip
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
import xml.etree.ElementTree as ET


HERE = Path(__file__).resolve().parent
REPO = HERE.parents[5]
PROJECT = REPO / "games/hollowmere"


def load(path):
    return json.loads(path.read_text())


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def launcher(evidence, method, flag, argument, pairing):
    require(not (PROJECT / "UserSettings/GameCoreStudio.json").exists(),
            "project ETOS settings must be absent; the driver will not read or change them")
    env = os.environ.copy()
    env["UNITY"] = str(HERE / "graphical-unity.sh")
    env["GAMECORE_R6_E_UNITY_EDITOR"] = env.get(
        "R6_E_UNITY_EDITOR", str(Path.home() / "Unity/Hub/Editor/6000.0.75f1/Editor/Unity"))
    # The host allocator filters *_KEY_* names; the wrapper sets the real resolver name
    # immediately before Unity exec. This value is a path, never credential contents.
    env["GAMECORE_R6_E_PAIRING_PATH"] = str(pairing)
    env["DISPLAY"] = ":1"
    command = [str(REPO / "studio/tools/unity-batch.sh"),
               "--project", str(PROJECT), "--log-dir", str(evidence / "live-logs"),
               "--label", "r6-e-" + ("prepare" if method.endswith("Prepare") else "graphical"),
               "--timeout", "600", "--attempts", "1", "--", "-force-glcore",
               "-executeMethod", method, flag, str(argument),
               "-upmLogFile", str(evidence / "upm.log")]
    return subprocess.call(command, cwd=REPO, env=env)


def prepare(output):
    require(not output.exists(), "prepare requires a fresh context filename")
    output.parent.mkdir(parents=True, exist_ok=True)
    evidence = output.parent / (output.stem + "-prepare")
    evidence.mkdir()
    # Explicit missing scratch pairing prevents ordinary startup from reading host pairing.
    with tempfile.TemporaryDirectory(prefix="r6-e-unpaired-") as directory:
        status = launcher(evidence, "Hollowmere.R6_E.RealAdmission.Prepare", "-gcR6EContext",
                          output, Path(directory) / "not-paired.json")
    require(status == 0 and output.is_file(), "context preparation failed; see prepare logs")
    print("R6_E_CONTEXT", output, flush=True)


def verify(evidence):
    admitted = load(evidence / "live-admit.json")
    undone = load(evidence / "live-undo.json")
    progress = load(evidence / "live-progress.json")
    refresh = load(evidence / "automatic-refresh.json")
    require(admitted["outcome"] == "Admitted" and undone["outcome"] == "Undone", "terminal outcomes failed")
    require(progress["phase"] == "complete", "driver did not finish")
    require(0 <= admitted["milliseconds"] <= 90000 and 0 <= admitted["admissionWallMs"] <= 90000,
            "admission exceeded unchanged 90s bound")
    require(0 <= undone["undoWallMs"] <= 180000, "undo exceeded 180s bound")
    require(admitted["live"] == admitted["predicted"] and admitted["confinement"] == "docker", "signed catalog mismatch")
    require(admitted["smokeSteps"] == 120 and admitted["smokeTransitions"] == ["Pending", "Passed"], "real smoke failed")
    require(admitted["coins"] == 9, "capture was not restored")
    require(undone["before"] == undone["live"] == admitted["before"], "undo did not restore catalog")
    require(admitted["graphics"]["isPlaying"] and not admitted["graphics"]["isBatchMode"]
            and admitted["graphics"]["display"] == ":1", "resumed graphical Play witness absent")
    require(refresh["companionVerified"] and refresh["currentRuntimeBound"]
            and refresh["domain"] == admitted["resumedDomain"]
            and refresh["domain"] != refresh["admissionDomain"], "automatic final-domain refresh absent")
    require(any(row["phase"] == "admission" and row.get("packageInstalled")
                and row.get("pendingPhase") in ("compile", "reload") for row in progress["reloads"]),
            "no genuine package compile/domain reload was observed")
    require((evidence / "admitted-play.png").is_file(), "resumed Play screenshot missing")
    before = load(evidence.parent / "ready.json")["beforeLive"]
    deadline = time.monotonic() + 5
    while True:
        audit = load(evidence.parent / "scratch-node.json")
        if (audit["verdictFetches"] >= before["verdictFetches"] + 2
                and audit["verdictVerifications"] >= before["verdictVerifications"] + 2):
            break
        require(time.monotonic() < deadline, "authenticated initial and post-reload proxy traffic absent")
        time.sleep(0.25)
    require(audit["providerCalls"] == audit["taskCount"] == 0, "forbidden provider or worker task observed")
    for name in ("editmode.xml", "playmode.xml"):
        root = ET.parse(evidence.parent / name).getroot()
        cases = root.findall(".//test-case")
        require(cases and all(case.get("result") == "Passed" for case in cases), name + " has missing or nonpassing cases")
    return admitted


def snapshot_owned_state(config, evidence):
    change_set = config["request"]["changeSetId"]
    require(change_set.startswith("cs_") and change_set[3:].isalnum(), "invalid evidence identity")
    destination = evidence / "admission-state"
    destination.mkdir(exist_ok=True)
    root = PROJECT / "Studio/Admission"
    for path in root.glob("*" + change_set + "*.json"):
        if path.is_file() and not path.is_symlink():
            shutil.copyfile(path, destination / path.name)
    for path in (PROJECT / "Studio/History").glob("*/*/" + change_set + ".json"):
        shutil.copyfile(path, destination / ("journal-" + path.name))


def graphical(config_path):
    config = load(config_path)
    evidence = Path(config["evidence"]).resolve()
    require(config_path.parent == evidence and evidence.is_relative_to(REPO / ".evidence/r6-e"), "unexpected evidence location")
    require(not (evidence / "live-progress.json").exists(), "use fresh live evidence; previous progress must remain intact")
    pairing = Path(config["pairingFile"]).resolve()
    require(pairing.is_file() and not pairing.is_relative_to(REPO), "external scratch pairing prerequisite missing")
    started = time.monotonic()
    status = launcher(evidence, "Hollowmere.R6_E.RealAdmission.Run", "-gcR6EConfig", config_path, pairing)
    (evidence / "launcher-wall.json").write_text(json.dumps({
        "milliseconds": round((time.monotonic() - started) * 1000), "exitCode": status,
    }, indent=2) + "\n")
    snapshot_owned_state(config, evidence)
    require(status == 0, "graphical Editor failed; durable recovery and logs remain untouched")
    admitted = verify(evidence)
    print("R6_E_PASS admissionWallMs=", admitted["admissionWallMs"], flush=True)


def retain(config_path, destination):
    config = load(config_path)
    graphical_root = Path(config["evidence"]).resolve()
    root = graphical_root.parent
    require(root.is_relative_to(REPO / ".evidence/r6-e"), "unexpected evidence source")
    require(not destination.exists(), "retention destination must be fresh")
    require(destination.is_relative_to(HERE / "Evidence~"), "retention must stay inside R6_E/Evidence~")
    destination.mkdir(parents=True)
    # Explicit public evidence allowlist: never recurse into installation state, credentials,
    # ledger, CAS, HOME, UserSettings or the companion's signing-key directory.
    selected = [root / name for name in (
        "project-context.json", "queued-job.json", "service-job.json", "stage-wall.json",
        "signed-verdict.json", "service-verify.json", "unauthenticated.json", "scratch-node.json",
        "semantic-findings.json", "editmode.xml", "playmode.xml", "issuance.json")]
    selected += [graphical_root / name for name in (
        "live-progress.json", "live-admit.json", "live-undo.json", "automatic-refresh.json",
        "last-pending.json", "live-failure.txt", "launcher-wall.json", "admitted-play.png", "upm.log")]
    selected += sorted((graphical_root / "live-logs").glob("*"))
    selected += sorted((graphical_root / "admission-state").glob("*.json"))
    selected += [root / "installation.json", root / "ready.json", root / "candidate/change-set.json"]
    manifest = {"sourceRevision": config["request"]["sourceRevision"], "jobId": config["jobId"],
                "files": [], "authority": "real companion, scratch authentication-only node"}
    for source in selected:
        if not source.is_file() or source.is_symlink():
            continue
        data = source.read_bytes()
        relative = source.relative_to(root)
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        if source.suffix == ".png":
            target.write_bytes(data)
        else:
            target = target.with_name(target.name + ".gz")
            target.write_bytes(gzip.compress(data, mtime=0))
        manifest["files"].append({"path": str(target.relative_to(destination)),
                                  "uncompressedBytes": len(data), "sha256": hashlib.sha256(data).hexdigest()})
    (destination / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print("R6_E_RETAINED", destination, flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="mode", required=True)
    commands.add_parser("prepare").add_argument("context", type=Path)
    commands.add_parser("graphical").add_argument("config", type=Path)
    retention = commands.add_parser("retain")
    retention.add_argument("config", type=Path)
    retention.add_argument("destination", type=Path)
    args = parser.parse_args()
    if args.mode == "prepare":
        prepare(args.context.resolve())
    elif args.mode == "graphical":
        graphical(args.config.resolve())
    else:
        retain(args.config.resolve(), args.destination.resolve())


if __name__ == "__main__":
    main()
