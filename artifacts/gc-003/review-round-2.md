# Review round 2 — re-review of the fixes

A second read-only review round checked the fix commit and the two areas no reviewer had read yet. Everything
below was verified against the sources.

| Area | Verdict |
|---|---|
| `EmitFieldCase` emits complete, compilable case blocks | confirmed |
| Emitter output versus the committed `ProbeCatalog.g.cs`, member by member | **one defect (fixed)**: `EmitCatalogFactory` omitted the blank line the committed file had before the closed-generic-root block |
| Generated `TryDeserialize` locals, `switch`/`default`, constructor call, `out` paths | confirmed |
| `DeclaredSerializer` compiles and cannot throw for reader-reachable input | confirmed |
| `CrossCheckMemberNames` rejects only the deliberate fixtures | confirmed, plus **one residual defect (fixed)**: a class named after one of its own generated members was accepted |
| `CatalogGenerator.Verify` fails on an edited file and succeeds on a correct one | confirmed, path-independent |
| `ProbeRunner.RunProductionCatalogProbe` compiles and its assertions hold | confirmed (kind `Handler`, tamper → `ChecksumMismatch`, truncation → `Truncated`, absent key → `MissingDependency`) |
| `ManifestValidationTests` and `CompilerTests` assertions | confirmed, except **one reproducibility gap (fixed)**: `DeclarationOrderDoesNotChangeGeneratedBytes` could not pass because the emitter kept group declaration order; the same review found that `supportedFeatureIds` order reached the bytes while the fingerprint sorted them |
| Production surface versus the frozen snapshot, line by line | `no removals or signature changes found`; the four additive deltas are exactly the ones recorded in HANDOFF §5 |

Discrepancies found by this round and how each was closed in commit `gc003: reserve the generated class name and
reproduce the committed catalog exactly` and the commit that follows it:

1. **Missing blank line before the closed-generic-root block.** `EmitCatalogFactory` now appends it. The
   committed catalog was regenerated from the emitter, and the reviewer re-walked the Append sequence against the
   committed file: 22579 emitted bytes, byte-identical, trailing newline present, zero CR.
2. **Group declaration order reached the output.** `EmitTables` now sorts group tables by ordinal table name and
   schemas by schema identity; the committed catalog was regenerated with `HandlerRegistrations` before
   `PluginRegistrations`. The reviewer confirmed the two permutation fixtures now emit identical bytes.
3. **`supportedFeatureIds` order reached the output** while `CatalogFingerprint.Compute` sorted the same ids, so
   two descriptions with the same feature set produced different file hashes and the same fingerprint. The
   reader now canonicalises the feature list, and `FeatureDeclarationOrderDoesNotChangeGeneratedBytes` covers it.
4. **A class named after one of its own generated members was accepted** (`className: "Serializers"` emitted
   `public static class Serializers` containing `public static readonly ... Serializers` — CS0542).
   `CrossCheckMemberNames` now reserves the class name in both directions, with a test per direction.

Round 2 also confirmed the committed catalog's recorded values independently: `sha256` of the prefix before the
`CatalogFileHash` declaration equals the literal, and `CatalogFingerprint` equals
`CatalogFingerprint.Compute` over the three factory registrations, the one schema registration, the one
serializer registration and the one declared feature the file's `BuildCatalog()` builds.
