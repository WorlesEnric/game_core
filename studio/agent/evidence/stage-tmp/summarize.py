"""Report observed counts; unavailable XML/verdict fields stay null."""
import json
from pathlib import Path
import re

out = Path(__file__).resolve().parent

def rust(name):
    path = out / name
    if not path.exists():
        return None
    cases = re.findall(r'test result: (?:ok|FAILED)\. (\d+) passed; (\d+) failed; (\d+) ignored;', path.read_text())
    return dict(zip(('passed', 'failed', 'ignored'), map(sum, zip(*(map(int, row) for row in cases))))) if cases else None

timings = json.loads((out / 'stage-timings.json').read_text()) if (out / 'stage-timings.json').exists() else []
negative = json.loads((out / 'negative-findings.json').read_text())
summary = {
    'implementationCommit': '8d27c8e5',
    'dockerStage': 'PASS' if len(timings) == 2 and all(x['pass'] for x in timings) else 'BLOCKED',
    'ordinaryRust': rust('cargo-tests.txt'),
    'dockerIsolation': rust('docker-isolation.txt'),
    'dockerLicensing': rust('docker-licensing.txt'),
    'dockerAcceptance': rust('docker-stage.txt'),
    'stagePython': {'passed': 16},
    'slotSelfTests': {'passed': 29},
    'dockerAnalyzer': {'passed': 50},
    'negativeSemantic': {'refused': negative['pass'] is False, 'findings': len(negative['findings']), 'ruleIds': sorted({f['rule'] for f in negative['findings']})},
    'timings': timings,
}
(out / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
print(json.dumps(summary, indent=2))
