# R2-03-R2-38-resume

Verdict: **PASS**. A real SIGKILL at the engine fault hook; the killed-editor attempt is an expected nonzero exit, retained separately. Recovery is checked in a different Editor process.

Source revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; host: `worlesenric`.
Started: 2026-10-07T05:09:22.418767+00:00; ended: 2026-10-07T05:10:58.930140+00:00; duration: 96.513 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2i/games/hollowmere --log-dir ~/wkspace/gc-studio/p4.2i/artifacts/studio/verification/W-REC-01/resume-reopened-editor-20261007T050922.417151Z/logs --label recovery-reopen -- -executeMethod Hollowmere.P4_2.ProcessRecovery.Recover -p42State ~/wkspace/gc-studio/p4.2i/artifacts/studio/verification/W-REC-01/resume-state-20261007T050804.537383Z
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
