#!/usr/bin/env python3
"""Host-only locked NuGet provisioning and payload verification; never runs candidate code."""
import argparse
import base64
import hashlib
import json
import os
import re
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile
from urllib.parse import unquote
import upm_cache
import analysis_context

HERE = Path(__file__).resolve().parent


def regular(path):
    if path.is_symlink() or not path.is_file():
        raise ValueError('cache_invalid: expected regular file')
    return path.read_bytes()


def no_links(path):
    if any(parent.is_symlink() for parent in (path, *path.parents)):
        raise ValueError('cache_invalid: linked cache path')


def versioned_cache(root, repo, source_project, binary):
    """Ask the staging binary for its identity; never duplicate its compiled-in lock hash."""
    root = root.absolute()
    no_links(root)
    value = subprocess.check_output([
        str(binary.resolve()), 'stage', 'cache-path', '--repo', str(repo.resolve()),
        '--source-project', str(source_project.resolve()), '--root', str(root),
    ], text=True).strip()
    cache = Path(value)
    if cache.parent != root / '_warm' or not re.fullmatch(r'[a-f0-9]{64}', cache.name):
        raise ValueError('cache_invalid: binary returned an unexpected versioned cache path')
    no_links(cache)
    return cache


def verify(cache, expected=None, complete=False):
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
    if complete or (cache / 'analysis-context').exists():
        for relative, digest in analysis_context.pinned().items():
            analysis_context.checked(cache / 'analysis-context', relative, digest)
    if complete or (cache / 'upm').exists():
        upm_cache.verify(cache / 'upm')


def verify_complete(cache):
    """A NuGet-only cache is not a usable offline stage prerequisite."""
    no_links(cache)
    verify(cache, complete=True)
    packages = cache / 'Library/PackageCache'
    no_links(packages)
    if not packages.is_dir():
        raise ValueError('cache_invalid: offline Unity PackageCache missing')


def provision(cache, offline):
    no_links(cache)
    cache.mkdir(parents=True, exist_ok=True)
    if cache.is_symlink() or (cache / 'nuget').exists():
        verify(cache)
        return
    with tempfile.TemporaryDirectory(prefix='gamecore-stage-provision-') as temp:
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
                                '-p:NuGetAudit=false', '-p:ImportDirectoryBuildProps=false',
                                '-p:ImportDirectoryBuildTargets=false', '-p:ImportDirectoryPackagesProps=false',
                                '--nologo'], check=True)
        shutil.copyfile(HERE / 'cache/cache-lock.json', work / 'cache-manifest.json')
        verify(work)
        shutil.move(str(work / 'nuget'), str(cache / 'nuget'))
        shutil.copyfile(work / 'cache-manifest.json', cache / 'cache-manifest.json')
    verify(cache)


def seed_unity(cache, source):
    """Seed only operator metadata and UPM packages, never candidate or project code."""
    trusted = json.loads((HERE / 'cache/unity-metadata-lock.json').read_text())
    for relative, digest in trusted.items():
        path = source / relative
        if hashlib.sha256(regular(path)).hexdigest() != digest:
            raise ValueError('cache_invalid: trusted Unity metadata mismatch')
    analysis_context.seed(source, cache / 'analysis-context')
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
    parser.add_argument('cache', type=Path, nargs='?', help='exact versioned cache directory (from stage cache-path)')
    parser.add_argument('--stage-root', type=Path, help='derive the exact versioned cache below this private or owner-scoped root')
    parser.add_argument('--repo', type=Path, default=HERE.parents[1])
    parser.add_argument('--source-project', type=Path)
    parser.add_argument('--binary', type=Path, help='companion built from this checkout; defaults to studio/agent/target/release/gamecore-studio')
    parser.add_argument('--verify', action='store_true')
    parser.add_argument('--complete', action='store_true', help='also require pinned analysis metadata and public UPM inputs when verifying')
    parser.add_argument('--offline-from', type=Path, help='copy the pinned closure from an existing host NuGet cache')
    parser.add_argument('--unity-library', type=Path, help='trusted host Unity 6000.0.75f1 Library with offline UPM and pinned analysis metadata')
    parser.add_argument('--upm-from', type=Path, help='host public Unity UPM cache; only committed public metadata/archive records are copied')
    args = parser.parse_args()
    if bool(args.cache) == bool(args.stage_root):
        parser.error('supply either an exact cache directory or --stage-root, not both')
    if args.stage_root and not args.source_project:
        parser.error('--stage-root requires --source-project')
    if not args.verify and (args.stage_root or args.complete) and (not args.unity_library or not args.upm_from):
        parser.error('complete provisioning requires --unity-library and --upm-from')
    try:
        cache = (versioned_cache(args.stage_root, args.repo, args.source_project,
                                 args.binary or args.repo / 'studio/agent/target/release/gamecore-studio')
                 if args.stage_root else args.cache)
        if args.verify:
            if args.complete or args.stage_root:
                verify_complete(cache)
            else:
                verify(cache)
        else:
            provision(cache, args.offline_from)
            if args.upm_from:
                upm_cache.provision(cache / 'upm', args.upm_from)
            if args.unity_library:
                seed_unity(cache, args.unity_library)
            if args.stage_root or args.complete:
                verify_complete(cache)
        scope = 'NuGet, Unity metadata and UPM' if args.stage_root or args.complete else 'supplied dependency'
        print(f'stage cache: verified pinned {scope} payloads at {cache}')
    except (OSError, ValueError, KeyError, zipfile.BadZipFile, subprocess.CalledProcessError):
        raise SystemExit('cache_invalid: provisioning or integrity verification failed')


if __name__ == '__main__':
    main()
