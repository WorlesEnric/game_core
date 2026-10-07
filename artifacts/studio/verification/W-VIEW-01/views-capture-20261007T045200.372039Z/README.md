# views-capture

Verdict: **PASS**.

Source revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; host: `worlesenric`.
Started: 2026-10-07T04:52:00.373756+00:00; ended: 2026-10-07T04:53:45.209621+00:00; duration: 104.838 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/evidence-p2.3.sh p4.2i games/hollowmere
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
