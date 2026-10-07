# schema-mirror

Verdict: **PASS**.

Source revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; host: `worlesenric`.
Started: 2026-10-07T04:16:38.510184+00:00; ended: 2026-10-07T04:16:38.523610+00:00; duration: 0.015 s.

Command (from repository root unless cwd specified):

```sh
diff -rq docs/studio/schemas studio/agent/schemas
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
