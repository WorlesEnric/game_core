# p42c-cap-baseline-after

Verdict: **PASS**.

Source revision: `dbedd2fb6c830be5213d1658da41f0956cbc7f28`; host: `worlesenric`.
Started: 2026-10-06T07:37:26.790966+00:00; ended: 2026-10-06T07:37:26.901110+00:00; duration: 0.112 s.

Command (from repository root unless cwd specified):

```sh
python3 -m unittest discover -s artifacts/studio/verification/TOOLS/tests -v
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
