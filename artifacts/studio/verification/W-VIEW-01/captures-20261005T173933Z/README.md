# views-capture

Verdict: **PASS**.

Source revision: `20e34d1e6a6800e4b0bd4a1e3ebd44611a968c55`; host: `worlesenric`.
Started: 2026-10-05T17:39:33.471759+00:00; ended: 2026-10-05T17:41:19.214208+00:00; duration: 105.744 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/evidence-p2.3.sh p4.2 games/hollowmere
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
