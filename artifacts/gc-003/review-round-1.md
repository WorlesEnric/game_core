# Review round 1 — defects found and how each was closed

Two independent read-only reviews of the GC-003 revision ran on the authoring host (which has no C# compiler).
Every item below was verified against the sources and fixed in commit `gc003: fix the defects the first review
round found`; the fix commit is included in `git log` on the branch.

| # | Defect | Severity | Closure |
|---|---|---|---|
| 1 | `CatalogFactoryKinds.HasBalancedDelimiters` had no `return` (CS0161) | blocker | return statement restored; `tools/check_game_core_csharp.py` now detects a non-void method with no return, and the detector was tested against the broken and fixed text |
| 2 | `RepoLayout.ApiSnapshotPath` was deleted while three call sites still used it (CS0117 in two projects) | blocker | member restored next to the new `ResultDirectory` |
| 3 | The first `assembly-independence` fixture case lost `requirementIds`, which makes the loader throw and aborts the whole fixture suite in both runs | blocker | line restored; `tools/check_game_core_csharp.py` does not load fixtures, so this was caught by review only |
| 4 | The API-snapshot tool referenced the W0 seam, and SDK project references flow transitively, so `GameCore.Contracts.Tests` saw two assemblies declaring `GameCore.Contracts` types (CS0433) | blocker | the tool's project reference removed (it loads assemblies by path and never used the seam); the contract test project's closure no longer contains the seam |
| 5 | `CatalogDescriptionReader` passed a null serializer table to the catalog cross-check, rejecting every description that declares a schema | blocker | new nested `DeclaredSerializer` stands in for the emitted serializer (same key, schema and declared field table) and is passed to `ImmutableCatalog.Build` |
| 6 | `CatalogGenerator.Verify` ignored the `mismatch:` verdict of `FilePrefixHash`, so an edited generated file verified as correct | high | `Verify` now fails on a mismatched hash; the shared `CatalogEmitter.MismatchPrefix` names the sentinel |
| 7 | `CatalogEmitter.EmitFieldCase` had lost its `break;` and closing `}`, so the emitter could not reproduce the committed catalog (CS0163/CS1513 after regeneration) | blocker | both emissions restored, with a new test that asserts one `break;` per declared field and balanced braces in generated output |
| 8 | `ProbeRecordValue`/`ProbeRecordSerializer` are nested inside the generated `ProbeCatalog`, so the unqualified uses in `ProbeRunner` do not resolve (CS0246) | blocker | uses qualified as `ProbeCatalog.ProbeRecordValue` / `ProbeCatalog.ProbeRecordSerializer` |
| 9 | The partitioned-writers test asserted success with an unregistered system key | medium | the key is registered in the fixture catalog (`SecondSystemKeyId`) |
| 10 | `CrossCheckMemberNames` reserved only a few generated member names, so a description could collide with `Serializers`, `SchemaRegistrations`, `BuildCatalog`, ... and emit uncompilable code (CS0102) | medium | the reserved set now mirrors the emitter (constants, tables, methods, root method, and every schema-declared type and key name) |

Round 1 also independently recomputed the committed catalog's fingerprint and disagreed with the value in the
file (`29b07e98...` versus `a4ea6f91...`). The committed value was first produced by a local mirror of the
emitter; the mirror was corrected (it had omitted the per-schema serializer registrations) and
`tools/verify_generated_catalog.py` now recomputes both the file hash and the fingerprint from the generated
tables, which is the same computation the player performs in `FingerprintMatchesGeneratedCatalog`. If the
build host's compiler disagrees, regeneration (§3.3 of HANDOFF) is the fix.
