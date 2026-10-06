# R5-C

Branch `codex/r5-c`, base `c9866292`, host myubuntu. This report lives inside the
exclusive stage test path; root `PACKET.md` is outside this packet's ownership.

## R2 fixes / R5 findings

| Finding | Fix | Regression |
|---|---|---|
| P4.2c #4, R2-11/13 boundary | Registered project's Git root owns pins, source revision and read-only package mount; the service checkout still owns tools. Preflight precedes queuing/candidate reads, reused slots check project/root before Unity, cache keys read the bound packages. Equivalent CLI source paths canonicalize. | Python `test_R5_04_*` (3 tests); Rust `r5_04_*` (5 tests), shared `package-root-cases.json`. The positive CLI case uses the unmodified pressure-plate candidate through the actual slot creator and scan, with no compiler/Unity step. |
| P4.2c #7 | Text2 selects Village instances plus indexed definitions; Narrative opens Marsh for Odd. Quest simulator resolves indexed OilFlask ID and requires a successful real-registry result. Refusals/exceptions fail. Required Play effects gate W-AI-02/03/05. | `DriverDryTests.R5_07_*`: selection, all three required gates, real index/simulator, explicit refusal/exception/Edit-time negatives, real boot observers, and unmodified retained dialogue/quest candidate replay. |

The shared root fixture reproduces two independently registered/tool checkouts,
relative/absolute valid pins, pins into the tools checkout or candidate storage,
and a symlink. Before the fix the Python tests had four assertion failures. The
C# regression XML reproduces the absent Play gates, misplaced Odd selection and
both retained `GP-QST-004` simulator errors.

The Play driver saves/bakes the actual applied content, rebinds the game after New
Game, and records committed state/presentation in `<tag>/play-effect.json`. Only
observed effects write `pass`. NPC identity comes from the indexed authored-ID
set difference, not an assumed worker name. Navigation needs a real active
NavMesh view. Dialogue requires a newly added node and compares unlit/lit
presentations. Quest stage 1 must hold after one oil flask and advance to 2 after
the second. Missing/unapplied candidates, headless navigation, timeouts and
exceptions are failures. Original prompts and witness candidates are unchanged.

## Verification

- Stage Python: **22 passed** (`python3 -m unittest discover -s studio/stage/tests -v`).
- Final ordinary Rust: **142 passed, 0 failed, 11 ignored**, including **5 root tests**.
  `cargo fmt --check`, `cargo clippy --all-targets -- -D warnings`, `cargo test`;
  retained transcript `evidence/r5-rust.txt`, focused root transcript `evidence/r5-root.txt`.
- Metadata: **42 packages / 91 assemblies**, pass. C# policy: **1,209 files**, pass.
- Initial Unity invocation: startup silence timeout after 601 s, no XML, NotRun.
  Retry XML: **8 passed / 7 failed** (six requested regressions plus the newly
  exposed headless-navigation prerequisite). Intermediate driver failures are
  retained under `evidence/`, never counted as passes.
- The unmodified retained dialogue and quest candidates both applied through the
  actual edit engine and passed their real Play observations in
  `evidence/r5-driver-replay-diagnostic.xml`. The added line was absent unlit and
  displayed lit; oil identity `bc9e0772-4981-4a2e-8e1b-03be5c839d18`; quest stages
  after one/two flasks: **1 / 2**. Both assets were restored byte-for-byte.
- Final Hollowmere dry EditMode XML: **16 passed, 0 failed, 0 skipped, 0 inconclusive**,
  Unity exit 0, 111 s wrapper wall time (`evidence/r5-driver-green.xml`). Both real
  Play tests passed, including the untouched candidate replay. The intermediate
  null-reference failure was in a test iterator's compiler-generated captured
  local object crossing EnterPlayMode reload; ordinary helper assertions remove
  that captured state. No product refusal was suppressed.
- Final evidence hashes/counts: `evidence/summary.json`. No paid operation,
  installed companion restart or node restart was performed.

## Requests to other packets

No code change outside this packet is needed for these fixes. P4.2c #1/#2/#3
(NPC inverse, history final stamps, Play admission catalog) remain with their
assigned owners; this packet does not modify those implementations.

## Left open

- Full W-AI-02 graphical navigation acceptance: the mandated dry command includes
  `-nographics`, and `PrefabViewBinder` intentionally disables views in that mode
  (`Packages/com.gamecore.gameplay.entities/Runtime/EntityBinders.cs:142`). The dry
  test requires the navigation check to refuse; it does not qualify navigation.
- Full paid creator workflows were not rerun. The retained, untouched W-AI-03/05
  candidates establish offline engine/Play effects, not a new provider/task/UI
  acceptance run. Historical matrix rows remain owned by the acceptance packet.
- Intermittent pre-dispatch startup delays remain unexplained. Redacted local
  logs and native wait stacks are under `.unity-logs/r5-c/`; no timeout was waived.
