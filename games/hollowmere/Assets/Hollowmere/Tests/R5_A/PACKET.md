# R5-A — undo correctness and Play-safe admission

Branch `codex/r5-a`, base `c9866292`, Linux build host myubuntu. Changes are confined
to the packet's engine/journal/stage, NPC Editor, Studio gameplay adapter, R5_A tests,
and the three named packet notes. No sibling clone, installed companion, or etosd was
modified. No paid operation was requested. Credentials are used only by the existing
application client loader for authenticated verdict fetch/verify; no credential bytes
were inspected, printed, copied or retained as evidence.

## R5 / R2 fixes

| Finding | Fix | Regression |
|---|---|---|
| P4.2c #1 / R2-03 | Prepare NPC identity and delete inverse before creation; retain it through apply/undo/redo. The exact ferryman candidate stays unchanged. | `R5_01_FerrymanWitness_UndoRemovesNpc_RedoKeepsIdentity`, `R5_01_CrashAtPreparedNpcCreationRetainsDeletionAndReplay` |
| #1 audit / R2-03 | Prepare behaviour asset path/id and NPC reference preimage for `npc.setPatrol`; capture existing behaviour fields too. | `R5_01_SetPatrol_RestoresBehaviourAndCreationIdentity(False/True)` |
| #1 audit / R2-03 | Core authored create/duplicate/place/addComponent prepare their inverse and D-format identities. Gameplay OnValidate no longer replaces a noncanonical minted id on redo. Duplicate preserves added prefab components. | `R5_01_CoreCreators_PrepareIdentity_UndoRedo` (4 cases), `R5_01_CoreAssetCreators_PreparePathAndIdentity_UndoRedo` (2 cases) |
| #1 audit / R2-03 | Replace retains the original hierarchy before mutation. | `R5_01_Replace_PreparesOriginalHierarchyBeforeMutation` |
| #1 inverse safety | Refuse occupied identities and replay paths before preparing deletion; preserve replacement asset bytes/meta. | `R5_01_ExistingIdentityCannotBecomeACreationInverse` (2 cases), `R5_01_ReplayNeverDeletesAnAssetThatOccupiedTheOldPath` (2 cases) |
| P4.2c #2 | Check only each target's final postimage. Newly written outcomes all witness the committed postimage after imports. Historical intermediate-stamp journals still undo. Later creator edits still conflict. Undo and interrupted rollback reverse actual dependency execution order. | `R5_02_OddWitness_FinalPostimageUndoesAfterRuntimeReopen`, `R5_02_OddWitness_UndoesAfterEditorReopenAndDomainReload`, `R5_02_FinalPostimageStillRefusesLaterExternalEdit`, `R5_02_DependencyOrderDeterminesFinalWitnessAndInverseOrder` |
| P4.2c #3 / R2-14 | Capture/stop durably in Play; obtain and persist the real authored catalog baseline only after Play stops and before installing code. Cancellation before the baseline performs no package mutation. | `R5_03_InstalledVerdict_CapturesInPlay_VerifiesCatalogBeforeInstall` |

The NPC helper methods take only gameplay/Unity types. The fixed first-party
`IPreparedCreationAdapter` bridge in Studio gameplay is selected by the engine;
candidate types cannot register adapters. It calls the actual NPC methods. No catalog
double, candidate rewrite, unsigned verdict import, additional gameplay→Studio dependency,
or worker-visible admission operation is introduced.

## Witnesses and reproduction

- Ferryman candidate, roster 20→21→21, journal and undo receipt:
  `artifacts/studio/verification/W-AI-02/p42c-text2-20261006T071746.894107Z/workflow/ferryman2/`.
- Odd candidate:
  `artifacts/studio/verification/W-AI-03/p42c-narrative-20261006T073303.291662Z/workflow/odd-line/candidate.json`.
- Reopened Odd journal and refusal:
  `artifacts/studio/verification/W-AI-06/p42c-reopen-20261006T073813.299352Z/workflow/odd-line/`.
  The tests retain the exact old journal, including op1 `9e74ed…` and final `65fc85…`,
  after applying the exact candidate; they do not replace those stamps with new ones.
