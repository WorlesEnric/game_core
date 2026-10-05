# schema-mirror

Verdict: **PASS**. 

Source revision: `813e6b4591d864c30450ffd436bd8008bb0e67e1`; host: `worlesenric`.
Started: 2026-10-05T17:00:26.356640+00:00; ended: 2026-10-05T17:00:26.358288+00:00; duration: 0.003 s.

Command (from repository root unless cwd specified):

```sh
diff -rq docs/studio/schemas studio/agent/schemas
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged. Hashes describe these retained sanitized bytes.
