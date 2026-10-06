"""All installer tests use temporary roots and a fake etos executable, never the live node."""
import hashlib
import importlib.util
import json
import os
import re
import shutil
from pathlib import Path
import subprocess
import tempfile
import tomllib
import unittest

HERE = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("install_state", HERE / "install-state.py")
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)


class Installer(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.project = self.root / "project space"
        (self.project / "ProjectSettings").mkdir(parents=True)
        (self.project / "ProjectSettings/ProjectSettings.asset").write_text("productGUID: " + "a" * 32 + "\n")
        self.binary = self.root / "binary"
        self.binary.write_bytes(b"release-v1")
        self.etos = self.root / "fake-etos"
        self.etos.write_text('''#!/usr/bin/env python3
import json,sys,tomllib
from pathlib import Path
root = Path(__file__).parent
with (root / "calls").open("a") as f: f.write(" ".join(sys.argv[1:]) + "\\n")
state = root / "node.json"
node = json.loads(state.read_text()) if state.exists() else {"workers": []}
args = sys.argv[1:]
if args == ["--json", "worker", "list"]:
 print(json.dumps(node["workers"]))
elif args == ["--json", "agent", "list"]:
 print(json.dumps([{"agent":"gamecore-studio", "state":"failed" if (root / "fail").exists() else "ready"}]))
elif len(args) == 4 and args[:2] in (["agent", "install"], ["agent", "upgrade"]) and args[2] == "--link":
 package = Path(args[3]).resolve()
 manifest = tomllib.loads((package / "agent.toml").read_text())
 if (root / "reject-package").exists() and (root / "reject-package").read_text() == str(package):
  sys.exit(1)
 for declared in manifest.get("worker", []):
  worker = next((w for w in node["workers"] if w["name"] == declared["name"]), None)
  if worker is None:
   worker = {"name": declared["name"], "image": None, "network": "open"}
   node["workers"].append(worker)
  worker.update(model=declared.get("model"), budget=declared.get("budget"),
                instructions=(package / declared["instructions"]).read_text())
 node.update(source=str(package), manifest=manifest)
 state.write_text(json.dumps(node))
 current = root / "agents/gamecore-studio/current"
 current.unlink(missing_ok=True)
 current.symlink_to(package)
 if (root / "fail-activation").exists() and (root / "fail-activation").read_text() == node["workers"][0]["instructions"]:
  sys.exit(1)
elif args != ["agent", "restart", "gamecore-studio"]:
 sys.exit(2)
''')
        self.etos.chmod(0o755)
        (self.root / "node.json").write_text(json.dumps({"workers": [
            {"name": name, "budget": None, "image": "operator-image", "network": "offline"}
            for name in ("gc-designer", "gc-mechanic")]}))

    def worker_release(self):
        package = self.root / "package"
        shutil.copytree(HERE / "agent", package)
        manifest = package / "agent.toml"
        manifest.write_text(manifest.read_text().replace(
            'instructions = "workers/gc-designer.md"',
            'instructions = "workers/gc-designer.md"\nbudget = { usd = 0.50 }'))
        # Operator-created workers carry image/network policy not declared by the agent.
        workers = [{"name": name, "budget": budget, "image": "operator-image@sha256:123",
                    "network": {"allowlist": ["operator.example"]}, "instructions": "old"}
                   for name, budget in [("gc-designer", {"usd": 0.5}), ("gc-mechanic", None)]]
        (self.root / "node.json").write_text(json.dumps({"workers": workers}))
        mod.release(self.root, self.binary, package, str(self.etos), attempts=1)
        return package

    def test_r6_f_worker_change_updates_registration_preserves_policy_and_noop(self):
        package = self.worker_release()
        before = json.loads((self.root / "node.json").read_text())
        current = self.root / "agents/gamecore-studio/current"
        old = current.resolve()
        worker = package / "workers/gc-designer.md"
        worker.write_text(worker.read_text() + "\nNew narrative closure prerequisite.\n")
        mod.release(self.root, self.binary, package, str(self.etos), attempts=1)
        after = json.loads((self.root / "node.json").read_text())
        expected = json.loads(json.dumps(before))
        expected["workers"][0]["instructions"] = worker.read_text()
        expected["source"] = str(current.resolve())
        self.assertEqual(after, expected)
        self.assertNotEqual(current.resolve(), old)
        self.assertEqual((current / "bin/gamecore-studio").read_bytes(), (old / "bin/gamecore-studio").read_bytes())
        calls = (self.root / "calls").read_text()
        mod.release(self.root, self.binary, package, str(self.etos), attempts=1)
        self.assertEqual((self.root / "calls").read_text(), calls)
        (current / "workers/gc-designer.md").write_text("corrupted installed instructions")
        with self.assertRaisesRegex(ValueError, "checksum mismatch"):
            mod.release(self.root, self.binary, package, str(self.etos), attempts=1)
        self.assertEqual((self.root / "calls").read_text(), calls)

    def test_r6_f_health_failure_restores_worker_registration_and_policy(self):
        package = self.worker_release()
        current = self.root / "agents/gamecore-studio/current"
        old = current.resolve()
        before = json.loads((self.root / "node.json").read_text())
        marker = self.root / "agents/gamecore-studio/.applied-config.sha256"
        applied = marker.read_bytes()
        (package / "workers/gc-designer.md").write_text("failed release instructions")
        (self.root / "fail").touch()
        with self.assertRaisesRegex(RuntimeError, "health check"):
            mod.release(self.root, self.binary, package, str(self.etos), attempts=1)
        self.assertEqual(current.resolve(), old)
        self.assertEqual(json.loads((self.root / "node.json").read_text()), before)
        self.assertEqual(marker.read_bytes(), applied)

    def test_r6_f_operator_budget_drift_refuses_before_registration(self):
        package = self.worker_release()
        current = self.root / "agents/gamecore-studio/current"
        old = current.resolve()
        node = json.loads((self.root / "node.json").read_text())
        node["workers"][0]["budget"] = {"usd": 0.25}
        (self.root / "node.json").write_text(json.dumps(node))
        (package / "workers/gc-designer.md").write_text("new instructions")
        with self.assertRaisesRegex(ValueError, "worker budget differs"):
            mod.release(self.root, self.binary, package, str(self.etos), attempts=1)
        self.assertEqual(current.resolve(), old)
        self.assertEqual(json.loads((self.root / "node.json").read_text()), node)
        self.assertNotIn("agent upgrade", (self.root / "calls").read_text())

    def test_r6_f_missing_registered_worker_refuses_policy_reset(self):
        package = self.worker_release()
        current = self.root / "agents/gamecore-studio/current"
        old = current.resolve()
        node = json.loads((self.root / "node.json").read_text())
        node["workers"].pop()
        (self.root / "node.json").write_text(json.dumps(node))
        (package / "workers/gc-designer.md").write_text("new instructions")
        with self.assertRaisesRegex(ValueError, "registered worker missing"):
            mod.release(self.root, self.binary, package, str(self.etos), attempts=1)
        self.assertEqual(current.resolve(), old)
        self.assertEqual(json.loads((self.root / "node.json").read_text()), node)

    def test_r6_f_manifest_policy_change_refuses_before_node_calls(self):
        package = self.worker_release()
        current = self.root / "agents/gamecore-studio/current"
        old = current.resolve()
        calls = (self.root / "calls").read_text()
        manifest = package / "agent.toml"
        manifest.write_text(manifest.read_text().replace('providers = ["studio-voice"]', 'providers = ["other-provider"]'))
        with self.assertRaisesRegex(ValueError, "manifest authority changed"):
            mod.release(self.root, self.binary, package, str(self.etos), attempts=1)
        self.assertEqual(current.resolve(), old)
        self.assertEqual((self.root / "calls").read_text(), calls)

    def test_r6_f_partial_upgrade_failure_restores_old_registration(self):
        package = self.worker_release()
        current = self.root / "agents/gamecore-studio/current"
        old = current.resolve()
        before = json.loads((self.root / "node.json").read_text())
        (package / "workers/gc-designer.md").write_text("partially activated instructions")
        (self.root / "fail-activation").write_text("partially activated instructions")
        with self.assertRaises(subprocess.CalledProcessError):
            mod.release(self.root, self.binary, package, str(self.etos), attempts=1)
        self.assertEqual(current.resolve(), old)
        self.assertEqual(json.loads((self.root / "node.json").read_text()), before)

    def test_r6_f_failed_registration_rollback_invalidates_noop_marker(self):
        package = self.worker_release()
        current = self.root / "agents/gamecore-studio/current"
        old = current.resolve()
        (package / "workers/gc-designer.md").write_text("failed release instructions")
        (self.root / "fail").touch()
        (self.root / "reject-package").write_text(str(old))
        with self.assertRaisesRegex(RuntimeError, "registration rollback failed"):
            mod.release(self.root, self.binary, package, str(self.etos), attempts=1)
        self.assertFalse((self.root / "agents/gamecore-studio/.applied-config.sha256").exists())
        self.assertNotEqual(current.resolve(), old)

    def test_r3_d15_copy_is_immutable_atomic_switch_and_rollback(self):
        base = self.root / "agents/gamecore-studio"
        base.mkdir(parents=True)
        # Exact old shape: current points to a build clone whose bin is itself a link.
        old = self.root / "build-clone"
        (old / "bin").mkdir(parents=True)
        (old / "bin/gamecore-studio").symlink_to(self.binary)
        (old / "agent.toml").write_bytes((HERE / "agent/agent.toml").read_bytes())
        (base / "current").symlink_to(old)
        mod.release(self.root, self.binary, HERE / "agent", str(self.etos), attempts=1)
        current = base / "current"
        first = current.resolve()
        self.assertEqual(first.parent, base)
        self.assertFalse((current / "bin/gamecore-studio").is_symlink())
        subprocess.run(["sha256sum", "--check", "SHA256SUMS"], cwd=current, check=True, capture_output=True)
        self.binary.write_bytes(b"release-v2")
        self.assertEqual((current / "bin/gamecore-studio").read_bytes(), b"release-v1")
        (self.root / "fail").touch()
        with self.assertRaisesRegex(RuntimeError, "health check"):
            mod.release(self.root, self.binary, HERE / "agent", str(self.etos), attempts=1)
        self.assertEqual(current.resolve(), first)
        self.assertTrue(first.exists())
        self.assertTrue(old.exists())
        (self.root / "fail").unlink()
        mod.release(self.root, self.binary, HERE / "agent", str(self.etos), attempts=1)
        self.assertNotEqual(current.resolve(), first)
        self.assertTrue(first.exists())
        self.assertNotIn("uninstall", (self.root / "calls").read_text())
        calls = (self.root / "calls").read_text()
        mod.release(self.root, self.binary, HERE / "agent", str(self.etos), attempts=1)
        self.assertEqual((self.root / "calls").read_text(), calls)
        config = base / "state/config.toml"
        config.parent.mkdir(exist_ok=True)
        config.write_text("port = 7451\n")
        mod.release(self.root, self.binary, HERE / "agent", str(self.etos), attempts=1)
        self.assertGreater(len((self.root / "calls").read_text()), len(calls))

    def test_r3_d24_register_preserves_config_and_matches_client_identity(self):
        mod.register(self.root, str(self.project), "auto")
        path = self.root / "agents/gamecore-studio/state/config.toml"
        first = path.read_text()
        config = tomllib.loads(first)
        identity = hashlib.sha256(("a" * 32 + "\n" + str(self.project)).encode()).hexdigest()
        self.assertEqual(config["stage"]["projects"][identity], str(self.project))
        self.assertEqual(config["stage"]["confinement"], "docker")
        self.assertEqual(config["ops_prices"][0]["provider"], "bailian-tts")
        mod.register(self.root, str(self.project), "auto")
        self.assertEqual(path.read_text(), first)
        self.assertEqual(path.stat().st_mode & 0o777, 0o600)
        second = self.root / "second"
        (second / "ProjectSettings").mkdir(parents=True)
        (second / "ProjectSettings/ProjectSettings.asset").write_text("productGUID: " + "b" * 32)
        mod.register(self.root, str(second), "b" * 64)
        config = tomllib.loads(path.read_text())
        self.assertEqual(len(config["stage"]["projects"]), 2)
        self.assertEqual(config["ops_prices"][0]["unit"], "bailian_character")

    def test_r3_d14_templates_prices_and_apply_diff(self):
        models = tomllib.loads((HERE / "models.toml.tmpl").read_text())
        for model in models["models"]:
            self.assertGreater(model["sla"]["cost"]["input"], 0)
            self.assertGreater(model["sla"]["cost"]["output"], 0)
        ops = tomllib.loads((HERE / "ops.toml.tmpl").read_text())
        tts = next(p for p in ops["providers"] if p["family"] == "tts")
        companion = tomllib.loads((HERE / "agent/config.example.toml").read_text())
        self.assertEqual(tts["cost"]["per_unit"], companion["ops_prices"][0]["per_unit"])
        for name in ["models", "ops"]:
            (self.root / f"{name}.toml").write_text("# old\n")
        config_path = self.root / "agents/gamecore-studio/state/config.toml"
        config_path.parent.mkdir(parents=True)
        config_path.write_text('port = 7451\n[stage.projects]\n"' + "c" * 64 + '" = "/kept/project"\n')
        result = subprocess.run(["bash", str(HERE / "install.sh"), "--apply-prices", "--only", "tts"],
                                env={**os.environ, "ETOS_STUDIO_ROOT": str(self.root)}, capture_output=True, text=True, check=True)
        self.assertIn("---", result.stdout)
        self.assertIn("+++", result.stdout)
        self.assertEqual((self.root / "models.toml").read_text(), "# old\n")
        installed = tomllib.loads((self.root / "ops.toml").read_text())
        self.assertEqual(installed["providers"], [tts])
        config = tomllib.loads((self.root / "agents/gamecore-studio/state/config.toml").read_text())
        self.assertEqual(config["ops_prices"], companion["ops_prices"])
        self.assertEqual(config["port"], 7451)
        self.assertEqual(config["stage"]["projects"]["c" * 64], "/kept/project")
        self.assertFalse((self.root / "calls").exists())

    def placeholder_template(self):
        # R4-C permits the shipping template to carry an approved operator tariff.
        # Construct unconfigured input explicitly, independent of that declaration.
        blocks = mod.provider_blocks((HERE / "ops.toml.tmpl").read_text())
        image = next(b for b in blocks if tomllib.loads(b)["providers"][0]["family"] == "image")
        placeholder = re.sub(r'(?m)^per_unit = .*$', 'per_unit = "SET_BY_OPERATOR"', image)
        placeholder = re.sub(r'(?m)^# @studio note = .*$',
                             '# @studio note = "SET_BY_OPERATOR: declare a total per-image estimate and its basis"',
                             placeholder)
        return (HERE / "ops.toml.tmpl").read_text().replace(image, placeholder)

    def test_r4_placeholder_refuses_before_any_write(self):
        for name in ["models", "ops"]:
            (self.root / f"{name}.toml").write_text("# untouched\n")
        fixture = self.root / "installer"
        fixture.mkdir()
        for name in ("install.sh", "install-state.py", "models.toml.tmpl"):
            shutil.copyfile(HERE / name, fixture / name)
        (fixture / "ops.toml.tmpl").write_text(self.placeholder_template())
        result = subprocess.run(["bash", str(fixture / "install.sh"), "--apply-prices"],
            env={**os.environ, "ETOS_STUDIO_ROOT": str(self.root)}, capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("tariff_placeholder", result.stderr)
        for name in ["models", "ops"]:
            self.assertEqual((self.root / f"{name}.toml").read_text(), "# untouched\n")
        self.assertFalse((self.root / "agents").exists())

    def test_r4_operator_declaration_and_provider_binding(self):
        template = self.placeholder_template()
        template = template.replace('per_unit = "SET_BY_OPERATOR"', 'per_unit = 0.02')
        with self.assertRaisesRegex(ValueError, "tariff_placeholder"):
            mod.tariffs(template)
        template = template.replace("SET_BY_OPERATOR: declare a total per-image estimate and its basis",
                                    "Operator estimate including input for low quality images")
        prices = mod.tariffs(template)
        image = next(p for p in prices if p["op"] == "image")
        self.assertEqual(image["source"], "operator")
        self.assertEqual(image["provider"], "echo-images")
        self.assertEqual(image["model"], "gpt-image-2")
        self.assertEqual(image["per_unit"], 0.02)
        tts = next(p for p in prices if p["op"] == "tts")
        self.assertEqual(tts["source"], "published")
        self.assertTrue(tts["url"].startswith("https://www.alibabacloud.com/"))

    def test_r6_c_describe_only_cli_refuses_placeholder_without_writes(self):
        fixture = self.root / "installer"
        fixture.mkdir()
        for name in ("install.sh", "install-state.py"):
            shutil.copyfile(HERE / name, fixture / name)
        template = (HERE / "ops.toml.tmpl").read_text()
        describe = next(b for b in mod.provider_blocks(template)
                        if tomllib.loads(b)["providers"][0]["family"] == "describe")
        for name in ("models", "ops"):
            (self.root / f"{name}.toml").write_text("# untouched\n")
        config_path = self.root / "agents/gamecore-studio/state/config.toml"
        config_path.parent.mkdir(parents=True)
        config_path.write_text("port = 7451\n")
        placeholders = {
            "price": re.sub(r'(?m)^per_unit = .*$', 'per_unit = "SET_BY_OPERATOR"', describe),
            "note": re.sub(r'(?m)^# @studio note = .*$',
                           '# @studio note = "SET_BY_OPERATOR: declare a per-call estimate and basis"', describe),
        }
        for field, placeholder in placeholders.items():
            with self.subTest(field=field):
                (fixture / "ops.toml.tmpl").write_text(template.replace(describe, placeholder))
                before = {p.relative_to(self.root): (p.read_bytes(), p.stat().st_mtime_ns)
                          for p in self.root.rglob("*") if p.is_file()}
                result = subprocess.run(
                    ["bash", str(fixture / "install.sh"), "--apply-prices", "--only", "describe"],
                    env={**os.environ, "ETOS_STUDIO_ROOT": str(self.root)}, capture_output=True, text=True)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn("tariff_placeholder", result.stderr)
                self.assertEqual(result.stdout, "")
                self.assertEqual(
                    {p.relative_to(self.root): (p.read_bytes(), p.stat().st_mtime_ns)
                     for p in self.root.rglob("*") if p.is_file()}, before)

    def test_r6_c_describe_only_cli_updates_provider_and_tariff_preserving_other_state(self):
        template = (HERE / "ops.toml.tmpl").read_text()
        # Distinct installed tariffs expose any accidental full-template replacement.
        before_ops = re.sub(r'(?m)^per_unit = .*$', 'per_unit = 0.75', template)
        ops_path = self.root / "ops.toml"
        ops_path.write_text(before_ops)
        models_path = self.root / "models.toml"
        models_path.write_text('# custom model prices\noperator_setting = "keep"\n')
        models_before = (models_path.read_bytes(), models_path.stat().st_mtime_ns)
        config_path = self.root / "agents/gamecore-studio/state/config.toml"
        config_path.parent.mkdir(parents=True)
        before_config = ('port = 7451\n' + (HERE / "agent/config.example.toml").read_text()
                         + '"' + "c" * 64 + '" = "/kept/project"\n'
                         + '\n[[ops_prices]]\nop = "image"\nprovider = "custom-image"\nper_unit = 0.5\n'
                         + '\n[[ops_prices]]\nop = "describe"\nprovider = "old-describe"\nper_unit = 0.75\n')
        config_path.write_text(before_config)
        result = subprocess.run(
            ["bash", str(HERE / "install.sh"), "--apply-prices", "--only", "describe"],
            env={**os.environ, "ETOS_STUDIO_ROOT": str(self.root)}, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual((models_path.read_bytes(), models_path.stat().st_mtime_ns), models_before)
        expected_ops = tomllib.loads(before_ops)
        describe_block = next(b for b in mod.provider_blocks(template)
                              if tomllib.loads(b)["providers"][0]["family"] == "describe")
        describe = tomllib.loads(describe_block)["providers"][0]
        metadata = tomllib.loads("\n".join(re.findall(r"(?m)^# @studio (.*)$", describe_block)))
        expected_ops["providers"] = [p for p in expected_ops["providers"] if p["family"] != "describe"] + [describe]
        self.assertEqual(tomllib.loads(ops_path.read_text()), expected_ops)
        for block in mod.provider_blocks(before_ops):
            if tomllib.loads(block)["providers"][0]["family"] != "describe":
                self.assertIn(block.rstrip(), ops_path.read_text())
        expected_config = tomllib.loads(before_config)
        expected_config["ops_prices"] = [p for p in expected_config["ops_prices"] if p["op"] != "describe"] + [{
            "op": "describe", "provider": "echo-describe", "model": "echo/gpt-5.6-sol",
            "source": "operator", "unit": "call", "per_unit": 0.01,
            "note": metadata["note"],
        }]
        self.assertEqual(tomllib.loads(config_path.read_text()), expected_config)
        self.assertFalse((self.root / "calls").exists())


if __name__ == "__main__":
    unittest.main()
