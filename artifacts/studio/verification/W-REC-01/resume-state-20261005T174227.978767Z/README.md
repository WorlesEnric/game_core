# R2-03-R2-38-resume

Verdict: **PASS**. A real SIGKILL at the engine fault hook; the killed-editor attempt is an expected nonzero exit, retained separately. Recovery is checked in a different Editor process.

Source revision: `20e34d1e6a6800e4b0bd4a1e3ebd44611a968c55`; host: `worlesenric`.
Started: 2026-10-05T18:06:54.262408+00:00; ended: 2026-10-05T18:07:25.662963+00:00; duration: 31.402 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2/games/hollowmere --log-dir ~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-REC-01/resume-reopened-editor-20261005T180654.261221Z/logs --label recovery-reopen -- -executeMethod Hollowmere.P4_2.ProcessRecovery.Recover -p42State ~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-REC-01/resume-state-20261005T174227.978767Z
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
