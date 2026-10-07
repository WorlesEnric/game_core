#!/usr/bin/env python3
"""Serial current-project row drivers; old outputs are retained then restored, never reused."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import verify as v

TOOLS = Path(__file__).resolve().parent
PROJECT = v.ROOT / 'games/hollowmere'
GRAPHICAL = str(TOOLS / 'unity-interactive-p42f.py')


def unity(row, label, method=None, test_filter=None, mode='EditMode', args=(), env=None, graphical=False, project=PROJECT):
    environment = {'GAMECORE_ETOS_AUTOSTART': '0', 'GAMECORE_P42_EVIDENCE': '{out}', 'GC_STUDIO_UNITY_SLOTS': '1', 'DISPLAY': ':1'}
    if graphical:
        environment['UNITY'] = GRAPHICAL if method else str(v.ROOT / 'games/hollowmere/Assets/Hollowmere/Tests/R7_C/batch-graphics.py')
    environment.update(env or {})
    command = ['bash', 'studio/tools/unity-batch.sh', '--project', project, '--log-dir', '{out}/logs', '--label', label, '--attempts', '1', '--timeout', '1800']
    if test_filter:
        command += ['--results', '{out}/results.xml', '--', '-runTests', '-testPlatform', mode, '-testFilter', test_filter]
    else:
        command += ['--', '-executeMethod', method]
    command += list(args)
    return v.run(row, label, command, env=environment, results='results.xml' if test_filter else None, editor=True)


def historical_output(path, action):
    before = {p.relative_to(path): p.read_bytes() for p in path.rglob('*') if p.is_file()}
    result = action()
    target = v.ROOT / result['evidencePath'] / 'workflow'
    shutil.copytree(path, target, dirs_exist_ok=True)
    for p in path.rglob('*'):
        if p.is_file() and p.relative_to(path) not in before:
            p.unlink()
    for name, data in before.items():
        (path / name).write_bytes(data)
    v.finish(v.ROOT / result['evidencePath'], result)
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('lane', choices=('install', 'editor', 'recovery', 'validation', 'native', 'lifecycle', 'timing', 'guide', 'views'))
    lane = parser.parse_args().lane
    if lane == 'install':
        if (PROJECT / 'Temp/UnityLockfile').exists():
            raise RuntimeError('Editor must exit before harness installation')
        pairs = [('P42bHarness', PROJECT / 'Assets/P42bHarness'), ('P42dLive', PROJECT / 'Assets/Hollowmere/Tests/P42dLive'),
                 ('P42gLive', PROJECT / 'Assets/Hollowmere/Tests/P42gLive'), ('P42iCancellation', PROJECT / 'Assets/Hollowmere/Tests/P42iCancellation/Editor'),
                 ('P42hMedia/Editor', PROJECT / 'Assets/Hollowmere/Tests/P42hMedia/Editor')]
        for source, target in pairs:
            if target.exists():
                raise RuntimeError('Refusing to overwrite existing fixture: ' + str(target))
            shutil.copytree(TOOLS / source, target)
        subprocess.run([sys.executable, str(TOOLS / 'P42hTasks/launch.py'), 'install'], check=True)
        return
    if lane == 'editor':
        historical_output(v.OUT / 'W-UI-01/r7-a/open-play', lambda: unity('W-UI-01', 'p42k-open-play', 'Hollowmere.R7_A.OpenPlay.Run', graphical=True, env={'GAMECORE_ETOS_AUTOSTART': '1'}))
        unity('W-UI-02', 'p42k-fence', 'Hollowmere.P3_2.Workflows.ScenariosR7Picking.RunFence', graphical=True)
        unity('W-UI-03', 'p42k-lantern', 'Hollowmere.P3_2.Workflows.ScenariosR7Picking.RunLantern', graphical=True)
        unity('GRAPHICAL', 'p42k-graphics', test_filter='R2_29_KeyDownUpOnPromptAndControlsNeverEnterViewportHandlers|D12_DeferredRelayoutSurvivesWindowManagerPlacementAndRunsOnce|Render_TargetMatchesTheViewportArea|PreviewScreenCapturesARenderTextureOrSkipsHeadless', graphical=True, env={'UNITY': GRAPHICAL})
    elif lane == 'views':
        subprocess.run(['bash', 'studio/tools/verify-all.sh', 'final-views-tests'], check=False)
        subprocess.run(['bash', 'studio/tools/verify-all.sh', 'views'], check=False)
    elif lane == 'timing':
        for n in (1, 2):
            unity('W-EDIT-08', 'p42k-timing-' + str(n), test_filter='P42b.Acceptance.TimingTests', graphical=True)
    elif lane == 'recovery':
        subprocess.run(['bash', 'studio/tools/verify-all.sh', 'recovery'], check=False)
    elif lane == 'validation':
        unity('W-KERNEL-01', 'p42k-kernel-save', test_filter='GameCore.App.Tests.GameApplicationRootTests.Boot_WithACorruptedCatalog_FailsWithANamedCode_AndCreatesNoWorld|GameCore.Persistence.Tests.SaveRestoreTests.AV1SaveIsMigratedToV2OnRestore|GameCore.Persistence.Tests.SaveRestoreTests.AMissingMigrationRefusesAndLeavesTheRunningWorldUntouched', project=v.ROOT / 'unity/GameCore.Validation')
    elif lane == 'native':
        for n in (1, 2):
            unity('W-PLUG-01', 'p42k-native-' + str(n), test_filter='NativeMediaAcceptance', graphical=True, env={'GAMECORE_R7_MEDIA_OUTPUT': '{out}/native'})
            subprocess.run([sys.executable, str(TOOLS / 'retain-large-p42k.py')], check=True)
        unity('W-PLUG-02', 'p42k-animation', test_filter='AnimatorRespawnTests', graphical=True)
        unity('W-PLUG-03', 'p42k-player-interactions', test_filter='LedgeJumpLanding|WalksInteractsTalksPastNpcsAndTravels|R7C_WPLUG08_RepeatedBarnLanternPickupAndReload_LeavesExactlyOneLantern|R7C_WPLUG11_ActualMarenLinePlaysItsNativeVoiceClip', mode='PlayMode', graphical=True)
        unity('W-PLUG-10', 'p42k-validator-parity', test_filter='WPlug10_SameInvalidDefinition_HasCodeMessageAndLocationParityAcrossAllFronts', graphical=True)
    elif lane == 'lifecycle':
        unity('W-EDIT-04', 'p42k-edit-lifecycle', 'Hollowmere.P3_2.Workflows.ScenariosR7Lifecycle.EditLifecycle', args=['-r7Evidence', '{out}/workflow'], graphical=True)
        first = unity('W-PERSIST-02', 'p42k-prefab-prepare', 'Hollowmere.P3_2.Workflows.ScenariosR7Lifecycle.PersistPrepare', args=['-r7Evidence', '{out}/workflow', '-saveDir', '{out}/workflow/saves'], graphical=True)
        shared = v.ROOT / first['evidencePath'] / 'workflow'
        unity('W-PERSIST-02', 'p42k-prefab-reopen', 'Hollowmere.P3_2.Workflows.ScenariosR7Lifecycle.PersistReopen', args=['-r7Evidence', shared, '-saveDir', shared / 'saves'], graphical=True)
        historical_output(v.OUT / 'W-EDIT-06/r7-b', lambda: unity('W-EDIT-06', 'p42k-runtime-promotion', test_filter='Hollowmere.R7_B.Promotion|GameCore.Studio.Edit.Tests.RuntimeMovePromotionTests', graphical=True))
    elif lane == 'guide':
        unity('W-DOC-01', 'p42k-creator-guide', test_filter='R2_38_Guide_NpcAndDialogueContextToolsUndo', graphical=True, env={'GAMECORE_ETOS_AUTOSTART': '1', 'UNITY': GRAPHICAL})
    v.summary()


if __name__ == '__main__':
    main()
