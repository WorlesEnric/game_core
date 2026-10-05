#!/usr/bin/env python3
"""Explicit display/node qualification. No provider substitutions or host-confinement fallback."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import verify as v


def identity():
    p=v.ROOT/'games/hollowmere'
    cache=p/'UserSettings/GameCoreStudio.Project.json'
    data=json.loads(cache.read_text()) if cache.exists() else {}
    if data.get('path')==str(p): return data['projectId']
    guid=re.search(r'productGUID: ([a-fA-F0-9]{32})',(p/'ProjectSettings/ProjectSettings.asset').read_text())[1]
    return hashlib.sha256((guid.lower()+'\n'+str(p)).encode()).hexdigest()


def live_env():
    return {'GAMECORE_ETOS_LIVE':'1','GAMECORE_ETOS_AUTOSTART':'1',
            'GAMECORE_ETOS_PROJECT_ID':identity(),
            'GAMECORE_ETOS_KEY_FILE':str(Path.home()/'.config/gamecore-studio/app-key.json'),
            'GC_ETOS_EVIDENCE_DIR':'{out}/live','GC_ETOS_FIXTURE_OUT':'{out}/fixtures'}


def graphical(row,label,extra,environment=None,results=None):
    # Every UI invocation holds the same lease as the batch suites, then the host allocator.
    wrapper='''set -euo pipefail
pgrep -af 'Unity|ffmpeg|xvfb' || true
if pgrep -x Unity >/dev/null; then echo 'BLOCKED: another Editor is active'; exit 2; fi
unity_tools_dir="$PWD/studio/tools"
source "$unity_tools_dir/unity-slot.sh"
unity_slot_acquire
trap unity_slot_release EXIT
out="$1"; shift
python3 studio/stage/run-redacted.py --log "$out/editor.log" --timeout 1500 --silence 600 -- env "$@"
'''
    env={'DISPLAY':os.environ.get('EVIDENCE_DISPLAY',':1'),'GAMECORE_P42_EVIDENCE':'{out}', **(environment or {})}
    forwarded=[f'{k}={val}' for k,val in env.items()]
    return v.run(row,label,['bash','-c',wrapper,'p42-graphics','{out}',*forwarded,
         str(Path.home()/'Unity/Hub/Editor/6000.0.75f1/Editor/Unity'),
         '-projectPath',v.ROOT/'games/hollowmere','-logFile','-',*extra],env=env,results=results,editor=True)


def ui():
    return v.run('W-UI-01','ui-capture',['bash','studio/tools/evidence-p2.1.sh',v.ROOT.name,'games/hollowmere'],
       env={'EVIDENCE_DEST':'{out}/captures','GAMECORE_ETOS_AUTOSTART':'1','EVIDENCE_ENTRY':'Hollowmere.P4_2.EvidenceEntry.RunUi'},editor=True)


def stage():
    for sample in ('pressure-plate',):
        candidate=v.ROOT/'samples/mechanisms'/sample/'candidate'
        graphical('W-MECH-01',sample+'-installed-ui',
          ['-executeMethod','Hollowmere.P3_2.Workflows.WorkflowRunner.Run'],
          {**live_env(),'GCS_P32_WORKFLOW':'mech-b','GCS_P32_OUT':'{out}/workflow',
           'GCS_P32_MECH_CANDIDATE':str(candidate)})


def live_media():
    # One image, one TTS, <=one describe, and an unavailable-3D refusal through the real Unity gateway.
    # The installed operator price table may refuse image before provider execution; preserve that result.
    env=live_env()
    # GC_* evidence knobs are not in the Unity runner allowlist. Set non-secret output paths via
    # the test harness's documented Library environment seam, restored immediately after the run.
    p=v.ROOT/'games/hollowmere/Library/P2_2/live-env.json'
    # Existing tests read process environment; use the graphical trusted env forwarder explicitly.
    return graphical('W-ETOS-07','installed-media',
       ['-runTests','-testPlatform','EditMode','-testFilter','C_D_E_F_I_MediaOps','-testResults','{out}/results.xml'],
       env,results='results.xml')


def reconnect():
    return v.run('W-ETOS-06','installed-stream-resume-cancel',
       ['dotnet','test','dotnet/tests/GameCore.Studio.Etos.Client.Tests','--filter','FullyQualifiedName~L03_',
        '--logger','trx','--results-directory','{out}/trx'],results='trx/*.trx',env=live_env())


def build():
    for game, method, args, row in (
        ('hollowmere','Hollowmere.Build.BuildLinuxPlayer',['-buildOutput',str(v.ROOT/'build/HollowmereLinux'),'-buildRevision',v.git('rev-parse','HEAD')],'W-GAME-06'),
        ('cleanproof','Saltmarsh.Build.BuildLinuxPlayer',['-quit'],'W-CLEAN-01')):
        v.run(row,game+'-linux-retry',['bash','studio/tools/unity-batch.sh','--project',v.ROOT/'games'/game,
          '--log-dir','{out}/logs','--label',game+'-build','--timeout','3600','--',
          '-executeMethod',method,*args],env={'GAMECORE_ETOS_AUTOSTART':'0'},editor=True)


def v1():
    # Adapter serializes all gate Editors with this packet's other jobs. Probe repetitions remain capped.
    return v.run('W-GAME-06','v1-gate-retry',['bash','tools/run_w7_gate.sh'],env={
      'UNITY':str(v.ROOT/'artifacts/studio/verification/TOOLS/unity-gate-adapter.py'),
      'ARTIFACTS':'{out}/gate','PROBE_RUNS':'2','BENCH_RUNS':'1','BENCH_WARMUP':'1',
      'BENCH_DURATION':'2','BENCH_REPETITIONS':'5','GAMECORE_ETOS_AUTOSTART':'0'})


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('mode',choices=['ui','views','stage','media','reconnect','build','v1','graphics','memory','voice'])
    mode=p.parse_args().mode
    if mode=='ui': ui()
    elif mode=='views': v.graphical_views()
    elif mode=='stage': stage()
    elif mode=='media': live_media()
    elif mode=='reconnect': reconnect()
    elif mode=='build': build()
    elif mode=='v1': v1()
    elif mode=='graphics': v.graphical_tests()
    elif mode=='voice': v.run('W-VOICE-01','recorded-wav-pipewire',['bash','artifacts/studio/verification/TOOLS/voice-fixture.sh','{out}'],results='results.xml',editor=True)
    elif mode=='memory': v.graphical_tests('R2_38_W_UI_04_W_GAME_08_TenCyclesWithSnapshotsAndOnePump','W-GAME-08','native-memory-and-pumps')
    v.summary()
    return int(any(r['status']!='PASS' for r in v.RESULTS))

if __name__=='__main__': sys.exit(main())
