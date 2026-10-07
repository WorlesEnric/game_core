#!/usr/bin/env python3
"""R10_A_01 cold/warm pressure plate and R10_A_02 literal lever, on scratch :1.

Build the current companion and R8_B Submit~/LeverSubmit.csproj (Release) first.
All source/review Editors and companion sandbox Editors use unity-batch's lease.
The runner never reads pairing/key contents or contacts the installed node. It
reuses R8_B's real etos scratch installation, provisioner and graphical driver;
R6_E supplies its graphical wrapper and public admission-state retention helper.
A failed run is durable evidence, not a license to reset its cache or journal.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import shutil
import sqlite3
import sys
import time
import xml.etree.ElementTree as ET

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
REPO = HERE.parents[5]
PROJECT = REPO / 'games/hollowmere'
TOOLS = REPO / 'artifacts/studio/verification/TOOLS'
OUT = REPO / 'artifacts/studio/verification'
STEPS = ['scan', 'checkers', 'dotnet', 'unity-editmode', 'playmode-smoke', 'determinism', 'budget']
SHARED_CACHE = Path.home() / '.cache/gamecore-studio/stage/_warm/22421f6df7cfc2cc396043796f1a4d9a966cbcd1e8c4a3287f60eb3816fa1c22'
SUBMIT = HERE.parent / 'R8_B/Lever/Submit~/LeverSubmit.csproj'


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


r8 = module('r10_r8_walkthrough', HERE.parent / 'R8_B/Lever/walkthrough.py')
r6 = module('r10_r6_evidence', HERE.parent / 'R6_E/run-live.py')


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def load(path):
    return json.loads(path.read_text())


def utc():
    return datetime.now(timezone.utc).isoformat()


def timed(path, action):
    started = utc()
    start = time.monotonic_ns()
    ok = False
    try:
        value = action()
        ok = True
        return value
    finally:
        elapsed = time.monotonic_ns() - start
        r8.save(path, dict(startedUtc=started, finishedUtc=utc(), elapsedNs=elapsed,
                           milliseconds=elapsed / 1_000_000, completed=ok))


def public_copy(source, target):
    require(source.is_file() and not source.is_symlink(), 'Missing regular public artifact: ' + str(source))
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, target)


def retain_slot(owner_root, slot, evidence):
    require(isinstance(slot, str) and re.fullmatch(r'[a-zA-Z0-9_-]+', slot), 'Invalid owned stage slot')
    source = owner_root / slot
    # These paths are produced by Stage. Never traverse node state, CAS, HOME,
    # signing authority, user settings, slot project or any private installation.
    for relative in ('stage.json', 'world-snapshot/catalog.json', 'world-snapshot/source.json'):
        path = source / relative
        if path.is_file():
            public_copy(path, evidence / 'service' / relative)
    out = source / 'out'
    if out.is_dir():
        for path in sorted(out.rglob('*')):
            if path.is_file() and not path.is_symlink() and path.suffix in ('.json', '.xml', '.log', '.txt', '.trx'):
                require(not any(parent.is_symlink() for parent in path.parents), 'Linked stage output refused')
                public_copy(path, evidence / 'service/slot-out' / path.relative_to(out))


def stage(config, owner_root, evidence):
    log = evidence / 'runner.log'
    # The Edit-mode exporter calls PrepareStageWorldSnapshot before request and
    # tool-catalog export. The source Editor exits BEFORE the literal client POST.
    timed(evidence / 'export-wall.json', lambda: r8.launch(config, 'Stage', log))
    config = load(evidence / 'config.json')
    require((evidence / 'source-world-snapshot.json').is_file(), 'Fresh source snapshot export missing')
    if config['workerEditUndo']:
        freshness = load(evidence / 'worker-edit-undo/result.json')
        require(freshness['origin'] == 'Agent' and freshness['snapshotAfterNormalUndo']
                and freshness['staleCommittedBakeObserved'] and freshness['editedSnapshotRefused']
                and freshness['bakedFilesUnchanged']
                and freshness['beforeSha256'] == freshness['restoredSha256']
                and freshness['beforeFingerprint'] == freshness['restoredFingerprint']
                and freshness['appliedFingerprint'] != freshness['beforeFingerprint'],
                'Normal worker edit/apply/Undo freshness prerequisite failed')
    stage_row = None

    def submit_and_wait():
        nonlocal config, stage_row
        r8.command([Path.home() / '.dotnet/dotnet', 'run', '--no-build', '--no-restore',
                    '--configuration', 'Release', '--project', SUBMIT, '--', evidence / 'config.json'], log)
        config = load(evidence / 'config.json')
        deadline = time.monotonic() + 2100
        database = Path(config['state']) / 'ledger.db'
        while True:
            # Read only the job we submitted in this newly created scratch node.
            with sqlite3.connect(database.as_uri() + '?mode=ro', uri=True) as db:
                row = db.execute('SELECT state, slot, verdict, created_at, updated_at FROM stage_jobs WHERE job_id=?',
                                 (config['jobId'],)).fetchone()
            if row:
                stage_row = dict(jobId=config['jobId'], state=row[0], slot=row[1],
                                 verdict=json.loads(row[2]) if row[2] else None,
                                 createdAtMs=row[3], updatedAtMs=row[4])
                r8.save(evidence / 'stage-job.json', stage_row)
                if row[0] in ('done', 'failed', 'cancelled'):
                    if row[2]:
                        # Preserve the issued JSON verbatim; this is not local authority.
                        (evidence / 'signed-record-original.json').write_text(row[2])
                    require(row[0] == 'done' and stage_row['verdict'] and stage_row['verdict'].get('pass') is True,
                            'Stage failed; retain the job/slot and use normal recovery, never reset the cache')
                    return
            require(time.monotonic() < deadline, 'Stage deadline; no acceptance claimed')
            time.sleep(1)

    try:
        timed(evidence / 'stage-wall.json', submit_and_wait)
    finally:
        if stage_row and stage_row['slot']:
            retain_slot(owner_root, stage_row['slot'], evidence)
    verdict = stage_row['verdict']
    require(verdict.get('confinement') == 'docker' and verdict.get('forbiddenHits') == [], 'Stage confinement/scan gates failed')
    require([step['id'] for step in verdict['steps']] == STEPS
            and all(step['status'] == 'pass' for step in verdict['steps']), 'All seven real Stage checks must pass')
    if config['workerEditUndo']:
        require(verdict['catalogDelta']['world'] == freshness['restoredFingerprint'],
                'Signed Stage world does not match the actual source after normal authored Undo')
    require(verdict.get('coldCache') is config['expectedCold'], 'Observed cold/warm cache state differs from the named regression')
    require(0 <= verdict['durationMs'] <= (1_800_000 if config['expectedCold'] else 360_000), 'Stage exceeded its existing cold/warm bound')
    for name in ('editmode.xml', 'playmode.xml'):
        path = evidence / 'service/slot-out' / name
        cases = ET.parse(path).getroot().findall('.//test-case')
        require(cases and all(case.get('result') == 'Passed' for case in cases), name + ' has missing or nonpassing cases')
    return config


def verify_live(config, evidence):
    admitted = load(evidence / 'live-admit.json')
    undone = load(evidence / 'live-undo.json')
    progress = load(evidence / 'live-progress.json')
    refresh = load(evidence / 'automatic-refresh.json')
    panel = load(evidence / 'panel-admit.json')
    signed = load(evidence / 'signed-verdict.json')
    require(signed == load(evidence / 'signed-record-original.json'), 'Panel did not authenticate the exact issued record')
    require(admitted['restoredRootCatalogHash'] == signed['catalogDelta']['predicted'],
            'The actual restored game root differs from the signed predicted catalog')
    require(admitted['outcome'] == 'Admitted' and undone['outcome'] == 'Undone' and progress['phase'] == 'complete',
            'Admission/normal History undo did not complete')
    require(0 <= admitted['milliseconds'] <= 90000 and 0 <= admitted['admissionWallMs'] <= 90000,
            'Admission exceeded the unchanged 90s gate')
    require(0 <= undone['undoWallMs'] <= 180000, 'Undo exceeded the unchanged 180s gate')
    require(admitted['live'] == admitted['predicted'] and admitted['confinement'] == 'docker', 'Signed catalog mismatch')
    require(admitted['smokeSteps'] == 120 and admitted['smokeTransitions'] == ['Pending', 'Passed']
            and admitted['checkpointRoundTripsEqual'] and len(admitted['restoredCheckpointSlotHash']) == 64
            and admitted['coins'] == 9, 'Restored nine-coin/tri-state smoke witness failed')
    require(undone['before'] == undone['live'] == admitted['before'], 'Undo did not restore the old catalog')
    require(panel['control'] == 'candidate-admit' and panel['event'] == 'NavigationSubmitEvent'
            and panel['enabled'] and panel['captureAndStop'] and panel['coins'] == 9, 'Actual creator Admit event missing')
    for witness in (panel, admitted):
        require(witness['graphics']['isPlaying'] and not witness['graphics']['isBatchMode']
                and witness['graphics']['display'] == ':1', 'Real graphical Play witness missing')
    require(refresh['currentRuntimeBound'] and refresh['companionVerified']
            and refresh['domain'] == admitted['resumedDomain'] and refresh['domain'] != refresh['admissionDomain'],
            'Automatic authenticated final-domain refresh missing')
    require(any(row['phase'] == 'admission' and row.get('packageInstalled')
                and row.get('pendingPhase') in ('compile', 'reload') for row in progress['reloads']),
            'New package registration and genuine compile/domain reload were not observed')
    require((evidence / 'pending-transitions.jsonl').is_file(), 'Pending admission observations missing')
    for name in ('panel-admit-enabled.png', 'admitted-play.png', 'config-upm.log', 'stage-upm.log'):
        require((evidence / name).is_file(), name + ' missing')
    require(load(evidence / 'undo-request.json')['ok'], 'Normal History Undo was not requested')
    if config['mechanism'] == 'lever':
        interactive = load(evidence / 'interactive-lever.json')
        require(admitted['mechanismStates'] == interactive['states'] == [0, 1, 0]
                and interactive['normalFrames'] and interactive['control'] == 'lever-toggle'
                and interactive['coins'] == 9, 'Real lever control did not commit off/on/off')
        for state in ('off-before', 'on', 'off-after'):
            for view in ('play', 'world'):
                require((evidence / ('lever-' + state + '-' + view + '.png')).is_file(), 'Lever graphical witness missing')
    return dict(caseName=config['caseName'], status='PASS', changeSetId=config['request']['changeSetId'],
                jobId=config['jobId'], sourceRevision=config['request']['sourceRevision'],
                coldCache=signed['coldCache'], signedStageMs=signed['durationMs'],
                stageWallMs=load(evidence / 'stage-wall.json')['milliseconds'],
                admissionWallMs=admitted['admissionWallMs'], undoWallMs=undone['undoWallMs'],
                admissionBudgetMs=90000, installedNodeUsed=False)


def manifest(evidence):
    # This directory contains only explicit public evidence and authored fixtures.
    files = []
    for path in sorted(evidence.rglob('*')):
        if path.is_file() and not path.is_symlink() and path.name != 'manifest.json':
            data = path.read_bytes()
            files.append(dict(path=str(path.relative_to(evidence)), bytes=len(data), sha256=hashlib.sha256(data).hexdigest()))
    r8.save(evidence / 'manifest.json', dict(files=files, authority='real scratch etos node and locally built companion'))


def execute(base, scratch_evidence, node, scratch_log, cases):
    owner = hashlib.sha256(json.dumps(['gamecore-unity', base['projectId']], separators=(',', ':')).encode()).hexdigest()
    owner_root = node.parent / 'stage' / owner
    for case in cases:
        evidence = case['evidence']
        config = dict(base, evidence=str(evidence), candidate=str(evidence / 'candidate'),
                      mechanism=case['mechanism'], caseName=case['name'], expectedCold=case['cold'],
                      workerEditUndo=case['mechanism'] == 'pressure-plate')
        r8.save(evidence / 'config.json', config)
        public_copy(scratch_log, evidence / 'scratch-setup.log')
        r8.save(evidence / 'scratch-installation.json', dict(installedNodeUsed=False,
            companionSha256=base['companionSha256'], nodeUrl=base['nodeUrl'], ownerCacheRoot=str(owner_root)))
        try:
            config = stage(config, owner_root, evidence)
            try:
                timed(evidence / 'graphical-wall.json', lambda: r8.launch(config, 'Config', evidence / 'runner.log'))
            finally:
                r6.snapshot_owned_state(config, evidence)
            result = verify_live(config, evidence)
            with sqlite3.connect((Path(base['state']) / 'ledger.db').as_uri() + '?mode=ro', uri=True) as db:
                counts = {table: db.execute('SELECT count(*) FROM ' + table).fetchone()[0]
                          for table in ('attempts', 'media_charges', 'voice_sessions')}
            r8.save(evidence / 'no-paid-ops.json', counts)
            require(not any(counts.values()), 'Unexpected worker/media/voice activity')
            r8.save(evidence / 'result.json', result)
            print(case['name'], 'PASS', evidence, flush=True)
        except Exception as error:
            # Child errors never include credential contents; only fixed paths and
            # the production redacted Unity/client diagnostics appear in run logs.
            r8.save(evidence / 'result.json', dict(caseName=case['name'], status='FAIL', error=str(error),
                                                installedNodeUsed=False, observedUtc=utc()))
            raise
        finally:
            manifest(evidence)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('work', type=Path, help='fresh private scratch installation outside the checkout; retained on failure')
    parser.add_argument('--lane', choices=('all', 'mechanism', 'lever'), default='all')
    parser.add_argument('--label', default='primary', help='fresh evidence subdirectory label, never an automatic retry')
    parser.add_argument('--companion', type=Path, default=REPO / 'studio/agent/target/release/gamecore-studio')
    parser.add_argument('--unity-library', type=Path, default=Path.home() / '.cache/gamecore-studio/stage/_warm/Library')
    parser.add_argument('--upm-from', type=Path, default=SHARED_CACHE / 'upm')
    args = parser.parse_args()
    require(re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9_-]*', args.label), 'Invalid evidence label')
    work = args.work.resolve()
    require(not work.exists() and not work.is_relative_to(REPO), 'Scratch work must be fresh and outside the checkout')
    require(not (PROJECT / 'UserSettings/GameCoreStudio.json').exists(), 'Existing project ETOS settings are never read or overwritten')
    for path in (args.companion.resolve(), r8.BIN / 'etos', r8.BIN / 'etosd',
                 SUBMIT.parent / 'bin/Release/net8.0/LeverSubmit.dll'):
        require(path.is_file(), 'Build/install prerequisite is missing: ' + str(path))
    require(args.companion.resolve().is_relative_to(REPO), 'Use the companion built from this checkout, not another installation')
    for path in (args.unity_library.resolve(), args.upm_from.resolve()):
        require(path.is_relative_to(REPO) or path.is_relative_to(Path.home() / '.cache'),
                'Seeds must come from this checkout or the user shared cache, never a sibling clone')
    cases = []
    if args.lane in ('all', 'mechanism'):
        for temperature in ('cold', 'warm'):
            cases.append(dict(name='R10_A_01_' + temperature, mechanism='pressure-plate', cold=temperature == 'cold',
                              evidence=OUT / 'W-MECH-01/r10-a' / args.label / temperature))
    if args.lane in ('all', 'lever'):
        cases.append(dict(name='R10_A_02', mechanism='lever', cold=args.lane == 'lever',
                          evidence=OUT / 'W-DOC-02/r10-a' / args.label / 'lever'))
    require(all(not case['evidence'].exists() for case in cases), 'Evidence directories must be fresh; previous attempts remain untouched')
    for case in cases:
        evidence = case['evidence']
        evidence.mkdir(parents=True)
        log = evidence / 'fixture-preparation.log'
        source = REPO / 'samples/mechanisms/pressure-plate/candidate'
        if case['mechanism'] == 'lever':
            source = evidence / 'literal-guide-candidate'
            r8.command([sys.executable, PROJECT / 'Assets/Hollowmere/Mechanisms/Lever~/make-candidate.py',
                        '--output', source], log)
        # Reuse the retained tool: only change-set identity changes, never package,
        # proposal, operations or passing claims. This leaves the receipt/hashes.
        r8.command([sys.executable, TOOLS / 'instantiate-p42d.py', source, evidence / 'candidate'], log)
    r8.walkthrough(work, cases[0]['evidence'] / 'candidate', args.companion,
                   args.unity_library, args.upm_from,
                   run=lambda *values: execute(*values, cases))


if __name__ == '__main__':
    main()
