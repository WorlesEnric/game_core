# R3-E mechanic output contract

## R3 fixes

The brief calls this W-AI-03; P3.2 uses W-AI-03 for dialogue and records this
mechanism attempt separately. Evidence is
`artifacts/studio/workflows/P3.2/runs/mech-a-20261005T113219Z/mech/`:
`request.json` carries `{intent:{origin:"agent",text:"Add a pressure plate ..."}}`;
`outcome.json` rejects `description` and requires `origin`/`text` at `/intent`.
Both task summaries report Rules success, ending at 12/12, without a valid candidate.

- Prompt: concrete intent shape, copied text, mandatory self-check before answering,
  explicit D1/D2 code/admission boundaries.
- Post-processor: `check-output.py --inputs DIR --outputs DIR [--repair-intent]`.
  Exit 0 means local schema/request/lifecycle validation only; exit 1 means refusal.
  `--repair-intent` copies only the authoritative input request's intent, validates
  the entire result, then replaces changeset.json atomically. Other defects are not
  silently repaired. It never runs a model or emits a stage verdict.
- Inputs: authoritative `request.json` (`changeSetId`, `intent`) when provided, plus
  `selection.json`. Otherwise reads the current companion `request.md` id/Intent
  section and uses agent origin, as the current Markdown contract omits origin.
- Schema: exact copy of `docs/studio/schemas/change-set.schema.json`, checked against
  that and `studio/agent/schemas/change-set.schema.json` by the offline tests.
- Packaging: Docker layer over the existing mechanic image installs jsonschema,
  checker, schema and prompt. The allowed host image builder consumes this layer.

## Reproduction and tests

`python3 -m pytest -q studio/etos/workers/tests` (pytest + jsonschema required).
`test_W_AI_03_retained_intent_is_repaired_then_full_schema_checked` proves the exact
three schema errors then repair and full schema acceptance with the retained task
id/selection/intent. `test_W_AI_03_self_check_does_not_hide_other_defects` has five
negative variants. Other tests cover current Markdown inputs, required prompt gate,
schema-copy drift and the two mech-b refused stage records. Initial test run: 8
failures, 2 passes. Final local run: 10 passes (offline, no model).

The Docker layer also built successfully on myubuntu as
`localhost/gc-mechanic-r3-e:test` (isolated verification tag). Four checks in that
image with `--network none` pass: reconstructed retained rejection (exit 1),
intent repair (exit 0), idempotent check (exit 0), unrelated null defect (exit 1).
The request was bind-mounted read-only; outputs were a temporary fixture folder.
Production image tags, workers and installed services were not changed.

The self-check is not a replacement for companion catalog, artifact integrity,
semantic scan, sandbox execution, signing or explicit creator Admit.

## Requests to other packets

R3-C owns the active `studio/etos/agent/workers/gc-mechanic.md` and
`studio/etos/agent/agent.toml` (worker `instructions = "workers/gc-mechanic.md"`).
Copy this packet's complete `gc-mechanic.md` into that active path, or package it
there via the manifest's supported instruction-file mechanism. The required command
is `python3 /opt/gamecore-worker/check-output.py --inputs /inputs --outputs /outputs --repair-intent`;
nonzero exit must prevent a successful answer. The image build supplies the command
without changing service configuration. No files outside R3-E's set were changed.

## Left open

- Active prompt deployment is the R3-C seam above; this packet does not claim its
  new directory is already referenced by the existing manifest.
- The exact rejected changeset and generated package were not retained. Tests
  reconstruct the envelope error from retained diagnostics, using the retained
  request; no model-generation or 12-test package rerun is claimed.
- The Markdown request contains no original intent origin/transcript metadata.
  If preserving voice origin is required, the companion owner should include
  `request.json` with `changeSetId` and the exact `intent`; the checker already
  consumes that form without changing its CLI.
- Retained mech-b runs have `panel-stage.json` and `verdict.json`, not outcome.json:
  run 120918Z reports `stage_service_unavailable` after ILPP failure; 124205Z reports
  `not_found` for the sample candidate. Those stage seams belong to other packets.
