# R2-03-R2-38-resume

Verdict: **PASS**. A real SIGKILL at the engine fault hook; the killed-editor attempt is an expected nonzero exit, retained separately. Recovery is checked in a different Editor process.

Source revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; host: `worlesenric`.
Started: 2026-10-07T18:52:40.877116+00:00; ended: 2026-10-07T18:53:12.532331+00:00; duration: 31.657 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2k/games/hollowmere --log-dir ~/wkspace/gc-studio/p4.2k/artifacts/studio/verification/W-REC-01/resume-reopened-editor-20261007T185240.875221Z/logs --label recovery-reopen -- -executeMethod Hollowmere.P4_2.ProcessRecovery.Recover -p42State ~/wkspace/gc-studio/p4.2k/artifacts/studio/verification/W-REC-01/resume-state-20261007T185206.701320Z
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
