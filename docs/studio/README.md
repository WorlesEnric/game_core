# GameCore Studio documentation

Final documentation **as of P4.2l (2026-10-08)**. Product acceptance is **62 PASS / 6 BLOCKED / 0 FAIL (68 rows)**, all rows judged at one revision (`6e8e73c4`) and one installed companion release; the six BLOCKED rows are the scenarios the owner's host rules forbid. Start with the [completion report](12-completion-report.md). ([SUMMARY](../../artifacts/studio/verification/SUMMARY.md), [P4.2l](packets/P4.2l-same-revision.md), [agents design](../agents/README.md))

| Document | Contents |
|---|---|
| [01 Gap assessment](01-gap-assessment.md) | Original 67 SR requirements plus closing status/evidence |
| [02 Architecture](02-architecture.md) | Boundaries, lifecycle and SADR-001..053 |
| [03 Authoring contracts](03-authoring-contracts.md) | Identities, metadata, generated schemas, edits and admission |
| [04 ETOS integration](04-etos-integration.md) | Service-form agent, authority, routes, deadlines and tariffs |
| [05 Plugin catalog](05-plugin-catalog.md) | Twelve groups, tool IDs/flags and reuse findings |
| [06 Implementation plan](06-implementation-plan.md) | Scope history and merge provenance |
| [07 Verification matrix](07-verification-matrix.md) | 68 dispositions, budgets and VSync decision |
| [08 Creator guide](08-creator-guide.md) | Author, prompt, review, save and troubleshoot |
| [09 Plugin developer guide](09-plugin-developer-guide.md) | Metadata, runtime/save seams, trusted stage and admission |
| [10 Install, build and run](10-install-build-run.md) | Linux commands, Saltmarsh/new-project flow and host troubleshooting |
| [11 Ownership plan](11-ownership-plan.md) | Maintenance roles and review/test boundaries |
| [12 Completion report](12-completion-report.md) | Delivery, blocked/failed rows, security posture and handover |
| [P4.3-final packet](packets/P4.3-final-docs.md) | All 57 reconciliation items and remaining requests |
| [Review](reviews/2026-10-05-studio-review.md) / [packet notes](packets/) | Revision-specific findings and implementation receipts |
| [Generated schemas](schemas/) | Generated authoring contracts; never hand-edit |
| [Archive](archive/) | Historical design, superseded by numbered decision records |

Kernel normative semantics remain in [00-core-protocols.md](../game-core/00-core-protocols.md). Studio evidence is under [artifacts/studio](../../artifacts/studio/).
