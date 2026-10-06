#!/usr/bin/env bash
# omp-packet.sh - run one implementation packet with omp (non-interactive) ON THE LINUX HOST, in a clone of the hub.
# Same contract as codex-packet.sh; the implementer is omp with model echo/gpt-6-astra at thinking "medium".
#
# Usage:
#   studio/tools/omp-packet.sh start  <packet-name> <branch> <prompt-file> [--base <ref>]
#   studio/tools/omp-packet.sh status <packet-name>
#   studio/tools/omp-packet.sh log    <packet-name> [lines]
#   studio/tools/omp-packet.sh result <packet-name>        # prints the agent's final message
#   studio/tools/omp-packet.sh stop   <packet-name>
#
# What `start` does:
#   1. pushes <base> (default: the current branch's HEAD) to the hub as <branch> if the branch does not exist there;
#   2. on the host, makes ~/wkspace/gc-studio/<packet-name> a clone of the hub at <branch>
#      (fetch + checkout when it exists; local uncommitted tracked changes make it refuse);
#   3. starts `omp -p` there under nohup/setsid, detached from this ssh session, with the provider keys imported
#      from ~/.bashrc (they are exported after its non-interactive early return), stdin from /dev/null,
#      tool calls auto-approved (the host is the dedicated build machine), working directory = the clone,
#      final message in .omp/<packet>.last.md, transcript in .omp/<packet>.log.
#   The packet brief must tell the agent to commit on <branch> and `git push origin <branch>`; the integrator then
#   fetches from the hub (`git fetch host`) and merges.
#
# Owner rules: nothing is compiled, built or tested on the Mac; the agent runs on the host only.
#
# Environment:
#   GC_STUDIO_HOST   ssh host (default: myubuntu)
#   GC_STUDIO_HUB    bare hub on the host (default: wkspace/gc-studio/hub.git, relative to the host home)
#   GC_STUDIO_CLONES clone root on the host (default: wkspace/gc-studio)
#   OMP_MODEL        (default: echo/gpt-6-astra)   OMP_THINKING (default: medium)   OMP_MAX_TIME (default: 8h)
set -euo pipefail

host="${GC_STUDIO_HOST:-myubuntu}"
hub="${GC_STUDIO_HUB:-wkspace/gc-studio/hub.git}"
clones="${GC_STUDIO_CLONES:-wkspace/gc-studio}"
model="${OMP_MODEL:-echo/gpt-6-astra}"
thinking="${OMP_THINKING:-medium}"
max_time="${OMP_MAX_TIME:-8h}"

die() { echo "omp-packet: $*" >&2; exit 2; }
cmd="${1:-}"; shift || true
[ -n "$cmd" ] || die "usage: omp-packet.sh start|status|log|result|stop ..."

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
    scp -q "$prompt" "$host:/tmp/omp-$packet.prompt.md"
    ssh -o BatchMode=yes "$host" bash -s -- "$packet" "$branch" "$hub" "$clones" "$model" "$thinking" "$max_time" <<'REMOTE'
set -euo pipefail
packet="$1"; branch="$2"; hub="$3"; clones="$4"; model="$5"; thinking="$6"; max_time="$7"
dir="$HOME/$clones/$packet"
if [ ! -d "$dir/.git" ]; then
  git clone -q --reference-if-able "$HOME/wkspace/game_core" --branch "$branch" "$HOME/$hub" "$dir"
else
  if [ -n "$(git -C "$dir" status --short | grep -v '^??')" ]; then
    echo "-- refusing: $dir has uncommitted tracked changes"; exit 3
  fi
  git -C "$dir" fetch -q origin
  git -C "$dir" checkout -q -B "$branch" "origin/$branch"
fi
mkdir -p "$dir/.omp"
mv "/tmp/omp-$packet.prompt.md" "$dir/.omp/$packet.prompt.md"
if [ -f "$dir/.omp/$packet.pid" ] && kill -0 "$(cat "$dir/.omp/$packet.pid")" 2>/dev/null; then
  echo "-- omp already running for $packet (pid $(cat "$dir/.omp/$packet.pid"))"; exit 0
fi
export PATH="$HOME/.local/bin:$HOME/.cargo/bin:$HOME/.dotnet:$PATH"
# Provider keys and the outbound proxy are exported in ~/.bashrc after its non-interactive early return.
eval "$(grep -E '^export ([A-Z_]+_API_KEY|http_proxy|https_proxy|HTTP_PROXY|HTTPS_PROXY|all_proxy|ALL_PROXY|no_proxy|NO_PROXY)=' "$HOME/.bashrc" 2>/dev/null || true)"
cd "$dir"
# The final assistant message is the last text block of the print-mode output; the full transcript is the log.
nohup setsid bash -c '
  omp -p --model "$1" --thinking "$2" --approval-mode yolo --max-time "$3" --cwd "$4" --mode text \
      --no-title "@$4/.omp/$5.prompt.md" </dev/null > "$4/.omp/$5.log" 2>&1
  rc=$?
  awk "BEGIN{RS=\"\"; ORS=\"\\n\\n\"} {last=\$0} END{print last}" "$4/.omp/$5.log" > "$4/.omp/$5.last.md" 2>/dev/null || true
  echo "exit=$rc" >> "$4/.omp/$5.last.md"
' _ "$model" "$thinking" "$max_time" "$dir" "$packet" &
echo $! > "$dir/.omp/$packet.pid"
echo "-- omp started for $packet in $dir (pid $(cat "$dir/.omp/$packet.pid"), model $model, thinking $thinking, max-time $max_time)"
REMOTE
    ;;
  status)
    packet="${1:-}"; [ -n "$packet" ] || die "status needs <packet>"
    ssh -o BatchMode=yes "$host" bash -s -- "$packet" "$clones" <<'REMOTE'
dir="$HOME/$2/$1"; pidf="$dir/.omp/$1.pid"
if [ -f "$pidf" ] && kill -0 "$(cat "$pidf")" 2>/dev/null; then echo "running pid=$(cat "$pidf") since $(ps -o lstart= -p "$(cat "$pidf")")"; else echo "not running"; fi
[ -f "$dir/.omp/$1.last.md" ] && echo "last message: $(wc -c < "$dir/.omp/$1.last.md") bytes"
git -C "$dir" log --oneline -1 2>/dev/null
REMOTE
    ;;
  log)
    packet="${1:-}"; n="${2:-40}"; [ -n "$packet" ] || die "log needs <packet>"
    ssh -o BatchMode=yes "$host" "tail -n $n \$HOME/$clones/$packet/.omp/$packet.log"
    ;;
  result)
    packet="${1:-}"; [ -n "$packet" ] || die "result needs <packet>"
    ssh -o BatchMode=yes "$host" "cat \$HOME/$clones/$packet/.omp/$packet.last.md"
    ;;
  stop)
    packet="${1:-}"; [ -n "$packet" ] || die "stop needs <packet>"
    ssh -o BatchMode=yes "$host" bash -s -- "$packet" "$clones" <<'REMOTE'
dir="$HOME/$2/$1"; pidf="$dir/.omp/$1.pid"
if [ -f "$pidf" ]; then pid="$(cat "$pidf")"; pkill -TERM -s "$pid" 2>/dev/null || kill -TERM "$pid" 2>/dev/null || true; echo "stopped session $pid"; fi
REMOTE
    ;;
  *) die "unknown command $cmd";;
esac
