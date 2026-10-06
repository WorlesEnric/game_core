# W-VIEW-03: Quests: Stage/objective graph, branches, reward/rule links, simulation and live state

Verdict: **PASS**. Final-main view suite: 41/41 passed, zero skips. P4.2 graphical captures retained with their original revision; no new final-main display capture is claimed.

Report timestamp: 2026-10-06T15:29:44.370861+00:00 UTC.

Acceptance baseline: merged main `1752ca8a`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh final-views-tests
studio/tools/verify-all.sh views
```

## Retained evidence

- [UNITY-HOLLOWMERE/p42b-final-views-20261006T042540.489299Z/results.xml](../UNITY-HOLLOWMERE/p42b-final-views-20261006T042540.489299Z/results.xml)
- [W-VIEW-01/views-capture-20261005T191314.272300Z/README.md](../W-VIEW-01/views-capture-20261005T191314.272300Z/README.md)
- [W-VIEW-01/captures-20261005T191314Z/capture.json](../W-VIEW-01/captures-20261005T191314Z/capture.json)
- [W-VIEW-03/p42-supplemental.md](../W-VIEW-03/p42-supplemental.md)
- [W-VIEW-03/p42-supplemental/README.md](../W-VIEW-03/p42-supplemental/README.md)

Historical references: P2.3 defines Quests at docs/studio/packets/P2.3-studio-views.md:27-34. P4.2 graphical run is supplemental evidence.
