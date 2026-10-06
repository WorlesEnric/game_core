# W-ETOS-02: Unity key against another agent: `forbidden`; shown in Studio log

Verdict: **BLOCKED**. Live app-key access to agent-only tasks returns 403 forbidden, but the other-agent probe returns 404 agent_unknown; no installed second-agent forbidden response is established.

Report timestamp: 2026-10-06T12:40:21.166241+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
GAMECORE_ETOS_LIVE=1 dotnet test dotnet/tests/GameCore.Studio.Etos.Client.Tests --filter FullyQualifiedName~L02_
```

## Retained evidence

- [W-HOST-01/installed-hello-authority-20261005T185555.694549Z/live/dotnet-w-etos-02-authority.json](../W-HOST-01/installed-hello-authority-20261005T185555.694549Z/live/dotnet-w-etos-02-authority.json)
