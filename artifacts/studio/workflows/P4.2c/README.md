# P4.2c installed final-tree workflows

Product `f787829289ea7402c08917a78553ff6c3838bda8`; immutable release `0.1.0-426b3ead95f21815`.

| Workflow | Outcome | Observed result and exercised fixes |
|---|---|---|
| W-AI-01 | partial | PARTIAL: R3-F six-digit tint applies/undoes with unchanged behaviour hashes; R3-A Sprite bind and R4-A image/TTS imports pass. Two images are generated, but the unchanged P3.2 robe driver never assigns its generated robe texture through R3-D entity.setMaterialTexture. The full texture-to-material chain is unexercised. |
| W-AI-02 | partial | PARTIAL: the installed R4-C validator accepts the scoped move/patrol and ferryman candidates; both apply. The P3.2 driver has no in-Play NPC/nav assertion and tries to select Odd in Village. Ferryman Undo reports Undone but leaves the created NPC (roster 20→21→21); owner request retained. |
| W-AI-03 | partial | PARTIAL: the live candidate applies; R3-D conditional-dialogue preview hides the new line when unlit and shows it for shrine_lit=1. The existing fact is reused, so R3-F same-candidate fact creation is not exercised. This driver does not run dialogue in Play. |
| W-AI-04 | pass | The installed-companion candidate applies ui.bind: objective-line.text → vm:hud.QuestStageTitle (R3-D/D10b). The resulting authored binding is retained; separate-Editor reopen preserves it and its undo/redo/final undo succeeds. |
| W-AI-05 | partial | PARTIAL: the live scoped quest.addObjective candidate applies a Collect requirement of 2 against the real OilFlask definition, exercising R3-F/R4-C validation. The legacy simulator uses oil_flask and refuses GP-QST-004; no in-Play consequence is established. |
| W-AI-06 | fail | Saved asset hashes and all three Applied journals survive a real Editor close/reopen. HUD and quest undo/redo/final undo pass; Odd undo refuses Conflict against the first operation’s intermediate after-stamp although the second operation’s final stamp matches. Final backToBefore=false; no forced undo or candidate edit. |

Text Send passed. Image Send was accepted and produced a PNG; after the initial 180-second harness timeout, the same request was recovered without resubmission. Unity refuses its candidate: MediaImporterInvalid for textureType "Sprite (2D and UI)". The two deferred Send actions are recorded, but image import/assignment and the full novice NPC/dialogue walkthrough do not pass.

Observed media: **3 images / 5 TTS / 0 describe / no paid 3D**. Packet accounting: **USD 0.6028555818** = companion ledger USD 0.4028555818 + owner estimate USD 0.20 for the worker image. These reported counts/accounting stay below 6/6/2 and USD 10.

Image charges use tariff.kind=operator (USD 0.20 each); TTS uses tariff.kind=published. Each id, quantity, charge and provenance is retained in [paid-ledger.json](paid-ledger.json); [ledger-final.json](ledger-final.json) contains only actual companion charge records. The worker image has no companion charge and its model is not reported; its USD 0.20 is explicitly a separate operator estimate. These are local ceiling charges/estimates, not provider invoices. Unreported worker provider attempts cannot be independently reconstructed. Worker task micro_usd and any worker-generated media are separately recorded in [task-ledger.json](task-ledger.json); voice sessions have no USD column ([voice-ledger.json](voice-ledger.json)).

The [packet note](../../../../docs/studio/packets/P4.2c-live-rows.md) records the deployment correction, owner requests, exact unexercised assertions, and the forbidden node-death portion. [Reproducer](../../verification/TOOLS/README-P4.2c.md).
