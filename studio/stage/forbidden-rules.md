# Staging lane: forbidden-content rules

`gamecore-studio stage` step `scan` (`studio/agent/src/stage/scan.rs`) runs these rules on every file of the
candidate package before any of its code is compiled or run. If any rule fires, the stage fails. The steps that
would run candidate code (`dotnet`, `unity-editmode`, `playmode-smoke`, `determinism`) are then skipped with
"scan failed". Each finding is a verdict `forbiddenHits[]` entry `{rule, path, line, excerpt}`, and the excerpt is
redacted.

The scan reads C# through a small lexer, not regular expressions. Comments, string contents and
`#if UNITY_EDITOR` branches are handled correctly, so a rule name inside a comment or string never fires.

| Rule | Fires on | Legitimate alternative / exemption |
|------|----------|------------------------------------|
| `reflection-emit` | `System.Reflection.Emit`, `DynamicMethod`, `ILGenerator`, `AssemblyBuilder`, `ModuleBuilder`, `TypeBuilder`, `MethodBuilder` | none: generated code must be source in the package |
| `process-start` | `Process.Start`, `ProcessStartInfo`, `System.Diagnostics.Process` | none |
| `file-write` | `File.Write*/Append*/Create*/Delete/Move/Copy/Replace/Open/OpenWrite/Set*/Encrypt/Decrypt`, `Directory.CreateDirectory/Delete/Move/CreateSymbolicLink`, `new StreamWriter/FileStream/BinaryWriter` | allowed when the statement's first string literal starts with `Assets/<package>/` or `Packages/<package>/`, or the statement uses `persistentDataPath` |
| `editor-in-runtime` | `UnityEditor` in a runtime assembly; a runtime asmdef referencing an Editor assembly | allowed in an Editor-only asmdef, under an `Editor/` folder, or inside `#if UNITY_EDITOR` |
| `dllimport` | `DllImport`, `LibraryImport`, `extern` | none |
| `native-plugin` | `.dll .so .dylib .bundle .a .lib .jar .aar .jnilib .exe` files, symbolic links | none |
| `unsafe` | `unsafe`, `stackalloc`, `fixed (...)`; asmdef `allowUnsafeCode: true` | allowed only when the proposal declares `allowUnsafe.reason` (non-empty); the reason is copied into the slot record |
| `network` | `System.Net*`, `UnityEngine.Networking`, `UnityWebRequest`, `HttpClient(Handler)`, `WebClient`, `(Http)WebRequest`, `TcpClient/Listener`, `UdpClient`, `Socket`, `(Client)WebSocket`, `Dns`, `NetworkStream` | none: mechanisms are offline |
| `resources-absolute` | `Resources.Load*("...")` with an absolute path, a drive colon, or `..` | use a path relative to a `Resources/` folder |
| `static-mutable` | non-readonly static fields, `static event`, static readonly arrays and mutable collections, `[ThreadStatic]` | `const`, `static readonly` immutable values, static methods and properties; simulation state belongs in components, which keeps the determinism step meaningful |
| `credentials` | `etk_ ett_ etp_ eta_` etos keys and tickets, `sk-` provider keys (16+ chars), `Bearer <token>` (8+ chars), in any text file | none: never commit a secret; the excerpt is masked |
| `binary-blob` | any file larger than 2 MB | allowed when declared in `proposal.blobs` or `package.json` `gamecore.blobs` |

`check_game_core_csharp.py` and `check_package_metadata.py` back up the scan in the `checkers` step. Run through
`studio/stage/slot-checks.py`, they apply to the slot package exactly as they apply to repository packages.
`tools/check_stage_slot.py` then checks the slot itself: the manifest allowlist, embedded files equal to
`stage.json`, and no stray assets.

No rule was relaxed for the pressure-plate sample, and `studio/stage/allowlist.json` lists only Unity packages and
the denied Studio and qualification packages. If a legitimate mechanism package is blocked by a checker rule, add
an explicit allowlist entry with a written reason. Do not change the rule.
