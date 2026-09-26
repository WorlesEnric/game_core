# GameCore V1 operator guide

This directory is the operator contract for the implemented GameCore V1 runtime: how to build it, how to run
it headless, how to regenerate and verify its catalogs, what every failure code means and what to do about
it, and exactly which behaviour is **not** supported.

It describes what this repository has evidence for. Where something is qualified on one profile only, or
deferred, this guide says so plainly rather than implying broader support.

## Read this first

| Page | What it answers |
| --- | --- |
| [profile.md](profile.md) | What the single qualified target is, and the complete list of unqualified ones. Read before making any portability claim. |
| [build-and-run.md](build-and-run.md) | Clean-checkout reproduction: `tools/reproduce.sh`, the toolchain prerequisites, and every step's command. |
| [packages.md](packages.md) | The local package/assembly contract: names, versions, dependencies, and the engine-free kernel rule. |
| [headless.md](headless.md) | Headless startup and shutdown, the production entry points, and the complete player command-line surface. |
| [catalog-generation.md](catalog-generation.md) | How the committed catalogs are generated and how byte-identity is proven. |
| [failure-codes.md](failure-codes.md) | **Generated from source.** Every protocol operation code, build-time catalog code and narrative refusal, with meaning and operator action. |
| [checkpoint-and-recovery.md](checkpoint-and-recovery.md) | Checkpoint capture, restore, and recovery of a faulted world. |
| [unload-and-leaks.md](unload-and-leaks.md) | Unload/teardown and how to read the resource and job ledgers for leaks. |
| [editor-hang.md](editor-hang.md) | The known intermittent Unity Editor pre-dispatch hang: symptoms, diagnosis, retry policy. |
| [deferred-scope.md](deferred-scope.md) | Required V1 scope versus deferred work, including the owner-deferred performance timing qualification. |

## The one-line version

```sh
UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=$HOME/.dotnet/dotnet tools/reproduce.sh
```

From a genuinely fresh clone that checks the toolchain, restores and verifies packages, proves catalog
byte-identity, builds and tests the dotnet solution, runs the Unity suites, builds both players, runs the
family probes twice, and validates the documentation. See [build-and-run.md](build-and-run.md).

## Status vocabulary used throughout

| Term | Means |
| --- | --- |
| **Qualified** | A player was built and executed and the result was archived. |
| **NotRun (pending orchestrator build host)** | The step has not been executed on the machine that produced this guide. Nothing in this directory may be read as a passing build or test result unless it cites archived evidence. |
| **Deferred (owner decision)** | Explicitly removed from V1's required scope by the project owner, with the decision recorded. Never a synonym for Pass. |
| **Unqualified** | No evidence exists. Not a claim that the code is wrong there — a claim that nothing has been demonstrated. |
