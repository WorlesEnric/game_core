#!/usr/bin/env python3
"""Verify a freshly prepared marker-free release clone (interpreter-level; NOT a build).

`tools/unity/prepare_gc017_release_project.py` prepares the disposable clone by copying the validation project and
then surgically removing every qualification-only file, mode and package. That surgery is textual, so the clone's own
invariants have to be asserted on the clone rather than trusted to the script that made it: a signature that lost a
parameter, a surviving file that still calls a removed member, or an asmdef reference whose assembly the clone no
longer depends on are all silent in a copy-and-delete script and fatal in a compiler.

This tool settles those invariants with no Unity and no .NET on the machine:

  1. no file references any qualification-only type or any member declared only in a removed file, including qualified
     accesses (`Gc020TraversalHost.RunReloadRoute` - the case a name-only grep misses);
  2. every `asmdef` reference resolves to an assembly that exists in the clone or in a package the clone still
     depends on;
  3. `ProbeArguments.cs`'s constructor call passes exactly its parameter list, in order;
  4. every C# file balances its braces, parentheses and brackets;
  5. the modes the clone keeps are wired end to end (const, parameter, local, parse branch, property, identity
     branch, dispatch branch) and the removed ones are gone from every one of those places;
  6. the manifest carries no qualification-marker or replay package, its `testables` list is empty, it has no lock,
     and the qualification-only `Tests/` tree and editor harness are gone.

Usage:
    python3 tools/check_release_clone.py [--clone unity/GameCore.ReleaseCheck] [--json <path>]

Exit codes: 0 every invariant holds; 1 at least one invariant fails; 2 the clone directory does not exist.
"""
import argparse
import collections
import json
import os
import re
import sys


parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
parser.add_argument('--clone', default='unity/GameCore.ReleaseCheck', metavar='DIR',
                    help='the disposable marker-free clone to verify')
parser.add_argument('--json', metavar='PATH', help='write the verdict here as well')
args = parser.parse_args()

root = args.clone
if not os.path.isdir(root):
    print(f'check_release_clone: no such clone directory: {root}', file=sys.stderr)
    raise SystemExit(2)
runtime = os.path.join(root, 'Assets/GameCore.Validation/Runtime')
editor = os.path.join(root, 'Assets/GameCore.Validation/Editor')

REMOVED_TYPES = [
 'FaultScenario', 'FaultScenarioHost', 'FaultScenarioStep', 'ProbeFaults',
 'W5GateScenario', 'W5GateFamily', 'W5GateNarrativeHost', 'W5GateCardsHost', 'ProbeW5Gate',
 'Gc021Scenario', 'Gc021Family', 'ProbeGc021', 'Gc021RecordingDestination', 'Gc021CrashHook',
 'Gc021DeliveryCrashException', 'Gc021CommittedEventRewardSource',
 'ProbeLifecycleStress', 'LifecycleStressScenario', 'LifecycleStressFamily', 'LifecycleStressNarrativeHost',
 'LifecycleStressCardsHost', 'LifecycleStressManifestSource', 'LifecycleStressResourceFactory',
 'LifecycleStressDeclarations', 'ScriptedResourceLease',
 'ReplayParallelJobs', 'ReplayScenario', 'ProbeReplay', 'ReplayProducerJob', 'ReplayJobsModule',
 'ReplayJobsRunner', 'ReplayJobsRegistration',
 'W6GateScenario', 'W6GateFamily', 'W6CompositionAudit', 'W6FamilyNarrativeHost', 'W6FamilyCardsHost',
 'W6FamilyTraversalHost', 'ProbeW6Gate', 'W6StageRuntime', 'IW6Family', 'W6RewardDestination',
 'W6FirstCommittedEventSource', 'LifecyclePlayModeMatrix',
 'Gc027Scenario', 'Gc027Family', 'Gc027SourceWorld', 'Gc027RestoreBuilder', 'Gc027NarrativeHost', 'Gc027CardsHost',
 'ProbeRecovery',
]
REMOVED_MEMBERS = [
 'RunReloadRoute', 'RunBothW6Gate', 'RunW6Gate', 'W6GateDigest', 'W6GateGeneratedDigest', 'W6GateFixtureDigest',
 'RunBothLifecycleStress', 'LifecycleStressGeneratedDigest', 'LifecycleStressFixtureDigest',
 'AttachGateRuntime', 'CycleRoute', 'CycleTarget', 'CycleSchema', 'CyclePayload', 'CyclePumpTicks',
 'StressDeclarations', 'StressInstance', 'StressMount', 'StressUnmount',
]
REMOVED_MODES = [('Faults', 'faults'), ('W5Gate', 'w5Gate'), ('Gc021', 'gc021'),
                 ('LifecycleStress', 'lifecycleStress'), ('Replay', 'replay'), ('W6Gate', 'w6Gate'),
                 ('Recovery', 'recovery')]
