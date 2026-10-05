# W-TOOL-01: Tool catalog of the clean project lists only installed plugins' tools

Verdict: **PASS**. Saltmarsh exports only installed production tools; fixture, Hollowmere and internal admission tools are absent. Catalog and installed-package manifest retained.

Report timestamp: 2026-10-05T21:03:25.245995+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-CLEANPROOF/editmode-20261005T190038.583676Z/README.md](../UNITY-CLEANPROOF/editmode-20261005T190038.583676Z/README.md)
- [W-TOOL-01/20261005T2021267101990Z/tool-catalog.json](../W-TOOL-01/20261005T2021267101990Z/tool-catalog.json)
- [W-TOOL-01/20261005T2021267101990Z/installed-packages.json](../W-TOOL-01/20261005T2021267101990Z/installed-packages.json)

Exact acceptance/component cases: `R2_39_W_TOOL_01_ExportOnlyInstalledProductionTools`. Component cases do not close any missing external workflow.
