#!/usr/bin/env bash
# GameCore Studio host bring-up (docs/studio/04-etos-integration.md §8). Idempotent: every step
# compares before it changes anything, prints `changed:` or `unchanged:`, and a second run ends
# with "nothing changed".
#
#   studio/etos/install.sh [--skip-images]      (run on the Linux host, from the game_core checkout)
#
# Prerequisites (done by the other host-*.sh scripts):
#   studio/tools/host-sync-etos.sh   (Mac)   etos source at the pinned commit -> ~/wkspace/etos-studio
#   studio/tools/host-build-etos.sh  (host)  ~/.local/opt/etos/bin/{etosd,etos,etos-musl}, etos.lock
#
# What it sets up (exact paths):
#   node root      ~/.local/share/etos-studio       ($ETOS_STUDIO_ROOT; see PACKET.md for why not
#                                                   ~/.local/share/etos, which holds an unrelated
#                                                   etos installation on this host)
#   config         <root>/etos.toml, models.toml, ops.toml   (from studio/etos/*.tmpl)
#   image layer    <root>/bin/etos                   (the static musl etos)
#   provider keys  ~/.config/gamecore-studio/providers.env (0600, generated here, never printed)
#   service        ~/.config/systemd/user/etosd.service (user unit, linger enabled)
#   API            127.0.0.1:7410 (SDK API), broker 172.17.0.1:7411, web UI 127.0.0.1:7400
#   images         localhost/etos-default:latest, localhost/gc-designer:current, localhost/gc-mechanic:current
#   workers        gc-designer, gc-mechanic, created (model alias `default`) before the agent so
#                  that `etos agent install` keeps their image and network; agent.toml sets the model
#   agent          gamecore-studio (studio/etos/agent, --link; binary built from studio/agent)
#   app            gamecore-unity, paired key ~/.config/gamecore-studio/app-key.json (0600)
set -euo pipefail

HERE="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
TOOLS="$HERE/../tools"
ROOT="${ETOS_STUDIO_ROOT:-$HOME/.local/share/etos-studio}"
BIN="${ETOS_STUDIO_BIN:-$HOME/.local/opt/etos/bin}"
ENVFILE="$HOME/.config/gamecore-studio/providers.env"
UNIT_DIR="$HOME/.config/systemd/user"
UNIT="etosd.service"
NODE_NAME="studio"
OWNER="$(id -un)"
SKIP_IMAGES=0
[ "${1:-}" = "--skip-images" ] && SKIP_IMAGES=1

export ETOS_ROOT="$ROOT"
export PATH="$BIN:$PATH"
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"

changes=0
restart=0
skipped=()
changed() { changes=$((changes + 1)); echo "  changed: $*"; }
same() { echo "  unchanged: $*"; }
step() { echo "== $*"; }
# place TMP DST MODE [restart]: install TMP as DST when the content or mode differs.
place() {
    local tmp="$1" dst="$2" mode="$3"
    if cmp -s "$tmp" "$dst" && [ "$(stat -c %a "$dst")" = "$mode" ]; then
        rm -f "$tmp"; same "$dst"
    else
        mkdir -p "$(dirname "$dst")"
        install -m "$mode" "$tmp" "$dst"; rm -f "$tmp"; changed "$dst"
        if [ "${4:-}" = restart ]; then restart=1; fi
    fi
}
render() { # TEMPLATE -> temp file path on stdout
    local tmp; tmp="$(mktemp)"
    sed -e "s|@NODE_NAME@|$NODE_NAME|g" -e "s|@OWNER@|$OWNER|g" -e "s|@ROOT@|$ROOT|g" \
        -e "s|@BIN@|$BIN|g" -e "s|@ENVFILE@|$ENVFILE|g" "$1" > "$tmp"
    echo "$tmp"
}
names() { # JSON on stdin -> every "name" value, one per line
    python3 -c 'import json,sys
def walk(v):
    if isinstance(v, dict):
        if isinstance(v.get("name"), str): print(v["name"])
        if isinstance(v.get("agent"), str): print(v["agent"])
        if isinstance(v.get("app"), str): print(v["app"])
        for x in v.values(): walk(x)
    elif isinstance(v, list):
        for x in v: walk(x)
walk(json.load(sys.stdin))'
}

step "preflight"
for b in etosd etos etos-musl; do
    [ -x "$BIN/$b" ] || { echo "missing $BIN/$b: run studio/tools/host-build-etos.sh" >&2; exit 1; }
