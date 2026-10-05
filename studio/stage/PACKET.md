# PACKET R2-G-host

Branch: `codex/r2-g-host`. Host: myubuntu (Linux). Scope: stage Python/analyzer, owned shell launchers, sample fixtures,
and the R2 section of P2.4. No gameplay adapter, Rust service, live Unity project, installed companion or ETOS changes.
Root `PACKET.md` is outside the exclusive paths; this is the packet's report.

## R2 fixes

| Finding / reconciliation | Implementation | Regression test |
|---|---|---|
| R2-08 | Artifact basenames and resolved containment checked before hashing; links refused. | `check_stage_slot.py --self-test`: `R2_08_ArtifactTraversal`, `R2_08_ArtifactLink` (real matching-digest outside targets). |
| R2-11 | Roslyn symbol/path rules, trusted-symbol checks, extension call chains; no candidate exemptions; prebuilt plugins refused. | Analyzer `R2_11_ForbiddenSemanticSymbols`, `R2_11_FilesystemPathsMustBeProven`, `R2_11_AuthorOperationHelpers`, `R2_11_EditorStaticConstructor`, `R2_11_CommittedNegativeFixtures`, forged-root/Combine, mutable-value and hidden-source tests; checker `R2_11_PrecompiledPlugin`. |
| R2-12 | Data-only inputs/settings, no links or executable directories, standard importers, trusted template digests. | Checker `R2_12_SettingsExecutable`, `R2_12_EditorInput`, `R2_12_AssemblyInput`, `R2_12_TemplateTamper`, `R2_12_Link`, `R2_12_ExplicitEditor`. |
| R2-13 | Strict proposal rules/tests paths before XML generation; retired legacy packageRef shell. | Checker `R2_13_RulesTraversal`, `R2_13_XmlInjection`; Python `test_R2_13_LegacyRequestRefused`. |
| R2-17 | Atomic host allocator mutex covers count + reservation for all four launchers; live PID checks, locked-file preservation, TERM forwarding. | `studio/tools/tests/unity-batch-lock.sh`: `R2_17_AtomicReservation`, `R2_17_StaleOwnerAndTerm`. |
| R2-18 | Explicit operator-child environment names, no DOTNET/UNITY/GAMECORE prefix inheritance. | Python `test_R2_18_EnvironmentIsEnumerated`. Service config/path authority remains R2-F. |
| R2-19 | Editor stdout and stderr pass through an incremental redacting writer before disk; process-group termination; no `.raw` capture. | Bash `R2_19_StreamRedaction`; Python `test_R2_19_RedactorAllForms`, `test_R2_19_PartialAndMultilineLogs`. |
| R2-37 / O48 | XML pass/fail/partial preserved; skipped/inconclusive never acceptance; named `--require-test` cases must appear and pass. | Python `test_R2_37_O48_XmlDispositions`, `test_R2_37_RequiredCaseAndSuiteDisposition`; bash `R2_37_CompilerErrorsPartialAndMissingCases`. |
| O49 | Minimal slot, seven steps, 360-second warm budget and explicit one-time cold overrun documented. | Real minimal pressure-plate slot construction + both slot checkers; budget enforcement remains R2-F. |
| O52 | Versioned cache key from Unity + kernel/gameplay versions; deterministic sample IDs documented; fresh catalog arrays and no unnecessary live stage inputs. | Python `test_O52_CacheKeyTracksVersions`; sample generator `--check` runs; positive semantic scan; `test_O52_JournalResetRequiresScratchMarker`. |
| O54 | Unity command-line example uses `GameCore.Studio.Edit.StageCommandLine`. | Shell syntax inspection; real type declaration read. No Unity admission invocation requested. |
| R2-34 / R2-41 script side | Wrappers now require real XML acceptance and support required named cases. Direct proxy/media or world asset setup is not recorded as actual tool integration proof. | XML required-case regression. Gameplay operation and media adapter fixes remain with their explicitly assigned owners. |

## Verification

Final results and commands are recorded in `evidence/R2-G-host/README.md`. The offline analyzer test uses Docker with
`--network none`, a read-only SDK/NuGet cache/Unity installation, scratch HOME, and no live project mount. It restores
Microsoft.CodeAnalysis.CSharp 4.5.0 and pinned tests entirely from the installed expanded NuGet cache. It parses both
positive pressure-plate and committed negative fixtures without executing candidate code.

The separate candidate-free Docker Unity probe reached licensing, then the Editor exited **198** after approximately
56 seconds. Its exact messages include `com.unity.editor.headless was not found` and `No valid Unity Editor license found`.
The existing entitlement XML was bind-mounted read-only; its contents were never read or printed by tooling. The probe
used the shared host reservation and issued no verdict. **Observed mode: Docker starts; Unity cannot license.** Required
lane outcome is `stage_failed{sandbox_unavailable}`. No `confinement = "host"` fallback or operator opt-in was performed.

## Requests to other packets

