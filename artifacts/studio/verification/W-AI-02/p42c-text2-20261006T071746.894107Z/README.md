# p42c-text2

Verdict: **FAIL**.

Source revision: `65666ac2716856fc5ce8c7fa3dac2ed4804f8a42`; host: `worlesenric`.
Started: 2026-10-06T07:17:46.895951+00:00; ended: 2026-10-06T07:24:39.022185+00:00; duration: 412.128 s.

Command (from repository root unless cwd specified):

```sh
bash ~/wkspace/gc-studio/p4.2c/studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2c/.evidence/live/games/hollowmere --log-dir ~/wkspace/gc-studio/p4.2c/artifacts/studio/verification/W-AI-02/p42c-text2-20261006T071746.894107Z/logs --label p42c-text2 --timeout 1800 -- -executeMethod Hollowmere.P4_2.EvidenceEntry.RunStage
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
