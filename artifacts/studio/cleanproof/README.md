# Saltmarsh clean-project proof — W-CLEAN-01 / W-CLEAN-02

PASS on Linux host **myubuntu** (hostname `worlesenric`), 2026-10-05.
Delivery source: `74481e6d864cb1785cc1d5efc34b11f8a1a138e4`. Qualified gameplay/build source:
`678b9200f7342300795679b7b3508889dc3b0615`. Base `origin/main`: `8b954014ac084f7f6787382083ffadbdc1044a2a`.
The evidence-only commit follows the delivery source. No runtime or baked-game asset changed
between the qualified source and delivery; `verify_evidence.py` checks that diff as well.

Saltmarsh is independent of Hollowmere: Harbour / Dunes / Lighthouse, Ada / Neri / Sol,
and the three-stage **Relight the Coast** quest. Its glass/spare-lens choice changes the ending;
restoring the beacon protects the harbour. Four items, package HUD/dialogue/journal/inventory/
menu/pause/settings/save/load/ending, three ambiences and one music state are wired through
real GameBoot. All media is labeled procedural placeholder content; no paid operations ran.

## Results and timings

| Gate | Result | Evidence | Host elapsed |
|---|---|---|---|
| Cold project setup, final corrected run | PASS, Unity resolved the new lock | [setup log](setup-final/setup-final-20261005T153408-837883-a1.log) | 84 s |
| Studio authoring + bake + Entry.Verify | PASS | [author log](author-additive/author-additive-20261005T153930-860957-a1.log), [journal](../../../games/cleanproof/Studio/History/2026/10/) | 44 s |
| EditMode | **3/3 passed**, zero skipped/inconclusive | [result XML](editmode-final/results.xml) | 47 s; suite 2.341 s |
| PlayMode | **3/3 passed**, zero skipped/inconclusive | [result XML](playmode-final/results.xml) | 51 s; suite 2.473 s |
| Scaffold regression | **1/1 passed** | [log](scaffold-tests.log) | 3.108 s |
| Metadata + self-tests | PASS; 41 packages, 89 assemblies, 3 locks; **31 self-tests** | [metadata](package-metadata.log), [self-tests](package-self-tests.log) | static checks |
| C# policy check | PASS, **1,109 files** | [log](csharp-check.log) | static check |
| Linux x86-64 IL2CPP build | PASS, Unity exit 0 | [build log](build/linux-player-20261005T154648-890154-a1.log) | **1,053 s** (BuildPipeline 837.507 s) |
| Standalone headless autoplay | PASS, **600 frames**, zero pump violations, **exit 0** | [player log](player.log), [process result](player-result.json) | **21.146 s** |

The three EditMode cases prove all twelve groups are discoverable, AuthorAll leaves every
asset byte unchanged on repeat invocation while journaling each phase, and Entry.Verify
rejects a deliberately modified bake report. PlayMode boots the actual scene/GameBoot,
drives both quest branches with typed commands, compares capture and restored slot hashes,
continues the quest after restore, checks the branch-specific ending and exactly one reward,
and runs Harbour → Dunes → Lighthouse → Harbour twice with stable NPC identity.

The standalone ran the same quest/save scenario and then idled to frame 600. Its logged
571 pumps are the **restored root's** counter (restore replaces the root); per-frame counter
deltas are asserted over the scenario's 20-frame sample and the remaining idle frames.

## Build identity

The host-local distributable is `games/cleanproof/Builds/Linux/`, **31 files /
204,393,508 bytes**. Unity's two explicitly “DoNotShip” backup/debug directories remain
on the host and are excluded from the shipping manifest. Large binaries are not committed.

- Whole shipping-tree checksum: SHA-256 of [build-files.sha256](build-files.sha256):
  `1ced85ce5df18927fc5ee1951183a9dca739d8446795b3ebe51b6ac042f27464`.
- Launcher `Saltmarsh.x86_64` SHA-256: `aeaf13e291886fbd5a99b7dbd7c113b8ee13ed462a419a4d31a8ecbc3241ac70`.
- Every runtime library and data file, including `GameAssembly.so`, has its individual digest
  in that manifest. [proof.json](proof.json) records machine-readable checks and revisions.

