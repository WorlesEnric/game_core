"""R6-B witnesses and provider identity, offline only."""
import importlib.util
import json
from pathlib import Path
import tomllib
import pytest
from jsonschema import Draft202012Validator

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
    spec = importlib.util.spec_from_file_location('r6_install_state', ROOT / 'studio/etos/install-state.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    placeholder = (WORKERS / 'tests/fixtures/describe-placeholder.toml').read_text()
    with pytest.raises(ValueError, match='tariff_placeholder'):
        module.tariffs(placeholder, 'describe')
    filled = placeholder.replace('per_unit = 0.0 # SET_BY_OPERATOR', 'per_unit = 0.03').replace(
        'SET_BY_OPERATOR: total USD estimate per describe call', 'Offline test operator estimate')
    tariff, = module.tariffs(filled, 'describe')
    assert (tariff['provider'], tariff['model'], tariff['unit'], tariff['source'], tariff['per_unit']) == (
        provider['name'], model['id'], 'call', 'operator', 0.03)
    assert 'url' not in tariff


def test_R6_C_ferryman_prompt_answer_carries_patrol_and_dialogue_prerequisites():
    instruction = (WORKERS / 'gc-designer.md').read_text()
    contract = instruction.split('## NPC patrol and dialogue prerequisites (W-AI-02)', 1)[1]
    rules, example = contract.split('### Ferryman answer example', 1)
    for prerequisite in ['npc.setPatrol', 'npc.setDialogue', 'NavMeshAgent',
                         'needs-clarification', 'entry-reachable', 'roster']:
        assert prerequisite in rules
    answer = json.loads(example.split('```json\n', 1)[1].split('```', 1)[0])
    operations = answer['operations']
    schema = json.loads((ROOT / 'docs/studio/schemas/change-set.schema.json').read_text())
    operation_validator = Draft202012Validator({
        '$defs': schema['$defs'], '$ref': '#/$defs/Operation'})
    reference_validator = Draft202012Validator({
        '$defs': schema['$defs'], '$ref': '#/$defs/AuthoringRef'})
    for operation in operations:
        operation_validator.validate(operation)
    patrol, = [op for op in operations if op['tool'] == 'npc.setPatrol']
    dialogue, = [op for op in operations if op['tool'] == 'npc.setDialogue']
    line, = [op for op in operations if op['tool'] == 'dialogue.addLine']
    reference_validator.validate(dialogue['args']['graph'])
    # NpcTools.SetPatrol takes vector3[] (numeric [x,y,z] arrays), wait 0..600 s.
    Draft202012Validator({
        'type': 'object', 'required': ['points'], 'additionalProperties': False,
        'properties': {
            'points': {'type': 'array', 'minItems': 2, 'items': {
                'type': 'array', 'minItems': 3, 'maxItems': 3,
                'items': {'type': 'number'}}},
            'waitSeconds': {'type': 'number', 'minimum': 0, 'maximum': 600},
        },
    }).validate(patrol['args'])
    # DialogueTools.AddLine accepts text/speaker strings and an optional after index.
    Draft202012Validator({
        'type': 'object', 'required': ['text'], 'additionalProperties': False,
        'properties': {
            'text': {'type': 'string', 'minLength': 1},
            'speaker': {'type': 'string'},
            'after': {'type': 'integer', 'minimum': -1},
        },
    }).validate(line['args'])
    assert set(dialogue['args']) == {'graph'}
    points = patrol['args']['points']
    assert len({(point[0], point[2]) for point in points}) >= 2
    assert patrol['target'] == dialogue['target']
    assert dialogue['args']['graph'] == line['target']
    assert 'bell' in line['args']['text'].lower()
    assert line['opId'] in dialogue['dependsOn']
    emitted = set()
    for operation in operations:
        assert set(operation['dependsOn']) <= emitted
        emitted.add(operation['opId'])
