# schema-mirror

Verdict: **PASS**.

Source revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; host: `worlesenric`.
Started: 2026-10-07T18:07:22.613704+00:00; ended: 2026-10-07T18:07:22.628340+00:00; duration: 0.016 s.

Command (from repository root unless cwd specified):

```sh
diff -rq docs/studio/schemas studio/agent/schemas
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
