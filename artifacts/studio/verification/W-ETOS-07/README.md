# W-ETOS-07: Generated texture arrives with matching sha256; tampered file refused

Verdict: **BLOCKED**. P4.2d authenticated hello exposes image operator and TTS published tariffs; two TTS calls succeed. Describe is live but has no tariff; R5-B’s template explicitly remains SET_BY_OPERATOR/0.0, so no describe call is made. Prior image-import evidence remains revision-specific; no invented per-call tariff or provider invoice.

Report timestamp: 2026-10-06T12:40:21.168013+00:00 UTC.

Acceptance baseline: merged main `40fb91fa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh p42d hello
studio/tools/verify-all.sh p42d voice2
```

## Retained evidence

- [INSTALL-P4.2d/hello-20261006T112349.691723Z/live/dotnet-a-hello.json](../INSTALL-P4.2d/hello-20261006T112349.691723Z/live/dotnet-a-hello.json)
- [W-VOICE-01/p42d-voice2-20261006T112706.963706Z/workflow/voice/spoken-prompts.json](../W-VOICE-01/p42d-voice2-20261006T112706.963706Z/workflow/voice/spoken-prompts.json)

Historical references: P4.2d installed release 0.1.0-e8a72b2d6eb3aad9 on main 40fb91fa. Earlier attempts remain retained; untouched rows keep their original revision-specific evidence.
