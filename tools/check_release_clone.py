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
  6. the manifest carries no qualification-only fixture package, its `testables` list is empty, and the
     qualification-only `Tests/` tree and editor harness are gone;
  7. the `packages-lock.json`, when one exists, names no qualification-only package and adds no `com.gamecore.*`
     dependency the manifest does not declare.

SCOPE: THE CLONE'S OWN SOURCES. Everything above is asserted on the clone's own files. Unity writes generated trees
that are not the clone's sources - `Library/` (whose `PackageCache` holds third-party package sources `this` checker
does not maintain), `Temp/`, `Logs/`, `Builds/` and `UserSettings/` - and it REGENERATES `Packages/packages-lock.json`
on import. Scanning the first would report other people's code as this clone's defect (a balance counter tuned to this
repository flags interpolated-string-heavy third-party sources), and treating the second as "the lock the preparer
left behind" is wrong the moment the project has been opened. So the walk prunes those directories at every level, and
the lock is checked for the two things that are defects in either state: a qualification-only package name, and a
`com.gamecore.*` entry the manifest does not declare. `tools/run_w7_gate.sh` runs this tool BEFORE it builds the
clone, which is the strictest moment; the rules above make a run after an import equally honest rather than reporting
a false alarm.

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
parser.add_argument('--self-test', action='store_true',
                    help='run this tool\'s own two-direction self-test on a synthetic clone and exit')
args = parser.parse_args()


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
 'Gc027Scenario', 'Gc027Family', 'Gc027SourceWorld', 'Gc027RestoreBuilder', 'Gc027PhysicsDomain', 'Gc027NarrativeHost',
 'Gc027CardsHost', 'Gc027TraversalHost', 'ProbeRecovery',
 'ProbeBenchmark', 'BenchmarkScenario', 'BenchmarkLiveWorld', 'LiveWorldFailure', 'BenchmarkOptions',
 'BenchmarkStep', 'BenchmarkScenarioResult', 'BenchmarkLiveFamily',
 'W7GateScenario', 'W7GateScenarioResult', 'W7GateStep', 'ProbeW7Gate',
]
REMOVED_MEMBERS = [
 'RunReloadRoute', 'RunBothW6Gate', 'RunW6Gate', 'W6GateDigest', 'W6GateGeneratedDigest', 'W6GateFixtureDigest',
 'RunBothLifecycleStress', 'LifecycleStressGeneratedDigest', 'LifecycleStressFixtureDigest',
 'W7GateScenario', 'W7GateScenarioResult', 'W7GateStep', 'ProbeW7Gate',
 # GC-024: the conformance runner, its three genre hosts' conformance halves, the combined cross-family world, the
 # world-preparation seam, the probe mode and the fixture package's own reader types. The package
 # `com.gamecore.reference-conformance` leaves through the manifest; nothing the clone keeps references either half.
 'ConformanceScenario', 'ConformanceHosts', 'ConformanceFamily', 'ConformanceWorldPreparation',
 'ConformanceCardsHost', 'ConformanceNarrativeHost', 'ConformanceTraversalHost', 'ConformanceCrossWorld',
 'ConformanceObservation', 'ConformanceStepStatus', 'ConformanceTableResult', 'ProbeConformance',
 'ConformanceStage', 'ConformanceScript', 'ConformanceStep', 'ConformanceExpectation', 'ConformanceField',
 'ConformanceFields', 'ConformanceOperations', 'ConformanceOracle', 'ConformanceVerdict', 'ConformanceTrace',
 'ConformanceValue', 'ConformanceOperationResult', 'ConformanceDocGaps',
 'ReferenceTables', 'ReferenceScripts', 'ReferenceProjections', 'CrossGraphValidation', 'CrossCompositionAudit',
 'AssemblyReferenceAudit', 'GenreAuditDocument',
 'AttachGateRuntime', 'CycleRoute', 'CycleTarget', 'CycleSchema', 'CyclePayload', 'CyclePumpTicks',
 'StressDeclarations', 'StressInstance', 'StressMount', 'StressUnmount',
]
REMOVED_MODES = [('Faults', 'faults'), ('W5Gate', 'w5Gate'), ('Gc021', 'gc021'),
                 ('LifecycleStress', 'lifecycleStress'), ('Replay', 'replay'), ('W6Gate', 'w6Gate'),
                 ('Recovery', 'recovery'), ('Benchmark', 'benchmark'), ('W7Gate', 'w7Gate'),
                 ('Conformance', 'conformance')]
