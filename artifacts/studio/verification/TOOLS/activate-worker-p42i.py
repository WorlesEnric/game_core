#!/usr/bin/env python3
"""Activate the exact P4.2i product with the existing immutable installer."""
import importlib.util
from pathlib import Path
import sys

spec = importlib.util.spec_from_file_location('activation', Path(__file__).with_name('activate-worker-p42g.py'))
activation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(activation)
activation.BASELINE = 'cb5e2aa20263209df2dea4ee17aa23c50daec0e0'
activation.LIVE = activation.ROOT
activation.STATE = activation.ROOT / 'artifacts/studio/workflows/P4.2i'
if __name__ == '__main__':
    sys.dont_write_bytecode = True
    activation.main()
