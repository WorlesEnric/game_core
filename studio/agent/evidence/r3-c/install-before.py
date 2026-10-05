"""Isolated reproduction of ed76089 install.sh's build-clone symlink, no live install."""
from pathlib import Path
import tempfile
with tempfile.TemporaryDirectory() as tmp:
    release = Path(tmp) / "studio/agent/target/release/gamecore-studio"
    release.parent.mkdir(parents=True)
    release.write_bytes(b"installed-v1")
    link = Path(tmp) / "studio/etos/agent/bin/gamecore-studio"
    link.parent.mkdir(parents=True)
    # Exact target installed by the baseline script, line 204.
    link.symlink_to("../../../agent/target/release/gamecore-studio")
    release.write_bytes(b"later-rebuild-v2")
    assert link.read_bytes() == b"installed-v1", "D15: rebuilding the clone replaced the installed executable"
