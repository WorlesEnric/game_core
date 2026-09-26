#!/usr/bin/env python3
"""Source-level invariants of one gate change set (interpreter-level; NOT a build).

A gate revision is a handful of hand-written C# files plus the harnesses that pin them, and the classes of defect that
survive a careful read are exactly the ones a compiler or a suite catches late: a call to a member that does not exist,
a type used but not imported, a name that resolves to two imported namespaces, and a frozen observation table that no
longer agrees with the digest literal, the suite or the probe harness. Running the whole build to find them is the
build host's job; this tool finds them in under a second, on any machine, with no Unity and no .NET SDK.

For each file it checks:

  * balance of braces, parentheses and brackets, with comments and string literals removed first;
  * every `Type.Member` access resolves against the declaring sources (only for names it can see - this is a
    spell-checker, not a type checker, which is why the suite and the player probe still exist);
  * every type a file uses is reachable through that file's own `using` directives;
  * no name a file uses is declared in two namespaces that file imports at once;
  * the frozen observation tables agree with every digest literal and with every declaration site, and the probe
    harnesses require exactly the steps those tables imply (the Wave 6 gate's five literals and this gate's own W7
    table, the latter only when the Wave 7 files are in the tree, so a subset invocation stays valid).

Usage:
    python3 tools/check_gate_sources.py [--file PATH ...] [--json <path>]

The default file list is the Wave 6 gate's own change set; `--file` replaces it (the Wave 7 gate passes its own list).

Exit codes: 0 every invariant holds; 1 at least one fails; 2 a named file does not exist.
"""
import argparse
import collections
import hashlib
import json
import os
import re
import sys


parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
parser.add_argument('--file', action='append', dest='files', metavar='PATH',
                    help='a C# file to check; repeatable, and it replaces the default list')
parser.add_argument('--json', metavar='PATH', help='write the verdict here as well')
args = parser.parse_args()

DEFAULT_FILES = [
 'unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/W6GateScenario.cs',
 'unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/W6GateFamily.cs',
 'unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/W6CompositionAudit.cs',
 'unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/W6FamilyNarrativeHost.cs',
 'unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/W6FamilyCardsHost.cs',
 'unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/W6FamilyTraversalHost.cs',
 'unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/ProbeW6Gate.cs',
 'unity/GameCore.Validation/Assets/GameCore.Validation/Tests/W6Gate/W6GateIntegrationTests.cs',
 'unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/ProbeArguments.cs',
 'unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/ProbeRunner.cs',
 'unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/Gc020TraversalHost.cs',
 'unity/GameCore.Validation/Assets/GameCore.Validation/Editor/LifecyclePlayModeMatrix.cs',
]


CS = list(args.files) if args.files else DEFAULT_FILES
for _f in CS:
    if not os.path.isfile(_f):
        print(f'check_gate_sources: no such file: {_f}', file=sys.stderr)
        raise SystemExit(2)


def strip_comment_and_strings(line):
    out = ''; i = 0; n = len(line)
    while i < n:
        if line[i:i + 2] == '//':
            break
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
        if c == '"':
            i += 1
            while i < n:
                if line[i] == '\\': i += 2; continue
                if line[i] == '"': i += 1; break
                i += 1
            out += '""'; continue
        if c == "'":
            i += 1
            while i < n:
                if line[i] == '\\': i += 2; continue
                if line[i] == "'": i += 1; break
                i += 1
            out += "''"; continue
        out += c; i += 1
    return out


print('== balance (.cs; comments and literals removed) ==')
balanced = True
for f in CS:
    depth = {'{': 0, '(': 0, '[': 0}; pairs = {'}': '{', ')': '(', ']': '['}
    for line in open(f, encoding='utf-8'):
        for ch in strip_comment_and_strings(line):
            if ch in depth: depth[ch] += 1
            elif ch in pairs: depth[pairs[ch]] -= 1
    ok = all(v == 0 for v in depth.values())
    balanced &= ok
    print(f'   {os.path.basename(f):34s} ' + ('balanced' if ok else 'UNBALANCED ' + str(depth)))

decl = re.compile(r'^\s*(?:\[[^\]]*\]\s*)?(?:public |internal |private |protected )?(?:sealed |static |abstract |readonly |partial |unsafe )*(class|struct|interface|enum) (\w+)', re.M)
typefiles = collections.defaultdict(set); typens = collections.defaultdict(set); typekind = {}
for root in ('Packages', 'tests', 'unity'):
    for d, subdirs, fs in os.walk(root):
        subdirs[:] = [name for name in subdirs if name not in {'bin', 'obj', 'Library', 'GameCore.ReleaseCheck'}]
        for f in fs:
            if not f.endswith('.cs'): continue
            p = os.path.join(d, f)
            try: s = open(p, encoding='utf-8', errors='replace').read()
            except OSError: continue
            ns = re.search(r'^namespace\s+([\w\.]+)', s, re.M)
            for kind, t in set(decl.findall(s)):
                typefiles[t].add(p)
                typens[t].add(ns.group(1) if ns else '')
                typekind[t] = kind

