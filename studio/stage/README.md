# Staging slot tooling (R2-G-host)

Generated code reaches a live Editor only after the companion stage service issues a passing authenticated verdict
and the creator explicitly chooses Admit. Artifact bytes, candidate `allowUnsafe` explanations and local CLI output
cannot authorize admission. `mechanism.admit` and `mechanism.remove` are internal UI capabilities (R2-B).

## Lane contract

R2-F owns execution: seven mandatory steps (`scan`, `checkers`, `dotnet`, `unity-editmode`,
`playmode-smoke`, `determinism`, `budget`), a minimal slot, and a **360 second warm budget**. A cold cache can exceed the budget once;
the verdict records `coldCache: true`. Partial steps, unavailable sandbox, analysis errors and findings refuse admission.
The dotnet step must run the semantic analyzer before any candidate tests. The lexical scanner remains a prefilter.
The historical 10-minute project-copy lane and legacy packageRef shell contract are retired; `stage.sh` returns
`stage_failed{legacy_stage_request}`. Submit the retained candidate by `changeSetId` through the authenticated,
project-scoped companion route so proposal, stage inputs and job ownership survive intact.

Default `confinement = "docker"`: no network, read-only Unity Editor and individual licence files, slot-local HOME,
only slot and versioned warm cache writable, no live project or credential directories mounted. Copy trusted package
sources into the slot and remap manifest pins there; original host `file:` pins produced during slot preparation are
not permission to mount the live repository. R2-F owns this remapping and process-group termination. Failure to
start or license the sandbox returns `stage_failed{sandbox_unavailable}` with **no verdict**. Never fall back silently.
An operator can explicitly configure `confinement = "host"`; every verdict must carry that value, admission displays
a warning, and the completion report records the deviation. This packet does not opt the installed service into host mode.

`probe-sandbox.sh <new-output-directory>` attempts a minimal, candidate-free Docker Unity startup under the host
allocator, without running the lane. It logs through the redacting writer. See [PACKET.md](PACKET.md) for the observed
host outcome and evidence. A loader failure is not a successful licence test.

## Slot preparation and validation

```sh
python3 studio/stage/make-slot.py --slot-root /tmp/stage --slot sample \
  --source-project games/hollowmere --candidate samples/mechanisms/pressure-plate/candidate
python3 tools/check_stage_slot.py /tmp/stage/sample
python3 tools/check_stage_slot.py --self-test
```

`candidate/change-set.json` must contain exactly one `mechanism.propose`. Artifact names are basenames; SHA-256 is
verified only after link rejection and resolved containment. `proposal.rules.directory` and `.tests` are normalized,
package-relative paths, validated before XML substitution. XML attribute values are escaped. Package archives reject
traversal, links, devices and oversize content. Prebuilt assemblies, native plugins, response files and asmrefs are refused.

`stageInputs` permits only images (`png/jpg/jpeg/webp`), audio (`wav/ogg/mp3`), plain data (`json/txt/csv`) and meshes
(`fbx/glb/gltf`). It rejects Editor/Plugins/Tests and executable files, including when explicitly naming a directory.
No `.prefab`, `.controller`, `.mat`, code, shader, assembly or importer-hook bytes enter through this data lane.
Metadata is checked against the standard data importer set; custom importer state is refused. Links are never followed.
Trusted source `Assets/Settings/*.asset` is a narrow Unity-YAML exception for render configuration, with recorded hashes,
not an executable-content exemption. All harness/template bytes must match the committed `template-manifest.json`.
Update that manifest only with a reviewed trusted template change. The checker verifies template bytes again.

## Semantic analyzer (trusted service seam)

```sh
dotnet test studio/stage/analyzer/Tests/StageAnalyzer.Tests.csproj
dotnet studio/stage/analyzer/bin/Debug/net8.0/StageAnalyzer.dll \
  --root /slot --rules /slot/trusted-analysis-rules.json --out /slot/out/findings.json
```

