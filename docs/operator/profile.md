# Supported profile

**Status of every claim on this page:** the profile below is what the repository has *evidence* for. It is
not a claim about any other platform, and no other platform is qualified.

The single qualified target is:

> **StandaloneLinux64 x86_64, IL2CPP, Release, High managed stripping, Burst enabled, run headless**
> (`-batchmode -nographics`), built by a Unity Editor installed on **Linux x86_64**.

This is the exact profile from [04 §1](../game-core/04-unity-integration.md) and it is the only one with a
built and executed player. `artifacts/baseline/ENVIRONMENT.md` records the machine that produced it.

## 1. The pins

These are deliberate pins, not aliases for the newest release. A different version is a different,
unqualified profile — not a compatible substitute.

| Item | Pin | Where it is asserted |
| --- | --- | --- |
| Unity Editor | `6000.0.75f1`, Linux x86_64, with the Linux IL2CPP module | `tools/unity/build_probe.sh` refuses an Editor whose path does not contain `6000.0.75f1` |
| Host OS / arch | Linux x86_64 (recorded: Ubuntu 24.04.4 LTS) | `artifacts/baseline/ENVIRONMENT.md` |
| Entities | `1.4.6` | `unity/GameCore.Validation/Packages/manifest.json` |
| Collections | `2.6.6` | same |
| Burst | `1.8.28` | same |
| Mathematics | `1.3.2` | same |
| Unity Test Framework | `1.6.0` | same |
| Performance Testing | `3.0.3` | same |
| Linux IL2CPP toolchain | `com.unity.toolchain.linux-x86_64` `2.0.11` | same |
| Scripting backend | **IL2CPP** | `BuildProbe.cs` sets it and reads it back |
| IL2CPP compiler configuration | **Release** | same |
| Managed stripping | **High** | same |
| API compatibility level | **.NET Standard 2.1** | same |
| Burst | **enabled** — the build refuses to proceed when it is off | same |
| Audio | **disabled** in the headless player | `unity/GameCore.Validation/ProjectSettings/AudioManager.asset` (`m_DisableAudio: 1`) |
| Managed language level | C# 9 | Unity's bundled Roslyn |

The Editor's own revision is checked mechanically. `BuildProbe.cs` records all of the build settings above
and reads them back into the player's result JSON, so a player result that claims IL2CPP + High stripping is
reporting what the engine actually built, not what a script requested.

## 2. What is qualified, and by what evidence

| Claim | Evidence |
| --- | --- |
| StandaloneLinux64 x86_64, IL2CPP, High stripping, Burst, headless | `artifacts/baseline/ENVIRONMENT.md`, `artifacts/gc-001/`, built by `tools/unity/build_probe.sh` |
| Every committed catalog's generated factory, serializer and closed-generic root executes in the player | `-probeCatalogCoverage` in both build shapes; `artifacts/baseline/catalog-reachability.json` |
| Registration fingerprints reproduce across builds | `tools/compare_registration_fingerprints.py` over two build ledgers |
| Kernel and gameplay assemblies are kept alive by generated roots, not by `link.xml` | `tools/check_link_xml.py` over both projects |
| The three reference families run on one kernel | `artifacts/w7-gate/`, `-probeConformance` |
| Recovery from a real file checkpoint without fault latches | `-probeRecoverySmoke` in the marker-free release player |

## 3. Explicitly unqualified

Everything else is unqualified. The full list with reasons and the conditions that would qualify each is
[`artifacts/baseline/unsupported-targets.md`](../../artifacts/baseline/unsupported-targets.md). In summary:

| Target | Status |
| --- | --- |
| macOS ARM64 (Apple silicon), macOS x86_64 | **Unqualified.** The earlier macOS profile was replaced when the build host became Linux x86_64; no Apple SDK or IL2CPP macOS toolchain is installed. |
| Windows x86_64 (IL2CPP or Mono) | **Unqualified.** No Windows host or toolchain; no player built. |
| Linux ARM64 | **Unqualified.** The Linux IL2CPP support is pinned to the x86_64 toolchain. |
| Android / iOS / mobile | **Unqualified.** No mobile module, device or player; these change the stripping and AOT surface, so Linux evidence does not transfer. |
| Consoles (any) | **Unqualified and out of V1 scope.** No SDK, no devkit; certification is a non-goal. |
| WebGL / WebAssembly | **Unqualified.** IL2CPP-over-WASM has a different AOT and threading surface. |
| Any Editor-only or Editor-hosted execution | **Not evidence at all.** The Editor runs the same managed code under the JIT and without managed stripping, so an EditMode pass proves nothing about a stripped player (P-058). |
| Mono / desktop scripting backend, on any platform | **Unqualified.** P-058 makes IL2CPP the V1 profile; a Mono player does not exercise the AOT/stripping surface this catalog work exists for. |

**A successful Linux build is not evidence for another platform.** Do not restate Linux results as general
portability, and do not round "unqualified" up to "supported".

## 4. Adding a target later

1. Add that platform's own pins (Editor, toolchain, player target) to 04 §1 — do not reuse the Linux pins by
   analogy.
2. Build a player on a host that has that platform's toolchain, and run the catalog coverage probe plus the
   family probes against it. **A build is not evidence; a run is.**
3. Compare generated registration fingerprints between the new platform's build and the Linux baseline. A
   difference is a defect in generation or an unsupported platform assumption — not a number to accept.
4. Record the exact versions in a filled-in copy of `ENVIRONMENT.md` beside the new target's evidence.
5. Keep the "unqualified" row for the target until steps 1–4 are done and archived.

## 5. Player bundle version

`PlayerSettings.bundleVersion` is `0.1.0` (`ProjectSettings.asset`, also set in `BuildProbe.cs:102`). This is
the **player's** version string, which is a different thing from a Game Core package version (all packages
are `1.0.0`; see [packages.md](packages.md)). Nothing asserts the bundle version, and GC-029 deliberately
does not change it: it is not package metadata, and it is not referenced by any check or gate.