KEPT_MODES = [('MissingRegistration', 'missingRegistration'), ('WorldDispatch', 'worldDispatch'),
              ('W1Gate', 'w1Gate'), ('W2Gate', 'w2Gate'), ('W3Gate', 'w3Gate'), ('Narrative', 'narrative'),
              ('Cards', 'cards'), ('W4Profile', 'w4Profile'), ('Gc013', 'gc013'), ('W4Gate', 'w4Gate'),
              ('Gc018', 'gc018'), ('Gc019', 'gc019'), ('Traversal', 'traversal'),
              ('CatalogCoverage', 'catalogCoverage'), ('RecoverySmoke', 'recoverySmoke')]

# A package the clone must not depend on, by the fragment its name carries, and the assembly whose reachability would
# mean a stripped fixture package is still resolvable.
STALE_PACKAGE_FRAGMENTS = ('qualification', 'replay', 'recovery', 'benchmarks', 'reference-conformance')
STRIPPED_ASSEMBLIES = ('GameCore.Replay', 'GameCore.Benchmarks', 'GameCore.ReferenceConformance')

# Directories Unity generates that are NOT the clone's own sources. Pruned at every level: `Library/PackageCache`
# holds third-party package sources this checker does not maintain, and a balance counter tuned to this repository
# would report other people's interpolated strings as this clone's defect. `obj`/`bin` are build output the dotnet
# side writes beside sources that DO belong to the clone.
GENERATED_DIRS = {'Library', 'Temp', 'Logs', 'Builds', 'UserSettings', 'obj', 'bin', 'obj~', 'Artifacts~'}


def stale_package(name):
    return any(fragment in name for fragment in STALE_PACKAGE_FRAGMENTS)

