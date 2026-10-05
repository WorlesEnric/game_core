# R2-F2 — offline Unity sandbox licensing

Branch `codex/r2-f2`, base `e4ffd40f940293d4ef48169f4a4e9e6cfff75202`; myubuntu, 2026-10-05.
Implementation commit `3e14b52b`; inherited-test alignment commit `d92d788d`.
Work and tests use this clone and `~/.cache/gamecore-studio/r2-f2`. No installed companion,
etosd, sibling clone, Unity package, game, or live node was changed. No paid operation ran.

## Outcome

**Docker licensing PASS.** A private writable copy alone still gives Editor exit 198.
The same copy with the original HOME path, UID/GID, hostname and read-only `/etc/machine-id`
licenses successfully with **`--network none`**. No network exception, Hub socket, host IPC,
host HOME or writable host licence mount is needed.

The default Docker launcher now makes a fresh private HOME beneath the job slot for each
confined process. It copies only Unity entitlement/configuration state (and a ULF if present),
never logs, telemetry, Hub state, general configuration or key files. Top-level copy mode is
0700 and copied files are 0600; symlinks/special files and unapproved source roots refuse.
Copies are not hardlinks. Each process deletes its copy when reaped; any abrupt-kill remainder
is inside the job and deleted with that job. Nested bind destinations are pre-created by the
host user so Docker cannot leave root-owned mount directories in the private HOME.

The seven default mounts are job (rw), versioned cache (rw), Editor (ro), trusted Packages
(ro), job tmp at `/tmp` (rw), private HOME at the original HOME path (rw), and `/etc/machine-id`
(ro). If installed, `/var/lib/unity` and system services-config get separate **private-copy**
mounts; neither exists here. Root remains read-only, capabilities dropped, privilege escalation
disabled, image pulls disabled, and the host Docker client configuration stays outside mounts.

The real companion launcher also exposed a pre-existing R2-F/R2-G log seam mismatch:
`unity-batch.sh` now captures engine output instead of echoing it. The probe now checks its
retained redacted engine stream for both the resolved entitlement and completed quit, in
addition to successful allocator exit. `-logFile -` is treated as stdout, never a host file
named `-`. An empty/stale/missing handshake, failing exit, licence refusal or timeout refuses.

## Inventory and attempts

[Inventory](inventory.txt) records paths, type/mode/owner and sizes only. There is no ULF in
`.config/unity3d/Unity` or `.local/share/unity3d/Unity`; the latter directory does not exist.
The installed entitlement is `.config/unity3d/Unity/licenses/UnityEntitlementLicense.xml`,
with client configuration at `Unity/config/production.json`. `/var/lib/unity` and
`/usr/share/unity3d/config/services-config.json` are absent. No token/license contents or
machine-id bytes were printed or committed.

| Attempt | Result | Evidence |
|---|---|---|
| Host, allocator + empty scratch project | exit 0, entitlement resolved | [host.txt](host.txt) |
| (a) Private writable state; original HOME path; UID/GID 1000:1000 | exit 198; zero matching entitlement groups, headless entitlement missing | [copy.txt](copy.txt) |
| (b) Same plus hostname and read-only machine-id | exit 0; entitlement resolved; successful quit | [identity.txt](identity.txt) |
| (c) Network requirement | `--network none` throughout both Docker experiments and production runs; no network relaxation required | same commands |
| Default launcher from this clone | exit 0; no private copy left | [launcher.txt](launcher.txt), [spec](launcher.json) |
| Pressure-plate full stage invocation | licensing, scan and checkers pass; mandatory semantic dotnet step fails closed | [pressure-stage.txt](pressure-stage.txt), [verdict](pressure-verdict.json) |

[probe.py](probe.py) records the exact Docker and allocator commands. In (a)/(b), Docker's
nested binds initially left root-owned directories, causing cleanup to fail. Only those two
scratch HOME copies were cleaned with an offline helper container (only the copy mounted);
the helper had DAC_OVERRIDE for cleanup, **never the Unity sandbox**. No copies remain. The
production implementation and final probe script prevent this by pre-creating bind targets.
Two intermediate launcher runs licensed Unity but the old stdout readiness check refused;
a subsequent retained-stream fix produced the passing launcher result above. They never
issued a passing verdict.

## R2 fixes and regressions

