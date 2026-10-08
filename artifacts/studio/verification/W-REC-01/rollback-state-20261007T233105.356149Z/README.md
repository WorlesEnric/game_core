# R2-03-R2-38-rollback

Verdict: **PASS**. A real SIGKILL at the engine fault hook; the killed-editor attempt is an expected nonzero exit, retained separately. Recovery is checked in a different Editor process.

Source revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; host: `worlesenric`.
Started: 2026-10-07T23:31:35.062128+00:00; ended: 2026-10-07T23:32:07.195151+00:00; duration: 32.134 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2l/games/hollowmere --log-dir ~/wkspace/gc-studio/p4.2l/artifacts/studio/verification/W-REC-01/rollback-reopened-editor-20261007T233135.060869Z/logs --label recovery-reopen -- -executeMethod Hollowmere.P4_2.ProcessRecovery.Recover -p42State ~/wkspace/gc-studio/p4.2l/artifacts/studio/verification/W-REC-01/rollback-state-20261007T233105.356149Z
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
