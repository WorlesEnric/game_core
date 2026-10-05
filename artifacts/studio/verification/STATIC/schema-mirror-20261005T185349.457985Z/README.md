# schema-mirror

Verdict: **PASS**.

Source revision: `4214d67b2289ac8c1986d34ed15f0efae30db00b`; host: `worlesenric`.
Started: 2026-10-05T18:53:49.459415+00:00; ended: 2026-10-05T18:53:49.460312+00:00; duration: 0.002 s.

Command (from repository root unless cwd specified):

```sh
diff -rq docs/studio/schemas studio/agent/schemas
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
