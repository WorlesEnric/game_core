#!/usr/bin/env python3
"""Keep allocator/watchdog/redaction; enable graphics and forward bounded driver settings."""
import os
from pathlib import Path
import sys
for suffix in ('WORKFLOW','OUT','MECH_CANDIDATE'):
    value=os.environ.get('GAMECORE_P42C_'+suffix)
    if value:
        os.environ['GCS_P32_'+suffix]=value
os.environ['GAMECORE_ETOS_KEY_FILE']=str(Path.home()/'.config/gamecore-studio/app-key.json')
if os.environ.get('GAMECORE_P42C_MIC'):
    os.environ['PULSE_SOURCE']=os.environ['GAMECORE_P42C_MIC']
binary=str(Path.home()/'Unity/Hub/Editor/6000.0.75f1/Editor/Unity')
arguments=[binary,*[arg for arg in sys.argv[1:] if arg not in ('-batchmode','-nographics')]]
if os.environ.get('GAMECORE_P42C_STAGE_SUBMIT')=='1':
    # The outer batch wrapper already owns this Editor's reservation. Keep the
    # allocator mutex until its UI process exits so the submitted service job
    # cannot allocate sandbox Unity concurrently with the creator Editor.
    import fcntl
    import subprocess
    import signal
    with (Path.home()/'wkspace/gc-studio/.unity-slots/allocator.lock').open('a') as barrier:
        fcntl.flock(barrier,fcntl.LOCK_EX)
        child=subprocess.Popen(arguments,close_fds=True)
        def terminate(signum,frame):
            child.send_signal(signum)
        for signum in (signal.SIGTERM,signal.SIGINT,signal.SIGHUP):signal.signal(signum,terminate)
        result=child.wait()
        fcntl.flock(barrier,fcntl.LOCK_UN)
        raise SystemExit(result)
os.execv(binary,arguments)
