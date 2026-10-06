# W-GAME-06: Build log + sha256 + V1 gate transcript on the same revision

Verdict: **PASS**. Hollowmere Linux IL2CPP build/hash passes. V1 phases 1–8 and resumed 9–11 pass; both release resumes retain their failed setup attempts, with no repeated qualification probes or relaxed budget.

Report timestamp: 2026-10-06T15:29:44.429968+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh build
studio/tools/verify-all.sh v1
```

## Retained evidence

- [W-GAME-06/hollowmere-linux-retry-20261005T195106.895058Z/README.md](../W-GAME-06/hollowmere-linux-retry-20261005T195106.895058Z/README.md)
- [W-GAME-06/hollowmere-linux-retry-20261005T195106.895058Z/binary.json](../W-GAME-06/hollowmere-linux-retry-20261005T195106.895058Z/binary.json)
- [W-GAME-06/v1-gate-retry-20261005T190359.447287Z/README.md](../W-GAME-06/v1-gate-retry-20261005T190359.447287Z/README.md)
- [W-GAME-06/v1-resume-phases-9-11-20261005T195509.235538Z/README.md](../W-GAME-06/v1-resume-phases-9-11-20261005T195509.235538Z/README.md)
- [W-GAME-06/v1-release-evidence-resume-20261005T200353.317063Z/README.md](../W-GAME-06/v1-release-evidence-resume-20261005T200353.317063Z/README.md)
- [W-GAME-06/final-player-manifests-20261005T201815.687151Z/README.md](../W-GAME-06/final-player-manifests-20261005T201815.687151Z/README.md)
