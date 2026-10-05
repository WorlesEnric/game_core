# ADAPT-SPLIT host evidence

Implementation, findings and cross-packet requests:
[ADAPT-SPLIT packet note](../../../../docs/studio/packets/ADAPT-SPLIT.md).

## Host Unity commands

Each command used `bash studio/tools/unity-batch.sh --project "$PWD/games/<game>"`
with `--log-dir "$PWD/.unity-logs"`, a unique `--label`, and `--results` pointing
to the corresponding XML in this directory. Extra arguments after `--`:

```text
hollowmere: -runTests -testPlatform EditMode -testFilter 'Hollowmere\.R2_G.*|Hollowmere\.R2_B.*|Hollowmere\.P1_7b.*|Hollowmere\.P2_4.*|GameCore\.Studio\..*|Hollowmere\.P1_1.*'
hollowmere: -runTests -testPlatform PlayMode -testFilter 'Hollowmere\.R2_G.*|Hollowmere\.P3_1.*'
cleanproof: -runTests -testPlatform EditMode -testFilter 'Saltmarsh\..*'
cleanproof: -runTests -testPlatform PlayMode -testFilter 'Saltmarsh\..*'
hollowmere: -runTests -testPlatform PlayMode -testFilter 'Hollowmere\.P1_1.*'
```

The broad Hollowmere EditMode command was repeated after cleanproof resolved its
lock; both result XMLs are retained. Cleanproof's Library was seeded with
`cp -a --reflink=auto games/hollowmere/Library games/cleanproof/Library` after the
Hollowmere Editor exited. All runs held at most one shared host reservation.

## Reproduce the confined acceptance

From this checkout on myubuntu, with no other Editor held by this packet:

```bash
export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH"
cargo build --manifest-path studio/agent/Cargo.toml
stage_cache=$(studio/agent/target/debug/gamecore-studio stage cache-path \
  --repo "$PWD" --source-project "$PWD/games/hollowmere" \
  --root "$HOME/.cache/gamecore-studio/adapt-split/stage")
bash studio/stage/provision-cache.sh "$stage_cache" \
  --unity-library "$HOME/.cache/gamecore-studio/stage/_warm/Library" \
  --upm-from "$HOME/.cache/Unity/upm"
bash studio/stage/provision-cache.sh "$stage_cache" --verify
harness_manifest=$(python3 studio/agent/evidence/adapt-split/prepare-harness.py)
CARGO_TARGET_DIR="$PWD/studio/agent/target" cargo test --offline \
  --manifest-path "$harness_manifest" --test stage_int -- --ignored --nocapture
python3 studio/agent/evidence/adapt-split/capture-stage.py
python3 studio/agent/evidence/adapt-split/summarize.py
```

`prepare-harness.py` copies the committed STAGE-INT test and fake-node support to a
private temporary crate; it changes only repo, binary, scratch-root and evidence paths.
The source Cargo lock is copied to retain dependency pins. It does not modify
`studio/agent/tests/stage_int.rs`, STAGE-INT evidence, shared STAGE-INT state, or any
sibling clone. The real companion/production Docker stage uses this checkout, a
scratch node and synthetic authentication. No live node or paid operation is used.
The test itself fetches and verifies the signed verdict through authenticated routes.

## Left open

Default Docker acceptance is **BLOCKED** before candidate execution:
`Sandbox::command` exports the full slot path as TMPDIR, causing Bee to create a
203-character Unix-domain socket path (maximum 108). The exact outside-path fix
request is in the linked packet note. The authenticated verdict endpoint returned
404; there is no StageVerdict or candidate test XML and no warm B-STAGE measurement.
`stage-timing.json` distinguishes unavailable verdict fields from the measured
service duration. The prepared pressure-plate closure has no Studio dependency.

The baseline `5eefd3d4` contains no P3.1 admission bootstrap, Authoring Editor asmdef,
or `P31AdmissionInPlayMode` test. The exact requested Hollowmere PlayMode filter
selects zero cases and is retained as NotRun, not as a pass. The game owner must
integrate that bootstrap/test and add `GameCore.Studio.Gameplay.Editor` to its Editor
asmdef. The moved public APIs and namespaces are unchanged.

Final XML counts and Docker disposition are recorded in the linked packet note and
`test-summary.json`; initial failures remain separate evidence, never final passes.
