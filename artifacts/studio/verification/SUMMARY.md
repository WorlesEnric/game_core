# P4.2 + P4.2b + P4.2c verification summary

Matrix row totals: PASS 33, BLOCKED 29, FAIL 6.

| Row | Verdict | Evidence / exact limitation |
|---|---|---|
| W-UI-01 | FAIL | [Evidence](W-UI-01/README.md): Graphical walking/picking captured and 1280×720 fits, but automatic gateway startup and R2-29 keyboard-event delivery fail; explicit pairing does not close the ordinary open-and-play path. |
| W-UI-02 | BLOCKED | [Evidence](W-UI-02/README.md): The capture includes marquee/overlap UI, but not the required three NPCs behind a fence with the resulting overlap choices. |
| W-UI-03 | BLOCKED | [Evidence](W-UI-03/README.md): Logical-object selection is captured; the lantern subpart/prefab/scope chooser sequence is not demonstrated by this driver. |
| W-UI-04 | PASS | [Evidence](W-UI-04/README.md): Ten graphical Play/Edit cycles: every measured frame has one pump and zero violations; combined engine/managed growth is +0.715% against the unchanged +15% limit. Full cycle-1/cycle-10 snapshots are retained with hashes. |
| W-UI-05 | PASS | [Evidence](W-UI-05/README.md): CORE-PICK on main 40fb91fa passes both datasets: each has 100 picks and 100 marquees over 500 distinct candidates. Pick p95 1.2553/0.8460 ms; marquee p95 0.3410/0.1970 ms, against unchanged 16/50 ms budgets. The separate 21-update timing regression also passes; combined XML 3/3. |
| W-VIEW-01 | PASS | [Evidence](W-VIEW-01/README.md): Final-main view suite: 41/41 passed, zero skips. P4.2 graphical captures retained with their original revision; no new final-main display capture is claimed. |
| W-VIEW-02 | PASS | [Evidence](W-VIEW-02/README.md): Final-main view suite: 41/41 passed, zero skips. P4.2 graphical captures retained with their original revision; no new final-main display capture is claimed. |
| W-VIEW-03 | PASS | [Evidence](W-VIEW-03/README.md): Final-main view suite: 41/41 passed, zero skips. P4.2 graphical captures retained with their original revision; no new final-main display capture is claimed. |
| W-VIEW-04 | PASS | [Evidence](W-VIEW-04/README.md): Final-main view suite: 41/41 passed, zero skips. P4.2 graphical captures retained with their original revision; no new final-main display capture is claimed. |
| W-VIEW-05 | PASS | [Evidence](W-VIEW-05/README.md): Final-main view suite: 41/41 passed, zero skips. P4.2 graphical captures retained with their original revision; no new final-main display capture is claimed. |
| W-VIEW-06 | PASS | [Evidence](W-VIEW-06/README.md): Final-main view suite: 41/41 passed, zero skips. P4.2 graphical captures retained with their original revision; no new final-main display capture is claimed. |
| W-MODEL-02 | BLOCKED | [Evidence](W-MODEL-02/README.md): Current index/impact tests cover typed references and lantern reward/stock; the exact deleted-item dialogue-line/objective witness is not exercised. |
| W-TOOL-01 | PASS | [Evidence](W-TOOL-01/README.md): Saltmarsh exports only installed production tools; fixture, Hollowmere and internal admission tools are absent. Catalog and installed-package manifest retained. |
| W-TOOL-02 | PASS | [Evidence](W-TOOL-02/README.md): Package metadata and exact asmdef-derived dependencies pass on the final harness tree. |
| W-EDIT-01 | BLOCKED | [Evidence](W-EDIT-01/README.md): Real task/candidate IDs are recorded, but the worker move is rejected for missing scope, so no applied joined task/GameCore-operation History entry exists. |
| W-EDIT-02 | PASS | [Evidence](W-EDIT-02/README.md): Five-op stale-member cases prove AllOrNothing applies nothing and BestEffort records the other outcomes. |
| W-EDIT-03 | BLOCKED | [Evidence](W-EDIT-03/README.md): No current generated portrait reaches apply/undo/redo: the legacy media caller supplies an unregistered local ID; direct image calls additionally need a verified operator tariff. Old image files are not new generation evidence. |
| W-EDIT-04 | BLOCKED | [Evidence](W-EDIT-04/README.md): Stale-target pieces pass, but the exact live NPC → exit Play → apply → delete → apply sequence is not retained as a combined acceptance run. |
| W-EDIT-05 | PASS | [Evidence](W-EDIT-05/README.md): Core and viewport gizmo tests compare the resulting journal entries with typed moves. |
| W-EDIT-06 | BLOCKED | [Evidence](W-EDIT-06/README.md): No durable Apply-to-authored workflow for runtime-only moves is exposed by the current engine/UI seam; runtime-only refusal tests are not this persistence workflow. |
| W-EDIT-07 | PASS | [Evidence](W-EDIT-07/README.md): Per-operation conflict/rebase tests pass; the retained real worker candidate also refuses with StaleContext after an actual registry revision change. |
| W-EDIT-08 | PASS | [Evidence](W-EDIT-08/README.md): single: p95 [55.8581, 63.073600000000006] ms, median 59.4659 ms, budget 200.0 ms; region: p95 [157.6064, 79.8135] ms, median 118.7100 ms, budget 1000.0 ms; prepare: p95 [4.3048, 10.177200000000001] ms, median 7.2410 ms, budget 300.0 ms |
| W-HOST-01 | BLOCKED | [Evidence](W-HOST-01/README.md): Immutable reinstall, no-op and authenticated hello pass. Full fresh install would touch provider credentials/restart etosd; node still reports TTS cost 0 after on-disk price updates, and image/describe/realtime acceptance is incomplete. |
| W-ETOS-01 | BLOCKED | [Evidence](W-ETOS-01/README.md): Both settings files are absent and proxy probes return 401. Intended committed packet evidence has no credential-shaped value or absolute home path; 3,700 inherited committed artifact files retain home paths outside this packet’s scope. |
| W-ETOS-02 | BLOCKED | [Evidence](W-ETOS-02/README.md): Live app-key access to agent-only tasks returns 403 forbidden, but the other-agent probe returns 404 agent_unknown; no installed second-agent forbidden response is established. |
| W-ETOS-04 | BLOCKED | [Evidence](W-ETOS-04/README.md): The real worker moved the selected well; no worker-side etos query of selected NPC dialogue nodes was requested or retained in that bounded live task. |
| W-ETOS-05 | BLOCKED | [Evidence](W-ETOS-05/README.md): Live client cancellation completes in 556 ms with one task/no candidate, but the creator tray-button portion is not exercised. |
| W-ETOS-06 | BLOCKED | [Evidence](W-ETOS-06/README.md): Companion portion PASS: actual etos agent restart, same task, one cancellation outcome and exact four-event cursor replay; reconnect 143.1 ms, cancel ack 188.0 ms. Node-death portion remains BLOCKED: etosd stop/restart is forbidden, and a companion restart cannot prove delayed attribution after node death. |
| W-ETOS-07 | BLOCKED | [Evidence](W-ETOS-07/README.md): P4.2d authenticated hello exposes image operator and TTS published tariffs; two TTS calls succeed. Describe is live but has no tariff; R5-B’s template explicitly remains SET_BY_OPERATOR/0.0, so no describe call is made. Prior image-import evidence remains revision-specific; no invented per-call tariff or provider invoice. |
| W-ETOS-08 | BLOCKED | [Evidence](W-ETOS-08/README.md): Removing the shared image provider and reloading the node would change concurrent users’ provider service; that operator scenario is outside the permitted no-node-restart run. |
| W-ETOS-09 | BLOCKED | [Evidence](W-ETOS-09/README.md): Client cursor replay is real; a source recompile/domain reload while an in-flight task returns to the tray was not completed. Simulated reload tests remain component evidence. |
| W-VOICE-01 | FAIL | [Evidence](W-VOICE-01/README.md): R5-B ready-gated self-test passes both real speech fixtures with no submission. The later unchanged driver fails both takes after a provider session.updated acknowledgement error: playback falls outside capture and two late sessions deliver 70/144 all-zero frames, with zero transcripts. Destructive tray/request/journal counts remain unchanged. No transcript loss occurs on the companion→client hop in the passing self-test. |
| W-AI-01 | BLOCKED | [Evidence](W-AI-01/README.md): PARTIAL: R3-F six-digit tint applies/undoes with unchanged behaviour hashes; R3-A Sprite bind and R4-A image/TTS imports pass. Two images are generated, but the unchanged P3.2 robe driver never assigns its generated robe texture through R3-D entity.setMaterialTexture. The full texture-to-material chain is unexercised. |
| W-AI-02 | FAIL | [Evidence](W-AI-02/README.md): Creation/undo portion passes: exact roster identity set returns 20→21→20. The untouched live ferryman candidate also edits Odd’s graph entry, leaving nodes 0–7 unreachable. The corrected driver fails its Play gate at bake with GP-DLG-005; NPC navigation/dialogue in Play is not established. |
| W-AI-03 | PASS | [Evidence](W-AI-03/README.md): The newly received live candidate applies unchanged. R5-C’s actual Hollowmere Play observer confirms the added line is absent unlit and displayed when shrine_lit=1; the driver records pass. |
| W-AI-04 | PASS | [Evidence](W-AI-04/README.md): The installed-companion candidate applies ui.bind: objective-line.text → vm:hud.QuestStageTitle (R3-D/D10b). The resulting authored binding is retained; separate-Editor reopen preserves it and its undo/redo/final undo succeeds. |
| W-AI-05 | PASS | [Evidence](W-AI-05/README.md): The live two-operation candidate applies against the real indexed OilFlask identity. The simulator no longer refuses GP-QST-004, and the actual Play observer confirms stage 1 after one flask and stage 2 after two; the driver records pass. |
| W-AI-06 | FAIL | [Evidence](W-AI-06/README.md): All three live entries survive close/reopen with matching saved hashes; undo/redo/final undo succeeds. The separate retained two-operation Odd witness also passes in a different Editor with Play/Edit domain reload (1/1 XML), closing R5-A’s final-postimage conflict. Full byte consistency still fails: backToBefore=false, with only contentStamp differences in Odd and DrownedBell among the four compared assets. Driver exit 0 is not treated as a full-row pass. |
| W-AI-07 | PASS | [Evidence](W-AI-07/README.md): Installed-main provider-before-budget 3D refusal returns not_configured; no generation. The named XML case passes. |
| W-PLUG-01 | BLOCKED | [Evidence](W-PLUG-01/README.md): Real three-region travel/pose/residency tests pass. Region texture/audio release and the required peak-relative Memory Profiler delta remain unmeasured. |
| W-PLUG-02 | BLOCKED | [Evidence](W-PLUG-02/README.md): Despawn/respawn variant and scale override preservation passes; the required real Animator-binding part is not asserted by this suite. |
| W-PLUG-03 | BLOCKED | [Evidence](W-PLUG-03/README.md): Real walking, focus/interact and travel pass; the deterministic script includes Jump but does not assert clearing a ledge and landing, so that required observation stays open. |
| W-PLUG-04 | PASS | [Evidence](W-PLUG-04/README.md): Fresh-boot restore retains the unloaded NPC’s captured slots, including PatrolIndex, and gameplay patrol tests assert committed progression. |
| W-PLUG-05 | PASS | [Evidence](W-PLUG-05/README.md): The actual Causeway Gate condition explain names the failed condition; committed locked-gate refusal and condition-controlled travel are exercised. |
| W-PLUG-06 | PASS | [Evidence](W-PLUG-06/README.md): Conditional dialogue differs by fact; fresh-boot restoration preserves heard_rumour/gate_open and reattaches conversations. |
| W-PLUG-07 | PASS | [Evidence](W-PLUG-07/README.md): Both quest branches pass; failure closes dependents transitively, cycles refuse, and the actual LanternLost failure ending is reached. |
| W-PLUG-08 | BLOCKED | [Evidence](W-PLUG-08/README.md): Exactly-once outbox restoration and grant IDs pass, but the specific repeated lantern pickup plus reload yielding exactly one lantern is not asserted by the retained tests. |
| W-PLUG-09 | PASS | [Evidence](W-PLUG-09/README.md): The real Hollowmere logic why-not trace names the failed condition and the change that would make it true. |
| W-PLUG-10 | BLOCKED | [Evidence](W-PLUG-10/README.md): Definition and tool validators run, but the same invalid definition is not compared through inspector, validator and live-agent fronts in one retained parity case. |
| W-PLUG-11 | BLOCKED | [Evidence](W-PLUG-11/README.md): Boot/UI/audio and direct TTS import pass; audible ambience crossfade and native clip release have no current combined capture/snapshot proof. |
| W-PLUG-12 | PASS | [Evidence](W-PLUG-12/README.md): Both final-tree rebakes/Entry.Verify pass without output changes; byte identity and single-definition revision tests pass. Structural recipe revisions did not change, preserving existing save compatibility. |
| W-KERNEL-01 | PASS | [Evidence](W-KERNEL-01/README.md): Production GameApplication boot with a corrupted catalog returns CatalogFingerprintMismatch and creates no world. |
| W-PERSIST-01 | PASS | [Evidence](W-PERSIST-01/README.md): Belfry save/fresh-boot restore preserves canonical slots, items/facts, resident region and in-flight work exactly once; real game mid-quest restore continues to ending C. |
| W-PERSIST-02 | BLOCKED | [Evidence](W-PERSIST-02/README.md): Cosmetic-content save compatibility passes, but the literal prefab rename/reopen case is not exercised; no claim is inferred from stable GUID design alone. |
| W-PERSIST-03 | PASS | [Evidence](W-PERSIST-03/README.md): Production SaveService restores a V1 checkpoint through the registered V2 migration; missing migration refuses while leaving the running world unchanged. |
| W-REC-01 | PASS | [Evidence](W-REC-01/README.md): Real SIGKILL during engine mutation, then a different Editor process: both rollback and resume recover successfully. Killed-Editor nonzero exits are expected and retained. |
| W-REC-03 | BLOCKED | [Evidence](W-REC-03/README.md): Region cancellation components exist; no installed running-stage cancellation is possible through the missing app-origin staging path, and discard is not proof of cancellation. |
| W-MECH-01 | FAIL | [Evidence](W-MECH-01/README.md): Installed app-origin stage passes all seven gates in 158.122 s (33 EditMode + 2 PlayMode XML passes). Authenticated fetch/verify enables Admit and the visible badge says verdict pass. Explicit Play Admit captures/stops and passes the real catalog checkpoint, but compilation stalls; the owned wrapper is stopped at 817 s. Recovery finishes rollback with catalog_mismatch because the signed delta has no world/predicted hashes. Package and pending record are absent afterward. No live restoration, tri-state smoke or successful admission undo is claimed. The semantic-negative fixture is refused with 14 lexical hits, no issued passing verdict and Admit disabled; Roslyn/Unity are skipped for that refusal. |
| W-GAME-01 | BLOCKED | [Evidence](W-GAME-01/README.md): The P3.1 recording is reused, as requested. It predates P3.1b and failed B-FRAME; P3.1b’s later 640×480 llvmpipe measurements cannot qualify final-tree RTX/1080p performance. |
| W-GAME-05 | BLOCKED | [Evidence](W-GAME-05/README.md): Editor full-quest endings and save/load pass; a current standalone menu→save→load→ending→restart playthrough is not re-recorded under the explicit recording-reuse instruction. |
| W-GAME-06 | PASS | [Evidence](W-GAME-06/README.md): Hollowmere Linux IL2CPP build/hash passes. V1 phases 1–8 and resumed 9–11 pass; both release resumes retain their failed setup attempts, with no repeated qualification probes or relaxed budget. |
| W-GAME-07 | BLOCKED | [Evidence](W-GAME-07/README.md): The owner forbids etosd stop/restart, so the requested stopped-node/no-network player scenario is not run. The companion-only supervisor restart does not establish this condition; the prior namespace prerequisite refusal remains historical evidence. |
| W-GAME-08 | PASS | [Evidence](W-GAME-08/README.md): Ten graphical Play/Edit cycles: every measured frame has one pump and zero violations; combined engine/managed growth is +0.715% against the unchanged +15% limit. Full cycle-1/cycle-10 snapshots are retained with hashes. |
| W-CLEAN-01 | PASS | [Evidence](W-CLEAN-01/README.md): Final-tree AuthorAll, content/catalog tests, fresh-cache Linux IL2CPP build and standalone 600-frame quest/save/restore/ending run pass. The separate app-stage contract test fails under W-MECH-01. |
| W-CLEAN-02 | PASS | [Evidence](W-CLEAN-02/README.md): After final AuthorAll, build and both suite rechecks, git diff against origin/main is empty for all Packages/. |
| W-DOC-01 | PASS | [Evidence](W-DOC-01/README.md): The creator-guide NPC/dialogue boundary is exercised through npc.addAt in Context and Add line in the Dialogue view, using the existing Maren definition and its bound graph. Placement and dialogue edit apply; save and normal journal undo restore the 20-entity/13-node baseline. Named guide test passes in XML. This is the documented existing-definition flow, not creation of a new unique NPC definition. |
| W-DOC-02 | FAIL | [Evidence](W-DOC-02/README.md): Guide 09: four sample regeneration/check steps pass. After supplying its built-binary prerequisite, stage refuses cache_invalid in the fresh private stage root. No exact versioned-cache provisioning recipe or authenticated app-origin handoff bridges 09:132-142. No new lever or successful Admit is claimed. |
| W-E2E-01 | BLOCKED | [Evidence](W-E2E-01/README.md): P4.2d requalifies the requested R5/CORE-PICK rows on main 40fb91fa with the matching installed release. Real dialogue/quest Play effects and targeted NPC/history fixes pass, but the ferryman Play bake, full byte consistency, driver voice, and live admission retain failures. Describe remains unpriced. Untouched rows retain their original revision-specific evidence; this is not all-row product acceptance. |

