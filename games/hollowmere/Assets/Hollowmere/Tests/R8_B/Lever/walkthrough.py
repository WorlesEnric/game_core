#!/usr/bin/env python3
"""Literal W-DOC-02 runner: real private node, signed service stage, creator admission.

Credentials are resolved only by etos and the production Unity client. This runner
never reads credential files. No providers, models or workers are configured.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import socket
import sqlite3
import subprocess
import time

REPO = Path(__file__).resolve().parents[7]
PROJECT = REPO / 'games/hollowmere'
BIN = Path.home() / '.local/opt/etos/bin'


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n')


def command(args, log, env=None):
    with log.open('a') as output:
        output.write('$ ' + ' '.join(map(str, args)) + '\n')
        output.flush()
        result = subprocess.run(list(map(str, args)), cwd=REPO, env=env, stdout=output, stderr=subprocess.STDOUT)
    if result.returncode:
        raise RuntimeError('Command failed (' + str(result.returncode) + '); see ' + str(log))


def launch(config, flag, log):
    env = dict(os.environ, DISPLAY=':1', GAMECORE_R6_E_PAIRING_PATH=config['pairing'],
               UNITY=str(REPO / 'games/hollowmere/Assets/Hollowmere/Tests/R6_E/graphical-unity.sh'))
    command(['bash', REPO / 'studio/tools/unity-batch.sh', '--project', PROJECT,
             '--log-dir', Path(config['evidence']) / 'logs', '--label', 'r8-c-' + flag,
             '--attempts', '1', '--timeout', '1500', '--', '-force-glcore',
             '-executeMethod', 'Hollowmere.R8_B.LeverWalkthrough.Run',
             '-gcR8C' + flag, Path(config['evidence']) / 'config.json'], log, env)


def walkthrough(work, candidate, companion, library, upm):
    work = work.resolve()
    candidate = candidate.resolve()
    companion = companion.resolve()
    if work.is_relative_to(REPO):
        raise RuntimeError('Use a work directory outside the checkout')
    if (PROJECT / 'UserSettings/GameCoreStudio.json').exists():
        raise RuntimeError('Existing ETOS settings are never read or overwritten')
    if not companion.is_file() or not (candidate / 'change-set.json').is_file():
        raise RuntimeError('Build the local companion and author the guide candidate first')
    if work.exists():
        evidence = work / 'evidence'
        config = json.loads((evidence / 'config.json').read_text())
        undo = evidence / 'live-undo.json'
        if not undo.exists():
            undo = evidence / 'recovery-undo.json'
        undone = json.loads(undo.read_text()) if undo.exists() else {}
        if (evidence / 'live-admit.json').exists() and (undone.get('outcome') != 'Undone'
                or undone.get('changeSetId') != config.get('request', {}).get('changeSetId')):
            raise RuntimeError('Admitted runs require normal History recovery before another candidate')
        count = 1
        while (work / ('evidence-attempt-' + str(count))).exists():
            count += 1
        shutil.copytree(evidence, work / ('evidence-attempt-' + str(count)))
        old_id = config.get('request', {}).get('changeSetId')
        new_id = json.loads((candidate / 'change-set.json').read_text())['id']
        if old_id == new_id:
            raise RuntimeError('A corrected candidate needs a fresh change-set id; retain previous job and cache')
        config['candidate'] = str(candidate)
        config.pop('jobId', None)
        config.pop('request', None)
        for name in ('live-progress.json', 'live-failure.txt', 'stage-job.json', 'live-admit.json',
                     'live-undo.json', 'interactive-lever.json', 'automatic-refresh.json', 'last-pending.json',
                     'signed-verdict.json', 'no-paid-ops.json', 'recovery-undo.json'):
            (evidence / name).unlink(missing_ok=True)
        for image in evidence.glob('*.png'):
            image.unlink()
        save(evidence / 'config.json', config)
        run_service(config, evidence, work / 'node', evidence / 'runner.log', install=False)
        return
    work.mkdir(mode=0o700, parents=True)
    evidence = work / 'evidence'
    evidence.mkdir()
    log = evidence / 'runner.log'
    node = work / 'node'
    command([BIN / 'etosd', 'init', '--profile', 'local', '--root', node,
             '--name', 'r8-c-lever', '--owner', 'r8-c'], log)
    ports = []
    for _ in range(3):
        with socket.socket() as sock:
            sock.bind(('127.0.0.1', 0))
            ports.append(sock.getsockname()[1])
    (node / 'etos.toml').write_text('models = "models.toml"\nops = "ops.toml"\nshare_roots = []\n'
        '[node]\nname = "r8-c-lever"\nowner = "r8-c"\nlocal = true\n'
        '[gateway]\nlisten = "127.0.0.1:' + str(ports[0]) + '"\n'
        '[containers]\nruntime = "none"\n'
        '[broker]\nlisten = "127.0.0.1:' + str(ports[2]) + '"\nauthority = "127.0.0.1:' + str(ports[2]) + '"\n'
        '[api]\nlisten = "127.0.0.1:' + str(ports[1]) + '"\nagent_budget_usd = 0.0\n'
        '[ssh]\nweb = false\n')
    (node / 'models.toml').write_text('# No model providers or workers.\n')
    (node / 'ops.toml').write_text('# No paid operations.\n')
    guid = re.search(r'productGUID: ([a-fA-F0-9]{32})', (PROJECT / 'ProjectSettings/ProjectSettings.asset').read_text())[1]
    project_id = hashlib.sha256((guid.lower() + '\n' + str(PROJECT)).encode()).hexdigest()
    state = node / 'agents/gamecore-studio/state'
    state.mkdir(parents=True)
    stage_root = work / 'stage'
    (state / 'config.toml').write_text('ops_max_cost_usd = 0.0\n[stage.projects]\n'
        + json.dumps(project_id) + ' = ' + json.dumps(str(PROJECT)) + '\n')
    agent = work / 'agent'
    (agent / 'bin').mkdir(parents=True)
    shutil.copy2(companion, agent / 'bin/gamecore-studio')
    process_env = {'RUST_LOG': 'warn', 'GAMECORE_STAGE_ROOT': str(stage_root),
                   'GAMECORE_STAGE_REPO': str(REPO), 'GAMECORE_STAGE_UNITY_TIMEOUT_S': '1500'}
    (agent / 'agent.toml').write_text('agent = "gamecore-studio"\nversion = "0.1.0"\n'
        'label = "R8-C scratch stage companion"\nsdk = ">=1, <2"\n'
        'grants = ["logger", "query", "files", "ops", "proxy", "services"]\n'
        '[process]\ncommand = "bin/gamecore-studio"\nrestart = "never"\nenv = { '
        + ', '.join(k + ' = ' + json.dumps(v) for k, v in process_env.items()) + ' }\n')
    owner = json.dumps(['gamecore-unity', project_id], separators=(',', ':'))
    owner_root = stage_root / hashlib.sha256(owner.encode()).hexdigest()
    command(['python3', REPO / 'studio/stage/cache.py', '--stage-root', owner_root,
             '--repo', REPO, '--source-project', PROJECT, '--binary', companion,
             '--offline-from', Path.home() / '.nuget/packages', '--unity-library', library,
             '--upm-from', upm], log)
    config = dict(candidate=str(candidate), evidence=str(evidence), state=str(state),
                  nodeUrl='http://127.0.0.1:' + str(ports[1]), pairing=str(work / 'pairing.json'),
                  projectId=project_id, project=str(PROJECT), installedNodeUsed=False,
                  companionSha256=hashlib.sha256(companion.read_bytes()).hexdigest())
    save(evidence / 'config.json', config)
    run_service(config, evidence, node, log, install=True)


def run_service(config, evidence, node, log, install):
    state = Path(config['state'])
    env = dict(os.environ, ETOS_ROOT=str(node))
    for key in ['ETOS_SOCKET', 'http_proxy', 'https_proxy', 'HTTP_PROXY', 'HTTPS_PROXY', 'ALL_PROXY', 'all_proxy']:
        env.pop(key, None)
    def interrupted(*_):
        raise KeyboardInterrupt()
    signal.signal(signal.SIGTERM, interrupted)
    with open(os.devnull, 'w') as node_log:
        process = subprocess.Popen([str(BIN / 'etosd'), 'run', '--root', str(node)], env=env,
                                   stdout=node_log, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + 60
            while subprocess.run([str(BIN / 'etos'), 'node', 'status'], env=env,
                                 stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode:
                if process.poll() is not None or time.monotonic() > deadline:
                    raise RuntimeError('Scratch node readiness failed')
                time.sleep(0.25)
            if install:
                command([BIN / 'etos', 'agent', 'install', node.parent / 'agent'], log, env)
                command([BIN / 'etos', 'app', 'install', REPO / 'studio/etos/app'], log, env)
                command([BIN / 'etos', 'app', 'pair', 'gamecore-unity', '--approve', '--out', config['pairing']], log, env)
            deadline = time.monotonic() + 60
            while not (state / 'ledger.db').exists():
                if time.monotonic() > deadline:
                    raise RuntimeError('Scratch companion readiness failed')
                time.sleep(0.25)
            print('R8_C_SCRATCH_READY', evidence, flush=True)
            launch(config, 'Stage', log)
            # The source Editor has exited and released its lease before any sandbox probe can start.
            command([Path.home() / '.dotnet/dotnet', 'run', '--project',
                     REPO / 'games/hollowmere/Assets/Hollowmere/Tests/R8_B/Lever/Submit~/LeverSubmit.csproj',
                     '--', evidence / 'config.json'], log)
            config = json.loads((evidence / 'config.json').read_text())
            deadline = time.monotonic() + 2100
            while True:
                with sqlite3.connect((state / 'ledger.db').as_uri() + '?mode=ro', uri=True) as db:
                    row = db.execute('SELECT state, verdict FROM stage_jobs WHERE job_id=?', (config['jobId'],)).fetchone()
                if row and row[0] in ('done', 'failed'):
                    save(evidence / 'stage-job.json', dict(jobId=config['jobId'], state=row[0], verdict=json.loads(row[1]) if row[1] else None))
                    if row[0] != 'done' or not row[1] or json.loads(row[1]).get('pass') is not True:
                        raise RuntimeError('Stage failed; retain stage-job.json and slot outputs')
                    break
                if time.monotonic() > deadline:
                    raise RuntimeError('Stage deadline; no acceptance claimed')
                time.sleep(1)
            launch(config, 'Config', log)
            admitted = json.loads((evidence / 'live-admit.json').read_text())
            undone = json.loads((evidence / 'live-undo.json').read_text())
            if admitted['mechanismStates'] != [0, 1, 0] or admitted['coins'] != 9 or undone['outcome'] != 'Undone':
                raise RuntimeError('Lever/restored checkpoint/undo acceptance failed')
            with sqlite3.connect((state / 'ledger.db').as_uri() + '?mode=ro', uri=True) as db:
                counts = {table: db.execute('SELECT count(*) FROM ' + table).fetchone()[0]
                          for table in ('attempts', 'media_charges', 'voice_sessions')}
            if any(counts.values()):
                raise RuntimeError('Unexpected worker/media/voice activity')
            save(evidence / 'no-paid-ops.json', counts)
            print('R8_C_W_DOC_02_PASS', evidence, flush=True)
        finally:
            signal.signal(signal.SIGTERM, signal.SIG_IGN)
            # Only this child node and its child companion are owned by this runner.
            process.terminate()
            try:
                process.wait(timeout=30)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)