# A simple name can bind to a MEMBER rather than a type - `public RouteId CommandRoute => ...` implementing an
# interface property is the pattern this repository already uses - so a name that some file in the namespace under
# check declares as a member is not evidence of a missing `using`.
member_names_by_namespace = collections.defaultdict(set)
for _t, _files in typefiles.items():
    for _p in _files:
        _s = open(_p, encoding='utf-8', errors='replace').read()
        _ns = re.search(r'^namespace\s+([\w\.]+)', _s, re.M)
        if not _ns:
            continue
        for _m in re.finditer(r'\b([A-Za-z_]\w*)\s*(?:<[^<>]*>)?\s*(?:\{|=>|;|=)', _s):
            member_names_by_namespace[_ns.group(1)].add(_m.group(1))

memo = {}
def members(t):
    if t in memo: return memo[t]
    ms = set()
    for p in typefiles[t]:
        for m in re.finditer(r'\b([A-Za-z_]\w*)\s*(?:<[^<>]*>)?\s*(?:\(|\{|=>|;|=)', open(p, encoding='utf-8', errors='replace').read()):
            ms.add(m.group(1))
    memo[t] = ms; return ms

own = set()
for f in CS: own |= set(t for _, t in decl.findall(open(f).read()))
unresolved = []; unreachable = []; ambiguous = []
for f in CS:
    s = open(f).read()
    ns = re.search(r'^namespace\s+([\w\.]+)', s, re.M)
    usings = set(re.findall(r'^using\s+([\w\.]+);', s, re.M)) | ({ns.group(1)} if ns else set())
    for i, line in enumerate(s.split('\n'), 1):
        st = line.strip()
        if st.startswith('//') or st.startswith('///') or st.startswith('*'): continue
        code = strip_comment_and_strings(line)
        for m in re.finditer(r'\b([A-Z]\w+)\.([A-Za-z_]\w*)', code):
            t, mem = m.group(1), m.group(2)
            if t in own or t not in typefiles: continue
            if mem == 'ToString' and typekind.get(t) == 'enum': continue
            if mem not in members(t):
                unresolved.append((os.path.basename(f), i, t, mem, st[:70]))
    # Strip comments and literals LINE BY LINE: the helper is written for one line, so handing it a joined
    # multi-line body would truncate everything after the first `//` and silently check nothing.
    body = '\n'.join(strip_comment_and_strings(l) for l in s.split('\n') if not l.strip().startswith('///'))
    body = re.sub(r'^using[^\n]*$', '', body, flags=re.M)
    for t in sorted(set(re.findall(r'(?<![\.\w])([A-Z]\w+)\b', body))):
        if t in own or t not in typefiles: continue
        nss = {n for n in typens[t] if n}
        if not nss: continue
        if t in member_names_by_namespace.get(ns.group(1) if ns else '', set()):
            continue
        if not (nss & usings):
            unreachable.append((os.path.basename(f), t, sorted(nss)))
        elif len(nss & usings) > 1:
            ambiguous.append((os.path.basename(f), t, sorted(nss & usings)))

print()
print('== member resolution (Type.Member against the declaring sources) ==')
print('   unresolved:', unresolved if unresolved else 'none')
print('== using coverage (a used type must be reachable through the file\'s own usings) ==')
print('   unreachable:', sorted({(f, t, tuple(n)) for f, t, n in unreachable}) if unreachable else 'none')
print('== ambiguity (a used name declared in two imported namespaces) ==')
print('   ambiguous:', sorted({(f, t, tuple(n)) for f, t, n in ambiguous}) if ambiguous else 'none')

