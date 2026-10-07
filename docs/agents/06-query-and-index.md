# 06. Query and index publication

The Editor's semantic index is published into the node's resource graph as owner-scoped `gc_*` records, so a worker can run `etos query` over the live project instead of relying on the bounded slice in its inputs.

## Editor side (`EtosIndexPublisher`, added in R8-A)

A full snapshot is posted when the session opens (no `baseRevision`); deltas follow on `Index.Changed`, `Engine.Applied`, and `Journal.Written` in the Applied or Undone states. Posting is rate-limited to one `POST /v1/index/delta` per second with one in flight. The body is `{project, revision, nodes (changed index nodes), edges (full), removals, baseRevision?}`; the companion's acknowledged revision must match or the client reports `index_revision_mismatch` and resends a snapshot.

## Companion ingestion (`index.rs`)

Keys are namespaced `sha256(owner):key` where the owner is the `[app, project]` tuple; a delta without `baseRevision` replaces the owner's snapshot and clears stale sources; removals are logged with `removed: true`. Records are posted through the agent's binding (`PUT /bindings/gamecore-studio`, `POST /bindings/{app}/traces`). Dialogue graphs are expanded into one `gc_dialogue_node` row per node (`graph/<index>`, with graph identity = asset GUID, else authoring id, else definition name) carrying kind, text, speaker, conditions, consequences, options and outgoing edges; definitions become `gc_definition` rows. Both carry an `owner` property.

## What a worker can ask

`request.md` tells the worker the owner hash and shows `gc_definition` and `gc_dialogue_node` query forms. In the P4.2h and R8-A runs a selected-NPC query returned the NPC's 8 nodes and the unfiltered owner query 53, matched against the real asset. Queries are the only way a worker sees anything beyond its uploaded slice; workers are offline otherwise.

Sources: `Packages/com.gamecore.studio.etos/Editor/EtosIndexPublisher.cs:47-123`, `Packages/com.gamecore.studio.etos/Editor/EtosAgentGateway.cs:126,259,268`, `studio/agent/src/api.rs:108,440-458`, `studio/agent/src/index.rs:5-16,77-89,377-500,700-830`, `studio/agent/src/model.rs:304-318`, `studio/agent/src/desk.rs:584-592`, `docs/studio/04-etos-integration.md` §7, `docs/studio/packets/R8-A-publication-cancellation.md`.
