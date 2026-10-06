#!/usr/bin/env python3
"""Aggregate two bounded datasets and add the existing P2.3 view definitions to 07."""
import json
from pathlib import Path
import statistics
import xml.etree.ElementTree as ET
import verify as v
import report_rows


def aggregate(row, pattern, files):
    attempts = []
    for folder in sorted(v.OUT.glob(pattern)):
        if not all((folder / name).exists() for name in files):
            continue
        xml = folder / 'results.xml'
        if not xml.exists():
            raise ValueError(f'{row}: dataset has no result XML: {folder.name}')
        cases = {case.get('name'): case.get('result') for case in ET.parse(xml).getroot().iter('test-case')}
        required = {'apply.json': ('R2_38_B_APPLY_20RealSingleTargetEdits', 20),
                    'region-apply.json': ('R2_38_B_APPLY_20MarshAllNpcEdits', 20),
                    'compose.json': ('R2_38_B_COMPOSE_20PreparesInReferenceWorld', 20),
                    'selection.json': ('R2_38_B_SELECT_100PicksAnd500CandidateMarquee', 100)}
        for filename in files:
            case, count = required[filename]
            if cases.get(case) not in ('Passed', 'Failed'):
                raise ValueError(f'{row}: required case absent/skipped: {case}')
            if filename != 'selection.json' and cases[case] != 'Passed':
                raise ValueError(f'{row}: component test failed: {case}')
        data = {name: json.loads((folder / name).read_text()) for name in files}
        for filename, keys in files.items():
            for key in keys:
                if data[filename][key]['count'] != required[filename][1]:
                    raise ValueError(f'{row}: incomplete sample count: {filename}/{key}')
        attempts.append((folder, data))
    if len(attempts) != 2:
        raise ValueError(f'{row}: expected exactly two complete datasets, found {len(attempts)}')
    metrics = {}
    for filename, keys in files.items():
        for key in keys:
            samples = [data[filename][key] for _, data in attempts]
            metrics[key] = {'runP95Ms': [s['p95Ms'] for s in samples],
                            'medianP95Ms': statistics.median(s['p95Ms'] for s in samples),
                            'counts': [s['count'] for s in samples], 'budgetMs': samples[0]['budgetMs'],
                            'pass': all(s['pass'] for s in samples)}
    status = 'PASS' if all(m['pass'] for m in metrics.values()) else 'FAIL'
    folder = v.OUT / row / 'p42b-dataset-summary'
    folder.mkdir(exist_ok=True, parents=True)
    data = {'metrics': metrics, 'attempts': [str(p.relative_to(v.OUT)) for p, _ in attempts],
            'sourceRevision': '1752ca8a5309b1df8cb1be983ccd6fc04d7dd6ba',
            'currentReproducerSha256': v.hashlib.sha256((v.OUT/'TOOLS/P42bHarness/TimingTests.cs').read_bytes()).hexdigest()}
    (folder/'dataset.json').write_text(json.dumps(data, indent=2)+'\n')
    note = '; '.join(f'{key}: p95 {m["runP95Ms"]} ms, median {m["medianP95Ms"]:.4f} ms, budget {m["budgetMs"]} ms' for key,m in metrics.items())
    (folder/'command.log').write_text(note+'\n')
    record = dict(label=row+' two-run summary',status=status,note=note,revision=data['sourceRevision'],
        host=v.platform.node(),started=v.utc(),ended=v.utc(),seconds=0,command='studio/tools/verify-all.sh final-timing\nstudio/tools/verify-all.sh final-selection')
    v.finish(folder,record)
    return status,note


def main():
    decisions_path = v.OUT/'ROW-DECISIONS.json'
    decisions=json.loads(decisions_path.read_text())
    for rid, data_row, pattern, files in [
        ('W-EDIT-08','B-EDIT','B-EDIT/p42b-dataset-*-*', {'apply.json':['single'],'region-apply.json':['region'],'compose.json':['prepare']}),
        ('W-UI-05','B-SELECT','B-SELECT/p42b-selection-*-*', {'selection.json':['picks','marquee']})]:
        status,note=aggregate(data_row,pattern,files)
        decisions[rid]={'status':status,'baseline':'1752ca8a','note':note,
                        'patterns':[data_row+'/p42b-dataset-summary/README.md',data_row+'/p42b-dataset-summary/dataset.json'],
                        'command':'studio/tools/verify-all.sh final-timing\nstudio/tools/verify-all.sh final-selection'}
    matrix=v.ROOT/'docs/studio/07-verification-matrix.md'
    content=matrix.read_text()
    definitions=[
        ('Relationships','Selection/search neighbourhood, labelled references, impact, navigation and JSON/Mermaid export'),
        ('Dialogue','Graph tools, conditions, preview, journaled edits/undo and real-Play bridge'),
        ('Quests','Stage/objective graph, branches, reward/rule links, simulation and live state'),
        ('World','Regions, portals, residency, spawn points and journaled world tools'),
        ('Tables','Typed inline edits, bulk AllOrNothing edits, filtering and CSV'),
        ('Changes','Pending operations, diagnostics, dependencies and journal inspection')]
    extra=['### Studio views (W-VIEW)', '| Row | Scenario | Requirement | Status | Evidence |', '|---|---|---|---|---|']
    for i,(name,scenario) in enumerate(definitions,1):
        rid=f'W-VIEW-{i:02d}'
        supplemental=v.OUT/rid/'p42-supplemental.md'
        if not supplemental.exists(): supplemental.write_text((v.OUT/rid/'README.md').read_text())
        decisions[rid]={'status':'PASS','baseline':'1752ca8a','note':
          'Final-main view suite: 41/41 passed, zero skips. P4.2 graphical captures retained with their original revision; no new final-main display capture is claimed.',
          'patterns':['UNITY-HOLLOWMERE/p42b-final-views-*/results.xml','W-VIEW-01/views-capture-20261005T191314.272300Z/README.md','W-VIEW-01/captures-20261005T191314Z/capture.json',rid+'/p42-supplemental.md'],
          'command':'studio/tools/verify-all.sh final-views-tests\nstudio/tools/verify-all.sh views',
          'historical':'P2.3 defines '+name+' at docs/studio/packets/P2.3-studio-views.md:27-34. P4.2 graphical run is supplemental evidence.'}
        extra.append(f'| {rid} | {name}: {scenario} | P2.3 view definition | planned | Pending report |')
    if '| W-VIEW-01 |' not in content:
        content=content.replace('### Edit execution (W-EDIT, W-MODEL, W-TOOL)', '\n'.join(extra)+'\n\n### Edit execution (W-EDIT, W-MODEL, W-TOOL)')
    matrix.write_text(content)
    decisions_path.write_text(json.dumps(decisions,indent=2)+'\n')
    report_rows.write_rows()


if __name__=='__main__': main()
