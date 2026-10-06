#!/usr/bin/env python3
"""Run the retained B-SELECT dataset twice on a pinned disposable source clone."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[4]


def editors():
    result = []
    for process in Path('/proc').iterdir():
        if not process.name.isdigit():
            continue
        try:
            if (process / 'comm').read_text().strip() != 'Unity':
                continue
            command = (process / 'cmdline').read_bytes()
            if command and b'AssetImportWorker' not in command:
                result.append(int(process.name))
        except OSError:
            pass
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, required=True, help='new evidence directory (never overwritten)')
    parser.add_argument('--library', type=Path, help='warm Library from this packet only')
    args = parser.parse_args()
    out = args.out.resolve()
    out.mkdir(parents=True, exist_ok=False)
    revision = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    clone = out / 'source'
    subprocess.run(['git', 'clone', '--shared', '--no-hardlinks', str(ROOT), str(clone)], check=True)
    subprocess.run(['git', 'checkout', '--detach', revision], cwd=clone, check=True)
    project = clone / 'games/hollowmere'
    if args.library:
        subprocess.run(['cp', '-a', '--reflink=auto', str(args.library.resolve()), str(project / 'Library')], check=True)
    harness = clone / 'artifacts/studio/verification/TOOLS/P42bHarness'
    shutil.copytree(harness, project / 'Assets/P42bHarness')
    # Add phase output to the disposable copy only. Keep the retained workload,
    # identities, Stopwatch totals, sample counts and assertions exactly intact.
    timing = project / 'Assets/P42bHarness/TimingTests.cs'
    original = timing.read_text()
    instrumented = original.replace('double[] picks = new double[100]; double[] marquee = new double[100];',
        'double[] picks = new double[100]; double[] marquee = new double[100]; double[] renderer = new double[100]; double[] resolve = new double[100];')
    instrumented = instrumented.replace('marquee[i] = box.Timings.TotalMs;',
        'marquee[i] = box.Timings.TotalMs; renderer[i] = box.Timings.RendererMs; resolve[i] = box.Timings.ResolveMs;')
    instrumented = instrumented.replace('Write("selection", new JObject',
        'Write("profile", new JObject { ["rendererMs"] = new JArray(renderer), ["resolveMs"] = new JArray(resolve) });\n                Write("selection", new JObject')
    if original == instrumented or 'Write("profile"' not in instrumented:
        raise SystemExit('Retained harness changed; review phase instrumentation before running.')
    timing.write_text(instrumented)
    receipt = {'sourceRevision': revision, 'instrumentedHarnessSha256': hashlib.sha256(timing.read_bytes()).hexdigest(), 'harnessSha256': {
        p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(harness.iterdir()) if p.is_file()}, 'runs': []}
    for number in (1, 2):
        folder = out / ('run' + str(number))
        folder.mkdir()
        results = folder / 'results.xml'
        command = ['bash', str(ROOT / 'studio/tools/unity-batch.sh'), '--project', str(project),
                   '--log-dir', str(folder / 'logs'), '--label', 'core-pick-' + str(number),
                   '--results', str(results), '--', '-runTests', '-testPlatform', 'EditMode', '-testFilter',
                   'P42b.Acceptance.TimingTests.R2_38_B_SELECT_100PicksAnd500CandidateMarquee']
        if number == 1:
            command[-1] += '|GameCore.Studio.Edit.Tests.CorePickRegressionTests.R2_38_CORE_PICK_500CandidatesMedianAcrossEditorFramesBelow50Ms'
        env = {**os.environ, 'GAMECORE_ETOS_AUTOSTART': '0', 'GC_STUDIO_UNITY_SLOTS': '1',
               'GAMECORE_P42_EVIDENCE': str(folder)}
        peak = 0
        with (folder / 'runner.txt').open('w') as log:
            child = subprocess.Popen(command, env=env, stdout=log, stderr=subprocess.STDOUT)
            while child.poll() is None:
                active = editors()
                # Other Editors while the wrapper waits are not part of this run.
                owns_editor = False
                for pid in active:
                    try:
                        owns_editor |= str(project).encode() in Path('/proc', str(pid), 'cmdline').read_bytes()
                    except OSError:
                        pass
                if owns_editor:
                    peak = max(peak, len(active))
                time.sleep(1)
        counts = {}
        if results.exists():
            for case in ET.parse(results).iter('test-case'):
                outcome = case.get('result', 'Unknown')
                counts[outcome] = counts.get(outcome, 0) + 1
        row = {'run': number, 'exitCode': child.returncode, 'xmlCounts': counts, 'peakEditors': peak}
        if (folder / 'selection.json').exists():
            row['selection'] = json.loads((folder / 'selection.json').read_text())
        receipt['runs'].append(row)
        (out / 'receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
        print(json.dumps({k: v for k, v in row.items() if k != 'selection'}), flush=True)
        if child.returncode or counts != {'Passed': 2 if number == 1 else 1} or peak != 1:
            raise SystemExit('Dataset failed or host was not exclusive; retained receipt is not acceptance.')


if __name__ == '__main__':
    main()
