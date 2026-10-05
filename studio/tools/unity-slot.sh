#!/usr/bin/env bash
# Shared host allocator. Source, then unity_slot_acquire; release while child is reaped.
unity_slot_acquire() {
  local count i probe mutex occupied owner waited=0
  slots="${GC_STUDIO_UNITY_SLOTS:-3}"
  [[ "$slots" =~ ^[1-3]$ ]] || { echo 'Unity slot limit must be 1..3' >&2; return 2; }
  slot_dir="${HOME}/${GC_STUDIO_REMOTE_BASE:-wkspace/gc-studio}/.unity-slots"
  mkdir -p "$slot_dir"
  while true; do
    exec {mutex}>"${slot_dir}/allocator.lock"
    flock "$mutex"
    occupied=0
    for ((i=1; i<=3; i++)); do
      exec {probe}>"${slot_dir}/slot${i}.lock"
      if flock -n "$probe"; then
        # Only a dead owner's diagnostics may be removed. Never unlink flock files.
        owner=$(sed -n 's/.*pid=\([0-9]*\).*/\1/p' "${slot_dir}/slot${i}.owner" 2>/dev/null || true)
        if [[ -z "$owner" ]] || ! kill -0 "$owner" 2>/dev/null; then rm -f "${slot_dir}/slot${i}.owner"; fi
        flock -u "$probe"
      else
        occupied=$((occupied+1))
      fi
      exec {probe}>&-
    done
    count=$(python3 "${unity_tools_dir}/../stage/host-processes.py" "$slot_dir")
    if (( occupied + count < slots )); then
      for ((i=1; i<=slots; i++)); do
        exec {unity_slot_fd}>"${slot_dir}/slot${i}.lock"
        if flock -n "$unity_slot_fd"; then
          unity_slot_number=$i
          printf 'pid=%s\n' "$$" > "${slot_dir}/slot${i}.owner"
          flock -u "$mutex"
          exec {mutex}>&-
          return 0
        fi
        exec {unity_slot_fd}>&-
      done
    fi
    flock -u "$mutex"
    exec {mutex}>&-
    (( waited % 60 != 0 )) || echo '-- waiting for a host-wide Unity slot' >&2
    sleep 1
    waited=$((waited+1))
  done
}
unity_slot_release() {
  [[ -n "${unity_slot_fd:-}" ]] || return 0
  local mutex
  exec {mutex}>"${slot_dir}/allocator.lock"
  flock "$mutex"
  rm -f "${slot_dir}/slot${unity_slot_number}.owner"
  flock -u "$unity_slot_fd"
  exec {unity_slot_fd}>&-
  unset unity_slot_fd
  flock -u "$mutex"
  exec {mutex}>&-
}
