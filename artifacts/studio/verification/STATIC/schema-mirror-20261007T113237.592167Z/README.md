# schema-mirror

Verdict: **PASS**.

Source revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; host: `worlesenric`.
Started: 2026-10-07T11:32:37.593516+00:00; ended: 2026-10-07T11:32:37.712477+00:00; duration: 0.120 s.

Command (from repository root unless cwd specified):

```sh
diff -rq docs/studio/schemas studio/agent/schemas
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
