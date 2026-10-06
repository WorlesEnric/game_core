#!/usr/bin/env python3
"""P4.2e bounded installed-main acceptance, with immutable one-use reservations."""
import importlib.util
import json
import os
from pathlib import Path
import sys
import verify as v
spec=importlib.util.spec_from_file_location('live42c',Path(__file__).with_name('live-p42c.py'))
live=importlib.util.module_from_spec(spec);spec.loader.exec_module(live)
live.STATE=v.ROOT/'artifacts/studio/workflows/P4.2e'
CAPS={'image':2,'tts':3,'describe':2}

def reserve(name,counts):
    path=live.STATE/'reservations.json'
    rows=json.loads(path.read_text()) if path.exists() else []
    if any(r['name']==name for r in rows): raise RuntimeError('lane already reserved; no automatic paid replay')
    base=json.loads((live.STATE/'ledger-before.json').read_text())
    spent=live.ledger()['costUsd']-base['costUsd']
    ceiling=counts.get('image',0)*0.25+counts.get('tts',0)*0.10+counts.get('describe',0)*0.01
    if spent+ceiling>3: raise RuntimeError('USD 3 ledger cap would be exceeded')
    for op,limit in CAPS.items():
        if sum(r['counts'].get(op,0) for r in rows)+counts.get(op,0)>limit: raise RuntimeError(op+' cap exceeded')
    rows.append({'name':name,'counts':counts,'utc':v.utc(),'reservedUsd':ceiling})
    path.write_text(json.dumps(rows,indent=2)+'\n')

