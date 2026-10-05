#!/usr/bin/env python3
"""Count Editors not descended from a live slot owner; invoked under allocator flock."""
from pathlib import Path
import sys
import re

owners = set()
for path in Path(sys.argv[1]).glob('slot*.owner'):
    try:
        match = re.search(r'(?:^|\s)pid=(\d+)', path.read_text())
        if match is None:
            continue
        pid = int(match.group(1))
        if Path(f'/proc/{pid}').exists():
            owners.add(pid)
    except (ValueError, OSError, IndexError):
        pass
processes = {}
editors = []
for path in Path('/proc').iterdir():
    if not path.name.isdigit():
        continue
    try:
        stat = (path / 'stat').read_text()
        tail = stat[stat.rfind(')') + 2:].split()
        pid = int(path.name)
        processes[pid] = int(tail[1])
        if (path / 'comm').read_text().strip() == 'Unity' and tail[0] != 'Z':
            args = (path / 'cmdline').read_bytes()
            if b'AssetImportWorker' not in args:
                editors.append(pid)
    except (OSError, ValueError, IndexError):
        pass
count = 0
for pid in editors:
    seen = set()
    while pid and pid not in seen and pid not in owners:
        seen.add(pid)
        pid = processes.get(pid, 0)
    if pid not in owners:
        count += 1
print(count)
