# rollback-killed-editor

Verdict: **FAIL**.

Source revision: `20e34d1e6a6800e4b0bd4a1e3ebd44611a968c55`; host: `worlesenric`.
Started: 2026-10-05T17:41:19.216787+00:00; ended: 2026-10-05T17:41:54.865564+00:00; duration: 35.650 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2/games/hollowmere --log-dir ~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-REC-01/rollback-killed-editor-20261005T174119.215565Z/logs --label recovery-crash -- -executeMethod Hollowmere.P4_2.ProcessRecovery.Crash -p42State ~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-REC-01/rollback-state-20261005T174119.215442Z -p42Recovery rollback
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
