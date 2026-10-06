# W-DOC-01: New user adds an NPC with dialogue from the creator guide

Verdict: **FAIL**. Text Send passed. Image Send was accepted and produced a PNG; after the initial 180-second harness timeout, the same request was recovered without resubmission. Unity refuses its candidate: MediaImporterInvalid for textureType "Sprite (2D and UI)". The two deferred Send actions are recorded, but image import/assignment and the full novice NPC/dialogue walkthrough do not pass.

Report timestamp: 2026-10-06T09:08:50.190960+00:00 UTC.

Acceptance baseline: merged main `f787829289ea7402c08917a78553ff6c3838bda8`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh p42c guides
studio/tools/verify-all.sh p42c guide-recover
```

## Retained evidence

- [W-DOC-01/p42c-guides-20261006T082946.366496Z/results.xml](../W-DOC-01/p42c-guides-20261006T082946.366496Z/results.xml)
- [W-DOC-01/p42c-guides-20261006T082946.366496Z/text-sent.json](../W-DOC-01/p42c-guides-20261006T082946.366496Z/text-sent.json)
- [W-DOC-01/p42c-guides-20261006T082946.366496Z/text-outcome.json](../W-DOC-01/p42c-guides-20261006T082946.366496Z/text-outcome.json)
- [W-DOC-01/p42c-guides-20261006T082946.366496Z/image-sent.json](../W-DOC-01/p42c-guides-20261006T082946.366496Z/image-sent.json)
- [W-DOC-01/p42c-image-recovered-20261006T084832.481069Z/image-outcome.json](../W-DOC-01/p42c-image-recovered-20261006T084832.481069Z/image-outcome.json)
- [W-DOC-01/p42c-image-recovered-20261006T084832.481069Z/results.xml](../W-DOC-01/p42c-image-recovered-20261006T084832.481069Z/results.xml)

Exact acceptance/component cases: `P42c.Live.GuideTests.R2_38_CreatorGuide_TextSend`, `P42c.Live.GuideTests.R2_38_CreatorGuide_ImageSend`, `P42c.Live.GuideTests.R2_38_CreatorGuide_ResumeImageSendWithoutAnotherRequest`. Component cases do not close any missing external workflow.

Historical references: Earlier P4.2/P4.2b attempts remain retained; this disposition uses the installed P4.2c release and final-main product source.
