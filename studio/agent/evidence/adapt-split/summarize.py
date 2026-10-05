"""Read test dispositions from retained XML, never infer passes from Unity stdout."""
from collections import Counter
import json
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parent
summary = {}
for path in sorted(root.glob('*.xml')):
    xml = ET.parse(path).getroot()
    cases = list(xml.iter('test-case'))
    counts = Counter(case.get('result', 'Unknown') for case in cases)
    groups = {}
    for prefix in ['Hollowmere.R2_G.', 'Hollowmere.R2_B.', 'Hollowmere.P1_7b.',
                   'Hollowmere.P2_4.', 'Hollowmere.P1_1.', 'Hollowmere.P3_1.',
                   'GameCore.Studio.', 'Saltmarsh.']:
        selected = [t for t in cases if t.get('fullname', '').startswith(prefix)]
        if selected:
            groups[prefix] = dict(Counter(t.get('result') for t in selected))
    summary[path.name] = {
        'total': len(cases), 'counts': dict(counts), 'groups': groups,
        'notPassed': [{'name': t.get('fullname'), 'result': t.get('result'),
                       'reason': t.findtext('.//message')}
                      for t in cases if t.get('result') != 'Passed']}
for path in sorted(root.glob('*.trx')):
    summary[path.name] = ET.parse(path).getroot().find('.//{*}Counters').attrib
(root / 'test-summary.json').write_text(json.dumps(summary, indent=2) + '\n')
for name, result in summary.items():
    print(name, result.get('counts', result))
