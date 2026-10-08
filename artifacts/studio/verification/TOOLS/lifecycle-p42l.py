#!/usr/bin/env python3
"""Unchanged R7 lifecycle route/assertions; record each actual PID-owned window, not desktop pixels."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import time
import verify as v

TOOLS = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS / 'P42hPlayer'))
from window import prepare

ORIGINAL = v.ROOT / 'games/hollowmere/Assets/Hollowmere/Tests/R7_C/PlayerFlow'
spec = importlib.util.spec_from_file_location('lifecycle_original', ORIGINAL / 'run_lifecycle.py')
original = importlib.util.module_from_spec(spec)
spec.loader.exec_module(original)


def dump(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n')


def stop(process):
    if process is not None and process.poll() is None:
        process.send_signal(signal.SIGINT)
        try:
            process.wait(timeout=30)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--player', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    player, output = args.player.resolve(), args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    saves = output / 'saves'
    saves.mkdir()
    environment = dict(os.environ, DISPLAY=':1', XDG_SESSION_TYPE='x11')
    revision = (player.parent / 'revision.txt').read_text().strip()
    if revision != v.git('rev-parse', 'HEAD'):
        raise RuntimeError('Player is not this product revision')
    summary = {'row': 'W-GAME-05', 'status': 'FAIL', 'revision': revision, 'startedUtc': v.utc(),
               'player': str(player), 'executableSha256': original.sha256(player), 'phases': [],
               'capture': 'Two exact PID-owned X11 player-window segments, concatenated after actual process restart; no desktop-only substitution.',
               'routeAndAssertions': 'Unchanged R7-C save-quit and load-ending-restart scripts and production lifecycle checks.'}
    child = recorder = None
    clips = []
    try:
        for script, expected in [('save-quit.autoplay', 'AWAITING_RELAUNCH'), ('load-ending-restart.autoplay', 'PASS')]:
            shutil.copy2(ORIGINAL / script, output / script)
            phase = script.removesuffix('.autoplay')
            player_log = output / (phase + '-player.log')
            command = [str(player), '-screen-fullscreen', '1', '-screen-width', '1920', '-screen-height', '1080',
                       '-autoplay', str(output / script), '-saveDir', str(saves),
                       '-frameLog', str(output / (phase + '-frames.csv')), '-logFile', str(player_log)]
            receipt = {'name': phase, 'command': command, 'startedUtc': v.utc()}
            summary['phases'].append(receipt)
            begun = time.monotonic()
            with (output / (phase + '-stdout.txt')).open('w') as stdout:
                child = subprocess.Popen(command, cwd=player.parent, env=environment, stdin=subprocess.DEVNULL, stdout=stdout, stderr=subprocess.STDOUT)
                receipt['pid'] = child.pid
                deadline = time.monotonic() + 30
                while not player_log.exists() or 'frame pacing' not in player_log.read_text(errors='replace'):
                    if child.poll() is not None or time.monotonic() > deadline:
                        raise RuntimeError('Owned player failed before capture readiness')
                    time.sleep(0.1)
                window = prepare(child.pid)
                receipt['windowId'] = hex(window)
                clip = output / (phase + '.mp4')
                capture = ['ffmpeg', '-hide_banner', '-nostdin', '-loglevel', 'warning', '-n',
                           '-f', 'x11grab', '-window_id', hex(window), '-draw_mouse', '0', '-framerate', '30',
                           '-video_size', '1920x1080', '-thread_queue_size', '1024', '-i', ':1.0',
                           '-f', 'pulse', '-thread_queue_size', '1024', '-i', '@DEFAULT_MONITOR@',
                           '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '23', '-pix_fmt', 'yuv420p',
                           '-c:a', 'aac', '-b:a', '128k', '-movflags', '+faststart', str(clip)]
                receipt['captureCommand'] = capture
                capture_began = time.monotonic()
                with (output / (phase + '-ffmpeg.log')).open('w') as capture_log:
                    recorder = subprocess.Popen(capture, env=environment, stdin=subprocess.DEVNULL, stdout=capture_log, stderr=subprocess.STDOUT)
                    deadline = begun + 900
                    while child.poll() is None:
                        if recorder.poll() is not None:
                            raise RuntimeError('Recorder exited while its owned player was running')
                        if time.monotonic() > deadline:
                            raise RuntimeError('Player phase exceeded unchanged 900-second limit')
                        time.sleep(0.25)
                    receipt['playerExit'] = child.returncode
                    child = None
                    stop(recorder)
                    receipt['recorderExit'] = recorder.returncode
                    recorder = None
                receipt['wallSeconds'] = time.monotonic() - begun
                receipt['captureWallSeconds'] = time.monotonic() - capture_began
            if receipt['playerExit'] != 0:
                raise RuntimeError('Actual player failed: ' + phase)
            report = json.loads((saves / 'lifecycle-report.json').read_text())
            dump(output / (phase + '-report.json'), report)
            if report.get('backend') != 'IL2CPP' or report.get('platform') != 'LinuxPlayer' or report.get('revision') != revision:
                raise RuntimeError('Production lifecycle receipt has wrong build identity')
            if report['status'] != expected:
                raise RuntimeError('Production lifecycle acceptance failed: ' + report['status'])
            names = [stage['name'] for stage in report['stages']]
            if names != (original.STAGES[:5] if expected == 'AWAITING_RELAUNCH' else original.STAGES):
                raise RuntimeError('Lifecycle stages missing or out of order')
            if not report['saveProcessQuitObserved'] or (expected == 'PASS' and not report['finalProcessQuitObserved']):
                raise RuntimeError('Production UI Quit was not observed')
            metadata = json.loads(subprocess.check_output(['ffprobe', '-v', 'error', '-show_streams', '-show_format', '-of', 'json', str(clip)], text=True))
            dump(output / (phase + '-recording.json'), metadata)
            kinds = {stream['codec_type'] for stream in metadata['streams']}
            if not {'audio', 'video'}.issubset(kinds) or float(metadata['format']['duration']) < receipt['captureWallSeconds'] - 5:
                raise RuntimeError('Owned-window recording is missing audio/video or is truncated')
            receipt['recordingValidated'] = True
            clips.append(clip)
            time.sleep(2)
        if summary['phases'][0]['pid'] == summary['phases'][1]['pid']:
            raise RuntimeError('Lifecycle did not restart in a distinct process')
        listing = output / 'segments.txt'
        listing.write_text(''.join("file '" + clip.name + "'\n" for clip in clips))
        subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-n', '-f', 'concat', '-safe', '0',
                        '-i', str(listing), '-c', 'copy', str(output / 'playthrough.mp4')], check=True)
        summary['status'] = 'PASS'
    except Exception as error:
        summary['failure'] = str(error)
    finally:
        stop(child)
        stop(recorder)
        summary['finishedUtc'] = v.utc()
        dump(output / 'driver-summary.json', summary)
    print(json.dumps(summary))
    return int(summary['status'] != 'PASS')


if __name__ == '__main__':
    raise SystemExit(main())
