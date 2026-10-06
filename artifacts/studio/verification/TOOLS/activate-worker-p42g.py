#!/usr/bin/env python3
"""P4.2g operator deployment; run only after Main builds the baseline binary.

Default invocation (from the owned checkout):
  python3 artifacts/studio/verification/TOOLS/activate-worker-p42g.py

If the installed manifest still omits the existing USD 0.50 designer budget:
  python3 artifacts/studio/verification/TOOLS/activate-worker-p42g.py \
    --approve-designer-budget-manifest

That flag approves only the documented R6-F manifest reconciliation, not new
worker policy, grants, models, or any etosd operation. An already reconciled
manifest needs no flag. The node must already enforce exactly USD 0.50.
No builds, paid tasks, credential reads, or daemon restarts are performed here.
"""
import argparse
import contextlib
import datetime
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import tomllib

import verify as v

ROOT = Path(__file__).resolve().parents[4]
LIVE = ROOT / '.evidence/live'
BASELINE = '55091b74be95eb6e47fe0e33c01ce2d8f2528779'
STATE = ROOT / 'artifacts/studio/workflows/P4.2g'
BUDGET = {'usd': 0.50}


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def command(argv):
    result = subprocess.run(argv, capture_output=True, text=True)
    # Child commands may print registration details; never retain their raw output.
    require(result.returncode == 0, f'{Path(argv[0]).name} command failed (exit {result.returncode})')
    return result.stdout


