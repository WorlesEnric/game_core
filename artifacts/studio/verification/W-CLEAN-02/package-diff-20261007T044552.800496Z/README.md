# package-diff

Verdict: **PASS**.

Source revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; host: `worlesenric`.
Started: 2026-10-07T04:45:52.801915+00:00; ended: 2026-10-07T04:45:52.836641+00:00; duration: 0.036 s.

Command (from repository root unless cwd specified):

```sh
git diff --exit-code origin/main -- Packages/
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
