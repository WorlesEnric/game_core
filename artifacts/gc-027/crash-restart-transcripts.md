# GC-027 crash/restart transcripts

**Executed:** the qualification IL2CPP `-probeRecovery` passed five clean runs; the canonical run 1 is `artifacts/gc-027/toolchain/probe-gc027.json`. The detail strings below are copied from run 1. The scenario does not emit a transcript clause for every observation: a blank would be fabricated evidence, so those rows explicitly say `not emitted` and retain their observed outcome. The per-family digests differ because they include session and document identities.

## Where the transcripts come from

Every GC-027 recovery writes a `GameCore.Execution.Recovery.RecoveryTranscript`: one ordered, bounded line per phase
it passed through, each carrying the injection point it reached (if any) and the data-loss class at that point. The
scenario embeds each transcript's one-line summary — `transcript(lines=N/M,faults=F,loss=<class>,digest=<sha256>)` —
in the `detail` of the observation that drove it, so the player probe artifact
`artifacts/gc-027/toolchain/probe-gc027.json` carries all of them without a second output channel.

The transcript has no timestamp, no host measure and no thread identity by construction, so two runs of the same
recovery produce the same text and the same digest. That is what makes the digest a value a reviewer can compare
instead of a log to be read.

For each family's passing detail, copy a `transcript(...)` clause only when the observation actually emits one. Capture and publication record their fault into the run's shared transcript; their own step detail does not embed its summary. Postwrite and publication refusal steps assert their staging/registry result directly but do not embed a transcript summary. Restart refusal asserts the two refusal codes without embedding a summary. All three families ran: narrative, cards, traversal; traversal omits delivery observations because it has no destination.

| Observation | Narrative | Cards | Traversal | Observed class / note |
|---|---|---|---|---|
| capture-copy fault | not emitted | not emitted | not emitted | `None`; no capture, source `Running` |
| file-publication fault | not emitted | not emitted | not emitted | `None`; prior envelope remains readable |
| reference-repair fault | `lines=2/32,faults=1,loss=None,digest=fbb3056e…` | `lines=2/32,faults=1,loss=None,digest=1d6a05c1…` | `lines=2/32,faults=1,loss=None,digest=abfc9d75…` | `None`; builder attempts 0 |
| postwrite apply fault | not emitted | not emitted | not emitted | `UncommittedAttemptWork`; staging disposed |
| recovery-publication fault | not emitted | not emitted | not emitted | same postwrite class; staging disposed |
| clean recovery | `lines=6/64,faults=0,loss=None,digest=64c10477…` | `lines=6/64,faults=0,loss=None,digest=9556ebde…` | `lines=6/64,faults=0,loss=None,digest=513267ee…` | `None`; source disposed, destination running |
| restart from store | `lines=5/64,faults=0,loss=UncommittedSinceCheckpoint,digest=560e98e4…` | `lines=5/64,faults=0,loss=UncommittedSinceCheckpoint,digest=790d44df…` | `lines=5/64,faults=0,loss=UncommittedSinceCheckpoint,digest=1939a85a…` | `UncommittedSinceCheckpoint`; prior session not contacted |
| restart absent/incompatible | not emitted | not emitted | not emitted | no world exposed; `ResourceUnavailable` / `UnsupportedVersion` |
| bounded retry | `lines=6/64,faults=1,loss=UncommittedSinceCheckpoint,digest=9c301f19…` | `lines=6/64,faults=1,loss=UncommittedSinceCheckpoint,digest=893a4976…` | `lines=6/64,faults=1,loss=UncommittedSinceCheckpoint,digest=46330917…` | host bound 3, two attempts, one retry, distinct identities |
| teardown | `lines=7/256,faults=2,loss=None,digest=9a905265…` | `lines=7/256,faults=2,loss=None,digest=9d0b350b…` | `lines=7/256,faults=2,loss=None,digest=d386df15…` | registry returns to 0 |

## The restart record

`WorldRecoveryReport.Restart` is a `RecoveryRestartPoint`, whose `ToLine()` is embedded in the two restart
observations. Its canonical fields are the restart's whole input and output, which is what makes "a restart has no
in-process state" checkable:

| Field | Meaning | Asserted by |
|---|---|---|
| `store=<location>` | where the bytes came from; diagnostic, never an identity (P-004) | `gc027-restart-…` |
| `present=0\|1` | whether a document was there at all | both restart observations |
| `document=<sha256>` | identity of the bytes read, or the all-zero hash when none was present | both |
| `was=<session>` | the session that committed them; **never contacted** | `…recovers-without-in-process-state` |
| `now=<session>` | the new incarnation, or `<none>` when nothing was published | both |
| `code=<code>` | the diagnostic code of a refusal | `…exposes-nothing` |

## Attempt identity, which is what P-049/P-050's retry requires

`WorldRecoveryReport.Describe()` emits `attempts=<n>(retries=<r>,distinct=<0|1>)` and then one line per attempt
(`attempt=<ordinal> kind=<Initial|Retry> source=<kind> session=<id> op=<id> outcome=<outcome> code=<code>`). Fill in:

| Observation | attempts | retries | distinct identities | sessions (old → new) |
|---|---|---|---|---|
| `gc027-transient-failure-is-retried-under-the-host-bound` | 2 | 1 | `True` | initial refused session `…09` in every family; retry uses a distinct reserved session and operation ID (asserted by the probe's `distinctIdentities=True`, though its detail only prints the first attempt's identity) |
| `gc027-teardown-disposes-every-world` | n/a | n/a | n/a | all three registries end at 0; not a retry observation |

`distinct=1` means every attempt named a different session **and** a different operation id; `distinct=0` would be a
contract violation, not a formatting difference.

## What a transcript is not

It is not a durability guarantee: it records what one recovery did, bounded at 256 lines per run (an overflow is
counted in `OverflowCount` rather than grown). It is not a substitute for the observations: it explains, it does not
assert. The assertions are the named observations, and the harness (`tools/unity/run_recovery_probe.sh`) fails when
one of them is missing, when a `"status": "Fail"` appears, or when either frozen digest literal is not reported.
