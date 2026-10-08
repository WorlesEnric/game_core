# package-diff

Verdict: **PASS**.

Source revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; host: `worlesenric`.
Started: 2026-10-07T23:11:43.752287+00:00; ended: 2026-10-07T23:11:43.890247+00:00; duration: 0.139 s.

Command (from repository root unless cwd specified):

```sh
git diff --exit-code origin/main -- Packages/
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
