# GameCore Studio install, build and run

Final runbook as of P4.2d (2026-10-06). Commands below are checked against the merged script headers/source; they are reproduction instructions, not new live qualification. Current acceptance is **33 PASS / 29 BLOCKED / 6 FAIL**. ([SUMMARY](../../artifacts/studio/verification/SUMMARY.md), [P4.2d](packets/P4.2d-live-rerun.md))

## Supported host and pins

| Component | Required profile | Source |
|---|---|---|
| Host | Linux myubuntu; all builds/tests here; one Editor per packet, at most three host-wide | [unity-batch.sh header](../../studio/tools/unity-batch.sh) |
| Unity | 6000.0.75f1 with Linux IL2CPP; Editor Mono; domain/scene reload on | [P0.2 §1](packets/P0.2-projects-tooling.md) |
| Game packages | Entities 1.4.6, Burst 1.8.28, Collections 2.6.6, Mathematics 1.3.2; URP 17.0.4, Input System 1.19.0, AI Navigation 2.0.12 | [Hollowmere manifest](../../games/hollowmere/Packages/manifest.json) |
| Tools | .NET 8 on `~/.dotnet`, Rust 1.97.1 on `~/.cargo/bin`, Docker, Python 3, Bash 4+ | [P0.1](packets/P0.1-host-etos.md), [unity-compile.sh](../../studio/tools/unity-compile.sh) |
| ETOS | **etos main ≥ e4067fd (contains 278ef9c)**; upstream merge/push dated 2026-10-06 by owner | [P4.3-final §Baseline](packets/P4.3-final-docs.md#baseline) |
| Retained build provenance | `etos.lock` records the original 278ef9c binaries and digests; do not hand-edit it to imply a new build | [machine-written lock](../../studio/etos/etos.lock) |

Graphical game qualification is distinct from the V1 headless kernel profile. RTX 4060 Ti 1080p frame timing passes with VSync off and fails the literal p95 rule with VSync on; the owner has not selected the measurement rule. ([07 §5](07-verification-matrix.md#5-b-frame-and-recording-clarification))

## Host installation and pairing

The dedicated Studio node root is `~/.local/share/etos-studio`, API `127.0.0.1:7410`, broker `172.17.0.1:7411`, web UI `127.0.0.1:7400`; binaries live in `~/.local/opt/etos/bin`. The existing user owns `etosd.service`. Provider credentials remain in the external environment file; the paired app key is `~/.config/gamecore-studio/app-key.json`. Never print either. ([P0.1 §Host](packets/P0.1-host-etos.md#host-myubuntu-user-worlesenric), [install.sh header](../../studio/etos/install.sh))

For a **new authorized host installation**, after the ETOS source is at the required revision:

```sh
studio/tools/host-build-etos.sh
studio/etos/install.sh --project "$PWD/games/hollowmere"
```

`host-build-etos.sh` builds and writes provenance; `install.sh [--skip-images] [--project <path>]` provisions node configuration, images, workers, immutable companion and pairing. It can restart services and is not a read-only health check. Under this packet's no-restart rule, do not run it on the existing shared installation. `studio/etos/verify.sh` also performs paid operations; it is not an offline test. ([script headers](../../studio/tools/host-build-etos.sh), [installer](../../studio/etos/install.sh), [verify.sh](../../studio/etos/verify.sh))

The companion manifest uses `bin/gamecore-studio`; etos rejects parent-directory traversal in its command. Current releases copy the binary and verify checksums; the older development symlink instructions are historical. P4.2d activated `0.1.0-e8a72b2d6eb3aad9`. Workers use `echo/gpt-6-sol` in offline containers because Echo Claude access was revoked. ([agent manifest](../../studio/etos/agent/agent.toml), [P4.2d §Host, installation and authority](packets/P4.2d-live-rerun.md#host-installation-and-authority))

A new app pairing, when separately needed by an operator, uses:

```sh
export ETOS_ROOT="$HOME/.local/share/etos-studio"
export PATH="$HOME/.local/opt/etos/bin:$PATH"
etos app install studio/etos/app
etos app pair gamecore-unity --approve --out ~/.config/gamecore-studio/app-key.json
```

In Unity choose **Project Settings → GameCore Studio → ETOS**, set **App key file** and **Node URL**, **Save and restart session**, then **Test connection**. Settings hold a file path, not the key. Batch clients need explicit `GAMECORE_ETOS_AUTOSTART=1`. Verify hello contract/revision and provider tariffs before a live operation. ([P2.2 §Decisions](packets/P2.2-studio-etos-client.md#decisions-and-deviations), [R2-D §R4](packets/R2-D-studio-etos-client.md))

Stage registration binds a stable project SHA-256 from trusted Editor context to the exact source checkout. The secret-free command is `studio/etos/install.sh --register-project <project-sha256> <absolute-project-path>`. It writes the operator map; activation of changed process configuration remains a separate authorized operator action. Never substitute another clone's paths or a candidate-supplied path. ([install-state.py register](../../studio/etos/install-state.py#L43), [P4.2d §Stage and admission](packets/P4.2d-live-rerun.md#stage-and-admission))

Image tariff is an operator USD 0.20 estimate and TTS has a published per-character tariff. Describe remains unpriced as of P4.2d. The requested `--apply-prices --only describe` is not supported by this baseline parser (`choices=["tts"]`); the exact code request is O59 in the packet. Do not invent a price or run the unpriced call. ([04 §Tariffs](04-etos-integration.md#tariffs-and-budgets), [install-state.py](../../studio/etos/install-state.py#L236), [P4.2d §Ledger and caps](packets/P4.2d-live-rerun.md#ledger-and-caps))

## Unity project setup

Local package paths in `games/<name>/Packages/manifest.json` are `file:../../../Packages/<package>`. Unity must resolve dependency changes on the host and the resulting lock must be committed. All present locks must agree with exact asmdef-derived dependencies; both Hollowmere and Saltmarsh have locks. Studio is Editor-only, while gameplay uses mirror metadata and independent runtime dependencies. ([package rules §9](../operator/packages.md#9-studio-gameplay-and-games-projects-sadr-014), [ADAPT-SPLIT](packets/ADAPT-SPLIT.md))

Open `games/hollowmere`, then `Assets/Hollowmere/Boot/Boot.unity`, **GameCore/Studio/Open Studio**. Enter Unity Play Mode and start the game from its main menu before testing walking. The viewport's Play button changes input mode; it does not create a second world or pump. Use a reachable graphical display for UI work; [evidence-p2.1.sh](../../studio/tools/evidence-p2.1.sh) and [evidence-p2.3.sh](../../studio/tools/evidence-p2.3.sh) launch through the host allocator. ([08](08-creator-guide.md), [P1.7a §Player walk-speed regression](packets/P1.7a-gameplay-hardening-runtime.md))

## Saltmarsh and new-project walkthrough

Create a content-free new project from the committed template:

```sh
studio/tools/new-project.sh games/coast-demo CoastDemo
bash studio/tools/unity-batch.sh --project "$PWD/games/coast-demo" \
  --log-dir "$PWD/.unity-logs/coast-demo" --label setup -- \
  -quit -executeMethod GameCoreProjectSetup.Configure
```

Existing files are preserved; no Hollowmere Library, lock or content is copied. Configure supplies URP/Input/NavMesh defaults, IL2CPP and Boot/build settings. Add game-owned definitions, region scenes, boot composition, codecs and stripping roots, then author/bake before a separate import/test invocation. ([new-project.sh header](../../studio/tools/new-project.sh), [P4.1 §Scope](packets/P4.1-clean-proof.md#scope))

For the **committed Saltmarsh example**, use a disposable clone, open `games/cleanproof`, and reproduce its five journaled authoring phases, then build in a separate Editor:

```sh
bash studio/tools/unity-batch.sh --project "$PWD/games/cleanproof" \
  --log-dir "$PWD/.unity-logs/saltmarsh" --label author -- \
  -quit -executeMethod Saltmarsh.Authoring.SaltmarshAuthoring.AuthorAll
bash games/cleanproof/Tools/build.sh
bash games/cleanproof/Tools/run-headless.sh
```

The run wrapper selects `SALTMARSH_DISPLAY` (default `:1`) and X11. Use a private reachable display for headless work; the built executable is `games/cleanproof/Builds/Linux/Saltmarsh.x86_64`. Its 600-frame quest/save/restore/ending proof and package-empty diff pass in P4.2. The neutral scaffold alone does not create a complete game. ([P4.1 §Qualification progress](packets/P4.1-clean-proof.md#qualification-progress), [build.sh](../../games/cleanproof/Tools/build.sh), [run-headless.sh](../../games/cleanproof/Tools/run-headless.sh), [W-CLEAN-01](../../artifacts/studio/verification/W-CLEAN-01/README.md))

## Exact runner interfaces

| Script | Interface / behavior | Source |
|---|---|---|
| unity-compile.sh | `<packet> <project-rel-path> [--tests EditMode\|PlayMode] [--filter <regex>] [--require-test <fullname>]`; runs the packet clone, delegates to unity-batch | [header](../../studio/tools/unity-compile.sh) |
| unity-batch.sh | `--project <abs> --log-dir <dir> --label <name> [--results <xml>] [--timeout <seconds>] [--attempts 1\|2] -- <Unity args>`; repeated `--require-test` supported | [header](../../studio/tools/unity-batch.sh) |
| dotnet-test.sh | `<packet> [project-or-solution] [-- extra dotnet test args...]` | [header](../../studio/tools/dotnet-test.sh) |
| build_game_player.sh | `<packet> [--gate]`; Hollowmere IL2CPP, hashes, xvfb smoke; optional V1 gate | [header](../../studio/tools/build_game_player.sh) |
| record_playthrough.sh | `<packet> <autoplay-script> [--minutes N] [--label name]`; default stops etosd, therefore outside the current no-restart authorization | [header](../../studio/tools/record_playthrough.sh) |
| verify-all.sh | `all` or selected static/display/node/build lanes; advanced `p42d <lane>` retains bounded live receipts | [header/dispatch](../../studio/tools/verify-all.sh) |

Exact Hollowmere EditMode arguments match unity-compile; test runs omit `-quit` and read XML:

```sh
bash studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" \
  --log-dir "$PWD/.unity-logs" --label studio-editmode \
  --results "$PWD/.unity-logs/studio-editmode.xml" -- \
  -runTests -testPlatform EditMode -testFilter 'GameCore\.Studio\..*'
PATH="$HOME/.dotnet:$PATH" dotnet test dotnet/tests/GameCore.Studio.Model.Tests
python3 tools/check_package_metadata.py
python3 tools/check_game_core_csharp.py
```

In `studio/agent`, the Rust lane is `~/.cargo/bin/cargo fmt --check && ~/.cargo/bin/cargo clippy --all-targets -- -D warnings && ~/.cargo/bin/cargo test`. Every Unity run holds one shared reservation; default timeout is 1500 s, silence watchdog 600 s. Current automatic retry is only the documented ILPP startup fault, not generic timeout or test failure. Missing/skipped/inconclusive XML is not PASS. ([unity-batch.sh header](../../studio/tools/unity-batch.sh), [STAGE-TMP2](../../studio/agent/evidence/stage-tmp2/PACKET.md))

## Reproduce verification

From a dedicated Linux clone with toolchain, licensed Unity, resolved packages and disk capacity:

```sh
studio/tools/verify-all.sh all
EVIDENCE_DISPLAY=:1 studio/tools/verify-all.sh ui
EVIDENCE_DISPLAY=:1 studio/tools/verify-all.sh views
EVIDENCE_DISPLAY=:1 studio/tools/verify-all.sh graphics
studio/tools/verify-all.sh build
```

`all` runs static gates, rebakes, suites, bounded probes, graphical memory cycles, clean checks and security probes; it makes no paid call or service restart. Display lanes need their own free display and the shared allocator. For installed-node reproduction, follow the retained [P4.2d reproducer](../../artifacts/studio/verification/TOOLS/README-P4.2d.md): authenticated hello, exact project registration and priced tariffs precede explicit live lanes. Its baseline/reservations prohibit automatic paid replay. Node-death tests remain BLOCKED under the no-stop/restart rule. Do not activate/restart the shared companion merely to read this report. ([P4.2 §Reproduce](packets/P4.2-verification.md#reproduce-and-evidence-integrity), [P4.2d §Final verification](packets/P4.2d-live-rerun.md#final-verification-and-reproduction))

## Troubleshooting

| Symptom | Diagnosis / action | Source |
|---|---|---|
| Bash 3.2 bad substitution / array behavior | Run the merged script with Linux Bash 4+; do not run host compilation under macOS Bash 3.2. | [unity-compile.sh](../../studio/tools/unity-compile.sh#L106) |
| No XML, package resolve or shutdown stall | Retain full redacted attempt; current wrapper retries only its named ILPP startup fault. A missing result is NotRun, not success. | [editor-hang](../operator/editor-hang.md), [unity-batch.sh](../../studio/tools/unity-batch.sh) |
| Player exit 139, null window backend | Even `-batchmode -nographics` needs reachable X. Use `xvfb-run -a <player> -batchmode -nographics ...` for private smoke; graphical runs use a real display. | [P4.1 CP-05](packets/P4.1-clean-proof.md#reusability-findings), [build_game_player.sh](../../studio/tools/build_game_player.sh) |
| Docker Unity exit 198 / no valid license | Historical offline sandbox licensing refusal; provision the trusted R2-F2 identity/license flow. Never choose host fallback automatically. Later P4.2d Docker stage passes on this host. | [stage packet](../../studio/stage/PACKET.md), [P4.2d §Stage](packets/P4.2d-live-rerun.md#stage-and-admission) |
| Companion rejects new catalog members / skew | Old installed binary/schema may disagree with source. Check authenticated hello minimum contract/revision and immutable release checksum; update via operator release workflow. | [R2-A §Installed companion](packets/R2-A-core-edit-recovery.md#installed-companion), [P4.2d](packets/P4.2d-live-rerun.md) |
| Metadata dependency/lock mismatch | Re-resolve the affected game's lock with host Unity after dependency changes, then rerun the checker; editing only package.json is insufficient. | [ADAPT-SPLIT §Validation](packets/ADAPT-SPLIT.md#validation) |
| Waiting for slot / instance limit | Interactive Editors count; maximum three host-wide, one per packet. Wait for capacity and keep distinct project copies. Do not bypass the allocator. | [unity-slot.sh](../../studio/tools/unity-slot.sh) |
| Stage `cache_invalid` | Build the binary, derive the exact owner/version cache, provision and verify it using 09. Never erase cold markers or widen budgets. | [09 §Build and provision](09-plugin-developer-guide.md#build-and-provision-before-staging) |
| Stage package-root mismatch | Register from the exact project checkout so trusted package pins/mounts/revisions agree. | [P4.2c §Requests](packets/P4.2c-live-rows.md#requests-to-other-packets), [P4.2d](packets/P4.2d-live-rerun.md) |
| Admit compile stall then `catalog_mismatch` | P4.2d lacks signed world/predicted hashes; preserve pending/capture evidence and production rollback. No live success is qualified. | [P4.2d §Stage](packets/P4.2d-live-rerun.md#stage-and-admission) |
| `budget_unpriced` for describe | No positive tariff/provenance; do not call. Parser's missing describe-only apply is O59. | [P4.3-final §Left open](packets/P4.3-final-docs.md#left-open) |
| Save unavailable / wrong bootstrap profile | Configure game-owned codecs and GameBoot saves; production registration uses HardFailure, while legacy validation may use InfrastructureFallback. | [P3.1 §Runtime](packets/P3.1-hollowmere-complete.md#2-runtime-pieces-gameshollowmere), [P0.4](packets/P0.4-kernel-app.md) |
