# p42c-cap-baseline-before

Verdict: **FAIL**. Expected before-fix failure: test_R2_38_P42c_baseline_cannot_reset_spend demonstrates that repeating baseline reset paid accounting. The after run passes with a refusal before overwrite.

Source revision: `dbedd2fb6c830be5213d1658da41f0956cbc7f28`; host: `worlesenric`.
Started: 2026-10-06T07:37:17.556698+00:00; ended: 2026-10-06T07:37:17.654583+00:00; duration: 0.099 s.

Command (from repository root unless cwd specified):

```sh
python3 -m unittest discover -s artifacts/studio/verification/TOOLS/tests -p test_p42c.py -v
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
