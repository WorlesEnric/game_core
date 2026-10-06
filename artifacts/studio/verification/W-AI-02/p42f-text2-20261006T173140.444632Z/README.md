# p42f-text2

Verdict: **FAIL**.

Source revision: `55f0a4dfe3cbf86ca9c195f6880c948deca57d27`; host: `worlesenric`.
Started: 2026-10-06T17:31:40.446099+00:00; ended: 2026-10-06T17:37:56.378934+00:00; duration: 375.934 s.

Command (from repository root unless cwd specified):

```sh
bash ~/wkspace/gc-studio/p4.2f/studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2f/.evidence/live/games/hollowmere --log-dir ~/wkspace/gc-studio/p4.2f/artifacts/studio/verification/W-AI-02/p42f-text2-20261006T173140.444632Z/logs --label p42e-text2 --timeout 1800 -- -executeMethod Hollowmere.P4_2.EvidenceEntry.RunStage
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
