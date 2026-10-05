# ui-capture

Verdict: **FAIL**.

Source revision: `12d7e5c7ababe95a1e251343584755e6c38fd454`; host: `worlesenric`.
Started: 2026-10-05T19:02:20.781053+00:00; ended: 2026-10-05T19:12:02.057122+00:00; duration: 581.277 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/evidence-p2.1.sh p4.2 games/hollowmere
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
