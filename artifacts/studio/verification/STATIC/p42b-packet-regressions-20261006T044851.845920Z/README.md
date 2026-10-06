# p42b-packet-regressions

Verdict: **PASS**.

Source revision: `8d1574e4326aaf3c26244cd0581e4b06854dc608`; host: `worlesenric`.
Started: 2026-10-06T04:48:51.880128+00:00; ended: 2026-10-06T04:48:52.321736+00:00; duration: 0.476 s.

Command (from repository root unless cwd specified):

```sh
python3 -m unittest discover -s artifacts/studio/verification/TOOLS/tests -v
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
