# allocator-isolated

Verdict: **PASS**.

Source revision: `20e34d1e6a6800e4b0bd4a1e3ebd44611a968c55`; host: `worlesenric`.
Started: 2026-10-05T17:32:02.631585+00:00; ended: 2026-10-05T17:32:09.194842+00:00; duration: 6.584 s.

Command (from repository root unless cwd specified):

```sh
docker run --rm --network none --mount type=bind,src=~/wkspace/gc-studio/p4.2,dst=/repo,readonly --workdir /repo localhost/gc-mechanic:current bash studio/tools/tests/unity-batch-lock.sh
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
