# GameCore Studio — AI-native game creation environment

**Status: in implementation (started 2026-10-04).** GameCore Studio is a Unity Editor extension, a companion
etos agent, a reusable gameplay plugin library and a reference game, built on the GameCore V1 kernel and the
etos v2 platform. This directory is the product's design and delivery set. The kernel's normative protocol remains
[`../game-core/00-core-protocols.md`](../game-core/00-core-protocols.md).

| Document | Holds |
|---|---|
| [01-gap-assessment.md](01-gap-assessment.md) | Requirement-by-requirement assessment (SR-x.y), source-cited status, prerequisites found |
| [02-architecture.md](02-architecture.md) | Product shape, ownership boundaries A–E, layout, data flows, commit boundaries, identity map, decision records SADR-001..020 |
| [03-authoring-contracts.md](03-authoring-contracts.md) | AuthoringRef, selection, semantic index, authoring metadata, tools, change sets, concurrency, staging, diagnostics |
| [04-etos-integration.md](04-etos-integration.md) | Roles and keys, Unity↔companion protocol, task lifecycle mapping, workers, media/voice, staging, RG publication, host setup |
| [05-plugin-catalog.md](05-plugin-catalog.md) | The twelve capability groups: packages, slots, commands, binders, definitions, tools, acceptance; reference game content |
| [06-implementation-plan.md](06-implementation-plan.md) | Packets, owners, exclusive paths, dependencies, waves, integration checks |
| [07-verification-matrix.md](07-verification-matrix.md) | Budgets, evidence conventions, every verification row, platform matrix |
| 08-creator-guide.md | (W4) how to author, prompt, review, save, build |
| 09-plugin-developer-guide.md | (W4) metadata, tools, definitions, validators, tests, diagnostic codes |
| 10-install-build-run.md | (W4) reproducible installation, build and run |
| 11-completion-report.md | (W4) supported profile and completion status |
| [archive/](archive/) | The paused 2026-09-28 design draft and its open problems (superseded where [02](02-architecture.md) §8 says so) |

Evidence lives under [`../../artifacts/studio/`](../../artifacts/studio/), keyed by verification row.
