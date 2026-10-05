#!/usr/bin/env python3
"""Read Unity evidence without printing raw logs or rewriting result XML."""
import argparse
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'stage'))
from redact import redact


def env_gates(path):
    try:
        root = ET.parse(path).getroot()
    except (OSError, ET.ParseError):
        return []
    if root.get('result') == 'Failed' or any(c.get('result') == 'Failed' for c in root.iter('test-case')):
        return []
    names = set()
    for case in root.iter('test-case'):
        if case.get('result', '').split(':')[0] != 'Skipped':
            continue
        for reason in case.findall('./reason/message'):
            names.update(re.findall(r'\bGAMECORE_[A-Z0-9_]+\b', reason.text or ''))
    return sorted(names)


def compiler_errors(log, project, since):
    pattern = re.compile(r'error CS[0-9]+|Scripts have compiler errors|Compilation failed')
    paths = [Path(log)]
    bee = Path(project) / 'Library/Bee/tundra.log.json'
    # Do not attribute a previous invocation's Bee failure to this compile.
    if bee.is_file() and bee.stat().st_mtime >= since:
        paths.append(bee)
    found = set()

    def strings(value):
        if isinstance(value, str):
            yield value
        elif isinstance(value, dict):
            for child in value.values():
                yield from strings(child)
        elif isinstance(value, list):
            for child in value:
                yield from strings(child)

    for path in paths:
        try:
            raw = path.read_text(errors='replace')
        except OSError:
            continue
        try:
            chunks = list(strings(json.loads(raw)))
        except ValueError:
            chunks = []
            for line in raw.splitlines():
                try:
                    chunks.extend(strings(json.loads(line)))
                except ValueError:
                    chunks.append(line)
        for chunk in chunks:
            for line in chunk.splitlines():
                if pattern.search(line):
                    found.add(redact(line).strip())
    return sorted(found)[:200]


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='mode', required=True)
    gates = sub.add_parser('env-gates')
    gates.add_argument('xml')
    errors = sub.add_parser('errors')
    errors.add_argument('log')
    errors.add_argument('project')
    errors.add_argument('since', type=float)
    args = parser.parse_args()
    if args.mode == 'env-gates':
        print(', '.join(env_gates(args.xml)))
    else:
        print('\n'.join(compiler_errors(args.log, args.project, args.since)))
