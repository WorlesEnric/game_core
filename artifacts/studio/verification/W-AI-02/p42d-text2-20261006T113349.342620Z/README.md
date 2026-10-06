# p42d-text2

Verdict: **FAIL**.

Source revision: `274cfc7d24bb050779d6e23eace2007e07af617f`; host: `worlesenric`.
Started: 2026-10-06T11:33:49.343960+00:00; ended: 2026-10-06T11:40:22.111485+00:00; duration: 392.769 s.

Command (from repository root unless cwd specified):

```sh
bash ~/wkspace/gc-studio/p4.2d/studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2d/.evidence/live/games/hollowmere --log-dir ~/wkspace/gc-studio/p4.2d/artifacts/studio/verification/W-AI-02/p42d-text2-20261006T113349.342620Z/logs --label p42d-text2 --timeout 1800 -- -executeMethod Hollowmere.P4_2.EvidenceEntry.RunStage
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