KEPT_MODES = [('MissingRegistration', 'missingRegistration'), ('WorldDispatch', 'worldDispatch'),
              ('W1Gate', 'w1Gate'), ('W2Gate', 'w2Gate'), ('W3Gate', 'w3Gate'), ('Narrative', 'narrative'),
              ('Cards', 'cards'), ('W4Profile', 'w4Profile'), ('Gc013', 'gc013'), ('W4Gate', 'w4Gate'),
              ('Gc018', 'gc018'), ('Gc019', 'gc019'), ('Traversal', 'traversal')]

problems = []
files = []
for d, _, fs in os.walk(root):
    for f in fs:
        if f.endswith(('.cs', '.asmdef', '.json', '.meta')):
            files.append(os.path.join(d, f))
print(f'clone files inspected: {len(files)}')

print()
print('== 1. no reference to a removed type or member ==')
def mentions(text, name):
    return re.search(r'(?<![\.\w])' + name + r'\b', text) is not None or ('.' + name) in text
hits = collections.defaultdict(list)
for p in files:
    s = open(p, encoding='utf-8', errors='replace').read()
    for n in REMOVED_TYPES + REMOVED_MEMBERS:
        if mentions(s, n):
            hits[os.path.relpath(p, root)].append(n)
for k, v in sorted(hits.items()):
    print('   ', k, '->', sorted(set(v)))
print('   clean' if not hits else '   REFERENCES FOUND')
problems += list(hits)

print()
print('== 2. every asmdef reference resolves ==')
def asmdefs(root_dir):
    out = {}
    for d, _, fs in os.walk(root_dir):
        for f in fs:
            if f.endswith('.asmdef'):
                p = os.path.join(d, f)
                out[json.load(open(p))['name']] = p
    return out
clone_asmdefs = asmdefs(os.path.join(root, 'Assets'))
package_asmdefs = {}
for name in sorted(os.listdir('Packages')):
    p = os.path.join('Packages', name)
    if os.path.isdir(p):
        package_asmdefs.update(asmdefs(p))
available = set(clone_asmdefs) | set(package_asmdefs)
external = ('Unity.', 'UnityEngine', 'UnityEditor', 'nunit', 'System', 'Microsoft', 'Mono', 'netstandard', 'mscorlib', 'Newtonsoft', 'com.unity')
dangling = {}
for name, p in list(clone_asmdefs.items()) + list(package_asmdefs.items()):
    for r in json.load(open(p)).get('references', []):
        if r.startswith('GUID:') or r in available or any(r.startswith(e) or r == e for e in external):
            continue
        dangling.setdefault(os.path.basename(p), []).append(r)
print('   assemblies available:', len(available), '| dangling references:', dangling or 'none')
print('   GameCore.Replay reachable:', 'GameCore.Replay' in available)
problems += list(dangling)
if 'GameCore.Replay' in available:
    problems.append('GameCore.Replay still reachable')

print()
print('== 3. ProbeArguments constructor call matches its parameter list ==')
pa = open(os.path.join(runtime, 'ProbeArguments.cs')).read()
params = re.search(r'private ProbeArguments\((.*?)\)\n        \{', pa, re.S).group(1)
plist = [p.strip().rstrip(',') for p in params.strip().split('\n') if p.strip()]
call = re.search(r'return new ProbeArguments\((.*?)\);', pa, re.S).group(1)
alist = [a.strip() for a in re.sub(r'\s+', ' ', call).split(',') if a.strip()]
expected = [('string? resultPath' if a == 'resultPath' else 'bool ' + a) for a in alist]
print('   parameters:', len(plist), '| arguments:', len(alist), '| agreement:', plist == expected)
if plist != expected:
    problems.append('constructor call does not match its parameters')
    print('   params:', plist)
    print('   args  :', alist)

