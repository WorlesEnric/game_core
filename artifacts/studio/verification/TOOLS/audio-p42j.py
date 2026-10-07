#!/usr/bin/env python3
"""Apply the unchanged audio metric to windows derived from this recording's markers."""
import argparse
import csv
import datetime
import json
from pathlib import Path
import subprocess


def aligned_windows(frames, phase, header_utc):
    markers = {part: float(row['time_s']) for row in frames for part in row['marker'].split('|') if part}
    capture_utc = datetime.datetime.fromisoformat(phase['startedUtc']) + datetime.timedelta(
        seconds=phase['wallSeconds'] - phase['captureWallSeconds'])
    origin = float(frames[0]['time_s']) + (capture_utc - datetime.datetime.fromisoformat(header_utc)).total_seconds()
    village = markers['audio-village-before-marsh'] - origin
    marsh = markers['audio-marsh-after-travel'] - origin
    before = (village + marsh) / 2 - 5
    if before < village + 1 or before + 10 > marsh - 1:
        raise ValueError('Marked pretransition interval cannot contain a guarded ten-second window')
    return {'voiceStart': markers['voice-maren'] - origin - 2, 'beforeStart': before,
            'afterStart': marsh + 3, 'windowDuration': 10,
            'estimatedFrameTimeAtCaptureStart': origin,
            'captureStartUtcEstimate': capture_utc.isoformat(), 'frameLogHeaderUtc': header_utc,
            'voiceMarker': markers['voice-maren'], 'villageMarker': markers['audio-village-before-marsh'],
            'marshTransitionMarker': markers['audio-marsh-after-travel'],
            'method': 'Ten-second windows centered inside the marked pretransition interval and starting three seconds after transition; voice begins two seconds before its marker. Clock origin is estimated from the retained capture stopwatch/UTC receipt and first CSV sample. No audio-dependent window selection; original fixed-window failure remains retained.'}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('lifecycle', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    summary = json.loads((args.lifecycle / 'driver-summary.json').read_text())
    lines = (args.lifecycle / 'save-quit-frames.csv').read_text().splitlines()
    header = next(line.removeprefix('# started ') for line in lines if line.startswith('# started '))
    frames = list(csv.DictReader(line for line in lines if not line.startswith('#')))
    config = aligned_windows(frames, summary['phases'][0], header)
    config['sourceMovie'] = str(args.lifecycle / 'playthrough.mp4')
    config['frameLog'] = str(args.lifecycle / 'save-quit-frames.csv')
    args.output.mkdir(parents=True, exist_ok=True)
    path = args.output / 'alignment.json'
    path.write_text(json.dumps(config, indent=2) + '\n')
    return subprocess.run([str(Path.home() / '.local/bin/node'), str(Path(__file__).with_name('audio-identity-p42j.mjs')),
                           config['sourceMovie'], str(args.output / 'audio-identity.json'), str(path)]).returncode


if __name__ == '__main__':
    raise SystemExit(main())
