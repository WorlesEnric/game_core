#!/usr/bin/env bash
# R2-17/R2-19: concurrent fake Editors, stale owners, cancellation and durable redaction.
set -euo pipefail
repo="$(cd "$(dirname "$0")/../../.." && pwd)"
work=$(mktemp -d)
slot_base=".cache/gamecore-lock-test-$$"
pids=()
cleanup() { for pid in "${pids[@]}"; do kill -TERM "$pid" 2>/dev/null || true; done; rm -rf "$work" "${HOME}/${slot_base}"; }
trap cleanup EXIT
mkdir -p "$work/project/ProjectSettings" "$work/project/Packages" "$work/logs" "${HOME}/${slot_base}/.unity-slots"
printf '{}' > "$work/project/Packages/manifest.json"
printf 'pid=99999999\n' > "${HOME}/${slot_base}/.unity-slots/slot1.owner"
cat > "$work/Unity" <<'PY'
#!/usr/bin/env python3
import ctypes,fcntl,json,os,signal,sys,time
ctypes.CDLL(None).prctl(15,b'Unity',0,0,0)
root=os.path.dirname(__file__)
def event(name):
    with open(root+'/events','a') as f:
        fcntl.flock(f,fcntl.LOCK_EX)
        f.write(json.dumps([name,os.getpid(),time.time()])+'\n')
def stop(sig,frame):
    event('term'); sys.exit(143)
signal.signal(signal.SIGTERM,stop)
event('start')
if '-compile-error' in sys.argv:
    print('error CS0001: synthetic compilation failure',flush=True)
    event('end'); sys.exit(0)
if '-testResults' in sys.argv:
    result=sys.argv[sys.argv.index('-testResults')+1]
    partial='-partial' in sys.argv
    with open(result,'w') as f:
        f.write('<test-run result="'+('Inconclusive' if partial else 'Passed')+'"><test-case fullname="Present" result="Passed" />'+('<test-case result="Inconclusive" />' if partial else '')+'</test-run>')
    event('end'); sys.exit(2 if partial else 0)
print('etk_fixture ett_fixture etp_fixture eta_fixture Bearer fixture sk-fixture',flush=True)
print('{"privateKey":"fixture-private", "nested":{"myToken":{"x":"fixture-nested"}}}',flush=True)
if '-hold' in sys.argv: time.sleep(60)
else: time.sleep(0.3)
event('end')
PY
chmod +x "$work/Unity"
export UNITY="$work/Unity" GC_STUDIO_REMOTE_BASE="$slot_base" GC_STUDIO_UNITY_SLOTS=3
for n in {1..7}; do
  bash "$repo/studio/tools/unity-batch.sh" --project "$work/project" --log-dir "$work/logs" --label "test$n" --attempts 1 -- -quit > "$work/run$n" 2>&1 &
  pids+=("$!")
done
for pid in "${pids[@]}"; do wait "$pid"; done
pids=()
python3 - "$work/events" <<'PY'
import json,sys
active=set(); peak=0
for line in open(sys.argv[1]):
    kind,pid,_=json.loads(line)
    if kind=='start': active.add(pid)
    else: active.remove(pid)
    peak=max(peak,len(active))
assert 0 < peak <= 3 and not active,(peak,active)
print('R2_17_AtomicReservation: PASS; peak fake Editors =',peak)
PY
bash "$repo/studio/tools/unity-batch.sh" --project "$work/project" --log-dir "$work/logs" --label cancel --attempts 1 -- -hold > "$work/cancel" 2>&1 &
pid=$!; pids=("$pid")
for n in {1..120}; do [[ $(wc -l < "$work/events") -ge 15 ]] && break; sleep 0.25; done
kill -TERM "$pid"
rc=0; wait "$pid" || rc=$?
[[ "$rc" == 143 ]]
pids=()
python3 - "$work" "${HOME}/${slot_base}/.unity-slots" <<'PY'
import json,pathlib,sys
root=pathlib.Path(sys.argv[1]); events=[json.loads(x) for x in (root/'events').read_text().splitlines()]
assert events[-1][0]=='term',events[-1]
assert not list(pathlib.Path(sys.argv[2]).glob('*.owner'))
assert len(list(pathlib.Path(sys.argv[2]).glob('slot*.lock')))==3
logs=''.join(p.read_text() for p in (root/'logs').glob('*.log'))
assert 'fixture' not in logs, 'secret reached durable log'
assert '[REDACTED]' in logs
assert not list(root.rglob('*.raw'))
print('R2_17_StaleOwnerAndTerm / R2_19_StreamRedaction: PASS')
PY

for mode in compiler partial required; do
  args=()
  unity_args=(-compile-error)
  if [[ "$mode" != compiler ]]; then
    args=(--results "$work/results.xml")
    unity_args=()
    if [[ "$mode" == partial ]]; then unity_args=(-partial); else args+=(--require-test Missing); fi
  fi
  rc=0
  bash "$repo/studio/tools/unity-batch.sh" --project "$work/project" --log-dir "$work/logs" --label "$mode" --attempts 1 "${args[@]}" -- "${unity_args[@]}" > "$work/$mode" 2>&1 || rc=$?
  [[ "$rc" == 1 ]]
  grep -q "RESULT $mode: FAIL" "$work/$mode"
done
echo 'R2_37_CompilerErrorsPartialAndMissingCases: PASS'
