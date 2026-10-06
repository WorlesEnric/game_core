## Shared media importer contract (R5 request #8)

`asset.import.args.importer` is a settings object, not an Inspector label map.
Emit exact, case-sensitive C# enum literals: `{"textureType":"Sprite",
"spriteImportMode":"Single","spritePixelsPerUnit":100}` for a single UI sprite.
Never emit `"Sprite (2D and UI)"`, `"Normal map"`, integer enum values, or guessed
settings. The live engine deliberately refuses these with `MediaImporterInvalid`.
For textures, `filterMode` is `Point`, `Bilinear`, or `Trilinear`; `wrapMode` is
`Repeat`, `Clamp`, `Mirror`, or `MirrorOnce`. Boolean properties take JSON booleans.
`textureType` uses Unity's enum names (for example `Default`, `NormalMap`, `Sprite`).
`Sprite` must accompany either sprite setting; only `Single` sprites are supported.
The versioned `importer-contract.json` records the enum subset for offline checks;
the project's live tool catalog and strict engine media policy remain authoritative.
Raw artifacts remain non-executable media/data only. Do not import code, assemblies,
import hooks, or raw prefab/controller/material files. Use the staging lane for code.
The original failed lantern candidate is retained unchanged in
`tests/fixtures/request8-original.json`; it is a negative example, not a template.
