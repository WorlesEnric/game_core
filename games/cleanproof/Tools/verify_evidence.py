#!/usr/bin/env python3
"""Validate clean-proof acceptance evidence, including exact XML case identities."""
import hashlib
import json
from pathlib import Path
import re
from datetime import datetime, timezone
import socket
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "artifacts/studio/cleanproof"


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()


def suite(folder, names):
    path = OUT / folder / "results.xml"
    root = ET.parse(path).getroot()
    cases = list(root.iter("test-case"))
    observed = {case.attrib["fullname"] for case in cases}
    if observed != set(names) or any(case.attrib["result"] != "Passed" for case in cases):
        raise SystemExit(f"Incomplete/failed XML: {path}; cases={observed}")
    return {"xml": str(path.relative_to(ROOT)), "passed": len(cases), "failed": 0,
            "skipped": 0, "inconclusive": 0, "durationSeconds": float(root.attrib["duration"])}


def digest(path):
    hashed = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            hashed.update(chunk)
    return hashed.hexdigest()


def main():
    edit = "Saltmarsh.Tests.CleanProofAuthoringTests."
    play = "Saltmarsh.Tests.SaltmarshQuestHeadless."
    suites = [suite("editmode-final", [edit + name for name in [
        "W_CLEAN_01_AllTwelveGroupsAreDiscoverable",
        "W_CLEAN_01_AuthorAllIsIdempotentAndJournaled",
        "W_CLEAN_02_BakeVerifyDetectsStaleOutput"]]),
        suite("playmode-final", [play + name for name in [
            "W_CLEAN_01_SalvageBranchSaveRestoreAndConsequence",
            "W_CLEAN_01_SpareBranchSaveRestoreAndConsequence",
            "W_CLEAN_01_ThreeRegionLoopPreservesNpcIdentity"]])]
    player = json.loads((OUT / "player-result.json").read_text())
    log = (OUT / "player.log").read_text(errors="replace")
    match = re.search(r"\[Saltmarsh\] AUTOPLAY PASS frames=(\d+) pumps=(\d+) violations=(\d+)", log)
    if player["exitCode"] != 0 or not match or int(match[1]) < 600 or int(match[3]) != 0:
        raise SystemExit("Player did not exit successfully after a healthy 600-frame run")
    player.update(frames=int(match[1]), pumps=int(match[2]), violations=int(match[3]))
    runtime_diff = git("diff", "--name-only", player["buildSourceRevision"], "--",
                       "games/cleanproof/Assets/Saltmarsh", "games/cleanproof/Assets/Boot",
                       ":(exclude)**/Editor/**", ":(exclude)**/Tests/**")
    if runtime_diff:
        raise SystemExit("Runtime/baked assets differ from the qualified build source: " + runtime_diff)
    diff = git("diff", "--stat", "origin/main", "--", "Packages/")
    if diff:
        raise SystemExit("Package diff must be empty: " + diff)
    (OUT / "package-diff.txt").write_text(diff)
    build = ROOT / "games/cleanproof/Builds/Linux"
    excluded = [p.name for p in build.iterdir() if p.is_dir() and
                ("ButDontShipItWithYourGame" in p.name or "BurstDebugInformation_DoNotShip" in p.name)]
    files = sorted(p for p in build.rglob("*") if p.is_file() and p.relative_to(build).parts[0] not in excluded)
    if not (build / "Saltmarsh.x86_64").is_file() or not (build / "GameAssembly.so").is_file():
        raise SystemExit("Linux IL2CPP executable or GameAssembly.so missing")
    lines = [digest(p) + "  " + p.relative_to(build).as_posix() for p in files]
    manifest = ("\n".join(lines) + "\n").encode()
    (OUT / "build-files.sha256").write_bytes(manifest)
    journals = [json.loads(p.read_text()) for p in (ROOT / "games/cleanproof/Studio/History").rglob("*.json")]
    proof = {"sourceRevision": git("rev-parse", "HEAD"), "baseRevision": git("rev-parse", "origin/main"),
             "host": socket.gethostname(), "verifiedAtUtc": datetime.now(timezone.utc).isoformat(), "suites": suites, "player": player,
             "buildManifestSha256": hashlib.sha256(manifest).hexdigest(),
             "executableSha256": digest(build / "Saltmarsh.x86_64"),
             "excludedDebugDirectories": excluded, "buildFiles": len(files), "buildBytes": sum(p.stat().st_size for p in files),
             "packageDiff": diff, "qualifiedRuntimeDiff": runtime_diff, "journalStates": {state: sum(j.get("state") == state for j in journals)
             for state in sorted({j["state"] for j in journals})}}
    (OUT / "proof.json").write_text(json.dumps(proof, indent=2) + "\n")
    print(json.dumps(proof, indent=2))


if __name__ == "__main__":
    main()
