#!/usr/bin/env bash
# P3.2 marker protocol with bounded playback and restoration of the prior source.
set -euo pipefail
out="$1"; shift
mkdir -p "$out/voice"
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"
original="$(wpctl inspect @DEFAULT_AUDIO_SOURCE@ | sed -n '1s/^id \([0-9]*\).*/\1/p')"
loopback_pid=''; player_pid=''
cleanup() {
  [[ -z "$player_pid" ]] || kill "$player_pid" 2>/dev/null || true
  [[ -z "$loopback_pid" ]] || kill "$loopback_pid" 2>/dev/null || true
  [[ -z "$original" ]] || wpctl set-default "$original" || true
}
trap cleanup EXIT
pw-loopback --capture-props='media.class=Audio/Sink node.name=gc_p42c_sink node.description=GC_P42c_sink' --playback-props='media.class=Audio/Source node.name=gc_p42c_mic node.description=GC_P42c_mic' >"$out/voice/pipewire.log" 2>&1 &
loopback_pid=$!
sleep 2
mic_id="$(wpctl status --name | sed -n '/Sources:/,/Source endpoints:/p' | grep -m1 gc_p42c_mic | grep -oE '[0-9]+\.' | head -n1 | tr -d '.')"
[[ -n "$mic_id" ]]
wpctl set-default "$mic_id"
wpctl status --name >"$out/voice/sources.txt"
(
  for label in move destructive; do
    for _ in $(seq 1 1800); do [[ ! -f "$out/voice/play-$label" ]] || break; sleep 1; done
    [[ -f "$out/voice/play-$label" ]] || exit 2
    sleep 1
    pw-play --target gc_p42c_sink "$out/voice/$label.wav" >>"$out/voice/play.log" 2>&1
    date -u +%FT%TZ >"$out/voice/played-$label"
  done
) &
player_pid=$!
export GAMECORE_P42C_MIC=gc_p42c_mic
"$@"
