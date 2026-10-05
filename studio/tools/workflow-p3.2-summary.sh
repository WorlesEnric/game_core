#!/usr/bin/env bash
# workflow-p3.2-summary.sh - aggregates the P3.2 run folders (artifacts/studio/workflows/P3.2/runs/*) into
# artifacts/studio/workflows/P3.2/summary.json and prints a Markdown digest: per request tag its result, task ids,
# tokens, B-AGENT-UX timings (submit -> accepted / first event / candidate, preview, apply, undo, cancel ack, visible
# lag), and the totals. Runs anywhere (python3 only; reads files, builds nothing).
#
# Cost: the node reports usage in tokens only (models.toml / ops.toml carry no prices: micro_usd is 0), so the USD
# figures are ESTIMATES at the assumed rates below; change them with P32_USD_IN / P32_USD_OUT (per million tokens),
# P32_USD_IMAGE, P32_USD_TTS, P32_USD_VOICE, P32_USD_DESCRIBE (per call).
# Usage: studio/tools/workflow-p3.2-summary.sh [<runs-dir>]
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
runs="${1:-${root}/artifacts/studio/workflows/P3.2/runs}"
python3 - "${runs}" "${root}/artifacts/studio/workflows/P3.2/summary.json" <<'PY'
import glob, json, os, re, sys
runs, out = sys.argv[1], sys.argv[2]
rate_in = float(os.environ.get("P32_USD_IN", "5"))
rate_out = float(os.environ.get("P32_USD_OUT", "20"))
rate = {"image": float(os.environ.get("P32_USD_IMAGE", "0.04")), "tts": float(os.environ.get("P32_USD_TTS", "0.002")),
        "voice": float(os.environ.get("P32_USD_VOICE", "0.01")), "describe": float(os.environ.get("P32_USD_DESCRIBE", "0.02"))}

def load(path):
    try:
        return json.load(open(path))
    except (OSError, ValueError):
        return None