def run_checks(clone):
    """Every invariant of one prepared clone. Narrates as it goes; returns (problems, report)."""
    root = clone
    runtime = os.path.join(root, 'Assets/GameCore.Validation/Runtime')
    editor = os.path.join(root, 'Assets/GameCore.Validation/Editor')

    problems = []
    files = []
    for d, dirs, fs in os.walk(root):
        dirs[:] = sorted(name for name in dirs if name not in GENERATED_DIRS)
        for f in fs:
            if f.endswith(('.cs', '.asmdef', '.json', '.meta')):
                files.append(os.path.join(d, f))
    print(f'clone files inspected: {len(files)} (the clone\'s own sources; '
          + ', '.join(sorted(GENERATED_DIRS)) + ' pruned)')

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
        for d, dirs, fs in os.walk(root_dir):
            dirs[:] = sorted(name for name in dirs if name not in GENERATED_DIRS)
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
    print('   stripped assemblies reachable:',
          {name: name in available for name in STRIPPED_ASSEMBLIES})
    problems += list(dangling)
    for stripped in STRIPPED_ASSEMBLIES:
        if stripped in available:
            problems.append(stripped + ' still reachable')

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
        # The mode is gone when its FLAG LITERAL, its PROPERTY declaration, its CONSTRUCTOR parameter and its dispatch
        # branch are all absent. Substring tests are wrong here twice over: `recovery` is a prefix of the KEPT
        # `recoverySmoke` (and `-probeRecovery` of `-probeRecoverySmoke`), and a surviving doc comment may legitimately
        # contain the English word. So each part is matched as the code shape the merge would have had to keep.
        flag_gone = ('"' + '-probe' + mode + '"') not in pa
        property_gone = ('public bool ' + mode + ' { get; }') not in pa
        parameter_gone = ('bool ' + low + ',') not in pa
        dispatch_gone = (re.search(r'arguments\.' + mode + r'(?![\w])', pr) is None
                         and ('Named("' + mode + '"') not in pr)
        gone = flag_gone and property_gone and parameter_gone and dispatch_gone
        print(f'   removed {mode:16s} absent everywhere: {gone}')
        if not gone:
            problems.append('removed mode still referenced: ' + mode)
    for mode, low in KEPT_MODES:
        # "Wired" is four things, not three: the flag literal, the constructor parameter, the property declaration and
        # the ASSIGNMENT that connects the parameter to the property. The assignment was the missing fourth leg — a clone
        # that kept the flag and the parameter but dropped the assignment leaves a property that is always false, which
        # is exactly how a kept mode silently stops being reachable.
        wired = (('"' + '-probe' + mode + '"') in pa
                 and ('bool ' + low + ',') in pa
                 and ('public bool ' + mode + ' { get; }') in pa
                 and re.search(r'^\s*' + mode + r' = ' + low + r';', pa, re.M) is not None
                 and re.search(r'arguments\.' + mode + r'(?![\w])', pr) is not None)
        print(f'   kept    {mode:16s} wired: {wired}')
        if not wired:
            problems.append('kept mode lost its wiring: ' + mode)

    manifest = json.load(open(os.path.join(root, 'Packages/manifest.json')))
    stale = [k for k in manifest['dependencies'] if stale_package(k)]

    print('   qualification-only dependencies:', stale or 'none')
    print('   testables:', manifest['testables'])

    # The lock. The preparer DELETES it, and Unity REGENERATES it on import, so its absence and its presence are both
    # legitimate and neither is a verdict by itself. What is a defect in either state is a qualification-only package name,
    # and — because a regenerated lock is a resolution OF THE MANIFEST — a `com.gamecore.*` entry the manifest does not
    # declare, which would mean the manifest edit was lost. Unity's transitive registry entries are expected and reported
    # as facts, never as problems.
    lock_path = os.path.join(root, 'Packages/packages-lock.json')
    lock = json.load(open(lock_path)) if os.path.exists(lock_path) else None
    lock_stale = sorted(k for k in lock['dependencies'] if stale_package(k)) if lock else []
    lock_undeclared = []
    if lock:
        for k in lock['dependencies']:
            if k.startswith('com.gamecore.') and k not in manifest['dependencies']:
                lock_undeclared.append(k)
        lock_undeclared.sort()
    print('   lock present:', lock is not None,
          '(the preparer deletes it; Unity regenerates it on import - both states are allowed)')
    if lock:
        print('   lock entries:', len(lock['dependencies']),
              '| qualification-only:', lock_stale or 'none',
              '| undeclared com.gamecore.*:', lock_undeclared or 'none')
    print('   Tests/ tree present:', os.path.exists(os.path.join(root, 'Assets/GameCore.Validation/Tests')))
    print('   LifecyclePlayModeMatrix present:', os.path.exists(os.path.join(editor, 'LifecyclePlayModeMatrix.cs')))
    if stale: problems.append('manifest still depends on ' + ','.join(stale))
    if manifest['testables']: problems.append('manifest still declares testables')
    if lock_stale: problems.append('the lock still depends on ' + ','.join(lock_stale))
    if lock_undeclared: problems.append(
        'the lock declares com.gamecore.* dependencies the manifest does not: ' + ','.join(lock_undeclared))
    if os.path.exists(os.path.join(root, 'Assets/GameCore.Validation/Tests')): problems.append('Tests tree survived')
    if os.path.exists(os.path.join(editor, 'LifecyclePlayModeMatrix.cs')): problems.append('editor matrix survived')

    print()
    print('VERDICT:', 'clone is clean' if not problems else f'{len(problems)} PROBLEM(S)')

    report = {
        'task': 'W7-GATE',
        'check': 'release-clone',
        'clone': os.path.abspath(root),
        'filesInspected': len(files),
        'lockPresent': lock is not None,
        'lockQualificationOnly': lock_stale,
        'lockUndeclaredGameCore': lock_undeclared,
        'danglingAsmdefReferences': dangling,
        'constructorAgreement': plist == expected,
        'unbalancedSources': [name for name, _ in unbalanced],
        'problems': problems,
        'status': 'Pass' if not problems else 'Fail',
    }
    return problems, report

def main():
    root = args.clone
    if not os.path.isdir(root):
        print(f'check_release_clone: no such clone directory: {root}', file=sys.stderr)
        return 2

    problems, report = run_checks(root)
    print()
    print('VERDICT:', 'clone is clean' if not problems else f'{len(problems)} PROBLEM(S)')
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
    return 1 if problems else 0


