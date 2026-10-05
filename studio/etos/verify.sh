#!/usr/bin/env bash
# GameCore Studio W0 verification of the host etos node (docs/studio/04-etos-integration.md §8.8).
#
#   studio/etos/verify.sh             (run on the Linux host, from the game_core checkout)
#
# Against the running node (etosd.service, 127.0.0.1:7410):
#   1. service, `etos node status`, workers, agents
#   2. installs the throwaway agent studio/etos/verify-agent (grants ops, realtime, files;
#      providers ["studio-voice"]) and uses the key etosd wrote for it (never printed)
#   3. POST /ops/generate.image (small, max_cost_usd 0.1) -> downloads the image reference
#   4. POST /ops/describe of that image
#   5. POST /ops/tts "Welcome to Hollowmere." -> downloads the wav
#   6. /realtime/connect?provider=studio-voice: 2 s of a 440 Hz tone, expects ready + clean close
#   7. POST /ops/generate.3d -> expected refusal not_configured (no 3D provider, SADR-020)
#   8. `etos agent uninstall verify --purge` (also on failure)
#   9. with the companion installed (P0.5): GET .../agents/gamecore-studio/http/v1/hello with the
#      paired app key, and studio/agent's real-node test (cargo test --test real_node -- --ignored)
# Writes transcript.txt, the artifacts and README.md to
# artifacts/studio/environment/etos-verify-<UTC date>/ (keys and signed URLs redacted).
# Exit status: 0 when every check passed, 1 otherwise.
set -uo pipefail

HERE="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd -- "$HERE/../.." && pwd)"
ROOT="${ETOS_STUDIO_ROOT:-$HOME/.local/share/etos-studio}"
BIN="${ETOS_STUDIO_BIN:-$HOME/.local/opt/etos/bin}"
SDK_PY="${ETOS_SDK_PY:-$HOME/wkspace/etos-studio/sdk/py}"
API="http://127.0.0.1:7410/api/v1"
DATE="$(date -u +%F)"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUT="$REPO/artifacts/studio/environment/etos-verify-$DATE"
export ETOS_ROOT="$ROOT" PATH="$BIN:$PATH"
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"

# One evidence directory per day: a later run of the same day replaces the earlier one.
rm -rf "$OUT"
mkdir -p "$OUT"
redact() { sed -u -E 's/(et[k]_)[A-Za-z0-9_-]+/\1[redacted]/g; s/(s[k]-)[A-Za-z0-9_-]{8,}/\1[redacted]/g; s/(Signature|OSSAccessKeyId|Expires)=[^&" ]*/\1=[redacted]/g'; }
exec > >(redact | tee "$OUT/transcript.txt") 2>&1

results=()
record() { results+=("$1|$2|$3"); echo ">> $1: $2${3:+ ($3)}"; }
run() { echo "\$ $*"; "$@"; }
json() { python3 -c "import json,sys; d=json.load(open(sys.argv[1])); $2" "$3"; }

TMP="$(mktemp -d)"
chmod 700 "$TMP"
HDR="$TMP/auth"
cleanup() {
    rm -rf "$TMP"
    if etos --json agent list 2>/dev/null | grep -q '"verify"'; then
        echo "\$ etos agent uninstall verify --purge"
        etos agent uninstall verify --purge
    fi
}
trap cleanup EXIT

echo "# GameCore Studio etos W0 verification, $STAMP"
echo "host: $(hostname) ($(uname -sr)), user $(id -un)"
echo "node root: $ROOT; binaries: $BIN"
echo "etos source: $(git -C "$HOME/wkspace/etos-studio" log -1 --format='%H (%D)' 2>/dev/null)"
echo "etos.lock:"; sed 's/^/  /' "$HERE/etos.lock" 2>/dev/null
GC_REV="${GC_REV:-$(git -C "$REPO" log -1 --format='%H (%D)' 2>/dev/null || echo unknown)}"
echo "game_core: $GC_REV"
echo

