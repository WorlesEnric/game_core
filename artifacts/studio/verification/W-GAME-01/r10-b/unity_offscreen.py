#!/usr/bin/env python3
"""Keep unity-batch allocation/redaction; replace only Null graphics with private llvmpipe."""
import os
from pathlib import Path
import sys

unity = Path.home() / 'Unity/Hub/Editor/6000.0.75f1/Editor/Unity'
os.environ.pop('DISPLAY', None)
os.environ.update(LIBGL_ALWAYS_SOFTWARE='1', GALLIUM_DRIVER='llvmpipe', LP_NUM_THREADS='4')
args = [arg for arg in sys.argv[1:] if arg != '-nographics']
os.execv('/usr/bin/xvfb-run', ['xvfb-run', '-a', '-s', '-screen 0 1920x1080x24', str(unity), '-force-glcore'] + args)
