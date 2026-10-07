# p42i-kernel-save

Verdict: **PASS**.

Source revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; host: `worlesenric`.
Started: 2026-10-07T05:10:59.071425+00:00; ended: 2026-10-07T05:12:39.499740+00:00; duration: 100.429 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2i/unity/GameCore.Validation --log-dir ~/wkspace/gc-studio/p4.2i/artifacts/studio/verification/W-KERNEL-01/p42i-kernel-save-20261007T051059.070176Z/logs --label p42i-kernel-save --attempts 1 --timeout 1800 --results ~/wkspace/gc-studio/p4.2i/artifacts/studio/verification/W-KERNEL-01/p42i-kernel-save-20261007T051059.070176Z/results.xml -- -runTests -testPlatform EditMode -testFilter 'GameCore.App.Tests.GameApplicationRootTests.Boot_WithACorruptedCatalog_FailsWithANamedCode_AndCreatesNoWorld|GameCore.Persistence.Tests.SaveRestoreTests.AV1SaveIsMigratedToV2OnRestore|GameCore.Persistence.Tests.SaveRestoreTests.AMissingMigrationRefusesAndLeavesTheRunningWorldUntouched'
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