echo "## 1. node"
run systemctl --user is-active etosd.service && record service ok "etosd.service active" || record service FAILED "etosd.service not active"
if run etos node status; then record node_status ok ""; else record node_status FAILED ""; fi
run etos worker list
run etos agent list

echo; echo "## 2. verify agent"
if etos --json agent list | grep -q '"verify"'; then run etos agent uninstall verify --purge; fi
if ! run etos agent install "$HERE/verify-agent"; then
    record verify_agent FAILED "install refused"
else
    KEY="$ROOT/agents/verify/key"
    ( umask 077; printf 'Authorization: Bearer %s\n' "$(tr -d '[:space:]' < "$KEY")" > "$HDR" )
    record verify_agent ok "key file $KEY (mode $(stat -c %a "$KEY"))"
fi

# op NAME BODY OUTFILE -> prints HTTP status and the answer
op() {
    echo "\$ POST /api/v1/ops/$1 $2"
    local code
    code="$(curl -sS --noproxy '*' -m 900 -H @"$HDR" -H 'Content-Type: application/json' \
        -X POST "$API/ops/$1" -d "$2" -o "$3" -w '%{http_code}')"
    echo "HTTP $code"
    python3 -m json.tool "$3" 2>/dev/null | cut -c1-400 || head -c 2000 "$3"
    echo
    [ "${code:0:1}" = 2 ]
}
fetch() { # REF FILE
    echo "\$ GET /api/v1/files/$1 -> $(basename "$2")"
    curl -sS --noproxy '*' -m 300 -H @"$HDR" "$API/files/$1" -o "$2" -w 'HTTP %{http_code}, %{size_download} bytes\n'
    file "$2" | sed "s|$OUT/||"
}
ref_of() { json "$1" 'r=d.get("refs") or []; r=r[0] if r else ""; print(r.get("id","") if isinstance(r,dict) else r)' "$1"; }
state_of() { json "$1" 'print((d.get("state") or {}).get("state",""))' "$1"; }

echo; echo "## 3. generate.image"
IMG_REF=""
if op generate.image "{\"prompt\":\"A small stone well in a misty village square, painterly game concept art\",\"size\":\"1024x1024\",\"count\":1,\"max_cost_usd\":0.1,\"key\":\"verify-img-$STAMP\"}" "$TMP/image.json" \
   && [ "$(state_of "$TMP/image.json")" = succeeded ]; then
    IMG_REF="$(ref_of "$TMP/image.json")"
    fetch "$IMG_REF" "$OUT/image.png"
    cp "$TMP/image.json" "$OUT/image.job.json"
    record generate.image ok "ref $IMG_REF, $(stat -c %s "$OUT/image.png") bytes"
else
    cp "$TMP/image.json" "$OUT/image.job.json" 2>/dev/null
    record generate.image FAILED "$(head -c 300 "$TMP/image.json" 2>/dev/null)"
fi

echo; echo "## 4. describe"
if [ -n "$IMG_REF" ] && op describe "{\"input\":\"$IMG_REF\",\"prompt\":\"Describe this image in two sentences.\"}" "$TMP/describe.json"; then
    cp "$TMP/describe.json" "$OUT/describe.json"
    record describe ok "$(json "$TMP/describe.json" 'print(d.get("text","")[:120].replace("\n"," "))' "$TMP/describe.json")"
else
    cp "$TMP/describe.json" "$OUT/describe.json" 2>/dev/null
    record describe FAILED "$(head -c 300 "$TMP/describe.json" 2>/dev/null || echo 'no image to describe')"
fi

echo; echo "## 5. tts"
if op tts "{\"text\":\"Welcome to Hollowmere.\",\"voice\":\"Cherry\",\"key\":\"verify-tts-$STAMP\"}" "$TMP/tts.json" \
   && [ "$(state_of "$TMP/tts.json")" = succeeded ]; then
    fetch "$(ref_of "$TMP/tts.json")" "$OUT/welcome.wav"
    cp "$TMP/tts.json" "$OUT/tts.job.json"
    record tts ok "$(stat -c %s "$OUT/welcome.wav") bytes; $(file -b "$OUT/welcome.wav")"
