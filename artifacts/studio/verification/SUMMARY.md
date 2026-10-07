# P4.2h verification summary

Matrix row totals: PASS 61, BLOCKED 7, FAIL 0.

| Row | Verdict | Evidence / exact limitation |
|---|---|---|
| W-UI-01 | PASS | [Evidence](W-UI-01/README.md): Ordinary graphical Open Studio connects automatically without pairing; HUD walking moves committed world.posX/posZ 7.636 m in 3.040 s. Runtime NPC view tags now override the authored GameBoot ancestor; Select shows Maren and MarenEntity in the actual definition card. Graphical R2-29 key delivery and focus-reset regressions pass. |
| W-UI-02 | PASS | [Evidence](W-UI-02/README.md): R7-D graphical 1280×720 marquee-result chooser presents three NPCs and fence geometry; real inclusion toggles exclude fence, then Apply selects exactly Maren/Odd/Pip and retains regionRect. Point choices confirm occlusion. Real chooser captures and PASS receipt retained. |
| W-UI-03 | PASS | [Evidence](W-UI-03/README.md): R7-D graphical 1280×720 Lantern chooser exposes logical, Mesh:Body, originating prefab and Instance scope choices. Attached button interactions assert exact identities, scopes and cleared parts; all four choices are visible in real captures. |
| W-UI-04 | PASS | [Evidence](W-UI-04/README.md): Ten graphical Play/Edit cycles: every measured frame has one pump and zero violations; combined engine/managed growth is +0.715% against the unchanged +15% limit. Full cycle-1/cycle-10 snapshots are retained with hashes. |
| W-UI-05 | PASS | [Evidence](W-UI-05/README.md): CORE-PICK on main 40fb91fa passes both datasets: each has 100 picks and 100 marquees over 500 distinct candidates. Pick p95 1.2553/0.8460 ms; marquee p95 0.3410/0.1970 ms, against unchanged 16/50 ms budgets. The separate 21-update timing regression also passes; combined XML 3/3. |
| W-VIEW-01 | PASS | [Evidence](W-VIEW-01/README.md): Final-main view suite: 41/41 passed, zero skips. P4.2 graphical captures retained with their original revision; no new final-main display capture is claimed. |
| W-VIEW-02 | PASS | [Evidence](W-VIEW-02/README.md): Final-main view suite: 41/41 passed, zero skips. P4.2 graphical captures retained with their original revision; no new final-main display capture is claimed. |
| W-VIEW-03 | PASS | [Evidence](W-VIEW-03/README.md): Final-main view suite: 41/41 passed, zero skips. P4.2 graphical captures retained with their original revision; no new final-main display capture is claimed. |
| W-VIEW-04 | PASS | [Evidence](W-VIEW-04/README.md): Final-main view suite: 41/41 passed, zero skips. P4.2 graphical captures retained with their original revision; no new final-main display capture is claimed. |
| W-VIEW-05 | PASS | [Evidence](W-VIEW-05/README.md): Final-main view suite: 41/41 passed, zero skips. P4.2 graphical captures retained with their original revision; no new final-main display capture is claimed. |
| W-VIEW-06 | PASS | [Evidence](W-VIEW-06/README.md): Final-main view suite: 41/41 passed, zero skips. P4.2 graphical captures retained with their original revision; no new final-main display capture is claimed. |
| W-MODEL-02 | PASS | [Evidence](W-MODEL-02/README.md): R7-B actual delete preview identifies the dialogue choice line through its item condition and the collect objective by nested field paths; engine deletion and ordinary History undo succeed. Exact case passes in final 8/8 XML. |
| W-TOOL-01 | PASS | [Evidence](W-TOOL-01/README.md): Saltmarsh exports only installed production tools; fixture, Hollowmere and internal admission tools are absent. Catalog and installed-package manifest retained. |
| W-TOOL-02 | PASS | [Evidence](W-TOOL-02/README.md): Package metadata and exact asmdef-derived dependencies pass on the final harness tree. |
| W-EDIT-01 | PASS | [Evidence](W-EDIT-01/README.md): P4.2h fresh installed-worker Instance-scoped runtime.move candidate applies unchanged in real Play. History joins task t9450022d9a74ef2db96f895e with the exact committed GameCore operation; production receipt and pose confirm Bram moved to (11.5,0,-13), with authored scene bytes unchanged. |
| W-EDIT-02 | PASS | [Evidence](W-EDIT-02/README.md): Five-op stale-member cases prove AllOrNothing applies nothing and BestEffort records the other outcomes. |
| W-EDIT-03 | PASS | [Evidence](W-EDIT-03/README.md): P4.2h one real USD0.20 portrait passes current media import, normal History undo/redo and final undo. Generated/applied/redone PNG SHA256 is identical; all post-generation charge/producer checkpoints remain unchanged. An observer initially wrongly required optional jobId; its failure is retained and corrected without regenerating. Normal History continuation reuses the original Applied journal after reopening. |
| W-EDIT-04 | PASS | [Evidence](W-EDIT-04/README.md): R7-B combined real Bram NPC command/staged edit, Play exit, authored apply, engine deletion and second apply produces a journaled Refused/StaleTarget without resurrection. Normal History undo restores original costume and scene bytes. |
| W-EDIT-05 | PASS | [Evidence](W-EDIT-05/README.md): Core and viewport gizmo tests compare the resulting journal entries with typed moves. |
| W-EDIT-06 | PASS | [Evidence](W-EDIT-06/README.md): R7-B real committed world.place leaves authored proxy unchanged; Changes creator command persists a distinct authored candidate. Play exit, ordinary move/save/reopen and History inverse/reopen pass. Runtime action remains non-undoable; final XML 8/8 includes refusal boundaries. |
| W-EDIT-07 | PASS | [Evidence](W-EDIT-07/README.md): Per-operation conflict/rebase tests pass; the retained real worker candidate also refuses with StaleContext after an actual registry revision change. |
| W-EDIT-08 | PASS | [Evidence](W-EDIT-08/README.md): single: p95 [55.8581, 63.073600000000006] ms, median 59.4659 ms, budget 200.0 ms; region: p95 [157.6064, 79.8135] ms, median 118.7100 ms, budget 1000.0 ms; prepare: p95 [4.3048, 10.177200000000001] ms, median 7.2410 ms, budget 300.0 ms |
| W-HOST-01 | BLOCKED | [Evidence](W-HOST-01/README.md): P4.2e activates and checksum-verifies immutable main-built release 0.1.0-cac2f82c59be070b; the repeat installer is a no-op and authenticated hello reports the owner describe tariff. A full fresh node install remains unrun under the no-etosd-restart rule. Initial host inventory is idle, but the monitor later sees a brief second Unity PID during NPC asset import; its exited process cannot be classified retrospectively, so strict host exclusivity is not claimed. |
| W-ETOS-01 | BLOCKED | [Evidence](W-ETOS-01/README.md): Both settings files are absent and proxy probes return 401. Intended committed packet evidence has no credential-shaped value or absolute home path; 3,700 inherited committed artifact files retain home paths outside this packet’s scope. |
| W-ETOS-02 | BLOCKED | [Evidence](W-ETOS-02/README.md): Live app-key access to agent-only tasks returns 403 forbidden, but the other-agent probe returns 404 agent_unknown; no installed second-agent forbidden response is established. |
| [W-ETOS-04/p42h-query-20261006T233851.413333Z](W-ETOS-04/p42h-query-20261006T233851.413333Z/README.md) | FAIL | a77cb38ba4a2 |
| W-ETOS-05 | PASS | [Evidence](W-ETOS-05/README.md): P4.2h actual enabled attached tray Cancel button is activated while request, task and tray report running. One original task becomes cancelled, no candidate or duplicate row appears; acknowledgement 427 ms, stable observation 3585 ms. |
| W-ETOS-06 | BLOCKED | [Evidence](W-ETOS-06/README.md): Companion portion PASS: actual etos agent restart, same task, one cancellation outcome and exact four-event cursor replay; reconnect 143.1 ms, cancel ack 188.0 ms. Node-death portion remains BLOCKED: etosd stop/restart is forbidden, and a companion restart cannot prove delayed attribution after node death. |
| W-ETOS-07 | BLOCKED | [Evidence](W-ETOS-07/README.md): P4.2e authenticated hello and one describe call pass with the owner-declared operator estimate USD 0.01/call. One new image is generated and downloaded with digest verification at USD 0.20; describe consumes that owned artifact and records its binding charge. Current client tests cover tamper refusal, but this run does not perform the full live generated-texture import/tamper workflow; prior image-import evidence remains revision-specific. |
| W-ETOS-08 | BLOCKED | [Evidence](W-ETOS-08/README.md): Removing the shared image provider and reloading the node would change concurrent users’ provider service; that operator scenario is outside the permitted no-node-restart run. |
| W-ETOS-09 | PASS | [Evidence](W-ETOS-09/README.md): P4.2h real changed-source compilation/domain reload in the same Editor preserves running task tec77a34b9f6f55d58ed934f5 and one attached tray row with unchanged creation identity. Loaded source token and compilation/reload callbacks are retained; session starts 11-to-12. Durable cursor 760 continues with exact production/independent replay [761,762], no gap/duplicate and one terminal event; creator tray cancellation then bounds task lifetime. |
| W-VOICE-01 | BLOCKED | [Evidence](W-VOICE-01/README.md): The unchanged R6-B driver now passes both readiness-gated real takes: move and destructive final transcripts arrive after proper Stop/drain. Destructive tray/request/journal counts remain exactly 8/11/122, and the field holds the destructive text without Send. Both provider traces contain only revision 1 with final=true; no partial speech revision is observed. Full-row partial-revision visibility remains unqualified despite the passing driver. |
| W-AI-01 | PASS | [Evidence](W-AI-01/README.md): P4.2h second real USD0.20 image is green woven cloth, imported through current media and assigned by explicit engine entity.setMaterialTexture. Actual GameBoot/New Game Maren body uses that texture via production property block; visible olive-to-forest-green cloth change and normal History restoration are captured. Behaviour/NPC/dialogue/village source hashes, runtime profile/route/schedule and structural recipe remain unchanged. Existing stylized capsule geometry and background graphics are not claimed improved. |
| W-AI-02 | PASS | [Evidence](W-AI-02/README.md): P4.2g fresh installed-worker six-operation candidate cs_01M49DK7R5Z2YD985AHZG0EWEN applies unchanged after creator-supplied measured navigation, shared-prefab Play evidence and explicit new-NPC intent. Its graph is enrolled in GameplayContentSet. Actual WorkflowPlayChecks.Effect("W-AI-02", ...) proves committed patrol motion, on-NavMesh view, successful DialogueRunner.Start, the presented bell line and conversation end. R6-C AppliedOr reads the durable journal; normal undo journals Undone, restores roster 20-to-21-to-20, and is explicitly saved. Final integrated XML is 45/45 PASS, including the unchanged entry-unreachable refusal. Earlier clarification-only requests, prerequisite-driver errors, the lost-observation receipt and unsaved-fixture bake refusal remain retained. |
| W-AI-03 | PASS | [Evidence](W-AI-03/README.md): The fresh installed-worker candidate applies unchanged; actual Hollowmere Play hides its added line while shrine_lit is false and displays it when lit. The corrected driver records pass. |
| W-AI-04 | PASS | [Evidence](W-AI-04/README.md): The fresh installed-worker HUD candidate applies and saves; a separate Editor reopens with matching saved hashes, and normal undo/redo/final undo succeeds. HUD bytes return exactly to baseline. |
| W-AI-05 | PASS | [Evidence](W-AI-05/README.md): The fresh installed-worker candidate uses the indexed OilFlask identity. Actual Play holds quest stage 1 after one flask and advances to stage 2 after two; the driver records pass. |
| W-AI-06 | PASS | [Evidence](W-AI-06/README.md): Unchanged retained narrative candidates save in Editor 4005226 and reopen in separate Editor 4007734 with all saved hashes equal. Normal undo/redo/final undo plus the production deterministic bake restores all four complete asset files exactly, including contentStamp: backToBefore=true. No fields or candidate stamps are normalized; no new paid generation. |
| W-AI-07 | PASS | [Evidence](W-AI-07/README.md): Installed R6-main companion refuses 3D with not_configured before a zero budget; no generation. The authenticated P4.2e receipt passes in TRX. FBX-only mesh import and predictions provider credentials remain an explicit owner decision. |
| W-PLUG-01 | PASS | [Evidence](W-PLUG-01/README.md): R7-C real three-region loop passes moved Odd, zero departed views/source clips, unloaded native audio data and six Memory Profiler snapshots per run. Residuals 0.08825–0.15878% of peak; both passing runs meet load ≤2 s/unload ≤1 s. Reloadable 702-byte clip descriptors remain included in the residual. |
| W-PLUG-02 | PASS | [Evidence](W-PLUG-02/README.md): R7-C production binders drive real Animator transitions and animated poses; despawn destroys the old Animator and respawn retains variant prefab, 1.2 scale and committed bindings. Named XML passes in graphics-enabled batch Editor. |
| W-PLUG-03 | PASS | [Evidence](W-PLUG-03/README.md): R7-C two identical 110-frame traces prove walking is blocked by a solid 0.8 m ledge, Jump clears its lip airborne, descends and lands grounded. Existing real walking/focus/prompt/dispatch/travel case passes. |
| W-PLUG-04 | PASS | [Evidence](W-PLUG-04/README.md): Fresh-boot restore retains the unloaded NPC’s captured slots, including PatrolIndex, and gameplay patrol tests assert committed progression. |
| W-PLUG-05 | PASS | [Evidence](W-PLUG-05/README.md): The actual Causeway Gate condition explain names the failed condition; committed locked-gate refusal and condition-controlled travel are exercised. |
| W-PLUG-06 | PASS | [Evidence](W-PLUG-06/README.md): Conditional dialogue differs by fact; fresh-boot restoration preserves heard_rumour/gate_open and reattaches conversations. |
| W-PLUG-07 | PASS | [Evidence](W-PLUG-07/README.md): Both quest branches pass; failure closes dependents transitively, cycles refuse, and the actual LanternLost failure ending is reached. |
| W-PLUG-08 | PASS | [Evidence](W-PLUG-08/README.md): R7-C 32 actual barn interactions across cooldown and production save/restore leave exactly one lantern; canonical saved/restored hash matches and the replacement world handles further retries. Final 10-case XML passes. |
| W-PLUG-09 | PASS | [Evidence](W-PLUG-09/README.md): The real Hollowmere logic why-not trace names the failed condition and the change that would make it true. |
| W-PLUG-10 | PASS | [Evidence](W-PLUG-10/README.md): R7-E real context inspector, validator console and live ETOS gateway with fake transport report identical GP-ENT-006 code/message/canonical subject for one missing-prefab definition; candidate refuses without writes. Full EditMode XML 181/181, Rules 309/309; generated voice bank enrollment and History inverses pass. |
| W-PLUG-11 | PASS | [Evidence](W-PLUG-11/README.md): R7-C real source overlap and native-release XML pass; the missing voice-bank enrollment is fixed. Final Linux IL2CPP monitor recording identifies Maren greeting (0.591 correlation), village before travel (0.464) and marsh after (0.455), with mismatched-region controls <0.075. Native residuals ≤0.159%; no paid calls. |
| W-PLUG-12 | PASS | [Evidence](W-PLUG-12/README.md): Both final-tree rebakes/Entry.Verify pass without output changes; byte identity and single-definition revision tests pass. Structural recipe revisions did not change, preserving existing save compatibility. |
| W-KERNEL-01 | PASS | [Evidence](W-KERNEL-01/README.md): Production GameApplication boot with a corrupted catalog returns CatalogFingerprintMismatch and creates no world. |
| W-PERSIST-01 | PASS | [Evidence](W-PERSIST-01/README.md): Belfry save/fresh-boot restore preserves canonical slots, items/facts, resident region and in-flight work exactly once; real game mid-quest restore continues to ending C. |
| W-PERSIST-02 | PASS | [Evidence](W-PERSIST-02/README.md): R7-B literally renames the referenced NPC prefab after real checkpoint capture; a separate Editor PID reopens and restores identical canonical slot hash, eight NPC fields and costume content. Original prefab/meta path and bytes restored exactly. |
| W-PERSIST-03 | PASS | [Evidence](W-PERSIST-03/README.md): Production SaveService restores a V1 checkpoint through the registered V2 migration; missing migration refuses while leaving the running world unchanged. |
| W-REC-01 | PASS | [Evidence](W-REC-01/README.md): Real SIGKILL during engine mutation, then a different Editor process: both rollback and resume recover successfully. Killed-Editor nonzero exits are expected and retained. |
| [W-REC-03/p42h-prerequisite](W-REC-03/p42h-prerequisite/README.md) | BLOCKED | a77cb38ba4a2 |
| W-MECH-01 | PASS | [Evidence](W-MECH-01/README.md): P4.2g installed main after R6-E/R6-F/R6-G: cold/warm signed Docker stages pass in 158.011/79.598 s, each 36 EditMode + 2 PlayMode XML passes. Explicit graphical Play Admit restores nine coins, completes 120-frame Pending-to-Passed smoke and normal undo twice. Cold durable-UTC admission is 42.869 s; corrected warm timer 47.070 s. Owned UPM admission/undo resolves are 16.622/19.119 s cold and 15.891/19.461 s warm. Both reload traces establish authenticated service through the R6-E resumer with zero refresh waits. No admission timeout or timeout retry occurred. Negative app candidate cannot Admit; separate production Docker Roslyn refuses 21 findings across SG001-SG010. A pre-Admit wrong-filename launch and the old cold harness timezone arithmetic remain retained, not rewritten. |
| W-GAME-01 | BLOCKED | [Evidence](W-GAME-01/README.md): P4.2h current-source release Linux IL2CPP, real RTX4060Ti on :1 at 1080p: four actual >600-second recordings. VSync OFF literal B-FRAME PASS twice: p95 3.461/3.552 ms, zero frames >100 ms outside transition windows. VSync ON FAIL twice: p95 17.375/17.314 ms; outside-transition >100 ms counts 2/0. All full routes, saves and recordings pass; transition hitches <=122.590 ms. Owner VSync definition remains open; no blended or relaxed verdict. Failed recorder setup attempts remain retained. |
| W-GAME-05 | PASS | [Evidence](W-GAME-05/README.md): R7-C fc3245ea Linux IL2CPP recording passes production menu/save/UI Quit, second-process menu/load, Ending C and Play Again reset. Final 350.2 s 1080p movie has actual voice/ambience, exact saved-state assertions and two exit-0 processes; Xvfb/llvmpipe, not B-FRAME. |
| W-GAME-06 | PASS | [Evidence](W-GAME-06/README.md): Hollowmere Linux IL2CPP build/hash passes. V1 phases 1–8 and resumed 9–11 pass; both release resumes retain their failed setup attempts, with no repeated qualification probes or relaxed budget. |
| W-GAME-07 | BLOCKED | [Evidence](W-GAME-07/README.md): The owner forbids etosd stop/restart, so the requested stopped-node/no-network player scenario is not run. The companion-only supervisor restart does not establish this condition; the prior namespace prerequisite refusal remains historical evidence. |
| W-GAME-08 | PASS | [Evidence](W-GAME-08/README.md): Ten graphical Play/Edit cycles: every measured frame has one pump and zero violations; combined engine/managed growth is +0.715% against the unchanged +15% limit. Full cycle-1/cycle-10 snapshots are retained with hashes. |
| W-CLEAN-01 | PASS | [Evidence](W-CLEAN-01/README.md): Final-tree AuthorAll, content/catalog tests, fresh-cache Linux IL2CPP build and standalone 600-frame quest/save/restore/ending run pass. The separate app-stage contract test fails under W-MECH-01. |
| W-CLEAN-02 | PASS | [Evidence](W-CLEAN-02/README.md): After final AuthorAll, build and both suite rechecks, git diff against origin/main is empty for all Packages/. |
| W-DOC-01 | PASS | [Evidence](W-DOC-01/README.md): The creator-guide NPC/dialogue boundary is exercised through npc.addAt in Context and Add line in the Dialogue view, using the existing Maren definition and its bound graph. Placement and dialogue edit apply; save and normal journal undo restore the 20-entity/13-node baseline. Named guide test passes in XML. This is the documented existing-definition flow, not creation of a new unique NPC definition. |
| [W-DOC-02/r7-a](W-DOC-02/r7-a/README.md) | PASS | 1a462f88 |
| W-E2E-01 | BLOCKED | [Evidence](W-E2E-01/README.md): P4.2h fresh complete-row PASS at product a77cb38b: W-AI-01, W-EDIT-01, W-EDIT-03, W-ETOS-05, W-ETOS-09. W-ETOS-04 lacks real dialogue-node publication; W-REC-03 lacks running-stage cancellation; W-GAME-01 has separate OFF PASS/ON FAIL measurements with the owner rule open. The other 59 rows were not rerun as complete scenarios at this revision; inherited PASS is not current acceptance. Current retained-dialogue regressions are 35/37, with two projection failures, and eight transient empty-argv children leave strict host exclusivity unproven. All 68 exact dispositions are listed. |

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
| [INSTALL-P4.2e/p42e-receipt-hello-20261006T143912.780456Z](INSTALL-P4.2e/p42e-receipt-hello-20261006T143912.780456Z/README.md) | PASS | 26c971a61ab0 |
| [INSTALL-P4.2f/p42f-receipt-hello-20261006T165823.107949Z](INSTALL-P4.2f/p42f-receipt-hello-20261006T165823.107949Z/README.md) | PASS | 4ac7ba858b91 |
| [INSTALL-P4.2f/p42f-receipt-hello-20261006T173136.849346Z](INSTALL-P4.2f/p42f-receipt-hello-20261006T173136.849346Z/README.md) | PASS | 55f0a4dfe3cb |
| [INSTALL-P4.2g/p42g-acceptance-final-20261006T201908.749448Z](INSTALL-P4.2g/p42g-acceptance-final-20261006T201908.749448Z/README.md) | PASS | e4bb29fb83c9 |
| [INSTALL-P4.2g/p42g-acceptance-python-20261006T190954.919424Z](INSTALL-P4.2g/p42g-acceptance-python-20261006T190954.919424Z/README.md) | FAIL | 55091b74be95 |
| [INSTALL-P4.2g/p42g-acceptance-python-20261006T195344.292879Z](INSTALL-P4.2g/p42g-acceptance-python-20261006T195344.292879Z/README.md) | FAIL | e4bb29fb83c9 |
| [INSTALL-P4.2g/p42g-acceptance-python-corrected-20261006T191305.444836Z](INSTALL-P4.2g/p42g-acceptance-python-corrected-20261006T191305.444836Z/README.md) | PASS | 55091b74be95 |
| [INSTALL-P4.2g/p42g-acceptance-python-final-20261006T195349.581179Z](INSTALL-P4.2g/p42g-acceptance-python-final-20261006T195349.581179Z/README.md) | PASS | e4bb29fb83c9 |
| [INSTALL-P4.2g/p42g-activate-20261006T190756.214896Z](INSTALL-P4.2g/p42g-activate-20261006T190756.214896Z/README.md) | PASS | 55091b74be95 |
| [INSTALL-P4.2g/p42g-cache-20261006T191101.465754Z](INSTALL-P4.2g/p42g-cache-20261006T191101.465754Z/README.md) | PASS | 55091b74be95 |
| [INSTALL-P4.2g/p42g-cargo-clippy-20261006T190106.315550Z](INSTALL-P4.2g/p42g-cargo-clippy-20261006T190106.315550Z/README.md) | PASS | 55091b74be95 |
| [INSTALL-P4.2g/p42g-cargo-fmt-20261006T190105.636068Z](INSTALL-P4.2g/p42g-cargo-fmt-20261006T190105.636068Z/README.md) | PASS | 55091b74be95 |
| [INSTALL-P4.2g/p42g-cargo-release-20261006T190215.463170Z](INSTALL-P4.2g/p42g-cargo-release-20261006T190215.463170Z/README.md) | PASS | 55091b74be95 |
| [INSTALL-P4.2g/p42g-cargo-test-20261006T190121.961345Z](INSTALL-P4.2g/p42g-cargo-test-20261006T190121.961345Z/README.md) | PASS | 55091b74be95 |
| [INSTALL-P4.2g/p42g-csharp-20261006T190920.483046Z](INSTALL-P4.2g/p42g-csharp-20261006T190920.483046Z/README.md) | PASS | 55091b74be95 |
| [INSTALL-P4.2g/p42g-csharp-20261006T195314.198353Z](INSTALL-P4.2g/p42g-csharp-20261006T195314.198353Z/README.md) | PASS | e4bb29fb83c9 |
| [INSTALL-P4.2g/p42g-csharp-final-20261006T201847.370380Z](INSTALL-P4.2g/p42g-csharp-final-20261006T201847.370380Z/README.md) | PASS | e4bb29fb83c9 |
| [INSTALL-P4.2g/p42g-final-audit-20261006T202130.827982Z](INSTALL-P4.2g/p42g-final-audit-20261006T202130.827982Z/README.md) | FAIL | e4bb29fb83c9 |
| [INSTALL-P4.2g/p42g-metadata-20261006T190920.040173Z](INSTALL-P4.2g/p42g-metadata-20261006T190920.040173Z/README.md) | PASS | 55091b74be95 |
| [INSTALL-P4.2g/p42g-metadata-20261006T195313.699519Z](INSTALL-P4.2g/p42g-metadata-20261006T195313.699519Z/README.md) | PASS | e4bb29fb83c9 |
| [INSTALL-P4.2g/p42g-metadata-final-20261006T201847.060881Z](INSTALL-P4.2g/p42g-metadata-final-20261006T201847.060881Z/README.md) | PASS | e4bb29fb83c9 |
| [INSTALL-P4.2g/p42g-receipt-hello-20261006T190806.394631Z](INSTALL-P4.2g/p42g-receipt-hello-20261006T190806.394631Z/README.md) | PASS | 55091b74be95 |
| [INSTALL-P4.2g/p42g-runner-python-20261006T190954.983043Z](INSTALL-P4.2g/p42g-runner-python-20261006T190954.983043Z/README.md) | PASS | 55091b74be95 |
| [INSTALL-P4.2g/p42g-runner-python-20261006T195344.349346Z](INSTALL-P4.2g/p42g-runner-python-20261006T195344.349346Z/README.md) | PASS | e4bb29fb83c9 |
| [P4.2h/p42h-harness-check-20261006T232700.601491Z](P4.2h/p42h-harness-check-20261006T232700.601491Z/README.md) | FAIL | a77cb38ba4a2 |
| [P4.2h/p42h-harness-check-fixed-20261006T232846.529458Z](P4.2h/p42h-harness-check-fixed-20261006T232846.529458Z/README.md) | FAIL | a77cb38ba4a2 |
| [P4.2h/p42h-hello-20261006T230811.468514Z](P4.2h/p42h-hello-20261006T230811.468514Z/README.md) | PASS | a77cb38ba4a2 |
| [P4.2h/p42h-python-acceptance-20261007T012629.626551Z](P4.2h/p42h-python-acceptance-20261007T012629.626551Z/README.md) | PASS | 44f2b4ba6996 |
| [P4.2h/p42h-python-runner-20261007T012633.846092Z](P4.2h/p42h-python-runner-20261007T012633.846092Z/README.md) | PASS | 44f2b4ba6996 |
| [R6-P4.2e/p42e-regression-20261006T143611.682920Z](R6-P4.2e/p42e-regression-20261006T143611.682920Z/README.md) | FAIL | 26c971a61ab0 |
| [R6-P4.2e/p42e-regression-20261006T143939.136591Z](R6-P4.2e/p42e-regression-20261006T143939.136591Z/README.md) | PASS | 26c971a61ab0 |
| [R6-P4.2f/p42f-regression-20261006T165922.104070Z](R6-P4.2f/p42f-regression-20261006T165922.104070Z/README.md) | PASS | 4ac7ba858b91 |
| [R6-P4.2f/p42f-regression-20261006T175113.703121Z](R6-P4.2f/p42f-regression-20261006T175113.703121Z/README.md) | PASS | 611897b46638 |
| [R6-P4.2g/p42g-regression-20261006T190828.261688Z](R6-P4.2g/p42g-regression-20261006T190828.261688Z/README.md) | FAIL | 55091b74be95 |
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
| [W-AI-01/p42h-robe-20261006T234742.071996Z](W-AI-01/p42h-robe-20261006T234742.071996Z/README.md) | PASS | a77cb38ba4a2 |
| [W-AI-02/p42b-live-prerequisite-20261006T044154.619482Z](W-AI-02/p42b-live-prerequisite-20261006T044154.619482Z/README.md) | BLOCKED | 8d1574e4326a |
| [W-AI-02/p42c-text2-20261006T071746.894107Z](W-AI-02/p42c-text2-20261006T071746.894107Z/README.md) | FAIL | 65666ac27168 |
| [W-AI-02/p42d-text2-20261006T113349.342620Z](W-AI-02/p42d-text2-20261006T113349.342620Z/README.md) | FAIL | 274cfc7d24bb |
| [W-AI-02/p42e-cleanup-npc-20261006T151151.965982Z](W-AI-02/p42e-cleanup-npc-20261006T151151.965982Z/README.md) | PASS | c047978f7382 |
| [W-AI-02/p42e-text2-20261006T150123.555155Z](W-AI-02/p42e-text2-20261006T150123.555155Z/README.md) | FAIL | c047978f7382 |
| [W-AI-02/p42f-npc-20261006T174113.356793Z](W-AI-02/p42f-npc-20261006T174113.356793Z/README.md) | FAIL | 55f0a4dfe3cb |
| [W-AI-02/p42f-text2-20261006T173140.444632Z](W-AI-02/p42f-text2-20261006T173140.444632Z/README.md) | FAIL | 55f0a4dfe3cb |
| [W-AI-02/p42g-npc-20261006T192937.013650Z](W-AI-02/p42g-npc-20261006T192937.013650Z/README.md) | FAIL | e4bb29fb83c9 |
| [W-AI-02/p42g-npc-confirmed-20261006T195105.737185Z](W-AI-02/p42g-npc-confirmed-20261006T195105.737185Z/README.md) | FAIL | e4bb29fb83c9 |
| [W-AI-02/p42g-npc-creation-20261006T195723.136865Z](W-AI-02/p42g-npc-creation-20261006T195723.136865Z/README.md) | PASS | e4bb29fb83c9 |
| [W-AI-02/p42g-npc-evidence-20261006T200512.485799Z](W-AI-02/p42g-npc-evidence-20261006T200512.485799Z/README.md) | FAIL | e4bb29fb83c9 |
| [W-AI-02/p42g-npc-evidence-final-20261006T201140.214123Z](W-AI-02/p42g-npc-evidence-final-20261006T201140.214123Z/README.md) | PASS | e4bb29fb83c9 |
| [W-AI-02/p42g-npc-view-20261006T193621.206649Z](W-AI-02/p42g-npc-view-20261006T193621.206649Z/README.md) | FAIL | e4bb29fb83c9 |
| [W-AI-02/p42g-npc-view-20261006T193713.622912Z](W-AI-02/p42g-npc-view-20261006T193713.622912Z/README.md) | FAIL | e4bb29fb83c9 |
| [W-AI-02/p42g-npc-view-20261006T193908.376967Z](W-AI-02/p42g-npc-view-20261006T193908.376967Z/README.md) | FAIL | e4bb29fb83c9 |
| [W-AI-02/p42g-npc-view-20261006T194115.635199Z](W-AI-02/p42g-npc-view-20261006T194115.635199Z/README.md) | FAIL | e4bb29fb83c9 |
| [W-AI-02/p42g-npc-view-20261006T194419.372785Z](W-AI-02/p42g-npc-view-20261006T194419.372785Z/README.md) | FAIL | e4bb29fb83c9 |
| [W-AI-02/p42g-npc-view-20261006T195015.674865Z](W-AI-02/p42g-npc-view-20261006T195015.674865Z/README.md) | PASS | e4bb29fb83c9 |
| [W-AI-02/p42g-regression-final-20261006T192656.457919Z](W-AI-02/p42g-regression-final-20261006T192656.457919Z/README.md) | FAIL | 55091b74be95 |
| [W-AI-02/p42g-regression-final-20261006T201600.167908Z](W-AI-02/p42g-regression-final-20261006T201600.167908Z/README.md) | PASS | e4bb29fb83c9 |
| [W-AI-03/p42b-live-prerequisite-20261006T044156.183935Z](W-AI-03/p42b-live-prerequisite-20261006T044156.183935Z/README.md) | BLOCKED | 8d1574e4326a |
| [W-AI-03/p42c-narrative-20261006T073303.291662Z](W-AI-03/p42c-narrative-20261006T073303.291662Z/README.md) | FAIL | dbedd2fb6c83 |
| [W-AI-03/p42d-narrative-20261006T114113.853565Z](W-AI-03/p42d-narrative-20261006T114113.853565Z/README.md) | PASS | 951a05eb084f |
| [W-AI-03/p42e-narrative-20261006T151348.703137Z](W-AI-03/p42e-narrative-20261006T151348.703137Z/README.md) | PASS | c047978f7382 |
| [W-AI-04/p42b-live-prerequisite-20261006T044156.192058Z](W-AI-04/p42b-live-prerequisite-20261006T044156.192058Z/README.md) | BLOCKED | 8d1574e4326a |
| [W-AI-05/p42b-live-prerequisite-20261006T044156.198991Z](W-AI-05/p42b-live-prerequisite-20261006T044156.198991Z/README.md) | BLOCKED | 8d1574e4326a |
| [W-AI-06/p42b-live-prerequisite-20261006T044156.738894Z](W-AI-06/p42b-live-prerequisite-20261006T044156.738894Z/README.md) | BLOCKED | 8d1574e4326a |
| [W-AI-06/p42c-reopen-20261006T073813.299352Z](W-AI-06/p42c-reopen-20261006T073813.299352Z/README.md) | FAIL | dbedd2fb6c83 |
| [W-AI-06/p42d-history-prepare-20261006T115218.627176Z](W-AI-06/p42d-history-prepare-20261006T115218.627176Z/README.md) | PASS | e46d5fff79dc |
| [W-AI-06/p42d-history-reopen-20261006T115409.820866Z](W-AI-06/p42d-history-reopen-20261006T115409.820866Z/README.md) | PASS | e46d5fff79dc |
| [W-AI-06/p42d-reopen-20261006T114958.044810Z](W-AI-06/p42d-reopen-20261006T114958.044810Z/README.md) | PASS | e46d5fff79dc |
| [W-AI-06/p42e-reopen-20261006T152055.608370Z](W-AI-06/p42e-reopen-20261006T152055.608370Z/README.md) | FAIL | e178f6d291ca |
| [W-AI-06/r7-a](W-AI-06/r7-a/README.md) | PASS | 1a462f88 |
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
| [W-DOC-02/r7-a](W-DOC-02/r7-a/README.md) | PASS | 1a462f88 |
| [W-DOC-02/r7-a](W-DOC-02/r7-a/README.md) | PASS | 1a462f88 |
| [W-DOC-02/r7-a](W-DOC-02/r7-a/README.md) | PASS | 1a462f88 |
| [W-E2E-01/p42h-accounting](W-E2E-01/p42h-accounting/README.md) | BLOCKED | a77cb38ba4a2 |
| [W-EDIT-01/p42h-history-capture-20261006T235319.527257Z](W-EDIT-01/p42h-history-capture-20261006T235319.527257Z/README.md) | FAIL | a77cb38ba4a2 |
| [W-EDIT-01/p42h-history-capture-fixed-20261006T235831.511301Z](W-EDIT-01/p42h-history-capture-fixed-20261006T235831.511301Z/README.md) | FAIL | a77cb38ba4a2 |
| [W-EDIT-01/p42h-move-20261006T233010.771874Z](W-EDIT-01/p42h-move-20261006T233010.771874Z/README.md) | PASS | a77cb38ba4a2 |
| [W-EDIT-03/p42h-portrait-20261006T233329.764957Z](W-EDIT-03/p42h-portrait-20261006T233329.764957Z/README.md) | FAIL | a77cb38ba4a2 |
| [W-EDIT-03/p42h-portrait-history-20261006T233621.554895Z](W-EDIT-03/p42h-portrait-history-20261006T233621.554895Z/README.md) | PASS | a77cb38ba4a2 |
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
| [W-ETOS-04/p42h-query-20261006T233851.413333Z](W-ETOS-04/p42h-query-20261006T233851.413333Z/README.md) | FAIL | a77cb38ba4a2 |
| [W-ETOS-05/p42h-cancel-20261006T234228.357309Z](W-ETOS-05/p42h-cancel-20261006T234228.357309Z/README.md) | PASS | a77cb38ba4a2 |
| [W-ETOS-06/installed-stream-resume-cancel-20261005T185854.961200Z](W-ETOS-06/installed-stream-resume-cancel-20261005T185854.961200Z/README.md) | PASS | 38baed3d6484 |
| [W-ETOS-06/p42b-live-prerequisite-20261006T044157.822009Z](W-ETOS-06/p42b-live-prerequisite-20261006T044157.822009Z/README.md) | BLOCKED | 8d1574e4326a |
| [W-ETOS-06/p42c-companion-restart-20261006T082527.197356Z](W-ETOS-06/p42c-companion-restart-20261006T082527.197356Z/README.md) | PASS | 9d8ab6b11d43 |
| [W-ETOS-07/direct-image-price-refusal-20261005T200701.405209Z](W-ETOS-07/direct-image-price-refusal-20261005T200701.405209Z/README.md) | PASS | c532b77edd93 |
| [W-ETOS-07/direct-tts-import-20261005T194103.515551Z](W-ETOS-07/direct-tts-import-20261005T194103.515551Z/README.md) | PASS | d26494a498d4 |
| [W-ETOS-07/installed-media-20261005T192218.561813Z](W-ETOS-07/installed-media-20261005T192218.561813Z/README.md) | FAIL | 12d7e5c7abab |
| [W-ETOS-07/p42e-describe-20261006T143956.171469Z](W-ETOS-07/p42e-describe-20261006T143956.171469Z/README.md) | PASS | 26c971a61ab0 |
| [W-ETOS-09/p42h-reload-20261006T234520.220306Z](W-ETOS-09/p42h-reload-20261006T234520.220306Z/README.md) | PASS | a77cb38ba4a2 |
| [W-GAME-01/p42h-player](W-GAME-01/p42h-player/README.md) | BLOCKED | a77cb38ba4a2 |
| [W-GAME-01/p42h-player-setup-desktop](W-GAME-01/p42h-player-setup-desktop/README.md) | FAIL | a77cb38ba4a2 |
| [W-GAME-01/p42h-player-setup-final](W-GAME-01/p42h-player-setup-final/README.md) | FAIL | a77cb38ba4a2 |
| [W-GAME-01/p42h-player-setup-recorded](W-GAME-01/p42h-player-setup-recorded/README.md) | FAIL | a77cb38ba4a2 |
| [W-GAME-01/p42h-player-setup-window](W-GAME-01/p42h-player-setup-window/README.md) | FAIL | a77cb38ba4a2 |
| [W-GAME-01/p42h-player-setup-window2](W-GAME-01/p42h-player-setup-window2/README.md) | FAIL | a77cb38ba4a2 |
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
| [W-MECH-01/p42e-receipt-stage-20261006T144507.225982Z](W-MECH-01/p42e-receipt-stage-20261006T144507.225982Z/README.md) | PASS | 739a5298fe5f |
| [W-MECH-01/p42e-receipt-stage-20261006T145342.508359Z](W-MECH-01/p42e-receipt-stage-20261006T145342.508359Z/README.md) | PASS | 739a5298fe5f |
| [W-MECH-01/p42e-receipt-stage-20261006T145345.507609Z](W-MECH-01/p42e-receipt-stage-20261006T145345.507609Z/README.md) | PASS | 739a5298fe5f |
| [W-MECH-01/p42e-stage-review-20261006T144510.582506Z](W-MECH-01/p42e-stage-review-20261006T144510.582506Z/README.md) | FAIL | 739a5298fe5f |
| [W-MECH-01/p42e-stage-review-20261006T145348.148583Z](W-MECH-01/p42e-stage-review-20261006T145348.148583Z/README.md) | FAIL | 739a5298fe5f |
| [W-MECH-01/p42e-stage-review-negative-20261006T150052.077560Z](W-MECH-01/p42e-stage-review-negative-20261006T150052.077560Z/README.md) | PASS | 739a5298fe5f |
| [W-MECH-01/p42e-stage-submit-20261006T144109.661452Z](W-MECH-01/p42e-stage-submit-20261006T144109.661452Z/README.md) | PASS | 26c971a61ab0 |
| [W-MECH-01/p42e-stage-submit-20261006T145038.813597Z](W-MECH-01/p42e-stage-submit-20261006T145038.813597Z/README.md) | PASS | 739a5298fe5f |
| [W-MECH-01/p42e-stage-submit-negative-20261006T145836.138161Z](W-MECH-01/p42e-stage-submit-negative-20261006T145836.138161Z/README.md) | PASS | 739a5298fe5f |
| [W-MECH-01/p42f-receipt-stage-20261006T170710.410489Z](W-MECH-01/p42f-receipt-stage-20261006T170710.410489Z/README.md) | FAIL | 55f0a4dfe3cb |
| [W-MECH-01/p42f-receipt-stage-20261006T170942.026722Z](W-MECH-01/p42f-receipt-stage-20261006T170942.026722Z/README.md) | PASS | 55f0a4dfe3cb |
| [W-MECH-01/p42f-receipt-stage-20261006T172113.588454Z](W-MECH-01/p42f-receipt-stage-20261006T172113.588454Z/README.md) | PASS | 55f0a4dfe3cb |
| [W-MECH-01/p42f-stage-review-20261006T170945.223444Z](W-MECH-01/p42f-stage-review-20261006T170945.223444Z/README.md) | FAIL | 55f0a4dfe3cb |
| [W-MECH-01/p42f-stage-review-20261006T171323.900006Z](W-MECH-01/p42f-stage-review-20261006T171323.900006Z/README.md) | FAIL | 55f0a4dfe3cb |
| [W-MECH-01/p42f-stage-review-20261006T172116.443048Z](W-MECH-01/p42f-stage-review-20261006T172116.443048Z/README.md) | FAIL | 55f0a4dfe3cb |
| [W-MECH-01/p42f-stage-review-20261006T172434.103813Z](W-MECH-01/p42f-stage-review-20261006T172434.103813Z/README.md) | FAIL | 55f0a4dfe3cb |
| [W-MECH-01/p42f-stage-review-negative-20261006T172910.619626Z](W-MECH-01/p42f-stage-review-negative-20261006T172910.619626Z/README.md) | PASS | 55f0a4dfe3cb |
| [W-MECH-01/p42f-stage-submit-20261006T170309.995129Z](W-MECH-01/p42f-stage-submit-20261006T170309.995129Z/README.md) | PASS | 4ac7ba858b91 |
| [W-MECH-01/p42f-stage-submit-20261006T171632.907547Z](W-MECH-01/p42f-stage-submit-20261006T171632.907547Z/README.md) | PASS | 55f0a4dfe3cb |
| [W-MECH-01/p42f-stage-submit-negative-20261006T172735.631334Z](W-MECH-01/p42f-stage-submit-negative-20261006T172735.631334Z/README.md) | PASS | 55f0a4dfe3cb |
| [W-MECH-01/p42g-receipt-stage-20261006T191613.095454Z](W-MECH-01/p42g-receipt-stage-20261006T191613.095454Z/README.md) | PASS | 55091b74be95 |
| [W-MECH-01/p42g-receipt-stage-20261006T191613.151367Z](W-MECH-01/p42g-receipt-stage-20261006T191613.151367Z/README.md) | PASS | 55091b74be95 |
| [W-MECH-01/p42g-receipt-stage-20261006T192303.876009Z](W-MECH-01/p42g-receipt-stage-20261006T192303.876009Z/README.md) | PASS | 55091b74be95 |
| [W-MECH-01/p42g-semantic-20261006T191238.035251Z](W-MECH-01/p42g-semantic-20261006T191238.035251Z/README.md) | PASS | 55091b74be95 |
| [W-MECH-01/p42g-stage-review-20261006T191631.705512Z](W-MECH-01/p42g-stage-review-20261006T191631.705512Z/README.md) | FAIL | 55091b74be95 |
| [W-MECH-01/p42g-stage-review-20261006T191739.034437Z](W-MECH-01/p42g-stage-review-20261006T191739.034437Z/README.md) | PASS | 55091b74be95 |
| [W-MECH-01/p42g-stage-review-20261006T192308.082336Z](W-MECH-01/p42g-stage-review-20261006T192308.082336Z/README.md) | PASS | 55091b74be95 |
| [W-MECH-01/p42g-stage-review-negative-20261006T192617.136441Z](W-MECH-01/p42g-stage-review-negative-20261006T192617.136441Z/README.md) | PASS | 55091b74be95 |
| [W-MECH-01/p42g-stage-submit-20261006T191228.517592Z](W-MECH-01/p42g-stage-submit-20261006T191228.517592Z/README.md) | PASS | 55091b74be95 |
| [W-MECH-01/p42g-stage-submit-20261006T192039.511265Z](W-MECH-01/p42g-stage-submit-20261006T192039.511265Z/README.md) | PASS | 55091b74be95 |
| [W-MECH-01/p42g-stage-submit-negative-20261006T192510.393381Z](W-MECH-01/p42g-stage-submit-negative-20261006T192510.393381Z/README.md) | PASS | 55091b74be95 |
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
| [W-REC-03/p42h-prerequisite](W-REC-03/p42h-prerequisite/README.md) | BLOCKED | a77cb38ba4a2 |
| [W-TOOL-02/final-harness-metadata-20261005T194717.145522Z](W-TOOL-02/final-harness-metadata-20261005T194717.145522Z/README.md) | PASS | c532b77edd93 |
| [W-TOOL-02/final-metadata-20261005T205459.094614Z](W-TOOL-02/final-metadata-20261005T205459.094614Z/README.md) | PASS | 50a4c07c2021 |
| [W-TOOL-02/metadata-20261005T170003.293967Z](W-TOOL-02/metadata-20261005T170003.293967Z/README.md) | PASS | 813e6b4591d8 |
| [W-TOOL-02/metadata-20261005T185323.090417Z](W-TOOL-02/metadata-20261005T185323.090417Z/README.md) | PASS | 4214d67b2289 |
| [W-UI-01/layout-1280x720-20261005T193653.367571Z](W-UI-01/layout-1280x720-20261005T193653.367571Z/README.md) | PASS | d26494a498d4 |
| [W-UI-01/r7-a](W-UI-01/r7-a/README.md) | PASS | 1a462f88 |
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
| [W-VOICE-01/p42e-voice2-20261006T152310.669342Z](W-VOICE-01/p42e-voice2-20261006T152310.669342Z/README.md) | PASS | e178f6d291ca |
| [W-VOICE-01/recorded-wav-pipewire-20261005T193238.270894Z](W-VOICE-01/recorded-wav-pipewire-20261005T193238.270894Z/README.md) | FAIL | d26494a498d4 |
| [W-VOICE-01/recorded-wav-pipewire-20261005T193646.251324Z](W-VOICE-01/recorded-wav-pipewire-20261005T193646.251324Z/README.md) | FAIL | d26494a498d4 |