- Installed Play job `stg_1a1104007444a5815c4a406`, original request:
  `artifacts/studio/verification/W-MECH-01/p42c-stage-submit-20261006T080546.723368Z/stage-request.json`.
  The regression fetches and verifies this job over `CompanionStageService` and exercises
  actual Hollowmere SaveService plus `ReflectionAdmissionCatalog`, with no catalog double.
  This is a replay of that job's original signed context and stops by fault injection at
  the durable pre-install checkpoint. It does not install code under another revision's verdict.

Initial unmodified-code witness run: `.unity-logs/r5-a-before.xml`, **0 passed / 2 failed**.
The two failures are exactly the NPC remaining after Undone and Odd's intermediate-stamp
Conflict. Intermediate runs are retained, including fixture compilation corrections,
the creator identity/prefab-override discoveries, and an invalidated broad run where
source changes triggered compilation during Editor-update smoke tests. They are not
counted as acceptance. `.unity-logs/r5-a-focused2.xml` then records **14 passed / 1 failed**:
all witness/creation/Play cases passed, with prepared-before-create rollback exposing the
need for an idempotent internal deletion inverse. `history.deleteCreated` now treats a
never-created target as already absent and remains outside the worker catalog.

The installed-verdict regression against baseline `AdmissionLifecycle.cs` (restored from
HEAD for that run only) records **0 passed / 1 failed**, exactly `catalog_missing`, in
`.unity-logs/r5-a-play-before.xml`. The fixed file was restored before final verification.

## Creation audit

- NPC Editor: `npc.addAt` and `npc.setPatrol` are the only creating AuthorOperations.
  Other NPC operations assign existing references/values; migration and catalog contribution
  are not creators. Both creator branches and existing-behaviour mutation are covered.
- Studio gameplay: the existing world/admission services do not create authored assets.
  The new adapter supplies prepared inverses for the two NPC tools.
- Core built-ins: authored scene `create`, `duplicate`, `place`, `addComponent`, definition
  `create`/`duplicate`, and `replace` are prepared in the engine. `asset.import`/`bind`
  already prepare their asset bytes/meta/importer inverse. Delete/removeComponent retain
  restore preimages, and history restore operations carry retained bytes/ids in their input.
- The core non-authorable scenery/component variants already return final global-reference
  inverses, but lack a preallocated authoring id for the new engine preparation helper.
  They are not silently described as newly covered by the authoring-id regressions; see request below.

## Verification

Final code checkpoint: `6638db2c` (same code bytes as the final runs). Commands use `unity-batch.sh`,
one host-wide reservation at a time. `--results` supplies Unity's `-testResults`; the wrapper
rejects a duplicate explicit argument. Pass/fail/counts are read from XML, not stdout.

Required EditMode filter:
`GameCore\.Studio\.Core.*|Hollowmere\.R2_B.*|Hollowmere\.R3_A.*|Hollowmere\.P1_3.*|Hollowmere\.R5_A.*`.
The old `Hollowmere.P3_1.EditMode.Tests.P31AdmissionInPlayMode` is an EditMode UnityTest
that enters Play; its test-service/compiler/catalog doubles remain explicitly separate
from the installed-verdict regression. P3_1's PlayMode suite is run separately.

Real close/reopen sequence (two Editor processes, plus real Play/Edit domain reloads in
process two): first run `-executeMethod Hollowmere.R5_A.HistoryReopenTests.Prepare -quit`;
then run EditMode `R5_02_OddWitness_UndoesAfterEditorReopenAndDomainReload` with
`GAMECORE_R5_REOPEN=1`. The second process asserts its PID differs, retained bytes/stamp
match, normal Undo/Redo/final Undo succeed, and restores the authored fixture.

## Requests to other packets

1. **Stage owner:** `studio/stage/template/project/Assets/StageHarness/Editor/CatalogProbe.cs`,
   `WritesTheCatalogDelta()`, and `studio/agent/src/stage/pipeline.rs` catalog-delta collection
   near lines 977–1041 / verdict assembly near 1551: supply a trusted world snapshot for the
   registered source project, emit `CatalogDelta.world` and `CatalogDelta.predicted` for
   admission-capable jobs, and require both before issuing a passing admission verdict.
   They must be bound by the existing service signature. The retained installed record has
   only `mechanisms`; `AdmissionLifecycle` correctly requires `verdict.World`/`Predicted`
   at `verify` and will roll back if absent. Do not solve this with a forged local verdict
   or a candidate-supplied fingerprint.
