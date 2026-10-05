#!/usr/bin/env bash
# Run a recorded destructive prompt through the actual virtual microphone and Studio prompt.
set -euo pipefail
out="$1"
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"
original="$(pactl get-default-source)"
loopback_pid=''
player_pid=''
cleanup() {
  [[ -z "$player_pid" ]] || kill "$player_pid" 2>/dev/null || true
  [[ -z "$loopback_pid" ]] || kill "$loopback_pid" 2>/dev/null || true
  pactl set-default-source "$original" || true
}
trap cleanup EXIT
cp artifacts/studio/workflows/P3.2/runs/voice-20261005T105318Z/voice/destructive.wav "$out/destructive.wav"
pw-loopback --capture-props='media.class=Audio/Sink node.name=gc_p42_sink node.description=GC_P42_sink' \
  --playback-props='media.class=Audio/Source node.name=gc_p42_mic node.description=GC_P42_mic' >"$out/pipewire.log" 2>&1 &
loopback_pid=$!
sleep 2
pactl set-default-source gc_p42_mic
pactl list short sources >"$out/sources.txt"
(
  for _ in $(seq 1 300); do
    if [[ -f "$out/play-destructive" ]]; then
      pw-play --target gc_p42_sink "$out/destructive.wav" >"$out/play.log" 2>&1
      date -u +%FT%TZ >"$out/played-destructive"
      exit
    fi
    sleep 1
  done
) &
player_pid=$!
unity_tools_dir="$PWD/studio/tools"
source "$unity_tools_dir/unity-slot.sh"
unity_slot_acquire
# Lease belongs to this shell and is released when it exits.
python3 studio/stage/run-redacted.py --log "$out/editor.log" --timeout 300 --silence 180 -- env \
 DISPLAY="${EVIDENCE_DISPLAY:-:1}" GAMECORE_ETOS_LIVE=1 GAMECORE_ETOS_AUTOSTART=1 GAMECORE_P42_EVIDENCE="$out" \
 "$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity" -projectPath "$PWD/games/hollowmere" -logFile - \
 -runTests -testPlatform EditMode -testFilter R2_38_W_VOICE_01_DestructiveSpeechNeverSubmits -testResults "$out/results.xml"
