"""Copy STAGE-INT's acceptance harness to a disposable crate; change only paths.

Run its generated command after provisioning ~/.cache/gamecore-studio/adapt-split/stage.
The real companion binary and production stage sources remain those of this checkout.
"""
from pathlib import Path
import json
import shutil
import tempfile

repo = Path(__file__).resolve().parents[4]
source = repo / 'studio/agent'
scratch = Path(tempfile.mkdtemp(prefix='adapt-split-harness-'))
manifest = (source / 'Cargo.toml').read_text()
dependencies = manifest[manifest.index('[dependencies]'):]
dependencies = dependencies.replace('path = "vendor/etos-sdk"',
                                    'path = ' + json.dumps(str(source / 'vendor/etos-sdk')))
manifest = ('[package]\nname = "adapt-split-acceptance"\nversion = "0.1.0"\n'
            'edition = "2021"\n[workspace]\n[[test]]\nname = "stage_int"\n'
            'path = "stage_int.rs"\n' + dependencies)
manifest = manifest.replace('[dependencies]', '[dependencies]\ngamecore-studio = { path = '
                            + json.dumps(str(source)) + ' }', 1)
(scratch / 'Cargo.toml').write_text(manifest)
shutil.copytree(source / 'tests/support', scratch / 'support')
text = (source / 'tests/stage_int.rs').read_text()
text = text.replace('let repo = Path::new(env!("CARGO_MANIFEST_DIR"))\n'
                    '        .ancestors()\n        .nth(2)\n        .unwrap();',
                    'let repo = Path::new(' + json.dumps(str(repo)) + ');')
text = text.replace('.cache/gamecore-studio/stage-int', '.cache/gamecore-studio/adapt-split')
text = text.replace('studio/agent/evidence/stage-int', 'studio/agent/evidence/adapt-split')
text = text.replace('env!("CARGO_BIN_EXE_gamecore-studio")',
                    json.dumps(str(source / 'target/debug/gamecore-studio')))
(scratch / 'stage_int.rs').write_text(text)
print(scratch / 'Cargo.toml')