2. **Integrator:** check the installed project mapping and stage this clone/revision once
   the catalog-delta contract is corrected. The current clone's registration was not assumed
   from another project's receipt. The map is startup configuration (`stage.rs:275`); if it
   needs changing, companion reload is an integrator action forbidden for this packet. The old job authorizes P4.2c's
   project/revision, not this branch. Re-run the complete installed compile/reload/restore/
   smoke/removal workflow with a newly signed complete catalog delta.
3. **Core Tools owner/integrator:** `Packages/com.gamecore.studio.core/Editor/Tools/BuiltIn/ComposeTools.cs`,
   `CreateTool.Apply`, `DuplicateTool.Apply`, `PlaceTool.Apply`, `AddComponentTool.Apply`:
   extend the `EditContext.PrepareInverse(Operation[], bool)` protocol with a durable identity
   for non-authorable scenery/components before construction, preserving their final global
   references. These Tools paths are outside R5-A's exclusive set. The engine helper is the
   seam for replay/path preparation; no claim is made that an object without an authoring id
   receives the authored-component crash guarantee.

## Left open

- Full current-branch installed admission is not qualified: the retained signed job has
  another project/source binding and lacks its world/predicted catalog delta. This packet
  proves the actual installed-verdict Play capture/stop/catalog checkpoint, then deliberately
  stops before code side effects. The original P31 double-based continuity test is regression
  evidence, not a substitute for the blocked real full workflow.
- Non-authorable core creation preallocation needs the Tools-owner seam above. Authored NPC,
  component and definition creation is covered by the new inverse/replay regressions.
- No ETOS node-death, companion restart, paid operation or Rust change was attempted.


## Final host results

| Check | Result |
|---|---|
| Full EditMode selection plus P2_4 and original P31 admission | **203 passed, 0 failed/skipped/inconclusive** |
| R5_A portion | **20 passed**, including installed signed-verdict/real-catalog Play checkpoint |
| Core / R2_B / R3_A / P1_3 | **83 / 71 / 3 / 17 passed** |
| Additional P2_4 / original P31 admission fixture | **8 / 1 passed** (the P31 fixture still has its documented doubles) |
| P3_1 PlayMode | **8 passed, 0 failed/skipped/inconclusive** |
| Separate Editor close/reopen + domain reload repeat | **1 passed, 0 failed/skipped/inconclusive** |
| .NET Studio Model TRX | **113 passed, 0 failed/skipped** |
| Studio/gameplay boundary Python tests | **2 passed** |
| Package metadata / C# policy / diff whitespace | **pass**, 42 packages / 91 assemblies; 1,213 C# files |

The first PlayMode invocation timed out during Editor startup after the wrapper's 600-second
silence limit (601 s total, no XML, **NotRun**). The same invocation was retried through the
wrapper and its XML records all eight passing tests. No service restart or lock bypass occurred.
Final EditMode took 113 s (test duration from XML below); successful PlayMode took 56 s.
The separate reopen processes were **2006343** (prepare) and **2012069** (verify); the test
asserted different PIDs and the retained `65fc85…` stamp before normal Undo/Redo/final Undo.
The latter repeat is also present in the full suite and is not counted as a new unique case.

Additional retained before-fix evidence:
- `r5-a-collision-before.xml`: **0 passed / 2 failed**, prepared creation inverses could name
  an existing identity. Both now pass; occupied asset replay paths also preserve bytes/meta.
- `r5-a-order-before.xml`: **0 passed / 1 failed**, Undo reported success but restored speed
  1.2 instead of the original 1.6 when declaration and dependency orders differed. It now passes.
- `r5-a-full.xml`: **197 passed / 1 failed**, a repeat fixture reused its own Undone journal;
  the fixture now creates fresh disposable journal state. No product refusal was relaxed.
- `r5-a-final.xml`: **198 passed** before the four collision/path tests and dependency-order
  test were added. `r5-a-verified.xml`: **202 passed** before the dependency-order test was added.