Exit **0**: no findings; **3**: findings; **2**: invalid inputs/analysis error. Output schema is
`gamecore.stage.findings/1`, with `findings[{RuleId,File,Line,Message}]`. Messages are fixed and never echo source literals.
`--root` accepts a prepared slot, package directory, or sample directory containing `package/`.
Rules are operator/service-owned JSON, never the candidate's proposal:

```json
{"Schema":1,"References":["/unity/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll"],"SupportSources":["/slot/trusted-packages"]}
```

Provide the complete trusted Unity/Unity package/NUnit metadata set and trusted GameCore source context. Sources are
parsed, never executed; metadata is read through Roslyn, never loaded as candidate assemblies. Unresolved calls,
attributes and base types are findings, so an incomplete reference context cannot manufacture a pass. Candidate tests
are scanned too; NUnit test entry points can call trusted Editor APIs in the sandbox but cannot bypass forbidden APIs.
Inactive conditional source is refused; submit a single audited source variant. See [forbidden-rules.md](forbidden-rules.md).

The console targets net8.0/C# 9 and uses Microsoft.CodeAnalysis.CSharp **4.5.0**, whose analysis libraries support
netstandard2.0. That exact package and its transitive dependencies already exist in `~/.nuget/packages` on myubuntu.
`analyzer/NuGet.Config` clears all feeds; NUnit 3.14.0, adapter 4.5.0 and Test SDK 17.11.1 are also pinned cached versions.
Mount the expanded package cache read-only at `/nuget`, set `NUGET_PACKAGES=/nuget` and slot-local `DOTNET_CLI_HOME`.
Provision those versions from NuGet.org in a separate trusted network-enabled image/cache preparation step on a new
host; stage execution never restores from the network. No package binary is vendored in git.

`analyzer/offline-check.sh <new-work-directory>` reproduces restore, unit tests and positive/negative scans with
Docker `--network none`. Its sample metadata comes from the existing host warm cache, copied into its scratch slot;
production must use the versioned cache owned by R2-F. It runs no candidate code and no full stage lane.

## Host execution and warm cache

All four launchers use `studio/tools/unity-slot.sh`. One allocator flock covers process enumeration and reservation;
locked slots plus Editors outside live owner process trees count against a maximum of three. Interactive Editors count;
AssetImportWorker children do not. Owner diagnostics are removed while holding the lock, stale owners require a dead PID,
and locked files are never unlinked. Each launcher holds at most one reservation. TERM reaches the child's process group.

`unity-batch.sh` uses `-logFile -` and `run-redacted.py`; no persistent raw log is created. The writer masks D9 token
families and JSON key/token/secret values before writing, buffers split/multiline records and drops oversized incomplete
records. Its child environment is an explicit list, excluding DOTNET startup hooks and arbitrary UNITY/GAMECORE variables.
This operator launcher is not an OS sandbox. R2-F must reserve on the host before starting the container,
then make the trusted Unity command launch the sandbox (as the probe does). Never relocate the host allocator
under slot-local HOME or reserve only inside a container. Candidate execution itself stays inside the sandbox.

`test-results.py` distinguishes passed, failed and partial/not-run XML. Every selected test case must pass for acceptance; use repeated `--require-test <fullname>` arguments on
`unity-batch.sh` or `unity-compile.sh` to also require designated cases to appear and pass;
zero failed plus skipped/inconclusive is partial, never PASS. Missing/malformed XML refuses. Runtime rendering assertions
and world/media operation correctness remain with their package/test owners.

```sh
studio/stage/cache-key.sh games/hollowmere
bash studio/tools/tests/unity-batch-lock.sh
python3 -m unittest discover -s studio/stage/tests -v
```

The cache script emits `stage-cache-v1-<sha256>` over Unity version and sorted kernel/gameplay package names + versions.
R2-F must key warm caches by this output, seed only on a miss, and invalidate automatically on upgrades. Same versions
with changed source require version bumps. Sample candidate IDs remain fixed deterministic fixtures: use fresh project
journals for acceptance, or `--reset-journal` only on an explicitly designated scratch project whose operator-created `.gamecore-stage-scratch` file
contains its exact absolute project path. The script refuses journal deletion without that marker. Never reset a creator journal.
