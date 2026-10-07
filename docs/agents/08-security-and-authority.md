# 08. Security and authority

The agents can propose, never act: every mutation of the Unity project goes through the typed change-set engine or the staging lane, and every credential stays with the node.

| Boundary | Rule | Enforced where |
| --- | --- | --- |
| App vs agent keys | Unity holds only the app key (`gamecore-unity`, routes `proxy`, `query`, `changes`, `entrances`). Tasks, files, ops and realtime are agent-only; the companion holds the agent key the node injected and never logs or forwards it. | `studio/etos/app/app.toml`, `agent.toml:3-8` |
| Proxy authentication | The node strips `Authorization`, adds `X-Etos-App` and `X-Etos-Proxy-Token`; the companion compares the token in constant time against its **current** welcome (never cached), checks the app against `allowed_apps`, and requires `X-GameCore-Project` (a sha256) on every route but `/v1/hello` (426 `client_upgrade_required` otherwise). Caller identity is the tuple `[app, project]`, so all ownership checks are per app and project. | `studio/agent/src/api.rs:168-223`, `config.rs:32` |
| WebSockets | A one-time ticket from `POST /api/v1/tickets` per connect; no key on the socket URL. | `Client/CompanionClient.cs:313-347` |
| Input hygiene | JSON containing `null` anywhere is refused; candidate import refuses `null` optional members, enforces `ValidationMode.Candidate`, checks `toolCatalogRevision` (`stale_context`), and verifies artifact sha256 and size before storing. | `api.rs:226-234`, companion `candidate.rs`, client import |
| Edit discipline (D1/D2) | Agents emit change sets over the typed tool catalog; no path lets candidate JSON reach `AssetDatabase` or the file system directly; media imports pass the allowlist (`.png/.jpg/.jpeg`, `.wav/.ogg/.mp3`, `.fbx`) with digest verification. | change-set engine, `MediaImportPolicy.cs` |
| Generated code | Staged and validated in an isolated Docker project with a signed verdict before it can affect the live Editor; admission is an explicit creator action; unrestricted script execution is never a tool. | staging lane (07) |
| Workers | Offline containers (`--network offline`), fixed images, instructions versioned in immutable releases; `release()` refuses a worker budget that differs from the manifest. | `install.sh:186-200`, `install-state.py:208-220` |
| Cost | Every priced op carries `max_cost_usd` (call value, else the configured ceiling, else refused); unpriced ops answer `budget_unpriced`; `describe` is capped locally because the provider input has no ceiling field; charges are recorded by idempotency key. | `ops.rs:318-359`, `pricing.rs:74-129` |
| Redaction | Keys matching `etk_|ett_|etp_|eta_|Bearer` are redacted in logs, UI and exceptions; the settings file `UserSettings/GameCoreStudio.json` never serializes a key. | client logging and settings code |

What the agent cannot do by construction: change the app or its pairing, reach a provider without a priced tariff, write to the live project, run a stage outside Docker, or restart `etosd`.

Sources: `studio/agent/src/api.rs`, `studio/agent/src/ops.rs`, `studio/agent/src/pricing.rs`, `Packages/com.gamecore.studio.etos/Client/CompanionClient.cs`, `Packages/com.gamecore.studio.core/Editor/Tools/BuiltIn/MediaImportPolicy.cs:32-34`, `docs/studio/04-etos-integration.md` §1 and §9.
