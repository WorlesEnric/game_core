# rollback-reopened-editor

Verdict: **PASS**.

Source revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; host: `worlesenric`.
Started: 2026-10-07T12:30:22.696791+00:00; ended: 2026-10-07T12:31:05.390912+00:00; duration: 42.695 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2j/games/hollowmere --log-dir ~/wkspace/gc-studio/p4.2j/artifacts/studio/verification/W-REC-01/rollback-reopened-editor-20261007T123022.695868Z/logs --label recovery-reopen -- -executeMethod Hollowmere.P4_2.ProcessRecovery.Recover -p42State ~/wkspace/gc-studio/p4.2j/artifacts/studio/verification/W-REC-01/rollback-state-20261007T122851.849146Z
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
