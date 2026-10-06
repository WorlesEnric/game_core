#!/usr/bin/env bash
set -euo pipefail
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"
original="$(wpctl inspect @DEFAULT_AUDIO_SOURCE@ | sed -n '1s/^id \([0-9]*\).*/\1/p')"
pw-loopback --capture-props='media.class=Audio/Sink node.name=gc_p42d_sink node.description=GC_P42d_sink' --playback-props='media.class=Audio/Source node.name=gc_p42d_mic node.description=GC_P42d_mic' &
loop=$!
trap 'kill "$loop" 2>/dev/null || true; [[ -z "$original" ]] || wpctl set-default "$original"' EXIT
sleep 2
mic="$(wpctl status --name | sed -n '/Sources:/,/Source endpoints:/p' | grep -m1 gc_p42d_mic | grep -oE '[0-9]+\.' | head -n1 | tr -d '.')"
wpctl set-default "$mic"
export GAMECORE_P42C_MIC=gc_p42d_mic
bash studio/tools/verify-all.sh p42d voice-self
