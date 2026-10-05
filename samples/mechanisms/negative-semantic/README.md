# Semantic scanner negative fixture

This package is deliberately non-admissible and must never be installed or compiled in a Unity project.
`expected.json` lists the required stable finding IDs. Analyzer unit tests parse these files without execution;
the host CLI regression uses the trusted Unity/GameCore metadata context and requires exit 3.
