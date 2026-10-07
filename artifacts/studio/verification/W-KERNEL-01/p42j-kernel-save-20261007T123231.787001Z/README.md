# p42j-kernel-save

Verdict: **PASS**.

Source revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; host: `worlesenric`.
Started: 2026-10-07T12:32:31.790925+00:00; ended: 2026-10-07T12:34:14.814921+00:00; duration: 103.026 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2j/unity/GameCore.Validation --log-dir ~/wkspace/gc-studio/p4.2j/artifacts/studio/verification/W-KERNEL-01/p42j-kernel-save-20261007T123231.787001Z/logs --label p42j-kernel-save --attempts 1 --timeout 1800 --results ~/wkspace/gc-studio/p4.2j/artifacts/studio/verification/W-KERNEL-01/p42j-kernel-save-20261007T123231.787001Z/results.xml -- -runTests -testPlatform EditMode -testFilter 'GameCore.App.Tests.GameApplicationRootTests.Boot_WithACorruptedCatalog_FailsWithANamedCode_AndCreatesNoWorld|GameCore.Persistence.Tests.SaveRestoreTests.AV1SaveIsMigratedToV2OnRestore|GameCore.Persistence.Tests.SaveRestoreTests.AMissingMigrationRefusesAndLeavesTheRunningWorldUntouched'
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