summary = {"assumedRates": {"inputPerM": rate_in, "outputPerM": rate_out, **rate}, "runs": [], "totals": {}}
tot = {"input_tokens": 0, "output_tokens": 0, "tasks": 0, "images": 0, "tts": 0, "voice": 0, "describe": 0}
seen_tasks = set()
for run in sorted(glob.glob(os.path.join(runs, "*"))):
    if not os.path.isdir(run):
        continue
    info = load(os.path.join(run, "run.json")) or {}
    usage = load(os.path.join(run, "usage.json")) or {"tasks": []}
    tasks = {}
    for t in usage.get("tasks", []):
        tasks[t["task"]] = t
    for name in glob.glob(os.path.join(run, "etos", "t*.json")):
        doc = load(name) or {}
        task = os.path.basename(name)[:-5]
        used = ((doc.get("budget") or {}).get("task") or {}).get("used") or {}
        show = (doc.get("show") or {}).get("task") or {}
        tasks.setdefault(task, {"task": task, "used": used, "status": show.get("status"), "worker": show.get("worker")})
    for task, t in tasks.items():
        if task in seen_tasks:
            continue
        seen_tasks.add(task)
        tot["tasks"] += 1
        tot["input_tokens"] += int((t.get("used") or {}).get("input_tokens") or 0)
        tot["output_tokens"] += int((t.get("used") or {}).get("output_tokens") or 0)
    lag_by_tag, last_state = {}, {}
    timeline = os.path.join(run, "timeline.jsonl")
    if os.path.exists(timeline):
        for line in open(timeline):
            try:
                d = json.loads(line)
            except ValueError:
                continue
            if d.get("kind") != "request" or not d.get("tag") or not d.get("updatedAt"):
                continue
            key = "%s/%s" % (d.get("state"), d.get("taskStatus"))
            if last_state.get(d["requestId"]) != key:
                lag_by_tag.setdefault(d["tag"], []).append(d["recvMs"] - d["updatedAt"])
            last_state[d["requestId"]] = key
    entry = {"run": os.path.basename(run), "workflow": info.get("workflow"), "revision": info.get("revision"), "exit": info.get("editorExit", info.get("exit")),
             "tasks": sorted(tasks), "requests": []}
    for timings in sorted(glob.glob(os.path.join(run, "*", "timings.json"))):
        tag = os.path.basename(os.path.dirname(timings))
        t = load(timings) or {}
        outcome = load(os.path.join(os.path.dirname(timings), "outcome.json")) or {}
        ids = [k for k in t if k.startswith("cs_")]
        first = t.get(ids[0], {}) if ids else {}
        lags = sorted(lag_by_tag.get(tag, []))
        t["visibleLagMs"] = {"n": len(lags), "max": lags[-1] if lags else None,
                             "p95": lags[max(0, -(-len(lags) * 95 // 100) - 1)] if lags else None}
        summary.setdefault("allLags", []).extend(lags)
        entry["requests"].append({
            "tag": tag, "result": outcome.get("result"), "tasks": outcome.get("tasks"),
            "submitToAcceptedMs": first.get("submitToAcceptedMs"), "submitToFirstEventMs": first.get("submitToFirstEventMs"),
            "submitToCandidateMs": t.get("submitToCandidateMs"), "candidateImportMs": t.get("candidateImportMs"),
            "previewMs": t.get("previewMs"), "applyMs": t.get("applyMs"), "undoMs": t.get("undoMs"), "cancelAckMs": t.get("cancelAckMs"),
            "visibleLagP95Ms": (t.get("visibleLagMs") or {}).get("p95"), "visibleLagMaxMs": (t.get("visibleLagMs") or {}).get("max"),
            "visibleLagN": (t.get("visibleLagMs") or {}).get("n"),
        })
    text = ""
    for log in glob.glob(os.path.join(run, "run-log.jsonl")):
        text = open(log).read()
    for gen in glob.glob(os.path.join(run, "*", "generate.json")):
        g = load(gen) or {}
        if g.get("providerSha256") or (g.get("refusal") is None and g.get("ok")):
            tot["images"] += 1
    for tts in glob.glob(os.path.join(run, "*", "tts.json")):
        if (load(tts) or {}).get("sha256"):
            tot["tts"] += 1
    spoken = load(os.path.join(run, "voice", "spoken-prompts.json")) or {}
    tot["tts"] += sum(1 for v in spoken.values() if isinstance(v, dict) and v.get("ok"))
    tot["voice"] += len(glob.glob(os.path.join(run, "voice", "*-transcript.json")))
    for d in glob.glob(os.path.join(run, "*", "describe.json")):
        if (load(d) or {}).get("ok"):
            tot["describe"] += 1
    for h in glob.glob(os.path.join(run, "headless-h2-media.json")):
        m = load(h) or {}
        tot["images"] += sum(1 for k in ("image", "budget") if (m.get(k) or {}).get("providerSha256") or (m.get(k) or {}).get("sha256"))
        tot["tts"] += 1 if (m.get("tts") or {}).get("providerSha256") else 0
    summary["runs"].append(entry)
usd = tot["input_tokens"] / 1e6 * rate_in + tot["output_tokens"] / 1e6 * rate_out + sum(tot[{"image": "images"}.get(k, k)] * rate[k] for k in rate)
all_lags = sorted(summary.pop("allLags", []))
summary["totals"] = {**tot, "estimatedUsd": round(usd, 2),
                     "stateVisibleLagMs": {"n": len(all_lags), "p95": all_lags[max(0, -(-len(all_lags) * 95 // 100) - 1)] if all_lags else None,
                                           "max": all_lags[-1] if all_lags else None,
                                           "note": "Unity receipt minus companion updatedAt, state transitions only (B-AGENT-UX <= 1 s)"}}
json.dump(summary, open(out, "w"), indent=2)
print("| run | workflow | tag | result | tasks | submit->accepted ms | ->first event ms | ->candidate ms | preview ms | apply ms | undo ms | cancel ack ms | visible lag p95/max ms (n) |")
print("|---|---|---|---|---|---|---|---|---|---|---|---|---|")
for r in summary["runs"]:
    for q in r["requests"] or [{"tag": "-", "result": "-"}]:
        print("| %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s/%s (%s) |" % (
            r["run"], r["workflow"], q.get("tag"), q.get("result"), ",".join(q.get("tasks") or []), q.get("submitToAcceptedMs"), q.get("submitToFirstEventMs"),
            q.get("submitToCandidateMs"), q.get("previewMs"), q.get("applyMs"), q.get("undoMs"), q.get("cancelAckMs"), q.get("visibleLagP95Ms"), q.get("visibleLagMaxMs"), q.get("visibleLagN")))
print()
print("Totals: %(tasks)d task(s), %(input_tokens)d input + %(output_tokens)d output tokens, %(images)d image(s), %(tts)d tts, %(voice)d voice session(s), %(describe)d describe; estimated USD %(estimatedUsd)s" % summary["totals"])
PY
