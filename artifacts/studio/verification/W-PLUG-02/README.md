# W-PLUG-02: Despawn/respawn keeps override; Animator bound

Verdict: **PASS**. Graphics-enabled current Animator transitions/poses pass; despawn destroys the old Animator and respawn retains variant and 1.2 scale overrides.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42i.py native
```

## Current-run evidence

- [W-PLUG-02/p42i-animation-20261007T050400.252886Z/results.xml](../W-PLUG-02/p42i-animation-20261007T050400.252886Z/results.xml)
- [W-PLUG-02/p42i-animation-20261007T050400.252886Z/result.json](../W-PLUG-02/p42i-animation-20261007T050400.252886Z/result.json)
