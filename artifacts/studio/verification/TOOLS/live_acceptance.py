#!/usr/bin/env python3
"""Final-main reacceptance lanes; immutable receipts use the P4.2 runner."""
import sys
import verify as v

if __name__ == '__main__':
    if sys.argv[1] == 'live-install':
        record = v.run('INSTALL-P4.2b', 'final-main-release', ['bash', 'artifacts/studio/verification/TOOLS/install-p42b.sh'])
        if record['exitCode'] == 2:
            record.update(status='BLOCKED', note='Documented live-run/display guard occupied; no installation or service change.')
            v.finish(v.ROOT / record['evidencePath'], record)
    elif sys.argv[1] == 'final-views-tests':
        v.unity_run('hollowmere', 'EditMode', 'p42b-final-views', r'GameCore\.Studio\.Views\..*|Hollowmere\.P2_3\..*|Hollowmere\.R2_E\..*')
    elif sys.argv[1] == 'final-checks':
        v.run('STATIC', 'p42b-metadata', ['python3', 'tools/check_package_metadata.py'])
        v.run('STATIC', 'p42b-csharp', ['python3', 'tools/check_game_core_csharp.py'])
        v.run('STATIC', 'p42b-installer', ['python3', '-m', 'unittest', 'discover', '-s', 'studio/etos/tests', '-v'])
        v.run('STATIC', 'p42b-runner', ['python3', '-m', 'unittest', 'discover', '-s', 'studio/tools/Tests/P4_2', '-v'])
    elif sys.argv[1] == 'final-timing':
        for attempt in (1, 2):
            v.run('B-EDIT', 'p42b-dataset-' + str(attempt), ['bash', 'artifacts/studio/verification/TOOLS/timing-p42b.sh', '{out}', str(attempt)], results='results.xml', env={'GAMECORE_ETOS_AUTOSTART': '0', 'GAMECORE_P42_EVIDENCE': '{out}'}, editor=True)
    elif sys.argv[1] == 'tariff-tests':
        command = ['python3', '-m', 'unittest', 'discover', '-s', 'artifacts/studio/verification/TOOLS/tests', '-v']
        v.run('INSTALL-P4.2b', 'owner-tariff-before', command, env={'P42B_BEFORE': '1'})
        v.run('INSTALL-P4.2b', 'owner-tariff-after', command)
    elif sys.argv[1] == 'final-guides':
        v.run('W-DOC-02', 'p42b-literal-plugin-guide', ['python3', 'artifacts/studio/verification/TOOLS/guide-p42b.py', '{out}'])
    elif sys.argv[1] == 'live-preflight':
        record = v.run('INSTALL-P4.2b', 'live-guard', ['python3', 'artifacts/studio/verification/TOOLS/preflight-p42b.py', '{out}/preflight.json'])
        if record['exitCode'] == 2:
            record.update(status='BLOCKED', note='The documented live-run guard detected an active P3 process. No install, configuration apply, companion restart or paid operation was attempted.')
            v.finish(v.ROOT / record['evidencePath'], record)
    elif sys.argv[1] == 'final-selection':
        for attempt in (1, 2):
            v.run('B-SELECT', 'p42b-selection-' + str(attempt), ['bash', 'artifacts/studio/verification/TOOLS/timing-p42b.sh', '{out}', str(attempt), 'P42b.Acceptance.TimingTests.R2_38_B_SELECT_100PicksAnd500CandidateMarquee'], results='results.xml', env={'GAMECORE_ETOS_AUTOSTART': '0', 'GAMECORE_P42_EVIDENCE': '{out}'}, editor=True)
    elif sys.argv[1] == 'release-build':
        v.run('INSTALL-P4.2b', 'final-main-build', ['bash', 'artifacts/studio/verification/TOOLS/build-companion-p42b.sh'])
    elif sys.argv[1] == 'release-binary':
        v.run('INSTALL-P4.2b', 'final-main-release-binary', [str(v.Path.home()/'.cargo/bin/cargo'), 'build', '--release', '--locked'], cwd=v.ROOT/'.evidence/companion-next/studio/agent')
    elif sys.argv[1] == 'node-equivalence':
        record = v.run('W-GAME-07', 'p42b-network-namespace-prerequisite', ['unshare', '--net', 'true'])
        if record['exitCode']:
            record.update(status='BLOCKED', note='Non-destructive network-namespace equivalent is unavailable (unshare failed); stopping etosd is forbidden.')
            v.finish(v.ROOT / record['evidencePath'], record)
    elif sys.argv[1] == 'guide-open':
        v.run('W-DOC-01', 'p42b-fresh-guide-open', ['bash', 'artifacts/studio/verification/TOOLS/guide-open-p42b.sh', '{out}'], results='results.xml', editor=True)
    elif sys.argv[1] == 'harness-checks':
        v.run('STATIC', 'p42b-packet-regressions', ['python3', '-m', 'unittest', 'discover', '-s', 'artifacts/studio/verification/TOOLS/tests', '-v'])
        v.run('STATIC', 'p42b-runner-final', ['python3', '-m', 'unittest', 'discover', '-s', 'studio/tools/Tests/P4_2', '-v'])
        v.run('STATIC', 'p42b-shell-syntax', ['bash', '-n', 'studio/tools/verify-all.sh', 'artifacts/studio/verification/TOOLS/build-companion-p42b.sh', 'artifacts/studio/verification/TOOLS/install-p42b.sh', 'artifacts/studio/verification/TOOLS/timing-p42b.sh', 'artifacts/studio/verification/TOOLS/guide-open-p42b.sh'])
    elif sys.argv[1] == 'evidence-check':
        v.run('STATIC', 'p42b-evidence-check', ['python3', 'artifacts/studio/verification/TOOLS/check_p42b.py'])
    elif sys.argv[1] == 'guide-stage':
        v.run('W-DOC-02', 'p42b-plugin-guide-built-prerequisite', ['bash', 'artifacts/studio/verification/TOOLS/guide-stage-p42b.sh', '{out}'], editor=True)
    v.summary()
    sys.exit(int(any(r['status'] != 'PASS' for r in v.RESULTS)))
