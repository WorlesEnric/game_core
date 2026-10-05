# W-MECH-01 sample mechanism: pressure plate

A complete mechanism package that the P2.4 staging lane can stage, scan, compile, test and smoke: a pressure plate
that is pressed while enough actors stand on it. It is also the reference shape for agent-proposed mechanisms.

## Layout

| Path | What it is |
|---|---|
| `package/` | UPM package `com.hollowmere.mechanism.pressureplate` 0.1.0 (Unity 6000.0); every file and folder has a `.meta` |
| `package/Rules/` | `Hollowmere.Mechanism.PressurePlate.Rules` (engine-free, no references): `PressurePlateRules.Press` |
| `package/Runtime/` | `Hollowmere.Mechanism.PressurePlate`: declarations, kernel, catalog set, mechanism, world surface, smoke, authoring, binder |
| `package/Runtime/Generated/` | `Hollowmere.Mechanism.PressurePlate.Generated`: the plate's own generated catalog |
| `package/Catalog/` | `PressurePlateCatalog.catalog.json`, the catalog description the generated file is emitted from |
| `package/Editor/` | `Hollowmere.Mechanism.PressurePlate.Editor`: the `mechanism.pressurePlate.add` tool |
| `package/Tests/Rules/` | `Hollowmere.Mechanism.PressurePlate.Rules.Tests` (EditMode and plain dotnet) |
| `package/Tests/Editor/` | `Hollowmere.Mechanism.PressurePlate.Editor.Tests` (catalog, catalog set, Extend, smoke, tool) |
| `overlays/forbidden/` | `Runtime/Diagnostics/PlateTelemetry.cs`: `Process.Start` and a static mutable field |
| `overlays/failing-test/` | `Tests/Editor/FailingFixtureTests.cs`: a deliberately failing test |
| `candidate*/` | the three staging-lane candidates (generated, committed) |
| `make-catalog.py` | writes the catalog description and runs `tools/emit_generated_catalog.py` (`--check` verifies) |
| `make-candidate.py` | builds the three candidates (`--check` verifies they are current) |
| `rules-tests.json` | the host's `dotnet test` result of `Tests/Rules`, copied into every proposal |

The Hollowmere PlayMode test lives in the game: `games/hollowmere/Assets/Hollowmere/Tests/P2_4/PlayMode/`
(`Hollowmere.P2_4.PlayMode.Tests`, compiled only when the package is installed: `versionDefines` sets
`HOLLOWMERE_PRESSUREPLATE`).

## The mechanism

* Slots, owned by `hollowmere.pressureplate.owner` in domain `hollowmere.pressureplate.domain.plate` v1: `plate.pressed`
  (0/1) and `plate.weight` (actors on the plate). int32 only.
* Route `plate.press` (target = the plate; payload: actor `TargetId` + load `int32` 0/1), one bounded lane of 16 rows.
* Rules: stepping on adds 1 and stepping off removes 1; pressed while weight >= threshold. Refusals:
  `plate.not-loaded` (off an empty plate), `plate.overloaded` (on a full plate), `plate.unknown` (not a plate), and
  `plate.invalid-definition` (threshold < 1 or max < threshold).
* Events, payload `{plate, actor, weight}`: `PlatePressed` (released to pressed), `PlateReleased` (pressed to released)
  and `PlateWeightChanged` (accepted press without a flip). The message plane commits a request only together with an
  event, so an accepted press that does not flip the plate needs the third event.
* Every stable name starts with `hollowmere.pressureplate.` and is derived with `StableNameKeyDerivation.Derive`, so no
  identity can collide with the gameplay catalog's `gameplay.` names.
* No static mutable state: the per-world `PlateModule` is handed to the `[DisableAutoCreation]` system after boot.

## Public API

