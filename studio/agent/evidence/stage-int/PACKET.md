# STAGE-INT — confined stage integration

Branch `codex/stage-int`, base `2ed48b96`, Linux build host myubuntu, 2026-10-05.
Only assigned stage sources/tests/evidence and the two designated packet-note appendices change.
The installed companion, etosd, Unity packages, games and sibling clones are untouched.
Retained integration state is below `~/.cache/gamecore-studio/stage-int`; ordinary unit-test
and host restore workspaces use disposable temporary directories.
Final implementation revision: `c2b8f7b56fac84fe27d566aeebca6789d966ccdc`; the following commit retains notes/evidence.

## R2 fixes

| Finding / blocker | Fix | Regression |
|---|---|---|
| R2-11 / R2-F2 NU1100 | Host-only locked restore of the union of analyzer and Rules-test dependency closures: 17 package versions. Verify pinned archive SHA-512, exact manifest and expanded payload bytes before every Docker launch. Refuse extra packages/files and links. | `test_R2_11_StageIntPinnedClosure`, `ArchiveTamper`, `ExpandedDllTamper`, `ExtraPackageRefused`, `LinkRefused`, `NuGetEscapedPayloadNames` (same prefix); Rust `r2_11_stage_int_launcher_refuses_missing_cache_before_execution`; real offline analyzer/Rules runs |
| R2-11 / R2-F2 analyzer mismatch | One strict lower-camel request and result; mandatory exit-zero plus versioned, passing, empty findings. Unknown/duplicate/missing fields refuse. | `R2_11_StageIntSharedWireFixture`, `R2_11_StageIntStrictRequest`; Rust `r2_11_stage_int_shared_analyzer_fixture_and_strict_results`; existing analyzer-absence test |
| R2-11 trusted context | Use modular installed Unity assemblies, 66 pinned Unity/NUnit metadata DLLs in slot Library, and read-only trusted package sources. Legacy monolithic assemblies caused duplicate types and are excluded. Candidate assemblies/paths never supply references. | Pressure-plate semantic acceptance and committed negative-semantic refusal through the production launcher; `reference-before.txt` records the integrated pre-fix refusal |
| R2-11 launcher integration | Translate the trusted `-testResults` argument into allocator `--results`, retaining host allocation and XML acceptance. Cold runs retain public `budgetMs=360000` and separately record their enforced extended execution allowance. | `r2_11_stage_int_results_are_owned_by_host_allocator`; signed-service integration asserts the standard budget |
| R2-16 parallel test regression | Explicitly unlock the lease on guard drop, including error paths; inherited file descriptions cannot prolong a completed lease. | `r2_16_stage_int_guard_drop_unlocks_inherited_description`; `lock-before.txt` fails / `lock-after.txt` passes |
| R2-09 integration | A separate copy of this clone's real companion runs against a loopback authentication test node; its actual stage service runs Docker and refuses authority for a failed job. The harness also requires authenticated issuance/fetch/verify after a full pass; that branch remains blocked. | `r2_11_stage_int_docker_pressure_signed_verdict` |

The baseline analyzer crashes (exit 134) on the new request; the fixed CLI returns zero for the
same request and clean source (`contract-before.txt`, analyzer shared fixture). R2-F2's retained
NU1100 verdict is the pre-fix full-lane cache evidence. Initial integrated attempts also exposed
legacy/modular reference duplication, Rules' additional System.Reflection.Metadata 1.6.0 closure,
and the allocator's reserved `-testResults` argument. None was bypassed. One exploratory launch
was interrupted by rebuilding its executable; the retained service test uses a private binary
copy to make the engine wrapper stable across later builds.

## Provisioning and strict contract

The reproducible host commands are in the STAGE-INT appendix of P2.4. Network restore occurs
only in `provision-cache.sh` on the host, inheriting its proxy. Restore projects and locked files
are copied to a private host temporary directory outside the container-writable cache; automatic
ancestor MSBuild props/targets imports are disabled. Docker retains `--network none`, no pulls,
read-only root, dropped capabilities and no-new-privileges. The existing v1 image is retained.
`cache/cache-lock.json` pins SHA-512 of actual `.nupkg` bytes; NuGet's content hashes are separately
retained in the two lockfiles. `cache-manifest.json` must equal this trusted manifest. Extracted
payloads are checked against the verified archive, including NuGet's URL-decoded ZIP paths.
The cache identity additionally binds the NuGet, Unity metadata and public UPM lock manifests.
Host provisioning can seed 44 pinned public UPM records with `--upm-from ~/.cache/Unity/upm`.
It copies only selected registry metadata/archives/signatures/size records and creates sanitized
cache indexes; no host UPM configuration, response headers or credentials are copied. The
launcher sets `UPM_CACHE_ROOT=<cache>/upm` and checks pinned contents/index mappings before launch.
The first service attempt and `upm-before.txt` record the otherwise missing offline metadata/archives.
`test_R2_11_StageIntPublicUpmMetadataAndMappingIntegrity` covers content and mapping tampering.

