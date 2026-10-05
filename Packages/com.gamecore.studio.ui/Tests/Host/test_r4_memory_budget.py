"""P42-MEMORY-01: keep the legacy game assertion aligned with 07's ceiling."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[4]


class MemoryBudgetTests(unittest.TestCase):
    def test_P42_MEMORY_01_ten_cycle_assertion_enforces_fifteen_percent(self):
        source = (ROOT / 'games/hollowmere/Assets/Hollowmere/Tests/P3_1/EditMode/P31PlayModeHooksTests.cs').read_text()
        match = re.search(r'Assert.That\(lastAllocated, Is.LessThan\((\d+(?:\.\d+)?)\)', source)
        self.assertIsNotNone(match)
        self.assertEqual(float(match[1]), 15.0)
        self.assertIn('less than 15% over ten cycles', source)


if __name__ == '__main__':
    unittest.main()
