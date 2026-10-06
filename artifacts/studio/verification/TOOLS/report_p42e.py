#!/usr/bin/env python3
"""Publish reviewed P4.2e dispositions without relabeling inherited evidence."""
import json
from pathlib import Path
import report_rows
import verify as v
STATE=v.ROOT/'artifacts/studio/workflows/P4.2e'
BASE='d140f748'
def main():
    observations=json.loads((STATE/'row-dispositions.json').read_text())
    ledger=json.loads((STATE/'paid-ledger.json').read_text())
    assert ledger['accountedUsd']<=3
    for op,limit in [('image',2),('tts',3),('describe',2)]:assert ledger['operationCounts'][op]<=limit
    decisions=json.loads((v.OUT/'ROW-DECISIONS.json').read_text())
    # Preserve the final-docs wording on untouched rows, including GPU clarification.
    for line in (v.ROOT/'docs/studio/07-verification-matrix.md').read_text().splitlines():
        if not line.startswith('| W-'): continue
        parts=[part.strip() for part in line.split('|')[1:-1]]
        if parts[0] not in observations:
            decisions[parts[0]]['note']=parts[4].split('): ',1)[1]
    for rid,data in observations.items():
        assert data['status'] in ('PASS','BLOCKED','FAIL')
        assert data['patterns'] and data['note']
        decisions[rid]={**data,'baseline':BASE,'historical':'P4.2e installed release 0.1.0-cac2f82c59be070b on main d140f748. Earlier attempts remain retained; untouched rows keep their original revision-specific evidence.'}
    (v.OUT/'ROW-DECISIONS.json').write_text(json.dumps(decisions,indent=2)+'\n')
    report_rows.write_rows()
    rows=json.loads((v.OUT/'ROWS.json').read_text())
    (STATE/'outcomes.json').write_text(json.dumps({'productRevision':BASE,'installedRelease':'0.1.0-cac2f82c59be070b','matrixCounts':rows['counts'],'rerunRows':observations,'ledger':ledger},indent=2)+'\n')
if __name__=='__main__':main()
