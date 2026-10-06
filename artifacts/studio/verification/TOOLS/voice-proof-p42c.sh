#!/usr/bin/env bash
set -euo pipefail
out="$1"; shift
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"
original="$(wpctl inspect @DEFAULT_AUDIO_SOURCE@ | sed -n '1s/^id \([0-9]*\).*/\1/p')"
loopback_pid=''; player_pid=''; recorder_pid=''
cleanup() {
  [[ -z "$player_pid" ]] || kill "$player_pid" 2>/dev/null || true
  if [[ -n "$recorder_pid" ]]; then kill -INT "$recorder_pid" 2>/dev/null || true; wait "$recorder_pid" 2>/dev/null || true; fi
  [[ -z "$loopback_pid" ]] || kill "$loopback_pid" 2>/dev/null || true
  [[ -z "$original" ]] || wpctl set-default "$original" || true
}
trap cleanup EXIT
cp "$GAMECORE_P42C_VOICE_FIXTURE" "$out/destructive.wav"
pw-loopback --capture-props='media.class=Audio/Sink node.name=gc_p42c_sink node.description=GC_P42c_sink' --playback-props='media.class=Audio/Source node.name=gc_p42c_mic node.description=GC_P42c_mic' >"$out/pipewire.log" 2>&1 &
loopback_pid=$!
sleep 2
mic_id="$(wpctl status --name | sed -n '/Sources:/,/Source endpoints:/p' | grep -m1 gc_p42c_mic | grep -oE '[0-9]+\.' | head -n1 | tr -d '.')"
[[ -n "$mic_id" ]]
wpctl set-default "$mic_id"
wpctl status --name >"$out/sources.txt"
pw-record --target gc_p42c_mic --format s16 --rate 24000 --channels 1 "$out/virtual-source.wav" >"$out/record.log" 2>&1 &
recorder_pid=$!
(
 for _ in $(seq 1 1800); do
  if [[ -f "$out/play-destructive" ]]; then
   pw-play --target gc_p42c_sink "$out/destructive.wav" >"$out/play.log" 2>&1
   date -u +%FT%TZ >"$out/played-destructive"
   exit
  fi
  sleep 1
 done
) &
player_pid=$!
export GAMECORE_P42C_MIC=gc_p42c_mic
"$@"
