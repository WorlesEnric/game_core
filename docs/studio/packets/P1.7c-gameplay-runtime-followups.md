# P1.7c gameplay runtime follow-ups

Branch: `codex/p1.7c`. Packet item IDs below are P17c-01..08, distinct from the Studio review's R2-01..41.

## R2 fixes

Implemented and verified on Linux myubuntu. No authored content or baked outputs are changed.

| Finding | Fix | Regression |
|---|---|---|
| P17c-01 | Runtime quest converter includes completion/failure action models; quest kernel commits ActionDue in the terminal transition, including failed dependents | `P17c_01_QuestTerminalActions_UseOutboxExactlyOnce` (complete/fail), `P17c_01_CompletionActionsSurviveFreshBoot`, `P17c_01_FailureActionsSurviveFreshBoot` |
| P17c-02 | NPC appearance resolver feeds the existing prefab/tint binder; views remain subject to committed visibility and residency | `P17c_02_NpcAppearanceUsesVariantPrefabAcrossResidency` |
| P17c-03 | Runtime spawning seeds definition visibility (optional override); entity.spawn preserves committed visibility unless its payload overrides it | `P17c_03_RuntimeSpawnUsesDefinitionVisibility`, `P17c_03_RespawnPreservesHiddenOverride`, dotnet `P17c_03_SpawnUsesDefaultOrExplicitVisibility` |
| P17c-04 | `QuestModule` implements and registers per-world `IQuestRuntimeQuery`; snapshots copy status, stage, branch, counts and done flags from committed slots | `P17c_04_QuestQueryRegisteredAndReadsCommittedCopy` |
| P17c-05 | Logic installation carries per-rule numeric config fields; `LogicModule.Retune` uses O-05 and reads committed effective config through RuleConfigBinding rows without touching counters | `P17c_05_RuleLiveConfigPreservesCountersAndChangesLimit` |
| P17c-06 | Presentation port settles into outbox terminal retention in the same synchronous dispatch; fresh ports consult that retention | `P17c_06_HollowmereSaveRestore_InFlightCueOnce_AndTerminalSurvivesFreshBoot` |
| P17c-07 | Each edge event's ordinal creates its own obligation; destination increments the live count once per admitted obligation, instead of repeating a stale absolute count | `P17c_07_TwoSignalsInOneStepBothCount` |
| P17c-08 | Event batch preflight runs the same manifest-driven obligation description without writes; interaction and travel also preflight before committing | `P17c_08_ExactCapacity_All256ObligationsFit_NoDrops`, `P17c_08_ManifestFanoutRefusesBeforeQuestMutation` |

## Contracts and formula

For a proposed event batch E, required open capacity is:

`D = sum(e in E) [describable action/reward deliveries(e) + matching rules with live targets(e) + matching active edge objectives(e)] + direct interaction action demands`.

Admission is `OpenCount + D <= Capacity` (256). Retained terminals consume no open capacity. The preflight uses exactly the runtime manifest models and the same payload decoders as commit; it does not allocate obligation IDs, alter slots, increment counters or enqueue rows. The legacy `HasRoom(n)` means n actual obligations. The new `IGameplayDeliveryBudget.HasRoomFor(schemas, payloads, actionDemands)` contract lives in the allowed GameplayClock.cs file; the file location is an exclusive-path constraint, not a clock dependency.

Entity spawn payloads remain compatible with existing 4/8-byte commands (preserve committed visibility). The explicit 12-byte form contains value, request ID, visibility (-1 default, 0 hidden, 1 visible). `GameplayCommands.Spawn(target, bool? visible)` and `EntitySpawner.TrySpawn(..., out target, out detail, bool? visible)` expose the override. The pre-existing one-argument pure `EntityRules.Spawn` retains its historical API semantics; production entity.spawn uses the new nullable-visibility overload.

