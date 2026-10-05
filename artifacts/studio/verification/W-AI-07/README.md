# W-AI-07: 3D generation request: `not_configured`/blocked surfaced honestly

Verdict: **BLOCKED**. Hello honestly reports 3D not_configured and no 3D provider is called. The actual refusal is 409 budget_unpriced, failing the specified not_configured/blocked-code test; provider absence and tariff absence are conflated.

Report timestamp: 2026-10-05T21:03:25.256406+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
GAMECORE_ETOS_LIVE=1 dotnet test dotnet/tests/GameCore.Studio.Etos.Client.Tests --filter FullyQualifiedName~L05_
```

## Retained evidence

- [W-AI-07/installed-3d-refusal-tts-tamper-20261005T192945.989942Z/README.md](../W-AI-07/installed-3d-refusal-tts-tamper-20261005T192945.989942Z/README.md)
- [W-AI-07/installed-3d-refusal-tts-tamper-20261005T192945.989942Z/live/dotnet-f-i-refusals.json](../W-AI-07/installed-3d-refusal-tts-tamper-20261005T192945.989942Z/live/dotnet-f-i-refusals.json)