```csharp
// Hollowmere.Mechanism.PressurePlate
public static class PressurePlateMechanism
{
    public const string MountStepName = "mount-pressureplate";
    public static ICatalog Catalog();
    public static CatalogPluginDeclaration Declaration();
    public static GameApplicationDefinition Extend(GameApplicationDefinition baseDefinition);
    public static PressurePlateWorld Attach(GameApplicationRoot root);
}

public sealed class PressurePlateWorld
{
    public GameApplicationRoot Root { get; }
    public PlateModule Module { get; }
    public Id128 Issuer { get; }
    public string LastDetail { get; }
    public IReadOnlyList<PlateEvent> Events { get; }
    public bool Place(TargetId plate, ScopeId scope, int threshold, int maxWeight);
    public CommandAdmissionReceipt Press(TargetId plate, TargetId actor, bool load);
    public bool IsPressed(TargetId plate);
    public int Weight(TargetId plate);   // -1 when the target holds no plate slots
    public int Pressed(TargetId plate);  // -1 when the target holds no plate slots
    public int Poll(int maxEvents = 256);
    public int CountEvents(PlateEventKind kind, TargetId plate = default);
}

public static class CatalogSet
{
    public const string Format = "gamecore.catalog-set/1";
    public static string Combine(string worldFingerprintHex, IEnumerable<string> mechanismFingerprintHexes);
}

public sealed class CompositeCatalog : ICatalog
{
    public CompositeCatalog(ICatalog world, IReadOnlyList<ICatalog> mechanisms);
    public ContentHash Fingerprint { get; }      // ToHex() == CatalogSet.Combine(world, mechanisms)
    public ContentHash WorldFingerprint { get; }
}

public static class PressurePlateSmoke
{
    public const int Steps = 120;
    public static PressurePlateSmokeSession Begin();                                  // PlayerLoop on, started
    public static PressurePlateSmokeSession Begin(GameApplicationBootOptions options);
    public static GameApplicationDefinition Definition();
}

public sealed class PressurePlateSmokeSession : IDisposable
{
    public GameApplicationRoot Root { get; }
    public PressurePlateWorld Plates { get; }
    public void Step(int frame);
    public string SlotHash();
    public void Dispose();
}

// Hollowmere.Mechanism.PressurePlate.Generated
public static class PressurePlateCatalog
{
    public const string CatalogFingerprint = "...";
    public static CatalogBuildResult BuildCatalog();
}

// Hollowmere.Mechanism.PressurePlate.Rules
public static class PressurePlateRules
{
    public static PlatePressResult Press(PlateState state, bool load, PlateSpec spec);
    public static PlatePressResult Unknown();
    public static PlateState Initial();
}
```

`CatalogSet.Combine(world, mechanisms)` is the lowercase hex SHA-256 of the UTF-8 text
`"gamecore.catalog-set/1\n" + world + "\n" + string.Join("\n", mechanisms sorted ordinal)`. With no mechanism it
returns `world` unchanged.

`SlotHash()` is the lowercase hex SHA-256 of the UTF-8 text of the lines `target|owner|slot|value`, one per plate slot
of every plate, sorted ordinal and joined by `\n`. Ids are lowercase hex and values are invariant decimal.

### Composition in a game

```csharp
WorldBuildPlan plan = WorldBuilder.Build(manifest, catalog, fingerprint, null);
GameApplicationDefinition extended = PressurePlateMechanism.Extend(plan.Definition);
GameApplicationRoot root = GameApplication.Boot(extended, new GameApplicationBootOptions());
GameplayWorld world = WorldBuilder.Attach(root, plan);
PressurePlateWorld plates = PressurePlateMechanism.Attach(root);
root.Start();
plates.Place(plateTarget, village.Scope, threshold: 1, maxWeight: 2);
plates.Press(plateTarget, world.Focus, load: true);
```

`Extend` copies every public property of the base definition and adds the plate's contributions. Those are the
composite catalog with `CatalogHash` set to its fingerprint, the plugin, the command system, a wrapped dispatch-kind
resolver, the route and lane on a rebuilt message plane with the base limits, the payload reader, the recipe, and the
mount boot step at the base root scope.

## Regenerating

```sh
python3 samples/mechanisms/pressure-plate/make-catalog.py           # description + generated catalog
python3 samples/mechanisms/pressure-plate/make-catalog.py --check
python3 samples/mechanisms/pressure-plate/make-candidate.py         # the three candidates
python3 samples/mechanisms/pressure-plate/make-candidate.py --check
```

