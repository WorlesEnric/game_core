# p42k-kernel-save

Verdict: **PASS**.

Source revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; host: `worlesenric`.
Started: 2026-10-07T18:53:12.725139+00:00; ended: 2026-10-07T18:54:45.265305+00:00; duration: 92.542 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2k/unity/GameCore.Validation --log-dir ~/wkspace/gc-studio/p4.2k/artifacts/studio/verification/W-KERNEL-01/p42k-kernel-save-20261007T185312.723379Z/logs --label p42k-kernel-save --attempts 1 --timeout 1800 --results ~/wkspace/gc-studio/p4.2k/artifacts/studio/verification/W-KERNEL-01/p42k-kernel-save-20261007T185312.723379Z/results.xml -- -runTests -testPlatform EditMode -testFilter 'GameCore.App.Tests.GameApplicationRootTests.Boot_WithACorruptedCatalog_FailsWithANamedCode_AndCreatesNoWorld|GameCore.Persistence.Tests.SaveRestoreTests.AV1SaveIsMigratedToV2OnRestore|GameCore.Persistence.Tests.SaveRestoreTests.AMissingMigrationRefusesAndLeavesTheRunningWorldUntouched'
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
