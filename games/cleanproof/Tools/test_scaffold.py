#!/usr/bin/env python3
"""Fresh scaffold and repeat-run contract; no Unity process or shared project needed."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[3]


class ScaffoldTests(unittest.TestCase):
    def test_W_CLEAN_02_fresh_scaffold_is_content_free_and_idempotent(self):
        with tempfile.TemporaryDirectory(prefix="saltmarsh-scaffold-") as temp:
            root = Path(temp)
            script = root / "studio/tools/new-project.sh"
            script.parent.mkdir(parents=True)
            shutil.copyfile(REPO / "studio/tools/new-project.sh", script)
            shutil.copytree(REPO / "games/cleanproof/Template~", root / "games/cleanproof/Template~")
            for package in (REPO / "Packages").glob("com.gamecore.*/package.json"):
                dest = root / "Packages" / package.parent.name / "package.json"
                dest.parent.mkdir(parents=True)
                shutil.copyfile(package, dest)
            subprocess.run(["bash", str(script), "games/probe", "Probe"], check=True)
            project = root / "games/probe"
            def snapshot():
                return {str(p.relative_to(project)): hashlib.sha256(p.read_bytes()).hexdigest()
                        for p in project.rglob("*") if p.is_file()}
            manifest = json.loads((project / "Packages/manifest.json").read_text())
            for name, version in manifest["dependencies"].items():
                if name.startswith("com.gamecore."):
                    self.assertTrue((project / "Packages" / version.removeprefix("file:")).resolve().is_dir())
            self.assertEqual(manifest["dependencies"]["com.unity.inputsystem"], "1.19.0")
            self.assertFalse((project / "Packages/packages-lock.json").exists())
            self.assertFalse((project / "Assets/Hollowmere").exists())
            self.assertFalse((project / "Assets/Saltmarsh").exists())
            sentinel = project / "Assets/Creator.txt"
            sentinel.write_text("creator content")
            before = snapshot()
            subprocess.run(["bash", str(script), "games/probe", "Probe"], check=True)
            self.assertEqual(snapshot(), before)
            refused = subprocess.run(["bash", str(script), "../escape", "Probe"], capture_output=True)
            self.assertNotEqual(refused.returncode, 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)
