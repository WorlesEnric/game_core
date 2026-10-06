"""Offline retained worker contract and tariff regressions. No node calls."""
import importlib.util
import json
from pathlib import Path
import pytest
from jsonschema import Draft202012Validator

WORKERS = Path(__file__).resolve().parents[1]
ROOT = WORKERS.parents[3]


def test_request8_retained_candidate_refuses_inspector_label():
    original = ROOT / "artifacts/studio/workflows/P4.2c/guide-image-ledger-candidate.json"
    retained = WORKERS / "tests/fixtures/request8-original.json"
    assert retained.read_bytes() == original.read_bytes()
    schema = json.loads((WORKERS / "importer-contract.json").read_text())
    importer = json.loads(retained.read_text())["candidate"]["operations"][0]["args"]["importer"]
    errors = list(Draft202012Validator(schema).iter_errors(importer))
    assert len(errors) == 1 and errors[0].validator == "enum"
    # Independent valid example; the retained failed candidate is never patched.
    Draft202012Validator(schema).validate({"textureType": "Sprite", "spriteImportMode": "Single"})


def test_request8_installed_prompts_embed_common_contract():
    common = (WORKERS / "common.md").read_text().strip()
    for name in ["gc-designer.md", "gc-mechanic.md"]:
        assert common in (WORKERS / name).read_text()


def test_describe_tariff_placeholder_refuses_before_apply():
    spec = importlib.util.spec_from_file_location("install_state", ROOT / "studio/etos/install-state.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    text = (WORKERS / "tests/fixtures/describe-placeholder.toml").read_text()
    with pytest.raises(ValueError, match="tariff_placeholder"):
        module.tariffs(text, "describe")
    # The explicit operator seam binds the provider/model and unit, without a live apply.
    filled = text.replace('per_unit = 0.0 # SET_BY_OPERATOR', 'per_unit = 0.03').replace('SET_BY_OPERATOR: total USD estimate per describe call', 'Offline test operator estimate')
    tariff, = module.tariffs(filled, "describe")
    assert (tariff["source"], tariff["provider"], tariff["model"], tariff["unit"]) == ("operator", "echo-describe", "echo/gpt-5.6-sol", "call")
    assert tariff["per_unit"] == 0.03