else
    cp "$TMP/tts.json" "$OUT/tts.job.json" 2>/dev/null
    record tts FAILED "$(head -c 300 "$TMP/tts.json" 2>/dev/null)"
fi

echo; echo "## 6. realtime (studio-voice)"
echo "\$ python3 studio/etos/verify-agent/realtime_probe.py"
if ETOS_SDK_PY="$SDK_PY" ETOS_KEY_FILE="$ROOT/agents/verify/key" ETOS_URL="http://127.0.0.1:7410" \
    no_proxy='*' NO_PROXY='*' python3 "$HERE/verify-agent/realtime_probe.py" | tee "$OUT/realtime.jsonl"; then
    record realtime ok "$(tail -n 1 "$OUT/realtime.jsonl")"
else
    record realtime FAILED "$(tail -n 1 "$OUT/realtime.jsonl")"
fi

echo; echo "## 7. generate.3d (expected: not configured)"
if op generate.3d "{\"prompt\":\"a wooden barrel\",\"key\":\"verify-3d-$STAMP\"}" "$TMP/3d.json"; then
    record generate.3d UNEXPECTED "a 3D provider answered"
else
    cp "$TMP/3d.json" "$OUT/3d.refusal.json"
    code="$(json "$TMP/3d.json" 'print(d.get("code") or (d.get("error") or {}).get("code",""))' "$TMP/3d.json" 2>/dev/null)"
    if [ "$code" = not_configured ]; then record generate.3d "blocked (expected)" "not_configured"; else record generate.3d FAILED "$code"; fi
fi

echo; echo "## 8. cleanup"
run etos agent uninstall verify --purge && record verify_agent_removed ok "" || record verify_agent_removed FAILED ""
trap - EXIT
rm -rf "$TMP"

# The `state` of every "--- settled" record the real-node test prints, in order.
settled_states() {
    python3 - "$1" <<'PY'
import json, sys
lines = open(sys.argv[1], encoding="utf-8", errors="replace").read().split("\n")
states, i = [], 0
while i < len(lines):
    if lines[i].startswith("--- settled"):
        buf, j = [], i + 1
        while j < len(lines):
            buf.append(lines[j])
            if lines[j] == "}":
                break
            j += 1
        try:
            states.append(str(json.loads("\n".join(buf)).get("state")))
        except ValueError:
            states.append("unparsed")
        i = j
    i += 1
print(" ".join(states))
PY
}

