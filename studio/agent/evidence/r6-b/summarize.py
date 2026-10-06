"""Read retained XML/TRX dispositions and hashes; never infer success from runner stdout."""
from collections import Counter
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent
rows = []
for path in sorted(list((ROOT / 'unity').glob('*.xml')) + list((ROOT / 'test-results').glob('*.trx'))):
    root = ET.parse(path).getroot()
    cases = list(root.iter('test-case'))
    if cases:
        counts = Counter(c.get('result') for c in cases)
        problems = [{'test': c.get('fullname'), 'result': c.get('result'),
                     'message': c.findtext('failure/message') or c.findtext('reason/message')}
                    for c in cases if c.get('result') != 'Passed']
    else:
        cases = list(root.iter('{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}UnitTestResult'))
        counts = Counter(c.get('outcome') for c in cases)
        problems = [{'test': c.get('testName'), 'result': c.get('outcome')}
                    for c in cases if c.get('outcome') != 'Passed']
    rows.append({'file': str(path.relative_to(ROOT)), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                 'counts': dict(counts), 'nonPassing': problems})
(ROOT / 'results.json').write_text(json.dumps(rows, indent=2) + '\n')
for row in rows:
    print(row['file'], row['counts'])
