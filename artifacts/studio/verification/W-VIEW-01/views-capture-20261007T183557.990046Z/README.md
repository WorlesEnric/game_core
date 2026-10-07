# views-capture

Verdict: **PASS**.

Source revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; host: `worlesenric`.
Started: 2026-10-07T18:35:57.991551+00:00; ended: 2026-10-07T18:37:40.849977+00:00; duration: 102.860 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/evidence-p2.3.sh p4.2k games/hollowmere
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