1. **R2-F, `studio/agent/src/stage/pipeline.rs`, `Run::step_dotnet(&mut self)` (current private step method):** run
   `dotnet <trusted StageAnalyzer.dll> --root <slot> --rules <trusted-context.json> --out <slot>/out/findings.json`
   inside the OS sandbox **before** candidate compilation/tests, including packages without a rules half. Require exit
   0 and an empty findings list; exit 2/3 or absent output must fail the mandatory dotnet step. Supply full trusted
   Unity/package metadata and GameCore support sources using the documented `Rules` JSON schema. Candidate proposal
   bytes may not set rules, references, support source paths or exemptions. Bind findings and step results into D2's verdict.
2. **R2-F, `studio/agent/src/stage/pipeline.rs`, `run_child(...)`, Unity launcher and warm-cache preparation:** reserve
   through `studio/tools/unity-slot.sh::unity_slot_acquire` on the **host**, then launch the container as the trusted Unity
   command, keeping the reservation until it exits. Do not put the allocator under slot-local HOME. Rewrite prepared
   absolute host package pins to trusted copies inside the slot; do not mount the live repository/project. Use Docker
   `--network none`, read-only Unity/licence mounts, scratch HOME, only slot + versioned cache writable. Keep auxiliary
   Unity/licensing component logs in ephemeral storage or route them through the redactor; `-logFile -` handles Editor
   output but is not authority for arbitrary child-written log files. Enforce `sandbox_unavailable` with no verdict on the
   recorded licence failure. Call `cache-key.sh <source-project>` for cache identity, retain the seven steps and 360-second
   warm budget, and record the single cold exception as `coldCache: true`.
3. **R2-F, `studio/agent/src/stage/env.rs`, `filter_env(...)` / `StageOptions::from_env(...)`:** replace prefix allowances
   with an explicit service environment; reject startup hooks and candidate tool overrides. Validate trusted executable,
   cache and licence mount paths. Operator `UNITY`/CLI choices in these scripts do not grant service-route authority.
   **`studio/agent/src/stage.rs`, request dispatch:** refuse legacy `{packageRef}`; retain proposal/inputs for the
   authenticated, project-scoped `{changeSetId}` route and return the trusted signed verdict route to R2-B/D.
4. **R2-A/core redaction owner:** publish the canonical `Redactor.Redact(string) -> string` and shared conformance corpus
   for D9 (all six token families, nested JSON values, escaped keys, multiline and partial records). Script seam is
   `studio/stage/redact.py::redact(text: str) -> str`, consumed only before writes by `run-redacted.py`. This packet ships
   the D9-compatible Python implementation; reconcile it with the canonical core implementation/corpus rather than
   claim cross-language single-source integration has already happened. R2-F must remove raw child spool files and
   post-hoc redaction from `run_child` / `redact_unity_logs`.
5. **R2-G gameplay owner and R2-E**, `Packages/com.gamecore.gameplay.world/Editor/WorldTools.cs`:
   keep `ConnectRegions(WorldDefinition world, RegionDefinition regionA, RegionDefinition regionB, string assetPath="")`
   and `AddPortal(AuthoredRegion region, PortalDefinition portal, Vector3 position, float yaw=0, float arrivalDistance=2.5f)`
   bindable through `[AuthorArg]`, with one journaled connection operation and full inverse. Supply exact regression test
   fullnames for second-step failure and one-Undo behavior to the acceptance launcher using `--require-test`.
6. **R2-A/D + gameplay owner**, `DialogueTools.cs::GenerateVoice(DialogueGraphDefinition,int,string)` and
   `AudioTools.cs::MediaGateways.Resolve()`: consume the shared async media adapter seam instead of constructing the
   unconfigured gateway / discovering a separate interface. Provide required XML test names exercising actual
   `dialogue.generateVoice` and audio tool IDs through journaled import/bind. No paid provider call was used here.
7. **R2-H/C**, rendering and views fixture test files: replace vacuous passes and obsolete Inconclusive catches. Use
   `unity-compile.sh ... --require-test <fullname>` (repeatable) for every designated acceptance test. The wrapper refuses
   partial XML but cannot turn a fixture's false `Assert.Pass` into real graphics proof.

## Left open

- Unity licensing in the default Docker sandbox is unavailable on this host with the installed entitlement. Evidence
  records the actual failure; an offline/container-compatible licence or a separately authorized explicit host-mode
  configuration is required. This packet does not change licences or service configuration.
- R2-F owns pipeline invocation, cache storage/version invalidation, sandbox launch and verdict authentication. This
  branch provides and tests their tooling contracts; it cannot prove those Rust changes from its exclusive paths.
- Canonical core redactor sharing is an integration seam owned outside this packet; script streaming redaction is tested
  but a single cross-language core implementation is not claimed.
- Full stage, Unity gameplay/admission tests, media providers and graphical acceptance were not run: the user explicitly
  excludes the full lane, gameplay files belong to another packet, and the sandbox licence probe failed. No Rust source
  changed, so no Rust build/test suite was run. No installed companion/etosd process was stopped or restarted.
