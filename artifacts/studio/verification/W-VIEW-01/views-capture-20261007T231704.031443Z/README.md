# views-capture

Verdict: **PASS**.

Source revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; host: `worlesenric`.
Started: 2026-10-07T23:17:04.033009+00:00; ended: 2026-10-07T23:18:47.853677+00:00; duration: 103.822 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/evidence-p2.3.sh p4.2l games/hollowmere
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
