#!/usr/bin/env python3
"""Final-tree live receipts; each invocation is independently bounded and reviewable."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import verify as v
ROOT=v.ROOT
LIVE=ROOT/'.evidence/live'
STATE=ROOT/'artifacts/studio/workflows/P4.2c'
TOOLS=ROOT/'artifacts/studio/verification/TOOLS'
CAPS={'image':6,'tts':6,'describe':2}

def project_id(game='hollowmere'):
    import re
    project=LIVE/'games'/game
    guid=re.search(r'productGUID: ([a-fA-F0-9]{32})',(project/'ProjectSettings/ProjectSettings.asset').read_text())[1]
    return hashlib.sha256((guid.lower()+'\n'+str(project)).encode()).hexdigest()

def ledger():
    db=Path.home()/'.local/share/etos-studio/agents/gamecore-studio/state/ledger.db'
    with sqlite3.connect('file:'+str(db)+'?mode=ro',uri=True) as conn:
        charges=[{'id':k,**json.loads(c)} for k,c in conn.execute('SELECT key,charge FROM media_charges ORDER BY key')]
    return {'utc':v.utc(),'charges':charges,'count':len(charges),'costUsd':sum(c['costUsd'] for c in charges)}

def snapshot(name):
    data=ledger();(STATE/(name+'.json')).write_text(json.dumps(data,indent=2)+'\n');return data

def reserve(name,counts):
    path=STATE/'reservations.json'
    rows=json.loads(path.read_text()) if path.exists() else []
    if any(r['name']==name for r in rows):raise RuntimeError('This paid lane was already reserved; no automatic replay')
    base=json.loads((STATE/'ledger-before.json').read_text())
    current=ledger()
    if current['costUsd']-base['costUsd']>=10:raise RuntimeError('USD 10 ledger cap reached; stopped')
    for op,limit in CAPS.items():
        if sum(r['counts'].get(op,0) for r in rows)+counts.get(op,0)>limit:raise RuntimeError(op+' reservation cap reached; stopped')
    rows.append({'name':name,'counts':counts,'utc':v.utc()});path.write_text(json.dumps(rows,indent=2)+'\n')

def unity(row,label,extra,workflow=None,results=None,environment=None):
    env={'DISPLAY':':1','GAMECORE_ETOS_LIVE':'1','GAMECORE_ETOS_AUTOSTART':'1',
         'GAMECORE_ETOS_PROJECT_ID':project_id(),'GAMECORE_P42_EVIDENCE':'{out}',
         'GAMECORE_P42C_OUT':'{out}/workflow','UNITY':str(TOOLS/'unity-interactive-p42c.py'),**(environment or {})}
    if workflow:env['GAMECORE_P42C_WORKFLOW']=workflow
    args=['bash',str(ROOT/'studio/tools/unity-batch.sh'),'--project',str(LIVE/'games/hollowmere'),
          '--log-dir','{out}/logs','--label',label,'--timeout','1800']
    if results:args+=['--results','{out}/results.xml']
    args+=['--',*extra]
    result=v.run(row,label,args,cwd=LIVE,env=env,results=results,editor=True)
    snapshot('ledger-after-'+label)
    return result

def main():
    p=argparse.ArgumentParser();p.add_argument('lane');a=p.parse_args()
    if a.lane=='baseline':snapshot('ledger-before');return
    if a.lane=='hello':
        env={'GAMECORE_ETOS_LIVE':'1','GAMECORE_ETOS_PROJECT_ID':project_id(),
             'GAMECORE_ETOS_KEY_FILE':str(Path.home()/'.config/gamecore-studio/app-key.json'),
             'GC_ETOS_EVIDENCE_DIR':'{out}/live'}
        v.run('INSTALL-P4.2c','hello',['dotnet','test','dotnet/tests/GameCore.Studio.Etos.Client.Tests',
          '--filter','FullyQualifiedName~L01_','--logger','trx','--results-directory','{out}/trx'],
          cwd=LIVE,env=env,results='trx/*.trx');return
    if a.lane=='tts-proof':
        reserve(a.lane,{'tts':1})
        v.run('INSTALL-P4.2c','priced-tts',['cargo','test','--test','real_node','r4_one_priced_tts_records_published_tariff','--','--ignored','--exact','--nocapture'],
          cwd=Path.home()/'wkspace/gc-studio/companion/studio/agent',env={
          'STUDIO_REAL_APP_KEY':str(Path.home()/'.config/gamecore-studio/app-key.json'),
          'STUDIO_REAL_ALLOW_OPS':'1','STUDIO_REAL_R4_LIVE':'1','GAMECORE_ETOS_PROJECT_ID':project_id(),'R4_EVIDENCE_DIR':'{out}'})
        snapshot('ledger-after-tts-proof');return
    row={'text2':'W-AI-02','robe2':'W-AI-01','narrative':'W-AI-03','reopen':'W-AI-06','voice2':'W-VOICE-01'}[a.lane]
    reserve(a.lane,{'image':3,'tts':2} if a.lane=='robe2' else {'tts':2} if a.lane=='voice2' else {})
    result=unity(row,'p42c-'+a.lane,['-executeMethod','Hollowmere.P4_2.EvidenceEntry.RunStage'],workflow=a.lane)
    raise SystemExit(result['status']!='PASS')
if __name__=='__main__':main()
