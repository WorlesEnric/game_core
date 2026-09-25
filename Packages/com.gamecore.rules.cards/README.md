# com.gamecore.rules.cards — the pure card-table rules (GC-011)

Normative sources: [`07-reference-compositions.md`](../../docs/game-core/07-reference-compositions.md) §2 and
§10.4, [`05-contracts-and-data-model.md`](../../docs/game-core/05-contracts-and-data-model.md) §2, §3 and §6,
[`00-core-protocols.md`](../../docs/game-core/00-core-protocols.md) P-004, P-005, P-008, P-009, P-019.

One assembly, Unity-free:

| Assembly | Sources | Platform | Engine |
|---|---|---|---|
| `GameCore.Rules.Cards` | `Runtime/**` | every platform | none (`noEngineReferences: true`) |
| `GameCore.Rules.Cards.Tests` | `Tests/**` | Editor only (`includePlatforms: ["Editor"]`) | `UnityEngine.TestRunner` for NUnit only |

No file here references `UnityEngine` or `Unity.*`, so the same sources build inside Unity and under the plain
.NET SDK. The only referenced assembly is `GameCore.Contracts`.

## What the slice is

The card table is one owner that settles several entities atomically (07 §2.2): the table entity and the seat
entities span several scopes, and `cards.commit` "rechecks table version, reserves output capacity, then writes
all accepted changes before observation is allowed". This package therefore never writes as it goes. It
validates one request, builds one bounded write set, and either commits that whole set or returns
`CardWriteSet.Empty` — a rejected command changes no entity, and a committed set moves the cards, the score and
the table version together or not at all.

## Public surface

| Type | Role |
|---|---|
| `CardIdentity` | stable name → typed identity, all derived with `StableNameKeyDerivation.Derive` (SHA-256 over UTF-8, first 16 digest bytes as two big-endian words) |
| `CardVocabulary` | the card market's stable names of 07 §2, byte-identical to the GC-006 fixture `CardComposition`, plus the derived `SetBonusSlot`/`SetBonusContract`/`BonusReducerKey`/`AlwaysPredicateKey` statics |
| `CardCommandKind`, `CardSettlementStatus`, `CardWriteKind` | the command, outcome and write vocabularies |
| `CardId`, `SeatOrdinal`, `CardWrite`, `CardHand` | the pure values a settlement reads and produces |
| `CardSettlementRequest`, `CardContestRequest`, `CardContestResolution` | one admitted command, one contest, one decision |
| `CardWriteSet` | the immutable, bounded, canonically ordered effect of one command; only this assembly can construct one, and `Empty` is the rejection result |
| `CardSetRules` | the rules: set score, bonus reduction, settlement and contest resolution |
| `ICardBonusReducer`, `CardSetBonusReducer`, `ICardTargetPredicate`, `CardAlwaysPredicate` | the `FactoryKey`-keyed registration seam a generated catalog binds (P-009) |

Semantics that callers depend on:

- `TryBuildSettlement` checks, in order: the expected table version, the hand's seat, the candidate bound, then
  the command's own rules. `SubmitSet` requires exactly `SetCardCount` distinct held cards and the seat's turn;
  `Transfer` requires exactly one held card and a receiver below `MaxHandCards`; `Contest` is never settled here —
  it is resolved by `TryResolveContest`.
- A committed `SubmitSet` writes the removals in ascending `CardId` order, then the score, then the table
  advance. A committed `Transfer` writes remove, add, advance. The order is canonical: it does not depend on how
  the rules or the candidate list were written (P-008).
- `TryResolveContest` picks the candidate with the smallest `(SeatOrdinal, sequence)` ascending total order, so
  the winner is independent of candidate array order; a rejection returns a default resolution.
- `SetScoreDelta` is `BaseSetScore + effectiveBonus` and throws on overflow rather than wrapping. `TryReduceBonus`
  is an ascending-index, overflow-checked fold that reports failure instead of exposing a partial sum.

## Bounded-work constants

| Constant | Value | Meaning |
|---|---|---|
| `CardSetRules.SetCardCount` | 3 | cards one accepted set plays |
| `CardSetRules.BaseSetScore` | 10 | score of a valid set before the bonus (07 §2.3) |
| `CardSetRules.MaxHandCards` | 8 | hand capacity; a transfer into a full hand is rejected |
| `CardSetRules.MaxCandidates` | 4 | a request carrying more candidates is refused, not truncated |
| `CardSetRules.MaxWriteEntries` | 12 | a settlement whose effect exceeds this is refused, not truncated |

Every refusal is a `CardSettlementStatus` value: the caller never receives a partial write set, a truncated
candidate list or a wrapped score.

## Build and test

The package's own sources are built and tested by the plain-dotnet projects
`dotnet/src/GameCore.Rules.Cards` and `dotnet/tests/GameCore.Rules.Cards.Tests`, which compile
`Runtime/**` and `Tests/**` respectively. Run from the repository root:

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test dotnet/tests/GameCore.Rules.Cards.Tests/GameCore.Rules.Cards.Tests.csproj -c Release
```

The same test sources run as Unity EditMode tests through `Tests/GameCore.Rules.Cards.Tests.asmdef`. The identity
test asserts the SHA-256 literals it computed independently, so a drift in a stable name or in the derivation
fails there rather than silently changing the market's identities.
