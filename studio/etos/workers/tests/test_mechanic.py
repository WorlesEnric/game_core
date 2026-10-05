"""Offline output harness: replay retained request/diagnostics, never call a model."""
import copy
import json
from pathlib import Path
import subprocess
import sys

from jsonschema import Draft202012Validator
import pytest

WORKERS = Path(__file__).resolve().parents[1]
ROOT = WORKERS.parents[2]
RUNS = ROOT / 'artifacts/studio/workflows/P3.2/runs'


def test_W_AI_03_schema_matches_companion_and_contract():
    schema = (WORKERS / 'schemas/change-set.schema.json').read_bytes()
    assert schema == (ROOT / 'docs/studio/schemas/change-set.schema.json').read_bytes()
    assert schema == (ROOT / 'studio/agent/schemas/change-set.schema.json').read_bytes()


@pytest.fixture
def replay(tmp_path):
    run = RUNS / 'mech-a-20261005T113219Z/mech'
    request = json.loads((run / 'request.json').read_text())
    diagnostics = json.loads((run / 'outcome.json').read_text())['row']['diagnostics']
    assert any("'description' was unexpected" in d['message'] for d in diagnostics)
    assert any('"origin" is a required property' in d['message'] for d in diagnostics)
    assert any('"text" is a required property' in d['message'] for d in diagnostics)
    inputs, outputs = tmp_path / 'inputs', tmp_path / 'outputs'
    inputs.mkdir(); outputs.mkdir()
    (inputs / 'request.json').write_text(json.dumps(request))
    (inputs / 'selection.json').write_text(json.dumps(request['selection']))
    # The raw rejected candidate/package were not retained. Reconstruct only the
    # envelope defect proven by diagnostics; these are fixture package bytes.
    candidate = {'schema': 'gamecore.studio.changeset/1', 'id': request['changeSetId'],
                 'intent': {'description': request['intent']['text']},
                 'selection': request['selection'],
                 'operations': [{'opId': 'propose', 'tool': 'mechanism.propose',
                                 'args': {}, 'applyRequirement': 'Compile'}],
                 'requirements': {'max': 'Compile', 'compile': True, 'worldRebuild': False, 'build': False}}
    validator = Draft202012Validator(json.loads((WORKERS / 'schemas/change-set.schema.json').read_text()))
    assert len(list(validator.iter_errors(candidate))) == 3
    (outputs / 'changeset.json').write_text(json.dumps(candidate))

    def check(repair=False):
        return subprocess.run([sys.executable, str(WORKERS / 'check-output.py'),
                               '--inputs', str(inputs), '--outputs', str(outputs),
                               *(['--repair-intent'] if repair else [])], text=True, capture_output=True)
    return inputs, outputs, request, candidate, validator, check


def test_W_AI_03_retained_intent_is_repaired_then_full_schema_checked(replay):
    _, outputs, request, _, validator, check = replay
    before = check()
    assert before.returncode == 1 and '/intent' in before.stdout
    after = check(repair=True)
    assert after.returncode == 0, after.stdout + after.stderr
    candidate = json.loads((outputs / 'changeset.json').read_text())
    assert candidate['intent'] == request['intent']
    assert candidate['selection'] == request['selection']
    validator.validate(candidate)
    assert check().returncode == 0


@pytest.mark.parametrize('mutation', ['id', 'null', 'state', 'unknown', 'selection'])
def test_W_AI_03_self_check_does_not_hide_other_defects(replay, mutation):
    _, outputs, request, candidate, _, check = replay
    candidate = copy.deepcopy(candidate)
    if mutation == 'id': candidate['id'] = 'cs_01M45XSNC3BE2T6P9Z28C5NG2T'
    if mutation == 'null': candidate['operations'][0]['args']['value'] = None
    if mutation == 'state': candidate['state'] = 'Applied'
    if mutation == 'unknown': candidate['operations'][0]['description'] = 'wrong'
    if mutation == 'selection': candidate['selection']['indexRevision'] += 1
    path = outputs / 'changeset.json'
    original = json.dumps(candidate)
    path.write_text(original)
    result = check(repair=True)
    assert result.returncode == 1
    assert path.read_text() == original  # no partial rewrite on validation failure


def test_W_AI_03_current_request_markdown_input(replay):
    inputs, outputs, request, _, _, check = replay
    (inputs / 'request.json').unlink()
    (inputs / 'request.md').write_text(
        f"# GameCore Studio request `{request['changeSetId']}`\n\n"
        f"## Intent\n\n{request['intent']['text']}\n\n## Inputs (`/inputs`)\n\n"
        "- `selection.json`\n\n## Output contract\n")
    result = check(repair=True)
    assert result.returncode == 0, result.stdout + result.stderr
    assert json.loads((outputs / 'changeset.json').read_text())['intent'] == request['intent']


def test_W_AI_03_prompt_requires_self_check():
    text = (WORKERS / 'gc-mechanic.md').read_text()
    assert '"intent": {"origin": "agent", "text":' in text
    assert 'check-output.py --inputs /inputs --outputs /outputs --repair-intent' in text
    assert 'Do not answer until' in text


def test_W_AI_03_fallbacks_are_not_worker_successes():
    for run in ('mech-b-20261005T120918Z', 'mech-b-20261005T124205Z'):
        verdict = json.loads((RUNS / run / 'mech/verdict.json').read_text())
        assert verdict['canAdmit'] is False and verdict['verified'] is False
