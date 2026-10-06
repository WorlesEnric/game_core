#!/usr/bin/env python3
"""Join exact acceptance dispositions to retained runs; keep historical attempts separate."""
import hashlib
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET
import verify as v


def latest(pattern):
    paths=sorted(v.OUT.glob(pattern))
    return str(paths[-1].relative_to(v.OUT)) if paths else None


def write_rows():
    decisions=json.loads((v.OUT/'ROW-DECISIONS.json').read_text())
    matrix=v.ROOT/'docs/studio/07-verification-matrix.md'
    lines=matrix.read_text().splitlines()
    rows=[]
    for i,line in enumerate(lines):
        if not line.startswith('| W-'): continue
        parts=[x.strip() for x in line.split('|')[1:-1]]
        rid,scenario,requirement=parts[:3]
        d=dict(decisions[rid]); d.update(row=rid,scenario=scenario,requirement=requirement,reportedAt=v.utc())
        refs=[]
        for pattern in d.pop('patterns',[]):
            resolved=latest(pattern)
            if resolved: refs.append(resolved)
            else: raise ValueError(rid+': no retained artifact for '+pattern)
        d['evidence']=refs
        folder=v.OUT/rid; folder.mkdir(exist_ok=True)
        (folder/'row.json').write_text(json.dumps(d,indent=2)+'\n')
        text=f"# {rid}: {scenario}\n\nVerdict: **{d['status']}**. {d['note']}\n\n"
        text+='Report timestamp: '+d['reportedAt']+' UTC.\n\n'
        text+='Acceptance baseline: merged main `' + d.get('baseline', 'e94f27aa') + '`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.\n\n'
        text+='## Reproduce\n\n```sh\n'+d['command']+'\n```\n\n'
        text+='## Retained evidence\n\n'+'\n'.join(f'- [{p}](../{p})' for p in refs)+'\n'
        if d.get('tests'):text+='\nExact acceptance/component cases: '+', '.join('`'+x+'`' for x in d['tests'])+'. Component cases do not close any missing external workflow.\n'
        if d.get('historical'):text+='\nHistorical references: '+d['historical']+'\n'
        (folder/'README.md').write_text(v.scrub(text))
        (folder/'SHA256SUMS').write_text(''.join(hashlib.sha256((folder/n).read_bytes()).hexdigest()+'  '+n+'\n' for n in ('README.md','row.json')))
        status={'PASS':'exercised','FAIL':'failed','BLOCKED':'blocked(prereq)'}[d['status']]
        parts[3]=status
        parts[4]=f"[{d['status']} evidence](../../artifacts/studio/verification/{rid}/README.md): {d['note']}"
        lines[i]='| '+' | '.join(parts)+' |'
        rows.append(d)
    matrix.write_text('\n'.join(lines)+'\n')
    counts={status:sum(r['status']==status for r in rows) for status in ('PASS','BLOCKED','FAIL')}
    (v.OUT/'ROWS.json').write_text(json.dumps({'counts':counts,'rows':rows},indent=2)+'\n')
    v.summary()
    print(json.dumps(counts))

if __name__=='__main__':write_rows()