Request: `{schema:"gamecore.stage.analyze/1",references:[absolute paths],supportSources:[absolute directories],policy:{mode:"D1"}}`.
Result: `{schema:"gamecore.stage.findings/1",pass:boolean,findings:[{rule:string,file:string,line:positive integer,message:string}]}`.
There are no optional authority fields or candidate policy overrides. The policy name selects
the existing semantic D1 rules. Absent/crashed/malformed/nonempty output always refuses. The Rust
suite reads `STAGE_CONTRACT_FIXTURE_OUT`, written by the C# CLI fixture test, and also checks the
committed fixture for ordinary runs. The companion had no existing stage schema file/directory, so the
strict DTOs and shared fixtures define this contract without changing unrelated schema files.

The operator metadata seed is the existing host cache
`~/.cache/gamecore-studio/stage/_warm/Library`. Only pinned Unity DLLs and offline UPM package
payloads are seeded; candidate/project assemblies are excluded. DLL names and SHA-256 values
are committed in `cache/unity-metadata-lock.json`. A metadata-only Library is cold until it has
an ArtifactDB; successful compilation replaces that seed with the completed warm Library.

## Validation

**Docker pressure-plate stage: BLOCKED. No passing or signed admission verdict was issued.**
The actual companion run at `5d5cf1c9` passed scan, checkers, semantic analysis (empty findings)
and all 15 candidate Rules tests, then Unity exited 1 during package resolution. No EditMode
XML existed, so candidate EditMode/PlayMode/determinism are not claimed. The original failure
is retained in `service-job-resolution.json`, `pressure-verdict.json`, `engine-log-tail.txt` and
`service-test-resolution.txt`. The final early-refusal run at `361666cc` names
`stage_dependency_denied: com.gamecore.gameplay.world requires excluded com.gamecore.studio.core`;
its authenticated verdict endpoint returned **404**, retained in `issuance.json`. The ignored
acceptance fixture intentionally continues to assert full success; its explicit invocation is
reported as blocked/failed, never counted among passing tests.

| Command / suite | Result |
|---|---|
| `cargo fmt --check`; `cargo clippy --all-targets -- -D warnings` | clean |
| `STAGE_CONTRACT_FIXTURE_OUT=... cargo test` | 114 pass: 89 unit, 21 companion, four stage-lane; zero failures in ordinary suite |
| Explicit ignored Docker isolation and offline Unity licensing tests | 2/2 pass; **116 executed Rust passes total** |
| Explicit `cargo test --test stage_int -- --ignored --nocapture` | one acceptance case BLOCKED, two recorded attempts; no signed pass |
| `dotnet test studio/stage/analyzer/Tests/StageAnalyzer.Tests.csproj` | 50/50 pass; C# writes fixture consumed by Rust |
| `studio/stage/analyzer/offline-check.sh <scratch> <cache>` | 50/50 pass inside default production Docker launcher; pressure empty findings; negative exit 3 with SG001–SG010, all expected IDs |
| `python3 -m unittest discover -s studio/stage/tests -v` | 16/16 pass |
| `python3 tools/check_stage_slot.py --self-test`; checker on real prepared pressure slot | 29/29 self-tests; real slot clean |
| `check_package_metadata.py`; `check_game_core_csharp.py` | pass; 1,136 C# files |
| Trusted Unity-only UPM compile probe, same Docker image/boundary | PASS: Unity exit 0, 75 s, no network; this is a package-resolution/compile probe, not candidate tests |
| Fresh host `provision-cache.sh <scratch>` plus `--verify` | 17-package locked union and expanded payloads verified; manifest retained |

The exact transcripts are adjacent to this packet. The normal Rust run registers seven ignored
host/external cases: two Docker tests were explicitly run and passed; the full service case was
explicitly run and blocked; two live-node and two legacy real-stage cases remain prerequisite
blocked/unrun. The legacy real-stage harness now re-enters a private copy of the actual CLI
instead of its test executable, and locates the versioned warm cache; it cannot qualify the
unchanged incompatible gameplay dependency closure.

## Requests to other packets

