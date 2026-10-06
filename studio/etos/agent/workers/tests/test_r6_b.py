"""R6-B witnesses and provider identity, offline only."""
import importlib.util
import json
from pathlib import Path
import tomllib
import pytest

WORKERS = Path(__file__).resolve().parents[1]
ROOT = WORKERS.parents[3]


def test_R6_Request6_worker_retains_original_and_requires_relink_or_clarification():
    original = ROOT / 'artifacts/studio/verification/W-AI-02/p42d-text2-20261006T113349.342620Z/workflow/ferryman2/candidate.json'
    retained = WORKERS / 'tests/fixtures/request6-original.json'
    assert retained.read_bytes() == original.read_bytes()
    operations = json.loads(retained.read_text())['operations']
    assert operations[2]['args'] == {'field': 'entry', 'value': 8}
    instruction = (WORKERS / 'gc-designer.md').read_text()
    for clause in ['GP-DLG-005', '"unreachable"', 'Re-link', 'needs-clarification', 'Never change entry']:
        assert clause in instruction


def test_R6_Request7_describe_alias_is_echo_and_cannot_inherit_dashscope_tariff():
    models = tomllib.loads((ROOT / 'studio/etos/models.toml.tmpl').read_text())
    model, = [m for m in models['models'] if 'describe' in m.get('aliases', [])]
    text = (ROOT / 'studio/etos/ops.toml.tmpl').read_text()
    provider, = [p for p in tomllib.loads(text)['providers'] if p['family'] == 'describe']
    assert model['id'] == provider['model'] == 'echo/gpt-5.6-sol'
    assert models['endpoints']['echo']['base_url'] == 'https://api.echo-coding.com/v1'
    assert 'A DashScope list price cannot' in text
    spec = importlib.util.spec_from_file_location('r6_install_state', ROOT / 'studio/etos/install-state.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    with pytest.raises(ValueError, match='tariff_placeholder'):
        module.tariffs(text, 'describe')
