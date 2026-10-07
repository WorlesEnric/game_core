# p42k-tamper

Verdict: **PASS**.

Source revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; host: `worlesenric`.
Started: 2026-10-07T19:28:04.030489+00:00; ended: 2026-10-07T19:28:13.928115+00:00; duration: 9.899 s.

Command (from repository root unless cwd specified):

```sh
dotnet test ~/wkspace/gc-studio/p4.2k/artifacts/studio/verification/TOOLS/P42jReceipt/P42jReceipt.csproj --filter 'FullyQualifiedName~R2_38_CurrentGeneratedTextureTamperRefusedWithoutRegeneration' --logger trx --results-directory ~/wkspace/gc-studio/p4.2k/artifacts/studio/verification/W-ETOS-07/p42k-tamper-20261007T192804.029366Z/trx
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
