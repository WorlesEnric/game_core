#!/usr/bin/env python3
"""XML dispositions, not Editor exit codes, determine test acceptance. 0 pass, 1 fail, 2 partial/not run."""
import sys
import xml.etree.ElementTree as ET


def main(path, required=()):
    root = ET.parse(path).getroot()
    cases = list(root.iter('test-case'))
    counts = {name: sum(c.get('result') == name for c in cases) for name in ('Passed', 'Failed', 'Skipped', 'Inconclusive')}
    print('-- test XML: ' + ', '.join(f'{k}={v}' for k, v in counts.items()))
    if counts['Failed'] or root.get('result') == 'Failed' or int(root.get('failed', 0)) > 0: return 1
    if any(not any(c.get('fullname') == name and c.get('result') == 'Passed' for c in cases) for name in required):
        print('-- NotRun: required acceptance case missing or not passed')
        return 2
    if (not cases or any(c.get('result') != 'Passed' for c in cases) or root.get('result') not in (None, 'Passed')
            or int(root.get('skipped', 0)) > 0 or int(root.get('inconclusive', 0)) > 0):
        print('-- PARTIAL/NotRun: acceptance requires every selected case to pass')
        return 2
    return 0


if __name__ == '__main__':
    try: sys.exit(main(sys.argv[1], sys.argv[2:]))
    except (OSError, ET.ParseError, IndexError, ValueError):
        print('-- NotRun: unavailable or invalid test XML')
        sys.exit(2)
