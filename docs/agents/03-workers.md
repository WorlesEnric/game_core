# 03. The workers

Two persistent, text-only, offline workers do all the reasoning; neither ever edits the project. Both run on `echo/gpt-6-sol` (alias `fast`) because the preferred `echo/claude-opus-5-5` alias has returned 401 from Echo since 2026-10-04; `describe` runs separately on `echo/gpt-5.6-sol`.

| | gc-designer | gc-mechanic |
| --- | --- | --- |
| Role | Turns a creator request into a change set of typed operations the edit engine previews, validates and applies. | Writes a new mechanism as a self-contained UPM package (`Rules/` with no engine references, `Runtime/`, `Editor/`, `Tests/`), runs `dotnet test`, and hands over `package.tgz`, `proposal.json` and a one-op `mechanism.propose` change set with `applyRequirement: Compile`. |
| Selected when | Default, and mode `design`. | Mode `mechanism`, or the `mechanism.propose` tool path. |
| Instructions | `studio/etos/agent/workers/gc-designer.md` (+ `common.md`) | `studio/etos/agent/workers/gc-mechanic.md` (+ `common.md`) |

## Inputs

Files the companion uploads with the task: `request.md` (change-set id, worker, index revision, tool-catalog revision, attempt number, re-ask note, resource-graph query guidance with the owner hash, truncation note, and a five-point output contract), `tool-catalog.json`, `selection.json`, `index-slice.json` (depth 2, 64 KB, may say `truncated`), optional `frame.png`, `diagnostics.json` on a re-ask, and the creator's attachments. The bounded scene-context attachment (`gamecore.studio.scenecontext/1`: name, hierarchy path, scene, position, rotation, scale and AuthoringRef per object; caps 64 KiB, 128 objects, 512 characters per string) arrives only as a generic attachment: neither prompt names it.

## Output contract (`gamecore.studio.changeset/1`)

`id`, `intent{text, origin:"agent"}`, `selection` copied as given, `baseVersions`, `operations[]` each with `opId`, catalog `tool`, `target` AuthoringRef with stamp, `args`, `dependsOn` (earlier ops only, acyclic), `preconditions: "stamp"`, `applyRequirement`; plus `artifacts[]` referenced as `{"artifact":"sha256:<hex>"}` with real digests, and `requirements`. Forbidden: invented object ids, `null` anywhere, `state`, `outcomes`, `timestamps.applied`, `links.gameCoreOps`. When the worker cannot proceed it writes only `/outputs/clarification.json` with one question.

## Validation after the worker (companion `candidate.rs`)

Exactly one schema-valid change set, matching id, unique op ids, acyclic `dependsOn`, artifact digest and size, every artifact used, candidate mode and the null rule, catalog rules (`UnknownTool`, `InvalidArgs`, `ScopeNotAllowed`), and `StaleContext` when the catalog is gone or mismatched. An invalid candidate gets exactly one re-ask (`<id>.r1`) with `diagnostics.json`; `StaleContext` is never re-asked. Engine-side rules the worker never sees by name but must satisfy on the change set's final projected state: GP-DLG-001 empty graph, GP-DLG-003 dangling edge, GP-DLG-005 unreachable node, `npc_dialogue_not_enrolled`, and the gameplay definition validators.

## Rules added by the fix waves

All are tested under `studio/etos/agent/workers/tests/`.

- R5-B importer contract (both workers): emit exact case-sensitive C# enum literals such as `{"textureType":"Sprite","spriteImportMode":"Single","spritePixelsPerUnit":100}`; never `"Sprite (2D and UI)"`, `"Normal map"`, integer enum values or guessed settings. The allowed subset is `importer-contract.json`.
- R6-B dialogue reachability (designer): "Every retained node must remain reachable from the proposed entry through Next, branch Else, or choice Option links … Never change entry to a new terminal line merely to make it speak first, and never remove existing nodes to silence the diagnostic."
- R6-C NPC prerequisites (designer): at least two distinct patrol points via `npc.setPatrol`, a resolvable graph via `npc.setDialogue` with an entry-reachable line, NavMeshAgent, displacement thresholds and roster entry; "never invent ids, silently omit patrol/dialogue, or claim W-AI-02 passed". A worked ferryman example follows the rule.
- R6-F changed no prompt text; it made the installer upgrade stored worker definitions on release so a changed `.md` is actually what the node runs.

Sources: `studio/etos/agent/agent.toml:35-47`, `studio/etos/agent/workers/gc-designer.md:3-173`, `gc-mechanic.md:3-85`, `common.md:1-17`, `studio/agent/src/desk.rs:540-683`, `studio/agent/src/candidate.rs:1-49`, `Packages/com.gamecore.studio.etos/Editor/AgentRequestBuilder.cs:20-155`, `Packages/com.gamecore.gameplay.contracts/Runtime/Narrative/NarrativeCatalogNames.cs:169-173`, `Packages/com.gamecore.studio.gameplay/Editor/DialogueClosureTools.cs:168-226`.
