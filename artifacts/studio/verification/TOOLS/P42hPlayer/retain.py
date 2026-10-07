#!/usr/bin/env python3
"""Retain text evidence only; original movies/binaries stay in .evidence.

python3 artifacts/studio/verification/TOOLS/P42hPlayer/retain.py \
  --source "$PWD/.evidence/P42hPlayer"
Destination must be fresh. Invoke only after build/recording processes finish.
"""
import argparse
import gzip
import hashlib
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[5]
DEFAULT_DESTINATION = ROOT / "artifacts/studio/verification/W-GAME-01/p42h-player"
MOVIES = {".mp4", ".mkv", ".mov", ".webm", ".avi"}
TEXT = {".log", ".txt", ".json", ".csv"}
CSV_GZIP_BYTES = 1024 * 1024


def digest(path):
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(block)
    return result.hexdigest()


def scrub(text):
    text = text.replace(str(ROOT), "<REPO>")
    text = text.replace(str(Path.home()), "<HOME>")
    return re.sub(r"/(?:home|Users)/[^/\s\"'<>]+|/root(?=/|[\s\"']|$)", "<HOME>", text)


def dump(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=ROOT / ".evidence/P42hPlayer")
    parser.add_argument("--destination", type=Path, default=DEFAULT_DESTINATION)
    args = parser.parse_args()
    source = args.source.resolve()
    destination = args.destination.resolve()
    evidence = (ROOT / ".evidence").resolve()
    if not source.is_dir() or not source.is_relative_to(evidence):
        parser.error("source must be an existing directory inside this clone's .evidence")
    if destination == source or destination.is_relative_to(source):
        parser.error("destination cannot be inside source")
    destination.mkdir(parents=True, exist_ok=False)
    receipt = {"row": "W-GAME-01", "source": str(source.relative_to(ROOT)),
               "retentionStatus": "RUNNING", "qualification": "See retained result.json; retention is not qualification",
               "policy": "Only sanitized logs/JSON/CSV/text copied. CSV >=1 MiB gzip-compressed deterministically. All movies remain external, including every movie >20 MiB. No player binaries copied.",
               "files": [], "externalMovies": [], "skippedFiles": 0}
    try:
        # Snapshot names before writing; no service, credential, or runtime reads.
        for path in sorted(source.rglob("*")):
            if path.is_symlink():
                receipt["skippedFiles"] += 1
                continue
            if not path.is_file():
                continue
            relative = path.relative_to(source)
            suffix = path.suffix.lower()
            if suffix in MOVIES:
                receipt["externalMovies"].append({"path": str(path.relative_to(ROOT)),
                    "bytes": path.stat().st_size, "sha256": digest(path),
                    "over20MiB": path.stat().st_size > 20 * 1024 * 1024})
                continue
            # Player/source snapshots can contain arbitrary third-party text. Keep
            # only builder receipts from these trees; runtime evidence is elsewhere.
            if relative.parts[0] in {"player", "sources", "capture-launcher"} and str(relative) not in {"player/build-report.json", "player/revision.txt"}:
                receipt["skippedFiles"] += 1
                continue
            if suffix not in TEXT:
                receipt["skippedFiles"] += 1
                continue
            size = path.stat().st_size
            compressed = suffix == ".csv" and size >= CSV_GZIP_BYTES
            target = destination / relative
            if compressed:
                target = target.with_suffix(target.suffix + ".gz")
            target.parent.mkdir(parents=True, exist_ok=True)
            source_hash = digest(path)
            with path.open("r", encoding="utf-8", errors="strict") as original:
                if compressed:
                    with target.open("wb") as output:
                        with gzip.GzipFile(filename="", mode="wb", fileobj=output, mtime=0) as packed:
                            for line in original:
                                packed.write(scrub(line).encode("utf-8"))
                else:
                    with target.open("w", encoding="utf-8", newline="") as output:
                        for line in original:
                            output.write(scrub(line))
            receipt["files"].append({"source": str(relative), "retained": str(target.relative_to(destination)),
                "sourceBytes": size, "sourceSha256": source_hash, "retainedSha256": digest(target),
                "homePathsSanitized": True, "gzip": compressed})
        receipt["retentionStatus"] = "PASS"
    except Exception as error:
        receipt["retentionStatus"] = "FAIL"
        receipt["failure"] = scrub(str(error))
    finally:
        dump(destination / "external-movies.json", {"source": receipt["source"], "movies": receipt["externalMovies"]})
        dump(destination / "retention.json", receipt)
        with (destination / "SHA256SUMS").open("w") as hashes:
            for path in sorted(destination.rglob("*")):
                if path.is_file() and path.name != "SHA256SUMS":
                    hashes.write(digest(path) + "  " + str(path.relative_to(destination)) + "\n")
    print(json.dumps({"retentionStatus": receipt["retentionStatus"], "copiedFiles": len(receipt["files"]),
                      "externalMovies": len(receipt["externalMovies"]), "destination": scrub(str(destination))}))
    return 0 if receipt["retentionStatus"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
