# Headless run configuration

The one supported execution shape for the GC-025 player evidence. It is what
`tools/unity/probe_runs.sh` implements and what `tools/unity/run_catalog_coverage_probe.sh` uses; this file records
it as the configuration rather than leaving it implicit in a shell script.

## 1. Player invocation

```sh
timeout --signal=TERM --kill-after=10 600 \
  "${PROBE_PLAYER}" \
  -batchmode \
  -nographics \
  -logFile "${log_file}" \
  -probeCatalogCoverage \
  -probeResult "${result_file}"
```

| Element | Value and reason |
| --- | --- |
| `-batchmode` | no window, no input, no play mode session management |
| `-nographics` | no renderer; the harness runs with the presentation adapters reporting their headless forms (04 §7) |
| `-logFile` | the player's own log, archived beside the result |
| `-probeCatalogCoverage` | one mode flag per invocation; exactly one mode is ever set |
| `-probeResult <path>` | the structured JSON result; without it the probe exits 1 rather than running unrecorded |
| **no `-quit`** | the probe exits itself through `Application.Quit` with a result-encoding exit code; `-quit` would kill it before it writes its result |
| `timeout … 600` | bounded so a hung player fails the step instead of hanging the gate |
| exit code | `0` with `"result": "Pass"`; `3` with `"result": "ExpectedNegative"` for the negative mode; `>= 128` is a crash (`139` SIGSEGV, `134` SIGABRT, `135` SIGBUS, `136` SIGFPE) and always fails |
| repetitions | `PROBE_RUNS` (default 5). Run 1 writes the canonical evidence files; runs 2..N write `<file>.run<N>`, and any dirty run fails the step |

**Audio stays disabled in this player.** The Linux headless player has no audio device, and enabling Unity audio in
it produced a native FMOD/PulseAudio crash at exit (`artifacts/crash-139/`, capture tool
`tools/unity/capture_crash_139.py`). The GC-020 traversal course therefore presents committed audio through an
engine-free recording sink and never constructs an `AudioSource`; do not re-enable audio on this host.

## 2. Result and log locations

| Shape | Result | Log |
| --- | --- | --- |
| qualification | `${ARTIFACTS}/probe/probe-catalog-coverage.json` | `${ARTIFACTS}/probe/player-catalog-coverage.log` |
| release | `${ARTIFACTS}/probe/probe-catalog-coverage-release.json` | `${ARTIFACTS}/probe/player-catalog-coverage-release.log` |

`ARTIFACTS` defaults to `artifacts/baseline/probe` for the probe harness and to
`artifacts/baseline/build` for the whole baseline build. The result is strict JSON with a fixed field order, so it
diffs cleanly between runs and between the two shapes.

## 3. What the mode asserts, headless

The mode runs `CatalogCoverageScenario` and writes one step per observation:

1. `catalog-reachability-manifest` — the committed manifest against the live generated catalogs (file hash,
   fingerprint, group/schema/registration/root counts);
2. `catalog-coverage-{probe,cards,checkpoint,traversal}-catalog` — each catalog's generated coverage companion
   resolves every registration through its own generated lookup, round-trips every declared schema through its
   generated serializer and executes every closed-generic root statement;
3. `catalog-coverage-registration-lookup` — every manifest registration and serializer root resolves by its derived
   key;
4. `catalog-coverage-closed-generic-roots` — every root statement of the manifest executes;
5. `catalog-coverage-traversal-generated-catalog` — the generated traversal catalog fingerprints identically to the
   hand-written generated-style table and resolves the same nine keys;
6. `catalog-coverage-late-mount-inactive-plugin` — the linked-but-inactive fixture plugins resolve by key and mount
   late;
7. `catalog-coverage-bake-runtime-parity` — the editor-baked and runtime-recipe materializations of the traversal
   course agree on definition, base layout, descriptor tag, applier registration, recipe-catalog fingerprint, run
   digest and canonical numbers;
8. `catalog-coverage-unknown-recipe-refused` — an undeclared recipe is `MissingDependency` and a declared recipe at
   another revision is `StalePlan`;
9. `catalog-coverage-world-stop-restart` — two course runs under two session salts create, drive and dispose two
   independent world hosts, with fresh session identities and the same digest;
10. `catalog-coverage-headless-canonical` — the headless run matches the pure-rule canonical fixtures and the frozen
    traversal digest.

The digest literal `77bc74196ec96b076bcc63b007cc0f57f5322121bad69f36c0e280d0576fbbb2` covers the observation-name
table, so a renamed, reordered, added or dropped observation fails the harness, the EditMode suite and the mode
itself rather than shrinking the gate silently.

## 4. Why a player run is required

P-058 and 04 §8 make stripping the point: the Editor runs the same managed code under the JIT and without managed
stripping, so an EditMode pass says nothing about whether a generated registration survived UnityLinker with High
stripping. The coverage companions are compiled into the same assemblies as their catalogs, so a registration or
serializer the linker removed fails in the player — and the qualification and release shapes are both run, because
the marker-free shape is the one that ships.
