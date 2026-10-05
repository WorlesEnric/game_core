# package-diff

Verdict: **PASS**.

Source revision: `20e34d1e6a6800e4b0bd4a1e3ebd44611a968c55`; host: `worlesenric`.
Started: 2026-10-05T18:22:04.125863+00:00; ended: 2026-10-05T18:22:04.130996+00:00; duration: 0.006 s.

Command (from repository root unless cwd specified):

```sh
git diff --exit-code origin/main -- Packages/
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
