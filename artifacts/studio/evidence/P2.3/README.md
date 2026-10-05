# P2.3 studio-views: graphical evidence

How it was made:
- Host `myubuntu`, Unity 6000.0.75f1, an interactive Editor on display `:1` (not batch mode).
- Run directory `~/wkspace/gc-studio/p2.3/.evidence/P2.3-20261005T043254Z`. The Editor exited with code 0 after 141 s.
- The script `studio/tools/evidence-p2.3.sh` drives `GameCore.Studio.Views.Evidence.StudioViewsEvidence.Run`. That entry opens each view in a 1280x720 window over a Studio runtime for `games/hollowmere`, with a temporary state root.
- Each PNG holds the window's own pixels, read through `GUIView.GrabPixels` into a render texture. The desktop is never captured. No `import` fallback was needed in this run.
- `capture.json` is the step log; `editor-excerpt.txt` contains the Editor's `[P2.3 evidence]` lines.

**Host-only verification patch.** On the commit under test, `com.gamecore.studio.core` rejects the gameplay packages' `[AuthorField(Type = "authoringId")]`, so dialogue graphs, quests and their tools are not indexed (see PACKET.md, left open).
- For this run, and for the "patched" test run, the host copy had a three-line alias of `authoringId` to `string` in `AuthoringIdentity`, `AuthoringMetadata` and `ToolCatalogBuilder`.
- The patch was applied after the sync and never committed.
- Everything else in these shots comes from committed code and unmodified Hollowmere content.
- The evidence run changed no asset. Its two journal entries were undone, and `git status` on the host shows only the patch plus Unity's own ProjectSettings rewrites.

| File | View | What it shows |
|---|---|---|
| [01-relationships-maren.png](01-relationships-maren.png) | W-VIEW-01 Relationships | Maren (npc.definition) at depth 2: 16 nodes and 22 links, built in 2.4 ms. Cards include her MarenEntity, MarenBehaviour ("2 patrol points"), the Maren dialogue graph (the string `dialogueGraph: dialogue.maren` resolved by name), BellRung, MarenIntro, DrownedBell and the placed Maren entity ("ThornwickVillage - Resident"). The selected Maren card's details list each link with its field (`[entity]`, `[behaviour]`, `[dialogueGraph]`, `[npcs]`). At this fitted zoom the canvas hides edge labels; they are drawn once you zoom in. |
| [02-relationships-impact-lantern.png](02-relationships-impact-lantern.png) | W-VIEW-01 impact | "Impact of deleting" the Lantern affects 8 nodes, with counts by type. Direct referrers: DrownedBell `rewards[0].target`, MarshLoot `entries[1].item`, HollowmereContent `definitions`. Transitive: MarenIntro, ReturnToMaren, the Maren graph, Maren, NpcRoster. |
| [03-dialogue-maren-preview.png](03-dialogue-maren-preview.png) | W-VIEW-02 Dialogue | Maren's graph (8 nodes, 7 edges) with the entry Branch `if BellRung` selected. The inspector shows the condition picker, ports and the connect form. With the fact `bell_rung=1`, `dialogue.preview` returns "[0] branch fact bell_rung >= 1 -> yes; [7] Maren: You rang it! ...", and that line's card is highlighted. |
| [04-quests-drowned-bell.png](04-quests-drowned-bell.png) | W-VIEW-03 Quests | The Drowned Bell: 4 stage columns, 8 objectives, the pay/persuade branch badges and 3 rewards. The inspector holds the reward editors. `quest.simulate` on the path generated for branch 1 reports "completed at stage 3 via pay Odd", with the events listed. |
| [05-world-hollowmere.png](05-world-hollowmere.png) | W-VIEW-04 World | 3 regions and the portal triangle, two directed edges per portal. Thornwick Village is the start region, Resident (its scene is open), with 16 indexed objects and spawn (0,0,-12). The other two regions are Unloaded. Forms on the right: connect, add portal (it warns that `world.addPortal` cannot bind `region`) and spawn point. The NPC schedule strip is underneath. |
| [06-tables-items.png](06-tables-items.png) | W-VIEW-05 Tables | The Items tab (inventory.item): OldCoin, Lantern, GateKey and BellClapper, with inline editors for displayName, maxStack, weight (g) and price. The toolbar has "apply to selected rows" and CSV. |
| [07-changes-conflict.png](07-changes-conflict.png) | W-VIEW-06 Pending inspector | An Agent candidate (2 ops, op2 dependsOn op1). op1 targets the Lantern with a stale stamp. The rendered op tree shows the `Conflict` diagnostic with expected/actual stamps from `data`, the Rebase hint, and a "go to" button. |
| [08-changes-dependencies.png](08-changes-dependencies.png) | W-VIEW-06 Dependencies | 34 registered com.gamecore packages laid out by layer (kernel, rules, unity, gameplay, studio), with no package metadata problems. |
| [09-changes-journal.png](09-changes-journal.png) | W-VIEW-06 Journal timeline | Two manual table change sets: a row commit and a 3-row bulk, both later undone. The stamp diff between them shows the before/after stamps per target. |
| [10-canvas-2000-nodes.png](10-canvas-2000-nodes.png) | Canvas at 2,000 nodes | A synthetic tree of 2,000 nodes (fan-out 13, depth 3), fitted, so the canvas draws the compact rectangles. Graph plus neighbourhood took 8.4 ms. Layout: 1 slice, at most 2.84 ms. Refresh 0.13 ms; edge paint 4.71 ms. |
| [11-quests-live.png](11-quests-live.png) | W-VIEW-03 in Play Mode | Boot.unity is running. The gameplay bridge found `NarrativeWorld via GameBoot.Narrative`. Objectives carry the live "open" badges, and the status line reads "live inactive stage 0" (the quest has not started). The "Last decisions" sidebar is empty because no rule has fired yet. |
| [12-world-live.png](12-world-live.png) | W-VIEW-04 in Play Mode | Live residency from the running world: Thornwick Village Resident, Blackmere Marsh and Drowned Belfry Unloaded. The region action reads "Travel here". |

All 12 files are 1280x746 (the window plus its tab strip) and under 300 KB.
