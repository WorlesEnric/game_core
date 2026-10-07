#!/usr/bin/env python3
"""Package the real lever candidate into an explicit directory outside Unity imports.

Usage: python3 .../Lever~/make-candidate.py --output /tmp/lever-work/candidate
Run make-catalog.py after source changes first. No build/test claims are fabricated:
the proposal names actual rule-test sources; authenticated Stage supplies their results.
"""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import sys

sys.dont_write_bytecode = True

HERE = Path(__file__).resolve().parent
ROOT = next(p for p in HERE.parents if (p / "tools/emit_generated_catalog.py").is_file())
PACKAGE = HERE / "package"


def load(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--id", default="cs_01K6R8CMECH00000000000000A")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    output = args.output.expanduser().resolve()
    for project in (ROOT / "games").iterdir():
        for imported in (project / "Assets", project / "Packages"):
            if output == imported or imported in output.parents:
                raise SystemExit("candidate output must be outside live Assets/Packages: " + str(output))
    imported = ROOT / "Packages"
    if output == imported or imported in output.parents:
        raise SystemExit("candidate output must be outside the trusted package tree")
    catalog = load("lever_catalog", HERE / "make-catalog.py")
    for path, data in catalog.outputs().items():
        if not path.is_file() or path.read_bytes() != data:
            raise SystemExit("stale catalog/content hash/metas; run make-catalog.py first: " + str(path))
    shared = load("mechanism_candidate", ROOT / "samples/mechanisms/pressure-plate/make-candidate.py")
    entries = shared.collect(PACKAGE)
    shared.check_metas(entries, "lever")
    archive = shared.tarball(entries)
    proposal = shared.canonical({
        "schema": "gamecore.studio.proposal/1", "package": "com.hollowmere.mechanism.lever", "version": "0.1.0",
        "adds": {"tools": [], "definitions": [], "slots": ["lever.state"], "routes": ["lever.toggle"], "events": ["LeverToggled"]},
        "smokeTest": {"type": "Hollowmere.Mechanism.Lever.LeverSmoke", "method": "Begin", "steps": 120},
        "catalog": {"type": "Hollowmere.Mechanism.Lever.Generated.LeverCatalog"},
        "rules": {"assembly": "Hollowmere.Mechanism.Lever.Rules", "directory": "Rules", "tests": "Tests/Rules"},
    })
    package_hash = hashlib.sha256(archive).hexdigest()
    proposal_hash = hashlib.sha256(proposal).hexdigest()
    change = {
        "schema": "gamecore.studio.changeset/1", "id": args.id,
        "intent": {"text": "Add a bistable lever whose position latches until the next bounded toggle.", "origin": "agent"},
        "operations": [{"opId": "op1", "tool": "mechanism.propose", "args": {
            "description": "One preserved int32 lever.state slot, bounded lever.toggle lane, committed LeverToggled events and handle presentation.",
            "package": {"artifact": "sha256:" + package_hash}, "proposal": {"artifact": "sha256:" + proposal_hash}, "stageInputs": []},
            "applyRequirement": "Compile"}],
        "requirements": {"max": "Compile", "worldRebuild": False, "compile": True, "build": False},
        "artifacts": [{"sha256": package_hash, "name": "package.tgz", "mediaType": "application/gzip", "bytes": len(archive), "role": "package"},
                      {"sha256": proposal_hash, "name": "proposal.json", "mediaType": "application/json", "bytes": len(proposal), "role": "proposal"}],
        "state": "Candidate",
    }
    schema = json.loads((ROOT / "docs/studio/schemas/change-set.schema.json").read_text())
    errors = []
    shared.validate(change, schema, schema, "$", errors)
    if errors:
        raise SystemExit("\n".join(errors))
    outputs = {output / "change-set.json": shared.canonical(change), output / "artifacts/package.tgz": archive,
               output / "artifacts/proposal.json": proposal}
    for path, data in outputs.items():
        if args.check:
            if not path.is_file() or path.read_bytes() != data:
                raise SystemExit("stale candidate: " + str(path))
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
    print(json.dumps({"candidate": str(output), "packageSha256": package_hash, "proposalSha256": proposal_hash,
                      "tests": "NotRun; Stage runs the included pure Rules and Unity suites"}, sort_keys=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())
