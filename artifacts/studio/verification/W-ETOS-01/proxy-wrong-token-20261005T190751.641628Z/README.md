# proxy-wrong-token

Verdict: **PASS**. Unauthenticated proxy probe: expected HTTP 401. This exercises the node boundary, not an independently authenticated companion app-header check.

Source revision: `12d7e5c7ababe95a1e251343584755e6c38fd454`; host: `worlesenric`.
Started: 2026-10-05T19:07:51.642672+00:00; ended: 2026-10-05T19:07:51.646781+00:00; duration: 0.005 s.

Command (from repository root unless cwd specified):

```sh
curl --silent --show-error --noproxy '*' --max-time 10 --write-out '
HTTP %{http_code}
' -H 'X-Etos-App: gamecore-unity' -H 'X-Etos-Proxy-Token: invalid-p42-probe' http://127.0.0.1:7410/api/v1/agents/gamecore-studio/http/v1/hello
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
