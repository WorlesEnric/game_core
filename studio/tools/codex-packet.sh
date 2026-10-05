#!/usr/bin/env bash
# codex-packet.sh - run one implementation packet with the Codex CLI ON THE LINUX HOST, in a clone of the hub.
#
# Usage:
#   studio/tools/codex-packet.sh start  <packet-name> <branch> <prompt-file> [--base <ref>]
#   studio/tools/codex-packet.sh status <packet-name>
#   studio/tools/codex-packet.sh log    <packet-name> [lines]
#   studio/tools/codex-packet.sh result <packet-name>        # prints the agent's final message
#   studio/tools/codex-packet.sh stop   <packet-name>
#
# What `start` does:
#   1. pushes <base> (default: the current branch's HEAD) to the hub as <branch> if the branch does not exist there;
#   2. on the host, makes ~/wkspace/gc-studio/<packet-name> a clone of the hub at <branch>
#      (git clone --reference-if-able ~/wkspace/game_core; fetch + reset when it exists);
#   3. copies the prompt file to the host and starts `codex exec` there under nohup, detached from this ssh session,
#      with model gpt-6-astra and reasoning effort high, full automation (the host is the dedicated build machine),
#      working directory = the clone, final message written to .codex/<packet>.last.md, transcript to .codex/<packet>.log.
#   The packet brief must tell Codex to commit on <branch> and `git push origin <branch>`; the integrator then
#   fetches from the hub (`git fetch host`) and merges.
#
# Owner rules: nothing is compiled, built or tested on the Mac; Codex runs on the host only.
#
# Environment:
#   GC_STUDIO_HOST   ssh host (default: myubuntu)
#   GC_STUDIO_HUB    bare hub on the host (default: wkspace/gc-studio/hub.git, relative to the host home)
#   GC_STUDIO_CLONES clone root on the host (default: wkspace/gc-studio)
#   CODEX_MODEL      (default: gpt-6-astra)   CODEX_EFFORT (default: high)
set -euo pipefail

host="${GC_STUDIO_HOST:-myubuntu}"
hub="${GC_STUDIO_HUB:-wkspace/gc-studio/hub.git}"
clones="${GC_STUDIO_CLONES:-wkspace/gc-studio}"
model="${CODEX_MODEL:-gpt-6-astra}"
effort="${CODEX_EFFORT:-high}"

die() { echo "codex-packet: $*" >&2; exit 2; }
cmd="${1:-}"; shift || true
[ -n "$cmd" ] || die "usage: codex-packet.sh start|status|log|result|stop ..."

case "$cmd" in
  start)
    packet="${1:-}"; branch="${2:-}"; prompt="${3:-}"; shift 3 || die "start needs <packet> <branch> <prompt-file>"
    base="HEAD"
    while [ $# -gt 0 ]; do case "$1" in --base) base="$2"; shift 2;; *) die "unknown option $1";; esac; done
    [ -f "$prompt" ] || die "prompt file not found: $prompt"
    case "$packet" in *[!A-Za-z0-9._-]*|"") die "bad packet name";; esac
    sha="$(git rev-parse "$base")"
    if git ls-remote --exit-code --heads host "$branch" >/dev/null 2>&1; then
      echo "-- hub already has $branch (not moved)"
    else
      git push -q host "$sha:refs/heads/$branch"
      echo "-- pushed $sha as $branch"
    fi
    scp -q "$prompt" "$host:/tmp/codex-$packet.prompt.md"
    ssh -o BatchMode=yes "$host" bash -s -- "$packet" "$branch" "$hub" "$clones" "$model" "$effort" <<'REMOTE'
set -euo pipefail
packet="$1"; branch="$2"; hub="$3"; clones="$4"; model="$5"; effort="$6"
dir="$HOME/$clones/$packet"
if [ ! -d "$dir/.git" ]; then
  git clone -q --reference-if-able "$HOME/wkspace/game_core" --branch "$branch" "$HOME/$hub" "$dir"
else
  git -C "$dir" fetch -q origin
  git -C "$dir" checkout -q -B "$branch" "origin/$branch"
fi
mkdir -p "$dir/.codex"
mv "/tmp/codex-$packet.prompt.md" "$dir/.codex/$packet.prompt.md"
if [ -f "$dir/.codex/$packet.pid" ] && kill -0 "$(cat "$dir/.codex/$packet.pid")" 2>/dev/null; then
  echo "-- codex already running for $packet (pid $(cat "$dir/.codex/$packet.pid"))"; exit 0
fi
export PATH="$HOME/.local/bin:$HOME/.cargo/bin:$HOME/.dotnet:$PATH"
cd "$dir"
nohup setsid codex exec \
  -m "$model" -c "model_reasoning_effort=\"$effort\"" \
  --dangerously-bypass-approvals-and-sandbox --skip-git-repo-check \
  -C "$dir" -o "$dir/.codex/$packet.last.md" \
  - < "$dir/.codex/$packet.prompt.md" > "$dir/.codex/$packet.log" 2>&1 &
echo $! > "$dir/.codex/$packet.pid"
echo "-- codex started for $packet in $dir (pid $(cat "$dir/.codex/$packet.pid"), model $model, effort $effort)"
REMOTE
    ;;
  status)
    packet="${1:-}"; [ -n "$packet" ] || die "status needs <packet>"
    ssh -o BatchMode=yes "$host" bash -s -- "$packet" "$clones" <<'REMOTE'
dir="$HOME/$2/$1"; pidf="$dir/.codex/$1.pid"
if [ -f "$pidf" ] && kill -0 "$(cat "$pidf")" 2>/dev/null; then echo "running pid=$(cat "$pidf") since $(ps -o lstart= -p "$(cat "$pidf")")"; else echo "not running"; fi
[ -f "$dir/.codex/$1.last.md" ] && echo "last message: $(wc -c < "$dir/.codex/$1.last.md") bytes"
git -C "$dir" log --oneline -1 2>/dev/null
REMOTE
    ;;
  log)
    packet="${1:-}"; n="${2:-40}"; [ -n "$packet" ] || die "log needs <packet>"
    ssh -o BatchMode=yes "$host" "tail -n $n \$HOME/$clones/$packet/.codex/$packet.log"
    ;;
  result)
    packet="${1:-}"; [ -n "$packet" ] || die "result needs <packet>"
    ssh -o BatchMode=yes "$host" "cat \$HOME/$clones/$packet/.codex/$packet.last.md"
    ;;
  stop)
    packet="${1:-}"; [ -n "$packet" ] || die "stop needs <packet>"
    ssh -o BatchMode=yes "$host" bash -s -- "$packet" "$clones" <<'REMOTE'
dir="$HOME/$2/$1"; pidf="$dir/.codex/$1.pid"
if [ -f "$pidf" ]; then pid="$(cat "$pidf")"; pkill -TERM -s "$pid" 2>/dev/null || kill -TERM "$pid" 2>/dev/null || true; echo "stopped session $pid"; fi
REMOTE
    ;;
  *) die "unknown command $cmd";;
esac
