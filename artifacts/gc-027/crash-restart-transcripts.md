# GC-027 crash/restart transcripts

**Status: `NotRun (pending orchestrator build host)`.** Every row below is a *template*. Nothing in this file is a run
result: this host has no Unity and no .NET SDK.

## Where the transcripts come from

Every GC-027 recovery writes a `GameCore.Execution.Recovery.RecoveryTranscript`: one ordered, bounded line per phase
it passed through, each carrying the injection point it reached (if any) and the data-loss class at that point. The
scenario embeds each transcript's one-line summary — `transcript(lines=N/M,faults=F,loss=<class>,digest=<sha256>)` —
in the `detail` of the observation that drove it, so the player probe artifact
`artifacts/gc-027/toolchain/probe-gc027.json` carries all of them without a second output channel.

The transcript has no timestamp, no host measure and no thread identity by construction, so two runs of the same
recovery produce the same text and the same digest. That is what makes the digest a value a reviewer can compare
instead of a log to be read.

## Fill-in procedure (build host)

For each `detail` in `probe-gc027.json` whose observation name is listed below, copy the `transcript(...)` clause and
the `loss=` class into the matching row. The step names are qualified `narrative/…` and `cards/…`; both families run
the same sequence, so both rows must agree.

| # | Observation | Injection point | Transcript clause (fill from the probe artifact) | Data-loss class (fill) |
|---|---|---|---|---|
| 1 | `gc027-capture-copy-fault-produces-no-checkpoint` | capture copy | | |
| 2 | `gc027-publication-fault-keeps-the-previous-document` | file publication | | |
| 3 | `gc027-reference-repair-fault-never-builds-a-destination` | restore reference repair | | |
| 4 | `gc027-postwrite-apply-fault-never-exposes-a-destination` | postwrite apply | | |
| 5 | `gc027-recovery-publication-fault-keeps-the-registry-unchanged` | postwrite apply (publication reach) | | |
| 6 | `gc027-recovery-publishes-a-new-session-with-the-captured-state` | — (clean recovery) | | |
| 7 | `gc027-restart-from-the-store-recovers-without-in-process-state` | restart | | |
| 8 | `gc027-restart-without-a-document-or-incompatible-content-exposes-nothing` | restart (refusal) | | |
| 9 | `gc027-transient-failure-is-retried-under-the-host-bound` | — (bounded retry) | | |
| 10 | `gc027-teardown-disposes-every-world` | — (teardown) | | |

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
| `gc027-transient-failure-is-retried-under-the-host-bound` | | | | |
| `gc027-teardown-disposes-every-world` | | | | |

`distinct=1` means every attempt named a different session **and** a different operation id; `distinct=0` would be a
contract violation, not a formatting difference.

## What a transcript is not

It is not a durability guarantee: it records what one recovery did, bounded at 256 lines per run (an overflow is
counted in `OverflowCount` rather than grown). It is not a substitute for the observations: it explains, it does not
assert. The assertions are the named observations, and the harness (`tools/unity/run_recovery_probe.sh`) fails when
one of them is missing, when a `"status": "Fail"` appears, or when either frozen digest literal is not reported.
