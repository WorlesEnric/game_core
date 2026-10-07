# package-diff

Verdict: **PASS**.

Source revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; host: `worlesenric`.
Started: 2026-10-07T18:31:11.941233+00:00; ended: 2026-10-07T18:31:11.992975+00:00; duration: 0.053 s.

Command (from repository root unless cwd specified):

```sh
git diff --exit-code origin/main -- Packages/
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
