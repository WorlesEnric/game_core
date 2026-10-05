# ui-capture

Verdict: **FAIL**.

Source revision: `d26494a498d422c9495ef503d88afbd1551d79c3`; host: `worlesenric`.
Started: 2026-10-05T19:23:23.971756+00:00; ended: 2026-10-05T19:28:20.970451+00:00; duration: 297.000 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/evidence-p2.1.sh p4.2 games/hollowmere
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
