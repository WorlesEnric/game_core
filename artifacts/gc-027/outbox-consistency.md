# GC-027 outbox consistency reports

**Status: `NotRun (pending orchestrator build host)`.** Every row is a template; nothing here is a run result.

**Which families this covers.** The narrative slice and the card market, which declare a delivery obligation. The
traversal course declares none (`HasDeliveryObligation` is false: it has no outbox and no external effect), so it
records no row here and no delivery observation; its recovery evidence is the engine-physics and authoritative-state
observations in `recovery-behavior-and-data-loss.md` §3.1.

## What is reported, and by what

`GameCore.Execution.Recovery.OutboxConsistency.Verify(rows, liveOutbox, label)` is the census a recovery is judged
by. It never delivers, never settles and never mutates: a report that moved an obligation would be the hidden replay
P-049 forbids. Its `Describe()` is embedded in two places:

1. the **recovery observation** `gc027-outbox-rows-and-delivery-cursor-survive-the-recovery`, which runs it against
   the recovered session's live outbox; and
2. the **builder's pre-exposure proof**, `Gc027RestoreBuilder.TryProveOutbox`, which
   `CheckpointRestoreExecutor` calls *before* `UnityWorldRegistry.TryExpose`: a restore that lost or invented an
   obligation is refused rather than published (P-045, P-053). `RestoreOutcome.RestoredOutboxRows` and the builder's
   `ProvedRowCount` are the two counts the same check produces on the two sides of that seam.

The canonical form is one line plus one line per cursor plus one line per problem:

```
outbox-consistency label=<label> carried=<n>(open=<o>,terminal=<t>,cursor=<c>) live=(<tracked>/<open>/<terminal>/pruned=<p>/adopted=<a>) cursors=<c> consistent=<0|1>
cursor(<destination>,ack=<obligation|none>,retained=<r>,total=<tt>)
problem=<what disagreed>
```

## Fill-in table (build host)

`label` is `recovered:<session>` for the recovery observation, `restarted:<session>` for the restart, and
`staging:<session>` for the builder's pre-exposure proof. Copy from `probe-gc027.json`.

| # | Observation / seam | Label | carried (open, terminal, cursor) | live (tracked, open, terminal, pruned, adopted) | consistent | Obligations owed | Destination attempts | Fill from probe |
|---|---|---|---|---|---|---|---|---|
| 1 | `gc027-outbox-rows-and-delivery-cursor-survive-the-recovery` | `recovered:<session>` | | | | | | |
| 2 | `CheckpointRestoreExecutor` pre-exposure proof of that same recovery | `staging:<session>` | | | | n/a | n/a | not separately emitted; `RestoredOutboxRows` + builder `ProvedRowCount` |
| 3 | `gc027-restart-from-the-store-recovers-without-in-process-state` | `restarted:<session>` | | | | | | |
| 4 | `gc027-outbox-append-fault-refuses-before-delivery` | n/a (a bare outbox, no checkpoint) | | | n/a | 0 | 0 | journal frames, tracked obligations |
| 5 | `gc027-outbox-delivery-fault-redelivers-with-one-destination-effect` | n/a | | | n/a | | | effects after crash / after redelivery / already-applied |
| 6 | `gc027-outbox-acknowledgement-fault-records-or-redelivers-once` | n/a | | | n/a | | | redeliverable-after-crash, settled state, effects |

## The invariants each row must show, and why

| Invariant | Meaning | Requirement |
|---|---|---|
| `carried.open == live.open` | the recovered outbox owes exactly what the checkpoint carried | P-045, P-053 |
| `carried.terminal == live.terminal` | no terminal record was invented or dropped | P-045 |
| `cursor.ack` unchanged per destination | the acknowledgement position did not move during recovery | P-045 |
| `cursor.retained == carried terminal rows` for that destination | the cursor's declared retention agrees with the rows | P-045, P-053 |
| no `problem=` line | nothing disagreed | P-052 |
| `destination attempts == 0` after a recovery | **no hidden external replay**: reinstatement is state, not delivery | P-049 |
| effects after a redelivery == effects after the first attempt | the redelivery reused the idempotency key | P-045 |
| `journal frames == 0` at the append refusal | nothing durable was written, so the commit was refused rather than applied | P-045 |

Rows 4–6 are about a bare outbox plus a destination port rather than about a checkpoint, so `carried`/`live` do not
apply; their evidence is the counters named in the last column. They exist because the task names outbox append,
delivery and acknowledgement as injection points in their own right, and because "the replayed external delivery does
not duplicate the test destination effect" is a statement about the destination's own mutation count.

## The one thing this report deliberately does not claim

Exactly-once delivery. P-045's boundary is at-least-once with destination-side deduplication, and the report is
honest about it: an obligation handed over and never acknowledged **will** be handed over again, and it is the
destination's explicit idempotency key (`DeliveryAttempt.IdempotencyKey`, derived from committed data) that makes
the second attempt a no-op. The report's `alreadyApplied`/`effects after redelivery` columns are the measurement of
that, not a claim that the retry did not happen.
