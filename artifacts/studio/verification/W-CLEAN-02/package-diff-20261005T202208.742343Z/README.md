# package-diff

Verdict: **PASS**.

Source revision: `c532b77edd93099ddd1e45d20f48db5d627413b3`; host: `worlesenric`.
Started: 2026-10-05T20:22:08.743562+00:00; ended: 2026-10-05T20:22:08.948525+00:00; duration: 0.206 s.

Command (from repository root unless cwd specified):

```sh
git diff --exit-code origin/main -- Packages/
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
