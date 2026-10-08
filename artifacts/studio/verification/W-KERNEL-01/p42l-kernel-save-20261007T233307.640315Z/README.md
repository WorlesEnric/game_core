# p42l-kernel-save

Verdict: **PASS**.

Source revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; host: `worlesenric`.
Started: 2026-10-07T23:33:07.642293+00:00; ended: 2026-10-07T23:35:45.935159+00:00; duration: 158.295 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2l/unity/GameCore.Validation --log-dir ~/wkspace/gc-studio/p4.2l/artifacts/studio/verification/W-KERNEL-01/p42l-kernel-save-20261007T233307.640315Z/logs --label p42l-kernel-save --attempts 1 --timeout 1800 --results ~/wkspace/gc-studio/p4.2l/artifacts/studio/verification/W-KERNEL-01/p42l-kernel-save-20261007T233307.640315Z/results.xml -- -runTests -testPlatform EditMode -testFilter 'GameCore.App.Tests.GameApplicationRootTests.Boot_WithACorruptedCatalog_FailsWithANamedCode_AndCreatesNoWorld|GameCore.Persistence.Tests.SaveRestoreTests.AV1SaveIsMigratedToV2OnRestore|GameCore.Persistence.Tests.SaveRestoreTests.AMissingMigrationRefusesAndLeavesTheRunningWorldUntouched'
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
