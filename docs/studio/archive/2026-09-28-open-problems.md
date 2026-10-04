# GameCore Studio — open problems

**Status as of 2026-09-28, when the work paused.** The owner is refactoring the etos SDK, and Studio work resumes
afterwards. This file records everything still open. The design itself is in [design.md](design.md).

Legend: **VERIFIED** means checked in code in this session. **REPORTED** means found by a review agent with
citations, but not re-checked by hand.

## A. Owner decisions

These decide the scope of R0 ([design.md §12](design.md#12-roadmap)).

| # | Decision needed | Context |
|---|---|---|
| A1 | **Accept boundary B1?** A block's tree position scopes its data and state, not its code. Replacing a block's implementation changes it everywhere it is mounted. Different behaviour in one subtree means a separate block. | This is an engine fact: one compiled type set and one system instance per world ([design.md §2](design.md#2-code-blocks-as-assets-the-answer)). It is the precise meaning "replaceable like assets in the tree" can have. |
| A2 | **Host changes on myubuntu.** May we: create the service users `etos` and `gcstage`; add a polkit rule and systemd units; install etos under `/opt/etos` with root `/var/lib/etos`; prune the ~280 GB `~/wkspace/gc-wt` worktrees; throttle k3s, helix and Postgres during play sessions? | myubuntu is shared. Nothing has been changed on it yet. |
| A3 | **Unity licence.** Does it allow an interactive Editor (`worlesenric`) and one or two batchmode staging instances under a second OS user (`gcstage`) at the same time? Can licence validation work with no network in the staging sandbox? | The answer decides whether `gc-stage@` can use `PrivateNetwork=yes` or needs an allowlist for the licence hosts. |
| A4 | **Models and providers.** Which model endpoints serve the coding workers (mech, types) and the content workers (`models.toml`)? Which providers handle image and 3D generation (`ops.toml`)? | etos's local profile ships with no models configured. etops 3D is generic "predictions" plumbing only, tested only for the not-configured refusal. |
| A5 | **Spend limits.** etos has no per-task budget, and a worker budget is a lifetime total that cannot be raised. The design uses a per-request deadline and attempt limit in the gateway, per-call `max_cost_usd`, and provider-side caps. What limits do you want? | [design.md §7.7](design.md#77-spend-control) |
| A6 | **Where will you play from?** The Mac↔myubuntu round trip is **VERIFIED ~250 ms**, which is fine for turn-based games. For action games, do you want a frozen Mac-local Mono player (no live tools) or a machine on myubuntu's LAN? | |
| A7 | **Apply policy for agent code.** Must every Tier 2 change wait for your click, or may identical-interface replacements that pass the replay oracle apply automatically while you are idle? | Auto-apply is deferred until proposal validity rates and costs are measured. |
| A8 | **Shipping targets** besides StandaloneLinux64 IL2CPP (macOS, Windows, consoles, mobile)? | The answer decides the publish lanes, and whether blocks default to integer/fixed-point or float determinism. That in turn decides how strong the replay oracle can be. |
| A9 | **Which game first?** Genre, art style, controls, and typical world size. Cards is the recommended first family. | At ≤ 1k targets a provider edit costs ~0.17 s to prepare (derive only); at 10k it costs ~1 s. |
| A10 | **One installed app per agent (8 keys) or one key with `[[api.entrances]]`?** | Per-agent apps allow runtime install and uninstall plus a per-agent kill switch. One key needs an etosd restart on every change ([design.md §7.1](design.md#71-packaging-in-game_core)). |
| A11 | **`@etos/sdk` licence.** It is `private: true` and `UNLICENSED`. Relicense it if game_core should vendor it. | The design's C# client does not need it at runtime. It is only a reference or test oracle. |

## B. etos facts to re-verify after the SDK refactor

[design.md §7](design.md#7-transaction-agents) and [§8](design.md#8-rg-bridge) were written against etos **`e99870d`**.
Each item below is load-bearing. If the refactor changes any of them, update the design and `studio/etos/etos.lock`.

| # | Fact at `e99870d` | Evidence | Design depends on it for |
|---|---|---|---|
| B1 | A transaction agent is one entrance bound to exactly one worker, plus instructions. It contains no etos code. | `etos/crates/etapi/src/config.rs:45-63` (**VERIFIED**) | Packaging in game_core (D7) |
| B2 | `etos app install` takes any directory path. `app.toml` is closed and has no image, network, controller or grants. If the worker is missing, `app install` creates it with the default image and network `open`. | `etos/crates/etcli/src/apps.rs:172-265,314-351` | Install script order; no `[worker]` in agent `app.toml` |
| B3 | No worker-update operation exists on the host socket, so changes require `worker rm --purge` plus create. | `etos/crates/etnode/src/worker.rs:502-520`; `socket/host.rs:25-58` | Agent update path |
| B4 | Entrance tasks get no repository (so no `etos deliver`) and no host mounts. | `etos/crates/etnode/src/endpoint.rs:440-444,457` (**VERIFIED** :440) | Git over gateway smart-HTTP; inbox for artifacts |
| B5 | The entrance preamble, up to 4000 bytes of history on follow-ups, and attachment limits (16 files × 16 MiB, 48 MiB body). | `etos/crates/etapi/src/entrance.rs:678-690,786-800`; `config.rs:85-100` | Fresh conversation per GC-TASK; envelope as a file |
| B6 | The entrance's `transcribe`/`ocr` conversion is refused for container workers, because the entrance actor has no task. | `etos/crates/etapi/src/entrance.rs:100-101`; `etos/crates/etnode/src/services.rs:103-116` (code reading only) | Entrances set both flags false |
| B7 | Output-side modality actors (design-agent §11) are design only. Generation is `etos generate …` from the worker shell (node-side, into `/outputs`) or sys/ops from a task-scoped actor. | `etos/crates/etapi/src/entrance.rs:571-591`; `etos/crates/etnode/src/socket/task.rs:1083-1122` | The artifact pipeline (D8 correction) |
| B8 | Actors run under bwrap with no network and no file access, so they cannot publish. | `etos/crates/etactor/src/process/confine.rs` | Workers upload, not actors |
| B9 | Broker credential schemes are bearer, token, basic and header only, with no SigV4. Grants match on host only. | `etos/crates/etbroker/src/db.rs:84-114`; `proxy.rs:173-185,684-721` | Gateway inbox with a bearer token; OBS only through the gateway or presigned URLs |
| B10 | Egress `allowlist` binds only proxied traffic; there is no packet filter. | `etos/crates/etnode/src/task.rs:1609-1610` | Trust model |
| B11 | Result = `{pos, at, text, references}`. References carry no bytes and no digest. Pinned-reference fetch is capped at 1 MiB, hard-coded. | `etos/crates/etapi/src/wire.rs:302-316`; `etos/crates/etnode/src/refs/mod.rs:272-285` | `GC-RESULT` JSON in text; inbox for bytes |
| B12 | A worker budget is a lifetime total and cannot be raised. An exhausted task stays Waiting and blocks the queue. There is no per-task budget. | `etos/crates/etnode/src/node.rs:259-274`; `task.rs:1637-1647` | Workers created without `--budget` |
| B13 | Subagents are one level only. The container cap returns `busy` (a refusal, not a queue). Children get no repository. | `etos/crates/etnode/src/endpoint.rs:664-712`; `task.rs:1207-1224` | Retry with backoff; `git clone --reference` |
| B14 | An SDK app sees only its own app states, not derived kinds, `rg.*` tables or violations. Apps cannot declare invariants. | `etos/crates/etsearch/src/catalog.rs:602-628`; `etos/crates/etapi/README.md:192-193` | RG is one-way; the gateway re-publishes accepted proposals |
| B15 | RG links are single-valued, there is no blob type, and everything lives in one SQLite database. Binding schema evolution and retention are not decided. | `etos/crates/etrg/src/types.rs:217-239`; `etos/docs/design/design-rg.md:338-346` | Edge kinds as states; tombstones; additive kinds |
| B16 | The binding digest is sha256 over etcore canonical JSON (sorted keys, no whitespace, serde_json escaping). | `etos/crates/etcore/src/text.rs:57-90`; `etos/crates/etapi/src/bindings.rs:33-41` | C# canonical-JSON writer |
| B17 | `etosd schema` equals the committed `crates/etapi/schema/etapi.json`. The CLI `--json` output has no schema. | `etos/crates/etapi/README.md` | Generated C# DTOs; contract check |
| B18 | etos publishes no binaries or packages. `@etos/sdk` is private and UNLICENSED. The Python `etos` package is not on any index. | `etos/docs/operator.md:14-32`; `etos/sdk/ts/package.json` (**VERIFIED**); `etos/sdk/py/pyproject.toml` (**VERIFIED**) | One-time build per pin into `/opt/etos` |
| B19 | Worker images can come from any Debian/Ubuntu/Alpine/Fedora base; the node adds its own etos layer. | `etos/crates/etnode/src/container/image.rs:1-17,368-383` | gc-base not derived from etos-default |

**Chances the refactor could simplify the design** (the etos owner's call; D5 keeps the core unchanged):
- a typed `Result.data` field
- artifact manifests with digests on the SDK wire
- a published, versioned contract, and SDK packages that are released or licensed
- a worker-update operation
- a per-task budget
- a repository for entrance tasks

Each of these would remove a workaround in [design.md §7](design.md#7-transaction-agents).

## C. etos behaviours to confirm at R0

Code reading could not settle these. Confirm them on a running node before relying on them:

1. An entrance-only installed app plus a pre-created container worker works end to end.
2. A cancelled task produces an Error event on the conversation.
3. Subagent children share the parent worker's broker grant.
4. `git` over `HTTP_PROXY` to `172.17.0.1:7421` works with the `etos-brokered` bearer placeholder substitution.
5. An image sent to an entrance with `ocr=true` is refused for a container worker (B6).
6. A hostless worker's `query` can read the Studio app's states. If it can't, all agents use container workers.
7. Presigned PUTs through the broker's CONNECT tunnel work. This is only needed for the optional large-video path.
8. Partition alignment: with `[node] local=true`, a worker's `query` returns the gateway's `studio` rows.

## D. Kernel problems found

The Studio design fixes each of these through an ADR. Several are **V1 gaps that matter even without the Studio.**

| # | Problem | Status | Fix |
|---|---|---|---|
| D1 | **Config never reaches derivation.** `07-reference-compositions.md:100` specifies a live reconfigure (+2 → +4, scores preserved). Derivation instead copies the manifest's frozen rule payload and never reads config. The review reports that the conformance test fakes the reconfigure with unmount plus remount. **This is a V1-normative behaviour that is unimplemented, even though V1 is declared complete.** | **VERIFIED** (derivation), **REPORTED** (conformance) | ADR-028 |
| D2 | **The production update path has never driven a real family world.** `GameCoreApplicationPump` calls `host.PumpFrame` directly. `WorldTimeDriver` (input cutoff, plugin clocks) is constructed only in fixtures, tests and validation scenarios. The only `RootFactory` is the validation probe's (`ProbeWorldDispatch.cs:32`). There is no long-running application root. | **VERIFIED** | Studio RootFactory (ADR-031); reconcile the pump with WorldTimeDriver |
| D3 | **Checkpoints capture only int32 slot rows,** so a family's private components are lost on restore. | **VERIFIED** | ADR-021 |
| D4 | **A restored world restarts at step 0 with zero time debt.** Captured temporal fields are never applied. | **VERIFIED** | ADR-022 |
| D5 | **Stale proposals apply silently and edits consume a step.** `WorldCompositionBridge` uses `Composition.Committed.Revision` as the expected revision, and every composition edit calls `NotifyCommandAdmitted(1U)`. | **VERIFIED** | ADR-029 |
| D6 | **A world can refuse an edit after the lane has published it.** It then cannot take new edits or be checkpointed. | **REPORTED** | ADR-025 |
| D7 | **A changed spawn-recipe revision makes existing targets unrestorable.** | **REPORTED** | ADR-020 recipe mappings |
| D8 | **Checkpoint migrations are planned but not executable.** Both restore builders refuse migrations. | **REPORTED** | ADR-023 |
| D9 | **ManifestValidator is only called from dotnet tests,** and every production manifest passes `ContentHash.Empty` (which ManifestValidator rejects). | **REPORTED** | ADR-019 |
| D10 | **The seat is named in the command payload and nobody checks it** (`CardTableSystems.cs:539,571`). | **REPORTED** | ADR-030 |
| D11 | **Unguarded host calls.** `RequestWake`, `Submit` and `ReadResourceLedger` have no main-thread guard. `RequestWake` has no lifecycle check and can raise `pendingDemand` on a Faulted world. | **REPORTED** | Guards in the Studio root work |
| D12 | **A composition defect with a Dormant block makes the bootstrap silently fall back to an infrastructure-only world.** | **REPORTED** | L1 full-set dry run; the Reload Controller treats a fallback as a failed Apply |
| D13 | **ProbeCatalog ids are not stable-name-derived** (3 of 4 committed ids). Auto-deriving them would re-key content. | **REPORTED** | Lint warns only; grandfather ProbeCatalog |
| D14 | **Observation data is mostly empty.** Snapshot state is only a 32-byte step fingerprint, and provenance/explain is wired only in tests. | **REPORTED** | Studio root owns ProvenanceStore; per-family readers |

## E. Measurements not taken

All of these are single measurements with at most 2 runs, per the no-full-benchmarks rule. None has been taken.

- Tier 2 end to end, from Apply click to play resumed (target ≤ 60 s). Also restore time, and Play Mode entry
  without a domain reload.
- Mono Editor prepare cost for Tier 0/1 edits (only IL2CPP numbers exist).
- Checkpoint capture latency, and single-spawn latency.
- Viewer latency: VNC compared with Sunshine/Moonlight, over the measured 250 ms path.
- etos agent turn timings, the rate of schema-valid `GC-RESULT`s, and cost per proposal.
- Media provider behaviour. It has only been exercised against mocks.
- Whether Unity's stock Entities windows show the GameCore world. This is inferred, not observed.

## F. Not yet designed

- **The player-window UX:** interaction flows, cognitive load, how four graphs (scopes, installs, manifests,
  contributions) are shown, and how the human, who is the single approver, integrator and build babysitter, stays
  on top of the work.
- **The target game itself:**
  - genre, art style and controls (no Input System package is pinned)
  - text rendering and localization
  - audio, which is disabled project-wide after crash-139 and needs its own qualification
- **Branching timelines in the RG.** Restore and fork create new WorldIds, and the RG timeline has no branches.
  The key scheme for them is untested.
- **The approval loop over etos conversations.** `ask` has no typed event, and a request after a Result opens a new
  task. The design avoids `ask` entirely; the inbox's asked, rejected and re-ask cycle is unspecified.
- **Multiple developers.** etos models one human per instance.
- **What makes development "a game":** goals, such as oracle rows as quests or balance targets as win conditions,
  versus only feeling playful.

## G. Carried over from V1

- The TEST-023 full timing qualification is deferred by owner decision (ADR-017).
- Whole-world prepare costs ~0.9–1.1 s per change at 10k targets, against a 100 ms target
  ([`artifacts/performance/BUDGET_DECISIONS.md`](../../artifacts/performance/BUDGET_DECISIONS.md)).
- The batchmode Unity Editor has an intermittent pre-dispatch hang on myubuntu
  ([`docs/operator/editor-hang.md`](../operator/editor-hang.md)). It matters for gcstage staging.
- The GC-020 contact observations gap.
- Baking is definition-level, not prefab.

## H. Environment notes

- `myubuntu:~/wkspace/etos` is a **different** etos codebase (`5485c0c`, codeup.aliyun remote, has a `v2/`
  folder). The etos to deploy is the Mac checkout, currently `e99870d`, or the refactored pin. Never build from the
  myubuntu checkout.
- The Mac has no Unity or .NET. All builds and the Studio run on myubuntu.
- This design session wrote no code. It left only `docs/studio/` in game_core. It made no changes on myubuntu or in
  etos.
