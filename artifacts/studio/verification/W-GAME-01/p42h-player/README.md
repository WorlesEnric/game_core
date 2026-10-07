# P4.2h current-source GPU recordings

Product `a77cb38ba4a2265007fa40c38983e01a17bb0914`; evidence harness `44f2b4ba`. Real RTX 4060 Ti, display :1, 1920×1080, release Linux IL2CPP. Four actual game-window H264/AAC recordings exceed 600 seconds, sampled visually; [visual review](visual-review.json), [external movie hashes](external-movies.json), [full result](result.json). Large movies remain in the packet-owned .evidence/P42hPlayer-qualified directory.

| VSync | Run | Steady s | p95 ms | >100 ms outside transitions | Literal B-FRAME |
|---|---|---|---|---|---|
| OFF | 1 | 611.395 | 3.461 | 0 | PASS |
| OFF | 2 | 609.205 | 3.552 | 0 | PASS |
| ON | 1 | 608.505 | 17.375 | 2 | FAIL |
| ON | 2 | 609.438 | 17.314 | 0 | FAIL |

All four full routes, saves and recordings pass their own checks; maximum transition hitch 122.590 ms. Capture and one early proof-frame capture overhead are included. Owner VSync definition remains open, so W-GAME-01 is BLOCKED, not an averaged/relaxed pass. Historical/setup directories preserve desktop-only and incomplete attempts and are not substituted for these four recordings.

Reproduce with TOOLS/P42hPlayer/player.py build/run and retain.py; exact commands are in the retained command JSON/transcript. Final capture selects only the launched PID’s substantial X11 window after actual player readiness. No unrelated window or installed service is changed.