echo; echo "## 9. companion through the proxy (app key) and its real-node test"
APP_KEY="$HOME/.config/gamecore-studio/app-key.json"
if etos --json agent list | grep -q '"gamecore-studio"' && [ -f "$APP_KEY" ]; then
    run etos agent list
    mkdir -p "$TMP" && chmod 700 "$TMP"
    ( umask 077; python3 -c 'import json,sys; print("Authorization: Bearer " + json.load(open(sys.argv[1]))["key"])' "$APP_KEY" > "$TMP/app-auth" )
    echo "\$ GET /api/v1/agents/gamecore-studio/http/v1/hello (app key)"
    code="$(curl -sS --noproxy '*' -m 30 -H @"$TMP/app-auth" "$API/agents/gamecore-studio/http/v1/hello" -o "$OUT/hello.json" -w '%{http_code}')"
    echo "HTTP $code"; python3 -m json.tool "$OUT/hello.json" 2>/dev/null || cat "$OUT/hello.json"; echo
    if [ "$code" = 200 ]; then record hello ok "$(head -c 200 "$OUT/hello.json" | tr '\n' ' ')"; else record hello FAILED "HTTP $code"; fi
    echo "\$ (cd studio/agent && STUDIO_REAL_APP_KEY=$APP_KEY STUDIO_REAL_ETOS=$BIN/etos cargo test --test real_node -- --ignored --nocapture --test-threads 1)"
    proxy_env=()
    if ss -ltn 2>/dev/null | grep -q '127.0.0.1:7897 '; then
        proxy_env=(http_proxy=http://127.0.0.1:7897 https_proxy=http://127.0.0.1:7897)
    fi
    if (cd "$REPO/studio/agent" && env -u HTTP_PROXY -u HTTPS_PROXY -u ALL_PROXY -u all_proxy "${proxy_env[@]}" \
            NO_PROXY=127.0.0.1,localhost no_proxy=127.0.0.1,localhost PATH="$HOME/.cargo/bin:$PATH" \
            STUDIO_REAL_APP_KEY="$APP_KEY" STUDIO_REAL_ETOS="$BIN/etos" \
            cargo test --locked --test real_node -- --ignored --nocapture --test-threads 1) > "$OUT/real-node.txt" 2>&1; then
        # Strict: the test accepts any terminal state; the environment check wants both
        # scenarios to end with a candidate (a worker task that ran and returned a change set).
        states="$(settled_states "$OUT/real-node.txt")"
        if [ "$states" = "candidate candidate" ]; then
            record real_node ok "$(grep -E '^test result' "$OUT/real-node.txt" | tail -n 1) outcomes: $states"
        else
            record real_node FAILED "test passed but the outcomes are '$states' (both must be candidate)"
        fi
    else
        record real_node FAILED "$(grep -E '^test result|panicked' "$OUT/real-node.txt" | tail -n 2 | tr '\n' ' ')"
    fi
    sed 's/^/  | /' "$OUT/real-node.txt" | tail -n 80
else
    record hello skipped "agent gamecore-studio not installed or no paired app key"
    record real_node skipped "agent gamecore-studio not installed or no paired app key"
fi

rm -rf "$TMP"

echo; echo "## results"
failed=0
for r in "${results[@]}"; do
    IFS='|' read -r name outcome detail <<< "$r"
    printf '  %-22s %-20s %s\n' "$name" "$outcome" "$detail"
    case "$outcome" in ok|"blocked (expected)"|skipped) ;; *) failed=1 ;; esac
done

{
    echo "# etos W0 verification, $DATE"
    echo
    echo "Produced by \`studio/etos/verify.sh\` on $(hostname) at $STAMP (UTC). The transcript is"
    echo "[transcript.txt](transcript.txt) (agent keys and signed URL parameters redacted; no provider key is ever read by this script)."
    echo
    echo "| Item | Value |"
    echo "|---|---|"
    echo "| Host | $(hostname), $(uname -sr), user $(id -un) |"
    echo "| etos | $(git -C "$HOME/wkspace/etos-studio" rev-parse HEAD 2>/dev/null) (branch $(git -C "$HOME/wkspace/etos-studio" rev-parse --abbrev-ref HEAD 2>/dev/null)), binaries per \`studio/etos/etos.lock\` |"
    echo "| game_core | ${GC_REV:-unknown} |"
    echo "| Node | root \`$ROOT\`, unit \`etosd.service\` (user), API \`127.0.0.1:7410\`, broker \`172.17.0.1:7411\` |"
    echo "| Key used | throwaway agent \`verify\` (grants ops, realtime, files; providers studio-voice), uninstalled with --purge at the end |"
    echo
    echo "| Check | Outcome | Detail |"
    echo "|---|---|---|"
    for r in "${results[@]}"; do
        IFS='|' read -r name outcome detail <<< "$r"
        echo "| $name | $outcome | $(echo "$detail" | tr '|' '/' | cut -c1-300) |"
    done
    echo
    echo "Artifacts: \`image.png\` (generate.image), \`describe.json\`, \`welcome.wav\` (tts), \`realtime.jsonl\`, \`hello.json\`, \`real-node.txt\`"
    echo "(one line per realtime event, audio replaced by its length), \`3d.refusal.json\`, and the job answers \`*.job.json\`."
} | redact > "$OUT/README.md"

echo
if [ "$failed" -eq 0 ]; then echo "verify.sh: all checks passed ($OUT)"; else echo "verify.sh: some checks FAILED ($OUT)"; fi
exit "$failed"