# --------------------------------------------------------------------------------------------------------------------
# The self-test: this checker's two directions, on a synthetic clone built here rather than on a Unity import.
#
# WHY IT EXISTS. The checks are asserted on a clone's own sources, but Unity writes `Library/`, `Temp/`, `Logs/`,
# `Builds/` and `UserSettings/` beside them and REGENERATES `Packages/packages-lock.json`, and this host has no Unity
# to produce any of that. So the two directions are proved against a tree this file builds: a post-import tree whose
# ONLY defects live in the generated directories and the regenerated lock must pass, and the same tree with a defect
# planted in the clone's OWN sources must fail. A checker that cannot tell those two apart is the one that reported
# the build host's import as twenty-seven clone problems.
# --------------------------------------------------------------------------------------------------------------------

SELF_TEST_MARKER = 'W7GATE_SELF_TEST_PLANTED_DEFECT'


def _write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8') as handle:
        handle.write(text)


def _synthetic_clone(base):
    """A minimal clone whose own sources satisfy every check, plus the noise a real Unity import adds."""
    runtime = os.path.join(base, 'Assets/GameCore.Validation/Runtime')
    lines = ['#nullable enable', 'namespace Synthetic', '{', '    public readonly struct ProbeArguments', '    {']
    for mode, low in KEPT_MODES:
        lines.append('        private const string ' + low + 'Name = ' + '"' + '-probe' + mode + '";')
    lines.append('        private ProbeArguments(')
    lines.append('            bool missingRegistration,')
    for _, low in KEPT_MODES[1:]:
        lines.append('            bool ' + low + ',')
    lines.append('            string? resultPath)')
    lines.append('        {')
    lines.append('            MissingRegistration = missingRegistration;')
    for mode, low in KEPT_MODES[1:]:
        lines.append('            ' + mode + ' = ' + low + ';')
    lines.append('            ResultPath = resultPath;')
    lines.append('        }')
    for mode, _ in KEPT_MODES:
        lines.append('        public bool ' + mode + ' { get; }')
    lines.append('        public string? ResultPath { get; }')
    lines.append('        public static ProbeArguments Parse(string[] arguments)')
    lines.append('        {')
    for _, low in KEPT_MODES:
        lines.append('            bool ' + low + ' = false;')
    lines.append('            string? resultPath = null;')
    lines.append('            foreach (string argument in arguments)')
    lines.append('            {')
    for _, low in KEPT_MODES:
        lines.append('                if (argument == ' + low + 'Name) { ' + low + ' = true; }')
    lines.append('            }')
    lines.append('            return new ProbeArguments(')
    arg_names = [low for _, low in KEPT_MODES] + ['resultPath']
    lines.append('                ' + ', '.join(arg_names) + ');')
    lines.append('        }')
    lines.append('    }')
    lines.append('}')
    pa = '\n'.join(lines) + '\n'
    _write(os.path.join(runtime, 'ProbeArguments.cs'), pa)
    pr = ('#nullable enable\nnamespace Synthetic\n{\n    public static class ProbeRunner\n    {\n'
          '        public static void Run(ProbeArguments arguments)\n        {\n'
          + ''.join('            if (arguments.' + mode + ') { }\n' for mode, _ in KEPT_MODES)
          + '        }\n    }\n}\n')
    _write(os.path.join(runtime, 'ProbeRunner.cs'), pr)
    _write(os.path.join(base, 'Packages/manifest.json'),
           json.dumps({'dependencies': {'com.gamecore.contracts': 'file:../../../Packages/com.gamecore.contracts'},
                       'testables': []}, indent=2) + '\n')
    # The generated trees Unity writes, holding exactly the noise that made a source-only scan lie.
    _write(os.path.join(base, 'Library/PackageCache/com.unity.entities@1.4.6/Runtime/ThirdParty.cs'),
           '#nullable enable\nnamespace Third\n{\n    public class W {\n'
           '        public string D(int n) => $"w {n} of {n * 2}";\n'
           '        public string R() => "a scenario that replays";\n    }\n}\n')
    _write(os.path.join(base, 'Library/PackageCache/com.unity.entities@1.4.6/Runtime/Broken.cs'),
           '#nullable enable\nnamespace Third { public class Y { public void Z() { var s = "a { b"; } }\n')
    for rel in ('Temp/worker.txt', 'Logs/AssetImportWorker0.log', 'Builds/x.txt', 'UserSettings/EditorUserSettings.asset'):
        _write(os.path.join(base, rel), 'Gc027Scenario ProbeConformance Gc021Scenario\n')
    return base