## Retained attempts (including superseded and expected failures)

| Evidence | Verdict | Revision |
|---|---|---|
| [B-EDIT/p42b-dataset-1-20261006T043035.329811Z](B-EDIT/p42b-dataset-1-20261006T043035.329811Z/README.md) | FAIL | 8d1574e4326a |
| [B-EDIT/p42b-dataset-2-20261006T043313.388120Z](B-EDIT/p42b-dataset-2-20261006T043313.388120Z/README.md) | FAIL | 8d1574e4326a |
| [B-EDIT/p42b-dataset-summary](B-EDIT/p42b-dataset-summary/README.md) | PASS | 1752ca8a5309 |
| [B-SELECT/p42b-dataset-summary](B-SELECT/p42b-dataset-summary/README.md) | FAIL | 1752ca8a5309 |
| [B-SELECT/p42b-selection-1-20261006T043401.486764Z](B-SELECT/p42b-selection-1-20261006T043401.486764Z/README.md) | FAIL | 8d1574e4326a |
| [B-SELECT/p42b-selection-2-20261006T043603.655328Z](B-SELECT/p42b-selection-2-20261006T043603.655328Z/README.md) | FAIL | 8d1574e4326a |
| [GRAPHICAL/graphics-required-tests-20261005T172641.170099Z](GRAPHICAL/graphics-required-tests-20261005T172641.170099Z/README.md) | FAIL | 3c5f817aa45a |
| [GRAPHICAL/graphics-required-tests-20261005T191202.057821Z](GRAPHICAL/graphics-required-tests-20261005T191202.057821Z/README.md) | FAIL | 12d7e5c7abab |
| [INSTALL-P4.2b/final-main-build-20261006T043503.739699Z](INSTALL-P4.2b/final-main-build-20261006T043503.739699Z/README.md) | FAIL | 8d1574e4326a |
| [INSTALL-P4.2b/final-main-release-20261006T042504.872797Z](INSTALL-P4.2b/final-main-release-20261006T042504.872797Z/README.md) | FAIL | 8d1574e4326a |
| [INSTALL-P4.2b/final-main-release-binary-20261006T043811.635588Z](INSTALL-P4.2b/final-main-release-binary-20261006T043811.635588Z/README.md) | PASS | 8d1574e4326a |
| [INSTALL-P4.2b/live-guard-20261006T043321.261196Z](INSTALL-P4.2b/live-guard-20261006T043321.261196Z/README.md) | BLOCKED | 8d1574e4326a |
| [INSTALL-P4.2b/live-guard-20261006T045515.048905Z](INSTALL-P4.2b/live-guard-20261006T045515.048905Z/README.md) | BLOCKED | 53525c74d62c |
| [INSTALL-P4.2b/owner-tariff-after-20261006T043138.998730Z](INSTALL-P4.2b/owner-tariff-after-20261006T043138.998730Z/README.md) | PASS | 8d1574e4326a |
| [INSTALL-P4.2b/owner-tariff-before-20261006T043138.809640Z](INSTALL-P4.2b/owner-tariff-before-20261006T043138.809640Z/README.md) | FAIL | 8d1574e4326a |
| [INSTALL-P4.2c/activate-20261006T071705.198288Z](INSTALL-P4.2c/activate-20261006T071705.198288Z/README.md) | PASS | 65666ac27168 |
| [INSTALL-P4.2c/align-stage-package-root-20261006T075909.903727Z](INSTALL-P4.2c/align-stage-package-root-20261006T075909.903727Z/README.md) | PASS | 9d8ab6b11d43 |
| [INSTALL-P4.2c/final-main-build-20261006T071249.676074Z](INSTALL-P4.2c/final-main-build-20261006T071249.676074Z/README.md) | PASS | f787829289ea |
| [INSTALL-P4.2c/harness-compile-20261006T072530.060614Z](INSTALL-P4.2c/harness-compile-20261006T072530.060614Z/README.md) | FAIL | 65666ac27168 |
| [INSTALL-P4.2c/harness-compile-fixed-20261006T072709.074883Z](INSTALL-P4.2c/harness-compile-fixed-20261006T072709.074883Z/README.md) | PASS | 65666ac27168 |
| [INSTALL-P4.2c/hello-20261006T071707.149737Z](INSTALL-P4.2c/hello-20261006T071707.149737Z/README.md) | PASS | 65666ac27168 |
| [INSTALL-P4.2c/priced-tts-20261006T071733.006098Z](INSTALL-P4.2c/priced-tts-20261006T071733.006098Z/README.md) | PASS | 65666ac27168 |
| [INSTALL-P4.2c/unity-fresh-compile-20261006T071423.613958Z](INSTALL-P4.2c/unity-fresh-compile-20261006T071423.613958Z/README.md) | PASS | f787829289ea |
| [INSTALL-P4.2d/hello-20261006T112349.691723Z](INSTALL-P4.2d/hello-20261006T112349.691723Z/README.md) | PASS | ed1969e4cc14 |
| [STATIC/allocator-isolated-20261005T173202.610714Z](STATIC/allocator-isolated-20261005T173202.610714Z/README.md) | PASS | 20e34d1e6a68 |
| [STATIC/cargo-clippy-20261005T170026.580378Z](STATIC/cargo-clippy-20261005T170026.580378Z/README.md) | PASS | 813e6b4591d8 |
| [STATIC/cargo-clippy-20261005T185349.726636Z](STATIC/cargo-clippy-20261005T185349.726636Z/README.md) | PASS | 4214d67b2289 |
| [STATIC/cargo-fmt-20261005T170026.358681Z](STATIC/cargo-fmt-20261005T170026.358681Z/README.md) | PASS | 813e6b4591d8 |
| [STATIC/cargo-fmt-20261005T185349.460752Z](STATIC/cargo-fmt-20261005T185349.460752Z/README.md) | PASS | 4214d67b2289 |
| [STATIC/cargo-test-20261005T170039.345047Z](STATIC/cargo-test-20261005T170039.345047Z/README.md) | PASS | 813e6b4591d8 |
| [STATIC/cargo-test-20261005T185350.034834Z](STATIC/cargo-test-20261005T185350.034834Z/README.md) | PASS | 4214d67b2289 |
| [STATIC/collector-temp-root-regression-before-20261005T180136.487684Z](STATIC/collector-temp-root-regression-before-20261005T180136.487684Z/README.md) | FAIL | 20e34d1e6a68 |
| [STATIC/csharp-20261005T170003.419790Z](STATIC/csharp-20261005T170003.419790Z/README.md) | PASS | 813e6b4591d8 |
| [STATIC/csharp-20261005T185323.246630Z](STATIC/csharp-20261005T185323.246630Z/README.md) | PASS | 4214d67b2289 |
| [STATIC/dotnet-20261005T170105.969990Z](STATIC/dotnet-20261005T170105.969990Z/README.md) | BLOCKED | 813e6b4591d8 |
| [STATIC/dotnet-20261005T185357.113798Z](STATIC/dotnet-20261005T185357.113798Z/README.md) | BLOCKED | 4214d67b2289 |
| [STATIC/enospc-summary-before-20261005T190318.843314Z](STATIC/enospc-summary-before-20261005T190318.843314Z/README.md) | FAIL | 12d7e5c7abab |
| [STATIC/evidence-footer-20261005T173202.335369Z](STATIC/evidence-footer-20261005T173202.335369Z/README.md) | PASS | 20e34d1e6a68 |
| [STATIC/final-csharp-policy-20261005T205441.575079Z](STATIC/final-csharp-policy-20261005T205441.575079Z/README.md) | PASS | 50a4c07c2021 |
| [STATIC/final-evidence-regressions-20261005T205458.709656Z](STATIC/final-evidence-regressions-20261005T205458.709656Z/README.md) | PASS | 50a4c07c2021 |
| [STATIC/final-harness-csharp-20261005T194657.444672Z](STATIC/final-harness-csharp-20261005T194657.444672Z/README.md) | PASS | d26494a498d4 |
| [STATIC/final-harness-regressions-20261005T194717.312081Z](STATIC/final-harness-regressions-20261005T194717.312081Z/README.md) | PASS | c532b77edd93 |
| [STATIC/host-local-before-20261005T170758.504928Z](STATIC/host-local-before-20261005T170758.504928Z/README.md) | FAIL | 813e6b4591d8 |
| [STATIC/host-python-20261005T170215.225826Z](STATIC/host-python-20261005T170215.225826Z/README.md) | FAIL | 813e6b4591d8 |
| [STATIC/host-python-20261005T185456.069013Z](STATIC/host-python-20261005T185456.069013Z/README.md) | FAIL | 4214d67b2289 |
| [STATIC/host-python-dependencies-20261005T185559.640539Z](STATIC/host-python-dependencies-20261005T185559.640539Z/README.md) | PASS | 38baed3d6484 |
| [STATIC/host-python-with-dependencies-20261005T170413.171545Z](STATIC/host-python-with-dependencies-20261005T170413.171545Z/README.md) | PASS | 813e6b4591d8 |
| [STATIC/installer-python-20261005T170213.921047Z](STATIC/installer-python-20261005T170213.921047Z/README.md) | PASS | 813e6b4591d8 |
| [STATIC/installer-python-20261005T185454.771478Z](STATIC/installer-python-20261005T185454.771478Z/README.md) | PASS | 4214d67b2289 |
| [STATIC/p42-recovery-csharp-20261005T172504.169495Z](STATIC/p42-recovery-csharp-20261005T172504.169495Z/README.md) | PASS | 3c5f817aa45a |
| [STATIC/p42-recovery-metadata-20261005T172521.633061Z](STATIC/p42-recovery-metadata-20261005T172521.633061Z/README.md) | PASS | 3c5f817aa45a |
| [STATIC/p42-regressions-20261005T170758.766805Z](STATIC/p42-regressions-20261005T170758.766805Z/README.md) | PASS | 813e6b4591d8 |
| [STATIC/p42-regressions-final-20261005T180136.569417Z](STATIC/p42-regressions-final-20261005T180136.569417Z/README.md) | PASS | 20e34d1e6a68 |
| [STATIC/p42-resume-regressions-20261005T190318.927664Z](STATIC/p42-resume-regressions-20261005T190318.927664Z/README.md) | PASS | 12d7e5c7abab |
| [STATIC/p42b-csharp-20261006T042924.991352Z](STATIC/p42b-csharp-20261006T042924.991352Z/README.md) | PASS | 8d1574e4326a |
| [STATIC/p42b-evidence-check-20261006T045515.284217Z](STATIC/p42b-evidence-check-20261006T045515.284217Z/README.md) | PASS | 53525c74d62c |
| [STATIC/p42b-evidence-check-20261006T045625.240655Z](STATIC/p42b-evidence-check-20261006T045625.240655Z/README.md) | PASS | 53525c74d62c |
| [STATIC/p42b-installer-20261006T043007.007502Z](STATIC/p42b-installer-20261006T043007.007502Z/README.md) | FAIL | 8d1574e4326a |
| [STATIC/p42b-metadata-20261006T042924.450487Z](STATIC/p42b-metadata-20261006T042924.450487Z/README.md) | PASS | 8d1574e4326a |
| [STATIC/p42b-packet-regressions-20261006T044851.845920Z](STATIC/p42b-packet-regressions-20261006T044851.845920Z/README.md) | PASS | 8d1574e4326a |
| [STATIC/p42b-runner-20261006T043009.111809Z](STATIC/p42b-runner-20261006T043009.111809Z/README.md) | PASS | 8d1574e4326a |
| [STATIC/p42b-runner-final-20261006T044852.507551Z](STATIC/p42b-runner-final-20261006T044852.507551Z/README.md) | PASS | 8d1574e4326a |
| [STATIC/p42b-shell-syntax-20261006T044853.671171Z](STATIC/p42b-shell-syntax-20261006T044853.671171Z/README.md) | PASS | 8d1574e4326a |
| [STATIC/p42c-cap-baseline-after-20261006T073726.789415Z](STATIC/p42c-cap-baseline-after-20261006T073726.789415Z/README.md) | PASS | dbedd2fb6c83 |
| [STATIC/p42c-cap-baseline-before-20261006T073717.555131Z](STATIC/p42c-cap-baseline-before-20261006T073717.555131Z/README.md) | FAIL | dbedd2fb6c83 |
| [STATIC/p42c-csharp-20261006T072434.908537Z](STATIC/p42c-csharp-20261006T072434.908537Z/README.md) | PASS | 65666ac27168 |
| [STATIC/p42c-existing-runner-tests-20261006T072434.219937Z](STATIC/p42c-existing-runner-tests-20261006T072434.219937Z/README.md) | PASS | 65666ac27168 |
| [STATIC/p42c-final-csharp-20261006T091304.938309Z](STATIC/p42c-final-csharp-20261006T091304.938309Z/README.md) | PASS | 9d8ab6b11d43 |
| [STATIC/p42c-final-evidence-20261006T091327.148640Z](STATIC/p42c-final-evidence-20261006T091327.148640Z/README.md) | PASS | 9d8ab6b11d43 |
| [STATIC/p42c-final-metadata-20261006T091304.273400Z](STATIC/p42c-final-metadata-20261006T091304.273400Z/README.md) | PASS | 9d8ab6b11d43 |
| [STATIC/p42c-final-runner-20261006T091303.468484Z](STATIC/p42c-final-runner-20261006T091303.468484Z/README.md) | PASS | 9d8ab6b11d43 |
| [STATIC/p42c-final-tools-20261006T091303.245301Z](STATIC/p42c-final-tools-20261006T091303.245301Z/README.md) | PASS | 9d8ab6b11d43 |
| [STATIC/p42c-metadata-20261006T072434.538983Z](STATIC/p42c-metadata-20261006T072434.538983Z/README.md) | PASS | 65666ac27168 |
| [STATIC/p42c-runner-tests-20261006T072434.120384Z](STATIC/p42c-runner-tests-20261006T072434.120384Z/README.md) | PASS | 65666ac27168 |
| [STATIC/python-test-dependencies-20261005T194810.055434Z](STATIC/python-test-dependencies-20261005T194810.055434Z/README.md) | PASS | c532b77edd93 |
| [STATIC/python-test-environment-20261005T194808.117890Z](STATIC/python-test-environment-20261005T194808.117890Z/README.md) | PASS | c532b77edd93 |
| [STATIC/reproducer-python-dependencies-20261005T194814.041644Z](STATIC/reproducer-python-dependencies-20261005T194814.041644Z/README.md) | PASS | c532b77edd93 |
| [STATIC/run-suffix-redaction-after-20261005T203340.470402Z](STATIC/run-suffix-redaction-after-20261005T203340.470402Z/README.md) | PASS | c532b77edd93 |
| [STATIC/run-suffix-redaction-before-20261005T203339.923384Z](STATIC/run-suffix-redaction-before-20261005T203339.923384Z/README.md) | FAIL | c532b77edd93 |
| [STATIC/schema-mirror-20261005T170026.355640Z](STATIC/schema-mirror-20261005T170026.355640Z/README.md) | PASS | 813e6b4591d8 |
| [STATIC/schema-mirror-20261005T185349.457985Z](STATIC/schema-mirror-20261005T185349.457985Z/README.md) | PASS | 4214d67b2289 |
| [STATIC/schemas-20261005T170021.351083Z](STATIC/schemas-20261005T170021.351083Z/README.md) | PASS | 813e6b4591d8 |
| [STATIC/schemas-20261005T185346.823990Z](STATIC/schemas-20261005T185346.823990Z/README.md) | PASS | 4214d67b2289 |
| [STATIC/stage-python-20261005T170212.958736Z](STATIC/stage-python-20261005T170212.958736Z/README.md) | PASS | 813e6b4591d8 |
| [STATIC/stage-python-20261005T185454.115173Z](STATIC/stage-python-20261005T185454.115173Z/README.md) | PASS | 4214d67b2289 |
| [STATIC/stage-slot-20261005T170215.275359Z](STATIC/stage-slot-20261005T170215.275359Z/README.md) | PASS | 813e6b4591d8 |
| [STATIC/stage-slot-20261005T185456.082576Z](STATIC/stage-slot-20261005T185456.082576Z/README.md) | PASS | 4214d67b2289 |
| [UNITY-CLEANPROOF/clean-recheck-editmode-20261005T182050.502116Z](UNITY-CLEANPROOF/clean-recheck-editmode-20261005T182050.502116Z/README.md) | FAIL | 20e34d1e6a68 |
| [UNITY-CLEANPROOF/clean-recheck-editmode-20261005T202042.513842Z](UNITY-CLEANPROOF/clean-recheck-editmode-20261005T202042.513842Z/README.md) | FAIL | c532b77edd93 |
| [UNITY-CLEANPROOF/clean-recheck-playmode-20261005T182126.162519Z](UNITY-CLEANPROOF/clean-recheck-playmode-20261005T182126.162519Z/README.md) | PASS | 20e34d1e6a68 |
| [UNITY-CLEANPROOF/clean-recheck-playmode-20261005T202131.388822Z](UNITY-CLEANPROOF/clean-recheck-playmode-20261005T202131.388822Z/README.md) | PASS | c532b77edd93 |
| [UNITY-CLEANPROOF/editmode-20261005T172247.414492Z](UNITY-CLEANPROOF/editmode-20261005T172247.414492Z/README.md) | PASS | 3c5f817aa45a |
| [UNITY-CLEANPROOF/editmode-20261005T190038.583676Z](UNITY-CLEANPROOF/editmode-20261005T190038.583676Z/README.md) | FAIL | 38baed3d6484 |
| [UNITY-CLEANPROOF/playmode-20261005T172329.747118Z](UNITY-CLEANPROOF/playmode-20261005T172329.747118Z/README.md) | PASS | 3c5f817aa45a |
| [UNITY-CLEANPROOF/playmode-20261005T190114.416914Z](UNITY-CLEANPROOF/playmode-20261005T190114.416914Z/README.md) | PASS | 38baed3d6484 |
| [UNITY-HOLLOWMERE/editmode-20261005T171102.242838Z](UNITY-HOLLOWMERE/editmode-20261005T171102.242838Z/README.md) | BLOCKED | 3c5f817aa45a |
| [UNITY-HOLLOWMERE/editmode-20261005T185625.043671Z](UNITY-HOLLOWMERE/editmode-20261005T185625.043671Z/README.md) | BLOCKED | 38baed3d6484 |
| [UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z](UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md) | BLOCKED | 50a4c07c2021 |
| [UNITY-HOLLOWMERE/editmode-final-harness-20261005T204130.782071Z](UNITY-HOLLOWMERE/editmode-final-harness-20261005T204130.782071Z/README.md) | FAIL | 74005cef3447 |
| [UNITY-HOLLOWMERE/p42b-final-views-20261006T042540.489299Z](UNITY-HOLLOWMERE/p42b-final-views-20261006T042540.489299Z/README.md) | PASS | 8d1574e4326a |
| [UNITY-HOLLOWMERE/perf-final-1-20261005T201110.316067Z](UNITY-HOLLOWMERE/perf-final-1-20261005T201110.316067Z/README.md) | PASS | c532b77edd93 |
| [UNITY-HOLLOWMERE/perf-final-2-20261005T201658.304250Z](UNITY-HOLLOWMERE/perf-final-2-20261005T201658.304250Z/README.md) | PASS | c532b77edd93 |
| [UNITY-HOLLOWMERE/perf-probe-1-20261005T181938.833666Z](UNITY-HOLLOWMERE/perf-probe-1-20261005T181938.833666Z/README.md) | PASS | 20e34d1e6a68 |
| [UNITY-HOLLOWMERE/perf-probe-2-20261005T182014.974536Z](UNITY-HOLLOWMERE/perf-probe-2-20261005T182014.974536Z/README.md) | PASS | 20e34d1e6a68 |
| [UNITY-HOLLOWMERE/playmode-20261005T172047.459311Z](UNITY-HOLLOWMERE/playmode-20261005T172047.459311Z/README.md) | PASS | 3c5f817aa45a |
| [UNITY-HOLLOWMERE/playmode-20261005T185951.720751Z](UNITY-HOLLOWMERE/playmode-20261005T185951.720751Z/README.md) | PASS | 38baed3d6484 |
| [UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z](UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md) | PASS | 74005cef3447 |
| [W-AI-01/p42b-live-prerequisite-20261006T044153.908826Z](W-AI-01/p42b-live-prerequisite-20261006T044153.908826Z/README.md) | BLOCKED | 8d1574e4326a |
| [W-AI-01/p42c-robe2-20261006T072832.434641Z](W-AI-01/p42c-robe2-20261006T072832.434641Z/README.md) | PASS | 65666ac27168 |
| [W-AI-02/p42b-live-prerequisite-20261006T044154.619482Z](W-AI-02/p42b-live-prerequisite-20261006T044154.619482Z/README.md) | BLOCKED | 8d1574e4326a |
| [W-AI-02/p42c-text2-20261006T071746.894107Z](W-AI-02/p42c-text2-20261006T071746.894107Z/README.md) | FAIL | 65666ac27168 |
| [W-AI-02/p42d-text2-20261006T113349.342620Z](W-AI-02/p42d-text2-20261006T113349.342620Z/README.md) | FAIL | 274cfc7d24bb |
| [W-AI-03/p42b-live-prerequisite-20261006T044156.183935Z](W-AI-03/p42b-live-prerequisite-20261006T044156.183935Z/README.md) | BLOCKED | 8d1574e4326a |
| [W-AI-03/p42c-narrative-20261006T073303.291662Z](W-AI-03/p42c-narrative-20261006T073303.291662Z/README.md) | FAIL | dbedd2fb6c83 |
| [W-AI-03/p42d-narrative-20261006T114113.853565Z](W-AI-03/p42d-narrative-20261006T114113.853565Z/README.md) | PASS | 951a05eb084f |
| [W-AI-04/p42b-live-prerequisite-20261006T044156.192058Z](W-AI-04/p42b-live-prerequisite-20261006T044156.192058Z/README.md) | BLOCKED | 8d1574e4326a |
| [W-AI-05/p42b-live-prerequisite-20261006T044156.198991Z](W-AI-05/p42b-live-prerequisite-20261006T044156.198991Z/README.md) | BLOCKED | 8d1574e4326a |
| [W-AI-06/p42b-live-prerequisite-20261006T044156.738894Z](W-AI-06/p42b-live-prerequisite-20261006T044156.738894Z/README.md) | BLOCKED | 8d1574e4326a |
| [W-AI-06/p42c-reopen-20261006T073813.299352Z](W-AI-06/p42c-reopen-20261006T073813.299352Z/README.md) | FAIL | dbedd2fb6c83 |
| [W-AI-06/p42d-history-prepare-20261006T115218.627176Z](W-AI-06/p42d-history-prepare-20261006T115218.627176Z/README.md) | PASS | e46d5fff79dc |
| [W-AI-06/p42d-history-reopen-20261006T115409.820866Z](W-AI-06/p42d-history-reopen-20261006T115409.820866Z/README.md) | PASS | e46d5fff79dc |
| [W-AI-06/p42d-reopen-20261006T114958.044810Z](W-AI-06/p42d-reopen-20261006T114958.044810Z/README.md) | PASS | e46d5fff79dc |
| [W-AI-07/installed-3d-refusal-tts-tamper-20261005T192945.989942Z](W-AI-07/installed-3d-refusal-tts-tamper-20261005T192945.989942Z/README.md) | FAIL | d26494a498d4 |
| [W-AI-07/p42b-live-prerequisite-20261006T044157.606085Z](W-AI-07/p42b-live-prerequisite-20261006T044157.606085Z/README.md) | BLOCKED | 8d1574e4326a |
| [W-CLEAN-01/cleanproof-fresh-build-cache-20261005T195625.655687Z](W-CLEAN-01/cleanproof-fresh-build-cache-20261005T195625.655687Z/README.md) | PASS | c532b77edd93 |
| [W-CLEAN-01/cleanproof-linux-retry-20261005T195339.041552Z](W-CLEAN-01/cleanproof-linux-retry-20261005T195339.041552Z/README.md) | FAIL | c532b77edd93 |
| [W-CLEAN-01/final-player-600-frames-20261005T201814.524176Z](W-CLEAN-01/final-player-600-frames-20261005T201814.524176Z/README.md) | PASS | c532b77edd93 |
| [W-CLEAN-01/saltmarsh-linux-build-20261005T184220.324518Z](W-CLEAN-01/saltmarsh-linux-build-20261005T184220.324518Z/README.md) | FAIL | 20e34d1e6a68 |
| [W-CLEAN-02/package-diff-20261005T182204.124598Z](W-CLEAN-02/package-diff-20261005T182204.124598Z/README.md) | PASS | 20e34d1e6a68 |
| [W-CLEAN-02/package-diff-20261005T202208.742343Z](W-CLEAN-02/package-diff-20261005T202208.742343Z/README.md) | PASS | c532b77edd93 |
| [W-DOC-01/p42b-fresh-guide-open-20261006T044416.491505Z](W-DOC-01/p42b-fresh-guide-open-20261006T044416.491505Z/README.md) | PASS | 8d1574e4326a |
| [W-DOC-01/p42c-guides-20261006T082946.366496Z](W-DOC-01/p42c-guides-20261006T082946.366496Z/README.md) | FAIL | 9d8ab6b11d43 |
| [W-DOC-01/p42c-image-recovered-20261006T084302.391519Z](W-DOC-01/p42c-image-recovered-20261006T084302.391519Z/README.md) | FAIL | 9d8ab6b11d43 |
| [W-DOC-01/p42c-image-recovered-20261006T084832.481069Z](W-DOC-01/p42c-image-recovered-20261006T084832.481069Z/README.md) | FAIL | 9d8ab6b11d43 |
| [W-DOC-01/p42d-guide-20261006T123449.140659Z](W-DOC-01/p42d-guide-20261006T123449.140659Z/README.md) | PASS | 369a0de1eb22 |
| [W-DOC-02/p42b-literal-plugin-guide-20261006T043233.012634Z](W-DOC-02/p42b-literal-plugin-guide-20261006T043233.012634Z/README.md) | FAIL | 8d1574e4326a |
| [W-DOC-02/p42b-plugin-guide-built-prerequisite-20261006T045350.370953Z](W-DOC-02/p42b-plugin-guide-built-prerequisite-20261006T045350.370953Z/README.md) | FAIL | 53525c74d62c |
| [W-EDIT-07/live-catalog-candidate-mode-20261005T194931.456257Z](W-EDIT-07/live-catalog-candidate-mode-20261005T194931.456257Z/README.md) | PASS | c532b77edd93 |
| [W-EDIT-07/live-catalog-stale-context-20261005T194602.663654Z](W-EDIT-07/live-catalog-stale-context-20261005T194602.663654Z/README.md) | FAIL | d26494a498d4 |
| [W-ETOS-01/proxy-missing-app-20261005T182210.204090Z](W-ETOS-01/proxy-missing-app-20261005T182210.204090Z/README.md) | PASS | 20e34d1e6a68 |
| [W-ETOS-01/proxy-missing-app-20261005T190751.625610Z](W-ETOS-01/proxy-missing-app-20261005T190751.625610Z/README.md) | PASS | 12d7e5c7abab |
| [W-ETOS-01/proxy-missing-app-20261005T202419.696320Z](W-ETOS-01/proxy-missing-app-20261005T202419.696320Z/README.md) | PASS | c532b77edd93 |
| [W-ETOS-01/proxy-missing-app-20261005T203050.874560Z](W-ETOS-01/proxy-missing-app-20261005T203050.874560Z/README.md) | PASS | c532b77edd93 |
| [W-ETOS-01/proxy-wrong-token-20261005T182210.216151Z](W-ETOS-01/proxy-wrong-token-20261005T182210.216151Z/README.md) | PASS | 20e34d1e6a68 |
| [W-ETOS-01/proxy-wrong-token-20261005T190751.641628Z](W-ETOS-01/proxy-wrong-token-20261005T190751.641628Z/README.md) | PASS | 12d7e5c7abab |
| [W-ETOS-01/proxy-wrong-token-20261005T202419.726540Z](W-ETOS-01/proxy-wrong-token-20261005T202419.726540Z/README.md) | PASS | c532b77edd93 |
| [W-ETOS-01/proxy-wrong-token-20261005T203050.892383Z](W-ETOS-01/proxy-wrong-token-20261005T203050.892383Z/README.md) | PASS | c532b77edd93 |
| [W-ETOS-01/security-scan-20261005T182204.131503Z](W-ETOS-01/security-scan-20261005T182204.131503Z/README.md) | PASS | 20e34d1e6a68 |
| [W-ETOS-01/security-scan-20261005T190745.437064Z](W-ETOS-01/security-scan-20261005T190745.437064Z/README.md) | PASS | 12d7e5c7abab |
| [W-ETOS-01/security-scan-20261005T202404.701613Z](W-ETOS-01/security-scan-20261005T202404.701613Z/README.md) | PASS | c532b77edd93 |
| [W-ETOS-01/security-scan-20261005T203045.679270Z](W-ETOS-01/security-scan-20261005T203045.679270Z/README.md) | PASS | c532b77edd93 |
| [W-ETOS-06/installed-stream-resume-cancel-20261005T185854.961200Z](W-ETOS-06/installed-stream-resume-cancel-20261005T185854.961200Z/README.md) | PASS | 38baed3d6484 |
| [W-ETOS-06/p42b-live-prerequisite-20261006T044157.822009Z](W-ETOS-06/p42b-live-prerequisite-20261006T044157.822009Z/README.md) | BLOCKED | 8d1574e4326a |
| [W-ETOS-06/p42c-companion-restart-20261006T082527.197356Z](W-ETOS-06/p42c-companion-restart-20261006T082527.197356Z/README.md) | PASS | 9d8ab6b11d43 |
| [W-ETOS-07/direct-image-price-refusal-20261005T200701.405209Z](W-ETOS-07/direct-image-price-refusal-20261005T200701.405209Z/README.md) | PASS | c532b77edd93 |
| [W-ETOS-07/direct-tts-import-20261005T194103.515551Z](W-ETOS-07/direct-tts-import-20261005T194103.515551Z/README.md) | PASS | d26494a498d4 |
| [W-ETOS-07/installed-media-20261005T192218.561813Z](W-ETOS-07/installed-media-20261005T192218.561813Z/README.md) | FAIL | 12d7e5c7abab |
| [W-GAME-06/final-player-manifests-20261005T201815.687151Z](W-GAME-06/final-player-manifests-20261005T201815.687151Z/README.md) | PASS | c532b77edd93 |
| [W-GAME-06/hollowmere-linux-build-20261005T183343.413775Z](W-GAME-06/hollowmere-linux-build-20261005T183343.413775Z/README.md) | FAIL | 20e34d1e6a68 |
| [W-GAME-06/hollowmere-linux-retry-20261005T195106.895058Z](W-GAME-06/hollowmere-linux-retry-20261005T195106.895058Z/README.md) | PASS | c532b77edd93 |
| [W-GAME-06/v1-gate-20261005T173334.352630Z](W-GAME-06/v1-gate-20261005T173334.352630Z/README.md) | FAIL | 20e34d1e6a68 |
| [W-GAME-06/v1-gate-retry-20261005T190359.447287Z](W-GAME-06/v1-gate-retry-20261005T190359.447287Z/README.md) | FAIL | 12d7e5c7abab |
| [W-GAME-06/v1-release-evidence-resume-20261005T200353.317063Z](W-GAME-06/v1-release-evidence-resume-20261005T200353.317063Z/README.md) | PASS | c532b77edd93 |
| [W-GAME-06/v1-resume-phases-9-11-20261005T195509.235538Z](W-GAME-06/v1-resume-phases-9-11-20261005T195509.235538Z/README.md) | FAIL | c532b77edd93 |
| W-GAME-06/v1-resumed-phases-6-10-20261005T182148.098597Z | BLOCKED (incomplete ENOSPC record retained) | unknown |
| [W-GAME-07/p42b-network-namespace-prerequisite-20261006T044240.056723Z](W-GAME-07/p42b-network-namespace-prerequisite-20261006T044240.056723Z/README.md) | BLOCKED | 8d1574e4326a |
| [W-GAME-08/final-ten-cycle-snapshots-20261005T201735.176679Z](W-GAME-08/final-ten-cycle-snapshots-20261005T201735.176679Z/README.md) | PASS | c532b77edd93 |
| [W-GAME-08/memory-20261005T185951.720007Z](W-GAME-08/memory-20261005T185951.720007Z/README.md) | BLOCKED | 38baed3d6484 |
| [W-GAME-08/memory-20261005T204457.635953Z](W-GAME-08/memory-20261005T204457.635953Z/README.md) | BLOCKED | 74005cef3447 |
| [W-GAME-08/memory-20261005T205148.674628Z](W-GAME-08/memory-20261005T205148.674628Z/README.md) | BLOCKED | 50a4c07c2021 |
| [W-GAME-08/native-memory-and-pumps-20261005T181112.083902Z](W-GAME-08/native-memory-and-pumps-20261005T181112.083902Z/README.md) | PASS | 20e34d1e6a68 |
| [W-HOST-01/companion-release-build-20261005T170828.242918Z](W-HOST-01/companion-release-build-20261005T170828.242918Z/README.md) | PASS | 813e6b4591d8 |
| [W-HOST-01/immutable-install-20261005T185516.633891Z](W-HOST-01/immutable-install-20261005T185516.633891Z/README.md) | PASS | 38baed3d6484 |
| [W-HOST-01/immutable-release-build-20261005T185321.958874Z](W-HOST-01/immutable-release-build-20261005T185321.958874Z/README.md) | PASS | 4214d67b2289 |
| [W-HOST-01/installed-hello-authority-20261005T185555.694549Z](W-HOST-01/installed-hello-authority-20261005T185555.694549Z/README.md) | PASS | 38baed3d6484 |
| [W-HOST-01/requested-status-command-20261005T194932.604898Z](W-HOST-01/requested-status-command-20261005T194932.604898Z/README.md) | FAIL | c532b77edd93 |
| [W-MECH-01/negative-semantic-explicit-paired-ui-20261005T193633.001803Z](W-MECH-01/negative-semantic-explicit-paired-ui-20261005T193633.001803Z/README.md) | FAIL | d26494a498d4 |
| [W-MECH-01/negative-semantic-installed-ui-20261005T193242.839824Z](W-MECH-01/negative-semantic-installed-ui-20261005T193242.839824Z/README.md) | FAIL | d26494a498d4 |
| [W-MECH-01/p42b-live-prerequisite-20261006T044157.689794Z](W-MECH-01/p42b-live-prerequisite-20261006T044157.689794Z/README.md) | BLOCKED | 8d1574e4326a |
| [W-MECH-01/p42c-cache-provision-20261006T071748.065567Z](W-MECH-01/p42c-cache-provision-20261006T071748.065567Z/README.md) | PASS | 65666ac27168 |
| [W-MECH-01/p42c-owner-cache-20261006T072924.288955Z](W-MECH-01/p42c-owner-cache-20261006T072924.288955Z/README.md) | PASS | 65666ac27168 |
| [W-MECH-01/p42c-owner-cache-verify-20261006T072926.021736Z](W-MECH-01/p42c-owner-cache-verify-20261006T072926.021736Z/README.md) | PASS | 65666ac27168 |
| [W-MECH-01/p42c-play-catalog-diagnostic-20261006T081636.630932Z](W-MECH-01/p42c-play-catalog-diagnostic-20261006T081636.630932Z/README.md) | FAIL | 9d8ab6b11d43 |
| [W-MECH-01/p42c-signed-receipts-20261006T090137.253975Z](W-MECH-01/p42c-signed-receipts-20261006T090137.253975Z/README.md) | PASS | 9d8ab6b11d43 |
| [W-MECH-01/p42c-stage-review-20261006T075609.509348Z](W-MECH-01/p42c-stage-review-20261006T075609.509348Z/README.md) | FAIL | 9d8ab6b11d43 |
| [W-MECH-01/p42c-stage-review-20261006T080916.723688Z](W-MECH-01/p42c-stage-review-20261006T080916.723688Z/README.md) | FAIL | 9d8ab6b11d43 |
| [W-MECH-01/p42c-stage-review-20261006T081247.078280Z](W-MECH-01/p42c-stage-review-20261006T081247.078280Z/README.md) | FAIL | 9d8ab6b11d43 |
| [W-MECH-01/p42c-stage-review-negative-20261006T082331.934190Z](W-MECH-01/p42c-stage-review-negative-20261006T082331.934190Z/README.md) | PASS | 9d8ab6b11d43 |
| [W-MECH-01/p42c-stage-submit-20261006T075219.891518Z](W-MECH-01/p42c-stage-submit-20261006T075219.891518Z/README.md) | PASS | dbedd2fb6c83 |
| [W-MECH-01/p42c-stage-submit-20261006T075911.180322Z](W-MECH-01/p42c-stage-submit-20261006T075911.180322Z/README.md) | PASS | 9d8ab6b11d43 |
| [W-MECH-01/p42c-stage-submit-20261006T080546.723368Z](W-MECH-01/p42c-stage-submit-20261006T080546.723368Z/README.md) | PASS | 9d8ab6b11d43 |
| [W-MECH-01/p42c-stage-submit-negative-20261006T082005.064618Z](W-MECH-01/p42c-stage-submit-negative-20261006T082005.064618Z/README.md) | PASS | 9d8ab6b11d43 |
| [W-MECH-01/p42d-signed-verdict-20261006T123351.066322Z](W-MECH-01/p42d-signed-verdict-20261006T123351.066322Z/README.md) | PASS | 369a0de1eb22 |
| [W-MECH-01/p42d-stage-recover-20261006T122131.388873Z](W-MECH-01/p42d-stage-recover-20261006T122131.388873Z/README.md) | FAIL | 369a0de1eb22 |
| [W-MECH-01/p42d-stage-review-20261006T120733.384847Z](W-MECH-01/p42d-stage-review-20261006T120733.384847Z/README.md) | FAIL | 369a0de1eb22 |
| [W-MECH-01/p42d-stage-review-negative-20261006T123317.023858Z](W-MECH-01/p42d-stage-review-negative-20261006T123317.023858Z/README.md) | PASS | 369a0de1eb22 |
| [W-MECH-01/p42d-stage-submit-20261006T115600.867964Z](W-MECH-01/p42d-stage-submit-20261006T115600.867964Z/README.md) | FAIL | e46d5fff79dc |
| [W-MECH-01/p42d-stage-submit-20261006T115938.454497Z](W-MECH-01/p42d-stage-submit-20261006T115938.454497Z/README.md) | PASS | e46d5fff79dc |
| [W-MECH-01/p42d-stage-submit-20261006T120325.388551Z](W-MECH-01/p42d-stage-submit-20261006T120325.388551Z/README.md) | PASS | e46d5fff79dc |
| [W-MECH-01/p42d-stage-submit-negative-20261006T123038.941727Z](W-MECH-01/p42d-stage-submit-negative-20261006T123038.941727Z/README.md) | PASS | 369a0de1eb22 |
| [W-MECH-01/pressure-plate-explicit-paired-ui-20261005T193418.507302Z](W-MECH-01/pressure-plate-explicit-paired-ui-20261005T193418.507302Z/README.md) | FAIL | d26494a498d4 |
| [W-MECH-01/pressure-plate-installed-ui-20261005T192605.266487Z](W-MECH-01/pressure-plate-installed-ui-20261005T192605.266487Z/README.md) | FAIL | d26494a498d4 |
| [W-MECH-01/semantic-analyzer-unit-20261005T192618.998527Z](W-MECH-01/semantic-analyzer-unit-20261005T192618.998527Z/README.md) | PASS | d26494a498d4 |
| [W-PLUG-12/bake-cleanproof-20261005T170620.529719Z](W-PLUG-12/bake-cleanproof-20261005T170620.529719Z/README.md) | PASS | 813e6b4591d8 |
| [W-PLUG-12/bake-cleanproof-20261005T185554.359006Z](W-PLUG-12/bake-cleanproof-20261005T185554.359006Z/README.md) | PASS | 38baed3d6484 |
| [W-PLUG-12/bake-hollowmere-20261005T170228.211803Z](W-PLUG-12/bake-hollowmere-20261005T170228.211803Z/README.md) | PASS | 813e6b4591d8 |
| [W-PLUG-12/bake-hollowmere-20261005T185517.764081Z](W-PLUG-12/bake-hollowmere-20261005T185517.764081Z/README.md) | PASS | 38baed3d6484 |
| [W-REC-01/resume-killed-editor-20261005T174227.978810Z](W-REC-01/resume-killed-editor-20261005T174227.978810Z/README.md) | FAIL | 20e34d1e6a68 |
| [W-REC-01/resume-killed-editor-20261005T202307.744055Z](W-REC-01/resume-killed-editor-20261005T202307.744055Z/README.md) | FAIL | c532b77edd93 |
| [W-REC-01/resume-reopened-editor-20261005T180654.261221Z](W-REC-01/resume-reopened-editor-20261005T180654.261221Z/README.md) | PASS | 20e34d1e6a68 |
| [W-REC-01/resume-reopened-editor-20261005T202333.359864Z](W-REC-01/resume-reopened-editor-20261005T202333.359864Z/README.md) | PASS | c532b77edd93 |
| [W-REC-01/resume-state-20261005T174227.978767Z](W-REC-01/resume-state-20261005T174227.978767Z/README.md) | PASS | 20e34d1e6a68 |
| [W-REC-01/resume-state-20261005T202307.744000Z](W-REC-01/resume-state-20261005T202307.744000Z/README.md) | PASS | c532b77edd93 |
| [W-REC-01/rollback-killed-editor-20261005T174119.215565Z](W-REC-01/rollback-killed-editor-20261005T174119.215565Z/README.md) | FAIL | 20e34d1e6a68 |
| [W-REC-01/rollback-killed-editor-20261005T202208.951931Z](W-REC-01/rollback-killed-editor-20261005T202208.951931Z/README.md) | FAIL | c532b77edd93 |
| [W-REC-01/rollback-reopened-editor-20261005T174154.867175Z](W-REC-01/rollback-reopened-editor-20261005T174154.867175Z/README.md) | PASS | 20e34d1e6a68 |
| [W-REC-01/rollback-reopened-editor-20261005T202237.272767Z](W-REC-01/rollback-reopened-editor-20261005T202237.272767Z/README.md) | PASS | c532b77edd93 |
| [W-REC-01/rollback-state-20261005T174119.215442Z](W-REC-01/rollback-state-20261005T174119.215442Z/README.md) | PASS | 20e34d1e6a68 |
| [W-REC-01/rollback-state-20261005T202208.950937Z](W-REC-01/rollback-state-20261005T202208.950937Z/README.md) | PASS | c532b77edd93 |
| [W-TOOL-02/final-harness-metadata-20261005T194717.145522Z](W-TOOL-02/final-harness-metadata-20261005T194717.145522Z/README.md) | PASS | c532b77edd93 |
| [W-TOOL-02/final-metadata-20261005T205459.094614Z](W-TOOL-02/final-metadata-20261005T205459.094614Z/README.md) | PASS | 50a4c07c2021 |
| [W-TOOL-02/metadata-20261005T170003.293967Z](W-TOOL-02/metadata-20261005T170003.293967Z/README.md) | PASS | 813e6b4591d8 |
| [W-TOOL-02/metadata-20261005T185323.090417Z](W-TOOL-02/metadata-20261005T185323.090417Z/README.md) | PASS | 4214d67b2289 |
| [W-UI-01/layout-1280x720-20261005T193653.367571Z](W-UI-01/layout-1280x720-20261005T193653.367571Z/README.md) | PASS | d26494a498d4 |
| [W-UI-01/ui-capture-20261005T190220.779562Z](W-UI-01/ui-capture-20261005T190220.779562Z/README.md) | FAIL | 12d7e5c7abab |
| [W-UI-01/ui-capture-20261005T192323.970290Z](W-UI-01/ui-capture-20261005T192323.970290Z/README.md) | FAIL | d26494a498d4 |
| [W-UI-05/p42d-selection-20261006T112100.063994Z](W-UI-05/p42d-selection-20261006T112100.063994Z/README.md) | PASS | ed1969e4cc14 |
| [W-UI-05/p42d-selection-repeat-20261006T123743.261129Z](W-UI-05/p42d-selection-repeat-20261006T123743.261129Z/README.md) | PASS | 249ce726c452 |
| [W-VIEW-01/captures-20261005T173933Z](W-VIEW-01/captures-20261005T173933Z/README.md) | PASS | 20e34d1e6a68 |
| [W-VIEW-01/views-capture-20261005T173933.469708Z](W-VIEW-01/views-capture-20261005T173933.469708Z/README.md) | PASS | 20e34d1e6a68 |
| [W-VIEW-01/views-capture-20261005T191314.272300Z](W-VIEW-01/views-capture-20261005T191314.272300Z/README.md) | PASS | 12d7e5c7abab |
| [W-VOICE-01/capture-ready-recorded-wav-20261005T194304.382344Z](W-VOICE-01/capture-ready-recorded-wav-20261005T194304.382344Z/README.md) | FAIL | d26494a498d4 |
| [W-VOICE-01/p42b-live-prerequisite-20261006T044157.715808Z](W-VOICE-01/p42b-live-prerequisite-20261006T044157.715808Z/README.md) | BLOCKED | 8d1574e4326a |
| [W-VOICE-01/p42c-fresh-destructive-20261006T074915.219505Z](W-VOICE-01/p42c-fresh-destructive-20261006T074915.219505Z/README.md) | FAIL | dbedd2fb6c83 |
| [W-VOICE-01/p42c-voice2-20261006T074412.994071Z](W-VOICE-01/p42c-voice2-20261006T074412.994071Z/README.md) | FAIL | dbedd2fb6c83 |
| [W-VOICE-01/p42d-voice-self-20261006T112419.768047Z](W-VOICE-01/p42d-voice-self-20261006T112419.768047Z/README.md) | FAIL | ed1969e4cc14 |
| [W-VOICE-01/p42d-voice-self-compile-retry-20261006T112540.255603Z](W-VOICE-01/p42d-voice-self-compile-retry-20261006T112540.255603Z/README.md) | PASS | 491bebc88c03 |
| [W-VOICE-01/p42d-voice2-20261006T112706.963706Z](W-VOICE-01/p42d-voice2-20261006T112706.963706Z/README.md) | FAIL | 274cfc7d24bb |
| [W-VOICE-01/recorded-wav-pipewire-20261005T193238.270894Z](W-VOICE-01/recorded-wav-pipewire-20261005T193238.270894Z/README.md) | FAIL | d26494a498d4 |
| [W-VOICE-01/recorded-wav-pipewire-20261005T193646.251324Z](W-VOICE-01/recorded-wav-pipewire-20261005T193646.251324Z/README.md) | FAIL | d26494a498d4 |