done
id -nG | tr ' ' '\n' | grep -qx docker || { echo "$OWNER is not in the docker group" >&2; exit 1; }
docker info >/dev/null 2>&1 || { echo "docker is not reachable" >&2; exit 1; }
systemctl --user show-environment >/dev/null || { echo "no user systemd manager" >&2; exit 1; }
echo "  binaries in $BIN, docker reachable, user manager running"

step "node root $ROOT"
if [ -f "$ROOT/etos.toml" ]; then
    same "$ROOT (initialized)"
else
    # Not `--profile local` (that sets runtime = "none").
    "$BIN/etosd" init --root "$ROOT" --name "$NODE_NAME" --owner "$OWNER" | sed 's/^/    /'
    changed "$ROOT initialized (etosd init --name $NODE_NAME --owner $OWNER)"
    restart=1
fi

step "configuration"
place "$(render "$HERE/etos.toml.tmpl")" "$ROOT/etos.toml" 600 restart
place "$(render "$HERE/models.toml.tmpl")" "$ROOT/models.toml" 600 restart
place "$(render "$HERE/ops.toml.tmpl")" "$ROOT/ops.toml" 600 restart
tmp="$(mktemp)"; cp "$BIN/etos-musl" "$tmp"
place "$tmp" "$ROOT/bin/etos" 755

step "provider environment"
set +e
bash "$TOOLS/host-providers-env.sh" | sed 's/^/  /'
rc=${PIPESTATUS[0]}
set -e
case "$rc" in
0) ;;
3) changes=$((changes + 1)); restart=1 ;;
*) echo "host-providers-env.sh failed ($rc)" >&2; exit 1 ;;
esac

step "service $UNIT"
place "$(render "$HERE/etosd.service.tmpl")" "$UNIT_DIR/$UNIT" 644 restart
if [ "$restart" -eq 1 ]; then systemctl --user daemon-reload; fi
if systemctl --user is-enabled --quiet "$UNIT" 2>/dev/null; then
    same "$UNIT enabled"
else
    systemctl --user enable "$UNIT" 2>&1 | sed 's/^/    /'
    changed "$UNIT enabled"
fi
if [ "$(loginctl show-user "$OWNER" -p Linger --value 2>/dev/null)" = "yes" ]; then
    same "linger for $OWNER"
else
    loginctl enable-linger "$OWNER"
    changed "linger enabled for $OWNER"
fi
# A new etosd binary also needs a restart.
stamp="$ROOT/.studio-etosd.sha256"
now="$(sha256sum "$BIN/etosd" | cut -d' ' -f1)"
if [ "$(cat "$stamp" 2>/dev/null)" != "$now" ]; then restart=1; fi
if ! systemctl --user is-active --quiet "$UNIT"; then
    systemctl --user start "$UNIT"
    changed "$UNIT started"
elif [ "$restart" -eq 1 ]; then
    systemctl --user restart "$UNIT"
    changed "$UNIT restarted (configuration or binary changed)"
else
    same "$UNIT running"
fi
echo "$now" > "$stamp"
ok=0
for _ in $(seq 1 60); do
    if etos node status >/dev/null 2>&1 &&
       [ "$(curl -s -o /dev/null -w '%{http_code}' --noproxy '*' http://127.0.0.1:7410/api/v1/tasks/x)" = "401" ]; then
        ok=1; break
    fi
    sleep 1
done
if [ "$ok" -ne 1 ]; then
    echo "etosd is not healthy; journalctl --user -u $UNIT -n 50:" >&2
    journalctl --user -u "$UNIT" -n 50 --no-pager >&2 || true
    exit 1
fi
echo "  healthy: etos node status ok, SDK API answers on 127.0.0.1:7410"

step "images"
if [ "$SKIP_IMAGES" -eq 1 ]; then
    echo "  skipped (--skip-images)"
else
    out="$(bash "$TOOLS/host-build-images.sh")"
    echo "$out" | sed 's/^/  /'
    n="$(echo "$out" | grep -c '^  built' || true)"
    changes=$((changes + n))
fi

step "workers"
# etos refuses an allowlist with no hosts (`--network allowlist:` -> invalid network policy,
# etos crates/etbroker/src/db.rs NetworkPolicy::from_str); `offline` is the policy with no egress
# at all, which is what 04 §4 asks for (operations run on the node, not in the container).
have="$(etos --json worker list | names)"
for w in gc-designer gc-mechanic; do
    if echo "$have" | grep -qx "$w"; then
        same "worker $w"
    else
        etos worker create "$w" --image "localhost/$w:current" --network offline --model default \
            --instructions "GameCore Studio $w worker. Follow the task's request.md and the tool catalog." \
            | sed 's/^/    /'
        changed "worker $w created (image localhost/$w:current, network offline, model default)"
    fi
