# Semantic staging rules (D1)

The trusted service invokes the Roslyn analyzer inside the D3 sandbox during the dotnet step. Lexical scanning is an
early prefilter only. Candidates cannot disable rules, supply trusted references, grant `allowUnsafe`, or claim generated
code exemptions. Hashes of findings are not authenticated verdicts; only R2-F's signed stage record can authorize R2-B.

| Stable ID | Refusal |
|---|---|
| SG000 | Invalid C# 9 syntax or inactive conditional source that has not been analyzed. |
| SG001 | `InitializeOnLoad*`, `DidReloadScripts`, `MenuItem`, `ExecuteAlways`/`ExecuteInEditMode`, module initializers, direct/indirect `AssetPostprocessor` or `AssetModificationProcessor` inheritance. All candidate menu entry points are refused conservatively. |
| SG002 | Semantic references to `System.Reflection.Emit`. |
| SG003 | `System.Diagnostics.Process`, `ProcessStartInfo`, or `NativeLibrary` execution/loading. |
| SG004 | `System.Net` and descendants, `UnityEngine.Networking`, legacy `WWW`. |
| SG005 | Filesystem access without proven package/persistent-data containment, file-handle constructors, IO method-group escapes, link creation and implicit temporary-file creation. |
| SG006 | `unsafe`, pointers or function pointers. |
| SG007 | Native imports (`DllImport` / `LibraryImport`). |
| SG008 | Nonconstant static mutable fields, reference-backed readonly fields (including arrays), mutable static auto-properties, static events. The explicit `ScriptableSingleton<T>` field exception follows the project contract. |
| SG009 | `Resources.Load*` with absolute, traversing or unproven dynamic paths. |
| SG010 | Static Editor constructors/field initialization or Editor API calls outside declared `[AuthorOperation]` / `[AuthorValidator]`, GameCore catalog contributor, or sandbox NUnit test call chains. |
| SG011 | Reflective/dynamic execution that can evade API attribution. |
| SG012 | Unresolved invocations, attributes or base types in the supplied trusted reference context. |

Symbols, aliases, inherited types, parameters and constant values come from Roslyn semantic models. Text in comments
and strings does not identify an API. The scanner never compiles or loads a candidate assembly or evaluates code.

Filesystem proof is conservative: literal `Assets/<package>` paths must consist of normalized components with no rooted
prefix, backslash, colon, `.` or `..`. The other allowed root is the semantic `UnityEngine.Application.persistentDataPath`
property. `System.IO.Path.Combine` accepts one proven root followed only by normalized constant relative components.
All path parameters must pass (e.g. both source and destination of Copy); the contents argument mentioning a permitted
root proves nothing. Unknown locals, aliases to mutable strings, interpolation and dynamic joins are refused. `File` and
`Directory` calls are checked; arbitrary filesystem object handles are refused. The sandbox remains necessary to contain
candidate execution, including existing filesystem links and native engine behavior that static analysis cannot prove.

Editor helpers are permitted only when reachable through the declared extension/test call graph. This does not permit
automatic hook registration. No generated-source filename/header exemption exists: the pressure-plate generator now
returns fresh catalog arrays rather than shared mutable fields. Candidate test methods are scanned under the same forbidden
API/state/path rules as production code. Negative fixtures are parsing inputs, never Unity packages to install.

R2-F must enforce all mandatory steps, versioned warm caches and the 360-second warm budget; mark the one cold overrun
`coldCache: true`. Sandbox or licensing failure emits `stage_failed{sandbox_unavailable}` with no verdict. Host execution
requires explicit operator configuration, a `confinement: "host"` verdict, an admission warning and a reported deviation.
