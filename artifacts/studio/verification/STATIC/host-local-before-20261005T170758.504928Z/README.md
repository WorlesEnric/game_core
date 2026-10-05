# host-local-before

Verdict: **FAIL**.

Source revision: `813e6b4591d864c30450ffd436bd8008bb0e67e1`; host: `worlesenric`.
Started: 2026-10-05T17:07:58.580851+00:00; ended: 2026-10-05T17:07:58.657846+00:00; duration: 0.079 s.

Command (from repository root unless cwd specified):

```sh
python3 -m unittest discover -s studio/tools/Tests/P4_2 -k HOST_01 -v
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