Change any package file and both the candidates and their sha256 values change, so re-run `make-candidate.py` and
commit the result.

## Candidates

| Candidate | Change set id | package.tgz sha256 | Expected verdict |
|---|---|---|---|
| `candidate/` | `cs_01K6RW0MECH00000000000000A` | `ab580b0e6c56d2b74596b9b3c16b69d7ed39c4b4c47d011568432ea0f69ed950` | pass |
| `candidate-forbidden/` | `cs_01K6RW0MECH00000000000000B` | `a3ca37f08a34b7398554ee92fd2f3ace5a4353b2e0b7281b7914ad1829caf125` | refused by the static scan |
| `candidate-failing-test/` | `cs_01K6RW0MECH00000000000000C` | `a067ef8464e0f23850dd989daf2f3593d8650e38385a29366256f5e398370acc` | failing test |

All three share `proposal.json` sha256 `ecda0696a036f51e3c2cabeae984129df5991ed84d7a7ec20aa09756708c8e94`.

## Host verification

Run on myubuntu (Unity 6000.0.75f1, .NET 8) on 2026-10-04/05, in the host clone `~/wkspace/gc-studio/p2.4-plate`
only. The package was copied to `games/hollowmere/Packages/com.hollowmere.mechanism.pressureplate` and added to
`testables`; the clone was reset afterwards.

```sh
studio/tools/sync-to-host.sh p2.4-plate
studio/tools/unity-compile.sh p2.4-plate games/hollowmere --tests EditMode --filter 'Hollowmere\.Mechanism\..*'
studio/tools/unity-compile.sh p2.4-plate games/hollowmere --tests PlayMode --filter 'Hollowmere\.P2_4\..*'
# throwaway csproj pair in /tmp/plate-dotnet (netstandard2.1 Rules/**/*.cs, net8.0 NUnit Tests/Rules/**/*.cs)
~/.dotnet/dotnet test -p:PlatePackage=<clone>/samples/mechanisms/pressure-plate/package
```

| Run | Result | Duration |
|---|---|---|
| dotnet `Tests/Rules` | 15 passed, 0 failed | 5 s (tests 11 ms) |
| EditMode `Hollowmere.Mechanism.*` | 26 passed, 0 failed (15 rules + 11 editor) | 616 s Editor run; smoke test 1.83 s |
| PlayMode `Hollowmere.P2_4.*` | 1 passed, 0 failed | 316 s Editor run; test 1.55 s |

The PlayMode test logged:

```
[W-MECH-01] plate placed in Thornwick Village; pressed after 1 frame(s), released after 1 frame(s); frames=2
sanctionedPumps=2 violations=0 catalogSet=1d45d1c96570bb2d1d8b2be003cc9fe791f54e97a067aa2962452e044011d9fb
world=425508a971072415b8f57e43894083a7f75396c1a47db4691b1a9c1b0e6ec899
plate=eabc05e00b2ad24fedbee8f283cd60e66782415e00865936919672ce1dab3f21
```

Unity wrote no file into the package directory: the file count and modification times were unchanged after both
runs. Unity did update `Packages/packages-lock.json` of the host clone, which is outside the package.
The first EditMode attempt failed on a missing `using` in `PressurePlateSmoke.cs`; that fix is committed.

## Open items

* **WorldBuilder extension seam.** `Extend` works on the built definition because `WorldBuilder` has no seam. A
  `WorldBuildOptions.Extensions` hook applied inside `Build` would let the world's own plan carry the mechanism.
* **Shared reader table.** `CommandPayloadReaders` cannot be enumerated or copied. `Extend` therefore binds the
  `plate.press` reader into the base definition's table, idempotently, and the extended definition shares it.
* **`CatalogOrdering` is internal** to `GameCore.Contracts`. `CompositeCatalog` repeats its comparison: registration
  key, then key version.
* **Checker path rule.** `tools/check_game_core_csharp.py` does not scan `samples/`. If it ever does, its
  "UnityEngine reference outside the Unity project" rule would flag the Unity-facing package files at this path.
  Installed under `games/hollowmere/Packages/` they pass every rule.
