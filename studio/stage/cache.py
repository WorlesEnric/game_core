#!/usr/bin/env python3
"""Host-only locked NuGet provisioning and payload verification; never runs candidate code."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile
from urllib.parse import unquote

HERE = Path(__file__).resolve().parent


def regular(path):
    if path.is_symlink() or not path.is_file():
        raise ValueError('cache_invalid: expected regular file')
    return path.read_bytes()


def verify(cache, expected=None):
    expected = expected or json.loads((HERE / 'cache/cache-lock.json').read_text())
    if json.loads(regular(cache / 'cache-manifest.json')) != expected:
        raise ValueError('cache_invalid: manifest differs from pinned closure')
    root = cache / 'nuget'
    if cache.is_symlink() or root.is_symlink() or not root.is_dir():
        raise ValueError('cache_invalid: cache root')
    actual = set()
    for directory, dirs, files in os.walk(root, followlinks=False):
        for name in dirs + files:
            if (Path(directory) / name).is_symlink():
                raise ValueError('cache_invalid: symlink')
        for name in files:
            actual.add((Path(directory) / name).relative_to(root).as_posix())
    allowed = set()
    for package in expected['packages']:
        stem = package['id'].lower() + '.' + package['version']
        relative = Path(package['id'].lower()) / package['version']
        folder = root / relative
        archive = folder / (stem + '.nupkg')
        if base64.b64encode(hashlib.sha512(regular(archive)).digest()).decode() != package['sha512']:
            raise ValueError('cache_invalid: nupkg sha512 mismatch')
        for name in (stem + '.nupkg', stem + '.nupkg.sha512', '.nupkg.metadata'):
            regular(folder / name)
            allowed.add((relative / name).as_posix())
        with zipfile.ZipFile(archive) as payload:
            for entry in payload.infolist():
                if entry.is_dir() or entry.filename.startswith(('_rels/', 'package/')) or entry.filename == '[Content_Types].xml':
                    continue
                name = unquote(entry.filename)
                if name.lower().endswith('.nuspec'):
                    name = package['id'].lower() + '.nuspec'
                target = folder / name
                if '..' in Path(name).parts or Path(name).is_absolute():
                    raise ValueError('cache_invalid: archive path')
                if regular(target) != payload.read(entry):
                    raise ValueError('cache_invalid: expanded package differs from pinned archive')
                allowed.add((relative / name).as_posix())
    if actual != allowed:
        raise ValueError('cache_invalid: unexpected or missing package files')


def provision(cache, offline):
    cache.mkdir(parents=True, exist_ok=True)
    if cache.is_symlink() or (cache / 'nuget').exists():
        verify(cache)
        return
    with tempfile.TemporaryDirectory(prefix='.provision-', dir=cache) as temp:
        work = Path(temp)
        for name in ('Dependencies.csproj', 'RulesDependencies.csproj', 'NuGet.Config', 'packages.lock.json', 'Rules.packages.lock.json'):
            shutil.copyfile(HERE / 'cache' / name, work / name)
        if offline:
            (work / 'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
            expected = json.loads((HERE / 'cache/cache-lock.json').read_text())
            for package in expected['packages']:
                relative = Path(package['id'].lower()) / package['version']
                shutil.copytree(offline / relative, work / 'nuget' / relative)
        else:
            for project in ('Dependencies.csproj', 'RulesDependencies.csproj'):
                subprocess.run([str(Path.home() / '.dotnet/dotnet'), 'restore', str(work / project),
                                '--locked-mode', '--packages', str(work / 'nuget'), '--configfile', str(work / 'NuGet.Config'),
                                '-p:NuGetAudit=false', '--nologo'], check=True)
        shutil.copyfile(HERE / 'cache/cache-lock.json', work / 'cache-manifest.json')
        verify(work)
        (work / 'nuget').rename(cache / 'nuget')
        (work / 'cache-manifest.json').replace(cache / 'cache-manifest.json')
    verify(cache)


def seed_unity(cache, source):
    """Seed only operator metadata and UPM packages, never candidate or project code."""
    trusted = json.loads((HERE / 'cache/unity-metadata-lock.json').read_text())
    for relative, digest in trusted.items():
        path = source / relative
        if hashlib.sha256(regular(path)).hexdigest() != digest:
            raise ValueError('cache_invalid: trusted Unity metadata mismatch')
    library = cache / 'Library'
    if (library / 'ArtifactDB').exists():
        return
    for relative in trusted:
        target = library / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source / relative, target)
    # UPM payloads are trusted operator cache inputs; refuse links before copying.
    for directory, dirs, files in os.walk(source / 'PackageCache', followlinks=False):
        for name in dirs + files:
            if (Path(directory) / name).is_symlink():
                raise ValueError('cache_invalid: UPM link')
    shutil.copytree(source / 'PackageCache', library / 'PackageCache', dirs_exist_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('cache', type=Path, help='exact versioned cache directory (from stage cache-path)')
    parser.add_argument('--verify', action='store_true')
    parser.add_argument('--offline-from', type=Path, help='copy the pinned closure from an existing host NuGet cache')
    parser.add_argument('--unity-library', type=Path, help='trusted host Unity 6000.0.75f1 Library with offline UPM and pinned analysis metadata')
    args = parser.parse_args()
    try:
        if args.verify:
            verify(args.cache)
        else:
            provision(args.cache, args.offline_from)
            if args.unity_library:
                seed_unity(args.cache, args.unity_library)
        print('stage cache: verified pinned analyzer/Rules closures and expanded payloads')
    except (OSError, ValueError, KeyError, zipfile.BadZipFile, subprocess.CalledProcessError):
        raise SystemExit('cache_invalid: provisioning or integrity verification failed')


if __name__ == '__main__':
    main()
