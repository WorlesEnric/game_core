# p42e-text2

Verdict: **FAIL**.

Source revision: `c047978f73829fa0b763b50047531a1886ac7800`; host: `worlesenric`.
Started: 2026-10-06T15:01:23.556992+00:00; ended: 2026-10-06T15:08:50.184869+00:00; duration: 446.630 s.

Command (from repository root unless cwd specified):

```sh
bash ~/wkspace/gc-studio/p4.2e/studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2e/.evidence/live/games/hollowmere --log-dir ~/wkspace/gc-studio/p4.2e/artifacts/studio/verification/W-AI-02/p42e-text2-20261006T150123.555155Z/logs --label p42e-text2 --timeout 1800 -- -executeMethod Hollowmere.P4_2.EvidenceEntry.RunStage
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
