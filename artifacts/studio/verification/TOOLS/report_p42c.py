#!/usr/bin/env python3
"""Publish exact final-tree dispositions while retaining inherited revision-specific rows."""
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import report_rows
import verify as v

BASE='f787829289ea7402c08917a78553ff6c3838bda8'
STATE=v.ROOT/'artifacts/studio/workflows/P4.2c'

def path(pattern):
    values=sorted(v.OUT.glob(pattern))
    if not values:raise RuntimeError('missing evidence '+pattern)
    return values[-1]

def main():
    guide=path('W-DOC-01/p42c-guides-*/results.xml')
    guide_counts=v.xml_counts(guide)
    guide_cases={case.get('name'):case.get('result') for case in ET.parse(guide).getroot().iter('test-case')}
    if guide_counts['total']!=3:raise RuntimeError('guide suite did not run all three named cases')
    restart=json.loads(path('W-ETOS-06/p42c-companion-restart-*/result.json').read_text())
    ledger=json.loads((STATE/'ledger-final.json').read_text())
    paid=json.loads((STATE/'paid-ledger.json').read_text())
    counts={op:paid['operationCounts'][op] for op in ('image','tts','describe')}
    recovered=path('W-DOC-01/p42c-image-recovered-*/result.json')
    recovery=json.loads(recovered.read_text())
    assert counts['image']<=6 and counts['tts']<=6 and counts['describe']<=2 and paid['accountedUsd']<=10
    decisions=json.loads((v.OUT/'ROW-DECISIONS.json').read_text())
    def row(rid,status,note,patterns,command,tests=None):
        decisions[rid]=dict(status=status,baseline=BASE,note=note,patterns=patterns,command=command,
            historical='Earlier P4.2/P4.2b attempts remain retained; this disposition uses the installed P4.2c release and final-main product source.')
        if tests:decisions[rid]['tests']=tests
    row('W-AI-01','BLOCKED','PARTIAL: R3-F six-digit tint applies/undoes with unchanged behaviour hashes; R3-A Sprite bind and R4-A image/TTS imports pass. Two images are generated, but the unchanged P3.2 robe driver never assigns its generated robe texture through R3-D entity.setMaterialTexture. The full texture-to-material chain is unexercised.',
        ['W-AI-01/p42c-robe2-*/README.md','W-AI-01/p42c-robe2-*/workflow/robe/apply-report.json','W-AI-01/p42c-robe2-*/workflow/icon/assign.json'], 'studio/tools/verify-all.sh p42c robe2')
    row('W-AI-02','BLOCKED','PARTIAL: the installed R4-C validator accepts the scoped move/patrol and ferryman candidates; both apply. The P3.2 driver has no in-Play NPC/nav assertion and tries to select Odd in Village. Ferryman Undo reports Undone but leaves the created NPC (roster 20→21→21); owner request retained.',
        ['W-AI-02/p42c-text2-*/README.md','W-AI-02/p42c-text2-*/workflow/ferryman2/roster-undone.json','W-AI-02/p42c-text2-*/workflow/ferryman2/undo-result.json'],'studio/tools/verify-all.sh p42c text2')
    row('W-AI-03','BLOCKED','PARTIAL: the live candidate applies; R3-D conditional-dialogue preview hides the new line when unlit and shows it for shrine_lit=1. The existing fact is reused, so R3-F same-candidate fact creation is not exercised. This driver does not run dialogue in Play.',
        ['W-AI-03/p42c-narrative-*/README.md','W-AI-03/p42c-narrative-*/workflow/odd-line/dialogue-preview-applied-unlit.json','W-AI-03/p42c-narrative-*/workflow/odd-line/dialogue-preview-applied-shrine-lit.json'],'studio/tools/verify-all.sh p42c narrative')
    row('W-AI-04','PASS','The installed-companion candidate applies ui.bind: objective-line.text → vm:hud.QuestStageTitle (R3-D/D10b). The resulting authored binding is retained; separate-Editor reopen preserves it and its undo/redo/final undo succeeds.',
        ['W-AI-03/p42c-narrative-*/workflow/hud/candidate.json','W-AI-03/p42c-narrative-*/workflow/hud/describe-applied.json','W-AI-06/p42c-reopen-*/workflow/hud/redo-result.json'],'studio/tools/verify-all.sh p42c narrative\nstudio/tools/verify-all.sh p42c reopen')
    row('W-AI-05','BLOCKED','PARTIAL: the live scoped quest.addObjective candidate applies a Collect requirement of 2 against the real OilFlask definition, exercising R3-F/R4-C validation. The legacy simulator uses oil_flask and refuses GP-QST-004; no in-Play consequence is established.',
        ['W-AI-03/p42c-narrative-*/workflow/quest/candidate.json','W-AI-03/p42c-narrative-*/workflow/quest/apply-report.json','W-AI-03/p42c-narrative-*/workflow/quest/quest-simulate-applied.json'],'studio/tools/verify-all.sh p42c narrative')
    row('W-AI-06','FAIL','Saved asset hashes and all three Applied journals survive a real Editor close/reopen. HUD and quest undo/redo/final undo pass; Odd undo refuses Conflict against the first operation’s intermediate after-stamp although the second operation’s final stamp matches. Final backToBefore=false; no forced undo or candidate edit.',
        ['W-AI-06/p42c-reopen-*/README.md','W-AI-06/p42c-reopen-*/workflow/reopen/after-reopen.json','W-AI-06/p42c-reopen-*/workflow/odd-line/undo-result.json','W-AI-06/p42c-reopen-*/workflow/reopen/final.json'],'studio/tools/verify-all.sh p42c narrative\nstudio/tools/verify-all.sh p42c reopen')
    row('W-VOICE-01','FAIL','R4-A microphone path delivers the move final and its explicit Send/apply/undo. Destructive speech yields no transcript in the reused session and a fresh ready-gated session (560 chunks, peak 0.15248). Request/tray/journal counts stay unchanged. Dedicated XML: 0 passed, 1 failed; listening/final status labels are not partial speech revisions.',
        ['W-VOICE-01/p42c-voice2-*/workflow/voice/destructive-check.json','W-VOICE-01/p42c-fresh-destructive-*/results.xml','W-VOICE-01/p42c-fresh-destructive-*/voice-result.json'],'studio/tools/verify-all.sh p42c voice2\nstudio/tools/verify-all.sh p42c voice-proof',['Hollowmere.P4_2.VoiceAcceptanceTests.R2_38_W_VOICE_01_DestructiveSpeechNeverSubmits'])
    three=guide_cases.get('P42_OPS_01_Installed3dRefusesUnconfigured')=='Passed'
    row('W-AI-07','PASS' if three else 'FAIL','Installed R4-C provider-before-budget 3D refusal: '+('not_configured surfaced, no generation.' if three else 'see named guide-suite XML failure.'),
        ['W-DOC-01/p42c-guides-*/results.xml','W-DOC-01/p42c-guides-*/3d-refusal.json'],'studio/tools/verify-all.sh p42c guides',['P42c.Live.GuideTests.P42_OPS_01_Installed3dRefusesUnconfigured'])
    row('W-MECH-01','FAIL','Installed R4-A/R3-C app-origin stage verifies through the authenticated service and enables Admit; the visible badge still says not staged. Docker cold 142.260 s / warm 62.905 s; each XML has 33 EditMode + 2 PlayMode passes. Actual Play Admit refuses catalog_missing because Entry.Verify opens Editor scenes during Play. No capture/restore/smoke/undo follows. Negative fixture is refused with Admit disabled (14 lexical hits).',
        ['W-MECH-01/p42c-signed-receipts-*/result.json','W-MECH-01/p42c-stage-submit-20261006T075911.180322Z/service/job.json','W-MECH-01/p42c-stage-submit-20261006T080546.723368Z/service/job.json','W-MECH-01/p42c-stage-review-20261006T081247.078280Z/play-verification.json','W-MECH-01/p42c-stage-review-20261006T081247.078280Z/admit.json','W-MECH-01/p42c-play-catalog-diagnostic-*/catalog-preflight.json','W-MECH-01/p42c-stage-review-negative-*/panel-verdict.json'],
        'See artifacts/studio/verification/TOOLS/README-P4.2c.md: stage-submit, watch-stage-p42c.py, stage-review.')
    row('W-ETOS-06','BLOCKED' if restart['status']=='PASS' else 'FAIL','Companion portion '+restart['status']+': actual etos agent restart, same task, one cancellation outcome and exact four-event cursor replay; reconnect 143.1 ms, cancel ack 188.0 ms. Node-death portion remains BLOCKED: etosd stop/restart is forbidden, and a companion restart cannot prove delayed attribution after node death.',
        ['W-ETOS-06/p42c-companion-restart-*/result.json','W-ETOS-06/p42c-companion-restart-*/live/dotnet-companion-restart.json','W-ETOS-06/p42c-companion-restart-*/live/dotnet-g-h-events-resume-cancel.json'],
        'studio/tools/verify-all.sh p42c restart',['GameCore.Studio.Etos.Client.Tests.LiveTests.R2_38_W_ETOS_06_CompanionRestartResumesCursorAndKeepsTask'])
    row('W-GAME-07','BLOCKED','The owner forbids etosd stop/restart, so the requested stopped-node/no-network player scenario is not run. The companion-only supervisor restart does not establish this condition; the prior namespace prerequisite refusal remains historical evidence.',
        decisions['W-GAME-07']['patterns'],decisions['W-GAME-07']['command'])
    guide_note='Text Send passed. Image Send was accepted and produced a PNG; after the initial 180-second harness timeout, the same request was recovered without resubmission. Unity refuses its candidate: MediaImporterInvalid for textureType "Sprite (2D and UI)". The two deferred Send actions are recorded, but image import/assignment and the full novice NPC/dialogue walkthrough do not pass.'
    row('W-DOC-01','FAIL',guide_note,['W-DOC-01/p42c-guides-*/results.xml','W-DOC-01/p42c-guides-*/text-sent.json','W-DOC-01/p42c-guides-*/text-outcome.json','W-DOC-01/p42c-guides-*/image-sent.json','W-DOC-01/p42c-image-recovered-*/image-outcome.json','W-DOC-01/p42c-image-recovered-*/results.xml'],'studio/tools/verify-all.sh p42c guides\nstudio/tools/verify-all.sh p42c guide-recover',['P42c.Live.GuideTests.R2_38_CreatorGuide_TextSend','P42c.Live.GuideTests.R2_38_CreatorGuide_ImageSend','P42c.Live.GuideTests.R2_38_CreatorGuide_ResumeImageSendWithoutAnotherRequest'])
    row('W-ETOS-07','BLOCKED','Installed R4 operator-priced image and published-priced TTS calls return verified imports; Sprite binding passes and 3D refuses honestly. The full media row remains partial: describe has no binding tariff in authenticated hello and is not called. No provider invoice is inferred from local ceiling charges.',
        ['INSTALL-P4.2c/hello-*/live/dotnet-a-hello.json','W-AI-01/p42c-robe2-*/workflow/icon/assign.json','W-DOC-01/p42c-guides-*/3d-refusal.json'],'studio/tools/verify-all.sh p42c robe2\nstudio/tools/verify-all.sh p42c guides')
    decisions['W-E2E-01'].update(status='BLOCKED',baseline=BASE,note='P4.2c ran the installed final-tree companion and disposed the requested rows, but live admission, history and destructive voice have retained failures; several workflow assertions and node-death scenarios remain unqualified. Inherited rows keep their original revision-specific evidence; this is not all-row product acceptance.')
    (v.OUT/'ROW-DECISIONS.json').write_text(json.dumps(decisions,indent=2)+'\n')
    report_rows.write_rows()
    workflows=[]
    for rid in ['W-AI-01','W-AI-02','W-AI-03','W-AI-04','W-AI-05','W-AI-06']:
        decision=decisions[rid]
        workflows.append({'row':rid,'outcome':{'PASS':'pass','FAIL':'fail','BLOCKED':'partial'}[decision['status']],'detail':decision['note'],'evidence':decision['patterns']})
    (STATE/'outcomes.json').write_text(json.dumps({'productRevision':BASE,'installedRelease':'0.1.0-426b3ead95f21815','workflows':workflows,'observedMediaCounts':counts,'companionLedgerUsd':ledger['costUsd'],'accountedUsd':paid['accountedUsd'],'guide':guide_note},indent=2)+'\n')
    text=['# P4.2c installed final-tree workflows','',f'Product `{BASE}`; immutable release `0.1.0-426b3ead95f21815`.','', '| Workflow | Outcome | Observed result and exercised fixes |','|---|---|---|']
    for w in workflows:text.append(f"| {w['row']} | {w['outcome']} | {w['detail']} |")
    text += ['',guide_note,'',f"Observed media: **{counts['image']} images / {counts['tts']} TTS / {counts['describe']} describe / no paid 3D**. Packet accounting: **USD {paid['accountedUsd']:.10f}** = companion ledger USD {ledger['costUsd']:.10f} + owner estimate USD 0.20 for the worker image. These reported counts/accounting stay below 6/6/2 and USD 10.", '',
      'Image charges use tariff.kind=operator (USD 0.20 each); TTS uses tariff.kind=published. Each id, quantity, charge and provenance is retained in [paid-ledger.json](paid-ledger.json); [ledger-final.json](ledger-final.json) contains only actual companion charge records. The worker image has no companion charge and its model is not reported; its USD 0.20 is explicitly a separate operator estimate. These are local ceiling charges/estimates, not provider invoices. Unreported worker provider attempts cannot be independently reconstructed. Worker task micro_usd and any worker-generated media are separately recorded in [task-ledger.json](task-ledger.json); voice sessions have no USD column ([voice-ledger.json](voice-ledger.json)).', '',
      'The [packet note](../../../../docs/studio/packets/P4.2c-live-rows.md) records the deployment correction, owner requests, exact unexercised assertions, and the forbidden node-death portion. [Reproducer](../../verification/TOOLS/README-P4.2c.md).']
    (STATE/'README.md').write_text('\n'.join(text)+'\n')

if __name__=='__main__':main()
