# v1-release-evidence-resume

Verdict: **PASS**.

Source revision: `c532b77edd93099ddd1e45d20f48db5d627413b3`; host: `worlesenric`.
Started: 2026-10-05T20:03:54.199876+00:00; ended: 2026-10-05T20:10:45.216008+00:00; duration: 411.833 s.

Command (from repository root unless cwd specified):

```sh
bash -c 'set -euo pipefail
mkdir -p "$ARTIFACTS/toolchain"
cp "$P42_PHASE1/toolchain/probe-replay.json" "$ARTIFACTS/toolchain/probe-replay.json"
sha256sum "$P42_PHASE1/toolchain/probe-replay.json" "$ARTIFACTS/toolchain/probe-replay.json"
bash artifacts/studio/verification/TOOLS/v1-resume-release.sh'
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
