# R8-C — W-DOC-02 literal lever walkthrough

**PASS** on source `09430b3093b8b97cd4cb878f59d38ce7a8d37743` (2026-10-07). This is a distinct authored lever package, not the maintained pressure plate under another name. The final run uses the guide's R8-B driver extension, a real private ETOS node, a locally built companion, authenticated app-origin staging, creator coordinator Admit, restored Play on `:1` / RTX 4060 Ti, actual runtime lever controls, and normal History Undo. No installed service was changed. No provider, worker or voice operation occurred.

## Acceptance chain

| Boundary | Observed result | Receipt |
|---|---|---|
| Author | Unity-excluded `Mechanisms/Lever~/package`; external candidate `cs_01K6R8CMECH00000000000000J` | [Exact candidate](candidate/change-set.json), [package archive](candidate/artifacts/package.tgz), [proposal](candidate/artifacts/proposal.json) |
| Stage | Job `stg_1a1147d3f1628f4f06c9485`; seven steps PASS; Docker; **76.172 s**, original 360 s budget; zero forbidden hits | [Signed service record](final/signed-verdict.json), [job](final/stage-job.json), [semantic findings](stage/J/semantic-findings.json) |
| Candidate tests | **6 Rules + 16 EditMode + 2 PlayMode**, zero failures/skips/inconclusive | [Rules transcript](stage/J/dotnet.log.gz), [EditMode XML](stage/J/editmode.xml), [PlayMode XML](stage/J/playmode.xml) |
| Creator Admit | Candidate coordinator refreshes the exact trusted job and invokes `Admit(entry, captureAndStop:true)`; package compile/domain reload; **40.990 s** durable wall time | [Admit](final/live-admit.json), [reload transitions](final/live-progress.json) |
| Authenticated recovery | Product resumer recreates `CompanionStageService` and verifies the same job after reload; driver does not replace it | [Refresh receipt](final/automatic-refresh.json) |
| Restored world | Captured **nine OldCoin** retained; equal save roundtrip before additive target/mount installation; final equal roundtrip | [Checkpoint hashes and state](final/live-admit.json) |
| Normal-frame smoke | **120 frames**, Pending → Passed, real committed lever states **0 → 1 → 0**; signed combined catalog equals active root | [Smoke receipt](final/live-admit.json) |
| Actual control | Attached, enabled, onscreen `lever-toggle` button receives its ordinary UI submit event twice; committed on/off observed, coins still nine | [Interactive receipt](final/interactive-lever.json) |
| Undo | Normal `runtime.History.Undo` compiles removal, removes the package and pending record, restores the exact original catalog; **22.911 s** | [Undo](final/live-undo.json) |
| Spend | Zero worker attempts, media charges and voice sessions | [Ledger counts](final/no-paid-ops.json) |

Package SHA-256: `96fc3e24ccdb6bcb84b45d7f09f78c20d21680a1d81208a7119947dbee5dbd15`.
Proposal SHA-256: `5d8863cfd99e676222d8ede1b6912b1f54e2d357f805736c03ecee9a12878336`.
Original catalog: `d82aed185b6d2e4f415e1e8d45f8d70a4e2f6779be825786ba03db3b3b447c18`.
Admitted catalog: `5403c907fa83cc66a8266912eb1176726f8aff5043cf360f5d15b5f09a5b3fbf`.

The restored, initial and final slot hashes are separately retained. Full-world hashes are **not** compared across advancing game frames; the equal-roundtrip checks compare each world's own capture/restoration, and the nine-coin witness independently proves checkpoint continuity.

## Graphical evidence

The final **1280×739** Game View captures show the real runtime control and its committed label:

- [Lever On](final/lever-on-play.png)
- [Lever Off after the second activation](final/lever-off-after-play.png)
- [Lever Off before interaction](final/lever-off-before-play.png)

The **960×720** close-up camera renders observe the actual playing world, not a recreated preview scene. The green/on handle leans one way; the red/off handle leans the other:

- [On world capture](final/lever-on-world.png)
- [Off world capture](final/lever-off-after-world.png)