# ----------------------------------------------------------------------------------------------------------------
# The `ProbeArguments.Parse` call site's own locals. THIS IS THE ONE CHECK THAT WOULD HAVE CAUGHT A REAL DEFECT this
# gate shipped and then found: an edit removed `string? resultPath = null;` from `Parse`'s local block while the
# constructor call still passed `resultPath`, which is a C# compile error (CS0103) that a balance check cannot see, a
# member-resolution check cannot see (the name IS declared - as a field of the type, not as a local) and the release
# clone's constructor-arity check cannot see either (the arity still matched). So every identifier the call passes must
# be a local the same method declares, and a missing one is reported here.
# ----------------------------------------------------------------------------------------------------------------
parse_problems = []
PA = 'unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/ProbeArguments.cs'
if os.path.isfile(PA) and PA in CS:
    pa_text = open(PA).read()
    declared = set(re.findall(r'^\s*(?:bool|string\?)\s+(\w+)\s*=\s*', pa_text, re.M))
    call_match = re.search(r'return new ProbeArguments\((.*?)\);', pa_text, re.S)
    if call_match is None:
        parse_problems.append(PA + ': no `return new ProbeArguments(...)` call found')
    else:
        passed = [a.strip() for a in re.sub(r'\s+', ' ', call_match.group(1)).split(',') if a.strip()]
        for name in passed:
            if not re.match(r'^\w+$', name):
                parse_problems.append(PA + ': the call passes a non-identifier argument: ' + repr(name))
            elif name not in declared:
                parse_problems.append(
                    PA + ': the constructor call passes `' + name
                    + '`, which `Parse` never declares as a local (CS0103)')
    print('== the Parse call site\'s own locals ==')
    print('   declared locals:', len(declared), '| problems:', parse_problems or 'none')
else:
    print('== the Parse call site\'s own locals ==')
    print('   not checked: ' + PA + ' is not in this change set')

print()
print('== frozen observation tables, digest literals and probe steps ==')
src = open('unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/W6GateScenario.cs').read()
def names_of(a):
    m = re.search(a + r'\s*=\s*\{(.*?)\};', src, re.S); return re.findall(r'"([^"]+)"', m.group(1))
trav = names_of('TraversalObservationNames'); narr = names_of('NarrativeObservationNames'); cards = names_of('CardsObservationNames')
def dig(label, names, prefix=''):
    return hashlib.sha256('\n'.join(prefix + label + '/' + n + '=pass' for n in names).encode()).hexdigest()
R = 'unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/'
literals = {
 'narrative generated': (dig('narrative', narr), R + 'W6FamilyNarrativeHost.cs'),
 'narrative fixture': (dig('narrative', narr, 'fixture:'), R + 'W6FamilyNarrativeHost.cs'),
 'cards generated': (dig('cards', cards), R + 'W6FamilyCardsHost.cs'),
 'cards fixture': (dig('cards', cards, 'fixture:'), R + 'W6FamilyCardsHost.cs'),
 'traversal': (dig('traversal', trav), R + 'W6FamilyTraversalHost.cs'),
}
probe = open(R + 'ProbeW6Gate.cs').read()
suite = open('unity/GameCore.Validation/Assets/GameCore.Validation/Tests/W6Gate/W6GateIntegrationTests.cs').read()
harness = open('tools/unity/run_w6_gate_probe.sh').read()
agree = True
for label, (literal, host) in sorted(literals.items()):
    where = dict(host=literal in open(host).read(), probe=literal in probe, suite=literal in suite, harness=literal in harness)
    agree &= all(where.values())
    print(f'   {label:20s} {literal}  ' + ' '.join(f'{k}={v}' for k, v in sorted(where.items())))
print(f'   observation counts: narrative {len(narr)}, cards {len(cards)}, traversal {len(trav)}')
steps = re.findall(r'^\s*\x27("name": "[^"]+")\x27\s*$', harness, re.M)
expected = ['"name": "w6-gate-cycle-count"', '"name": "w6-gate-native-leak-detection"', '"name": "w6-gate-telemetry-build-shape"']
for n in narr: expected.append('"name": "narrative/%s"' % n)
for n in narr: expected.append('"name": "fixture:narrative/%s"' % n)
for n in cards: expected.append('"name": "cards/%s"' % n)
for n in cards: expected.append('"name": "fixture:cards/%s"' % n)
for n in trav: expected.append('"name": "traversal/%s"' % n)
expected += ['"name": "w6-narrative-digest"', '"name": "w6-cards-digest"', '"name": "w6-traversal-digest"']
missing_steps = [e for e in expected if e not in steps]; extra_steps = [s for s in steps if s not in expected]
print(f'   harness steps: {len(steps)} | missing: {missing_steps or "none"} | extra: {extra_steps or "none"}')

