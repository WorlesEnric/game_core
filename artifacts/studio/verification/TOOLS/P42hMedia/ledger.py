#!/usr/bin/env python3
"""Main-owned read-only observer. Never loads credentials, calls ETOS, or submits media."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import sqlite3
import time


def utc():
    return datetime.now(timezone.utc).isoformat()


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def atomic_json(path, value):
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n")
    temporary.replace(path)


def snapshot(db, output, checkpoint):
    label = checkpoint["label"]
    result = {"row": checkpoint["row"], "label": label, "observedUtc": utc(),
              "status": "BLOCKED", "authority": "companion SQLite, mode=ro; no credentials or node operation",
              "accountingScope": "Binding tariff ledger, not an upstream invoice. All concurrent media would invalidate this isolated interval."}
    try:
        with sqlite3.connect(db.resolve().as_uri() + "?mode=ro", uri=True) as connection:
            connection.execute("PRAGMA query_only=ON")
            connection.execute("BEGIN")
            charges = [{"key": key, "charge": json.loads(charge), "createdAt": created}
                       for key, charge, created in connection.execute(
                           "SELECT key,charge,created_at FROM media_charges ORDER BY key")]
            # Only read packet-owned artifact metadata by its verified provider digest. No unrelated
            # request bodies, messages, configuration, credentials or media bytes are opened.
            artifact = None
            if label != "before":
                generated = json.loads((output / "generate.json").read_text())
                sha = generated["providerSha256"]
                stored = connection.execute(
                    "SELECT sha256,bytes,media_type,producer,change_set_id FROM artifacts WHERE sha256=?", (sha,)).fetchone()
                if stored is None:
                    raise ValueError("Verified provider digest has no companion artifact metadata")
                producer = json.loads(stored[3])
                artifact = {"sha256": stored[0], "bytes": stored[1], "mediaType": stored[2],
                            "changeSetId": stored[4], "producer": {
                                key: producer.get(key) for key in
                                ("op", "provider", "jobId", "key", "etosRef", "changeSetId", "charge")}}
        result.update({"charges": charges, "ledgerSha256": digest(charges), "chargeCount": len(charges),
                       "chargedUsd": sum(row["charge"]["costUsd"] for row in charges)})
        if label == "before":
            if checkpoint["generationCalls"] != 0:
                raise ValueError("Baseline was requested after generation was issued")
            result.update({"status": "PASS", "reason": "Baseline captured before the only allowed image submission"})
            return result
        before = json.loads((output / "ledger-before.json").read_text())
        baseline = {row["key"]: row for row in before["charges"]}
        current = {row["key"]: row for row in charges}
        if any(current.get(key) != row for key, row in baseline.items()):
            raise ValueError("Existing charge ledger entries changed or disappeared")
        additions = [row for row in charges if row["key"] not in baseline]
        result["newCharges"] = additions
        if len(additions) != 1:
            raise ValueError("Expected exactly one new image charge in the isolated run; found " + str(len(additions)))
        producer = artifact["producer"]
        charge = additions[0]
        if not producer["key"] or producer["key"] != charge["key"]:
            raise ValueError("New image charge is not the generated artifact's actual effect key")
        if producer["op"] != "generate.image" or not producer["etosRef"]:
            raise ValueError("Artifact lacks actual generate.image etosRef authority")
        if artifact["changeSetId"] is not None or producer["changeSetId"] is not None:
            raise ValueError("Direct media unexpectedly claimed changeSetId authority")
        tariff = charge["charge"]
        if tariff.get("tariff", {}).get("unit") != "image" or tariff.get("quantity") != 1:
            raise ValueError("Charge is not exactly one image under a bound image tariff")
        if not isinstance(tariff.get("costUsd"), (float, int)) or not 0 < tariff["costUsd"] <= 0.20:
            raise ValueError("Image charge must be positive, priced and at most USD0.20")
        if producer["charge"] != tariff:
            raise ValueError("Artifact producer charge does not match the immutable media charge")
        usage = generated.get("opState", {}).get("usage")
        if not isinstance(usage, dict):
            raise ValueError("Actual generation response has no node-reported usage object")
        if generated.get("maxCostUsd") != 0.20 or checkpoint["generationCalls"] != 1:
            raise ValueError("Driver did not enforce exactly one image call with USD0.20 ceiling")
        result.update({"artifact": artifact, "identity": {"effectKey": producer["key"], "jobId": producer["jobId"],
                       "etosRef": producer["etosRef"], "requestId": None, "taskIds": [],
                       "reason": "App-owned direct media route; no worker request or task was created"},
                       "nodeUsageAtGeneration": usage, "chargedUsdForRun": tariff["costUsd"]})
        if label != "generated":
            generation = json.loads((output / "ledger-generated.json").read_text())
            if result["ledgerSha256"] != generation["ledgerSha256"]:
                raise ValueError("Companion media usage/charges changed after initial generation")
            if artifact != generation["artifact"]:
                raise ValueError("Generated artifact producer identity changed during History replay")
            result["usageUnchangedSinceGeneration"] = True
        result.update({"status": "PASS", "reason": "Exactly one owned priced image; retained producer identity and charge ledger verified"})
    except (OSError, sqlite3.Error, ValueError, TypeError, KeyError) as error:
        result["reason"] = str(error)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--ledger", type=Path, default=Path.home() / ".local/share/etos-studio/agents/gamecore-studio/state/ledger.db")
    parser.add_argument("--timeout", type=float, default=1500)
    args = parser.parse_args()
    output = args.out.resolve()
    output.mkdir(parents=True, exist_ok=True)
    deadline = time.monotonic() + args.timeout
    print('LEDGER_OBSERVER_READY', flush=True)
    while time.monotonic() < deadline:
        checkpoint_path = output / "checkpoint.json"
        if checkpoint_path.exists():
            try:
                checkpoint = json.loads(checkpoint_path.read_text())
            except json.JSONDecodeError:
                time.sleep(0.1)
                continue
            label = checkpoint.get("label")
            if label not in {"before", "generated", "undone", "redone", "final"}:
                raise SystemExit("Unknown media checkpoint: " + str(label))
            target = output / ("ledger-" + label + ".json")
            if not target.exists():
                receipt = snapshot(args.ledger, output, checkpoint)
                atomic_json(target, receipt)
                print(json.dumps({"label": label, "status": receipt["status"], "reason": receipt["reason"]}), flush=True)
                if receipt["status"] != "PASS":
                    return 1
        final = output / "result.json"
        if final.exists():
            try:
                result = json.loads(final.read_text())
            except json.JSONDecodeError:
                time.sleep(0.1)
                continue
            return 0 if result.get("runtimeStatus") == "PASS" else 1
        time.sleep(0.25)
    atomic_json(output / "ledger-observer-error.json", {"status": "BLOCKED", "reason": "Bounded observer deadline", "utc": utc()})
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
