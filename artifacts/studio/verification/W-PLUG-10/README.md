# W-PLUG-10: Definition validator parity (inspector, validator, agent)

Verdict: **BLOCKED**. Definition and tool validators run, but the same invalid definition is not compared through inspector, validator and live-agent fronts in one retained parity case.

Report timestamp: 2026-10-06T15:29:44.415356+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md](../UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md)
