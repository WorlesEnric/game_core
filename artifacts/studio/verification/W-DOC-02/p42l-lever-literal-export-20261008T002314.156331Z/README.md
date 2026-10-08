# p42l-lever-literal-export

Verdict: **PASS**.

Source revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; host: `worlesenric`.
Started: 2026-10-08T00:23:14.157429+00:00; ended: 2026-10-08T00:23:56.915086+00:00; duration: 42.759 s.

Command (from repository root unless cwd specified):

```sh
bash studio/tools/unity-batch.sh --project ~/wkspace/gc-studio/p4.2l/games/hollowmere --log-dir ~/wkspace/gc-studio/p4.2l/artifacts/studio/verification/W-DOC-02/p42l-lever-literal-export-20261008T002314.156331Z/logs --label p42l-lever-literal-export --attempts 1 --timeout 1800 -- -executeMethod Hollowmere.R8_B.LeverWalkthrough.Run -gcR8CStage ~/wkspace/gc-studio/p4.2l/artifacts/studio/verification/W-DOC-02/p42l-lever-literal/config.json -force-glcore
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
