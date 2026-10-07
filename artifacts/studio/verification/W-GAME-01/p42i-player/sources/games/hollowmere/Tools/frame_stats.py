#!/usr/bin/env python3
"""P3.1 frame statistics, verbatim calculation from record_playthrough.sh; no recording or service operations."""
import json
import os
import sys

out = sys.argv[1]
minutes = 11

def percentile(values, q):
    if not values:
        return None
    ordered = sorted(values)
    k = (len(ordered) - 1) * q
    lo = int(k)
    hi = min(lo + 1, len(ordered) - 1)
    return round(ordered[lo] + (ordered[hi] - ordered[lo]) * (k - lo), 3)


rows = []
log_path = os.path.join(out, "frame-log.csv")
if os.path.exists(log_path):
    with open(log_path, encoding="utf-8", errors="replace") as handle:
        for line in handle:
            if line.startswith("#") or line.startswith("frame,") or not line.strip():
                continue
            parts = line.rstrip("\n").split(",", 4)
            if len(parts) < 5:
                continue
            try:
                rows.append((int(parts[0]), float(parts[1]), float(parts[2]), parts[3], parts[4]))
            except ValueError:
                continue

start = None
for i, row in enumerate(rows):
    if "ready" in row[4]:
        start = i
        break
start_rule = "first 'ready' marker"
if start is None:
    start = min(120, len(rows))
    start_rule = "no 'ready' marker: after the first 120 frames"
steady = rows[start:]
transitions = [r for r in steady if any(tag in r[4] for tag in ("region:", "restore", "load"))]
transition_times = [r[1] for r in transitions]


def after_transition(t):
    return any(0.0 <= t - tt <= 1.0 for tt in transition_times)


dts = [r[2] for r in steady]
over100 = [{"frame": r[0], "time_s": r[1], "dt_ms": r[2], "region": r[3]} for r in steady if r[2] > 100.0 and not after_transition(r[1])]
hitches = []
for tr in transitions:
    window = [r[2] for r in steady if 0.0 <= r[1] - tr[1] <= 1.0]
    hitches.append({"frame": tr[0], "time_s": tr[1], "marker": tr[4], "max_dt_ms": round(max(window), 3) if window else None})
duration = round(steady[-1][1] - steady[0][1], 3) if len(steady) > 1 else 0.0
p95 = percentile(dts, 0.95)
regions = {}
for r in steady:
    regions.setdefault(r[3] or "-", []).append(r[2])
checks = {
    "p95_le_16_7ms": p95 is not None and p95 <= 16.7,
    "no_frame_over_100ms_outside_transitions": len(over100) == 0,
    "transition_hitch_le_250ms": all(h["max_dt_ms"] is None or h["max_dt_ms"] <= 250.0 for h in hitches),
    "duration_ge_budget": duration >= minutes * 60 * 0.9,
}
stats = {
    "budget": "B-FRAME: p95 <= 16.7 ms over the run; no frame > 100 ms outside the first second after a region transition; transition hitch <= 250 ms",
    "frameLog": "frame-log.csv",
    "rowsTotal": len(rows),
    "steadyStartRule": start_rule,
    "steadyFrames": len(steady),
    "steadySeconds": duration,
    "dtMs": {"p50": percentile(dts, 0.5), "p95": p95, "p99": percentile(dts, 0.99), "max": round(max(dts), 3) if dts else None,
             "mean": round(sum(dts) / len(dts), 3) if dts else None},
    "fpsMean": round(len(dts) / duration, 2) if duration > 0 else None,
    "framesOver16_7ms": sum(1 for d in dts if d > 16.7),
    "framesOver100msOutsideTransitions": over100[:50],
    "framesOver100msOutsideTransitionsCount": len(over100),
    "transitions": hitches,
    "perRegion": {k: {"frames": len(v), "p95": percentile(v, 0.95), "max": round(max(v), 3)} for k, v in sorted(regions.items())},
    "checks": checks,
    "verdict": "pass" if all(checks.values()) else "fail",
}
with open(os.path.join(out, "frame-stats.json"), "w", encoding="utf-8") as handle:
    json.dump(stats, handle, indent=2)
    handle.write("\n")

print(json.dumps(stats, indent=2))
