# P4.2d installed-main live rerun

Product source is detached `40fb91fa` under `.evidence/live`, within this packet's clone.
Build/check `studio/agent` here, then `install-p42d.sh`: immutable install, documented
companion-only restart, checksum recheck, and registrations from the exact live clone.
Do not run the legacy companion build helper: it moves a sibling checkout.

Use `studio/tools/verify-all.sh p42d LANE`: `baseline` once, `hello`, `selection`,
`text2`, `narrative`, `reopen`, `voice2`, `guide`, `stage-submit`, `stage-review`.
The latter two accept the P4.2c `--candidate`/`--input`/`--negative` arguments.
`watch-stage-p42c.py REQUEST` only collects evidence; it never authorizes admission.
Copy the trusted `P42cLive`, `P42dLive`, and `P42bHarness` assemblies into the
scratch project's Assets before launch. Shipping R5-C driver sources are unchanged.

Run `voice-self-p42d.sh` before `voice2`. It configures/restores its own PipeWire source
and passes `GAMECORE_R5B_VOICE_SELF_TEST=1` through the shared runner. The self-test
uses the retained P4.2c WAVs; the later driver generates its two current TTS fixtures.
Client frame/byte/RMS and companion frame/provider-event traces correlate by session.

Caps are 4 images / 4 TTS / 2 describe / USD 5 ledger total, no paid 3D. Reservations
persist before dispatch; a used lane refuses replay. No describe request is allowed
without a priced installed tariff. Prior packet charges form an immutable baseline.
An unpriced describe capability in hello is not a priced contract.

Unity always runs through `unity-batch.sh`, with at most one packet-owned Editor.
The trusted graphics adapter forwards only the driver's bounded environment settings;
the stage submit adapter holds the host allocator mutex until the creator Editor exits.
The standard runner handles startup retry, redaction and result XML. Test pass/fail
comes from XML; workflow acceptance additionally requires its specific observations.

The pressure-plate fixed fixture ID belongs to the previous project. Use
`instantiate-p42d.py SOURCE NEW_DEST` to mint only a fresh envelope identity;
`instantiation.json` proves package/proposal/operation bytes are unchanged.
Before stage, `provision-p42d.sh` derives the exact owner/version cache and invokes
the trusted pinned-cache provisioner. It does not reset cold markers or deadlines.

`history-prepare` then `history-reopen` runs the retained two-operation R5-A witness
in two real Editor processes; `GAMECORE_R5_REOPEN=1` requires distinct PIDs and a
Play/Edit domain reload. It supplements the live candidate run, which may contain
only one operation per target.
