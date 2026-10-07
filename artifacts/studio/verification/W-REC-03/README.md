# W-REC-03: Cancel region load mid-way; cancel staging job

Verdict: **PASS** on product `fccf3f8c44e08bd3c6925ef99a8fcd15991d8703`, Linux build host, 2026-10-07.

## Observed workflow

1. Real Hollowmere Play startup reaches an unfinished native region scene load (`isDone=false`, progress 0.9). Production region cancellation settles it: one load and one unload, zero load failures, all three region scenes absent and residency `Unloaded`.
2. An authenticated app-origin candidate starts real Docker staging on a freshly installed scratch companion. The actual semantic-analyzer `dotnet run` container is observed running and the stage slot lock is held.
3. To obey the one-Editor-per-packet rule, the harness pauses that exact job-labelled dotnet container after the stage's real Unity confinement probe finishes. This is explicit test synchronization, not a substitute process or fake stage.
4. A second batch Editor opens the production **Stage panel**. Its attached, enabled Cancel button receives a `NavigationSubmitEvent`; the normal client invokes authenticated `POST /v1/stage/{job}/cancel`.
5. The job becomes durable `cancelled`; all matching Docker containers are gone; the stage slot advisory lock is acquirable; no owned Unity allocator reservation remains; no issued verdict or `out/verdict.json` exists. The authenticated verdict route returns 404, and the real panel's Admit button remains disabled. Restarting only the scratch companion preserves the cancelled state and absent verdict.

Job: `stg_1a114232ad123b9e94dfc3a`. Foreign app and foreign project cancellation return **404**; unauthenticated proxy access returns **401**. The installed node and companion were untouched; zero external provider calls.

Allocator precision: the stage's Unity probe releases its reservation before the dotnet phase. The cancellation proof releases the **held stage slot lock** and confirms zero remaining stage-owned Unity reservations; it does not claim a Unity reservation was held during dotnet cancellation.

## Evidence

- [Region before](r8-a/region-cancel-before.json.gz), [region after](r8-a/region-cancel-after.json.gz).
- [Actual running container and held slot lock](r8-a/container-running.json.gz).
- [Authentication/ownership refusals](r8-a/stage-authority.json.gz).
- [Actual Stage button before](r8-a/stage-ui-before.json.gz), [Admit/verdict/journal after](r8-a/stage-ui-after.json.gz).
- [Cancelled job](r8-a/stage-cancelled.json.gz), [post-restart durable read](r8-a/stage-durable-read.json.gz), [resource cleanup](r8-a/stage-resources.json.gz).
- [Complete scratch result](r8-a/result.json.gz).
- [Baseline missing-route regression](r8-a/baseline-cancel-test.log): **0 passed / 1 failed**, 404 instead of 200.
- [Rust gates](r8-a/rust-gates.log): fmt/clippy clean, **153 passed / 12 ignored**; [client tests](r8-a/dotnet-client-tests.log): **69 passed / 6 environment-gated skips**.
- [EditMode XML](r8-a/editmode-results.xml.gz): **199 passed / 0 failed / 0 skipped**.
- Full redacted logs/checksums: [scratch-02 manifest](../../../../games/hollowmere/Assets/Hollowmere/Tests/R8_A/Evidence~/scratch-02/manifest.json).

## Reproduce and limits

Use [R8-A packet](../../../../docs/studio/packets/R8-A-publication-cancellation.md). Batch Editor only: attached UI behavior is exercised, not graphical appearance. The packet records a separate restart-only fail-closed limitation for unverifiable orphan process-group identities; it cannot issue a verdict or falsely acknowledge cancellation.

Historical P4.2h prerequisite receipts remain revision-specific in this directory.