Exact final commands (all from this clone, one Editor at a time):

```bash
bash studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" --log-dir "$PWD/.unity-logs" --label r5-a-final203 --results "$PWD/.unity-logs/r5-a-final203.xml" -- -runTests -testPlatform EditMode -testFilter 'GameCore\.Studio\.Core.*|Hollowmere\.R2_B.*|Hollowmere\.R3_A.*|Hollowmere\.P1_3.*|Hollowmere\.R5_A.*|Hollowmere\.P2_4.*|Hollowmere\.P3_1\.EditMode\.Tests\.P31AdmissionInPlayMode.*' -saveDir "$PWD/.unity-logs/r5-a-saves"
bash studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" --log-dir "$PWD/.unity-logs" --label r5-a-playmode-retry --results "$PWD/.unity-logs/r5-a-playmode-retry.xml" -- -runTests -testPlatform PlayMode -testFilter 'Hollowmere\.P3_1.*' -saveDir "$PWD/.unity-logs/r5-a-play-saves"
bash studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" --log-dir "$PWD/.unity-logs" --label r5-a-release-reopen-prepare -- -executeMethod Hollowmere.R5_A.HistoryReopenTests.Prepare -quit
GAMECORE_R5_REOPEN=1 bash studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" --log-dir "$PWD/.unity-logs" --label r5-a-release-reopen-verify --results "$PWD/.unity-logs/r5-a-release-reopen-verify.xml" -- -runTests -testPlatform EditMode -testFilter 'Hollowmere\.R5_A\.HistoryReopenTests.*'
PATH="$HOME/.dotnet:$PATH" dotnet test dotnet/tests/GameCore.Studio.Model.Tests/GameCore.Studio.Model.Tests.csproj --logger 'trx;LogFileName=r5-a-model.trx' --results-directory "$PWD/.unity-logs/dotnet"
python3 tools/check_package_metadata.py
python3 tools/check_game_core_csharp.py
python3 Packages/com.gamecore.studio.gameplay/Tests~/test_boundary.py
git diff --check
```

XML evidence (retained under `.unity-logs/` on this host):

| XML | Passed / failed | Test seconds | SHA-256 |
|---|---|---|---|
| `r5-a-before.xml` | 0 / 2 | 6.371 | `0e8388b26f198f5fb4c28289d48f5c2532eb1fac60eea6ff1568fcbcf7367685` |
| `r5-a-play-before.xml` | 0 / 1 | 3.511 | `d1085fbe80dec5378567e0c483e6d3f68cb7a62d560c5a29bbbccb0474b189c1` |
| `r5-a-collision-before.xml` | 0 / 2 | 2.046 | `a763857dda314016f5818af3396c51850de44bd5edccc5f4d3f194a8b3cef7c0` |
| `r5-a-order-before.xml` | 0 / 1 | 2.115 | `c85448b105b110552fd86398363f07f990e71132e67e0dc548c4f183c13eaed5` |
| `r5-a-final203.xml` | 203 / 0 | 26.764 | `78a32ef921bca6efb8a5b5af425afd5dcd4a3f91cd9dc1c1472bda0c246cfc5e` |
| `r5-a-playmode-retry.xml` | 8 / 0 | 5.469 | `010d604324b4179e8359935785723ff7c10982f2ac5fdb282309155fce117cc4` |
| `r5-a-reopen-verify.xml` | 1 / 0 | 1.876 | `528c9d3ae62bf927e7eaee6f162f992bfc5228dd2e427ede9e5a742d9d4fb312` |

TRX SHA-256: `4da00ea4d3a67429f26b56901bb93b52433cf2a7caf71cad1887260d8e1e0eff`.

Final-code separate-process repeat: `r5-a-release-reopen-verify.xml`, **1 passed / 0 failed**, 1.673 test seconds, SHA-256 `f5d31ad3a10485b0750f3bee6d45f9737c73c49dabccea67f097e7a07ba70cf1`. Prepare exited 0 in 101 s; verify exited 0 in 479 s, including slow Editor startup. The earlier reopen receipt above remains historical evidence.
