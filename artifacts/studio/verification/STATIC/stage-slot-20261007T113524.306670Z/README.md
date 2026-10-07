# stage-slot

Verdict: **PASS**.

Source revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; host: `worlesenric`.
Started: 2026-10-07T11:35:24.308335+00:00; ended: 2026-10-07T11:35:24.544935+00:00; duration: 0.238 s.

Command (from repository root unless cwd specified):

```sh
python3 tools/check_stage_slot.py --self-test
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
