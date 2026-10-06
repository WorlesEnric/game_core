# R7-A W-AI-06

**PASS.** Source: `1a462f88` (committed source bytes exercised). No new worker or provider requests. The unchanged retained P4.2e Odd/HUD/quest candidates were replayed through the real engine with normal preconditions; neither candidate stamps nor asset hashes were normalized.

## Contract and fix

Narrative `contentStamp` was already a deterministic content hash, not a timestamp. The Play bake updates it; History restores authored fields, leaving last-bake metadata stale. The contract paragraph in `03-authoring-contracts.md` makes the persistence boundary explicit: a baked baseline, inverse/replay operations, then the **production bake** before saving and comparing all serialized bytes. The Reopen step calls `Entry.Bake`, fails on a bake refusal, saves and retains the existing exact-byte assertion. It never drops `contentStamp` or copies fixture bytes to produce a pass.

## Proof

- [Regression XML](stamps.xml): `R7_A_ReopenBakeRestoresAllBytesAfterRetainedNarrativeUndoRedoUndo` passes. It demonstrates the pre-fix stale-stamp mismatch, executes the actual Reopen bake step, compares every byte, and requires another bake to change nothing. Fixture restoration happens only after the assertions.
- Preparation Editor **4005226** applies all three retained candidates, production-bakes and saves. [Prepared state](reopen-proof/prepare/saved.json), [original bytes](reopen-proof/prepare/before/Assets/Hollowmere), [saved bytes](reopen-proof/prepare/saved/Assets/Hollowmere).
- Separate Editor **4007734** reopens: **hashesMatchSaved=true**, all three journal entries Applied. [Receipt](reopen-proof/workflow/reopen/after-reopen.json).
- Normal History undo → redo → final undo succeeds. Production rebake succeeds; **backToBefore=true** for all four complete files: Odd, Doc_Hud, DrownedBell and Lantern. [Final hashes](reopen-proof/workflow/reopen/final.json), [bake result](reopen-proof/workflow/reopen/restored-bake.json), [verification](reopen-proof/verification.json).
- Actual [final Editor History capture](reopen-proof/workflow/keyframes/011-reopen-final.png) and all intermediate captures are retained. Prepare exits 0 in 43 s; reopen exits 0 in 79 s.

## Reproduce

Use a fresh scratch checkout with the four original fixture assets and no existing retained candidate journal IDs or `Library/P3_2/narrative.json`. Do not reset a creator's history.

```sh
bash artifacts/studio/verification/W-AI-06/r7-a/run.sh /absolute/fresh/evidence-directory
```

The preparation refuses changed baselines, existing journal IDs and overwritten evidence. It uses retained candidates, not another paid narrative generation. The reopen guard rejects execution in the preparation process.