def _lock(base, extra):
    deps = {'com.gamecore.contracts': {'version': 'file:../../../Packages/com.gamecore.contracts'}}
    deps.update({'com.unity.entities': {'version': '1.4.6'}, 'com.unity.burst': {'version': '1.8.28'}})
    deps.update(extra)
    _write(os.path.join(base, 'Packages/packages-lock.json'),
           json.dumps({'dependencies': deps}, indent=2) + '\n')


def self_test():
    """Both directions, on a synthetic post-import clone. Returns 0 when every case behaves."""
    import shutil
    import tempfile

    cases = []

    workdir = tempfile.mkdtemp(prefix='check-release-clone-selftest-')
    try:
        # Case A: a post-import clone whose only defects are in the generated trees and the regenerated lock.
        a = _synthetic_clone(os.path.join(workdir, 'a'))
        _lock(a, {})
        problems, report = run_checks(a)
        cases.append(('post-import noise is not a defect', not problems,
                      'problems=' + str(problems) + ' filesInspected=' + str(report['filesInspected'])))

        # Case B: the same clone with a removed type named in its OWN source.
        b = _synthetic_clone(os.path.join(workdir, 'b'))
        _lock(b, {})
        _write(os.path.join(b, 'Assets/GameCore.Validation/Runtime/ProbeRunner.cs'),
               open(os.path.join(b, 'Assets/GameCore.Validation/Runtime/ProbeRunner.cs'), encoding='utf-8').read()
               + '// ' + SELF_TEST_MARKER + ' Gc027Scenario\n')
        problems, _ = run_checks(b)
        cases.append(('a planted stale reference in the clone source fails', bool(problems),
                      'problems=' + str(sorted(str(problem) for problem in problems))))

        # Case C: the regenerated lock names a qualification-only package.
        c = _synthetic_clone(os.path.join(workdir, 'c'))
        _lock(c, {'com.gamecore.replay': {'version': 'file:../../../tests/GameCore.Replay'}})
        problems, _ = run_checks(c)
        cases.append(('a qualification-only lock entry fails', bool(problems),
                      'problems=' + str(sorted(str(problem) for problem in problems))))

        # Case D: the lock declares a com.gamecore.* package the manifest does not.
        d = _synthetic_clone(os.path.join(workdir, 'd'))
        _lock(d, {'com.gamecore.reference-conformance': {'version': 'file:../../../tests/GameCore.ReferenceConformance'}})
        problems, _ = run_checks(d)
        cases.append(('an undeclared com.gamecore.* lock entry fails', bool(problems),
                      'problems=' + str(sorted(str(problem) for problem in problems))))

        # Case E: an unbalanced file in the clone's own source.
        e = _synthetic_clone(os.path.join(workdir, 'e'))
        _lock(e, {})
        _write(os.path.join(e, 'Assets/GameCore.Validation/Runtime/Unbalanced.cs'),
               '#nullable enable\nnamespace Synthetic { public class Broken { public void M() { \n')
        problems, _ = run_checks(e)
        cases.append(('an unbalanced clone source fails', bool(problems),
                      'problems=' + str(sorted(str(problem) for problem in problems))))
    finally:
        shutil.rmtree(workdir, ignore_errors=True)

    failures = 0
    for label, ok, detail in cases:
        print(('   ok  ' if ok else '   FAIL') + ' ' + label + ' (' + detail + ')')
        if not ok:
            failures += 1
    print('SELF-TEST:', 'pass' if failures == 0 else f'{failures} case(s) failed')
    return 0 if failures == 0 else 1


if __name__ == '__main__':
    if '--self-test' in sys.argv:
        raise SystemExit(self_test())
    raise SystemExit(main())
