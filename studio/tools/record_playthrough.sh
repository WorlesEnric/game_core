#!/usr/bin/env bash
# record_playthrough.sh - record a Hollowmere playthrough of the built Linux player on the host display :1 (P3.1,
# W-GAME-06/07 and B-FRAME evidence).
#
# Usage:   studio/tools/record_playthrough.sh <packet-name> <autoplay-script> [--minutes N] [--label name]
# Example: studio/tools/record_playthrough.sh p3.1 games/hollowmere/Autoplay/full-playthrough.autoplay --minutes 11
#
#   <autoplay-script>  path relative to the packet clone (or absolute on the host); passed to the player as -autoplay
#   --minutes N        recording budget in minutes (default 11). The player is expected to quit by itself (autoplay
#                      `quit`); it is stopped after N minutes + 5 minutes grace.
#   --label name       evidence folder name (default: playthrough-<UTC timestamp>)
#
# Needs the player built by studio/tools/build_game_player.sh <packet> (build/HollowmereLinux/Hollowmere.x86_64 in the
# clone). Run from the Mac, the script re-runs the clone's copy on the host over ssh; nothing runs on the Mac.
#
# On the host it:
#   1. refuses when a Hollowmere player or an x11grab recording of :1 is already running, or :1 is not answering;
#   2. stops the user service etosd.service (systemctl --user) for the whole run and restarts it on exit (trap), only
#      if it was active - "etosd stopped" for the recording;
#   3. network: probes `unshare -rn true`; when user namespaces are allowed the player runs inside `unshare -rn` (no
#      network at all). When they are not (this host: kernel.apparmor_restrict_unprivileged_userns=1) it falls back to
#      evidence: `ss -tunapH` snapshots every 5 s filtered to the player PID, written to network.txt, with the verdict
#      "no inet sockets opened by the player" or the offending lines;
#   4. records :1 with ffmpeg x11grab (1920x1080 -> 1280x720, 30 fps, libx264, bitrate sized so the file stays
#      <= 24 MB, cap 280 kbit/s video) plus the PulseAudio default monitor as 32 kbit/s mono AAC when it is reachable;
#   5. launches the player fullscreen on :1:  -frameLog <dir>/frame-log.csv -autoplay <script> -saveDir <dir>/saves
#      -logFile <dir>/player.log
#   6. afterwards writes: keyframes/kf-<sec>s.png every 30 s (each <= 300 KB), frame-stats.json (B-FRAME verdict),
#      save-headers.json, record-summary.json, playthrough.sha256 (sha256 of every evidence file).
#
# Evidence goes to ~/wkspace/gc-studio/evidence/<packet>/<label>/ on the host (videos stay there; the packet commits
# the sha256 + path + keyframes + json).
#
# B-FRAME (frame-stats.json): the steady window starts at the first frame-log row whose marker contains "ready"
# (else after the first 120 frames). Verdict pass = p95(dt) <= 16.7 ms AND no frame > 100 ms outside the first second
# after a transition marker ("region:", "restore", "load") AND every transition hitch (max dt within 1 s after the
# marker) <= 250 ms AND the steady window lasts >= --minutes x 60 s x 0.9 (0.9 because the budget includes boot).
#
# Environment: GC_STUDIO_HOST (default myubuntu), GC_STUDIO_REMOTE_BASE (default wkspace/gc-studio),
#              RECORD_STOP_UNITS (default "etosd.service"; user units stopped for the run)
#
# Exit codes: 0 recorded and the player exited 0; 3 the player exited non-zero (autoplay failure = 3) or was stopped;
# 2 refused / bad usage. The B-FRAME verdict is reported, not turned into an exit code.
set -euo pipefail

usage() {
  sed -n '2,45p' "${BASH_SOURCE[0]:-$0}" 2>/dev/null | sed 's/^# \{0,1\}//' >&2 || true
  exit 2
}

