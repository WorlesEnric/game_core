# rollback-reopened-editor

Verdict: **PASS**.

Source revision: `c532b77edd93099ddd1e45d20f48db5d627413b3`; host: `worlesenric`.
Started: 2026-10-05T20:22:37.273889+00:00; ended: 2026-10-05T20:23:07.740849+00:00; duration: 30.468 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2/games/hollowmere --log-dir ~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-REC-01/rollback-reopened-editor-20261005T202237.272767Z/logs --label recovery-reopen -- -executeMethod Hollowmere.P4_2.ProcessRecovery.Recover -p42State ~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-REC-01/rollback-state-20261005T202208.950937Z
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
