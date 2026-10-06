#!/usr/bin/env python3
"""Summarize retained FrameTiming/ProfilerRecorder counters and exported native samples."""
import csv
import json
import statistics
import sys
from collections import defaultdict
from pathlib import Path


def summary(values):
    values = sorted(values)
    if not values:
        return None
    k = (len(values) - 1) * 0.95
    lo = int(k)
    return {"median": round(statistics.median(values), 3),
            "p95": round(values[lo] + (values[min(lo + 1, len(values) - 1)] - values[lo]) * (k - lo), 3),
            "max": round(max(values), 3)}


root = Path(sys.argv[1])
results = {}
for path in sorted(root.rglob('timings.csv')):
    rows = list(csv.DictReader(path.open()))
    start = float(rows[0]['time_s'])
    # Diagnostics only: exclude the menu/new-game initialization to attribute steady village work.
    steady = [r for r in rows if float(r['time_s']) >= start + 12]
    counters = {key: summary([float(r[key]) for r in steady]) for key in rows[0] if key not in ('frame', 'time_s')}
    results[str(path.relative_to(root))] = {'frames': len(rows), 'steadyFrames': len(steady), 'counters': counters}
    native = path.with_name('capture.raw.csv')
    if native.exists():
        totals = defaultdict(float)
        first = []
        frames = set()
        for row in csv.DictReader(native.open()):
            frame = int(row['frame'])
            value = float(row['total_ms'])
            frames.add(frame)
            totals[(row['thread'], row['marker'])] += value
            if frame < 20 and value >= 5:
                first.append(row)
        results[str(path.relative_to(root))]['native'] = {
            'firstFrame': min(frames), 'lastFrame': max(frames),
            'topInclusiveSamples': [{'thread': k[0], 'marker': k[1], 'totalMs': round(v, 3)}
                                    for k, v in sorted(totals.items(), key=lambda x: -x[1])[:60]],
            'startupSamplesOver5ms': first}
output = root / 'profile-summary.json'
output.write_text(json.dumps(results, indent=2) + '\n')
print(output)