def load_installer(path):
    spec = importlib.util.spec_from_file_location('p42g_install_state', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def node_rows(etos, kind):
    rows = json.loads(command([str(etos), '--json', kind, 'list']))
    require(isinstance(rows, list), f'node {kind} inspection did not return a list')
    return rows


def unique(rows, field, value):
    matches = [row for row in rows if row.get(field) == value]
    require(len(matches) == 1, f'expected one registered {value}')
    return matches[0]


def policies(rows, declared):
    result = {}
    for worker in declared:
        row = unique(rows, 'name', worker['name'])
        require(all(key in row for key in ('budget', 'image', 'network')),
                f"incomplete operator policy for {worker['name']}")
        require(row['budget'] == worker.get('budget'),
                f"worker budget differs from approved manifest: {worker['name']}")
        result[worker['name']] = {key: row[key] for key in ('budget', 'image', 'network')}
    return result


def checksums(package):
    return {str(path.relative_to(package)): digest(path)
            for path in sorted(package.rglob('*')) if path.is_file() and path.name != 'SHA256SUMS'}


def verify_package(package, expected):
    paths = list(package.rglob('*'))
    require(not any(path.is_symlink() for path in paths), 'release package contains a symlink')
    names = {str(path.relative_to(package)) for path in paths if path.is_file()}
    require(names == set(expected) | {'SHA256SUMS'}, 'release file set differs from exact approved package')
    # Compare the allowlisted file set before reading any installed file contents.
    require(all(digest(package / name) == value for name, value in expected.items()),
            'release checksum differs from exact approved package')
    wanted = ''.join(f'{value}  {name}\n' for name, value in sorted(expected.items()))
    require((package / 'SHA256SUMS').read_text() == wanted, 'release SHA256SUMS differs from expected content')
    require(os.access(package / 'bin/gamecore-studio', os.X_OK), 'release binary is not executable')


def call_release(installer, root, binary, package, etos):
    # A child captures both installer prints and its inherited CLI output. No patching
    # of release(), its subprocess seam, authority checks, budget checks or markers.
    result = command([sys.executable, str(Path(__file__).resolve()), '--installer-release',
                      str(installer), str(root), str(binary), str(package), str(etos)])
    summaries = [line for line in result.splitlines()
                 if line.startswith(('healthy: ', 'unchanged: release '))]
    require(len(summaries) == 1, 'installer did not report one healthy/no-op result')
    return summaries[0]


def snapshot(etos, base, expected, declared, original_policies):
    release = (base / 'current').resolve(strict=True)
    verify_package(release, expected)
    workers = node_rows(etos, 'worker')
    require(policies(workers, declared) == original_policies, 'worker image/network/budget policy changed')
    for worker in declared:
        row = unique(workers, 'name', worker['name'])
        require(row.get('model') == worker.get('model'), f"node model differs: {worker['name']}")
        served = row.get('instructions')
        require(isinstance(served, str), f"node instructions missing: {worker['name']}")
        require(hashlib.sha256(served.encode()).hexdigest() == expected[worker['instructions']],
                f"node-served instructions differ: {worker['name']}")
    agent = unique(node_rows(etos, 'agent'), 'agent', 'gamecore-studio')
    require(agent.get('state') == 'ready', 'installed companion is not ready')
    pid = agent.get('pid')
    require(isinstance(pid, int) and pid > 0, 'node did not report a running companion PID')
    executable = Path('/proc') / str(pid) / 'exe'
    require(executable.resolve(strict=True) == release / 'bin/gamecore-studio',
            'running companion executable is not the activated immutable release')
    executable_digest = digest(executable)
    require(executable_digest == expected['bin/gamecore-studio'], 'running executable digest differs from built binary')
    designer = unique(workers, 'name', 'gc-designer')
    return {'release': release.name, 'state': agent['state'], 'pid': pid,
            'runningExecutable': str(executable.resolve()),
            'runningExecutableSha256': executable_digest,
            'designerInstructionsSha256': hashlib.sha256(designer['instructions'].encode()).hexdigest(),
            'workerBudget': designer['budget']}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--binary', type=Path, default=ROOT / 'studio/agent/target/release/gamecore-studio')
    parser.add_argument('--etos', type=Path, default=Path.home() / '.local/opt/etos/bin/etos')
    parser.add_argument('--root', type=Path, default=Path.home() / '.local/share/etos-studio')
    parser.add_argument('--approve-designer-budget-manifest', action='store_true')
    args = parser.parse_args()
    binary, etos, root = args.binary.resolve(), args.etos.resolve(), args.root.resolve()
    os.environ['ETOS_ROOT'] = str(root)
    os.environ['PYTHONDONTWRITEBYTECODE'] = '1'
    receipt_path = STATE / 'worker-activation.json'
    require(not receipt_path.exists(), 'P4.2g worker-activation.json already exists; retain existing evidence')
    for process in ('Unity', 'ffmpeg'):
        status = subprocess.run(['pgrep', '-x', process], capture_output=True).returncode
        require(status == 1, f'{process} active or process inspection failed; do not change installed service')
    require(binary.is_file() and os.access(binary, os.X_OK), 'Main must build the executable first; use --binary for its exact path')
    require(etos.is_file() and os.access(etos, os.X_OK), 'installed etos CLI is required')
    for checkout in (ROOT, LIVE):
        require(checkout.is_dir(), 'Main must create the owned .evidence/live checkout first')
        require(command(['git', '-C', str(checkout), 'rev-parse', 'HEAD']).strip() == BASELINE,
                f'checkout must be at P4.2g baseline: {checkout}')
        command(['git', '-C', str(checkout), 'diff', '--exit-code', BASELINE, '--',
                 'studio/agent', 'studio/etos', 'studio/stage'])
    installer_path = LIVE / 'studio/etos/install-state.py'
    installer = load_installer(installer_path)
    source = ROOT / 'studio/etos/agent'
    manifest_text = (source / 'agent.toml').read_text()
    source_manifest = tomllib.loads(manifest_text)
    source_designer = unique(source_manifest['worker'], 'name', 'gc-designer')
    require('budget' not in source_designer, 'baseline manifest changed; review operator overlay')
    needle = 'instructions = "workers/gc-designer.md"'
    require(manifest_text.count(needle) == 1, 'designer manifest insertion point is ambiguous')
    approved_text = manifest_text.replace(needle, needle + '\nbudget = { usd = 0.50 }')
    approved = tomllib.loads(approved_text)
    declared = approved['worker']
    require(unique(declared, 'name', 'gc-designer')['budget'] == BUDGET, 'designer budget overlay failed')
    before_policies = policies(node_rows(etos, 'worker'), declared)
    base = root / 'agents/gamecore-studio'
    current = base / 'current'
    require(current.is_symlink() and current.exists(), 'existing registered companion current symlink is required')
    current_manifest = tomllib.loads((current / 'agent.toml').read_text())
    reconcile = current_manifest != approved
    if reconcile:
        require(current_manifest == source_manifest,
                'installed manifest differs beyond designer budget; explicit operator reconciliation is required')
        require(args.approve_designer_budget_manifest,
                'installed manifest omits the existing USD 0.50 ceiling; rerun with --approve-designer-budget-manifest for the R6-F documented registration prerequisite')
    projects = [LIVE / 'games' / name for name in ('hollowmere', 'cleanproof')]
    for project in projects:
        require(project.is_dir() and not project.is_symlink() and project.resolve().is_relative_to(LIVE.resolve()),
                f'owned live Unity project missing or outside owned live checkout: {project}')
        require((project / 'ProjectSettings/ProjectSettings.asset').is_file(), 'owned Unity project settings missing')
    tracked = command(['git', '-C', str(ROOT), 'ls-files', '-z', '--', 'studio/etos/agent/workers']).split('\0')
    worker_paths = [Path(name) for name in tracked if name]
    require(worker_paths, 'no tracked worker files found')
    with tempfile.TemporaryDirectory(prefix='.p42g-package-', dir=base) as temporary:
        package = Path(temporary) / 'package'
        (package / 'bin').mkdir(parents=True)
        (package / 'agent.toml').write_text(approved_text)
        shutil.copyfile(binary, package / 'bin/gamecore-studio')
        (package / 'bin/gamecore-studio').chmod(0o755)
        for relative in worker_paths:
            origin = ROOT / relative
            require(not origin.is_symlink(), 'tracked worker file may not be a symlink')
            target = package / relative.relative_to('studio/etos/agent')
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(origin, target)
        expected = checksums(package)
        checksum_text = ''.join(f'{value}  {name}\n' for name, value in sorted(expected.items()))
        (package / 'SHA256SUMS').write_text(checksum_text)
        operator_package = base / ('0.1.0-p42g-operator-' + hashlib.sha256(checksum_text.encode()).hexdigest()[:16])
        if operator_package.exists():
            verify_package(operator_package, expected)
        else:
            os.replace(package, operator_package)
        # Registration is the existing secret-free seam. Suppress its config diff;
        # evidence retains only the owned project IDs and roots, never config text.
        with contextlib.redirect_stdout(io.StringIO()):
            for project in projects:
                installer.register(root, str(project), 'auto')
        if reconcile:
            command([str(etos), 'agent', 'upgrade', '--link', str(operator_package)])
            require(tomllib.loads((current / 'agent.toml').read_text()) == approved,
                    'operator registration did not activate the approved manifest')
            require(policies(node_rows(etos, 'worker'), declared) == before_policies,
                    'operator registration changed image/network/budget policy')
        first = call_release(installer_path, root, binary, operator_package, etos)
        before_repeat = snapshot(etos, base, expected, declared, before_policies)
        marker = base / '.applied-config.sha256'
        marker_before = (marker.read_bytes(), marker.stat().st_mtime_ns)
        link_before = (os.readlink(current), current.lstat().st_mtime_ns)
        repeated = call_release(installer_path, root, binary, operator_package, etos)
        require(repeated == f"unchanged: release {before_repeat['release']} (checksums verified)",
                'repeat release was not the installer checksum-verified no-op')
        require((marker.read_bytes(), marker.stat().st_mtime_ns) == marker_before,
                'repeat release rewrote the applied-config marker')
        require((os.readlink(current), current.lstat().st_mtime_ns) == link_before,
                'repeat release switched the current symlink')
        after_repeat = snapshot(etos, base, expected, declared, before_policies)
        require(after_repeat == before_repeat, 'repeat changed the running companion or node-served worker state')
    config = tomllib.loads((base / 'state/config.toml').read_text())
    registered = {identity: path for identity, path in config['stage']['projects'].items()
                  if path in {str(project) for project in projects}}
    require(set(registered.values()) == {str(project) for project in projects}, 'owned projects were not registered')
    require(config['stage']['command'] == str(LIVE / 'studio/stage/stage.sh'), 'stage command is not owned live source')
    receipt = dict(after_repeat, productRevision=BASELINE, label='p42g-worker-activation',
                   recordedAt=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                   binarySha256=expected['bin/gamecore-studio'],
                   installerSha256=digest(installer_path),
                   operatorManifestSha256=expected['agent.toml'],
                   operatorPackage=operator_package.name,
                   operatorManifestReconciled=reconcile,
                   operatorOverlay='Only designer worker budget = USD 0.50; exact baseline binary and tracked worker files.',
                   workerFilesSha256={name: value for name, value in expected.items() if name.startswith('workers/')},
                   projects=registered, workerPoliciesPreserved=True, repeatChecksumNoop=True,
                   installerResults=[first, repeated])
    STATE.mkdir(parents=True, exist_ok=True)
    rendered = v.scrub(json.dumps(receipt, indent=2)) + '\n'
    with receipt_path.open('x') as stream:
        stream.write(rendered)
    print(rendered, end='')


if __name__ == '__main__':
    sys.dont_write_bytecode = True
    try:
        if len(sys.argv) == 7 and sys.argv[1] == '--installer-release':
            _, _, installer_path, root, binary, manifest_dir, etos = sys.argv
            load_installer(Path(installer_path)).release(
                root=Path(root), binary=Path(binary), manifest_dir=Path(manifest_dir), etos=etos)
        else:
            main()
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        raise SystemExit(v.scrub(str(error))) from None
