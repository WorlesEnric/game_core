# p42l-player-interactions

Verdict: **PASS**.

Source revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; host: `worlesenric`.
Started: 2026-10-07T23:29:45.972443+00:00; ended: 2026-10-07T23:30:27.025492+00:00; duration: 41.054 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2l/games/hollowmere --log-dir ~/wkspace/gc-studio/p4.2l/artifacts/studio/verification/W-PLUG-03/p42l-player-interactions-20261007T232945.971207Z/logs --label p42l-player-interactions --attempts 1 --timeout 1800 --results ~/wkspace/gc-studio/p4.2l/artifacts/studio/verification/W-PLUG-03/p42l-player-interactions-20261007T232945.971207Z/results.xml -- -runTests -testPlatform PlayMode -testFilter 'LedgeJumpLanding|WalksInteractsTalksPastNpcsAndTravels|R7C_WPLUG08_RepeatedBarnLanternPickupAndReload_LeavesExactlyOneLantern|R7C_WPLUG11_ActualMarenLinePlaysItsNativeVoiceClip'
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
