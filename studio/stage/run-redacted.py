#!/usr/bin/env python3
"""Run an operator child, forwarding signals to its group; redact before any log write.
CLI: --log PATH --timeout SECONDS --silence SECONDS -- COMMAND [ARGS].
The service must supply an env-cleared sandbox; this operator runner is not a sandbox.
"""
import argparse
import os
import re
import selectors
import signal
import subprocess
import sys
import time
from redact import redact


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--log', required=True)
    parser.add_argument('--timeout', type=int, required=True)
    parser.add_argument('--silence', type=int, required=True)
    parser.add_argument('command', nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ['--'] else args.command
    # Operator UI variables are enumerated, never prefix-matched. Service mode must still run
    # this inside R2-F's env-cleared sandbox with slot-local HOME and trusted absolute command.
    child_env = {name: os.environ[name] for name in (
        'HOME', 'USER', 'LOGNAME', 'PATH', 'LANG', 'LC_ALL', 'DISPLAY', 'XAUTHORITY',
        'XDG_RUNTIME_DIR', 'GCS_EVIDENCE_DIR', 'GCS_FLIP') if name in os.environ}
    child = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                             start_new_session=True, env=child_env)
    interrupted = []
    def stop(sig, frame):
        interrupted.append(sig)
    for sig in (signal.SIGTERM, signal.SIGINT, signal.SIGHUP):
        signal.signal(sig, stop)
    start = last = time.monotonic()
    killed_at = None
    result = None
    pending = b''
    oversized = False
    json_pending = ''
    selector = selectors.DefaultSelector()
    selector.register(child.stdout, selectors.EVENT_READ)
    with open(args.log, 'w', encoding='utf-8') as output:
        def write(line):
            nonlocal json_pending
            text = line.decode('utf-8', errors='replace')
            # Pretty-printed JSON is held until complete, so nested secret values cannot leak.
            object_start = re.search(r'\{\s*(?:"|$)', text)
            stripped = text.lstrip()
            array_start = stripped.startswith('[') and (not stripped[1:].strip() or stripped[1:].lstrip()[0] in '\"{[0123456789-]')
            if json_pending or object_start or array_start:
                json_pending += text
                import json
                try:
                    match = re.search(r'\{\s*(?:"|$)', json_pending)
                    offset = match.start() if match else len(json_pending)-len(json_pending.lstrip())
                    _, end = json.JSONDecoder().raw_decode(json_pending[offset:])
                except ValueError:
                    if len(json_pending) > 1024 * 1024:
                        output.write('[incomplete structured log omitted]\n')
                        json_pending = ''
                    return
                prefix = json_pending[:offset]
                structured = json_pending[offset:offset+end]
                suffix = json_pending[offset+end:]
                text, json_pending = redact(prefix) + redact(structured).rstrip('\n') + redact(suffix), '' 
            output.write(redact(text))
            output.flush()
        while selector.get_map() or child.poll() is None:
            now = time.monotonic()
            if killed_at is None and (interrupted or now-start > args.timeout or (args.silence and now-last > args.silence)):
                result = 143 if interrupted else 124
                killed_at = now
                try: os.killpg(child.pid, signal.SIGTERM)
                except ProcessLookupError: pass
            if killed_at is not None and now-killed_at > 8:
                try: os.killpg(child.pid, signal.SIGKILL)
                except ProcessLookupError: pass
            for key, _ in selector.select(0.2):
                chunk = os.read(key.fd, 65536)
                if not chunk:
                    selector.unregister(key.fileobj)
                    continue
                last = now
                pending += chunk
                while b'\n' in pending:
                    line, pending = pending.split(b'\n', 1)
                    if oversized:
                        output.write('[oversized log line omitted]\n')
                        oversized = False
                    else:
                        write(line+b'\n')
                if len(pending) > 1024 * 1024:
                    pending = b''
                    oversized = True
            if child.poll() is not None and killed_at is None:
                # Reap leftover descendants rather than allowing inherited stdout to hang forever.
                killed_at = now
                try: os.killpg(child.pid, signal.SIGTERM)
                except ProcessLookupError: pass
        if pending and not oversized: write(pending)
        if json_pending: output.write('[incomplete structured log omitted]\n')
    return result if result is not None else max(0, child.wait()) if child.returncode >= 0 else 128-child.returncode


if __name__ == '__main__':
    sys.exit(main())
