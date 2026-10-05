# Saltmarsh: Relight the Coast

A small independent GameCore game, with procedural placeholder art and audio.
Ada asks a courier to restore the island beacon. In the dunes, Neri offers a choice:
recover sea glass with Sol, preserving the reserve lens, or use the spare before nightfall.
Copper wire repairs the beacon; the harbour becomes safe and the ending remembers the choice.

The game uses the twelve gameplay groups through the real `GameBoot`, plus the Studio
change-set engine for authoring. It does not reference Hollowmere assemblies or assets.

## Reproduce on myubuntu

From the repository root:

```bash
studio/tools/new-project.sh games/cleanproof Saltmarsh
bash studio/tools/unity-batch.sh --project "$PWD/games/cleanproof" --log-dir "$PWD/artifacts/studio/cleanproof/setup" --label setup -- -quit -executeMethod GameCoreProjectSetup.Configure
bash studio/tools/unity-batch.sh --project "$PWD/games/cleanproof" --log-dir "$PWD/artifacts/studio/cleanproof/author" --label author -- -quit -executeMethod Saltmarsh.Authoring.SaltmarshAuthoring.AuthorAll
bash studio/tools/unity-batch.sh --project "$PWD/games/cleanproof" --log-dir "$PWD/artifacts/studio/cleanproof/editmode" --label editmode --results "$PWD/artifacts/studio/cleanproof/editmode/results.xml" -- -runTests -testPlatform EditMode -testFilter 'Saltmarsh.Tests'
bash studio/tools/unity-batch.sh --project "$PWD/games/cleanproof" --log-dir "$PWD/artifacts/studio/cleanproof/playmode" --label playmode --results "$PWD/artifacts/studio/cleanproof/playmode/results.xml" -- -runTests -testPlatform PlayMode -testFilter 'Saltmarsh.Tests'
bash games/cleanproof/Tools/build.sh
bash games/cleanproof/Tools/run-headless.sh
python3 games/cleanproof/Tools/verify_evidence.py
```

`new-project.sh` is idempotent and preserves every existing file. Its content-free template
contains host-compatible project defaults, a camera/light Boot scene, URP settings and a setup Editor assembly. It never copies
Hollowmere content, a Library, or a lock. Unity creates the lock when resolving the pins.

`AuthorAll` uses the registered `saltmarsh.author` operation through `ChangeSetEngine.Apply`.
Its world, narrative, presentation, bake and boot phases each leave a Studio/History entry,
including created paths. The baked catalog is trusted game code produced by the compiler;
this game does not import candidate code or exercise mechanism admission.

WASD moves, mouse looks, E interacts, Shift runs, Space jumps, J opens the journal,
I opens inventory and Escape pauses. New Game starts from the package menu; Save and Load
are on the pause menu. The headless hook drives the same game through typed commands,
checks save/restore and quest completion, and exits after at least 600 frames.
The run wrapper selects `DISPLAY=:1 XDG_SESSION_TYPE=x11` while retaining `-batchmode -nographics`;
this avoids the pinned Unity player's native null-backend crash in a tty shell. Override the
host display with `SALTMARSH_DISPLAY` if needed. Audio remains enabled in the product settings.

The verification status, exact source revision, logs and reusability gaps are recorded in
`artifacts/studio/cleanproof/README.md` and `docs/studio/packets/P4.1-clean-proof.md`.
