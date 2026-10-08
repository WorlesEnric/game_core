# p42l-tamper

Verdict: **PASS**.

Source revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; host: `worlesenric`.
Started: 2026-10-07T23:46:08.138958+00:00; ended: 2026-10-07T23:46:17.083905+00:00; duration: 8.946 s.

Command (from repository root unless cwd specified):

```sh
dotnet test ~/wkspace/gc-studio/p4.2l/artifacts/studio/verification/TOOLS/P42lReceipt/P42lReceipt.csproj --filter 'FullyQualifiedName~R2_38_CurrentGeneratedTextureTamperRefusedWithoutRegeneration' --logger trx --results-directory ~/wkspace/gc-studio/p4.2l/artifacts/studio/verification/W-ETOS-07/p42l-tamper-20261007T234608.137312Z/trx
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