def main():
    lane=sys.argv[1]
    if lane=='baseline':
        if (live.STATE/'ledger-before.json').exists(): raise RuntimeError('baseline cannot reset spend')
        live.snapshot('ledger-before');return
    if lane=='cleanup-npc':
        live.unity('W-AI-02','p42e-cleanup-npc',['-runTests','-testPlatform','EditMode','-testFilter','R2_38_P42e_NormalJournalUndoAfterNpcPlayReload'],results='results.xml',environment={'GAMECORE_ETOS_AUTOSTART':'0','UNITY':str(Path.home()/'Unity/Hub/Editor/6000.0.75f1/Editor/Unity')});return
    if lane=='regression':
        live.unity('R6-P4.2e','p42e-regression',['-runTests','-testPlatform','EditMode','-testFilter','Hollowmere.R6_A.AdmissionLifecycleTests|Hollowmere.R6_B|Hollowmere.P3_2.Headless.DriverDryTests'],results='results.xml',environment={'GAMECORE_ETOS_AUTOSTART':'0','UNITY':str(Path.home()/'Unity/Hub/Editor/6000.0.75f1/Editor/Unity')});return
    if lane in ('receipt-hello','describe','receipt-stage'):
        if lane == 'describe': reserve(lane, {'image':1,'describe':1})
        case = {'receipt-hello':'R2_38_P42e_HelloOwnerDescribeAnd3dRefusal','describe':'R2_38_P42e_OnePricedImageThenOneDescribe','receipt-stage':'R2_09_13_P42e_InstalledSignedRecordHasWorldAndPredicted'}[lane]
        v.run('INSTALL-P4.2e' if lane == 'receipt-hello' else 'W-ETOS-07' if lane == 'describe' else 'W-MECH-01', 'p42e-'+lane,
          ['dotnet','test',str(live.TOOLS/'P42eReceipt/P42eReceipt.csproj'),'--filter','FullyQualifiedName~'+case,'--logger','trx','--results-directory','{out}/trx'],
          env={'GAMECORE_ETOS_PROJECT_ID':live.project_id(),'GAMECORE_ETOS_KEY_FILE':str(Path.home()/'.config/gamecore-studio/app-key.json'),'GAMECORE_P42_EVIDENCE':'{out}','GAMECORE_P42E_PAID':'1' if lane=='describe' else '0'},results='trx/*.trx')
        live.snapshot('ledger-after-'+lane);return
    if lane=='hello':
        v.run('INSTALL-P4.2e','hello',['dotnet','test','dotnet/tests/GameCore.Studio.Etos.Client.Tests','--filter','FullyQualifiedName~L01_','--logger','trx','--results-directory','{out}/trx'],cwd=live.LIVE,env={'GAMECORE_ETOS_LIVE':'1','GAMECORE_ETOS_PROJECT_ID':live.project_id(),'GAMECORE_ETOS_KEY_FILE':str(Path.home()/'.config/gamecore-studio/app-key.json'),'GC_ETOS_EVIDENCE_DIR':'{out}/live'},results='trx/*.trx');return
    if lane == 'voice2':
        reserve(lane, {'tts':2})
        v.run('W-VOICE-01','p42e-voice2',[
            'bash',str(live.TOOLS/'voice-p42e.sh'),'{out}/workflow',
            'bash',str(v.ROOT/'studio/tools/unity-batch.sh'),'--project',str(live.LIVE/'games/hollowmere'),
            '--log-dir','{out}/logs','--label','p42e-voice2','--timeout','1800','--',
            '-executeMethod','Hollowmere.P4_2.EvidenceEntry.RunStage'],cwd=live.LIVE,editor=True,
            env={'DISPLAY':':1','GAMECORE_ETOS_LIVE':'1','GAMECORE_ETOS_AUTOSTART':'1',
                 'GAMECORE_ETOS_PROJECT_ID':live.project_id(),'GAMECORE_P42_EVIDENCE':'{out}',
                 'GAMECORE_P42C_OUT':'{out}/workflow','GAMECORE_P42C_WORKFLOW':'voice2',
                 'UNITY':str(live.TOOLS/'unity-interactive-p42c.py')})
        live.snapshot('ledger-after-voice2');return
    if lane in ('text2','narrative','reopen','voice2'):
        reserve(lane,{'tts':2} if lane=='voice2' else {})
        row={'text2':'W-AI-02','narrative':'W-AI-03','reopen':'W-AI-06','voice2':'W-VOICE-01'}[lane]
        live.unity(row,'p42e-'+lane,['-executeMethod','Hollowmere.P4_2.EvidenceEntry.RunStage'],workflow=lane)
    elif lane.startswith('voice-self'):
        reserve(lane,{})
        live.unity('W-VOICE-01','p42e-'+lane,['-runTests','-testPlatform','EditMode','-testFilter','Hollowmere.R5_B.VirtualVoiceSelfTest'],results='results.xml',environment={'GAMECORE_R5B_VOICE_SELF_TEST':'1','GAMECORE_R4A_VIRTUAL_SOURCE':'GC_P42d_mic','GAMECORE_R4A_VIRTUAL_SINK':'gc_p42d_sink'})
    elif lane in ('selection','selection-repeat'):
        live.unity('W-UI-05','p42e-'+lane,['-runTests','-testPlatform','EditMode','-testFilter',('R2_38_B_SELECT_100PicksAnd500CandidateMarquee' if lane=='selection-repeat' else 'R2_38_B_SELECT_100PicksAnd500CandidateMarquee|R2_38_CORE_PICK_500CandidatesMedianAcrossEditorFramesBelow50Ms')],results='results.xml',environment={'GAMECORE_ETOS_AUTOSTART':'0'})
    elif lane=='history-prepare':
        live.unity('W-AI-06','p42e-history-prepare',['-executeMethod','Hollowmere.R5_A.HistoryReopenTests.Prepare','-quit'],environment={'GAMECORE_ETOS_AUTOSTART':'0'})
    elif lane=='history-reopen':
        live.unity('W-AI-06','p42e-history-reopen',['-runTests','-testPlatform','EditMode','-testFilter','Hollowmere.R5_A.HistoryReopenTests'],results='results.xml',environment={'GAMECORE_R5_REOPEN':'1','GAMECORE_ETOS_AUTOSTART':'0'})
    elif lane=='guide':
        reserve(lane,{})
        live.unity('W-DOC-01','p42e-guide',['-runTests','-testPlatform','EditMode','-testFilter','P42d.Live.NoviceGuideTests|P42_OPS_01_Installed3dRefusesUnconfigured'],results='results.xml')
    elif lane=='stage-recover':
        import argparse
        parser=argparse.ArgumentParser();parser.add_argument('lane');parser.add_argument('--input',required=True);a=parser.parse_args()
        live.unity('W-MECH-01','p42e-stage-recover',['-executeMethod','P42c.Live.StageUi.Recover'],environment={'GAMECORE_P42C_INPUT':str(Path(a.input).resolve())})
    elif lane.startswith('stage-'):
        sys.argv[0]=str(Path(__file__).with_name('live-p42c.py'))
        original=live.unity
        def renamed(row,label,*args,**kwargs): return original(row,label.replace('p42c-','p42e-'),*[([x.replace('P42c.Live.StageUi.', 'P42e.Live.StageUi.') for x in a] if isinstance(a,list) else a) for a in args],**kwargs)
        live.unity=renamed;live.main()
    else: raise ValueError(lane)
    live.snapshot('ledger-after-'+lane)

if __name__=='__main__':
    main()
    raise SystemExit(int(any(r['status']!='PASS' for r in v.RESULTS)))
