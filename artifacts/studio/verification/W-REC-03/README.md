# W-REC-03: Cancel region load mid-way; cancel staging job

Verdict: **BLOCKED**. P4.2h current app-origin staging exists, but installed running-stage cancellation does not: no authenticated cancel route/client method, and action accepts only stage/discard. Requires owner-scoped cancellation, durable cancelled state, child/container teardown and allocator release without a passing verdict. Discard or cancelling an HTTP wait is not job cancellation; source-backed prerequisite retained.

Product baseline: `a77cb38ba4a2265007fa40c38983e01a17bb0914`. Historical receipts retain their original revision.

## Reproduce

```sh
Read docs/studio/packets/P4.2h-live-rows.md Requests to other packets and W-REC-03/p42h-prerequisite/result.json
```

## Retained evidence

- [UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md](../UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md)
- [W-MECH-01/pressure-plate-explicit-paired-ui-20261005T193418.507302Z/README.md](../W-MECH-01/pressure-plate-explicit-paired-ui-20261005T193418.507302Z/README.md)
- [W-REC-03/p42h-prerequisite/result.json](../W-REC-03/p42h-prerequisite/result.json)
