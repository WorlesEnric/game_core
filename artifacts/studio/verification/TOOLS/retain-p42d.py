#!/usr/bin/env python3
"""Keep correlated voice telemetry and this packet's read-only ledger delta."""
import json
from pathlib import Path
import sqlite3
import verify as v
ROOT=v.ROOT;STATE=ROOT/'artifacts/studio/workflows/P4.2d'
base=Path.home()/'.local/share/etos-studio/agents/gamecore-studio'
start=json.loads((STATE/'voice-log-start.json').read_text())['offset']
with (base/'log/agent.log').open('rb') as stream:
    stream.seek(start);lines=stream.read().decode(errors='replace').splitlines()
(STATE/'companion-voice.log').write_text(v.scrub('\n'.join(line for line in lines if 'voice ' in line))+'\n')
with sqlite3.connect('file:'+str(base/'state/ledger.db')+'?mode=ro',uri=True) as conn:
    charges=[{'id':k,**json.loads(c)} for k,c in conn.execute('SELECT key,charge FROM media_charges ORDER BY key')]
    names=[r[0] for r in conn.execute("SELECT name FROM sqlite_master WHERE type='table'")]
    voice=[]
    if 'voice_sessions' in names:
        conn.row_factory=sqlite3.Row
        voice=[dict(r) for r in conn.execute('SELECT * FROM voice_sessions')]
before=json.loads((STATE/'ledger-before.json').read_text());ids={x['id'] for x in before['charges']}
new=[x for x in charges if x['id'] not in ids]
paid={'charges':new,'companionLedgerUsd':sum(x['costUsd'] for x in new),'accountedUsd':sum(x['costUsd'] for x in new),'operationCounts':{op:sum(x['quantity'] for x in new if x['tariff']['unit']==unit) if op=='image' else sum(x['tariff']['unit']==unit for x in new) for op,unit in [('image','image'),('tts','bailian_character'),('describe','call')]},'voiceAccounting':'voice_sessions has no USD column; no provider invoice inferred'}
(STATE/'paid-ledger.json').write_text(json.dumps(paid,indent=2)+'\n')
(STATE/'ledger-final.json').write_text(json.dumps({'charges':charges,'costUsd':sum(x['costUsd'] for x in charges)},indent=2)+'\n')
# Retain only sessions opened since the packet guard; timestamps in the ledger are UTC milliseconds.
import datetime
stamp=datetime.datetime.fromisoformat(json.loads((STATE/'guard-before.json').read_text())['utc']).timestamp()*1000
voice=[r for r in voice if r.get('started_at',r.get('opened_at',0))>=stamp]
(STATE/'voice-ledger.json').write_text(v.scrub(json.dumps(voice,indent=2))+'\n')
print(json.dumps(paid))