## Empty package diff

At delivery source `74481e6d864cb1785cc1d5efc34b11f8a1a138e4`:

```text
$ git diff --stat origin/main -- Packages/
(no output)
```

The retained [raw diff file](package-diff.txt) is empty. This covers kernel, gameplay and Studio
packages. The final evidence commit changes only packet/matrix evidence and artifacts;
the same command is checked again after committing.

## Authoring and reproduction

[Saltmarsh instructions](../../../games/cleanproof/README.md) give the exact host commands.
`new-project.sh` supplies a content-free template, pinned file references, Boot/build settings,
URP/Input/AI Navigation defaults and link.xml. It preserves existing files and does not copy
Hollowmere content, its lock, or a Library. Unity re-resolved the new lock here.

`SaltmarshAuthoring.AuthorAll` submits five registered `saltmarsh.author` composition phases
through `StudioRuntime.Engine.Apply`. Each journal detail lists the created asset paths.
There are **25 Applied** phase records (initial successful authoring plus idempotence passes)
and **one retained Interrupted** record from the stopped bootstrap attempt before any world
files existed. The latter is not counted as a pass or silently rewritten.

```bash
bash games/cleanproof/Tools/build.sh
bash games/cleanproof/Tools/run-headless.sh
python3 games/cleanproof/Tools/verify_evidence.py
```

The verifier checks the exact six XML case identities, player exit/frames/pump violations,
qualified-runtime equality and the empty package diff, and records the shipping-tree digest.

## Reusability findings

| ID | What required a workaround | What a plugin/project developer needs |
|---|---|---|
| CP-01 | Production checkpoint codecs were only supplied by Hollowmere's test-only binding (`Tests/P1_5/Checkpoint/Hollowmere.P1_5.Checkpoint.asmdef:14`). Saltmarsh owns a production copy of the content-independent wire serializers/binding. | A production `GameplayCheckpointCodecs.TryBuild(out CheckpointCodecSet?, out string)` factory; exact request in the packet. |
| CP-02 | Studio's object composer does not create scene assets (`Packages/com.gamecore.studio.core/Editor/Tools/BuiltIn/ComposeTools.cs:200`). The game supplies registered bootstrap composition operations. | A journaled `scene.create(path)` with replay identity/rollback and an explicit destination-scene seam. This proof does not claim a zero-code bootstrap workflow. |
| CP-03 | Bake writes generated C# without importing it when refresh is false (`Packages/com.gamecore.gameplay.compile/Editor/Entry.cs:182`). | Separate author/bake and test/build Editor invocations; preserve the generated catalog in link.xml. |
| CP-04 | The compiler emits mutable array-backed static registration metadata (`CatalogEmitter.cs:302`, `:371`, `:407`, `:473`, `:606`). | Emitter-owned immutable tables/fresh accessors. Saltmarsh never mutates the tables and retains byte-exact bake output; no authoritative game state is static. |
| CP-05 | The pinned Linux player selected a null backend in a tty shell and crashed before GameBoot. | Explicit `DISPLAY=:1 XDG_SESSION_TYPE=x11` in the run wrapper, retaining `-batchmode -nographics`. Product audio stays enabled. |

Exact requested files/signatures and remaining limits are in the
[P4.1 packet](../../../docs/studio/packets/P4.1-clean-proof.md).

## Retained failed attempts

- Setup: one package-resolution timeout, then a compile refusal for the wrong Studio assembly
  name; the corrected assembly name compiled. The following compile exposed the test's
  unnecessary direct Newtonsoft use, replaced with StudioJson.
- First author invocation: stopped through its wrapper during scene replacement; [stack](author/main-thread.txt)
  and interrupted journal retained. Keeping Boot and region scenes additive during Apply fixed it.
- First EditMode XML: the three authoring tests passed, but a misconfigured Editor-only PlayMode
  asmdef made its three scene-loading tests run in EditMode and fail. [Original XML](editmode/results.xml)
  is retained; the corrected assemblies each pass their proper platform.
- [Initial player run](player-initial/player-result.json): native SIGSEGV, exit signal -11,
  before GameBoot; [native log](player-initial/player.log). The same build passed with the
  explicit X11 backend. No failed attempt is represented as a passing result.
