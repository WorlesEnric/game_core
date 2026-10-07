# R11-A — one enrollment per definition

## Result and scope

Reference-list `assign` append now has set semantics. The exact retained P4.2k Ferryman candidate passes Candidate-mode gateway import, staging, apply and journal undo without changing its seven operations. A hand-listed duplicate still reports GP-LOG-002. No acceptance-harness deduplication, validator changes, worker execution or paid calls were used.

Evidence root: [`artifacts/studio/verification/W-AI-02/r11-a-20261007T221743Z/`](../../../artifacts/studio/verification/W-AI-02/r11-a-20261007T221743Z/). This is an EditMode product regression, not a new W-AI-02 Play or W-E2E-01 acceptance judgment. ROWS.json and matrix totals are unchanged.

## Diagnosis and product changes

The retained candidate creates `dialogue.graph`, then explicitly appends that graph to `HollowmereContent.definitions`. Generic graph creation uses the same trusted preparation as `dialogue.createGraph`, which already enrolls the graph. The old unconditional append produced the duplicate correctly refused by NarrativeBake.Plan.

- `ConfigureTools.AssignTool.AssignReference` checks existing serialized elements by Unity instance ID and managed collections by reference identity. An existing append returns Applied with `alreadyListed: true` and the existing `index`, without writes, dirtying, touched-object witnesses or inverse operations. The structured result is also serialized into the existing journal outcome `detail` field.
- Assign prepares its field inverse only after the no-op check, immediately before mutation. The engine no longer records a speculative whole-object inverse for assign. Distinct append and index replacement retain their behavior.
- `ChangeSetEngine.ProjectDefinition` uses the same identity check against the dependency-ordered detached definition graph, including projected identities. Its preview exposes `alreadyListed` and `index`. This is the product staging path, not an acceptance workaround.
- The other `arraySize++` / `items.Add(` occurrences under built-in tools are non-append replacement or compose inspection output, not separate AuthorRef append paths. `bind` delegates to assignment without append.
- `assign`, `create`, and `dialogue.createGraph` catalog documentation now explains recorded no-op append and automatic graph enrollment. Designer NPC prerequisites name both creation tools, forbid redundant `definitions` enrollment, and require one `npcs` append. `common.md` and `gc-mechanic.md` are unchanged.
- NarrativeBake, DialogueContentClosure, GP-DLG-* and GP-LOG-* checks are unchanged.

## Retained fixture and roster meaning

Fixture: `games/hollowmere/Assets/Hollowmere/Tests/P4_2/EditMode/Fixtures/FerrymanElianCandidate.json`.

- Original source: `artifacts/studio/verification/W-AI-02/p42k-npc-20261007T191317.157379Z/workflow/ferryman2/candidate.json`.
- Exact size: 6,095 bytes.
- SHA-256: `839a6c4907699a312434de3d0cd70c66804d484c1900cf3d409c1124cbb3849b`.
- The fixture remains byte-identical. In memory, `ChangeSetEngine.Rebase`, the production helper used by `Workflows.RebaseAndApply` through `CandidateCoordinator.Rebase`, refreshes only the content-set target stamp and its two base-version stamp fields. The regression asserts equality after removing stamps. [`fixture-stamps.json`](../../../artifacts/studio/verification/W-AI-02/r11-a-20261007T221743Z/fixture-stamps.json) and [`stamp-changes.json`](../../../artifacts/studio/verification/W-AI-02/r11-a-20261007T221743Z/stamp-changes.json) retain the exact refresh.

The packet's 20-entry roster is the retained workflow's **scene-entity roster**, not the `NpcRoster.asset` list. P4.2k's `roster-before.json` lists twenty AuthoredEntity objects including lanterns, crates, stones, the well and NPCs; `Workflows.Roster` enumerates those objects. The committed `NpcRoster.asset` has six NPC definitions. The regression therefore proves both real transitions: **scene entities 20 → 21 → 20**, **NPC definitions 6 → 7 → 6**, plus exactly one FerrymanElian graph enrollment. It does not fabricate fourteen extra NPC definitions.

`RetainedCandidateTests` uses a loopback retained HTTP response with the real CompanionClient, EtosAgentGateway.ImportCandidateAsync, Candidate-mode engine, apply and history. Exactly one GET is made; no task submission. Normal undo restores original content-set and NPC-roster bytes before any failure-safety restoration executes. Scene-file bytes and removal of created assets/meta files are also asserted.

## Catalog revision

Current Hollowmere product catalog, without acceptance-only injected tools:

| State | Revision |
|---|---|
| Before | `00ebe875fac2615b7a270ab55f7b76f8ac1168819333ff5228ce751c751eb52b` |
| After | `cfc50810795811b303ffe9c6f17b6fa7b8c748904913464968d6c9b77dd3c27b` |

The after catalog is exported by the real regression runtime. The prior catalog is reconstructed from that export by restoring only the three original tool doc strings; both revisions are calculated by production `ToolCatalog.ComputeRevision`. Full catalogs and [`catalog-revisions.json`](../../../artifacts/studio/verification/W-AI-02/r11-a-20261007T221743Z/catalog-revisions.json) are retained. The historical P4.2k request revision (`8d5bcc39924ca426cb4a137eceb0c8d5df5d3880c92a70514608c3c1c48b5d5e`) belongs to its installed acceptance context, not this uninjected runtime.

`tools/studio/emit_studio_schemas.py` regenerated all six schemas; `--check` passes with no schema changes because doc-string revisions are catalog content, not schema shape. Worker fixtures have no current-catalog revision pin. Companion tests compute revisions dynamically; `fixtures/r4_c/context.json` and SOURCE.json deliberately retain their historical captured catalog and digest, rather than rewriting historical evidence. Their relevant Rust suites pass.

## Verification

All Unity invocations used `studio/tools/unity-batch.sh`, one Editor slot, serially. Scoped host checks found no foreign project sessions or Editors. Free /home space stayed above 28 GB; no pruning was needed. No etosd, pairing or key-file operations occurred.

| Check | Result | Evidence |
|---|---|---|
| Final EditMode filter `P4_2;GameCore.Studio.Edit.Tests;GameCore.Studio.Views.Tests` | 144 passed, 0 failed, 6 skipped | `final-editmode.xml`, `unity-summary.json`, `unity/final-*.log` |
| Studio core assembly | 116/116 passed, including serialized/managed no-op undo, distinct append and index replacement | `final-editmode.xml` |
| Studio Views assembly | 26/26 passed | `final-editmode.xml` |
| Retained Ferryman + explicit GP-LOG-002 negative | 2/2 passed | `retained.xml`, final XML |
| .NET solution Release | 1,832 passed, 6 NotExecuted, 0 failed | `dotnet/*.trx`, `dotnet-summary.json` |
| Worker prompt suite | 7/7 passed | `workers.xml` |
| Companion retained-catalog suites | 6/6 passed | `companion-tests.json` |
| Package metadata | 42 packages / 92 assemblies; 4 engine pins / 6 game pins / 3 lock sources | `static-checks.json` |
| C# policy | 1,285 files, pass | `static-checks.json` |
| Generated schemas | 6 regenerated, subsequent check passes | `static-checks.json` |

The wrapper intentionally returns 1 for the broad final selection's six skips: four installed-node/voice/media tests require GAMECORE_ETOS_LIVE, and two layout/memory cases require a graphical Editor. They were not enabled because this packet makes no provider or graphical acceptance claim. The dedicated retained-candidate invocation exits 0. The six .NET NotExecuted cases are environment-gated ETOS client tests.

Initial receipts are retained, not relabeled: `editmode.xml` records a new test teardown error for an initially empty scene setup; `integrated.xml` records the mistaken assumption that the six-entry NPC-definition list was the twenty-entry scene roster. The first shell invocation also failed to quote the assembly-list separators, so it ran only P4_2; the subsequent quoted assembly invocation and final filter cover both full Studio suites. Those fixture/command issues were corrected without changing candidate operations or product validators.

## Reproduction and remaining acceptance

Use the evidence root as `$OUT` and the repository root as `$ROOT`:

```sh
GC_STUDIO_UNITY_SLOTS=1 GAMECORE_R11_EVIDENCE="$OUT" \
  studio/tools/unity-batch.sh --project "$ROOT/games/hollowmere" \
  --log-dir "$OUT/unity" --label retained --results "$OUT/retained.xml" \
  --attempts 1 -- -runTests -testPlatform EditMode \
  -testFilter Hollowmere.P4_2.RetainedCandidateTests
```

For the broad requested selection, use `-testFilter 'P4_2;GameCore.Studio.Edit.Tests;GameCore.Studio.Views.Tests'`; expect the six explicitly gated skips without live/graphical prerequisites. Python worker tests require pytest and jsonschema; a temporary venv was used on this host. Temporary probes/environments, Python caches and unrelated Unity-generated metadata were removed; the Unity-deleted tracked Cargo.lock.meta was restored from its preimage. No new package dependencies were introduced.

W-AI-02 Play/nav/dialogue acceptance, installed worker rollout and the next all-row same-revision judgment remain with the next packet. This change does not mark W-AI-02 or W-E2E-01 PASS.
