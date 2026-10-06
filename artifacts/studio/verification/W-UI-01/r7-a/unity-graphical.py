#!/usr/bin/env python3
"""Keep unity-batch allocation/redaction; run its Editor on the owned display."""
import os
from pathlib import Path
import sys

for suffix in ('WORKFLOW', 'OUT'):
    value = os.environ.get('GAMECORE_R7_' + suffix)
    if value:
        os.environ['GCS_P32_' + suffix] = value

binary = str(Path.home() / 'Unity/Hub/Editor/6000.0.75f1/Editor/Unity')
os.execv(binary, [binary, *[arg for arg in sys.argv[1:] if arg not in ('-batchmode', '-nographics')]])