Rule tuning uses SADR-013's per-install config-buffer option. Stable fields are `LogicModule.TuningField(ruleKey, "cooldownMs" | "maxFires")`. `LogicModule.ConfigBindings` declares the buffer rows; the buffer reads the installed effective ConfigDocument at execution, so an O-05 publication and a restore immediately select the new values. The rows are registered in `GameApplicationDefinition.ConfigBindings`; their direct per-install buffer reader is independent of derivation capability payloads. These rows do not require new baked capability schemas or mutate logic.fired/counter/cooldown deadline slots. `Retune(int ruleKey, int cooldownMs, int maxFires, out string detail)` publishes numeric changes through the composition lane. Zero maxFires means unlimited. Existing deadlines remain valid when tuning changes.

Presentation settlement uses two synchronous phases of `NarrativeDelivery.Pump`: at most 64 normal attempts, then (only when presentation receipts exist) at most 256 acknowledgement-only visits. The second phase submits no gameplay commands and presents no new cues. `Applied` and `AlreadyApplied` retain their kernel meanings; transient receipts are drained before Pump returns, and checkpoints retain the outbox terminal rows.

Query registration: `QuestModule.OnWorld` calls `NarrativeRuntime.RegisterQuery<IQuestRuntimeQuery>(this)`. For Hollowmere the current runtime is reachable through `GameBoot.Narrative.Runtime`. Consumers obtain the current world's `NarrativeRuntime.TryQuery<IQuestRuntimeQuery>(out query)` and call `TryRead(int questKey, out QuestState? state)`. No static registry or Studio dependency; reattachment registers the restored module; a disposed owner's query refuses.

## Verification

Executed on Linux myubuntu, Unity 6000.0.75f1 / .NET 8.0.425. Runtime/test revision: `3859d0c5` (final documentation commit adds no code). Unity verdicts below come from result XML, not wrapper stdout. All files are under this clone's `.unity-logs/`.

| Run | Result | XML/TRX duration | Unity process wall time | Result file |
|---|---|---:|---:|---|
| Rules.Gameplay (existing suite) | 307 passed, 0 failed | 1.962 s runner | — | `p17c-rules-final.trx` |
| P1_7c dotnet regression project | 3 passed, 0 failed | 4.741 s runner | — | `p17c-new-final.trx` |
| Hollowmere EditMode, all | 219 passed, 0 failed, 5 skipped (224 total) | 54.978 s | 95 s | `p17c-edit-all.xml` |
| Hollowmere PlayMode, all, after EditMode fixtures | 14 passed, 0 failed | 4.358 s | 40 s | `p17c-play-verified.xml` |
| Validation EditMode persistence/restore/delivery/application suites | 85 passed, 0 failed | 2.793 s | 120 s | `p17c-persistence.xml` |
| Hollowmere PlayMode, all, original committed assets restored | 14 passed, 0 failed | 4.263 s | 40 s | `p17c-play-original-content.xml` |

EditMode skips: four real ETOS tests (`GAMECORE_ETOS_LIVE=0`, no paid operations), one graphics-device preview unavailable under `-nographics`. No inconclusive cases. No test failure was hidden by a wrapper exit-code interpretation.

Red/green evidence: the same P1_7c Unity fixtures were compiled and run with all touched runtime files temporarily replaced by base `4434e6a8`, then those files were restored byte-for-byte from snapshots. `p17c-red-editmode.xml`: 10 failed / 0 passed, 0.881 s tests, 40 s process. `p17c-red-playmode.xml`: 3 failed / 0 passed, 1.292 s tests, 40 s process. Original code lost completion/failure actions, counted two signals as one, stopped admitting at row 253, forced hidden spawns visible, lacked query/config/appearance hooks, and replayed a cue after restore (2 instead of 1). The standalone original EntityRules regression likewise failed all 3 cases (`p17c-red-rules.trx`); fixed cases pass in `p17c-new-final.trx`.

The final PlayMode fixtures extend the P1.7a HollowmereSaveRestore harness with `P17c_01_CompletionActionsSurviveFreshBoot`, `P17c_01_FailureActionsSurviveFreshBoot`, and `P17c_06_HollowmereSaveRestore_InFlightCueOnce_AndTerminalSurvivesFreshBoot`. Fixtures use cloned definitions and a distinct terminal cue to distinguish the tested effect from the game's ordinary ending cue.

