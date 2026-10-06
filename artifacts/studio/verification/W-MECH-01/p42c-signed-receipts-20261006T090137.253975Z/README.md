# p42c-signed-receipts

Verdict: **PASS**.

Source revision: `9d8ab6b11d43a22b5b120d5b1e19e23e8d878778`; host: `worlesenric`.
Started: 2026-10-06T09:01:37.255296+00:00; ended: 2026-10-06T09:01:41.232063+00:00; duration: 3.978 s.

Command (from repository root unless cwd specified):

```sh
dotnet test artifacts/studio/verification/TOOLS/P42cReconnect/P42cReconnect.csproj --filter 'FullyQualifiedName~R2_38_RetainedColdWarmVerdicts' --logger trx --results-directory ~/wkspace/gc-studio/p4.2c/artifacts/studio/verification/W-MECH-01/p42c-signed-receipts-20261006T090137.253975Z/trx
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
