# P4.2 verification summary

Matrix row totals: PASS 20, BLOCKED 38, FAIL 4.

| Row | Verdict | Evidence / exact limitation |
|---|---|---|
| W-UI-01 | FAIL | [Evidence](W-UI-01/README.md): Graphical walking/picking captured and 1280×720 fits, but automatic gateway startup and R2-29 keyboard-event delivery fail; explicit pairing does not close the ordinary open-and-play path. |
| W-UI-02 | BLOCKED | [Evidence](W-UI-02/README.md): The capture includes marquee/overlap UI, but not the required three NPCs behind a fence with the resulting overlap choices. |
| W-UI-03 | BLOCKED | [Evidence](W-UI-03/README.md): Logical-object selection is captured; the lantern subpart/prefab/scope chooser sequence is not demonstrated by this driver. |
| W-UI-04 | PASS | [Evidence](W-UI-04/README.md): Ten graphical Play/Edit cycles: every measured frame has one pump and zero violations; combined engine/managed growth is +0.715% against the unchanged +15% limit. Full cycle-1/cycle-10 snapshots are retained with hashes. |
| W-UI-05 | BLOCKED | [Evidence](W-UI-05/README.md): Recorded small-sample click p95 exceeds 16 ms, but the mandatory 100-pick p95 and 500-candidate marquee dataset is absent. Neither a qualified pass nor a full-workload timing verdict is inferred. |
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
| W-EDIT-08 | BLOCKED | [Evidence](W-EDIT-08/README.md): Existing timings are isolated operations; there is no completed 20-apply single-target/Marsh p95 and 1.5k-target compose dataset. The fixed budgets are not inferred from smaller probes. |
| W-HOST-01 | BLOCKED | [Evidence](W-HOST-01/README.md): Immutable reinstall, no-op and authenticated hello pass. Full fresh install would touch provider credentials/restart etosd; node still reports TTS cost 0 after on-disk price updates, and image/describe/realtime acceptance is incomplete. |
| W-ETOS-01 | BLOCKED | [Evidence](W-ETOS-01/README.md): Both settings files are absent and proxy probes return 401. Intended committed packet evidence has no credential-shaped value or absolute home path; 3,700 inherited committed artifact files retain home paths outside this packet’s scope. |
| W-ETOS-02 | BLOCKED | [Evidence](W-ETOS-02/README.md): Live app-key access to agent-only tasks returns 403 forbidden, but the other-agent probe returns 404 agent_unknown; no installed second-agent forbidden response is established. |
| W-ETOS-04 | BLOCKED | [Evidence](W-ETOS-04/README.md): The real worker moved the selected well; no worker-side etos query of selected NPC dialogue nodes was requested or retained in that bounded live task. |
| W-ETOS-05 | BLOCKED | [Evidence](W-ETOS-05/README.md): Live client cancellation completes in 556 ms with one task/no candidate, but the creator tray-button portion is not exercised. |
| W-ETOS-06 | BLOCKED | [Evidence](W-ETOS-06/README.md): Socket reconnect/cursor replay passes; killing/restarting etosd is expressly forbidden, so the complete companion-death/node-death attribution scenario cannot run. |
| W-ETOS-07 | FAIL | [Evidence](W-ETOS-07/README.md): Legacy Unity media calls fail 404 for an unregistered local request ID. Direct TTS generation/download/import/undo and tamper refusal pass, but a new texture/import is not achieved. |
| W-ETOS-08 | BLOCKED | [Evidence](W-ETOS-08/README.md): Removing the shared image provider and reloading the node would change concurrent users’ provider service; that operator scenario is outside the permitted no-node-restart run. |
| W-ETOS-09 | BLOCKED | [Evidence](W-ETOS-09/README.md): Client cursor replay is real; a source recompile/domain reload while an in-flight task returns to the tray was not completed. Simulated reload tests remain component evidence. |
| W-VOICE-01 | FAIL | [Evidence](W-VOICE-01/README.md): Real PipeWire speech reached the microphone (44 frames, peak 0.199); the final transcript was “To lead every N P C in the village” with no partial revisions. Requests/journal/tray stayed unchanged. |
| W-AI-01 | BLOCKED | [Evidence](W-AI-01/README.md): The image tariff remains unverified and manual media request ownership is broken; no current generated robe/material/undo workflow completed. R3 texture-binding regression passes only as component evidence. |
| W-AI-02 | BLOCKED | [Evidence](W-AI-02/README.md): The packet’s one bounded text task produced a rejected well move. Ferryman dialogue/patrol/NavMesh behaviour in Play remains historical/partial P3.2 evidence, not a current end-to-end run. |
| W-AI-03 | BLOCKED | [Evidence](W-AI-03/README.md): R3-F fact handoff regressions pass, but no current real-worker conditional Odd line was produced and played; the bounded live task failed earlier on target scope. |
| W-AI-04 | BLOCKED | [Evidence](W-AI-04/README.md): The stage-title binding exists and its tests pass; no current live-worker HUD edit and creator apply is retained. |
| W-AI-05 | BLOCKED | [Evidence](W-AI-05/README.md): Quest scope inference has regression coverage, but the requested live two-oil-flask edit and consequences in Play are not completed. |
| W-AI-06 | BLOCKED | [Evidence](W-AI-06/README.md): The live edit was rejected before apply, so there is no current six-workflow undo/redo/close/reopen sequence to verify. Local journal/process recovery is reported separately. |
| W-AI-07 | BLOCKED | [Evidence](W-AI-07/README.md): Hello honestly reports 3D not_configured and no 3D provider is called. The actual refusal is 409 budget_unpriced, failing the specified not_configured/blocked-code test; provider absence and tariff absence are conflated. |
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
| W-MECH-01 | FAIL | [Evidence](W-MECH-01/README.md): Installed paired UI Stage returns 404 no owned resource for the app-origin pressure-plate and semantic-negative samples; no signed verdict, coldCache, cold/warm stage duration, Admit or undo exists. No host fallback. |
| W-GAME-01 | BLOCKED | [Evidence](W-GAME-01/README.md): The P3.1 recording is reused, as requested. It predates P3.1b and failed B-FRAME; P3.1b’s later 640×480 llvmpipe measurements cannot qualify final-tree RTX/1080p performance. |
| W-GAME-05 | BLOCKED | [Evidence](W-GAME-05/README.md): Editor full-quest endings and save/load pass; a current standalone menu→save→load→ending→restart playthrough is not re-recorded under the explicit recording-reuse instruction. |
| W-GAME-06 | PASS | [Evidence](W-GAME-06/README.md): Hollowmere Linux IL2CPP build/hash passes. V1 phases 1–8 and resumed 9–11 pass; both release resumes retain their failed setup attempts, with no repeated qualification probes or relaxed budget. |
| W-GAME-07 | BLOCKED | [Evidence](W-GAME-07/README.md): Stopping etosd is forbidden by the packet; network-namespace isolation is unavailable on this host. Historical no-socket observation does not prove this exact final-tree stopped-node scenario. |
| W-GAME-08 | PASS | [Evidence](W-GAME-08/README.md): Ten graphical Play/Edit cycles: every measured frame has one pump and zero violations; combined engine/managed growth is +0.715% against the unchanged +15% limit. Full cycle-1/cycle-10 snapshots are retained with hashes. |
| W-CLEAN-01 | PASS | [Evidence](W-CLEAN-01/README.md): Final-tree AuthorAll, content/catalog tests, fresh-cache Linux IL2CPP build and standalone 600-frame quest/save/restore/ending run pass. The separate app-stage contract test fails under W-MECH-01. |
| W-CLEAN-02 | PASS | [Evidence](W-CLEAN-02/README.md): After final AuthorAll, build and both suite rechecks, git diff against origin/main is empty for all Packages/. |
| W-DOC-01 | BLOCKED | [Evidence](W-DOC-01/README.md): No independent new-creator exercise of the draft guide is available; authoring automation cannot stand in for a novice following the document. |
| W-DOC-02 | BLOCKED | [Evidence](W-DOC-02/README.md): No developer-only-guide lever-plugin walkthrough is retained. The pressure-plate sample and existing unit tests do not establish that documentation acceptance. |
| W-E2E-01 | BLOCKED | [Evidence](W-E2E-01/README.md): The integrated tree has real live-scope, app-origin stage, media-ownership and graphical/voice failures plus explicitly unqualified budgets; all rows are disposed, not all accepted. |

