#!/usr/bin/env python3
"""Judge retained real worker output using R8's owner:asset-GUID graph contract; never submit."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sqlite3
import verify as v


def verify_query(request, receipt, trace, project_id):
    selected = request['selection']['targets']
    if len(selected) != 1:
        raise ValueError('Exactly one selected NPC required')
    nodes = request['request']['ContextSlice']['nodes']
    npc = next(node for node in nodes if node['ref'].get('assetGuid') == selected[0]['assetGuid'])
    reference = next(ref['to'] for ref in npc['refs'] if ref['field'] == 'dialogue')
    graph = next(node for node in nodes if node['type'] == 'dialogue.graph' and node['ref'].get('assetGuid') == reference['assetGuid'])
    owner = hashlib.sha256(json.dumps(['gamecore-unity', project_id], separators=(',', ':')).encode()).hexdigest()
    graph_id = owner + ':' + reference['assetGuid']
    expected = graph['fields']['nodes']['value']
    data = json.loads(receipt['stdout'])
    if receipt['exitCode'] != 0 or receipt['graphId'] != graph_id or data.get('complete') is not True or data.get('partial'):
        raise ValueError('Query exit/completeness/owner-scoped graph binding mismatch')
    returned = data['rows']
    if len(returned) != len(expected) or sorted(row['node_index'] for row in returned) != list(range(len(expected))):
        raise ValueError('Missing, duplicate or stale selected graph nodes')
    for row in returned:
        source = expected[row['node_index']]
        if row['owner'] != owner or row['graph'] != graph_id or row['removed'] is not False:
            raise ValueError('Foreign/stale graph row')
        if row['text'] != (source.get('text') or '') or row['kind'] != source['kind']:
            raise ValueError('Node differs from actual selected graph snapshot')
    command = receipt['command']
    proof = [call for call in trace if call['name'] == 'shell'
             and 'subprocess.run(command' in call['args'].get('command', '')
             and 'gc_dialogue_node' in call['args']['command']
             and owner in call['args']['command'] and reference['assetGuid'] in call['args']['command']
             and call.get('outcome', {}).get('ok') is True
             and 'rows ' + str(len(expected)) in call['outcome'].get('text', '')
             and 'exit 0' in call['outcome'].get('text', '')]
    if not proof or 'etos query' not in command or graph_id not in command:
        raise ValueError('No independent actual worker query execution witness')
    return {'status': 'PASS', 'selectedNpcAssetGuid': selected[0]['assetGuid'], 'graphAuthoringId': reference['authoringId'],
            'graphAssetGuid': reference['assetGuid'], 'graphId': graph_id, 'owner': owner, 'nodeCount': len(returned),
            'allNodeIndicesKindsAndTextsMatch': True, 'realWorkerQueryTrace': True,
            'observerCorrection': 'R8 identity is SHA256([app,projectId]):graphAssetGuid. The real worker may compose these two validated identity components rather than embed their concatenation as one literal. Original observer failures remain retained.',
            'newProviderCalls': 0}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--workflow', required=True, type=Path)
    args = parser.parse_args()
    folder = args.workflow.resolve()
    if not folder.is_relative_to(v.OUT.resolve()):
        parser.error('current owned verification workflow required')
    state = v.ROOT / 'artifacts/studio/workflows/P4.2j'
    activation = json.loads((state / 'worker-activation.json').read_text())
    project_id = next(identity for identity, path in activation['projects'].items() if path.endswith('/games/hollowmere'))
    tasks = (folder / 'task-ids.txt').read_text().splitlines()
    trace = []
    with sqlite3.connect((Path.home() / '.local/share/etos-studio/node.db').as_uri() + '?mode=ro', uri=True) as db:
        for task in tasks:
            if not re.fullmatch(r't[a-f0-9]+', task):
                raise ValueError('Invalid owned task identity')
            for turn, index, name, arguments, outcome in db.execute('SELECT turn,idx,name,args,outcome FROM tool_call WHERE task=? ORDER BY turn,idx', (task,)):
                trace.append({'taskId': task, 'turn': turn, 'index': index, 'name': name,
                              'args': json.loads(arguments), 'outcome': json.loads(outcome) if outcome else None})
    result = verify_query(json.loads((folder / 'request.json').read_text()), json.loads((folder / 'worker-query-receipt.json').read_text()), trace, project_id)
    result.update(revision=activation['productRevision'], releaseId=activation['release'], reportedAt=v.utc(), taskIds=tasks)
    (folder / 'worker-tool-trace.json').write_text(v.scrub(json.dumps(trace, indent=2)) + '\n')
    (folder / 'current-query-verification.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result))


if __name__ == '__main__':
    main()
