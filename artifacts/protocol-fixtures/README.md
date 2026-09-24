# Protocol fixture evidence (GC-002)

Status: **NotRun (pending orchestrator build host)**. Nothing in this directory claims a passing run; no
case has been executed anywhere yet.

## What this directory holds

| File | Produced by | Contents |
|---|---|---|
| `results.json` | `dotnet test dotnet/GameCore.sln -c Release` (`GameCore.ProtocolFixtures.Tests`) | One row per committed fixture case: case id, requirement ids, primary test id, outcome `Pass`/`Fail`/`NotRun`/`Blocked`, and the expected-versus-observed detail. |

`results.json` is **not committed by GC-002** on purpose: committing it before a run would record outcomes
that were never observed. The build host run creates it through
`GameCore.ProtocolFixtures.Fixtures.ResultDocument`, which is also what the test reads back to verify the
document's shape and summary. Once a host has run the suite, that file is the evidence artifact and belongs
in the versioned record.

## Commands that produce the evidence

From the repository root:

```sh
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
dotnet build dotnet/GameCore.sln -c Release
dotnet test dotnet/GameCore.sln -c Release --logger trx
```

`tools/run_w0_checks.sh` runs exactly those four steps and tees each raw log into `artifacts/raw/w0/`
(gitignored by design; summaries and result indexes stay versioned).

## Reading the result document

Case semantics, the per-kind parameter tables, the outcome vocabulary and the source-of-truth rules live in
`tests/GameCore.ProtocolFixtures/Data/result-schema.json` and
`tests/GameCore.ProtocolFixtures/README.md`. In short: `Pass` means the independent oracle agreed with the
case's stated expectation, `Fail` means it disagreed (including a missing or different refusal code),
`NotRun` means unexecuted, and `Blocked` means the case data was malformed. A delivered run must contain
neither `NotRun` nor `Blocked`; the suite asserts that.
