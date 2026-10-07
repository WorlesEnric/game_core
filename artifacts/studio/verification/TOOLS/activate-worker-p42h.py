#!/usr/bin/env python3
"""Activate the current-main immutable release using the reviewed R6-F installer."""
import importlib.util
from pathlib import Path
import sys

spec = importlib.util.spec_from_file_location('activation', Path(__file__).with_name('activate-worker-p42g.py'))
activation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(activation)
activation.BASELINE = 'a77cb38ba4a2265007fa40c38983e01a17bb0914'
activation.LIVE = activation.ROOT
activation.STATE = activation.ROOT / 'artifacts/studio/workflows/P4.2h'
if __name__ == '__main__':
    sys.dont_write_bytecode = True
    activation.main()
