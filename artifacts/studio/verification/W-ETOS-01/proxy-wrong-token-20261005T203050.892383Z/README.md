# proxy-wrong-token

Verdict: **PASS**. Unauthenticated proxy probe: expected HTTP 401. This exercises the node boundary, not an independently authenticated companion app-header check.

Source revision: `c532b77edd93099ddd1e45d20f48db5d627413b3`; host: `worlesenric`.
Started: 2026-10-05T20:30:50.894568+00:00; ended: 2026-10-05T20:30:50.918698+00:00; duration: 0.026 s.

Command (from repository root unless cwd specified):

```sh
curl --silent --show-error --noproxy '*' --max-time 10 --write-out '
HTTP %{http_code}
' -H 'X-Etos-App: gamecore-unity' -H 'X-Etos-Proxy-Token: invalid-p42-probe' http://127.0.0.1:7410/api/v1/agents/gamecore-studio/http/v1/hello
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
