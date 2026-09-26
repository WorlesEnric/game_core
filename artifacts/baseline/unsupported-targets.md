# Unsupported targets — explicitly unqualified

GC-025's acceptance says "Unsupported target platforms remain explicitly unqualified", P-058 makes IL2CPP and
managed stripping build *acceptance requirements*, and P-060 makes the supported profile an explicit record rather
than an assumption. 04 §1 already settles the baseline: the only qualified target is **StandaloneLinux64 x86_64,
IL2CPP, direct player build, run headless**, built by an Editor installed on Linux x86_64.

This file is the list of everything else, with the exact reason it is unqualified and what would qualify it. Nothing
here is a claim that the code is wrong on those targets — it is a statement that no evidence exists for them, which
is a different thing and must not be rounded up.

## 1. Unqualified targets

| Target | Status | Why unqualified | What would qualify it |
| --- | --- | --- | --- |
| macOS ARM64 (Apple silicon) | **Unqualified** | 04 §1 replaced the earlier macOS 15.5 ARM64 / Xcode 16.4 profile when the build host became Linux x86_64. No Apple SDK, Xcode or IL2CPP macOS toolchain is installed on the qualification host, so no player has been produced. | A macOS-hosted Editor with its macOS IL2CPP toolchain, the same catalog codegen + build + headless probe sequence, and the same fingerprint comparison as the Linux baseline. |
| macOS x86_64 | **Unqualified** | Same as above. | Same as above. |
| Windows x86_64 (IL2CPP or Mono) | **Unqualified** | No Windows toolchain, no Windows host, no player built or run. A Mono/desktop player would additionally not satisfy P-058's IL2CPP acceptance requirement. | A Windows-hosted Editor with the Windows IL2CPP toolchain and the same evidence sequence. |
| Linux ARM64 | **Unqualified** | The Editor's Linux IL2CPP support is pinned to the x86_64 toolchain (`com.unity.toolchain.linux-x86_64` 2.0.11); no ARM64 player was produced. | An ARM64 Linux host with a matching IL2CPP toolchain and the same evidence sequence. |
| Android / iOS / mobile | **Unqualified** | No mobile module installed, no device or emulator, no player built. These also change the stripping and AOT surface materially (linker settings, `link.xml` behaviour, native plugin load paths), so the *existing* Linux evidence would not transfer. | A dedicated qualification project per platform with its own manifest pin, build script and player probes; a platform is not qualified by a successful Linux build. |
| Consoles (any) | **Unqualified** | No SDK, no devkit, and platform certification is explicitly a non-goal of GC-025 ("No unrequested platform certification"). | Platform-specific SDKs, per-platform builds and the platform holders' own certification process — explicitly out of V1 scope. |
| WebGL / WebAssembly | **Unqualified** | No player built; IL2CPP-over-WASM has a different AOT and threading surface, so the player probes in this repository do not describe it. | A WebGL build with its own probe evidence, including a non-threaded execution path. |
| Any Editor-only or Editor-hosted execution | **Not evidence** | The Editor runs the same managed code under the JIT and without managed stripping, so an EditMode pass proves nothing about a stripped player (P-058: "successful Editor execution alone fails this gate"). | Nothing qualifies it: it is a different execution model by construction. |
| Mono / desktop scripting backend on any platform | **Unqualified** | P-058 makes IL2CPP the V1 profile; a Mono player does not exercise the AOT/stripping surface the catalog reachability work exists for. | A separate profile decision in the protocol, not an evidence change. |

## 2. What *is* qualified, and by what

| Claim | Evidence |
| --- | --- |
| StandaloneLinux64 x86_64, IL2CPP, High stripping, Burst enabled, headless | `artifacts/gc-001/`, and the build script `tools/build_baseline_player.sh` with `artifacts/baseline/ENVIRONMENT.md` filled in |
| Every committed catalog's generated factory, serializer and closed-generic root executes in the player | `-probeCatalogCoverage` (GC-025) in both build shapes, plus the catalog reachability manifest `artifacts/baseline/catalog-reachability.json` |
| Registration fingerprints are reproducible across builds | `tools/compare_registration_fingerprints.py` over two build ledgers, run by `tools/build_baseline_player.sh` |
| Kernel and gameplay assemblies are kept alive by generated roots, not by `link.xml` | `tools/check_link_xml.py` over both projects |

## 3. Rules for adding a target later

1. Add the platform's own pins (Editor, toolchain, player target) to 04 §1 rather than reusing the Linux pins by
   analogy — the pins are deliberate, not aliases for the newest release.
2. Build a player on a host that has that platform's toolchain, and run the catalog coverage probe plus the family
   probes against it. A build is not evidence; a run is.
3. Compare the generated registration fingerprints between the new platform's build and the Linux baseline. A
   difference is a defect in generation or an unsupported platform assumption, not a number to accept.
4. Record the exact versions in a filled-in copy of `ENVIRONMENT.md` beside the new target's evidence.
5. Keep this file's "unqualified" row for the target until steps 1–4 are done and archived.
