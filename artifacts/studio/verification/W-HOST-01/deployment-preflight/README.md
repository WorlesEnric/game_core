# Deployment preflight

2026-10-05T17:17:50.584098+00:00; source 3c5f817aa45a86d0af85cd8af6e953d128524a02.

Read-only price diff, agent readiness and authenticated hello before deployment. No services or configuration changed. The installed CLI rejects `agent status`; `agent list` is the supported readiness command. Reinstall waits for the concurrent P3.1b process to exit. Node price template differences would make the full installer restart etosd, which is forbidden. The isolated immutable-release helper avoids that node path.
