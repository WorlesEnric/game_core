# evidence-footer

Verdict: **PASS**.

Source revision: `20e34d1e6a6800e4b0bd4a1e3ebd44611a968c55`; host: `worlesenric`.
Started: 2026-10-05T17:32:02.340737+00:00; ended: 2026-10-05T17:32:02.609404+00:00; duration: 0.274 s.

Command (from repository root unless cwd specified):

```sh
python3 Packages/com.gamecore.studio.ui/Tests/Host/test_evidence_contract.py
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
