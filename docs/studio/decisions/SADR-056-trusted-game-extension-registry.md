# SADR-056: Reviewed game-owned extension registry

- Status: implementation decision; not an acceptance claim for W-DOC-02.
- Scope: Hollowmere's trusted Editor composition and live admission smoke. Candidate packages remain subject to signed Stage and explicit creator Admit.
- Related: SADR-004 (declared slot state), SADR-010 (one application root/pump), SADR-023 (authentic verdicts/internal admission), SADR-032 (restore rebinding), SADR-050 (game-owned admission binding), SADR-052 (Editor-only Studio adapters).

## Context

R8-B demonstrated that an independently authored lever could not pass live admission: the trusted dispatcher recognized only the pressure-plate smoke identity. Passing a sandbox stage could not legitimately add trusted live authority. Accepting arbitrary candidate-named callbacks, discovering candidate registrations or borrowing the pressure-plate identity would erase the trust boundary rather than solve the missing game integration.

A further restore constraint matters: the pre-Admit checkpoint contains the old topology. Restoring it does not execute the new application's boot steps, so a new mechanism's target and plugin mount are initially absent even when its runtime extension is composed. Seeding old slot rows during `Attach(seedSlots:false)` would destroy the checkpoint continuity being proved.

`LiveTargetSeeder.TrySeed`, used by application boot seed steps and trusted new-target creation, creates an empty slot buffer without invoking the recipe applier. Initialization cannot rely on a recipe's spawn-layout callback for this path. The reviewed `public bool InitializeNewTarget()` explicitly initializes missing state to zero after target creation and preserves existing `0/1` state. Fresh `Attach(true)` and sandbox startup call it after root attachment; restored `Attach(false)` does not. The trusted live adapter calls it only after adding the missing target/plugin topology.

The admission catalog path uses `Entry.Verify`, not a write/rebake of the checked-in generated gameplay catalog. A new `IGameplayCatalogContributor` would therefore change the staged base-world fingerprint without changing the generated base catalog loaded at runtime. The regular contributor seam remains appropriate for packages delivered with a regular rebake, but is not sufficient for this admission flow.

## Decision

1. **Only reviewed game source declares live entries.** `HollowmereExtensionRegistry` in the trusted Hollowmere Editor assembly creates an immutable per-instance entry list. An entry fixes the smoke type/method, package, runtime assembly, world-extension type and minimum step requirement. Exact ordinal lookup retains the existing pressure-plate identity and adds `com.hollowmere.mechanism.lever` / `Hollowmere.Mechanism.Lever.LeverSmoke.Begin`. There is no candidate registration method, initialization hook, callback discovery or mutable process-wide registry.
2. **The proposal selects identity, not executable live code.** `RunAdmittedSmokeEntry(StageVerdict,string,string,int)` keeps its existing signature. Authenticated admission still verifies the signed complete Docker verdict, package/proposal digests and owner scope. The live dispatcher selects a game-owned adapter; it never invokes the proposal's sandbox `Begin` method. The maintained pressure-plate path remains available under its original identity.
3. **Use normal world extensions with reviewed catalog composition.** The game adds only the reviewed `Hollowmere.Mechanism.Lever.LeverWorldExtension` from assembly `Hollowmere.Mechanism.Lever`, through `WorldBuildOptions.Extensions`. The resolver checks the exact type contract and that its asmdef resides in the installed project package with matching `package.json` identity. The candidate implements `IGameplayWorldExtension` and `IGameplayWorldTargets`. The trusted composition seam explicitly composes the existing world catalog with `Hollowmere.Mechanism.Lever.Generated.LeverCatalog` from assembly `Hollowmere.Mechanism.Lever.Generated`, retaining the real combined `CatalogSet` fingerprint and ordinary world build/restore plan. Candidate Editor contributor discovery is omitted: no generated base-world rebake is performed by admission. The combined catalog must never be disguised as the baseline fingerprint.
4. **Observe committed effects, not candidate assertions.** The reviewed lever API is a public parameterless constructor, `public int State { get; }`, `public bool Toggle()` and `public bool InitializeNewTarget()`. State reads the committed int32 slot (`0/1`, or `-1` while absent); Toggle only submits one bounded command and returns ingress admission. Before adding topology, the restored world must complete an equal save roundtrip; the walkthrough independently checks the captured nine-OldCoin inventory state. Initial and final full-world roundtrip hashes are not required to equal each other across advancing game/NPC frames. The adapter then adds only the missing declared target/plugin through ordinary composition, explicitly initializes missing lever state, and observes committed off/on/off states on subsequent normal frames. Existing state is not overwritten; `Attach(false)` never resets restored slots. No live smoke boots another world or calls another pump. After smoke, the walkthrough exercises the attached runtime button and captures the actual playing world in both committed positions before normal admission undo.
5. **Authoring files are not live imports.** The maintained recipe lives under `Assets/Hollowmere/Mechanisms/Lever~/`; Unity ignores the tilde directory. Its generator emits the content-addressed candidate outside live Assets/Packages. Candidate code reaches `<project>/Packages/com.hollowmere.mechanism.lever` only through the signed Stage plus explicit creator Admit workflow. Admission undo uses the ordinary journal/recovery path, not direct file removal.

## Consequences and review obligations

- New mechanisms require a reviewed trusted-game source change and a bounded game-owned adapter before their separate candidate admission. A registry edit, a local unsigned stage verdict or passing the old dispatch probe cannot grant admission authority.
- The candidate and trusted game agree on a small explicit runtime interface rather than a runtime discovery convention. Wrong assembly/package ownership, unknown identities and incompatible signatures fail closed.
- Candidate pure rules, declared slots/routes, generated catalog, real package-content hash and committed presentation are still required. A registry entry cannot excuse a fake mechanism or renamed sample.
- Graphics, signed seven-step Stage, explicit creator Admit, reload/restore continuity, normal pump observations and admission undo require retained evidence from the delivered revision. This decision does not declare that verification complete.
- Player distribution/stripping remains its own qualification obligation; this change does not turn Editor admission into arbitrary runtime player package loading.

## Implementation references

- [Trusted registry](../../../games/hollowmere/Assets/Hollowmere/Authoring/Editor/HollowmereExtensionRegistry.cs)
- [Lever authoring recipe](../../../games/hollowmere/Assets/Hollowmere/Mechanisms/Lever~/)
- [Literal plugin guide walkthrough](../09-plugin-developer-guide.md#new-lever-author-stage-admit-observe-and-undo)
- [R8-B driver, extended with the signed walkthrough](../../../games/hollowmere/Assets/Hollowmere/Tests/R8_B/Lever/run-probe.py)
- [R8-C signed Stage, restored working lever and normal Undo evidence](../../../artifacts/studio/verification/W-DOC-02/r8-c/README.md): final literal workflow PASS on `09430b30`; separate broad-suite limitations remain explicit.