done

step "companion agent gamecore-studio"
# etos refuses `..` in an agent's paths, so the manifest's `bin/gamecore-studio` is a symlink
# (bin/ is git-ignored) to the release build of studio/agent, installed with --link.
AGENT_DIR="$HERE/agent"
AGENT_SRC="$HERE/../agent"
agents="$(etos --json agent list | names)"
if [ ! -f "$AGENT_DIR/agent.toml" ] || [ ! -f "$AGENT_SRC/Cargo.toml" ]; then
    skipped+=("agent: studio/etos/agent/agent.toml or studio/agent is absent (packet P0.5)")
else
    cargo_env=(env PATH="$HOME/.cargo/bin:$PATH")
    if ss -ltn 2>/dev/null | grep -q '127.0.0.1:7897 '; then
        cargo_env+=(http_proxy=http://127.0.0.1:7897 https_proxy=http://127.0.0.1:7897)
    fi
    if ! (cd "$AGENT_SRC" && "${cargo_env[@]}" cargo build --release --locked -q); then
        echo "cargo build --release in studio/agent failed" >&2; exit 1
    fi
    mkdir -p "$AGENT_DIR/bin"
    link="$AGENT_DIR/bin/gamecore-studio"
    target="../../../agent/target/release/gamecore-studio"
    if [ "$(readlink "$link" 2>/dev/null)" = "$target" ]; then
        same "$link -> $target"
    else
        ln -sfn "$target" "$link"; changed "$link -> $target"
    fi
    agent_stamp="$ROOT/.studio-agent.sha256"
    agent_now="$(cat "$AGENT_DIR/agent.toml" "$AGENT_DIR"/workers/*.md "$AGENT_SRC/target/release/gamecore-studio" | sha256sum | cut -d' ' -f1)"
    if ! echo "$agents" | grep -qx gamecore-studio; then
        etos agent install --link "$AGENT_DIR" | sed 's/^/    /'
        changed "agent gamecore-studio installed (--link $AGENT_DIR)"
    elif [ "$(cat "$agent_stamp" 2>/dev/null)" != "$agent_now" ]; then
        etos agent upgrade --link "$AGENT_DIR" | sed 's/^/    /'
        changed "agent gamecore-studio upgraded (manifest, instructions or binary changed)"
    else
        same "agent gamecore-studio installed"
    fi
    echo "$agent_now" > "$agent_stamp"
    state=""
    for _ in $(seq 1 30); do
        state="$(etos --json agent list | python3 -c 'import json,sys
for a in json.load(sys.stdin):
    if a.get("agent", a.get("name")) == "gamecore-studio": print(a.get("state", ""))')"
        [ "$state" = ready ] && break
        sleep 1
    done
    echo "  agent state: ${state:-unknown}"
    agents="gamecore-studio"
fi

step "Unity app gamecore-unity"
if echo "$agents" | grep -qx gamecore-studio; then
    apps="$(etos --json app list | names)"
    if echo "$apps" | grep -qx gamecore-unity; then
        same "app gamecore-unity installed"
    else
        etos app install "$HERE/app" | sed 's/^/    /'
        changed "app gamecore-unity installed"
    fi
    key="$HOME/.config/gamecore-studio/app-key.json"
    paired="$(etos --json app list | python3 -c 'import json,sys
d=json.load(sys.stdin); print(sum(1 for k in d.get("keys", []) if k.get("app") == "gamecore-unity"))')"
    if [ -f "$key" ] && [ "${paired:-0}" -gt 0 ]; then
        same "$key (paired)"
    else
        ( umask 077; etos app pair gamecore-unity --approve --out "$key" >/dev/null )
        chmod 600 "$key"
        changed "$key written (etos app pair gamecore-unity --approve)"
    fi
else
    skipped+=("app: needs the agent first; later: etos app install studio/etos/app")
    skipped+=("pairing: later: etos app pair gamecore-unity --approve --out ~/.config/gamecore-studio/app-key.json")
fi

echo
for s in "${skipped[@]}"; do echo "skipped $s"; done
if [ "$changes" -eq 0 ]; then
    echo "install.sh: nothing changed (already installed)"
else
    echo "install.sh: $changes change(s) applied"
fi
