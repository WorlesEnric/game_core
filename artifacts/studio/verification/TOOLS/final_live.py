#!/usr/bin/env python3
"""Guarded P3.2 replay against the final-main immutable installation."""
import json
from pathlib import Path
import runpy
import subprocess
import sys
import verify as v
import acceptance as a

preflight = runpy.run_path(str(Path(__file__).with_name('preflight-p42b.py')))
ROWS = ('W-AI-01', 'W-AI-02', 'W-AI-03', 'W-AI-04', 'W-AI-05', 'W-AI-06', 'W-AI-07',
        'W-MECH-01', 'W-VOICE-01', 'W-ETOS-06')


def blocked(row, reason, snapshot):
    stamp = v.datetime.datetime.now(v.datetime.timezone.utc).strftime('%Y%m%dT%H%M%S.%fZ')
    folder = v.OUT / row / ('p42b-live-prerequisite-' + stamp)
    folder.mkdir(parents=True)
    (folder / 'preflight.json').write_text(json.dumps(snapshot, indent=2) + '\n')
    (folder / 'command.log').write_text(reason + '\nNo live request sent.\n')
    record = dict(label='P4.2b live prerequisite', status='BLOCKED', note=reason,
                  revision=v.git('rev-parse', 'HEAD'), host=v.platform.node(), started=v.utc(), ended=v.utc(),
                  seconds=0, command='studio/tools/verify-all.sh final-live', evidencePath=str(folder.relative_to(v.ROOT)))
    v.finish(folder, record)
    v.RESULTS.append(record)


def main():
    snapshot = preflight['capture']()
    if snapshot['liveRunPids'] or snapshot['editorPids'] or snapshot['recorderPids']:
        reason = ('Documented live-run/display guard is occupied. Required final-main companion reinstall cannot run; '
                  'installed release ' + snapshot['installedRelease'] + ' is not accepted as the R4 release. '
                  'No paid or voice request, companion restart, stage or admission was attempted.')
        for row in ROWS:
            blocked(row, reason, snapshot)
        return 1
    installed = v.run('INSTALL-P4.2b', 'final-main-release', ['bash', 'artifacts/studio/verification/TOOLS/install-p42b.sh'])
    if installed['status'] != 'PASS':
        for row in ROWS:
            blocked(row, 'Required immutable release activation failed; see INSTALL-P4.2b. No live request sent.', snapshot)
        return 1
    baseline = preflight['ledger']()
    if not baseline['available']:
        for row in ROWS:
            blocked(row, baseline['reason'], preflight['capture']())
        return 1
    state = v.ROOT / 'artifacts/studio/workflows/P4.2b'
    state.mkdir(parents=True, exist_ok=True)
    (state / 'ledger-before.json').write_text(json.dumps(baseline, indent=2) + '\n')
    # The existing R4 probe first asserts hello tariff provenance, then makes exactly one <=$0.001 TTS call.
    v.run('INSTALL-P4.2b', 'r4-hello-and-one-tts',
          [str(Path.home()/'.cargo/bin/cargo'), 'test', '--test', 'real_node', 'r4_one_priced_tts_records_published_tariff', '--', '--ignored', '--exact', '--nocapture'],
          cwd=Path.home()/'wkspace/gc-studio/companion/studio/agent',
          env={'STUDIO_REAL_APP_KEY': str(Path.home()/'.config/gamecore-studio/app-key.json'),
               'STUDIO_REAL_ALLOW_OPS': '1', 'STUDIO_REAL_R4_LIVE': '1',
               'GAMECORE_ETOS_PROJECT_ID': a.identity(), 'R4_EVIDENCE_DIR': '{out}'})
    if v.RESULTS[-1]['status'] != 'PASS':
        return 1
    hello = json.loads((v.ROOT / v.RESULTS[-1]['evidencePath'] / 'hello.json').read_text())
    if not any(t.get('op') == 'image' and t.get('tariff', {}).get('kind') == 'operator' and t['tariff'].get('perUnitUsd') == .20 for t in hello.get('tariffs', [])):
        raise RuntimeError('Installed hello does not report the declared operator image estimate')
    # Reserve upper bounds before each P3.2 driver. Stop before launching work if the ledger cannot account for it.
    reserved = {'image': 0, 'tts': 1, 'describe': 0}
    for workflow, row, cost in [('text2', 'W-AI-02', {}), ('robe2', 'W-AI-01', {'image': 3, 'tts': 1}),
                                ('narrative', 'W-AI-03', {}), ('reopen', 'W-AI-06', {})]:
        current = preflight['ledger']()
        if not current['available'] or current['costUsd'] - baseline['costUsd'] >= 10:
            blocked(row, 'Companion ledger is unavailable or packet USD 10 limit reached; stopped.', preflight['capture']())
            break
        for op, count in cost.items():
            reserved[op] += count
            if reserved[op] > {'image': 6, 'tts': 6, 'describe': 2}[op]:
                raise RuntimeError('packet operation reservation exceeded')
        a.graphical(row, 'p42b-' + workflow,
            ['-executeMethod', 'Hollowmere.P3_2.Workflows.WorkflowRunner.Run'],
            {**a.live_env(), 'GCS_P32_WORKFLOW': workflow, 'GCS_P32_OUT': '{out}/workflow'})
        (state / 'ledger-after.json').write_text(json.dumps(preflight['ledger'](), indent=2) + '\n')
    # Existing wrappers preserve the pressure-plate and negative-semantic fixture bytes, and microphone WAV.
    a.stage()
    v.run('W-VOICE-01', 'p42b-virtual-source', ['bash', 'artifacts/studio/verification/TOOLS/voice-fixture.sh', '{out}'], results='results.xml', editor=True)
    a.reconnect()
    return int(any(r['status'] != 'PASS' for r in v.RESULTS))


if __name__ == '__main__':
    result = main()
    v.summary()
    sys.exit(result)