[[ $# -ge 2 ]] || usage
packet="$1"
script_arg="$2"
shift 2
minutes=11
label="playthrough-$(date -u +%Y%m%dT%H%M%SZ)"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --minutes) [[ $# -ge 2 ]] || usage; minutes="$2"; shift 2 ;;
    --label) [[ $# -ge 2 ]] || usage; label="$2"; shift 2 ;;
    *) echo "record_playthrough.sh: unknown argument '$1'" >&2; usage ;;
  esac
done
if ! [[ "${packet}" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]]; then
  echo "record_playthrough.sh: packet name must match [A-Za-z0-9][A-Za-z0-9._-]*" >&2
  exit 2
fi
if ! [[ "${label}" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]]; then
  echo "record_playthrough.sh: label must match [A-Za-z0-9][A-Za-z0-9._-]*" >&2
  exit 2
fi
if ! [[ "${minutes}" =~ ^[0-9]+$ ]] || (( minutes < 1 || minutes > 60 )); then
  echo "record_playthrough.sh: --minutes takes 1..60" >&2
  exit 2
fi

host="${GC_STUDIO_HOST:-myubuntu}"
remote_base="${GC_STUDIO_REMOTE_BASE:-wkspace/gc-studio}"

# Off the host: run the clone's own copy on the host.
if [[ "${GC_STUDIO_ON_HOST:-0}" != "1" && "$(uname -s)" != "Linux" ]]; then
  remote_script="\${HOME}/${remote_base}/${packet}/studio/tools/record_playthrough.sh"
  remote_env="GC_STUDIO_ON_HOST=1 GC_STUDIO_REMOTE_BASE=$(printf '%q' "${remote_base}")"
  if [[ -n "${RECORD_STOP_UNITS:-}" ]]; then
    remote_env+=" RECORD_STOP_UNITS=$(printf '%q' "${RECORD_STOP_UNITS}")"
  fi
  rc=0
  # shellcheck disable=SC2029  # quoting is done here on purpose (printf %q)
  ssh -o BatchMode=yes "${host}" "${remote_env} bash ${remote_script} $(printf '%q ' "${packet}" "${script_arg}" --minutes "${minutes}" --label "${label}")" \
    < /dev/null || rc=$?
  exit "${rc}"
fi

clone="${HOME}/${remote_base}/${packet}"
player="${clone}/build/HollowmereLinux/Hollowmere.x86_64"
out="${HOME}/${remote_base}/evidence/${packet}/${label}"
stop_units="${RECORD_STOP_UNITS-etosd.service}"
export DISPLAY=":1"
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"

case "${script_arg}" in
  /*) autoplay="${script_arg}" ;;
  *) autoplay="${clone}/${script_arg}" ;;
esac

for tool in ffmpeg python3 sha256sum ss pgrep xdpyinfo systemctl timeout; do
  command -v "${tool}" >/dev/null 2>&1 || { echo "record_playthrough.sh: '${tool}' is required" >&2; exit 2; }
done
if [[ ! -x "${player}" ]]; then
  echo "record_playthrough.sh: no player at ${player} (run studio/tools/build_game_player.sh ${packet} first)" >&2
  exit 2
fi
if [[ ! -f "${autoplay}" ]]; then
  echo "record_playthrough.sh: no autoplay script at ${autoplay}" >&2
  exit 2
fi
if pgrep -f 'Hollowmere\.x86_64' >/dev/null 2>&1; then
  echo "record_playthrough.sh: a Hollowmere player is already running; refusing" >&2
  pgrep -af 'Hollowmere\.x86_64' >&2 || true
  exit 2
fi
if pgrep -af ffmpeg 2>/dev/null | grep -q 'x11grab.*:1'; then
  echo "record_playthrough.sh: another x11grab recording of :1 is running; refusing" >&2
  exit 2
fi
if ! xdpyinfo -display :1 >/dev/null 2>&1; then
  echo "record_playthrough.sh: display :1 is not answering (xdpyinfo -display :1)" >&2
  exit 2
fi
if [[ -e "${out}" ]]; then
  echo "record_playthrough.sh: ${out} already exists; pick another --label" >&2
  exit 2
fi
mkdir -p "${out}/keyframes" "${out}/saves"
cp "${autoplay}" "${out}/$(basename "${autoplay}")"
revision="$(git -C "${clone}" rev-parse HEAD 2>/dev/null || echo unknown)"
exe_sha="$(sha256sum "${player}" | cut -d' ' -f1)"

echo "== Hollowmere playthrough recording =="
echo "revision : ${revision}"
echo "player   : ${player} (sha256 ${exe_sha})"
echo "autoplay : ${autoplay}"
echo "budget   : ${minutes} min (+5 min grace)"
echo "evidence : ${out}"

# ------------------------------------------------------------------------------------------- services (etosd stopped)
stopped_units=()
ffmpeg_pid=""
player_pid=""
watch_pid=""
cleanup() {
  local unit
  if [[ -n "${watch_pid}" ]]; then kill "${watch_pid}" 2>/dev/null || true; fi
  if [[ -n "${player_pid}" ]] && kill -0 "${player_pid}" 2>/dev/null; then kill -TERM "${player_pid}" 2>/dev/null || true; fi
  if [[ -n "${ffmpeg_pid}" ]] && kill -0 "${ffmpeg_pid}" 2>/dev/null; then kill -INT "${ffmpeg_pid}" 2>/dev/null || true; sleep 3; fi
  for unit in "${stopped_units[@]+"${stopped_units[@]}"}"; do
    if systemctl --user start "${unit}"; then
      echo "-- restarted ${unit} ($(systemctl --user is-active "${unit}" 2>/dev/null || true))"
    else
      echo "-- WARNING: could not restart ${unit}; start it with: systemctl --user start ${unit}" >&2
    fi
  done
  stopped_units=()
}
trap cleanup EXIT
trap 'exit 143' TERM INT HUP

services_txt="${out}/services.txt"
: > "${services_txt}"
for unit in ${stop_units}; do
  state="$(systemctl --user is-active "${unit}" 2>/dev/null || true)"
  if [[ "${state}" == "active" ]]; then
    systemctl --user stop "${unit}"
    stopped_units+=("${unit}")
    echo "${unit}: was active, stopped for the recording ($(systemctl --user is-active "${unit}" 2>/dev/null || true))" | tee -a "${services_txt}"
  else
    echo "${unit}: ${state:-unknown} (left alone)" | tee -a "${services_txt}"
  fi
done
echo "etosd processes during the run: $(pgrep -c -x etosd 2>/dev/null || true)" | tee -a "${services_txt}"

# ------------------------------------------------------------------------------------------------- network isolation
net_mode="ss-snapshots"
launcher=()
if unshare -rn true 2>/dev/null; then
  net_mode="unshare-rn"
  launcher=(unshare -rn)
fi
echo "network  : ${net_mode}"

# ------------------------------------------------------------------------------------------------------- recording
budget_s=$(( minutes * 60 + 60 ))
audio_args=()
audio="none"
audio_kbps=0
if timeout 5 ffmpeg -hide_banner -nostdin -loglevel error -f pulse -i @DEFAULT_MONITOR@ -t 0.5 -f null - >/dev/null 2>&1; then
  audio="pulse @DEFAULT_MONITOR@"
  audio_kbps=32
  audio_args=(-f pulse -thread_queue_size 1024 -i @DEFAULT_MONITOR@)
fi
video_kbps=$(( 24 * 8 * 1000 * 95 / 100 / budget_s - audio_kbps ))
if (( video_kbps > 280 )); then video_kbps=280; fi
if (( video_kbps < 120 )); then video_kbps=120; fi
video="${out}/playthrough.mp4"
encode_args=(-c:v libx264 -preset veryfast -tune zerolatency -pix_fmt yuv420p -b:v "${video_kbps}k" -maxrate "${video_kbps}k"
  -bufsize "$(( video_kbps * 2 ))k" -g 120)
if [[ "${audio}" != "none" ]]; then
  encode_args+=(-c:a aac -b:a "${audio_kbps}k" -ac 1)
fi
echo "recorder : x11grab :1 1920x1080 -> 1280x720 @30, video ${video_kbps} kbit/s, audio ${audio}"
ffmpeg -hide_banner -nostdin -loglevel warning -y -f x11grab -draw_mouse 0 -framerate 30 -video_size 1920x1080 \
  -thread_queue_size 1024 -i :1.0 "${audio_args[@]+"${audio_args[@]}"}" -vf scale=1280:720 "${encode_args[@]}" \
  -movflags +faststart -t "$(( minutes * 60 + 300 ))" "${video}" > "${out}/ffmpeg.log" 2>&1 &
ffmpeg_pid=$!
sleep 2
if ! kill -0 "${ffmpeg_pid}" 2>/dev/null; then
  echo "record_playthrough.sh: ffmpeg did not start; see ${out}/ffmpeg.log" >&2
  tail -n 20 "${out}/ffmpeg.log" >&2 || true
  ffmpeg_pid=""
  exit 2
fi

# ---------------------------------------------------------------------------------------------------------- player
record_start="$(date +%s)"
"${launcher[@]+"${launcher[@]}"}" "${player}" -screen-fullscreen 1 -screen-width 1920 -screen-height 1080 \
  -frameLog "${out}/frame-log.csv" -autoplay "${autoplay}" -saveDir "${out}/saves" -logFile "${out}/player.log" \
  < /dev/null > "${out}/player-stdout.txt" 2>&1 &
player_pid=$!
echo "player pid ${player_pid}"

network_txt="${out}/network.txt"
{
  echo "mode: ${net_mode}"
  echo "player pid: ${player_pid}"
  echo "== ss -tunapH before launch (host-wide) =="
  ss -tunapH 2>/dev/null || true
} > "${network_txt}"
: > "${out}/network-snapshots.txt"
(
  while kill -0 "${player_pid}" 2>/dev/null; do
    # The player's own PID and any child it spawned (with unshare the player is the launcher's child).
    pids="${player_pid} $(pgrep -P "${player_pid}" 2>/dev/null | tr '\n' ' ')"
    lines=""
    for pid in ${pids}; do
      lines+="$(ss -tunapH 2>/dev/null | grep -E "pid=${pid}," || true)"
    done
    printf '== %s ==\n%s\n' "$(date -u +%H:%M:%S)" "${lines}" >> "${out}/network-snapshots.txt"
    sleep 5
  done
) &
watch_pid=$!

limit_s=$(( minutes * 60 + 300 ))
player_rc=0
stopped=0
while kill -0 "${player_pid}" 2>/dev/null; do
  if (( $(date +%s) - record_start > limit_s )); then
    echo "-- player still running after ${limit_s}s: stopping it" >&2
    stopped=1
    kill -TERM "${player_pid}" 2>/dev/null || true
    sleep 10
    kill -KILL "${player_pid}" 2>/dev/null || true
    break
  fi
  sleep 2
done
wait "${player_pid}" 2>/dev/null || player_rc=$?
player_pid=""
played_s=$(( $(date +%s) - record_start ))
echo "player exited ${player_rc} after ${played_s}s"
sleep 2
kill -INT "${ffmpeg_pid}" 2>/dev/null || true
wait "${ffmpeg_pid}" 2>/dev/null || true
ffmpeg_pid=""
kill "${watch_pid}" 2>/dev/null || true
wait "${watch_pid}" 2>/dev/null || true
watch_pid=""

offending="$(grep -vE '^(== |$)' "${out}/network-snapshots.txt" 2>/dev/null || true)"
{
  echo "== snapshots of sockets owned by the player (every 5 s): network-snapshots.txt =="
  if [[ "${net_mode}" == "unshare-rn" ]]; then
    echo "verdict: player ran in a private network namespace (unshare -rn): no network"
  elif [[ -z "${offending}" ]]; then
    echo "verdict: no inet sockets opened by the player"
  else
    echo "verdict: the player opened inet sockets:"
    printf '%s\n' "${offending}"
  fi
  echo "== ss -tunapH after exit (host-wide) =="
  ss -tunapH 2>/dev/null || true
} >> "${network_txt}"

# Services back before the slow post-processing.
cleanup

# ------------------------------------------------------------------------------------------------------- keyframes
ffmpeg -hide_banner -nostdin -loglevel error -y -i "${video}" \
  -vf "select='isnan(prev_selected_t)+gte(t-prev_selected_t\,30)',scale=960:540" -vsync vfr \
  -compression_level 9 "${out}/keyframes/raw-%04d.png" || true
index=0
for raw in "${out}"/keyframes/raw-*.png; do
  [[ -f "${raw}" ]] || continue
  name="$(printf 'kf-%04ds.png' $(( index * 30 )))"
  mv "${raw}" "${out}/keyframes/${name}"
  if (( $(stat -c %s "${out}/keyframes/${name}") > 300000 )); then
    ffmpeg -hide_banner -nostdin -loglevel error -y -i "${out}/keyframes/${name}" -vf scale=640:360 -compression_level 9 \
      "${out}/keyframes/small.png" && mv "${out}/keyframes/small.png" "${out}/keyframes/${name}"
  fi
  if (( $(stat -c %s "${out}/keyframes/${name}") > 300000 )); then
    ffmpeg -hide_banner -nostdin -loglevel error -y -i "${out}/keyframes/${name}" -vf scale=480:270 -compression_level 9 \
      "${out}/keyframes/small.png" && mv "${out}/keyframes/small.png" "${out}/keyframes/${name}"
  fi
  index=$(( index + 1 ))
done

# ----------------------------------------------------------------------------------- frame stats, saves, summary
python3 - "${out}" "${minutes}" "${revision}" "${exe_sha}" "${player_rc}" "${played_s}" "${stopped}" "${net_mode}" \
  "${audio}" "${video_kbps}" "${label}" "${packet}" <<'PY'
import hashlib, json, os, sys
out, minutes, revision, exe_sha, player_rc, played_s, stopped, net_mode, audio, video_kbps, label, packet = sys.argv[1:13]
minutes = int(minutes)


def percentile(values, q):
    if not values:
        return None
    ordered = sorted(values)
    k = (len(ordered) - 1) * q
    lo = int(k)
    hi = min(lo + 1, len(ordered) - 1)
    return round(ordered[lo] + (ordered[hi] - ordered[lo]) * (k - lo), 3)


rows = []
log_path = os.path.join(out, "frame-log.csv")
if os.path.exists(log_path):
    with open(log_path, encoding="utf-8", errors="replace") as handle:
        for line in handle:
            if line.startswith("#") or line.startswith("frame,") or not line.strip():
                continue
            parts = line.rstrip("\n").split(",", 4)
            if len(parts) < 5:
                continue
            try:
                rows.append((int(parts[0]), float(parts[1]), float(parts[2]), parts[3], parts[4]))
            except ValueError:
                continue

start = None
for i, row in enumerate(rows):
    if "ready" in row[4]:
        start = i
        break
start_rule = "first 'ready' marker"
if start is None:
    start = min(120, len(rows))
    start_rule = "no 'ready' marker: after the first 120 frames"
steady = rows[start:]
transitions = [r for r in steady if any(tag in r[4] for tag in ("region:", "restore", "load"))]
transition_times = [r[1] for r in transitions]


def after_transition(t):
    return any(0.0 <= t - tt <= 1.0 for tt in transition_times)


dts = [r[2] for r in steady]
over100 = [{"frame": r[0], "time_s": r[1], "dt_ms": r[2], "region": r[3]} for r in steady if r[2] > 100.0 and not after_transition(r[1])]
hitches = []
for tr in transitions:
    window = [r[2] for r in steady if 0.0 <= r[1] - tr[1] <= 1.0]
    hitches.append({"frame": tr[0], "time_s": tr[1], "marker": tr[4], "max_dt_ms": round(max(window), 3) if window else None})
duration = round(steady[-1][1] - steady[0][1], 3) if len(steady) > 1 else 0.0
p95 = percentile(dts, 0.95)
regions = {}
for r in steady:
    regions.setdefault(r[3] or "-", []).append(r[2])
checks = {
    "p95_le_16_7ms": p95 is not None and p95 <= 16.7,
    "no_frame_over_100ms_outside_transitions": len(over100) == 0,
    "transition_hitch_le_250ms": all(h["max_dt_ms"] is None or h["max_dt_ms"] <= 250.0 for h in hitches),
    "duration_ge_budget": duration >= minutes * 60 * 0.9,
}
stats = {
    "budget": "B-FRAME: p95 <= 16.7 ms over the run; no frame > 100 ms outside the first second after a region transition; transition hitch <= 250 ms",
    "frameLog": "frame-log.csv",
    "rowsTotal": len(rows),
    "steadyStartRule": start_rule,
    "steadyFrames": len(steady),
    "steadySeconds": duration,
    "dtMs": {"p50": percentile(dts, 0.5), "p95": p95, "p99": percentile(dts, 0.99), "max": round(max(dts), 3) if dts else None,
             "mean": round(sum(dts) / len(dts), 3) if dts else None},
    "fpsMean": round(len(dts) / duration, 2) if duration > 0 else None,
    "framesOver16_7ms": sum(1 for d in dts if d > 16.7),
    "framesOver100msOutsideTransitions": over100[:50],
    "framesOver100msOutsideTransitionsCount": len(over100),
    "transitions": hitches,
    "perRegion": {k: {"frames": len(v), "p95": percentile(v, 0.95), "max": round(max(v), 3)} for k, v in sorted(regions.items())},
    "checks": checks,
    "verdict": "pass" if all(checks.values()) else "fail",
}
with open(os.path.join(out, "frame-stats.json"), "w", encoding="utf-8") as handle:
    json.dump(stats, handle, indent=2)
    handle.write("\n")

saves = []
save_dir = os.path.join(out, "saves")
for base, _, names in os.walk(save_dir):
    for name in sorted(names):
        path = os.path.join(base, name)
        with open(path, "rb") as handle:
            data = handle.read()
        entry = {"file": os.path.relpath(path, save_dir), "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest(),
                 "headHex": data[:64].hex()}
        try:
            parsed = json.loads(data.decode("utf-8"))
            if isinstance(parsed, dict):
                entry["jsonScalars"] = {k: v for k, v in parsed.items() if not isinstance(v, (list, dict))}
        except (UnicodeDecodeError, ValueError):
            pass
        saves.append(entry)
with open(os.path.join(out, "save-headers.json"), "w", encoding="utf-8") as handle:
    json.dump({"saveDir": "saves", "files": saves}, handle, indent=2)
    handle.write("\n")

video = os.path.join(out, "playthrough.mp4")
video_bytes = os.path.getsize(video) if os.path.exists(video) else 0
summary = {
    "packet": packet,
    "label": label,
    "revision": revision,
    "playerSha256": exe_sha,
    "playerExit": int(player_rc),
    "playerStoppedByTimeout": stopped == "1",
    "playedSeconds": int(played_s),
    "network": net_mode,
    "audio": audio,
    "videoKbps": int(video_kbps),
    "video": "playthrough.mp4",
    "videoBytes": video_bytes,
    "videoFitsRepo": 0 < video_bytes <= 25 * 1024 * 1024,
    "hostPath": out,
    "keyframes": sorted(os.listdir(os.path.join(out, "keyframes"))),
    "bFrameVerdict": stats["verdict"],
    "saveFiles": len(saves),
}
with open(os.path.join(out, "record-summary.json"), "w", encoding="utf-8") as handle:
    json.dump(summary, handle, indent=2)
    handle.write("\n")
PY

(cd "${out}" && find . -type f ! -name playthrough.sha256 -printf '%P\n' | LC_ALL=C sort | while IFS= read -r f; do sha256sum "${f}"; done) \
  > "${out}/playthrough.sha256"

echo "-- network: $(grep '^verdict' "${network_txt}" | head -n 1 || true)"
echo "-- autoplay: $(grep -E '\[autoplay\] (FAILED|finished|quit)' "${out}/player.log" 2>/dev/null | tail -n 1 || true)"
echo "-- record-summary.json:"
cat "${out}/record-summary.json"
echo "-- frame-stats.json checks:"
python3 -c 'import json,sys; s=json.load(open(sys.argv[1])); print(json.dumps({"dtMs": s["dtMs"], "checks": s["checks"], "verdict": s["verdict"]}, indent=2))' \
  "${out}/frame-stats.json"
verdict="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["verdict"])' "${out}/frame-stats.json")"
if (( player_rc != 0 || stopped == 1 )); then
  echo "RESULT record_playthrough ${packet} ${label}: FAIL (player exit ${player_rc}, stopped ${stopped}; B-FRAME ${verdict}; ${out})"
  exit 3
fi
echo "RESULT record_playthrough ${packet} ${label}: PASS (player exit 0, ${played_s}s; B-FRAME ${verdict}; ${out})"
exit 0