Initial verification caught and fixed an invalid uppercase stable config-field key and an acknowledgement telemetry regression. The first full PlayMode run also caught the need to distinguish the fixture's cue from a normal game ending cue. Final files above supersede those diagnostic runs.

Commands:

- `PATH="$HOME/.dotnet:$PATH" dotnet test dotnet/tests/GameCore.Rules.Gameplay.Tests/GameCore.Rules.Gameplay.Tests.csproj --logger 'trx;LogFileName=p17c-rules-final.trx' --results-directory "$PWD/.unity-logs"`
- Same command with `dotnet/tests/GameCore.Rules.Gameplay.Tests/P1_7c/GameCore.Rules.Gameplay.P1_7c.Tests.csproj` and `p17c-new-final.trx`.
- `GAMECORE_ETOS_LIVE=0 bash studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" --log-dir "$PWD/.unity-logs" --label p17c-edit-all --results "$PWD/.unity-logs/p17c-edit-all.xml" -- -runTests -testPlatform EditMode`.
- Same wrapper with `-testPlatform PlayMode`, labels/results listed in the table.
- Validation uses `--project "$PWD/unity/GameCore.Validation"`, `-testPlatform EditMode -testFilter 'GameCore.Persistence.Tests|GameCore.Gc018.Tests|GameCore.Gc027.Tests|GameCore.Execution.Tests.Delivery|GameCore.Execution.Tests.Observation.DelayedConsumerDeliveryTests|GameCore.App.Tests'`.

Every Unity invocation uses the host-wide lock and this packet holds at most one Editor. The first run waited 180 s for a slot and took 451 s for cold import/initial tests; subsequent runs needed no timeout retry. `bash` is used because the checked-in wrapper lacks its executable bit. Wrapper invocation records: `p17c-runs.json` and timestamped logs beside the XML.

`python3 tools/check_package_metadata.py`: pass, 41 packages / 89 package assemblies. `python3 tools/check_game_core_csharp.py`: pass, 1,087 C# files. `git diff --check`: pass. No Rust paths changed.

Bake/content audit: 9 tracked manifest/content/catalog/generated outputs are byte-identical to base `4434e6a8`; SHA-256s and the comparison are in `p17c-bake-audit.json`. No rebake is required or committed. Existing full EditMode authoring fixtures temporarily reserialized 39 tracked assets; their exact diff is retained in `p17c-authoring-fixture-changes.patch`, and every one was restored to its original bytes before the final original-content PlayMode run. No authored asset is in the packet diff.

## Requests to other packets

- `Packages/com.gamecore.gameplay.quest/Editor/QuestTools.cs`: the owner must connect `quest.inspectRuntime` to the current game's `NarrativeRuntime.TryQuery<IQuestRuntimeQuery>` and `TryRead(NarrativeRefs.KeyOf(quest), out state)`, reporting source `committed`; outside Play keep the existing explicit initial/simulated result. Runtime query is registered and has no Studio dependency. Editor and GameBoot are excluded here.
- `Packages/com.gamecore.gameplay.logic/Runtime/RuleDefinition.cs` and its Editor translator: classify only numeric `cooldownMs` and `maxFires` as Live, translating them to `LogicModule.Retune(...)`. Definition/declaration/Editor files are excluded here. The runtime config path is implemented; this packet cannot advertise the metadata/Studio integration as completed.
- `dotnet/tests/GameCore.Rules.Gameplay.Tests/GameCore.Rules.Gameplay.Tests.csproj`: add `<Compile Include="P1_7c/**/*.cs" Exclude="P1_7c/**/obj/**/*.cs;P1_7c/**/bin/**/*.cs" />` if one parent-suite invocation must include the new regression source. The parent explicitly lists subdirectories and lies outside this packet. A standalone test project under P1_7c is run meanwhile.

## Left open

- The two Editor/metadata integrations above are outside the exclusive paths; exact requested contracts are listed above.
- Presentation guarantee concerns synchronous in-process sinks and committed checkpoint boundaries. An external audio device effect cannot be atomically committed with a checkpoint; a process crash during the sink callback can replay physical playback. No external-device exactly-once guarantee is claimed.
