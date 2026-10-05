# final-evidence-regressions

Verdict: **PASS**.

Source revision: `50a4c07c202151e2e877dece26579dff6420cd66`; host: `worlesenric`.
Started: 2026-10-05T20:54:58.711024+00:00; ended: 2026-10-05T20:54:59.093815+00:00; duration: 0.384 s.

Command (from repository root unless cwd specified):

```sh
python3 -m unittest discover -s studio/tools/Tests/P4_2 -v
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
