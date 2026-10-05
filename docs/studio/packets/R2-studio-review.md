# Packet R2: independent Studio review

Reviewed baseline: `ddd22fecf048db08760c5ea49360c8e2dd529814` (local `origin/main`), on Linux, branch `codex/r2-studio-review`.

Deliverable: [Studio review](docs/studio/reviews/2026-10-05-studio-review.md). It contains numbered findings, all 57 P4.3 reconciliation dispositions, a static-state inventory, acceptance coverage audit, verified checks, architect questions and exclusive-path fix packets.

Only this file and the new review report were written. Existing untracked `.codex/` was present at start and is excluded. No code, asset or existing doc was changed. No Unity, dotnet, cargo, ETOS, provisioning or credential-file operation was run.

Read-only checks passed: package metadata (41 packages / 88 assemblies), C# (1,076 files), stage-slot self-test (14 cases). Runtime results from older packets remain historical; this packet claims source review and interpreter checks only.

Findings: **3 blockers, 27 majors, 11 minors (41 total)**. The three blockers are executable artifacts bypassing staging, unauthenticated/self-authored admission verdicts, and unsandboxed stage execution capable of affecting the live project. The report supplies exact locations and fixes.