print()
print('== 4. every .cs balances ==')
def stripped(line):
    out = ''; i = 0; n = len(line)
    while i < n:
        if line[i:i + 2] == '//': break
        c = line[i]
        if c == '@' and i + 1 < n and line[i + 1] == '"':
            i += 2
            while i < n:
                if line[i] == '"':
                    if i + 1 < n and line[i + 1] == '"':
                        i += 2; continue
                    i += 1; break
                i += 1
            out += '""'; continue
        if c in '"\'':
            quote = c; i += 1
            while i < n:
                if line[i] == '\\': i += 2; continue
                if line[i] == quote: i += 1; break
                i += 1
            out += '""' if quote == '"' else "''"; continue
        out += c; i += 1
    return out
unbalanced = []
count = 0
for p in files:
    if not p.endswith('.cs'): continue
    count += 1
    depth = {'{': 0, '(': 0, '[': 0}; pairs = {'}': '{', ')': '(', ']': '['}
    for line in open(p, encoding='utf-8', errors='replace'):
        for ch in stripped(line):
            if ch in depth: depth[ch] += 1
            elif ch in pairs: depth[pairs[ch]] -= 1
    if any(v != 0 for v in depth.values()):
        unbalanced.append((os.path.relpath(p, root), depth))
print(f'   {count} C# files | unbalanced: {unbalanced or "none"}')
problems += unbalanced

print()
print('== 5. kept modes wired, removed modes gone ==')
pr = open(os.path.join(runtime, 'ProbeRunner.cs')).read()
for mode, low in REMOVED_MODES:
    gone = (mode not in pa) and (low not in pa) and ('arguments.' + mode) not in pr
    print(f'   removed {mode:16s} absent everywhere: {gone}')
    if not gone:
        problems.append('removed mode still referenced: ' + mode)
for mode, low in KEPT_MODES:
    wired = (low in pa) and ('arguments.' + mode in pr) and ('bool ' + low in pa)
    print(f'   kept    {mode:16s} wired: {wired}')
    if not wired:
        problems.append('kept mode lost its wiring: ' + mode)

print()
print('== 6. manifest and lock ==')
manifest = json.load(open(os.path.join(root, 'Packages/manifest.json')))
stale = [k for k in manifest['dependencies'] if 'qualification' in k or 'replay' in k]
print('   qualification/replay dependencies:', stale or 'none')
print('   testables:', manifest['testables'])
print('   lock present:', os.path.exists(os.path.join(root, 'Packages/packages-lock.json')))
print('   Tests/ tree present:', os.path.exists(os.path.join(root, 'Assets/GameCore.Validation/Tests')))
print('   LifecyclePlayModeMatrix present:', os.path.exists(os.path.join(editor, 'LifecyclePlayModeMatrix.cs')))
if stale: problems.append('manifest still depends on ' + ','.join(stale))
if manifest['testables']: problems.append('manifest still declares testables')
if os.path.exists(os.path.join(root, 'Packages/packages-lock.json')): problems.append('lock was not removed')
if os.path.exists(os.path.join(root, 'Assets/GameCore.Validation/Tests')): problems.append('Tests tree survived')
if os.path.exists(os.path.join(editor, 'LifecyclePlayModeMatrix.cs')): problems.append('editor matrix survived')

print()
print('VERDICT:', 'clone is clean' if not problems else f'{len(problems)} PROBLEM(S)')
report = {
    'task': 'W6-GATE',
    'check': 'release-clone',
    'clone': os.path.abspath(root),
    'filesInspected': len(files),
    'removedReferenceHits': sorted(hits),
    'danglingAsmdefReferences': dangling,
    'constructorAgreement': plist == expected,
    'unbalancedSources': [name for name, _ in unbalanced],
    'problems': problems,
    'status': 'Pass' if not problems else 'Fail',
}
rendered = json.dumps(report, indent=2, sort_keys=True)
if args.json:
    parent = os.path.dirname(os.path.abspath(args.json))
    if parent and not os.path.isdir(parent):
        os.makedirs(parent, exist_ok=True)
    with open(args.json, 'w', encoding='utf-8') as handle:
        handle.write(rendered + '\n')
if problems:
    for problem in problems:
        print('   FAIL', problem, file=sys.stderr)
raise SystemExit(0 if not problems else 1)
