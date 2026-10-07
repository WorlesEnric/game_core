# views-capture

Verdict: **PASS**.

Source revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; host: `worlesenric`.
Started: 2026-10-07T12:11:49.316194+00:00; ended: 2026-10-07T12:13:40.178327+00:00; duration: 110.864 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/evidence-p2.3.sh p4.2j games/hollowmere
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