## Retained attempts (including superseded and expected failures)

| Evidence | Verdict | Revision |
|---|---|---|
| [GRAPHICAL/graphics-required-tests-20261005T172641.170099Z](GRAPHICAL/graphics-required-tests-20261005T172641.170099Z/README.md) | FAIL | 3c5f817aa45a |
| [GRAPHICAL/graphics-required-tests-20261005T191202.057821Z](GRAPHICAL/graphics-required-tests-20261005T191202.057821Z/README.md) | FAIL | 12d7e5c7abab |
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
| [UNITY-HOLLOWMERE/perf-final-1-20261005T201110.316067Z](UNITY-HOLLOWMERE/perf-final-1-20261005T201110.316067Z/README.md) | PASS | c532b77edd93 |
| [UNITY-HOLLOWMERE/perf-final-2-20261005T201658.304250Z](UNITY-HOLLOWMERE/perf-final-2-20261005T201658.304250Z/README.md) | PASS | c532b77edd93 |
| [UNITY-HOLLOWMERE/perf-probe-1-20261005T181938.833666Z](UNITY-HOLLOWMERE/perf-probe-1-20261005T181938.833666Z/README.md) | PASS | 20e34d1e6a68 |
| [UNITY-HOLLOWMERE/perf-probe-2-20261005T182014.974536Z](UNITY-HOLLOWMERE/perf-probe-2-20261005T182014.974536Z/README.md) | PASS | 20e34d1e6a68 |
| [UNITY-HOLLOWMERE/playmode-20261005T172047.459311Z](UNITY-HOLLOWMERE/playmode-20261005T172047.459311Z/README.md) | PASS | 3c5f817aa45a |
| [UNITY-HOLLOWMERE/playmode-20261005T185951.720751Z](UNITY-HOLLOWMERE/playmode-20261005T185951.720751Z/README.md) | PASS | 38baed3d6484 |
| [UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z](UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md) | PASS | 74005cef3447 |
| [W-AI-07/installed-3d-refusal-tts-tamper-20261005T192945.989942Z](W-AI-07/installed-3d-refusal-tts-tamper-20261005T192945.989942Z/README.md) | FAIL | d26494a498d4 |
| [W-CLEAN-01/cleanproof-fresh-build-cache-20261005T195625.655687Z](W-CLEAN-01/cleanproof-fresh-build-cache-20261005T195625.655687Z/README.md) | PASS | c532b77edd93 |
| [W-CLEAN-01/cleanproof-linux-retry-20261005T195339.041552Z](W-CLEAN-01/cleanproof-linux-retry-20261005T195339.041552Z/README.md) | FAIL | c532b77edd93 |
| [W-CLEAN-01/final-player-600-frames-20261005T201814.524176Z](W-CLEAN-01/final-player-600-frames-20261005T201814.524176Z/README.md) | PASS | c532b77edd93 |
| [W-CLEAN-01/saltmarsh-linux-build-20261005T184220.324518Z](W-CLEAN-01/saltmarsh-linux-build-20261005T184220.324518Z/README.md) | FAIL | 20e34d1e6a68 |
| [W-CLEAN-02/package-diff-20261005T182204.124598Z](W-CLEAN-02/package-diff-20261005T182204.124598Z/README.md) | PASS | 20e34d1e6a68 |
| [W-CLEAN-02/package-diff-20261005T202208.742343Z](W-CLEAN-02/package-diff-20261005T202208.742343Z/README.md) | PASS | c532b77edd93 |
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
| [W-VIEW-01/captures-20261005T173933Z](W-VIEW-01/captures-20261005T173933Z/README.md) | PASS | 20e34d1e6a68 |
| [W-VIEW-01/views-capture-20261005T173933.469708Z](W-VIEW-01/views-capture-20261005T173933.469708Z/README.md) | PASS | 20e34d1e6a68 |
| [W-VIEW-01/views-capture-20261005T191314.272300Z](W-VIEW-01/views-capture-20261005T191314.272300Z/README.md) | PASS | 12d7e5c7abab |
| [W-VOICE-01/capture-ready-recorded-wav-20261005T194304.382344Z](W-VOICE-01/capture-ready-recorded-wav-20261005T194304.382344Z/README.md) | FAIL | d26494a498d4 |
| [W-VOICE-01/recorded-wav-pipewire-20261005T193238.270894Z](W-VOICE-01/recorded-wav-pipewire-20261005T193238.270894Z/README.md) | FAIL | d26494a498d4 |
| [W-VOICE-01/recorded-wav-pipewire-20261005T193646.251324Z](W-VOICE-01/recorded-wav-pipewire-20261005T193646.251324Z/README.md) | FAIL | d26494a498d4 |
