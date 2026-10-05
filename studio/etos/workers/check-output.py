#!/usr/bin/env python3
"""Offline worker schema self-check. This is not a stage verdict or admission authority."""
import argparse
import copy
import json
from pathlib import Path
import re
import sys

from jsonschema import Draft202012Validator


def read_json(path):
    def unique(pairs):
        obj = {}
        for key, value in pairs:
            if key in obj:
                raise ValueError('duplicate JSON member')
            obj[key] = value
        return obj
    return json.loads(path.read_text(), object_pairs_hook=unique,
                      parse_constant=lambda _: (_ for _ in ()).throw(ValueError('non-finite JSON number')))


def request_contract(inputs):
    selection = read_json(inputs / 'selection.json')
    if (inputs / 'request.json').is_file():
        request = read_json(inputs / 'request.json')
        return request['changeSetId'], request['intent'], selection
    # Current companion Desk::request_md wire shape. Never take intent from the
    # candidate's description, which is precisely the retained W-AI-03 defect.
    text = (inputs / 'request.md').read_text()
    identity = re.match(r'# GameCore Studio request `(cs_[0-7][0-9A-HJKMNP-TV-Z]{25})`\n', text)
    prefix, separator, _ = text.rpartition('\n## Inputs (`/inputs`)\n')
    _, intent_marker, intent = prefix.partition('\n## Intent\n\n')
    if not identity or not separator or not intent_marker or not intent.strip():
        raise ValueError('request.md contract unavailable; cannot reconstruct intent')
    return identity[1], {'origin': 'agent', 'text': intent.strip()}, selection


def validate(candidate, schema, identity, intent, selection):
    errors = []
    for error in Draft202012Validator(schema).iter_errors(candidate):
        # Paths/keywords are sufficient; do not echo candidate values into logs.
        path = '/' + '/'.join(str(part) for part in error.absolute_path)
        errors.append(f'schema_violation {path}: {error.validator}')
    if not isinstance(candidate, dict):
        return errors
    if candidate.get('id') != identity:
        errors.append('request_mismatch /id')
    if candidate.get('intent') != intent:
        errors.append('request_mismatch /intent')
    if candidate.get('selection') != selection:
        errors.append('request_mismatch /selection')
    for member in ('state', 'outcomes'):
        if member in candidate:
            errors.append('candidate_lifecycle /' + member)
    for parent, member in (('timestamps', 'applied'), ('links', 'gameCoreOps')):
        if isinstance(candidate.get(parent), dict) and member in candidate[parent]:
            errors.append(f'candidate_lifecycle /{parent}/{member}')

    def nulls(value, path=''):
        if value is None:
            errors.append('null_forbidden ' + (path or '/'))
        elif isinstance(value, dict):
            for key, child in value.items():
                nulls(child, path + '/' + key)
        elif isinstance(value, list):
            for index, child in enumerate(value):
                nulls(child, path + '/' + str(index))
    nulls(candidate)
    return sorted(set(errors))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--inputs', type=Path, required=True)
    parser.add_argument('--outputs', type=Path, required=True)
    parser.add_argument('--repair-intent', action='store_true',
                        help='copy request intent verbatim; write only after full validation passes')
    args = parser.parse_args()
    try:
        path = args.outputs / 'changeset.json'
        if path.is_symlink():
            raise ValueError('changeset output must be a regular file')
        candidate = read_json(path)
        identity, intent, selection = request_contract(args.inputs)
        schema = read_json(Path(__file__).parent / 'schemas/change-set.schema.json')
        checked = copy.deepcopy(candidate)
        if args.repair_intent and isinstance(checked, dict):
            checked['intent'] = copy.deepcopy(intent)
        errors = validate(checked, schema, identity, intent, selection)
        if errors:
            print('\n'.join(errors))
            return 1
        if checked != candidate:
            # Same-directory replace; invalid candidates are never partially rewritten.
            import tempfile
            with tempfile.NamedTemporaryFile(mode='w', dir=path.parent, delete=False) as temporary:
                json.dump(checked, temporary, ensure_ascii=False, indent=2, allow_nan=False)
                temporary.write('\n')
            Path(temporary.name).replace(path)
        print('worker_schema_check: PASS (schema, request identity/intent/selection, candidate lifecycle, nulls)')
        return 0
    except (OSError, ValueError, KeyError, TypeError):
        print('worker_schema_check: FAIL (missing or invalid input/output contract)')
        return 1


if __name__ == '__main__':
    sys.exit(main())