- **Gameplay/Studio integration owner:** `Packages/com.gamecore.gameplay.world/package.json`
  currently requires `com.gamecore.studio.core`; `Editor/GameCore.Gameplay.World.Editor.asmdef`
  unconditionally references `GameCore.Studio.Core.Editor` and `GameCore.Studio.Model`.
  Remove that mandatory gameplay→Studio dependency by moving
  `Editor/WorldLiveOpTranslator.cs` and `Editor/StudioAdmissionServices.cs` into a separately
  installed Studio integration package/assembly (e.g.
  `Packages/com.gamecore.studio.gameplay/Editor/GameCore.Studio.Gameplay.Editor.asmdef`).
  Preserve `WorldLiveOpTranslator.Register(StudioRuntime runtime)` and the existing
  `StudioAdmissionServices.BindAdmission(StudioRuntime, Func<SaveService?>, Func<bool>, Func<StageVerdict,bool>)`
  and the overload whose final argument is `Func<StageVerdict,AdmissionSmokeStatus>`, including
  their namespaces, for the creator bootstrap.
  Required behavior: `com.gamecore.gameplay.world` and its World.Editor assembly compile when
  Studio is absent; the full creator installation still registers translators/admission
  services. Update exact package/asmdef dependencies and game installation pins in that packet.
  STAGE-INT provides `check_dependency_boundary(&StageOptions) -> Result<(), String>` before
  Unity and regression `r2_11_stage_int_denied_transitive_dependency_has_named_seam`; it refuses
  the excluded dependency rather than importing Studio implicitly or weakening the allowlist.

- `studio/agent/src/stage.rs::StageRunner::probe_startup`: resolve a registered project's
  versioned cache (the same `pipeline::cache_version(&StageOptions)` contract as jobs) instead
  of `_warm/probe`. The generic startup probe has no manifest and now correctly refuses with
  `cache_invalid`. Per-job probes use the provisioned versioned cache. This file is outside
  STAGE-INT's exclusive `src/stage/**` set; no startup authority was weakened to mask it.

## Left open

- `studio/agent/Cargo.toml` declares Rust 2024 / 1.97.1. The host toolchain builds it. Edition
  migration is outside this packet's manifest scope; new implementation syntax does not require
  an edition change.
- The named `docs/studio/packets/R2-G-host-stage-tooling.md` does not exist in this baseline.
  Its merged instructions/evidence were read in `studio/stage/PACKET.md`,
  `studio/stage/evidence/R2-G-host/README.md` and P2.4's R2-G-host appendix.
- Full pressure-plate Unity tests and a passing signed verdict are blocked by the explicit
  gameplay→Studio dependency above. Those package/game files are outside this packet, and
  `tools/check_stage_slot.py` independently requires Studio exclusion. No temporary source
  rewrite, hidden transitive import, candidate exemption or host-confinement fallback was used.
- Installed/live-node qualification is excluded by the packet. The two paid/live-node fixtures
  stay unrun. Scratch authentication is supplied by the existing fake-node fixture. The real companion/launcher
  reached pressure-plate semantic/Rules execution and authenticated refusal; candidate Unity
  tests and the passing signing/fetch/verify path remain unexecuted.


## Final confinement follow-ups

Attempts on an initially cold Unity-only project confirmed two additional launcher integration requirements without
loading any candidate or GameCore source: public UPM metadata plus the matching cached archives,
and bounded compiler parallelism. `upm-probe.py` records the exact invocation and uses only this
packet's disposable cache/project copies. `upm-before.txt` records the missing archive refusal.
`upm-probe.txt` and `upm-engine-log-tail.txt` record the final **exit 0 in 75 seconds** with
`--network none`, read-only root, cap-drop, no-new-privileges and the unchanged **512 PID** limit.
Earlier attempts hit the PID ceiling and shader-compiler startup timeouts; an exploratory retry
also overlapped probe setup, so no result from that attempt is used as acceptance. The final
probe's project and executable are isolated and its completed run is the acceptance evidence. That final run resumes the partially imported
project; it is not a cold-cache or B-STAGE performance measurement.

The launcher sets `DOTNET_PROCESSOR_COUNT=4`, `-job-worker-count 4` and
`-diag-debug-shader-compiler`. The latter selects one shader compiler; its internal diagnostic
timeout never overrides the enclosing allocator/stage deadline. This behavior is documented by
[Unity's command-line reference](https://docs.unity3d.com/cn/current/Manual/EditorCommandLineArguments.html).
The pinned public UPM lock contains 44 cache records (606,784,683 bytes), checked for content and
index mapping integrity. The host's provider credentials and UPM configuration are not copied.

The parallel Rust run additionally exposed an existing lease lifetime race. A concurrent fork
can temporarily retain the same open file description after the parent's close. The new
regression duplicates that description deterministically, fails before explicit unlock, and
passes after it. `cargo-before-lock.txt` preserves the original CLI discard failure; final
`cargo-tests.txt` supersedes it. No retry or test serialization hides the race.

Full pressure-plate compile/tests, signed passing authority and warm B-STAGE performance still
require the outside-path gameplay/Studio dependency split. A trusted Unity-only compile probe
and successful semantic/Rules tests do not stand in for that missing acceptance.


One final repeat of the licensing regression exhausted its allocator deadline while all three
host-wide reservations were occupied (`docker-licensing-queue.txt`); no packet-owned Editor was
started in that interval. After a reservation became available, the unchanged test passed in
70.58 seconds (`docker-licensing.txt`). Other Editors/services were not interrupted and the
allocator limit was not changed. Final Rust total: **116 passes**, plus the separately reported
blocked full-stage acceptance fixture. Machine-readable totals are in `summary.json`.
