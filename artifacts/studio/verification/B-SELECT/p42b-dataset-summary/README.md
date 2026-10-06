# B-SELECT two-run summary

Verdict: **FAIL**. picks: p95 [3.245, 3.7098] ms, median 3.4774 ms, budget 16.0 ms; marquee: p95 [330.90360000000004, 621.8599] ms, median 476.3818 ms, budget 50.0 ms

Source revision: `1752ca8a5309b1df8cb1be983ccd6fc04d7dd6ba`; host: `worlesenric`.
Started: 2026-10-06T04:56:25.151616+00:00; ended: 2026-10-06T04:56:25.151622+00:00; duration: 0.000 s.

Command (from repository root unless cwd specified):

```sh
studio/tools/verify-all.sh final-timing
studio/tools/verify-all.sh final-selection
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
