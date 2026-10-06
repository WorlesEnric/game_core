#!/usr/bin/env python3
"""UNITY adapter: drain existing Editors, then prevent new leases during a benchmark.

unity-batch.sh reserves exactly one slot before invoking this adapter. Holding the
allocator mutex delays new reservations and completed runners' bookkeeping, never
terminates their Editors. Release the mutex before unity-batch releases our slot.
The normal redacting runner owns timeout/signal forwarding for this process group.
"""
import fcntl
import os
from pathlib import Path
import subprocess
import sys
import time


def active_editors():
    for p in Path('/proc').iterdir():
        if not p.name.isdigit():
            continue
        try:
            command = (p / 'cmdline').read_bytes()
            if (p / 'comm').read_text().strip() == 'Unity' and command and b'AssetImportWorker' not in command:
                return True
        except OSError:
            pass
    return False


def main():
    mutex = Path.home() / os.environ.get('GC_STUDIO_REMOTE_BASE', 'wkspace/gc-studio') / '.unity-slots/allocator.lock'
    unity = Path.home() / 'Unity/Hub/Editor/6000.0.75f1/Editor/Unity'
    with mutex.open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        quiet = 0
        polls = 0
        while quiet < 2:
            quiet = 0 if active_editors() else quiet + 1
            if polls % 60 == 0:
                print('CORE-PICK: waiting for existing Editors to finish; one reserved slot.', flush=True)
            polls += 1
            time.sleep(1)
        print('CORE-PICK: exclusive Editor measurement begins.', flush=True)
        return subprocess.call([str(unity), *sys.argv[1:]])


if __name__ == '__main__':
    sys.exit(main())
