# CORE-PICK verification

Product/test source: `2d992c9f`, Linux myubuntu, Unity 6000.0.75f1.
W-UI-05 / B-SELECT: **PASS**. Marquee p95 **0.2688 / 0.3098 ms** against 50 ms;
100 picks and 100 marquee queries per run, 500 distinct identities, peak Editors 1.
Both final XMLs pass (2/2 and 1/1); the 21-update median is 1.8216 ms.

See [PACKET.md](../PACKET.md) for full counts, cold-latency limits, profiles, commands,
original hashes and all earlier failures. The complete datasets and XMLs are retained
next to this README. Metadata (42 packages / 91 assemblies), C# policy (1,209 files)
and installer (5/5) final transcripts are `final-*.txt`.
