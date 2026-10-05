"""Provision pinned public Unity registry metadata/archives, without credentials or host config."""
import base64
import hashlib
import json
from pathlib import Path


def location(root, integrity):
    algorithm, digest = integrity.split('-', 1)
    if algorithm not in ('sha1', 'sha512'):
        raise ValueError('cache_invalid: unsupported UPM integrity')
    value = base64.b64decode(digest, validate=True).hex()
    return root / 'db/content-v2' / algorithm / value[:2] / value[2:4] / value[4:]


def index_path(root, key):
    value = hashlib.sha256(key.encode()).hexdigest()
    return root / 'db/index-v5' / value[:2] / value[2:4] / value[4:]


def manifest():
    return json.loads((Path(__file__).parent / 'cache/upm-lock.json').read_text())


def verify(root):
    for record in manifest()['records']:
        content = location(root, record['integrity'])
        index = index_path(root, record['key'])
        for path in (content, index):
            if any(parent.is_symlink() for parent in (path, *path.parents)):
                raise ValueError('cache_invalid: UPM link')
        if hashlib.sha256(content.read_bytes()).hexdigest() != record['sha256']:
            raise ValueError('cache_invalid: UPM metadata digest')
        latest = None
        for line in index.read_text().splitlines():
            if '\t' not in line:
                continue
            checksum, body = line.split('\t', 1)
            if hashlib.sha1(body.encode()).hexdigest() != checksum:
                raise ValueError('cache_invalid: UPM index checksum')
            entry = json.loads(body)
            if entry['key'] == record['key']:
                latest = entry
        if latest is None or latest['integrity'] != record['integrity']:
            raise ValueError('cache_invalid: UPM index mapping')


def provision(root, source):
    for record in manifest()['records']:
        data = location(source, record['integrity']).read_bytes()
        if hashlib.sha256(data).hexdigest() != record['sha256']:
            raise ValueError('cache_invalid: host UPM metadata digest')
        target = location(root, record['integrity'])
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        # Only cache bookkeeping is copied; host response headers/configuration never enter.
        value = json.dumps({'key':record['key'], 'integrity':record['integrity'], 'size':record['size'], 'time':0}, separators=(',', ':'))
        target = index_path(root, record['key'])
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text('\n' + hashlib.sha1(value.encode()).hexdigest() + '\t' + value + '\n')
    verify(root)
