"""ADAPT-SPLIT / R2-11: gameplay closure must resolve without Studio."""
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[3]


class AdapterBoundaryTests(unittest.TestCase):
    def test_R2_11_ADAPT_SPLIT_gameplay_closure_excludes_studio(self):
        pending = list((ROOT / "Packages").glob("com.gamecore.gameplay.*/package.json"))
        seen = set()
        while pending:
            path = pending.pop()
            if path in seen:
                continue
            seen.add(path)
            manifest = json.loads(path.read_text())
            for dependency in manifest.get("dependencies", {}):
                self.assertFalse(dependency.startswith("com.gamecore.studio."),
                                 f"{manifest['name']} requires excluded {dependency}")
                local = ROOT / "Packages" / dependency / "package.json"
                if local.exists():
                    pending.append(local)

    def test_R2_06_ADAPT_SPLIT_world_editor_has_no_studio_reference(self):
        directory = ROOT / "Packages/com.gamecore.gameplay.world/Editor"
        for path in directory.glob("*.asmdef"):
            assembly = json.loads(path.read_text())
            self.assertFalse(any(ref.startswith("GameCore.Studio.")
                                 for ref in assembly["references"]), path)
            self.assertNotIn("Newtonsoft.Json.dll", assembly["precompiledReferences"])
        for path in directory.glob("*.cs"):
            self.assertNotIn("using GameCore.Studio.", path.read_text(), path)


if __name__ == "__main__":
    unittest.main()
