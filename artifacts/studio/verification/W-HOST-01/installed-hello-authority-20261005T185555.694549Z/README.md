# installed-hello-authority

Verdict: **PASS**.

Source revision: `38baed3d6484f29674131fba4c2773c41ebf68ab`; host: `worlesenric`.
Started: 2026-10-05T18:55:55.695555+00:00; ended: 2026-10-05T18:55:59.638555+00:00; duration: 3.944 s.

Command (from repository root unless cwd specified):

```sh
dotnet test dotnet/tests/GameCore.Studio.Etos.Client.Tests --filter 'FullyQualifiedName~L01_|FullyQualifiedName~L02_' --logger trx --results-directory ~/wkspace/gc-studio/p4.2/artifacts/studio/verification/W-HOST-01/installed-hello-authority-20261005T185555.694549Z/trx
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
