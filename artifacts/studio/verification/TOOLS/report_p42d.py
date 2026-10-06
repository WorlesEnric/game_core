#!/usr/bin/env python3
"""Publish reviewed P4.2d dispositions without relabeling inherited evidence."""
import json
from pathlib import Path
import report_rows
import verify as v
STATE=v.ROOT/'artifacts/studio/workflows/P4.2d'
BASE='40fb91fa'
def main():
    observations=json.loads((STATE/'row-dispositions.json').read_text())
    ledger=json.loads((STATE/'paid-ledger.json').read_text())
    assert ledger['accountedUsd']<=5
    for op,limit in [('image',4),('tts',4),('describe',2)]:assert ledger['operationCounts'][op]<=limit
    decisions=json.loads((v.OUT/'ROW-DECISIONS.json').read_text())
    for rid,data in observations.items():
        assert data['status'] in ('PASS','BLOCKED','FAIL')
        assert data['patterns'] and data['note']
        decisions[rid]={**data,'baseline':BASE,'historical':'P4.2d installed release 0.1.0-e8a72b2d6eb3aad9 on main 40fb91fa. Earlier attempts remain retained; untouched rows keep their original revision-specific evidence.'}
    (v.OUT/'ROW-DECISIONS.json').write_text(json.dumps(decisions,indent=2)+'\n')
    report_rows.write_rows()
    rows=json.loads((v.OUT/'ROWS.json').read_text())
    (STATE/'outcomes.json').write_text(json.dumps({'productRevision':BASE,'installedRelease':'0.1.0-e8a72b2d6eb3aad9','matrixCounts':rows['counts'],'rerunRows':observations,'ledger':ledger},indent=2)+'\n')
if __name__=='__main__':main()
