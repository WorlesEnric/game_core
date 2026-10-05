"""Pinned Unity analysis inputs, independent of Unity's mutable Library outputs."""
import hashlib
import json
from pathlib import Path
import shutil

MANIFEST = Path(__file__).parent / 'cache/unity-metadata-lock.json'


def pinned():
    try:
        return json.loads(MANIFEST.read_text())
    except FileNotFoundError as error:
        raise ValueError('trusted Unity metadata missing: pinned manifest unavailable') from error


def checked(root, relative, digest):
    path = root / relative
    if Path(relative).is_absolute() or '..' in Path(relative).parts:
        raise ValueError('trusted Unity metadata manifest path invalid')
    if any(p.is_symlink() for p in [path, *path.parents]):
        raise ValueError('trusted Unity metadata contains a link')
    if not path.is_file():
        raise ValueError('trusted Unity metadata source incomplete: ' + relative)
    if hashlib.sha256(path.read_bytes()).hexdigest() != digest:
        raise ValueError('trusted Unity metadata digest mismatch: ' + relative)
    return path


def seed(source, target):
    trusted = pinned()
    inputs = [(relative, checked(source, relative, digest)) for relative, digest in trusted.items()]
    # Always reseed, including reused slots. Never consult the slot Library.
    if any(p.is_symlink() for p in [target, *target.parents]):
        raise ValueError('trusted Unity metadata contains a link')
    if target.exists():
        shutil.rmtree(target)
    for relative, path in inputs:
        destination = target / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, destination)
    for relative, digest in trusted.items():
        checked(target, relative, digest)