# ----------------------------------------------------------------------------------------------------------------
# The Wave 7 gate's own frozen table, checked the same way and only when its files exist, so this checker stays
# usable on a subset of the tree (its `--file` list is a subset) while a Wave 7 invocation gets the same
# falsifiability the Wave 6 block above provides: the observation table, the quoted literal, the probe's table and
# the harness's required steps must all be the SAME table.
# ----------------------------------------------------------------------------------------------------------------
w7_good = True
w7_checked = False
W7 = 'unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/W7GateScenario.cs'
W7_HARNESS = 'tools/unity/run_w7_gate_probe.sh'
if os.path.isfile(W7) and os.path.isfile(W7_HARNESS):
    w7_checked = True

    def w7_names(array_name, text):
        m = re.search(array_name + r'\s*=\s*\{(.*?)\};', text, re.S)
        return re.findall(r'"([^"]+)"', m.group(1)) if m else []

    w7_src = open(W7).read()
    proc = w7_names('ProcessObservationNames', w7_src)
    fam = w7_names('FamilyObservationNames', w7_src)
    con = w7_names('ConformanceObservationNames', w7_src)

    # The three family labels are read from the hosts the scenario names, so a relabelled genre changes this table
    # instead of being silently absent from it.
    labels = []
    for host, const_name in (('Gc013NarrativeHost', 'Label'), ('Gc013CardsHost', 'Label'), ('Gc020TraversalHost', 'Label')):
        host_text = open(R + host + '.cs').read()
        found = re.search(r'public const string ' + const_name + r'\s*=\s*"([^"]+)"', host_text)
        if found:
            labels.append(found.group(1))
    # The emission order `W7GateScenario.ObservationNames()` fixes: the process group, then the recovery group per
    # family, then GC-024's conformance group.
    w7_table = ['w7/' + n for n in proc] + [lab + '/' + n for lab in labels for n in fam] + ['w7/' + n for n in con]
    w7_literal = hashlib.sha256('\n'.join(n + '=pass' for n in w7_table).encode()).hexdigest()
    w7_probe = open(R + 'ProbeW7Gate.cs').read()
    w7_suite = open('unity/GameCore.Validation/Assets/GameCore.Validation/Tests/W7Gate/W7GateIntegrationTests.cs').read()
    w7_harness = open(W7_HARNESS).read()
    # The suite does not COPY the literal (a copied literal can drift); it recomputes it from the table alone and
    # compares it with the probe's constant, which is the same falsifiability one step stronger. So the suite's leg is
    # that it really performs that recomputation against the probe's own value.
    suite_recomputes = ('W7GateScenario.ExpectedDigest()' in w7_suite and 'ProbeW7Gate.ExpectedDigest' in w7_suite
                        and 'W7GateScenario.ObservationNames()' in w7_suite)
    w7_where = dict(probe=w7_literal in w7_probe, suite=suite_recomputes, harness=w7_literal in w7_harness)
    w7_good &= all(w7_where.values())
    print(f'   {"w7 table":20s} {w7_literal}  ' + ' '.join(f'{k}={v}' for k, v in sorted(w7_where.items())))
    print(f'   w7 observation counts: process {len(proc)}, per family {len(fam)}, families {labels}, '
          f'conformance {len(con)}')

    w7_steps = re.findall(r'^\s*\x27("name": "[^"]+")\x27\s*$', w7_harness, re.M)
    w7_expected = ['"name": "%s"' % n for n in w7_table]
    w7_expected += ['"name": "w7/w7-gate-digest"', '"name": "w7-frozen-digest-agrees-with-the-table"']
    w7_missing = [e for e in w7_expected if e not in w7_steps]
    w7_extra = [s for s in w7_steps if s not in w7_expected]
    w7_good &= not w7_missing and not w7_extra
    print(f'   w7 harness steps: {len(w7_steps)} | missing: {w7_missing or "none"} | extra: {w7_extra or "none"}')
else:
    print('   w7 table: NOT CHECKED (the Wave 7 gate files are not in this tree)')

good = (balanced and not unresolved and not unreachable and not ambiguous and agree and not missing_steps
        and not extra_steps and w7_good and not parse_problems)
print('VERDICT:', 'ok' if good else 'REVIEW NEEDED')
report = {
    'task': 'W7-GATE',
    'check': 'gate-sources',
    'files': CS,
    'unresolvedMembers': [list(x) for x in unresolved],
    'unreachableTypes': sorted({(f, t, tuple(n)) for f, t, n in unreachable}),
    'w7TableChecked': w7_checked,
    'w7TableAgreement': w7_good if w7_checked else None,
    'ambiguousNames': sorted({(f, t, tuple(n)) for f, t, n in ambiguous}),
    'parseCallSiteProblems': parse_problems,
    'unbalancedSources': [] if balanced else 'see console',
    'digestAgreement': agree,
    'harnessSteps': len(steps),
    'harnessStepsMissing': missing_steps,
    'harnessStepsExtra': extra_steps,
    'status': 'Pass' if good else 'Fail',
}
rendered = json.dumps(report, indent=2, sort_keys=True)
if args.json:
    parent = os.path.dirname(os.path.abspath(args.json))
    if parent and not os.path.isdir(parent):
        os.makedirs(parent, exist_ok=True)
    with open(args.json, 'w', encoding='utf-8') as handle:
        handle.write(rendered + '\n')
raise SystemExit(0 if good else 1)
