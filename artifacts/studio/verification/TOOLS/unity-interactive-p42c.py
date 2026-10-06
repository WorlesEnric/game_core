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
os.execv(binary,[binary,*[arg for arg in sys.argv[1:] if arg not in ('-batchmode','-nographics')]])
