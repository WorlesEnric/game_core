# GameCore Studio — design

**Status: DRAFT, paused 2026-09-28. Not normative.** Code citations are repo-prefixed: `game_core/...` is this
repository at HEAD `0523bea`, and `etos/...` is the etos Mac checkout at **`e99870d`**. Every etos fact must be
re-verified after the etos SDK refactor ([open-problems.md §B](open-problems.md#b-etos-facts-to-re-verify-after-the-sdk-refactor)).
Numbers are marked **MEASURED** (with source), **ESTIMATE** or **UNMEASURED**.

Contents:
1. [Vision and decisions](#1-vision-and-decisions)
2. [Code blocks as assets: the answer](#2-code-blocks-as-assets-the-answer)
3. [Architecture](#3-architecture)
4. [Code blocks](#4-code-blocks)
5. [Type-system extension](#5-type-system-extension)
6. [Player profiles extension](#6-player-profiles-extension)
7. [Transaction agents](#7-transaction-agents)
8. [RG bridge](#8-rg-bridge)
9. [Kernel changes (proposed ADRs)](#9-kernel-changes-proposed-adrs)
10. [Edit loops and latency](#10-edit-loops-and-latency)
11. [First slice](#11-first-slice)
12. [Roadmap](#12-roadmap)
13. [Risks](#13-risks)

---

## 1. Vision and decisions

**Vision.** GameCore is AI-driven. Developing a game means developing extensions and wiring them in GameCore's
extension tree. A bridge publishes the tree and its interfaces to the etos Resource Graph (RG). Specialist etos
**transaction agents** produce the work: mechanism code, narrative, 2D, 3D and tuning. A human designer works in
the **Studio** and plays the game in the player view while designing it. In-window tools let the designer place
assets, change the narrative, adjust mechanisms and formulas, check curves, and switch between player profiles.
Game development becomes a game.

**Binding decisions:**

| Id | Decision |
|---|---|
| D1 | The Studio runs on `myubuntu`. The owner works from a Mac. |
| D2 | There are two speeds. A player-window tool never waits on an LLM, and agent work arrives asynchronously. |
| D3 | Kernel changes and V1.x requalification are accepted. **Code blocks (E/C/S) are assets:** they can be mounted, unmounted, replaced or updated whenever the interface matches. Interfaces are published to the RG, and a "type system" extension can be mounted near the root. Rebuild latency is acceptable. Mechanisms are agent-authored. |
| D4 | The designer creates **multiple virtual player profiles** and switches between them while playing. This is an extension. |
| D5 | The etos **core stays unchanged**. Every agent is an etos transaction agent: one worker per task, and subagents are allowed. Mechanism coding is also a transaction agent. |
| D6 | The design builds on **etos v2**, the Mac checkout at `e99870d`. `myubuntu:~/wkspace/etos` is a different, unrelated codebase (`5485c0c`, an aliyun remote) and is never used. |
| D7 | Transaction-agent code is **maintained in game_core** and uses etos only through its binaries, SDKs and contracts. It never uses etos source. [§7.1](#71-packaging-in-game_core) confirms this is feasible. |
| D8 | Each transaction agent produces its own artifacts and delivers them to a target directory or object store. [§7.5](#75-artifact-pipeline) confirms the substance of this, and corrects the mechanism. |

**The three speeds:**

| Speed | What | Latency | LLM in path |
|---|---|---|---|
| Fast | Designer gestures: play, switch profile, tune, place, mount or unmount a compiled block | Milliseconds to about 1 s | Never |
| Medium | Agent data proposals: tuning, narrative content, assets | Seconds to minutes, asynchronous | Yes, off the play path |
| Slow | New or changed code blocks: agent work, staging, then Apply with a rebuild and restore | Agent minutes to hours, then Apply in 60 s or less (**ESTIMATE**) | Yes, off the play path |

---

## 2. Code blocks as assets: the answer

**Yes, code blocks are assets.** A code block is content-addressed, placed in the extension tree, versioned by its
interface and published to the RG. A transaction agent writes it. You can mount, unmount, replace or update it
whenever the interface matches. For code, "importing the asset" means compiling it. Unity already treats a `.cs`
file as an asset whose import compiles it and reloads the scripting domain. What GameCore adds is the rule for
which state carries across that import.

**This is consistent with the kernel's trust model.** P-001 allows "trusted, precompiled plugins". Agent-written
source that the designer approves and compiles into the build is still precompiled. P-009 ("mounting does not load
new code") also still holds: mounting selects from what is compiled, and an update is a new build plus a restored
world, not hot replacement.

**There are three speeds of change:**

1. **Mounting or unmounting a block that is already compiled in is live, with no rebuild.** V1 already builds the
   ownership/schedule pipeline from *every* declared plugin manifest, not only the mounted ones
   (`game_core/unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/Gc013CardsHost.cs:384-393`). A mount
   therefore only adds bindings, config and state (O-03, O-07).
2. **Swapping block A for block B with compatible interfaces is one atomic edit.** State moves across through a
   Transfer mapping, and the swap is live when both blocks are compiled in. This needs ADR-024.
3. **New or changed code means compile, one domain reload, then a new world restored from a checkpoint.** The
   restored world has the same game state, logical step, time debt and domain time. Only the `WorldId` is new.

**Two boundaries:**

- **B1: a block's tree position scopes its data, not its code.** A compiled block exists once per build, and its
  systems run over the whole world: V1 creates one system instance per world
  (`game_core/Packages/com.gamecore.unity.runtime/Runtime/UnityWorldRegistration.cs:59-62`). Mounting decides
  where the block has bindings, config and state. Replacing block X's implementation replaces it at every scope,
  in every profile and in every playthrough. If one subtree needs different behaviour, that is a separate block
  with its own `blockId` and schema ids. *(The owner has not yet accepted B1; see open-problems A1.)*
- **B2: a type system near the root governs everything below it** through generation, compile-time analysis and
  mount-time conformance ([§5](#5-type-system-extension)). Re-typing its descendants requires a rebuild.

**Hard constraints.** These are engine facts, and no kernel work removes them:

| Id | Constraint |
|---|---|
| H1 | IL2CPP is ahead-of-time, so a player can never load new managed code. A changed block that reaches a player needs a rebuild, a restart and a restore. |
| H2 | The Mono Editor cannot unload assemblies, and every script compile reloads the whole domain. That destroys **every** World in the Editor, including every profile's world. Continuity comes only from serialized checkpoints. |
| H3 | The Entities TypeManager fixes component types once per domain. Persisted state must be keyed by schema id, never by `TypeIndex`. GameCore already keys it that way. |
| H4 | The ECS half of a block compiles only through Unity's pipeline (source generators, IL post-processing, Burst). Plain dotnet checks only the Unity-free Rules half, so agents need a Unity **staging service** on the host. |
| H5 | Compile-time rules bind only the assemblies that reference them. Rules scoped to part of the tree are enforced when a block is mounted. |
| H6 | Whether two implementations behave the same is undecidable. "The interface matches" is a structural check; behaviour needs tests and replay oracles. |
| H7 | There is no in-process sandbox. The mitigation is separate OS users and processes. |
| H8 | Unity allows one Editor per project directory, so every staging slot needs its own project folder and `Library`. |

**Policies changed by ADR.** These are GameCore rules, not engine limits:

- **P-001, P-009 and ADR-014.** Agent source compiled into the build counts as precompiled, and the catalog becomes
  a generated output of the build's set of blocks (ADR-018).
- **04 §9: "Editor compilation/reload terminates the running session".** See
  `game_core/docs/game-core/04-unity-integration.md:195`. The same paragraph already allows "a deliberate saved
  checkpoint can start a new session", and ADR-031 automates that.
- **Restore refuses a catalog-fingerprint mismatch.** See `RequireCatalogMatch`,
  `game_core/Packages/com.gamecore.unity.runtime/Runtime/Pure/Persistence/CheckpointRestorePlan.cs:257-265`.
  A per-block interface-compatibility rule replaces it (ADR-020).

**The real work is keeping state across a rebuild, not loading code.** Every item below was verified in code:

1. **Checkpoints capture only the int32 slot rows** (`TargetSlotState`,
   `game_core/Packages/com.gamecore.unity.runtime/Runtime/Persistence/UnityCommittedBoundaryReader.cs:225-245`).
   A family's private components, such as the cards table, hands and scores, are lost on every rebuild. → ADR-021
2. **A restored world restarts at step 0 with zero time debt**
   (`game_core/Packages/com.gamecore.unity.runtime/Runtime/WorldHost.cs:184,511-512`). → ADR-022
3. **A changed spawn-recipe revision makes every existing target that uses it unrestorable**
   (`game_core/Packages/com.gamecore.unity.runtime/Runtime/Assembly/SpawnRecipeCatalog.cs:167-180`). → ADR-020
4. **Checkpoint migrations are planned but not executable.** Both restore builders refuse plans with migrations,
   and migrations are forward-only
   (`game_core/Packages/com.gamecore.contracts/Runtime/Serialization/CheckpointMigrationPlan.cs:505-512`). → ADR-023
5. **Families keep static or host-wired per-world state** (for example CardTableModule,
   `game_core/Packages/com.gamecore.gameplay.cards/Runtime/CardTableSystems.cs:34-38,98-139`). Generated lifecycle
   hooks are needed. → ADR-018
6. **A world can refuse an edit its composition lane already published.** It then can neither take new edits nor be
   checkpointed (`game_core/Packages/com.gamecore.unity.runtime/Runtime/Integration/WorldCompositionBridge.cs:368-390`;
   `.../Persistence/UnityCommittedBoundaryReader.cs:97-106`). → ADR-025
7. **Unapproved agent code must never run as the designer or as the etos daemon.** → [§7.9](#79-security-and-trust)

**Not recommended:** patching method bodies in a running process. It cannot handle source-generated ECS code or
Burst, and with rebuild latency accepted it is not needed.

---

## 3. Architecture

### 3.1 Machines and viewing

- **myubuntu is the whole Studio (D1).** Verified facts: Ubuntu 24.04, NVIDIA RTX 4060 Ti (8 GB), X display `:1`
  active with a seat0 session, `x11vnc` and `gnome-remote-desktop` installed, 20 cores, 31 GB RAM, Unity
  6000.0.75f1 (Linux and Mac standalone support), .NET SDK 8.0.425, Docker 29.6.2, and 218 GB free on `/home`. The
  host is **shared**: k3s, helix and Postgres run continuously.
- **The Mac only views it.** The Mac↔myubuntu round trip is **MEASURED ~250 ms** (ping, 2026-09-28). That sets a
  floor of about 250 ms input lag on any transport: fine for turn-based games, poor for action games.
  - Now: macOS Screen Sharing through `ssh -L 5900:localhost:5900 myubuntu` to the existing x11vnc.
  - Later: Sunshine on myubuntu (NVENC capture of `:1`) and Moonlight on the Mac (not installed).
  - Escape hatch for action games: a frozen Mono macOS player cross-built on myubuntu, with no live tools.

### 3.2 OS identities on myubuntu (new; configuration only; needs owner approval)

| User | Runs | Must never |
|---|---|---|
| `worlesenric` (existing, uid 1000; in the sudo and docker groups, so root-equivalent) | X session `:1`, the interactive Studio Editor, the IL2CPP publish lane | Run code the designer has not Applied |
| `etos` (new system user, in the docker group) | `etosd` (root `/var/lib/etos`), `studio-gateway` | Execute agent code. It holds the app keys, the host socket, the worker private keys and the broker CA (`etos/crates/etnode/src/keys.rs:4-6`). |
| `gcstage` (new, no supplementary groups) | All unapproved agent code: dotnet build/test of the Rules half, and batchmode Unity L3–L5 | Have network or read `/var/lib/etos`, the Studio working copy or the X session |

`gcstage` jobs run as systemd template units `gc-stage@<job>.service` with `User=gcstage`, `PrivateNetwork=yes`
(subject to the Unity licence check, open-problems A3), `ProtectHome=yes`, `ProtectSystem=strict` and
`ReadWritePaths=/srv/gcstage/slot-N`. A polkit rule lets the `etos` user start only `gc-stage@*`. As optional
hardening (R5), the Studio can run as an unprivileged `studio` user with X access through
`xhost +SI:localuser:studio`.

### 3.3 Repositories and layout

**game_core (this repository)** gains:

```
game_core/
  Packages/
    com.gamecore.blocks/            # gamecore.block/1 schema, Unity-free `gcblock` CLI (generator, interface
                                    # extractor, compatibility classifier, canonical hashing, deterministic .meta
                                    # writer), declarative type-system template support
    com.gamecore.blocks.analyzers/  # Roslyn DLL, diagnostics only
    com.gamecore.profiles/          # the player-profiles extension (§6)
  templates/typesystem/             # starter type-system block
  studio/                           # everything Studio-side; outside Packages/, unity/, dotnet/, tests/,
                                    # so no existing gate scans it (D7, §7.1)
    etos/                           # etos.lock, etapi.schema.json, build-etos.sh
    node/                           # etos.toml.tmpl, models.toml.tmpl, ops.toml.tmpl, etosd.service
    images/                         # gc-base, gc-mech, gc-content, gc-art, gc-art3d Dockerfiles
    agents/<agent>/                 # app.toml, agent.toml, instructions.md, actors/, tests/
    tools/                          # install-agents.sh, uninstall-agent.sh, etos-contract-check.sh
    gateway/                        # studio-gateway (.NET 8)
    editor/                         # com.gamestudio.editor: in-window tools, Reload Controller, Admission,
                                    # Agent Desk UI (a UPM package the per-game Studio project references)
    systemd/                        # gc-stage@.service, polkit rule, unit files
```

The qualification project `game_core/unity/GameCore.Validation` is never opened by the Studio. Studio projects
live outside `game_core/unity/`, because `game_core/tools/check_gate_sources.py:113-115` and
`game_core/tools/make_unity_metas.py:114` scan every `unity/*` project.

**Per game** (outside game_core):

- The canonical bare repo is `/var/lib/gcgit/<game>.git`. It is owned by `etos` and served only by the gateway's
  git smart-HTTP.
- The Studio working copy is `~/wkspace/games/<game>/`:
  - `design/tree.json`: scopes, installs, configs, type-system and profile installs, and a monotonic `designRevision`
  - `Packages/com.<game>.block.*`
  - `content/`
  - `assets/`: binaries plus a sha256 manifest
  - `profiles/`
  - `studio/`: the Unity project. Its `Packages/manifest.json` is generated from the Admitted block set.
  - `playthroughs/`
- Staging slots are `/srv/gcstage/slot-N`. Each slot has its own project directory and `Library` (about 5 GB each),
  so imports stay incremental. For each job the gateway exports the candidate commit into a slot with
  `git archive`. Staging never shares package folders with the Studio.

**etos** is built once from `e99870d` into `/opt/etos`. This is configuration only: game_core has no source
dependency on etos ([§7.1](#71-packaging-in-game_core)).

### 3.4 Processes

**A. Studio Editor** (`worlesenric`, `DISPLAY=:1`, project `~/wkspace/games/<game>/studio/`)

- The Game view is the player view. UI Toolkit overlays hold the tools: place asset, narrative, mechanism,
  formulas, curves, profiles and the Agent Desk. The tools use runtime UI, never `EditorWindow`/`SceneView`, so
  they survive into a development player later.
- StudioHost is a long-running host for several worlds. Its catalog hash is the real `buildId`, replacing
  `ContentHash.Empty` in `game_core/Packages/com.gamecore.unity.adapters/Runtime/PlayerLoop/GameCoreApplicationBootstrap.cs:81-87`.
- The Editor also runs the Reload Controller ([§4.11](#411-rebuild-and-restore-flow-tier-2)) and Admission
  ([§4.6](#46-tiers)).
- It talks only to the gateway, on `127.0.0.1:7420`, with a Studio token.
- **Editor settings contract (ADR-031):**
  - Auto Refresh is off.
  - Script Changes While Playing is set to *Recompile After Finished Playing*.
  - Enter Play Mode Options: reload the scene without reloading the domain. This makes the SubsystemRegistration
    resets of 04 §9 and the ban on static mutable state mandatory.
  - Burst compiles synchronously.
  - Every merge and `gcblock gen` runs inside `AssetDatabase.StartAssetEditing` (or `DisallowAutoRefresh`) and
    ends in exactly one Refresh. Nothing else writes into `studio/` or `Packages/`.

**B. studio-gateway** (.NET 8, user `etos`, systemd `After=etosd`)

The gateway hosts six parts:
1. The **Agent Desk backend**: a durable request ledger with a deadline and an attempt counter per request. It
   uses `etos task show` and `etos task cancel` through the pinned CLI.
2. The **RG bridge** ([§8](#8-rg-bridge)).
3. The **staging orchestrator**. It runs L0–L1 itself, over data only, and starts `gc-stage@` units for L2–L5.
4. The **artifact inbox and publisher** ([§7.5](#75-artifact-pipeline)).
5. **Git smart-HTTP** (`git http-backend`) with per-identity ref rules. A worker may push only
   `refs/heads/agent/<worker>/**`, and the Studio only `refs/heads/studio/**`. Large or binary blobs are refused on
   agent refs.
6. The **etapi client** ([§7.1](#71-packaging-in-game_core)).

It listens on `127.0.0.1:7420` for the Studio and on `172.17.0.1:7421` for containers (git, stage, inbox).

**C. etosd** (user `etos`, root `/var/lib/etos`, systemd `After=docker.service`, because the broker binds
`172.17.0.1`, which exists only once docker0 is up). Key `etos.toml` settings (game_core owns the whole file):

- `[node] local = true`, `owner = <owner>`, runtime docker. Do **not** use `etosd init --profile local`, which sets
  `runtime=none` (`etos/docs/operator.md:47-57`).
- `[broker]` listens on `172.17.0.1:<port>`. `host.docker.internal` does not resolve on Linux.
- `[api] listen = 127.0.0.1:7410`.
- `[limits] subagent_containers` is raised to fit RAM.

`models.toml` names the coding and content models. `ops.toml` configures `generate.image`, `generate.3d` and
`describe`.

**D. `gc-stage@<job>` units** (`gcstage`). Batchmode Unity with `-burst-force-sync-compilation` in a slot, plus
`dotnet test`. A watchdog fires after 10 minutes without log progress (the batchmode Editor hang,
`game_core/docs/operator/editor-hang.md`).

**E. Publish lane** (`worlesenric`). IL2CPP builds in disposable worktrees, under `nice` and a CPU quota.

**F. Viewer and supervisor.** x11vnc (later Sunshine), plus a supervisor that watches `Editor.log` progress and
runs restart-and-restore.

### 3.5 Data flows

```mermaid
flowchart LR
  Mac["Mac: VNC / Moonlight"] -->|input, ~250 ms RTT| Studio["Studio Editor on :1<br/>Game view + tools"]
  Studio <-->|127.0.0.1:7420| GW["studio-gateway<br/>(Desk, RG bridge, staging,<br/>inbox/publish, git)"]
  GW -->|etapi 127.0.0.1:7410<br/>entrances, logger, query| ETOS["etosd"]
  ETOS --> W["worker containers<br/>(mech, types, narrative, art2d,<br/>art3d, tuning, qa)"]
  W -->|git fetch/push, stage, inbox PUT<br/>via broker, 172.17.0.1:7421| GW
  W -->|query /rg| ETOS
  GW -->|gc-stage@ units| STG["gcstage slots<br/>(no network)"]
  GW -->|verified artifacts| OUT["/srv/gc-artifacts and/or OBS/OSS/MinIO"]
```

- **Play.** Mac input → viewer → `:1` → Game view → ProfileAdapterFrame (the active profile's issuer id, with
  sequences continued across restores) → TypedInputIngress → world. Committed images go to the presenters, filtered
  by the profile's visibility policy. Every admitted command is appended to the play journal (since the last
  checkpoint).
- **Tool edit (Tier 0/1).** The flow is overlay → Admission → validate-before-commit (ADR-025) → WorldCompositionBridge
  with the explicit expected revision (ADR-029). Only on `BridgeOutcome.Executed` does the Studio commit
  `tree.json`, refresh the world's rolling last-good checkpoint and emit the RG trace. Any other outcome leaves
  everything untouched and shows a structured refusal.
- **Agent work (Tier 2, content, assets).** The flow is overlay → gateway → etapi entrance request → worker. The
  worker fetches over git, queries the RG, writes, checks L0–L2, pushes or uploads, and calls stage. Its Result
  carries a `GC-RESULT {json}`, which the gateway parses and turns into an Agent Desk card. The designer clicks
  Apply, and it goes through Admission. Nothing in the player window waits (D2).
- **RG.** This is one-way: Studio to agents only ([§8](#8-rg-bridge)).

---

## 4. Code blocks

### 4.1 Unit

A code block is one UPM package, `com.<game>.block.<name>`:

| Part | Content |
|---|---|
| `package.json` | UPM dependencies on kernel packages and on the type-system block it is compiled against |
| `block.json` | The `gamecore.block/1` manifest ([§4.2](#42-manifest-and-interface-blockjson)) |
| `Rules/` | asmdef with `noEngineReferences`. Pure logic, reducers, formulas and migration transforms, testable with plain dotnet. |
| `Runtime/` | asmdef. Systems carry `[DisableAutoCreation]`, plus spawn appliers. |
| `Generated/` | Produced by `gcblock gen`, committed, and checked for byte identity like today's generated catalogs. Never hand-edited. Contents: catalog fragment and PluginManifest; UnityWorldRegistration fragment; MessagePlane routes and readers; spawn recipes with revisions; per-schema checkpoint codecs; name table; `IComponentData`/buffer structs for schema-first components; per-world lifecycle hooks; SubsystemRegistration reset stubs. |
| `Tests/`, `Oracle/` | Tests, and replay scenarios as command logs plus seeds |

- Every file has a deterministic, committed `.meta`: GUID = the first 128 bits of
  `SHA-256(blockId + '/' + relative path)`. Today `game_core/tools/make_unity_metas.py` uses `uuid`, so two Editors
  importing the same new file would write different GUIDs.
- **Forbidden contents** are rejected at L0, because they would run inside the Editor on import:
  - any `.dll`, `.so` or native plugin
  - Roslyn analyzers or source generators
  - `csc.rsp` or other `.rsp` files
  - an asmdef that includes the Editor platform (except Tests with `UNITY_INCLUDE_TESTS`)
  - `allowUnsafeCode`, or `overrideReferences` outside the kernel set
  - `[InitializeOnLoad]`, `AssetPostprocessor`, and Editor APIs in `Runtime/`

The shape copies today's split between `com.gamecore.gameplay.cards` (ECS, references Unity.Entities) and
`com.gamecore.rules.cards` (`noEngineReferences`, in the plain-dotnet solution).

**The code rule (ADR-018).** Implementation is per build and per `blockId`. Mounting is per scope and is data:
bindings, config and state. The Studio build registers **every Admitted block, Mounted or Dormant**, and the
ownership/schedule descriptor is compiled over all of them. That is why mount and unmount are live, and it also
means Dormant blocks must co-validate: two Admitted blocks can never own the same `(owner, slot)`.

### 4.2 Manifest and interface (`block.json`)

**Header:** `{format: "gamecore.block/1", name, version, kind: mechanic|typesystem|profiles|content|presentation|adapter,
conformsTo: [{typesystem, range}], determinism: integer|fixed|float}`

**provides:**
- service exports (`contract@ver`, visibility, `OverrideAncestor`)
- capability contracts (stratum, slot schemas, policy, `reducer key@ver`)
- derivation outputs
- `stages@ver`
- owned command routes (payload schema, owner stage, ingress buffer, capacity, `requiresControlGrant`)
- emitted events
- owned schemas with field tables: `{id, name, wire, required, unit, range, fixedScale, targetRef}`
- definition schemas
- spawn recipes `{id, revision, baseLayout (component set), seeds}`
- presentation keys

**requires:**
- services (range, required or optional, fallback)
- capabilities read
- stage edges
- buffers consumed
- schemas read
- routes it sends to

**state:**
- `slots: [{owner, slot, schema, policies {init, lastSupport, transfer, reset, migrate}}]`
- `components: [{name, schema, authoritative, on: world|ControllableTarget|any, enableable, buffer, dispositions {unmount, replace}}]`
- `managed: [{name, derivedFrom}]`. These are declared derived caches, rebuilt by hooks. Any other managed or static
  mutable state is forbidden.

**systems:** `[{key, stage, affinity, class: SystemBase|ISystem, access {read[], write[]}}]`

**hooks:** `{onWorldCreated, onRestored}`. These are generated wrappers around a hand-written body. They attach
modules, bind `IDomainVersionAuthority` and readers, and rebuild derived side tables from restored ECS state.

**Also:** `config {schema, defaults}`; `migrations [{schema, from, to, transform}]`; `tests`; `oracle`.

### 4.3 Hashes and build identity

- `interfaceJSON` is the RFC 8785 canonical JSON of: header kind, `conformsTo`, determinism, `provides` (including
  field tables and `recipe@revision` with base layout), `requires`, `state`, `systems` (stage and access), whether
  hooks are present, and `config.schema`.
- `effectiveInterfaceHash` = SHA-256(`interfaceJSON` + the resolved `effectiveInterfaceHash` of every type system
  the block conforms to + the gcblock generator version). A type-system update inside the declared range therefore
  changes the effective interface of every descendant.
- `implHash` = SHA-256 over the sorted (path, LF-normalized bytes) of `Rules/`, `Runtime/`, `block.json` and the
  `.meta` files. `effectiveImplHash` = SHA-256(`implHash` + the hash of the `Generated/` tree). `testHash` is kept
  separately.
- One `schema id@ver` names exactly one field table. The same `id@ver` with a different table is Breaking. The
  catalog allows only one version per schema id
  (`game_core/Packages/com.gamecore.contracts/Runtime/Catalog/ImmutableCatalog.cs:273-279`).

**Content addressing:**

- `blockId` = `StableNameKeyDerivation(name)`, and `ownerPackageId` = `blockId`. Today `ownerPackageId` is all zeros.
- `ImplementationId` stays **stable**: `StableNameKeyDerivation(name + '#' + interface major)`. `CatalogFingerprint`
  hashes `ImplementationId`
  (`game_core/Packages/com.gamecore.contracts/Runtime/Catalog/CatalogFingerprint.cs:22-30`), so putting `implHash`
  there would turn every body edit into a foreign catalog.
- `implHash` goes only into `PluginManifest.PackageContentHash`. Today that is `ContentHash.Empty`
  (`game_core/Packages/com.gamecore.gameplay.cards/Runtime/CardTableDeclarations.cs:411-415`), and ManifestValidator,
  which rejects an empty hash, is called only from dotnet tests.
- There are three build identities:
  - **InterfaceFingerprint**: today's CatalogFingerprint over stable ids, plus field tables and recipe revisions.
  - **ImplementationFingerprint**: over sorted `(blockId, effectiveImplHash)` pairs. A new header field.
  - **buildId** = SHA-256(ImplementationFingerprint + kernel tag + Unity version + generator version). A new header
    field, and the Studio world's catalog hash.

  The new header fields sit behind a required-feature id.

### 4.4 Lifecycle states (`gc_block.status`)

- **Main path:**
  - `Draft`
  - `Returned`: a Result names the commit
  - `Staged`: exported to a gcstage slot, hashes recomputed
  - `Verified`: L0–L5 green; the gateway recorded a `gc_verdict`
  - `Admitted`: merged into `studio/build`, compiled and registered
  - then `Mounted` at one or more installs, or `Dormant` (Admitted but not mounted)
  - `Superseded`: a newer `effectiveImplHash` for the same `blockId` was Admitted
  - `Retired`: removed from the build after its unmount dispositions ran
- **Side states:**
  - `Refused`: with a witness
  - `Quarantined`: a runtime fault after Apply caused an automatic rollback
  - `Published`: the IL2CPP lane passed for this `buildId`. This is separate from Admitted, because Mono and IL2CPP
    can diverge.

### 4.5 Rollback is not free

Checkpoint migrations are forward-only. After an Additive or migrating Apply, the old build can restore only the
**pre-Apply** checkpoint. Play since the Apply is reported as lost unless the play journal replays cleanly.

### 4.6 Tiers

| Tier | What | Mechanism | Rebuild |
|---|---|---|---|
| 0 | Data: config, formulas, content packs, assets bound through presentation keys, all for known schemas | Composition edits (O-05 reconfigure, spawns) | No |
| 1 | An Admitted block: `InstallMount`, `InstallUnmount`, `InstallReplace(A→B)` with a Transfer mapping | Ordinary live V1 edits (O-03, O-05, O-07) | No |
| 2 | New or changed code | Rebuild, then a world restored from checkpoint ([§4.11](#411-rebuild-and-restore-flow-tier-2)) | Yes |

**Admission** runs the composite validator (ADR-025) for Tier 0 and Tier 1:
- type-system conformance at the scope
- dependency resolution
- ManifestValidator
- the derive-and-plan dry run

Ownership and schedule are checked over the whole compiled set. A refusal happens before the lane commits.

### 4.7 Replace compatibility classes (ADR-020)

`InterfaceCompatibility.Check(old, new)` runs over the **effective** interfaces and returns one of four classes,
with witnesses:

| Class | Meaning | State |
|---|---|---|
| Identical | Only `effectiveImplHash` changed | All state Retained. The L5 replay oracle is required. |
| AdditiveCompatible | New optional fields, provides or optional requires; narrower access; new recipe components with declared seeds | Existing rows Retained, new rows seeded, recipe mapping emitted |
| MigrationRequired | A schema version bump with a unique migration path | Migrations executed |
| Breaking | Refused before any reload; the old build keeps running | — |

Breaking cases include:
- a removed provide that still has consumers
- an owner or slot change without `TransferTo`
- wider access that creates `AmbiguousOrder` or an ownership conflict against **any** Admitted block
- a policy or reducer change without a version bump
- a route payload change without a migration
- a changed field table under the same `id@ver`
- a recipe change without a mapping

The designer may force a replacement with explicit Reset dispositions (P-032).

### 4.8 Recipes

Checkpoints record each target's recipe revision, and resolution is exact (`SpawnRecipeCatalog.cs:167-180`). Every
recipe change therefore carries a **mapping**: captured revision → current revision, plus a base-layout delta and
seeds. The production restore builder allocates from the current revision and applies the delta. The generator
must keep revisions stable when a recipe has not changed.

### 4.9 State dispositions and authoring rules

Dispositions reuse `StateDispositionKind`
(`game_core/Packages/com.gamecore.contracts/Runtime/Manifest/ManifestEnums.cs:198-217`): Retract/Reset,
Retain/RetainDormant, Migrate and Transfer.

**Authoring rules** (the ADR-027 analyzer plus runtime checks):
- Every authoritative datum is a declared slot or a declared authoritative component, checkpointed as a
  `ComponentState` record.
- Entity-typed fields are allowed only as `TargetRef`, remapped to `TargetId` (04 §5).
- Undeclared components are derived, and are rebuilt by recipes and hooks.
- No static mutable fields and no undeclared managed per-world state. With the domain reload skipped, statics would
  leak between sessions.
- Systems write only their declared access set, backed by a runtime write oracle.
- No reflection, IO, threads, network or `Assembly.Load`.

### 4.10 Validation layers

| Layer | Where | Checks |
|---|---|---|
| L0 | Worker and gateway, data only | `block.json` schema and naming; forbidden package contents; deterministic `.meta`; forbidden-API text scan |
| L1 | Worker and gateway, Unity-free | `gcblock gen` byte identity; ManifestValidator; InterfaceCompatibility against the Admitted interface in the RG; type-system conformance at the intended scope; ownership/schedule dry run over the candidate plus **every** Admitted block (needs `OwnershipSchedulePipeline` moved out of `Runtime/Integration` into the dotnet build) |
| L2 | Worker container, then gcstage | `dotnet build` and test of Rules |
| L3 | gcstage slot, batchmode | Unity compile with the analyzer; EditMode tests |
| L4 | gcstage slot, PlayMode, synchronous Burst | World creation with the full Admitted set (bootstrap `FallbackCount == 0`); mount/reconfigure/replace/unmount probe; checkpoint round trip (equal canonical hash of carried records, equal temporal origin and issuer high-water marks, hooks proven to have run); runtime write oracle (per-type chunk change versions around each guarded dispatch vs the declared write set) |
| L5 | gcstage | For the Identical class with integer or fixed determinism: replay oracle command logs on old and new and diff the committed events; property tests |
| L6 | Asynchronous publish lane | IL2CPP build plus probe → `Published` |

The Studio re-runs L0–L1 itself, and trusts L2–L5 only through verdicts recorded by the gateway.

### 4.11 Rebuild and restore flow (Tier 2)

1. The designer clicks **Apply**. The Reload Controller pauses input and reaches a committed boundary in every live
   world.
2. It captures each world to FileCheckpointStore: ComponentState records, the temporal header, issuer high-water
   marks, the per-block interface table and the profile header.
   - A world that cannot be captured falls back to its rolling last-good checkpoint plus play-journal replay.
     Otherwise the loss is reported explicitly.
   - The reload journal holds the UI state, the play journals and the rollback target.
3. It exits Play Mode. Inside `StartAssetEditing` it merges the verified commit, runs `gcblock gen`, regenerates the
   aggregation and manifest, then does **one** `AssetDatabase.Refresh`.
   - If the compile fails, Unity keeps the old assemblies and no reload happens. The merge is reverted, and the
     diagnostics go to the agent as a new request.
4. The domain reloads once. A Studio `[InitializeOnLoad]` hook reads the journal and enters Play Mode without another
   domain reload.
5. StudioHost creates the worlds from the new registration, with catalog hash = the new `buildId` and a
   `RestoredTemporalOrigin` (ADR-022).
   - Bootstrap `FallbackCount > 0`, `LastCode != None` or a descriptor refusal means the Apply failed, and it is
     reverted automatically.
6. The restore planner classifies each block and applies recipe mappings, dispositions and migrations. It restores
   in **one** batched composition publication (ADR-023).
7. It runs the `onRestored` hooks and rebinds adapter frames (issuer sequences continue), presenters and the
   camera, then resumes. LogicalStep, time debt and domain seconds are the same; the `WorldId` is new.
8. **Guard.** A P-031 fail-stop or a sampled write-oracle violation within N steps triggers a rollback: revert the
   build commit, recompile, restore the pre-Apply checkpoint, report the play lost since Apply, and mark the block
   `Quarantined`.

**IL2CPP variant:** the publish lane builds, then the player restarts from a checkpoint file
(`WorldRecovery.Restart`).

---

## 5. Type-system extension

A type system is an ordinary block of kind `typesystem`, mounted at a scope. Every block installed at or below that
scope must conform to it. It scopes installations and data, not code (B1), so it governs through three layers.

**Layer 1: generation and build time (the strongest layer).** The type-system package ships:
- a type-kit Rules assembly: fixed-point, unit-tagged and bounded numerics, enums, id types, and a formula evaluator
- schema families: `{prefix, allowedWireTypes, fieldAnnotations (unit, range, fixedScale, enum, targetRef),
  requiredFields, wire-type → ECS field-type mapping, codec template}`
- analyzer rule parameters

`gcblock gen` reads the declarative family tables and restricted templates. These are data, so no agent code runs
inside the generator. It writes into each conforming block's committed `Generated/` folder: `IComponentData` and
buffer structs, checkpoint codecs, migration skeletons, access declarations, and the `gc_schema` rows the gateway
publishes.

**Why not a Roslyn source generator.** Types emitted by one Roslyn generator are invisible to Unity's Entities
generators (SystemAPI, IJobEntity) in the same compilation, and a generator cannot write RG data. The Roslyn DLL
only reports diagnostics. It rejects:
- hand-written `IComponentData` in conforming blocks
- ECS access outside the declared set
- reflection
- static mutable state
- floats in integer-determinism families
- writes to another owner's components

**Identity.** A block's `effectiveInterfaceHash` includes the resolved hash of each type system it conforms to,
plus the generator version. A type-system change that alters a family's wire layout (wire type, fixedScale, unit)
must bump the version of every affected schema. Re-typed data therefore always goes through MigrationRequired, and
never silently rides an Identical classification.

**Layer 2: mount and edit time (tree-scoped, ADR-026).** The manifest adds a ConformanceProfile:
`{id@ver, refines?, families[], naming {regex per category}, policyRules [{ruleId, predicateKey, params}], mode: staged|enforced}`.
A `ScopedConformanceValidator` evaluates it as one member of the composite `ICompositionEditValidator` (ADR-025).
The seam exists at `game_core/Packages/com.gamecore.composition/Runtime/Operations/CompositionChangeSet.cs:438-481`,
but today CompositionHost takes a single validator and a refusal carries only a string.

- **When it runs:** on mount, reconfigure, reparent and replace, and on mounting or replacing a type system. A
  stricter type system re-checks its whole subtree.
- **Effective profile at a scope:** the union of every type-system install on the ancestor path. Nesting may only
  refine; contradictions are refused.
- **Mount condition:** the block's compiled `conformsTo` must include every type system governing the target scope,
  with a matching resolved hash.
- **On refusal:** the old composition stays, and a structured witness `{typesystem, ruleId, block, declaration path,
  offending value}` goes to the window and to the RG as `gc_refusal`.

**Layer 3: runtime (optional).** Services exported to descendants (P-011): unit conversion, formula evaluation,
validation. A validation stage that descendant systems must run `After`. Invariant systems over the families; a
violation is a P-031 fail-stop in the Studio and telemetry in players.

**Example policy rules:**
- Authoritative slots are PreserveDormant and not resettable.
- Gameplay buffers use RejectCommand overflow with a capacity of at most N.
- No Float32 or Float64 in authoritative schemas.
- Every system in family X runs `After ts.validate`.
- A component with unit `hp` requires the capability `health.read@1`.
- Player-owned state lives only on `profiles.ControllableTarget/v1` targets.

**Formulas** are Tier 0 config, typed and unit-checked by the governing type system. The formula editor and curve
checker call the type kit's Unity-free evaluator.

**Lifecycle:**
1. Mount in `staged` mode. Violations become `gc_conformance` rows, and nothing is refused.
2. The types agent fixes the descendants.
3. Switch to `enforced`.

Replacing a type system regenerates every descendant and becomes one Tier 2 Apply, with migrations for re-typed
schemas.

**Where the freedom ends:**
- **Engine limits:** a type system cannot change how Entities stores components, cannot register types without a
  domain reload, and cannot give two subtrees different code for one block.
- **Kernel invariants it may add to but never relax:**
  - P-034: one owner per slot
  - P-043: bounded buffers, no silent drop
  - P-021: finite strata
  - P-030: one publication path
  - P-042: typed commands
  - P-054: no reflection
  - P-001: the trust model
- **Also:** two subtrees cannot run different versions of one schema id, and a type system cannot intercept other
  blocks' systems except through declared stage edges or request ports.

These invariants are exactly what make blocks replaceable. Everything else is open: naming, units, numeric model,
storage conventions, required contracts, generated code, runtime validation and formula typing.

---

## 6. Player profiles extension

`com.gamecore.profiles` is a first-party block of kind `profiles` mounted near the root, plus Studio features. A
game with a single profile mounts nothing, and the Studio hides the switcher. GameCore has no player-profile
concept today; "profile" in its docs means the build profile.

**Level 1: profiles that share one session** (seats, co-op, PvP, spectators, game master).
- One world per playthrough. Each profile is one issuer id bound to its controllable targets.
- The pieces already exist:
  - card seats and runners are targets
  - the TypedInputIngress operation id is `(World, Source, Sequence)`
    (`game_core/Packages/com.gamecore.unity.adapters/Runtime/Pure/Input/InputIngress.cs:31-64`)
  - reparenting keeps state (REF-C05)
- **Switching** is done by the Studio alone: it changes the active issuer, how bindings resolve, the camera and the
  visibility filter. No world operation runs, so the switch is instant and deterministic.
- Inactive profiles are Idle, Scripted or Bot. A bot is itself a code block.

**Level 2: independent playthroughs** (newcomer vs veteran, class A vs class B). The checkpoint with rebase is the
source of truth, and live worlds are a warm cache.
- A playthrough is `{designRevision, play-section checkpoint}`.
- **Active:** running and presented.
- **Warm** (K ≈ 2–3, least recently used): paused with O-26. Paused worlds still accept publications.
- **Cold:** checkpoint only.

**Why not a pure model.**
- *Pure shared world:* it shares world-global state, such as the narrative QuestLedger
  (`game_core/Packages/com.gamecore.gameplay.narrative/Runtime/NarrativeKeys.cs:65-66`), and the single clock.
- *Pure world per profile:* every edit pays the preparation cost N times, and one world can accept an edit that
  another refuses. Every Tier 2 Apply destroys every live world anyway (H2).

**Catch-up and restore:**
- `design/tree.json` carries a monotonic `designRevision`. Each world keeps its own `CompositionRevision`, plus a map
  `designRevision → CompositionRevision` published in the profile header (ADR-033).
- **Warm catch-up** diffs the world's last `designRevision` against the current tree, and applies the diff as one
  multi-edit proposal (ADR-024) with that world's own expected revision (ADR-029). A warm world that refuses is marked
  `Blocked`; it never silently diverges.
- **Cold open** is a rebase restore onto the current design in one batched publication (ADR-023). Today's GC-027
  builder replays edit by edit
  (`game_core/unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/Gc027RestoreBuilder.cs:352,865`).
- **Issuer continuity.** RequestLedger keeps a high-water mark per IssuerId, but `WorldAdapterFrame` restarts its
  sequence at 0 (`game_core/Packages/com.gamecore.unity.adapters/Runtime/Input/WorldAdapterFrame.cs:41,110`).
  ProfileAdapterFrame resumes from the restored high-water mark (ADR-022).

**What the extension provides:**
- the `profiles.Directory/v1` service
- the **ProfileBinding** plugin: one install per profile, whose config is the profile definition. Its rule derives a
  Replace-policy `profiles.EffectiveProfile` binding onto targets that declare `profiles.ControllableTarget/v1`.
- **control grants** (issuer → targets), checked at command admission (ADR-030). Today the seat is named in the
  payload and nobody checks it (`game_core/Packages/com.gamecore.gameplay.cards/Runtime/CardTableSystems.cs:539,571`).
- a **visibility-policy** contract: a pure function over the committed image. Observation always covers the whole
  world (P-045), so hidden information is filtered in presentation.
- a seed schema: `Fresh | Commands (typed envelopes from a privileged studio issuer) | Fork(checkpointRef)`
- generated codecs

**Profile definition** (config, versioned with the design tree):
`{id (Id128 = issuer id), name, role, controlledRecipe, loadout[], permissions[route|capability], locale,
device {kbm|pad|touch, viewport}, controlMode Human|Idle|Scripted(recipe), seed, playthrough Shared(id)|Solo}`

**What the Studio owns:**
- the create/edit UI and the switcher (hotkey and overlay)
- world lifecycle: create, pause, capture, restore, evict
- ProfileAdapterFrame: several `(issuer, bindings, sequence)` triples per world
- a camera and presentation filter per profile
- a play journal per world
- checkpoints at `<game>/playthroughs/<pt>/<designRev>-<step>.gcc`
- RG publication: `gc_profile`, `gc_playthrough`, `gc_profile_seat`, and `gc_checkpoint` (metadata only), with traces
  `by=profile:<id>`

**Kernel dependencies:**
- Level 1 needs ADR-028, 029, 030, 022 and ProfileAdapterFrame.
- Level 2 adds ADR-023, 033, 024, 031 and 021.

---

## 7. Transaction agents

*All facts in this section are at etos `e99870d`. Re-verify them after the SDK refactor.*

### 7.1 Packaging in game_core

**D7 holds.** At `e99870d` a transaction agent contains no etos code. It is three things:
- an **entrance** `{name, app, worker, transcribe, ocr}`, served by the built-in native entrance actor
  (`etos/crates/etapi/src/config.rs:45-63`)
- a **worker record**: image, model, instructions, network, optional controller, budget
  (`etos/crates/etcore/src/proto.rs:730-760`)
- **instructions** text

The references confirm this:
- `etos/apps/browser` is only `app.toml`, `instructions.md` and an extension. The extension carries its own small
  etapi client and does not use `@etos/sdk`.
- `etos app install` accepts **any directory path** (`etos/crates/etcli/src/apps.rs:217-265`).

**Four qualifications:**

1. **etos publishes no binaries or packages at `e99870d`.** `etosd`, `etos` and the static musl `etos` must be built
   once from the pinned commit (`etos/docs/operator.md:14-32`). `@etos/sdk` is `private: true`, `UNLICENSED`
   (`etos/sdk/ts/package.json`). The Python `etos` package (Apache-2.0, stdlib only) is not on any index: never
   `pip install etos`, because the name may resolve to an unrelated package. The etos source is therefore a
   one-time build input per pin, never a code dependency of game_core.
2. **`app.toml` cannot express Studio workers.** It is closed (`deny_unknown_fields`) and allows only `app`, `label`,
   `routes`, one `[entrance]`, one `[worker] {name, hostless, model, instructions, budget}` and `[extension]`
   (`etos/crates/etcli/src/apps.rs:172-215`). It has no image, network, controller, git identity or grants. If the
   worker is missing, `app install` creates it with the default image and network `open`. So:
   - agent `app.toml` files carry **no `[worker]` section**
   - the install script creates the worker first with the CLI
3. **Custom actors and controllers can keep their source in game_core,** but `actor.register` is a task-socket
   operation only (`etos/crates/etnode/src/socket/task.rs:209-217`). None are needed: the default native controller
   plus instructions is enough.
4. **The etapi contract is documented and machine-readable,** through `etosd schema`, which is identical to the
   committed `etos/crates/etapi/schema/etapi.json`. It is pre-1.0, though: `/api/v1` is its only version marker, and
   the CLI's `--json` output has no schema. game_core pins a version and runs a contract check.

**Layout** (under `game_core/studio/`, see [§3.3](#33-repositories-and-layout)):

```
studio/
  etos/
    etos.lock            # etos_commit=e99870d; sha256 of etosd, etos (host), etos (x86_64-musl);
                         # sha256 of `etosd schema`; image_layer_version=3; sha256 of vendored SDK artifacts
    etapi.schema.json    # snapshot of `etosd schema` at the pin
    build-etos.sh        # one-time host step: cargo build -p etnode -p etcli, the musl etos, optional npm pack;
                         # install into /opt/etos/{bin, lib/etos/sdk/py, lib/etos/sdk/js}; verify against etos.lock
  node/
    etos.toml.tmpl       # the WHOLE file: etos.toml has no include and rejects unknown keys
    models.toml.tmpl, ops.toml.tmpl, etosd.service (User=etos, ETOS_ROOT=/var/lib/etos)
  images/
    gc-base/Dockerfile   # FROM a digest-pinned Debian/Ubuntu: bash, coreutils, git, tmux, ca-certificates,
                         # python3, tini. NOT FROM etos-default: the node adds its etos layer to any base
                         # (etos/crates/etnode/src/container/image.rs:1-17)
    gc-mech/Dockerfile   # + .NET 8 SDK, /opt/nuget pre-restored, gcblock, analyzer DLL, block templates
    gc-content/Dockerfile# + .NET 8, gcblock validate-content, numpy, matplotlib
    gc-art/Dockerfile    # + Pillow, ImageMagick, the `gcart` CLI
    gc-art3d/Dockerfile  # gc-art + gltf-validator, trimesh, optional headless Blender
  agents/<agent>/
    app.toml             # app="studio-<agent>", label, routes=["entrances"], [entrance] name/worker; NO [worker]
    agent.toml           # game_core's own install spec (etos never reads it): worker, image, model alias,
                         # network="allowlist:172.17.0.1", git_name/git_email, instructions="instructions.md",
                         # grants=[{kind="credential", target="172.17.0.1",
                         #          source="file:/var/lib/etos/secrets/<worker>.token", scheme="bearer"}]
    instructions.md      # GC-TASK / GC-RESULT protocol and verification ladder; uses only the in-container
                         # `etos` CLI and tools of the pinned version
    actors/              # optional; none needed today
    tests/               # instructions lint, image smoke, optional e2e against an ephemeral etosd with a
                         # game_core-owned scripted-model stub
  tools/
    install-agents.sh    # idempotent; run as `sudo -u etos env ETOS_ROOT=/var/lib/etos PATH=/opt/etos/bin:$PATH`
    uninstall-agent.sh
    etos-contract-check.sh
  gateway/               # .NET 8 studio-gateway; Etapi/ DTOs generated from etos/etapi.schema.json
```

**Apps and keys.** There is one installed app per agent, each with one entrance and one worker (the table in
[§7.3](#73-roster)). A separate app, `studio`, is used by the RG bridge (routes `logger` and `query`, plus `sources`
if needed). The gateway holds 1 + 7 keys in `/var/lib/etos/secrets/*.key.json` (mode 0600), minted with
`etos app pair <app> --approve --out <file>`.

This beats seven `[[api.entrances]]` under one key for three reasons:
- agents install, replace and uninstall at runtime without an etosd restart
- `etos app revoke studio-<agent>` is a per-agent kill switch
- each agent directory is a self-contained unit, like `etos/apps/browser`

The fallback, if one key is preferred, is `[[api.entrances]]` with app `studio` in `etos.toml.tmpl`, at the cost of
an etosd restart on every change. *(Open: open-problems A10.)*

**Install script, per agent, in order:**
1. **Verify `etos.lock`:** the sha256 of `/opt/etos/bin/*`, and `sha256(etosd schema)` equal to the snapshot. Abort
   on drift.
2. **Build the image:** `docker build -t localhost/gc-<img>:<studio-tree-sha> -t localhost/gc-<img>:current`.
   Workers reference `:current`, and the node re-inspects the base image id for every task, so a rebuild updates
   agents without recreating workers.
3. **Create or recreate the worker:** `etos worker list --json`.
   - If the worker is missing:
     `etos worker create <w> --image localhost/gc-<img>:current --model <alias> --network allowlist:172.17.0.1
     --git-name … --git-email … --instructions "$(cat agents/<a>/instructions.md)"`. **Never pass `--budget`**
     ([§7.7](#77-spend-control)).
   - If it exists and its instructions, model, network or image differ: require it to be idle, then
     `etos worker rm <w> --purge` and create it again. **No worker-update operation exists** on the host socket, and
     `--purge` deletes the home volume.
4. **Grant the credential:** `etos grant <w> credential 172.17.0.1 --source file:… --scheme bearer`. Grants are
   upserts and are re-issued every time. They match on host only (`etos/crates/etbroker/src/proxy.rs:173-185`).
5. **Install the app:** `etos app install $PWD/studio/agents/<a>`. This replaces the app's single entrance.
6. **Pair the key:** if there is no approved key for `studio-<a>`, run
   `etos app pair studio-<a> --approve --out /var/lib/etos/secrets/studio-<a>.key.json`.

Uninstall is: `etos app uninstall studio-<a>`, then `etos revoke <w> credential 172.17.0.1`, then optionally
`etos worker rm <w> --purge`.

**Gateway etapi client.** It is C#, with DTOs generated from `etapi.schema.json`. It needs:
- lenient deserialization
- a hand-written canonical-JSON writer that matches `etcore::canonical_json` for the binding digest: sorted keys, no
  whitespace, serde_json escaping. `System.Text.Json`'s default encoder is wrong for this.
- a persisted producer id and seq

Task control shells out to the pinned `etos` CLI with `--json`; it never speaks the host-socket protocol.
`@etos/sdk` is optional (a reference and test oracle). Vendoring its tarball needs the owner to relicense it from
UNLICENSED.

**Remaining couplings to etos** (all version-pinned, none to source):
- the binaries
- the schemas of `etos.toml`, `models.toml`, `ops.toml` and `app.toml`
- the host CLI surface and its `--json` shapes
- the etapi `/api/v1` wire contract, including canonical JSON and idempotency rules
- the in-container contract:
  - the tools `query`, `ask`, `publish`, `spawn` and `wait`
  - `etos generate`, `describe` and `transcribe`
  - `/inputs`, `/outputs`, `/rg` and `/home/agent`
  - `HTTP_PROXY` with the `etos-brokered` placeholder
- entrance behaviour: the preamble, the 4000-byte history, attachment limits
- image-layer requirements
- behavioural facts: lifetime budget semantics, a subagent cap that returns `busy`, one level of subagents

`etos-contract-check.sh` plus `etos.lock` turns every etos upgrade into a deliberate, reviewed step.

### 7.2 Common protocol: GC-TASK and GC-RESULT

**Request.** The gateway opens a **fresh conversation for every GC-TASK**. The entrance adds its own preamble, and a
continued conversation would add up to 4000 bytes of stale history (`etos/crates/etapi/src/entrance.rs:678-690,786-800`).
- The request text is one pointer line, `GC-TASK request <id>: see /inputs/gc-task.json`, followed by the designer's
  prose.
- The envelope is the attachment `gc-task.json`. Attachments are staged into `/inputs`. Limits: at most 16 files of
  at most 16 MiB each, and the body at most 48 MiB of base64 (about 36 MiB decoded;
  `etos/crates/etapi/src/config.rs:85-100`).
- A follow-up is a new request whose envelope has `parent_request_id`. Nothing relies on conversation history or on
  `ask`.
- Every Studio entrance sets `transcribe=false` and `ocr=false`. By code reading, the entrance's own conversion is
  refused for container workers, because the entrance actor has no task
  (`etos/crates/etapi/src/entrance.rs:100-101`; `etos/crates/etnode/src/services.rs:103-116`). Workers run
  `etos ocr|transcribe|describe /inputs/<file>` themselves.

**Envelope (`gc-task.json`):**
```
{ v, op, request_id, parent_request_id?, game, base_commit,
  git: "http://172.17.0.1:7421/git/<game>.git", push_prefix: "refs/heads/agent/<worker>/<request_id>/",
  gateway: "http://172.17.0.1:7421", rg: {app: "studio", designRevision},
  subject: {...op-specific: mount_path, blockId, keep_interface | allowed classes, effective_interface_hash,
            scope, schema ids, presentation key, constraints...},
  acceptance_tests: [...], deadline_utc, max_stage_attempts, max_cost_usd_per_generate? }
```

**Result.** The Result text starts with `GC-RESULT {json}`:
```
{ v, request_id, status: ok|partial|refused|needs-mechanic, read_interface_hash,
  code?:      {branch, commit, base, block {id, version, effective_interface_hash, impl_hash, class},
               verdict_id, files [{path, sha256}]},
  artifacts?: [{sha256, name, role, media_type, bytes, source_path, generator {op, provider, job_key, cost_usd},
                derived_from [sha256], import {...}, presentation_key}],
  proposals?: [{kind: tuning|profile|content, payload}],
  notes }
```

The gateway:
- long-polls conversations (`wait_ms` up to 60000)
- parses `GC-RESULT` strictly against a JSON schema. On a parse failure it sends one re-ask, as a new request with
  `parent_request_id`.
- treats four things as terminal: a Result, an Error, a progress text matching `I stopped because` (budget stop), and
  its own deadline cancel (`etos task cancel`)
- uses `etos task show` to tell queued from running. The entrance posts "Working on it" even for a queued task
  (`etos/crates/etapi/src/entrance.rs:811-825`).

The Studio rejects results built on a stale interface: `read_interface_hash` must match what it holds.

### 7.3 Roster

| Agent | App / entrance / worker | Image | Output | Return path | Verification |
|---|---|---|---|---|---|
| mech: mechanism code | `studio-mech` / `mech` / `mechanic` | gc-mech | Code blocks: new, update, replace, migration, unmount plan | git branch + `GC-RESULT.code` | L0–L2 in task; gateway L0–L1; gcstage L2–L5; Studio re-verifies hashes and L1 |
| types: type systems, cascades | `studio-types` / `types` / `typesmith` | gc-mech | Type-system block + regenerated descendants, cascade plan | git branches (one per block) | L0–L5 over the affected subtree; conformance; migration round trips on exported playthrough checkpoints |
| narrative | `studio-narrative` / `narrative` / `narrator` | gc-content | Content packs for known schemas (ADR-032); or `needs-mechanic` with a spec | git (JSON) or inline proposals | Schema, reference integrity, graph reachability with pure `Rules.Narrative`; headless gcstage dialogue run |
| art2d | `studio-art2d` / `art2d` / `painter` | gc-art | Sprites, textures, icons, UI, with import settings | inbox + `GC-RESULT.artifacts` | Dimensions, format, alpha, size budget, `etos describe` against the brief; gateway checks; trial import |
| art3d | `studio-art3d` / `art3d` / `modeler` | gc-art3d | GLB + manifest | inbox + `GC-RESULT.artifacts` | gltf-validator, triangle/texture budgets, units, pivot, material slots; gcstage import |
| tuning: formulas, curves, balance | `studio-tuning` / `tuning` / `tuner` | gc-content | Config revisions (Tier 0 InstallReconfigure); curve CSV/PNG | inline proposals / git; inbox for previews | Type-system unit checks; monotonicity/bounds; replay of recorded traces in headless gcstage |
| qa: playtest analyst, profile designer | `studio-qa` / `qa` / `tester` | gc-content | Coverage per profile, profile proposals, oracle scenarios | inline + git (scenarios) | Scenarios replay green on the current build |

**Notes:**
- Narrative content is C# today (`game_core/Packages/com.gamecore.rules.narrative/Runtime/NarrativeChapters.cs`),
  so until ADR-032 lands narrative changes go through mech as Tier 2.
- A 3D model bound to an existing presentation key is Tier 0. A new baked prefab or spawn recipe is generated code
  resolved by exact revision, so it goes to mech as Tier 2.
- Content agents need **container** workers: hostless workers have no shell and cannot run `etos generate`.
- Accepted qa and tuning proposals are re-published by the gateway as Studio-owned `gc_proposal` and `gc_coverage`
  rows ([§8](#8-rg-bridge)).

### 7.4 Code path (git)

- The worker's source comes over git smart-HTTP through the broker, into a mirror in the persistent `/home/agent`:
  `git -c http.proxy=$HTTP_PROXY -c http.extraHeader='Authorization: Bearer etos-brokered' fetch`. The broker
  substitutes the real token (`etos/crates/etbroker/src/proxy.rs:684-721`), so the token never enters model context.
- Entrance tasks get **no repository** (`etos/crates/etnode/src/endpoint.rs:440-444`), so `etos deliver` is
  unavailable (`endpoint.rs:1083-1095`).
- Code is pushed to `agent/<worker>/<request_id>/<n>`. The gateway refuses any other ref for that token, and refuses
  large or binary blobs.
- Code stays on git because it needs ancestry, diff, review and merge. Staging verdicts are keyed by commit, and
  Apply is a merge.

### 7.5 Artifact pipeline

**D8 is partly right.**

What the owner has right:
- Generation is a **node-side** etops capability, separate from the worker's Result text.
- `etos generate image|video|music|3d` runs on the node with the `ops.toml` provider credentials and writes real
  files into the task's `/outputs`. The host path is `<root>/outputs/<task>/`, and the files stay there whatever the
  Result says (`etos/crates/etnode/src/socket/task.rs:1083-1122`).
- A worker container can push bytes to any HTTP endpoint its network policy allows, with no size limit.

What does not match the code at `e99870d`:
1. **No output-side modality actors.** The part of design-agent §11 where results become speech, images or rendered
   output on the way back is design only. An entrance has only the input flags `transcribe` and `ocr`
   (`etos/crates/etapi/src/entrance.rs:571-591`).
2. **Generation is not done by autonomous actors inside the agent.** The text-only worker runs it from its shell, or
   through a task-scoped helper actor that it spawns (sys/ops).
3. **Actors cannot publish.** They run under bwrap with no network and no file access, and they cannot read
   `/outputs` (`etos/crates/etactor/src/process/confine.rs`).
4. **etos never writes to an object store,** and its broker cannot sign S3/OBS requests. It supports only bearer,
   token, basic and header schemes, with no SigV4 (`etos/crates/etbroker/src/db.rs:84-114`).
5. **Entrance tasks get no host mounts** (`etos/crates/etnode/src/endpoint.rs:457`).
6. **References carry no bytes and no digest** on the SDK wire (`etos/crates/etapi/src/wire.rs:302-316`), and
   pinned-reference fetch is capped at 1 MiB, hard-coded (`etos/crates/etnode/src/refs/mod.rs:272-285`).

**Pipeline** (zero etos core changes; everything is maintained on the game_core side):

1. **Produce.** The worker runs `etos generate … --out /outputs/<slot>/<name>.<ext> --option max_cost_usd=<cap>`. It
   post-processes in the container (Pillow, ImageMagick, gltf-validator, trimesh) and self-checks with
   `etos describe`. For variants it uses subagents.
2. **Upload** to the gateway inbox (primary path). The gc-art image ships `gcart put <file> --request <id> --role <role>
   --type <media>`. It hashes the file and streams it with `curl --upload-file` to
   `http://172.17.0.1:7421/v1/inbox/<request_id>/<sha256>`, with `Authorization: Bearer etos-brokered`, through
   `$HTTP_PROXY`. The gateway:
   - streams into quarantine, recomputes the sha256 while receiving, and rejects a mismatch
   - binds the token to the request currently assigned to that worker
   - enforces per-request quotas (file count, bytes per kind, total) and a media-type allowlist
   - returns 200 on a repeat of the same sha256, so retries are idempotent

   No object-store credential ever enters etos or a container.
3. **Claim.** `GC-RESULT.artifacts[]` lists what the worker claims. The claim must be a subset of the inbox ledger,
   matched by sha256. Unclaimed uploads expire.
4. **Verify** (gateway):
   - magic-byte sniffing against `media_type`
   - decode or probe (identify/Pillow, ffprobe, gltf-validator)
   - budgets for dimensions, triangles, duration and size
   - pack policy: no `.cs`, `.dll`, `.rsp`, `.asmdef` or scripts; no archive path traversal; sanitized names
   - trial import in a gcstage slot
   - record a `gc_verdict`
5. **Publish** (the gateway is the **only** publisher). It atomically copies the verified bytes, content-addressed, to:
   - `/srv/gc-artifacts/<game>/sha256/<aa>/<sha256>.<ext>`, and/or
   - OBS/OSS/MinIO at `<bucket>/<game>/sha256/<sha256>.<ext>`, with the gateway's own SDK credentials, metadata
     `sha256` and `request_id`, and `Content-MD5` on the PUT, confirmed with HEAD

   Content addressing makes re-publishing idempotent.
6. **Announce.** A `gc_asset` row or trace through the RG bridge: `{sha256, uri, media_type, request_id, verdict_id,
   presentation_key, status}`. The Studio and other agents learn about assets from the RG and the gateway ledger. A
   bucket listing is only for audit.
7. **Fallback.** Since the gateway and etosd share host and OS user (D1), the gateway may pull from
   `/var/lib/etos/outputs/<task>/` if an upload failed. The pull must be symlink-safe: `lstat`, `O_NOFOLLOW`, regular
   files only, realpath confined to that task's outputs, size caps. This depends on etos's internal layout, and
   `/outputs` is never garbage-collected by etnode, so retention is an operator task.
8. **Later, optional, for large video.** Gateway-minted presigned PUT URLs to a quarantine prefix. The worker's
   allowlist then adds the exact bucket host, and the gateway GETs, hashes and server-side copies the object.

**Rejected:**
- a brokered OBS credential (the broker has no SigV4)
- visible-secret AK/SK grants (they put long-lived keys where the model can read them)
- a writable host mount (unavailable to entrance tasks)

### 7.6 Staging service

- The worker calls `POST /v1/stage {request_id, commit}` and long-polls `GET /v1/stage/{job}`.
- The gateway re-runs L0–L1 over data only. It exports the commit into a free gcstage slot, where L2–L5 run as
  `gc-stage@<job>`:
  - sandboxed user, no network
  - synchronous Burst
  - write oracle
  - checkpoint round trip, including the temporal origin
  - replay oracle
- After `max_stage_attempts` the gateway answers "attempts exhausted; return GC-RESULT status=partial".
- The worker iterates inside **one** task and never uses ask-and-wait.

### 7.7 Spend control

At `e99870d`, `--budget` is a **lifetime total** for the worker's scope, and resetting it keeps the usage
(`etos/crates/etnode/src/node.rs:259-274`). No host operation raises it or wakes the task. When it runs out, the task
is left Waiting, which counts as busy and blocks the queue (`etos/crates/etnode/src/task.rs:1637-1647`). So:

- workers are created **without** `--budget`
- spend is bounded instead by:
  - the gateway's per-request deadline and stage-attempt limit, with `etos task cancel`
  - the per-call `--option max_cost_usd` on generation
  - provider-side caps

### 7.8 Subagents

- Subagents are one level only (`etos/crates/etnode/src/endpoint.rs:664-670`).
- `limits.subagent_containers` (default 8) is a **refusal, not a queue**: spawn returns `busy`
  (`endpoint.rs:688-712`). Instructions therefore retry with backoff.
- A child sees the parent's home read-only and gets no repository. It clones with
  `git clone --reference /home/agent/mirror/<game>.git` into `/tmp` and pushes `…/<n>-<key>`. Whether it shares the
  parent's broker grant is to be confirmed in R0.
- The parent merges, re-runs L0–L2 and stages once.

### 7.9 Security and trust

- **Unapproved agent code** runs only as `gcstage`, without network. Unity licence validation may need network,
  which would force an allowlist for licence hosts (open-problems A3).
- **Approved code** runs in the Studio as `worlesenric`, which is root-equivalent. Apply is a full-trust decision
  until the optional `studio`-user hardening (R5).
- **Import-time code:** block packages could carry Editor-time code, which L0 forbids.
- **Egress:** `--network allowlist:…` binds only proxied traffic at `e99870d`. etnode has no packet filter
  (`etos/crates/etnode/src/task.rs:1609-1610`). Containment relies on the inbox token scope and gateway verification.
  A host firewall rule for etos containers is optional hardening.
- **Container privileges:** workers have passwordless sudo inside their containers.

---

## 8. RG bridge

**Where it runs.** Inside `studio-gateway` (user `etos`), not in the Editor, so it survives domain reloads and
Editor crashes. It uses etapi at `http://127.0.0.1:7410` with app key `studio`:

- `PUT /api/v1/bindings/studio` declares the states. The same declaration keeps the same version.
- `POST /api/v1/bindings/studio/traces` appends batches idempotently by `(producer 'studio-bridge', seq)`, where
  `seq` is persisted.
- Batches are chunked to at most 8 MiB and 10k traces.
- `[node] local=true` with the owner set means rows land in the owner's partition, which workers read
  (`etos/crates/etapi/src/state.rs:188-192`).

**Sources of truth.** Git (`studio/build`) holds the design tree, block manifests and content. The Editor and the
gateway hold live state. The bridge publishes only after `BridgeOutcome.Executed` and the `tree.json` commit, never
on a lane-only publication. It also publishes on Admission and Apply, on staging verdicts, and on profile and
playthrough changes.

**One-way.** An SDK app sees only its own app states. Derived kinds, `rg.*` tables and violations are for workers
(`etos/crates/etsearch/src/catalog.rs:602-628`). Everything from agents to the Studio therefore travels in
`GC-RESULT` or through the inbox. The gateway re-publishes what the Studio accepts as Studio-owned app states, so the
window and other workers can then query it.

**App states (Studio-owned).** Kind names are lowercase and at most 64 characters. Keys are stable names; 32-hex
ids go in props. Every JSON prop has a `format` field.

| Kind | Key | Props / links |
|---|---|---|
| `gc_game` | game | repo, build_id, kernel_tag, unity |
| `gc_build` | build_id | interface_fingerprint, implementation_fingerprint, blocks json `[{block_id, effective_impl_hash, status Mounted\|Dormant}]`, status editor\|staged\|published; link game |
| `gc_block` | `<name>@<version>` | block_id, kind, effective_interface_hash, interface_hash, impl_hash, effective_impl_hash, commit, path, determinism, status, interface json |
| `gc_scope` | `<game>/<scope path>` | depth, path (materialized ancestor list), isolation, exclusions, governing_typesystems; link parent |
| `gc_install` | install | instance_id, lifecycle, config json, config_rev, design_revision, priority; links block, scope |
| `gc_schema` | `<schema>@<ver>` | id, storage slot\|component\|definition\|payload, fields json; link owner → gc_block |
| `gc_recipe` | `<recipe>@<rev>` | base_layout, seeds; link block |
| `gc_system` | system | stage, access json; link block |
| `gc_stage`, `gc_route`, `gc_event` | — | — |
| `gc_typesystem` | `profile@ver` | profile json, mode staged\|enforced, effective_interface_hash; link install |
| `gc_profile`, `gc_playthrough`, `gc_profile_seat`, `gc_checkpoint` | — | checkpoint = metadata only (sha256, step, design_rev, path) |
| `gc_asset` | sha256 | name, kind, media_type, uri, presentation_key, request_id, verdict_id, status |
| `gc_request` | request_id | entrance, op, status queued\|running\|result\|error\|cancelled\|stalled, conversation, task, deadline, attempts, result json |
| `gc_verdict` | verdict_id | layers json, commit, class, ok, sandbox_user; links block, request |
| `gc_refusal` | operation_id | code, phase, witness json; links block, install |
| `gc_conformance` | `<typesystem>\|<install>` | ok, violations json |
| `gc_proposal` | `<request_id>/<n>` | source_agent, kind tuning\|profile\|content\|asset, payload json, status offered\|accepted\|rejected |
| `gc_coverage` | `<profile>\|<build_id>` | routes json, states json; link request |

**Edge kinds.** An RG link holds exactly one key (`etos/crates/etrg/src/types.rs:217-239`), so many-to-many
relations become kinds of their own: `gc_provides`, `gc_requires` (range, required, fallback), `gc_reads` and
`gc_writes` (`<system>|<schema>`), `gc_emits`, `gc_constrains` (`<typesystem>|<scope>`) and `gc_depends`.
Removals are tombstone props.

**Traces.**
- ops: mount, unmount, replace, reconfigure, publish, refuse, restore, reincarnate, switch_profile, apply, rollback,
  command
- by: `designer | studio | agent:<entrance> | profile:<id>`
- correlation: the OperationId or request_id
- playtest commands are sampled and bounded; there are no per-tick traces

**Derived kinds** are optional and only for sharing between workers. Each kind has exactly one owner
(`etos/crates/etrg/src/derived.rs:163-188`). No flow into the Studio depends on them.

**Invariants** run as SQL in the gateway through `POST /query` over its own states, after each publish. Apps cannot
declare RG invariants. Examples:
- every required `gc_requires` has a visible `gc_provides` at the install's scope
- every `gc_install` points at an Admitted block
- every authoritative `gc_schema` has a codec
- no two Admitted blocks own the same slot

**Never in the RG:** source code, checkpoint bytes, binaries. The RG holds only commit, path, uri and sha256.

**The "type system near the root" example** is visible as `gc_typesystem` plus `gc_constrains` edges, and as
`governing_typesystems` on each `gc_scope`. The mech agent reads the effective profile of scope S and generates
conforming code against that type system's `effective_interface_hash`.

---

## 9. Kernel changes (proposed ADRs)

ADR-017 is taken by the V1 completion decision
(`game_core/docs/game-core/10-decisions-and-open-questions.md:91`). Sizes are S, M, L or XL. None of these is
accepted yet.

| ADR | Decision | Where | Size |
|---|---|---|---|
| 018 | **Code blocks are content-addressed source assets; code is per build, mounting is per scope.** `gamecore.block/1` plus the Unity-free `gcblock gen`, emitting manifest, registration, routes, readers, recipes, codecs, name table, lifecycle hooks, reset stubs and deterministic `.meta`. The Studio build registers every Admitted block. Convert cards first, with parity tests. Amends the wording of P-009 and ADR-014. | content.compiler; new `com.gamecore.blocks`; contracts/Manifest; `emit_generated_catalog.py`; cards Registration/Declarations/Systems; `make_unity_metas.py` | L |
| 019 | **Interface fingerprint, implementation fingerprint and buildId are separate; admission is validated.** Stable ImplementationId; implHash in PackageContentHash; ManifestValidator on production admission; buildId as catalog hash; OwnershipSchedulePipeline Unity-free. | CatalogFingerprint.cs; CheckpointRecords (header); ManifestValidator.cs; CatalogManifestSource.cs; GameCoreApplicationBootstrap.cs:81-87; OwnershipSchedulePipeline.cs; GameCore.Execution.csproj | M |
| 020 | **"Replaceable while the interface matches" is decided per block over effective interfaces, including recipes.** Identical / Additive / MigrationRequired / Breaking with witnesses; recipe mappings; replaces RequireCatalogMatch; TryProvePlannedState accepts declared seeds and deltas. | new Unity-free module in contracts/planning; CheckpointRestorePlan.cs:247-349; CheckpointRestoreExecutor.cs:509-640 | M |
| 021 | **Every authoritative datum is a declared slot or declared component, and both are checkpointed.** `CheckpointRecordKind.ComponentState = 13` (target, schema@ver, presence, enableable bit, canonical bytes, TargetRef remapped); header count plus a required-feature id; generated per-schema codecs moved out of the validation project. | CheckpointFormat.cs:32-77; CheckpointRecords.cs:98-110; UnityCommittedBoundaryReader.cs:225-245; Gc018CheckpointCodecs.cs | L |
| 022 | **A restored world continues its logical step, time debt, domain time and issuer sequences.** A `RestoredTemporalOrigin` in WorldCreateRequest, applied before PublishInitialAssembly; adapter frames resume from RequestLedger high-water marks. | WorldHost.cs:184,496-513; CheckpointRecords.cs:40-60; RequestLedger.cs:364,588; WorldAdapterFrame.cs:41,110 | S–M |
| 023 | **Restore is production code: batched, recipe-aware, migrating, and targeting the current design.** One manifest-driven restore builder in unity.runtime; batched composition restore; executable forward-only migrations; rebase restore with removal fallbacks; onRestored hooks. | CheckpointMigrationPlan.cs; MigrationScratch.cs; new builder replacing Gc018Scenario/Gc027RestoreBuilder paths | XL |
| 024 | **Block swap is one atomic composition edit, and proposals may carry several edits.** `InstallReplace(A→B)` with a slot/component mapping and Transfer; multi-edit proposals. | CompositionEditPayload.cs; CompositionEditApplier.cs:170,703-775; ActivationLedger.cs; OwnerTransferValidator.cs | L |
| 025 | **An edit is refused before its lane commits if any world would refuse it.** A composite `ICompositionEditValidator` with structured witnesses; derive-and-plan validation hands the prepared plan to the bridge. Closes the "lane published, world refused" state. | CompositionChangeSet.cs:438-481; CompositionHost.cs:184-196; WorldCompositionBridge.cs:355-395; DerivedAssemblyPipeline.cs; AssemblyPublisher.cs | L |
| 026 | **Type systems constrain installations and data at and below their scope.** ConformanceProfile, a ScopedConformanceValidator with refine-only nesting, and refusal of mounts into governed scopes the block was not compiled against. | contracts (declaration); unity.runtime/Integration (validator) | L |
| 027 | **Declared access is checked at compile time and verified at run time.** An analyzer that only reports diagnostics; an L4 write oracle; a sampled Studio oracle, where a violation is a fail-stop plus quarantine. | new `com.gamecore.blocks.analyzers`; guarded dispatch; OwnershipSchedulePipeline.cs:264-299 | L |
| 028 | **Configuration is a derivation input.** Derivation binds ConfigDocument fields into the rule payload on mount and reconfigure. This is V1-normative (O-05, P-029, `07-reference-compositions.md:100`) but unimplemented: derivation copies the manifest's frozen payload. | DerivationEngine.cs:~456-462; DerivationChangeSet.cs | M |
| 029 | **Design edits carry explicit revisions and are not logical steps.** The bridge takes a caller-supplied expected revision (today it uses `Composition.Committed.Revision`, `WorldCompositionBridge.cs:266`) and stops calling `NotifyCommandAdmitted(1U)` for composition-only edits (`:395`). | WorldCompositionBridge.cs:266,395 | S |
| 030 | **Who may command which target is a kernel admission rule.** A route may declare RequiresControlGrant; the plane checks IssuerId against grants derived from ProfileBinding. | WorldMessagePlane.cs:307-380; route declarations | M |
| 031 | **A code reload continues the session through a checkpoint** (amends 04 §9). Studio RootFactory with the real buildId; capture before compile; reload journal; one compile then Play Mode without a domain reload; SubsystemRegistration resets; fallback detection with auto-revert; rolling last-good checkpoints; the Editor settings contract; a long-running IL2CPP player restarting from a checkpoint. | GameCoreApplicationBootstrap.cs:44-87; GameCoreApplicationReset.cs; WorldRecovery.cs; 04-unity-integration.md:189-195 | L |
| 032 | **New data and visuals for known schemas are live assets, not builds.** Content-addressed definition documents resolved from DefinitionRef through IAssetBackend; presentation asset keys; narrative moves into packs. | contracts (DefinitionRef); UnityResourcesAssetBackend; NarrativeChapters.cs | L |
| 033 | **Play state is separable from design state.** The checkpoint gets a design section, a play section and a profile header (profile id, playthrough id, designRevision, the designRevision → CompositionRevision map, issuer high-water marks). | CheckpointDocument.cs; CheckpointRecords.cs | M |
| 034 | **Qualification of a build is keyed by buildId and per-block hashes.** Gates pin each block's effectiveImplHash and the buildId; Studio projects stay outside `game_core/unity`; protocol text amended (P-009, 04 §8/§9, deferred-scope "hot replacement"). | readiness_data.py:207,225; check_package_metadata.py; check_link_xml.py; build_probe.sh; 00-core-protocols.md; 04-unity-integration.md; deferred-scope.md | M |

---

## 10. Edit loops and latency

**Latency sources:**
- IL2CPP numbers come from `game_core/artifacts/performance/summary.md` and
  `game_core/artifacts/gc-030/player-build/build.log`.
- Editor numbers come from batchmode logs on myubuntu.
- Nothing has been measured in an interactive Editor.

| Edit | Path | Latency | Session continuity |
|---|---|---|---|
| Switch profile within a playthrough (Level 1) | Studio only: active issuer, bindings, camera, visibility filter | < 1 frame + ~250 ms viewer RTT (**MEASURED** RTT) | Same world, no state change |
| Place, move or remove a known recipe or presentation key | Tier 0: validate-before-commit → publish with explicit revision → commit `tree.json` on Executed | Prepare ~0.1–0.2 s at ~1k targets, 0.86–1.12 s at 10k; apply 0.6–2.2 ms (**MEASURED**, IL2CPP). Editor **UNMEASURED** | Live, same WorldId; no logical step after ADR-029 |
| Adjust a parameter or formula constant | Tier 0 InstallReconfigure (O-05) + ADR-028 | About one prepare (**ESTIMATE**) | Live. Before ADR-028 the edit is accepted but has **no effect** |
| Check curves | Pure Rules formula over ranges + per-profile traces | < 100 ms (**ESTIMATE**) | No world change |
| Edit narrative content | Content pack (ADR-032) → validate → reconfigure | ~1 s after validation; agent authoring takes minutes | Live; Tier 2 until ADR-032 |
| Import a new 2D/3D asset | Inbox → verify → publish → import; Tier 0 if bound to a presentation key | Seconds to import; generation takes minutes | Live for presentation-key assets |
| Mount or unmount an Admitted block | Tier 1 (O-03, O-07) with the composite validator | About one prepare | Live, same WorldId |
| Swap A→B (both Admitted, compatible) | `InstallReplace` in one atomic proposal (ADR-024) | About one prepare | Live, state moved by mapping |
| Replace an implementation, identical interface | Agent task + staging (asynchronous, 1–3 min **ESTIMATE**) → Apply ([§4.11](#411-rebuild-and-restore-flow-tier-2)) | Target ≤ 60 s. **MEASURED** parts: compile 3.4–11 s, asset refresh ~10 s, domain reload p50 2.7 s / p90 8.7 s. Restore **UNMEASURED** | Same state, step, time and issuer sequences; new WorldId |
| Add a block or change an interface (Additive/Migration) | As above, plus aggregation, seeds, recipe mappings and migrations | 30–90 s (**ESTIMATE**) | Carried state migrated or seeded; Breaking refused before reload; rollback only to the pre-Apply checkpoint |
| Mount or replace a type system | staged → conform descendants → enforced → one Tier 2 Apply | Agent minutes to hours; Apply ~1–2 min (**ESTIMATE**) | As Additive, with migrations |
| Switch playthrough (Level 2) | Warm: one multi-edit catch-up; Cold: batched rebase restore | Warm ~0.1 s + one prepare; Cold: one restore (**UNMEASURED**) | Each playthrough resumes its own state |
| Publish for IL2CPP | Asynchronous publish lane | IL2CPP stage 87–160 s full, 22–58 s incremental; ~4 min end to end (**MEASURED**) | Does not touch the Studio session |

---

## 11. First slice

**Goal:** one block, one agent, one rebuild, and the same game afterwards, including the same step and time. The
slice proves the core claim end to end on myubuntu, with zero etos core changes and with unapproved agent code
confined to gcstage.

**Scope:**

1. **Host (configuration only, after owner approval):**
   - create the users `etos` and `gcstage`, the polkit rule and the `gc-stage@` template
   - build etos `e99870d` (or the refactored pin) into `/opt/etos`, plus the static musl `etos`
   - run etosd as `etos` with `[node] local=true`, broker on `172.17.0.1` and API on `127.0.0.1:7410`
   - install the `studio` app plus `studio-mech` through `install-agents.sh`
   - write `models.toml`
   - build the gc-base and gc-mech images
   - build a minimal studio-gateway:
     - git smart-HTTP with per-identity ref rules
     - the Desk backend with deadlines, attempt limits and task show/cancel
     - one gcstage slot
     - an RG bridge for gc_build, gc_block, gc_install, gc_scope, gc_schema, gc_recipe, gc_request, gc_verdict and
       gc_refusal
   - create a cards Studio project outside `game_core/unity`, with the Editor settings contract
   - use x11vnc for viewing
2. **Kernel, on a `v1x` branch:**
   - ADR-018-lite: cards only
   - ADR-019
   - ADR-020: Identical and Additive classes, with recipe mappings
   - ADR-021: cards components
   - ADR-022
   - ADR-023-lite: production restore for cards, batched, with recipe deltas and no migrations
   - ADR-025-lite: derive-and-plan before the lane commits
   - ADR-031-lite: capture before compile, reload journal, automatic re-entry without a domain reload, fallback
     detection, last-good checkpoint
3. **Studio:**
   - cards playable in the Game view (minimal UI Toolkit), with two seats and Level-1 seat switching without grants
   - an "Ask mechanic" panel
   - Agent Desk cards: queued, running, result, error, cancelled
   - Apply, with a progress overlay

**Demo scenarios:**
- (a) An implementation-only change: "make the set bonus escalate".
- (b) An additive change: a new "streak" block mounted at a league scope. It adds a slot, and a new seat-recipe
  component with a declared seed.
- (c) A deliberately Breaking change: an owned slot is removed.
- (d) A fault injected after Apply.
- (e) A conflict with a Dormant block.
- (f) A request that is never green.
- (g) A Tier 0 edit that the world refuses.
- (h) A staged test that tries to escape the sandbox.

**Success criteria** (at most 2 runs each):

| Id | Criterion |
|---|---|
| SC1 | **Parity.** The generated cards manifest, registration, routes, readers and recipes are semantically equal to today's. The existing cards conformance and EditMode tests pass on the generated artifacts. Regeneration is byte-identical, including `.meta`. |
| SC2 | **Agent loop.** One request opens one fresh conversation, with `/inputs/gc-task.json`. One task runs, with at least one subagent, and busy spawns are retried. Git fetch and push go through the broker, and the token never appears in the transcript. A push outside `agent/mechanic/**` is refused. The Studio re-verifies the commit's hashes and the recorded verdict. |
| SC3 | **Continuity (a).** After Apply, the canonical hash of carried records (slots, ComponentState, RNG, clock) equals the pre-capture hash. LogicalStep, TimeDebtTicks and DomainSeconds are equal, and the WorldId is new. The next seat command is accepted. CardTableModule is re-attached. The same hands, scores and active seat are visible, and only the intended rule effect differs. Exactly one domain reload happens. |
| SC4 | **Additive (b).** All prior rows are Retained. Seats restore through the recipe mapping with only the declared seed rows added. The planned-state proof passes. |
| SC5 | **Breaking (c).** Refused before any reload, with a structured witness shown in the window and recorded as `gc_refusal`. The old build keeps running. |
| SC6 | **Rollback (d, e).** A fail-stop within N steps reverts, recompiles, restores the pre-Apply checkpoint, reports the lost play and marks the block Quarantined. For (e), the L1 full-set dry run refuses before Apply. If it is forced through, `FallbackCount > 0` is detected and reverted. |
| SC7 | **Two speeds (D2).** While the agent works and staging compiles, the Game view keeps pumping, and the Studio log shows zero compiles or reloads. |
| SC8 | **Isolation (h).** A staged test that reads `/var/lib/etos`, worker keys or the Studio working copy, or opens a socket, fails with a red verdict naming the violation. |
| SC9 | **Deadline (f).** The request is cancelled at its deadline, the Desk shows a terminal card, and the next queued request starts. |
| SC10 | **Divergence (g).** The refusal happens before the lane commits. `tree.json` is unchanged, no RG trace is written, and the world can still be captured. |
| SC11 | **RG.** `GC-RESULT` cites the `effective_interface_hash` it queried. The bridge emits `op=apply/replace` with correlation = request_id. A worker's `query` returns the Studio's rows. |
| SC12 | **Latency.** Recorded from the Apply click until play resumes, for (a) and (b). Target ≤ 60 s for (a). |

**Non-goals:** the IL2CPP publish lane (run once by hand), type systems, the analyzer beyond L0 text rules, the write
oracle, Level-2 profiles, migrations, and the art, narrative and tuning agents.

---

## 12. Roadmap

| Phase | Content |
|---|---|
| **R0: host bring-up and measurements** (configuration only, owner approval) | Resolve the owner decisions in [open-problems §A](open-problems.md#a-owner-decisions). Re-verify etos after the SDK refactor (§B). Users `etos`/`gcstage`, polkit, the `gc-stage@` template. Build the pinned etos into `/opt/etos`; never touch `myubuntu:~/wkspace/etos`. etosd as `etos`; `models.toml`/`ops.toml`; gc-* images; gateway skeleton with git smart-HTTP and bearer grants; Studio and staging-slot templates outside `game_core/unity`; x11vnc now, Sunshine/Moonlight later. Measure once (≤ 2 runs): Mono Editor prepare, restore, compile and reload, Play Mode entry without a domain reload, viewer latency. Confirm the etos behaviours in [open-problems §C](open-problems.md#c-etos-behaviours-to-confirm-at-r0). |
| **R1: first slice** | [§11](#11-first-slice). |
| **R2: Tier 0 loops and Level-1 profiles** | ADR-028, 029, 030, 032. `com.gamecore.profiles` Level 1 with ProfileAdapterFrame and issuer continuity. In-window tools: place asset, formula editor, curves, narrative editor. The narrative, art2d and tuning agents, with the inbox/publish pipeline and `gc_proposal` re-publication. Convert narrative and traversal to blocks. |
| **R3: type systems and enforcement** | ADR-025 in full, 026, 027 (analyzer plus L4 write oracle), a type-system block template, ADR-024 (InstallReplace and multi-edit), the types agent and cascade planning. |
| **R4: migrations and playthroughs** | ADR-023 in full (executable migrations, rebase restore), ADR-033, Level-2 profiles (warm cache, cold rebase), the qa and art3d agents, oracle scenarios feeding L5. Optionally move the pure prepare phase off the main thread. |
| **R5: publish, requalify, harden** | ADR-034; the IL2CPP publish lane per buildId with a mount/replace probe; a long-running player restarting from a checkpoint; protocol amendments (P-009, 04 §8/§9, deferred-scope); a V1.x qualification run on myubuntu; optional `studio`-user hardening. |

---

## 13. Risks

| Risk | Mitigation |
|---|---|
| **Silent semantic drift.** An agent keeps the effective interface identical while changing meaning, and Identical carries state by default. | The L5 replay oracle and a diff shown to the designer are required. The oracle is exact only for integer and fixed-point determinism (P-008), so staging and the Studio force synchronous Burst. |
| **State loss before ADR-021 and ADR-022.** A cards-style block resets its private components on every rebuild, and every restore restarts domain time at 0. | Both ADRs are in the first slice. |
| **Recipe churn.** A changed recipe revision makes existing targets unrestorable. | ADR-020 mappings; the generator keeps revisions stable. |
| **Lane/world divergence.** A world-side refusal after the lane publishes leaves the world unable to take edits or be captured. | ADR-025; commit `tree.json` only on Executed; last-good checkpoints and play journals. |
| **Lossy rollback.** Migrations are forward-only. | Restore the pre-Apply checkpoint; the Studio says what play was lost. |
| **Dormant co-validation.** A new block conflicting with a Dormant one makes the bootstrap silently fall back to an infrastructure-only world (`GameCoreApplicationBootstrap.cs:47-56`). | L1 checks the full set; the Reload Controller treats a fallback as a failed Apply; Retire unused blocks. |
| **Trust.** Approved code runs root-equivalent; the Unity licence may need network in the sandbox; workers have sudo in containers; egress is proxy-only. | [§7.9](#79-security-and-trust); `studio`-user hardening in R5. |
| **etos churn.** etos is pre-1.0 and its SDK is being refactored now; binding evolution and retention are undecided upstream. | `etos.lock` plus the contract check; re-verify [§7](#7-transaction-agents) and [§8](#8-rg-bridge) on every pin change. |
| **RG is one-way for apps.** | Everything returns through `GC-RESULT` or the inbox and is re-published by the gateway. |
| **Shared host.** k3s, helix and Postgres compete with the Editor, staging and IL2CPP builds (20 cores, 31 GB). | nice/CPU quotas on staging and publish; owner decision on throttling (open-problems A2). |
| **Remote play latency (~250 ms).** | Fine for turn-based games; action games need the Mac-local player or a LAN machine (open-problems A6). |
