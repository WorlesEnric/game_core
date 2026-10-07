# p42j-tamper

Verdict: **PASS**.

Source revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; host: `worlesenric`.
Started: 2026-10-07T12:44:29.522655+00:00; ended: 2026-10-07T12:44:38.365675+00:00; duration: 8.845 s.

Command (from repository root unless cwd specified):

```sh
dotnet test ~/wkspace/gc-studio/p4.2j/artifacts/studio/verification/TOOLS/P42jReceipt/P42jReceipt.csproj --filter 'FullyQualifiedName~R2_38_CurrentGeneratedTextureTamperRefusedWithoutRegeneration' --logger trx --results-directory ~/wkspace/gc-studio/p4.2j/artifacts/studio/verification/W-ETOS-07/p42j-tamper-20261007T124429.514414Z/trx
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
