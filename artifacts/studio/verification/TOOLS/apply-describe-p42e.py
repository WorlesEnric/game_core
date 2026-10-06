#!/usr/bin/env python3
"""Scoped adapter for main's missing --only describe CLI choice; use its public function."""
import importlib.util
from pathlib import Path
root = Path(__file__).resolve().parents[4]
spec = importlib.util.spec_from_file_location('installed_prices', root / 'studio/etos/install-state.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
module.apply_prices(Path.home() / '.local/share/etos-studio', only='describe')
