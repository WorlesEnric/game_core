#!/usr/bin/env python3
"""R8-B real scratch-node media qualification. Never opens a credential file.

prepare creates only external disposable installation state and a public run config.
serve uses systemd's existing EnvironmentFile credential resolver, starts no installed
service, and acknowledges read-only companion ledger checkpoints. launch must be run
only after Main grants the serial Unity slot. No paid call is made by this script.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import socket
import sqlite3
import subprocess
import time
import tomllib

REPO = Path(__file__).resolve().parents[7]
OWN = Path(__file__).resolve().parent
EVIDENCE = REPO / 'artifacts/studio/verification/W-ETOS-07/r8-b-media'
BIN = Path.home() / '.local/opt/etos/bin'
COMPANION = Path('/tmp/r8-b-cargo-target/debug/gamecore-studio')
ROOT = Path('/tmp/r8-b-media-20261007')
UNIT = 'gamecore-r8-b-media-20261007'


def utc():
    return datetime.now(timezone.utc).isoformat()


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + '.tmp')
    temporary.write_text(json.dumps(value, indent=2, sort_keys=True) + '\n')
    temporary.replace(path)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def command(args, env=None):
    # Commands used here never print credentials: app pair MUST carry --out.
    result = subprocess.run([str(a) for a in args], env=env, capture_output=True, text=True)
    with (EVIDENCE / 'setup-commands.log').open('a') as log:
        log.write('$ ' + ' '.join(map(str, args)) + '\nexit=' + str(result.returncode) + '\n')
    if result.returncode:
        raise RuntimeError('Scratch command failed with exit ' + str(result.returncode) + ': ' + ' '.join(map(str, args)))
    return result


def prepare():
    if ROOT.exists() or (EVIDENCE / 'config.json').exists():
        raise RuntimeError('Existing scratch run; refuse replacement or new paid reservation')
    ROOT.mkdir(mode=0o700)
    EVIDENCE.mkdir(parents=True, exist_ok=True)
    node = ROOT / 'node'
    command([BIN / 'etosd', 'init', '--profile', 'local', '--root', node, '--name', 'r8-b-media', '--owner', 'r8-b'])
    # Select unused loopback ports; daemon readiness checks detect any intervening race.
    ports = []
    for _ in range(3):
        with socket.socket() as sock:
            sock.bind(('127.0.0.1', 0))
            ports.append(sock.getsockname()[1])
    node_config = ('models = "models.toml"\nops = "ops.toml"\nshare_roots = []\n'
        '[node]\nname = "r8-b-media"\nowner = "r8-b"\nlocal = true\n'
        '[gateway]\nlisten = "127.0.0.1:' + str(ports[0]) + '"\n'
        '[containers]\nruntime = "none"\n'
        '[broker]\nlisten = "127.0.0.1:' + str(ports[2]) + '"\nauthority = "127.0.0.1:' + str(ports[2]) + '"\n'
        '[api]\nlisten = "127.0.0.1:' + str(ports[1]) + '"\nagent_budget_usd = 0.20\n'
        '[ssh]\nweb = false\n')
    (node / 'etos.toml').write_text(node_config)
    (node / 'models.toml').write_text('# No paid worker, describe or TTS model configured.\n')
    template = (REPO / 'studio/etos/ops.toml.tmpl').read_text()
    block = next('[[providers]]' + b for b in template.split('[[providers]]')[1:]
                 if tomllib.loads('[[providers]]' + b)['providers'][0]['family'] == 'image')
    # A tighter output limit does not change provider or tariff authority.
    block = block.replace('max_outputs = 4', 'max_outputs = 1')
    (node / 'ops.toml').write_text(block)
    provider = tomllib.loads(block)['providers'][0]
    assert provider['cost']['per_unit'] == 0.20
    assert provider['credential'] == 'env:ECHO_API_KEY'
    agent = ROOT / 'agent'
    (agent / 'bin').mkdir(parents=True)
    # No workers and no worker/task/realtime grants. Only real media/proxy capabilities.
    (agent / 'agent.toml').write_text('agent = "gamecore-studio"\nversion = "0.1.0"\n'
        'label = "R8-B scratch companion"\nsdk = ">=1, <2"\n'
        'grants = ["logger", "query", "files", "ops", "proxy", "services"]\n'
        '[process]\ncommand = "bin/gamecore-studio"\nrestart = "never"\n'
        'env = { RUST_LOG = "warn" }\n')
    state = node / 'agents/gamecore-studio/state'
    state.mkdir(parents=True)
    tariff = ('ops_timeout_secs = 300\nops_max_cost_usd = 0.20\n'
        '[[ops_prices]]\nop = "image"\nprovider = "echo-images"\nmodel = "gpt-image-2"\n'
        'source = "operator"\nunit = "image"\nper_unit = 0.20\n'
        'note = "Owner-declared total per-image estimate (2026-10-06), basis: OpenAI gpt-image published list prices; Echo publishes no tariff"\n')
    (state / 'config.toml').write_text(tariff)
    project = REPO / 'games/hollowmere'
    import re
    guid = re.search(r'productGUID: ([a-fA-F0-9]{32})', (project / 'ProjectSettings/ProjectSettings.asset').read_text())[1]
    project_id = hashlib.sha256((guid.lower() + '\n' + str(project)).encode()).hexdigest()
    config = dict(root=str(ROOT), node=str(node), nodeUrl='http://127.0.0.1:' + str(ports[1]),
        agent=str(agent), state=str(state), pairing=str(ROOT / 'scratch-pairing.json'),
        evidence=str(EVIDENCE), projectId=project_id, project=str(project), preparedUtc=utc(),
        maximumImageCalls=1, maximumTtsCalls=0, maximumDescribeCalls=0, maxCostUsd=0.20,
        totalCapUsd=0.30, unit=UNIT, installedNodeUsed=False, localCompanion=str(COMPANION))
    save(EVIDENCE / 'config.json', config)
    save(EVIDENCE / 'isolation.json', dict(config, provider=provider,
        credentialResolution='systemd EnvironmentFile; harness never opens or prints provider/app secret contents',
        configs={'node': node_config, 'ops': block, 'companion': tariff},
        limits='One immutable dispatch reservation; gateway OpReplayWindow=0; image-only node; max_outputs=1; agent total budget USD0.20; no worker definitions'))
    print('R8_B_MEDIA_PREPARED ' + str(EVIDENCE / 'config.json'), flush=True)


def snapshot(config, label):
    with sqlite3.connect(Path(config['state']).joinpath('ledger.db').as_uri() + '?mode=ro', uri=True) as db:
        charges = [{'key': key, 'owner': owner, 'charge': json.loads(charge)}
                   for key, owner, charge in db.execute('SELECT key,owner,charge FROM media_charges ORDER BY key')]
        artifacts = [dict(sha256=digest, mediaType=media, bytes=count, producer=json.loads(producer))
                     for digest, media, count, producer in db.execute('SELECT sha256,media_type,bytes,producer FROM artifacts ORDER BY sha256')]
        requests = db.execute('SELECT count(*) FROM requests').fetchone()[0]
    result = dict(label=label, observedUtc=utc(), charges=charges, artifacts=artifacts, workerRequests=requests)
    if label == 'before':
        assert charges == [] and artifacts == [] and requests == 0, 'Scratch ledger not empty'
    else:
        assert len(charges) == 1 and requests == 0, 'Expected exactly one direct media charge, zero workers'
        charge = charges[0]['charge']
        assert charge['costUsd'] == 0.20, 'Binding cost differs from authorized tariff'
        assert len(artifacts) == 1, 'Expected exactly one generated artifact'
        producer = artifacts[0]['producer']
        assert producer['key'] == charges[0]['key'] and producer['charge'] == charge
        assert producer['op'] == 'generate.image' and producer['etosRef']
        assert charge['quantity'] == 1 and charge['tariff']['unit'] == 'image'
        if label != 'generated':
            baseline = json.loads((EVIDENCE / 'ledger-generated.json').read_text())
            assert charges == baseline['charges'] and artifacts == baseline['artifacts'], 'Usage or producer changed after generation'
    result['status'] = 'PASS'
    save(EVIDENCE / ('ledger-' + label + '.json'), result)


def serve():
    config = json.loads((EVIDENCE / 'config.json').read_text())
    if not COMPANION.is_file():
        raise RuntimeError('Fresh local companion build absent')
    shutil.copy2(COMPANION, Path(config['agent']) / 'bin/gamecore-studio')
    save(EVIDENCE / 'executable.json', dict(path=str(COMPANION), sha256=sha(COMPANION), copiedSha256=sha(Path(config['agent']) / 'bin/gamecore-studio'), utc=utc()))
    node = Path(config['node'])
    env = dict(os.environ, ETOS_ROOT=str(node))
    env.pop('ETOS_SOCKET', None)
    process = subprocess.Popen(['systemd-run', '--user', '--unit=' + UNIT, '--wait', '--pipe', '--collect',
        '--property=EnvironmentFile=' + str(Path.home() / '.config/gamecore-studio/providers.env'),
        '--property=UnsetEnvironment=http_proxy https_proxy HTTP_PROXY HTTPS_PROXY all_proxy ALL_PROXY no_proxy NO_PROXY',
        '--setenv=ETOS_ROOT=' + str(node), '--setenv=RUST_LOG=warn', str(BIN / 'etosd'), 'run', '--root', str(node)],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    signal.signal(signal.SIGTERM, lambda *_: (_ for _ in ()).throw(KeyboardInterrupt()))
    signal.signal(signal.SIGINT, lambda *_: (_ for _ in ()).throw(KeyboardInterrupt()))
    try:
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            if process.poll() is not None:
                raise RuntimeError('Scratch daemon exited; systemd return=' + str(process.returncode))
            probe = subprocess.run([str(BIN / 'etos'), 'node', 'status'], env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            if probe.returncode == 0:
                break
            time.sleep(0.25)
        else:
            raise RuntimeError('Scratch node readiness deadline')
        command([BIN / 'etos', 'agent', 'install', config['agent']], env)
        command([BIN / 'etos', 'app', 'install', REPO / 'studio/etos/app'], env)
        command([BIN / 'etos', 'app', 'pair', 'gamecore-unity', '--approve', '--out', config['pairing']], env)
        deadline = time.monotonic() + 40
        while not (Path(config['state']) / 'ledger.db').exists():
            if time.monotonic() > deadline:
                raise RuntimeError('Scratch companion ledger readiness deadline')
            time.sleep(0.25)
        save(EVIDENCE / 'service-ready.json', dict(readyUtc=utc(), nodeUrl=config['nodeUrl'], installedNodeUsed=False, unit=UNIT))
        print('R8_B_MEDIA_READY', flush=True)
        while process.poll() is None:
            checkpoint_path = EVIDENCE / 'checkpoint.json'
            if checkpoint_path.exists():
                checkpoint = json.loads(checkpoint_path.read_text())
                label = checkpoint['label']
                if not (EVIDENCE / ('ledger-' + label + '.json')).exists():
                    try:
                        snapshot(config, label)
                    except Exception as error:
                        save(EVIDENCE / ('ledger-' + label + '.json'), dict(status='FAIL', label=label, reason=str(error), utc=utc()))
            time.sleep(0.2)
    except KeyboardInterrupt:
        pass
    finally:
        # Hub may send both INT and TERM. Once cleanup starts, a second signal must
        # not interrupt stopping the external transient unit.
        signal.signal(signal.SIGTERM, signal.SIG_IGN)
        signal.signal(signal.SIGINT, signal.SIG_IGN)
        subprocess.run(['systemctl', '--user', 'stop', UNIT], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        process.wait(timeout=30)
        save(EVIDENCE / 'service-stopped.json', dict(utc=utc(), unit=UNIT, installedServiceTouched=False))


def launch():
    if os.environ.get('GAMECORE_R8_MEDIA_SLOT_GRANTED') != '1':
        raise RuntimeError('Main must explicitly grant the Unity slot')
    config = json.loads((EVIDENCE / 'config.json').read_text())
    environment = dict(os.environ, DISPLAY=':1', GAMECORE_ETOS_AUTOSTART='0',
        GAMECORE_R8_MEDIA_CONFIG=str(EVIDENCE / 'config.json'),
        GAMECORE_R8_MEDIA_PAIRING_PATH=config['pairing'],
        UNITY=str(OWN / 'graphical-unity.sh'))
    command_line = ['bash', str(REPO / 'studio/tools/unity-batch.sh'), '--project', config['project'],
        '--log-dir', str(EVIDENCE / 'logs'), '--label', 'r8-b-media', '--timeout', '1500', '--attempts', '1',
        '--', '-executeMethod', 'Hollowmere.R8_B.MediaQualification.Run']
    with (EVIDENCE / 'unity-command.log').open('a') as log:
        log.write('$ DISPLAY=:1 ' + ' '.join(command_line) + '\n')
        log.flush()
        result = subprocess.run(command_line, env=environment, stdout=log, stderr=subprocess.STDOUT)
    print('R8_B_MEDIA_UNITY_EXIT=' + str(result.returncode), flush=True)
    return result.returncode


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['prepare', 'serve', 'launch'])
    args = parser.parse_args()
    if args.action == 'prepare':
        prepare()
    elif args.action == 'serve':
        serve()
    else:
        raise SystemExit(launch())
