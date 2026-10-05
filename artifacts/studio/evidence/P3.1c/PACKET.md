# P3.1c — graphical B-FRAME qualification

Branch: `codex/p3.1c`. Source/build revision: `c79a38321f2b56592ce4dd9e62ec734e3be38cf9` (merged `origin/main`, descendant of `7051e95`).
Host: myubuntu (`worlesenric`). Only the packet's evidence, measurement launcher,
B-FRAME disposition and appended P3.1b note are owned here.

## R2 fixes

| Finding | Work | Test / disposition |
|---|---|---|
| P31c-GPU / R2-38, B-FRAME evidence subset only | Replace the offscreen qualification gap with exactly two identical-profile graphical probes on Xorg `:1`, NVIDIA RTX 4060 Ti, 1920×1080 borderless fullscreen, without recording. | `P31c_GPU_RequiredProfile`: old P3.1b 640×480 llvmpipe fails the profile; both new runs pass. `P31c_GPU_TwoGraphicalPlaythroughs`: both full routes pass, exit 0, 608.406 / 609.220 s. |
| B-FRAME performance acceptance | Preserve 16.7 / 100 / 250 ms limits and the original first-ready/transition rules. | `P31c_BFRAME_UnchangedBudget`: **FAIL / FAIL**, p95 **18.122 / 17.912 ms**, >100 ms outside transitions **2 / 2**, marsh→belfry **18.695 / 18.518 ms**, save worst **80.953 / 31.579 ms**. |

Full method, commands, before/after profile witness and raw evidence links are in
[README.md](README.md) and [qualification-checks.json](qualification-checks.json).
P3.1's existing video remains the recording evidence and predates these fixes.

## Verification

- Build through `unity-batch.sh`: PASS, one attempt, 1181 s, 0 errors / 7 warnings;
  Linux IL2CPP shipped-file hashes retained. No runtime source changes.
- Metadata: PASS (42 packages, 91 assemblies). C#: PASS (1198 files).
- Bash launcher syntax, Python report syntax, original statistics parity and
  original graphical failure witness pass. The first offline assertion used
  rounded prose instead of authoritative JSON; its correction is recorded.
- One allocator reservation plus its mutex covered both measured launches;
  no other Editor/player was present. Idle wait 1086 s; initial wait load
  18.41 / 22.93 / 19.72; run-start loads 6.18 / 9.82 / 13.62 and 2.37 / 3.36 / 8.19.
- Runtime/package/project sources match the build revision after restoring
  build-generated settings. Only the explicitly owned paths are changed.

## Requests to other packets

None. No cross-packet API or implementation change is required to publish the
measurement. Performance remediation is not claimed by this packet.

## Left open

- B-FRAME is **FAIL**. Frames 1 and 2 exceed 100 ms in both runs (2675.260 / 183.973
  ms; 2606.899 / 144.199 ms), and both p95 values exceed 16.7 ms. There is no boot exemption.
- The exact causes of the startup stalls and p95 excess are unknown: no profiler
  attribution or further run is authorized within this measurement-only, two-run
  packet. The passing save/transition bounds do not establish an overall pass.
- Other verification rows and the broader R2-38 finding are outside this packet;
  only 07's B-FRAME row is updated. No game code or budget is changed.
