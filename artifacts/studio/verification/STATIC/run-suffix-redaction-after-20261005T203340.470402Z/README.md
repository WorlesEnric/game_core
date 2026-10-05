# run-suffix-redaction-after

Verdict: **PASS**.

Source revision: `c532b77edd93099ddd1e45d20f48db5d627413b3`; host: `worlesenric`.
Started: 2026-10-05T20:33:40.471419+00:00; ended: 2026-10-05T20:33:40.861920+00:00; duration: 0.391 s.

Command (from repository root unless cwd specified):

```sh
python3 -m unittest discover -s studio/tools/Tests/P4_2 -v
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
