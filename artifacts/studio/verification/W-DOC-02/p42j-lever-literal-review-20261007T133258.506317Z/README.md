# p42j-lever-literal-review

Verdict: **FAIL**.

Source revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; host: `worlesenric`.
Started: 2026-10-07T13:32:58.507749+00:00; ended: 2026-10-07T13:37:00.117388+00:00; duration: 241.611 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2j/games/hollowmere --log-dir ~/wkspace/gc-studio/p4.2j/artifacts/studio/verification/W-DOC-02/p42j-lever-literal-review-20261007T133258.506317Z/logs --label p42j-lever-literal-review --attempts 1 --timeout 1800 -- -executeMethod Hollowmere.R8_B.LeverWalkthrough.Run -gcR8CConfig ~/wkspace/gc-studio/p4.2j/artifacts/studio/verification/W-DOC-02/p42j-lever-literal/config.json -force-glcore
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
