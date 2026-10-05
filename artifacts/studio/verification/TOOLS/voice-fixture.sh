#!/usr/bin/env bash
# Run a recorded destructive prompt through the actual virtual microphone and Studio prompt.
set -euo pipefail
out="$1"
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"
original="$(wpctl inspect @DEFAULT_AUDIO_SOURCE@ | sed -n '1s/^id \([0-9]*\).*/\1/p')"
loopback_pid=''
player_pid=''
recorder_pid=''
cleanup() {
  [[ -z "$player_pid" ]] || kill "$player_pid" 2>/dev/null || true
  if [[ -n "$recorder_pid" ]]; then kill -INT "$recorder_pid" 2>/dev/null || true; wait "$recorder_pid" 2>/dev/null || true; fi
  [[ -z "$loopback_pid" ]] || kill "$loopback_pid" 2>/dev/null || true
  [[ -z "$original" ]] || wpctl set-default "$original" || true
}
trap cleanup EXIT
cp artifacts/studio/workflows/P3.2/runs/voice-20261005T105318Z/voice/destructive.wav "$out/destructive.wav"
pw-loopback --capture-props='media.class=Audio/Sink node.name=gc_p42_sink node.description=GC_P42_sink' \
  --playback-props='media.class=Audio/Source node.name=gc_p42_mic node.description=GC_P42_mic' >"$out/pipewire.log" 2>&1 &
loopback_pid=$!
sleep 2
mic_id="$(wpctl status --name | sed -n '/Sources:/,/Source endpoints:/p' | grep -m1 'gc_p42_mic' | grep -oE '[0-9]+\.' | head -n1 | tr -d '.')"
[[ -n "$mic_id" ]] || { echo 'PipeWire virtual source did not appear'; exit 2; }
wpctl set-default "$mic_id"
wpctl status --name >"$out/sources.txt"
pw-record --target gc_p42_mic --format s16 --rate 24000 --channels 1 "$out/virtual-source.wav" >"$out/record.log" 2>&1 &
recorder_pid=$!
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
 DISPLAY="${EVIDENCE_DISPLAY:-:1}" PULSE_SOURCE=gc_p42_mic GAMECORE_ETOS_KEY_FILE="$HOME/.config/gamecore-studio/app-key.json" GAMECORE_ETOS_LIVE=1 GAMECORE_ETOS_AUTOSTART=1 GAMECORE_P42_EVIDENCE="$out" \
 "$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity" -projectPath "$PWD/games/hollowmere" -logFile - \
 -runTests -testPlatform EditMode -testFilter R2_38_W_VOICE_01_DestructiveSpeechNeverSubmits -testResults "$out/results.xml"
