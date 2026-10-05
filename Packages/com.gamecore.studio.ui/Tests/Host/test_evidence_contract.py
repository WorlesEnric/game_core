"""R2-C evidence report checks only; never starts an Editor or calls ETOS."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[4]
SCRIPT = ROOT / "studio/tools/evidence-p2.1.sh"


class EvidenceContractTests(unittest.TestCase):
    def report(self, measurement):
        # Exercise the report's actual acceptance gate with synthetic capture metadata.
        script = Path(os.environ.get("R2_C_EVIDENCE_SCRIPT", SCRIPT)).read_text()
        report = script.rsplit("<<'PY'\n", 1)[1].rsplit("\nPY", 1)[0]
        with tempfile.TemporaryDirectory(prefix="r2-c-report-") as folder:
            dest = Path(folder)
            for index in range(8):
                (dest / f"{index}.png").write_bytes(b"fixture")
            rows = [] if measurement is None else [dict(step=16, name="walk-measurement", **measurement)]
            (dest / "evidence-log.jsonl").write_text("\n".join(map(json.dumps, rows)))
            result = subprocess.run(
                [sys.executable, "-c", report, folder, "test-revision", "myubuntu", folder, "0"],
                capture_output=True, text=True, check=False,
            )
            return result.returncode, (dest / "README.md").read_text()

    def test_R2_29_WalkingRequiresHudRoutingAndCommittedPose(self):
        valid = dict(hud=True, routing=True, source="world.posX/posZ", metres=1.5, seconds=2.5)
        for change in (None, {"hud": False}, {"routing": False}, {"source": "CharacterController"}, {"metres": 0}, {"seconds": 0}):
            with self.subTest(change=change):
                data = None if change is None else dict(valid, **change)
                code, report = self.report(data)
                self.assertEqual(code, 1)
                self.assertIn("Missing or failed HUD walk measurement", report)
        code, report = self.report(valid)
        self.assertEqual(code, 0)
        self.assertIn("1.500 m in 2.500 s", report)


if __name__ == "__main__":
    unittest.main()
