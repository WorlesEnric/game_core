#!/usr/bin/env python3
"""Candidate-free licensing experiment. Private copies never enter the repository."""
import argparse
import os
from pathlib import Path
import re
import shlex
import shutil
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('mode', choices=['host', 'copy', 'identity'])
args = parser.parse_args()
repo = Path(__file__).resolve().parents[4]
home = Path.home()
root = home / '.cache/gamecore-studio/r2-f2'
root.mkdir(parents=True, exist_ok=True, mode=0o700)
job = root / args.mode
job.mkdir(mode=0o700)  # refuse reuse
project = job / 'project'
for part in ['Assets', 'ProjectSettings', 'Packages']:
    (project / part).mkdir(parents=True)
(project / 'Packages/manifest.json').write_text('{"dependencies":{}}\n')
(project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 6000.0.75f1\n')
editor = home / 'Unity/Hub/Editor/6000.0.75f1/Editor'
config = root / 'docker-client'
config.mkdir(exist_ok=True, mode=0o700)
command = ['bash', str(repo / 'studio/tools/unity-batch.sh'), '--project', str(project),
           '--log-dir', str(job / 'logs'), '--label', 'r2-f2-' + args.mode,
           '--timeout', '180', '--attempts', '1', '--', '-quit']
env = dict(os.environ)
private_home = job / 'home'
private_var = job / 'var-unity'
if args.mode != 'host':
    private_home.mkdir(mode=0o700)
    # Only Unity licensing state; no Hub, general config, caches, sockets or key files.
    def ignore(directory, names):
        excluded = []
        for name in names:
            p = Path(directory) / name
            if (name in ['Editor', 'auth.json', 'providers.env'] or name.endswith('.key')
                    or '.log' in name or name.startswith('CoreBusinessMetrics')
                    or p.is_symlink() or not (p.is_file() or p.is_dir())):
                excluded.append(name)
        return excluded
    for rel in ['.config/unity3d/Unity', '.local/share/unity3d/Unity']:
        source = home / rel
        target = private_home / rel
        if source.is_dir():
            shutil.copytree(source, target, ignore=ignore)
        else:
            target.mkdir(parents=True)
    for rel in ['.cache/unity3d', '.config/unity3d/Unity', '.local/share/unity3d']:
        (private_home / rel).mkdir(parents=True, exist_ok=True)
    docker = ['/usr/bin/docker', '--config', str(config), 'run', '--rm', '--pull=never',
              '--name', 'r2-f2-' + args.mode, '--network', 'none', '--read-only',
              '--cap-drop=ALL', '--security-opt=no-new-privileges', '--pids-limit=512',
              '--user', f'{os.getuid()}:{os.getgid()}', '--env', 'HOME=' + str(home),
              '--env', 'PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin',
              '--mount', f'type=bind,src={job},dst={job}',
              '--mount', f'type=bind,src={private_home},dst={home}',
              '--mount', f'type=bind,src={editor},dst={editor},readonly',
              '--tmpfs', '/tmp:rw,nosuid,nodev', '--workdir', str(project)]
    var = Path('/var/lib/unity')
    if var.is_dir():
        shutil.copytree(var, private_var, ignore=ignore)
        docker += ['--mount', f'type=bind,src={private_var},dst={var}']
    service = Path('/usr/share/unity3d/config/services-config.json')
    if service.is_file():
        shutil.copyfile(service, job / 'services-config.json')
        docker += ['--mount', f'type=bind,src={job}/services-config.json,dst={service},readonly']
    if args.mode == 'identity':
        docker += ['--hostname', os.uname().nodename, '--mount',
                   'type=bind,src=/etc/machine-id,dst=/etc/machine-id,readonly']
    # Docker must not create root-owned nested mount targets in the private HOME.
    for target in [job, editor]:
        (private_home / target.relative_to(home)).mkdir(parents=True, exist_ok=True)
    docker += ['gamecore-stage:6000.0.75f1-v1', str(editor / 'Unity')]
    wrapper = job / 'docker-unity'
    wrapper.write_text('#!/bin/sh\nexec ' + shlex.join(docker) + ' "$@"\n')
    wrapper.chmod(0o700)
    env['UNITY'] = str(wrapper)
    print('ENGINE: ' + shlex.join(docker) + ' <unity-batch arguments>', flush=True)
else:
    env['UNITY'] = str(editor / 'Unity')
print('COMMAND: UNITY=' + shlex.quote(env['UNITY']) + ' ' + shlex.join(command), flush=True)
try:
    with (job / 'allocator.log').open('w') as output:
        result = subprocess.run(command, env=env, stdout=output, stderr=subprocess.STDOUT)
    print('WRAPPER_EXIT:', result.returncode)
    # Retain licensing diagnostics only, with identifying fields removed.
    for log in sorted((job / 'logs').glob('*.log')):
        print('LOG:', log)
        for line in log.read_text(errors='replace').splitlines():
            if re.search(r'licens|entitlement|Batchmode quit|Aborting batchmode', line, re.I):
                if re.search(r'token|serial|machine.?id|session|correlation', line, re.I):
                    print('[licensing identifier/token line omitted]')
                else:
                    print(line)
    for line in (job / 'allocator.log').read_text(errors='replace').splitlines():
        if line.startswith('RESULT '):
            print(line)
finally:
    if args.mode != 'host':
        subprocess.run(['/usr/bin/docker', '--config', str(config), 'rm', '-f', 'r2-f2-' + args.mode],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        shutil.rmtree(private_home)
        if private_var.exists():
            shutil.rmtree(private_var)
        (job / 'services-config.json').unlink(missing_ok=True)
        print('PRIVATE_LICENSE_COPY_DELETED')
