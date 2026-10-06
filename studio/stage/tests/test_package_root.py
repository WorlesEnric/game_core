"""R5-C / P4.2c request 4: one registered checkout supplies pins and mounts."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

STAGE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(STAGE))
spec = importlib.util.spec_from_file_location("make_slot", STAGE / "make-slot.py")
slot = importlib.util.module_from_spec(spec)
spec.loader.exec_module(slot)
FIXTURE = json.loads((Path(__file__).parent / "package-root-cases.json").read_text())


class PackageRootTests(unittest.TestCase):
    def test_R5_04_shared_root_cases(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            project = root / FIXTURE["project"]
            (project / "Packages").mkdir(parents=True)
            (project / "ProjectSettings").mkdir()
            subprocess.run(["git", "init", "-q", str(root / "registered")], check=True)
            for prefix in ("registered/Packages", "tools/Packages", "candidate"):
                package = root / prefix / FIXTURE["package"]
                package.mkdir(parents=True)
                (package / "package.json").write_text(json.dumps({"name": FIXTURE["package"]}))
            (root / "registered/Packages/linked").symlink_to(root / "candidate" / FIXTURE["package"])
            for case in FIXTURE["cases"]:
                with self.subTest(case=case["name"]):
                    (project / "Packages/manifest.json").write_text(json.dumps({"dependencies": {
                        FIXTURE["package"]: case["pin"].replace("{root}", str(root))}}))
                    if case["ok"]:
                        manifest, _ = slot.build_manifest(project, "candidate", {})
                        self.assertEqual(manifest["dependencies"][FIXTURE["package"]],
                                         "file:" + str(root / "registered/Packages" / FIXTURE["package"]))
                    else:
                        with self.assertRaisesRegex(slot.SlotError, "stage_package_root_mismatch.*stage.projects"):
                            slot.build_manifest(project, "candidate", {})

    def test_R5_04_unregistered_checkout_has_no_root(self):
        with tempfile.TemporaryDirectory() as temporary:
            project = Path(temporary)
            (project / "Packages").mkdir()
            (project / "Packages/manifest.json").write_text('{"dependencies":{}}')
            with self.assertRaisesRegex(slot.SlotError, "stage_package_root_mismatch"):
                slot.build_manifest(project, "candidate", {})

    def test_R5_04_mount_mismatch_precedes_candidate_access(self):
        import argparse
        repo = STAGE.parents[1]
        with tempfile.TemporaryDirectory() as temporary:
            args = argparse.Namespace(slot="mismatch", source_project=repo / "games/hollowmere",
                                      slot_root=Path(temporary) / "slots", candidate=Path(temporary) / "absent",
                                      package_root=Path(temporary) / "arbitrary", force=False)
            with self.assertRaisesRegex(slot.SlotError, "stage_package_root_mismatch.*sandbox mount"):
                slot.make_slot(args)
            self.assertFalse((Path(temporary) / "slots").exists())
