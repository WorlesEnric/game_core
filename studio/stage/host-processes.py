#!/usr/bin/env python3
"""Count Editors not descended from a live slot owner; invoked under allocator flock."""
from pathlib import Path
import sys
import re
import fcntl


def count_unowned(slot_dir: Path, proc_root: Path = Path('/proc')) -> int:
    owners = set()
    for path in slot_dir.glob('slot*.owner'):
        try:
            # Owner text is diagnostic, never a reservation. Ignore unlocked files even
            # when a stale PID has been reused by a currently running process.
            with path.with_suffix('.lock').open('a') as lock:
                try:
                    fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
                except BlockingIOError:
                    pass
                else:
                    continue
            match = re.search(r'(?:^|\s)pid=(\d+)', path.read_text())
            if match is None:
                continue
            pid = int(match.group(1))
            if (proc_root / str(pid)).exists():
                owners.add(pid)
        except (ValueError, OSError, IndexError):
            pass
    processes = {}
    editors = []
    for path in proc_root.iterdir():
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
    return count


if __name__ == '__main__':
    print(count_unowned(Path(sys.argv[1])))