Both capture sets were visually inspected. `admitted-play.png` is an immediate resize-transition image and is not the accepted graphical proof; the readable captures above are taken after the standalone Game View settles. Earlier G proved the world effect and undo but its docked Game View was only 501×83. H intentionally failed the new readable-size guard after successful admission; the product command-line Undo recovered H normally before the final repeat. No package was manually deleted.

## Reproduction

Follow [the literal plugin guide](../../../../../docs/studio/09-plugin-developer-guide.md#new-lever-author-stage-admit-observe-and-undo). The retained runner transcript records exact invocations. On this host the mutable default UPM cache did not serve as the seed: the operator's verified public closure under `.cache/gamecore-studio/stage/_warm/22421f6df7cfc2cc396043796f1a4d9a966cbcd1e8c4a3287f60eb3816fa1c22/upm` was passed explicitly. The installer copies and verifies pinned public inputs; it does not clone a warm ArtifactDB or reset cold grace.

The source Editor exports full `BuildStageRequest` and tool catalog, exits, and releases its host lease **before** the standalone production-client submitter calls the companion. The sandbox Editors then run serially through `unity-batch.sh`; only after Stage terminates does the graphical creator Editor open. The scratch process owns and stops only its private node/companion. App pairing and signing keys remain outside the checkout and are not retained here.

## Attempts and regressions

All failed stage/runtime/capture attempts are retained under [stage](stage/) and [attempts](attempts/), with graphical logs compressed under [graphical-logs](graphical-logs/). No partial or failed verdict authorized admission.

- A: semantic SG012 exposed the missing command-reader namespace.
- B: compiler exposed missing Derivation/import dependencies; the extra tracked mechanism `.catalog.json` also conflicted with source-world discovery. The mechanism recipe now uses `.description.json`.
- C: Unity Entities DC0061 required an explicit Collections assembly dependency. The retained Bee failure contains the diagnostic omitted from summarized stdout.
- D: both sandbox PlayMode cases failed because `LiveTargetSeeder.TrySeed` creates empty slot storage rather than invoking the recipe applier. Explicit missing-row initialization fixed this; the smoke also proves initialization cannot reset a committed on-state.
- E: complete stage passed; graphical driver refused its incomplete four-field context before Admit. It now retains the production full request.
- F: complete stage and creator Admit reached restore, then rolled back. Candidate detach accessed a disposed old ECS world; it now releases presentation and skips system lookup after that world's disposal. The runtime control reuses the game's actual UI theme.
- G: full admission, interaction, world captures and undo passed; readable Game View capture needed correction.
- H: admission passed; capture-size guard failed; normal product Undo restored the baseline.
- J: final complete, readable workflow PASS. The candidate package is byte-identical to G/H; only trusted capture driver/envelope revisions changed.

The same private node, owner/version cache and `.cold-grace-used` marker were retained through retries. New candidate IDs preserve the earlier jobs and creator history.

## Required suite disposition

Rules gameplay: **309 passed / 0 failed / 0 skipped**. Companion fmt/clippy pass; **145 passed / 12 ignored** (ignored live fixtures are not acceptance). Final source policy gates: **42 packages / 91 package assemblies / 1,267 C# files**, pass.

The requested broad Hollowmere suite is retained without filtering away failures. Its composed-source run is **107 passed / 3 failed / 0 skipped / 0 inconclusive, 110 total**. The three R5-A dialogue/history cases fail at unchanged candidate projection, before lever admission; exact names, diagnostics and the out-of-scope core-owner request are in [the packet note](../../../../../docs/studio/packets/R8-C-trusted-extensions.md). The initial missing voice-bank authoring journal step was applied by the first test run; idempotency passes thereafter. Final XML is retained separately; no all-green project claim is made.

The final required EditMode rerun is also **107/110 with exactly those three failures**, zero skips/inconclusive: [final XML](editmode-final.xml). The separate graphics-enabled game PlayMode run passes **10/10**, zero skips/inconclusive: [graphics XML](playmode-graphics.xml). Its initial headless 9/10 run is retained as `playmode-final.xml`; the native voice assertion correctly requires graphics, so the rerun uses the existing R7-C graphics-enabled batch adapter on `:1`.
