# p42k-query-regression

Verdict: **PASS**.

Source revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; host: `worlesenric`.
Started: 2026-10-07T21:57:15.764990+00:00; ended: 2026-10-07T21:57:15.838464+00:00; duration: 0.075 s.

Command (from repository root unless cwd specified):

```sh
python3 artifacts/studio/verification/TOOLS/test_query_p42k.py
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
