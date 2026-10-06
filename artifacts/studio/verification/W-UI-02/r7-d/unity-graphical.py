#!/usr/bin/env python3
"""Use the commissioned :1 graphical surface under unity-batch's host allocator."""
import os
from pathlib import Path
import sys
binary = str(Path.home() / 'Unity/Hub/Editor/6000.0.75f1/Editor/Unity')
os.execv(binary, [binary, *[arg for arg in sys.argv[1:] if arg not in ('-batchmode', '-nographics')]])
