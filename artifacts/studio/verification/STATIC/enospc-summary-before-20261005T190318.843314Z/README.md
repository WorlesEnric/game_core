# enospc-summary-before

Verdict: **FAIL**.

Source revision: `12d7e5c7ababe95a1e251343584755e6c38fd454`; host: `worlesenric`.
Started: 2026-10-05T19:03:18.844639+00:00; ended: 2026-10-05T19:03:18.926899+00:00; duration: 0.084 s.

Command (from repository root unless cwd specified):

```sh
python3 -c 'import subprocess,tempfile,types,pathlib; m=types.ModuleType('"'"'before'"'"'); m.__file__='"'"'artifacts/studio/verification/TOOLS/verify.py'"'"'; exec(subprocess.check_output(['"'"'git'"'"','"'"'show'"'"','"'"'cf5b18ff:artifacts/studio/verification/TOOLS/verify.py'"'"']),m.__dict__); t=tempfile.TemporaryDirectory(); m.OUT=pathlib.Path(t.name); p=m.OUT/'"'"'ROW'"'"'/'"'"'retained'"'"'; p.mkdir(parents=True); (p/'"'"'result.json'"'"').write_text('"'"''"'"'); m.summary()'
```

Text evidence redacts credentials and substitutes `~` for absolute home paths. XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.
