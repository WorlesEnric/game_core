# GC-027 outbox consistency reports

**Executed:** qualification IL2CPP `-probeRecovery` Pass 5/5; run 1 in `artifacts/gc-027/toolchain/probe-gc027.json`. Narrative and cards have durable outboxes; traversal has no destination or obligation, so delivery rows there are N/A.

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

## Executed outbox census

The recovered report's label is `recovered:<session>`; narrative run 1 uses `recovered:09716070107562600000000000000005`, cards uses its own new session. The restart observation emits obligation/effect counters, not a separate `restarted:` census string. The builder's internal `staging:` census is asserted via `ProvedRowCount` but not emitted in the player JSON.

| Observation / seam | Narrative run 1 | Cards run 1 | Result |
|---|---|---|---|
| recovered outbox / cursor | `carried=4(open=1,terminal=1,cursor=1); live=(2/1/1/pruned=0/adopted=2); consistent=1; provedRows=4; destinationAttempts=0` | same counts and verdict, distinct session/destination IDs | Pass x5; one owed obligation, acknowledged predecessor cursor unchanged, no hidden delivery |
| builder's pre-exposure proof | `RestoredOutboxRows=4; ProvedRowCount=4` | same | Pass x5; `TryProveOutbox` completed before exposure; the probe does not separately emit the builder's `staging:` report string |
| restart from verified store | `owedObligations=1; destinationAttempts=0; outcome=Published` | same | Pass x5; no in-process replay |
| append fault | `journalFrames=0; trackedObligations=0; destinationAttempts=0` | same | Pass x5; no unpersisted effect |
| delivery crash and redelivery | `effectsAfterCrash=1; effectsAfterRedelivery=1; alreadyApplied=1; settledState=Acknowledged` | same | Pass x5; same idempotency key, one effect |
| acknowledgement on either side | `stillRedeliverable=True` before; `afterCrashed=True; afterSettled=True; destinationEffects=1` | same | Pass x5; retained cursor and one effect |

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