| Finding | Fix | Test |
|---|---|---|
| R2-11 / R2-F2 licensing | Private original-path licensing state and machine identity, network still disabled | `r2_f2_default_docker_launcher_licenses_offline_and_removes_private_state` (real Unity, fails with baseline launcher, passes after fix) |
| R2-11 / R2-F2 confinement | Exact mount list; no writable host state, network or extra host paths | `r2_f2_licensing_command_mounts_only_private_state_and_fixed_identity`; existing `r2_11_docker_isolation_blocks_host_files_environment_and_network` |
| R2-11 / R2-F2 state isolation | Distinct writable copies; untouched originals; cleanup without following links | `r2_f2_private_license_copies_are_writable_isolated_and_deleted`; `r2_f2_license_links_and_unapproved_sources_fail_closed_and_clean_up` |
| R2-11 / R2-F2 readiness | Require actual engine licensing/quit markers and successful allocator result | `r2_f2_probe_requires_engine_handshake_even_when_allocator_exits_zero`; real CLI test also requires retained stream and no `-` file |

The existing `the_repository_allowlist_loads_and_bad_entries_are_refused` asserted a nonempty
exemption list, contrary to the R2-G manifest already in this baseline. It now requires the
empty list; malformed-rule/reason refusal coverage remains. No production scanning policy
was loosened.

## Validation

[Final validation](validation.txt): fmt/clippy clean; **111 executed Rust passes, zero failures**
(84 unit, 21 fake-node, four stage-lane, one explicit Docker isolation, one explicit real Unity
licensing acceptance). Four external tests remain unrun (two real-node, two full real-stage).
The same real licensing test fails against the original launcher from `e4ffd40`
([before](regression-before.txt)) and passes with the fixed source ([after](validation.txt)).
The earlier full Rust run had one stale empty-exemption expectation failure, corrected as
explained above. Package metadata passes (41 packages, 90 assemblies, three lock sources);
C# policy passes (1,136 files); whitespace and probe-script syntax checks pass.

Probe work began at 10:12 UTC and concluded within 25 minutes, below the 90-minute limit.
All Unity launches went through the shared host allocator, holding at most one Editor.
Final command results and before/after licensing regression are recorded alongside this packet.
The full pressure-plate verdict is **failed**, not admission authority. No candidate EditMode
or PlayMode XML was generated because dotnet refused before Unity candidate execution.
The successful Unity runs here are licensing/quit probes, not candidate tests or B-STAGE qualification.

## Requests to other packets

- Stage image/cache provisioning owner: pre-provision `Microsoft.CodeAnalysis.CSharp` 4.5.0
  and its dependency closure in the **versioned writable cache's `nuget/` directory**, or a
  trusted image feed copied there, plus the pinned Rules test dependencies. The fixed launcher
  sets `NUGET_PACKAGES=<versioned-cache>/nuget`; never enable container networking for restore.
  Current image `gamecore-stage:6000.0.75f1-v1` does not supply this cache and the real run returns
  NU1100 before the semantic analyzer starts.
- R2-F/R2-G integration owner: reconcile `studio/agent/src/stage/pipeline.rs::Run::semantic_scan`
  with `studio/stage/analyzer/Program.cs::Rules` and `Program.Main`. The caller currently writes
  a string `schema` plus policy booleans and requires `{pass:true,findings:[]}`. The analyzer
  currently requires case-sensitive `{Schema:1,References:string[],SupportSources:string[]}`
  and emits `{schema:"gamecore.stage.findings/1",findings:[]}` with exit 0 on success. Supply
  trusted Unity/NUnit/package analysis references and agree one strict result contract; do
  not bypass the semantic gate. This mismatch is source-confirmed, not a claim that the
  analyzer ran past NU1100 in this packet.
- Integrator: `studio/agent/Cargo.toml` still declares Rust 2024 / 1.97.1 outside this packet's
  exclusive paths. Converting the crate to Rust 2021 requires a coordinated manifest and
  existing let-chain migration. New code here uses Rust-2021-compatible syntax.

## Left open

- Passing full stage/admission remains blocked by missing offline semantic-analyzer dependencies
  and the subsequent analyzer contract mismatch described above. This packet delivers the
  requested licensing fix and the actual failed stage verdict; it does not relabel partial
  work as a pass or issue a signed admission record.
- The two existing real-node suites are not run: their documented prerequisites would touch
  the installed companion/live node, prohibited by this packet. Existing real-stage success
  suites remain prerequisite-blocked by the mandatory dotnet gate; the explicit pressure-plate
  CLI run above records that failure.
- `R2-G-host-stage-tooling.md` is absent from the baseline. Its corresponding merged note and
  evidence were read at `studio/stage/PACKET.md`, P2.4's R2-G-host section, and
  `studio/stage/evidence/R2-G-host/README.md` instead.
