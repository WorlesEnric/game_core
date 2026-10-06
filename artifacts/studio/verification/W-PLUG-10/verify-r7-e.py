#!/usr/bin/env python3
"""Verify executed R7-E XML/TRX and retain canonical three-front observations."""
import hashlib
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[4]
OUT = Path(__file__).resolve().parent
xml = OUT / "r7-e-final/results.xml"
trx = OUT / "r7-e-rules/r7-e-rules.trx"
root = ET.parse(xml).getroot()
cases = list(root.iter("test-case"))
assert cases and all(case.get("result") == "Passed" for case in cases), "All selected Unity cases must pass"
parity_name = "Hollowmere.R7_C.Validation.DefinitionDiagnosticParityTests.WPlug10_SameInvalidDefinition_HasCodeMessageAndLocationParityAcrossAllFronts"
parity = next(case for case in cases if case.get("fullname") == parity_name)
observations = {}
output = parity.findtext("output") or ""
decoder = json.JSONDecoder()
for front in ("inspector", "validator"):
    payload = output.split(front + " diagnostics=", 1)[1].lstrip()
    observations[front] = decoder.raw_decode(payload)[0]
agent_output = output.split("agent request=", 1)[1]
assert " accepted=False diagnostics=" in agent_output
observations["agent"] = decoder.raw_decode(agent_output.split(" diagnostics=", 1)[1].lstrip())[0]
assert set(observations) == {"inspector", "validator", "agent"}, observations
expected = observations["validator"]
assert len(expected) == 1 and expected[0]["code"] == "GP-ENT-006", expected
canonical = lambda diagnostic: (diagnostic["code"], diagnostic["message"], diagnostic["where"]["authoringId"], diagnostic["data"]["subject"])
for front, diagnostics in observations.items():
    assert len(diagnostics) == 1, (front, diagnostics)
    assert canonical(diagnostics[0]) == canonical(expected[0]), front
    assert diagnostics == expected, "Full canonical diagnostic differs: " + front
assert sum("R7E_" in case.get("name", "") for case in cases) == 7
assert sum("AttachGenerated_" in case.get("name", "") for case in cases) == 2
rules = ET.parse(trx).getroot().find(".//{*}Counters")
assert rules is not None and rules.get("failed") == "0" and rules.get("notExecuted") == "0"
assert rules.get("passed") == rules.get("total") == "309"
source_files = [
    "Packages/com.gamecore.studio.core/Editor/Engine/ChangeSetEngine.cs",
    "Packages/com.gamecore.studio.core/Tests/Editor/ChangeSetEngineTests.cs",
    "Packages/com.gamecore.studio.ui/Editor/Context/ContextPanelView.cs",
    "games/hollowmere/Assets/Hollowmere/Authoring/Editor/HollowmereMedia.cs",
    "games/hollowmere/Assets/Hollowmere/Tests/R7_C/Validation/DefinitionDiagnosticParityTests.cs",
    "games/hollowmere/Assets/Hollowmere/Tests/R7_E/GeneratedVoiceEnrollmentTests.cs",
]
record = {
    "row": "W-PLUG-10", "status": "PASS",
    "revision": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
    "unity": {"passed": len(cases), "failed": 0, "skipped": 0},
    "rules": {"passed": 309, "failed": 0, "skipped": 0},
    "parityCase": parity_name, "observations": observations,
    "sourceSha256": {path: hashlib.sha256((ROOT / path).read_bytes()).hexdigest() for path in source_files},
    "receiptSha256": {str(path.relative_to(OUT)): hashlib.sha256(path.read_bytes()).hexdigest() for path in (xml, trx)},
}
(OUT / "r7-e-result.json").write_text(json.dumps(record, indent=2) + "\n")
print(json.dumps({"status": record["status"], "unity": record["unity"], "rules": record["rules"]}))
