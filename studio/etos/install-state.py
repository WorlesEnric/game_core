#!/usr/bin/env python3
"""Secret-free project registration and immutable companion releases (integrator only)."""
import argparse
import difflib
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import time
import tomllib

HERE = Path(__file__).resolve().parent


def atomic_write(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(prefix=".config-", dir=path.parent)
    try:
        with os.fdopen(fd, "w") as stream:
            stream.write(text)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(tmp, path)
    finally:
        if os.path.exists(tmp):
            os.unlink(tmp)


def section(text, name, values):
    # Preserve unrelated settings and comments; our sections contain only scalar entries.
    block = f"[{name}]\n" + "".join(f"{json.dumps(k)} = {json.dumps(v)}\n" for k, v in sorted(values.items()))
    pattern = rf"(?ms)^\[{re.escape(name)}\]\s*\n.*?(?=^\[|\Z)"
    if re.search(pattern, text):
        return re.sub(pattern, lambda _: block + "\n", text)
    return text.rstrip() + "\n\n" + block


def register(root, project, identity):
    project = Path(os.path.abspath(project))
    settings = project / "ProjectSettings/ProjectSettings.asset"
    if not settings.is_file():
        raise ValueError("registration requires a Unity project with ProjectSettings.asset")
    if identity == "auto":
        # Same authority derivation as EtosProjectContext.LoadProjectId; never read settings secrets.
        cached = project / "UserSettings/GameCoreStudio.Project.json"
        stored = json.loads(cached.read_text()) if cached.exists() else {}
        if stored.get("path") == str(project) and re.fullmatch(r"[a-f0-9]{64}", stored.get("projectId", "")):
            identity = stored["projectId"]
        else:
            guid = re.search(r"(?m)^\s*productGUID:\s*([a-fA-F0-9]{32})\s*$", settings.read_text())
            if not guid:
                raise ValueError("project_guid_missing: supply the paired app's stable SHA-256 id")
            identity = hashlib.sha256((guid[1].lower() + "\n" + str(project)).encode()).hexdigest()
    if not re.fullmatch(r"[a-f0-9]{64}", identity):
        raise ValueError("project id must be a lowercase SHA-256")
    path = root / "agents/gamecore-studio/state/config.toml"
    before = path.read_text() if path.exists() else (HERE / "agent/config.example.toml").read_text()
    config = tomllib.loads(before)
    stage = dict(config.get("stage", {}))
    projects = dict(stage.pop("projects", {}))
    if identity in projects and projects[identity] != str(project):
        raise ValueError("project id already maps to a different path; explicit operator reconciliation required")
    projects[identity] = str(project)
    stage["command"] = str(HERE.parent / "stage/stage.sh")
    after = section(section(before, "stage", stage), "stage.projects", projects)
    tomllib.loads(after)
    if not path.exists() or after != before:
        print("".join(difflib.unified_diff(before.splitlines(True), after.splitlines(True), fromfile=str(path), tofile=str(path))))
        atomic_write(path, after)
    print(f"registered {identity} -> {project}")


def apply_prices(root):
    for name in ("models", "ops"):
        path = root / f"{name}.toml"
        before = path.read_text() if path.exists() else ""
        after = (HERE / f"{name}.toml.tmpl").read_text()
        tomllib.loads(after)
        if before != after:
            print("".join(difflib.unified_diff(before.splitlines(True), after.splitlines(True), fromfile=str(path), tofile=str(path))))
            atomic_write(path, after)
    path = root / "agents/gamecore-studio/state/config.toml"
    before = path.read_text() if path.exists() else ""
    # Price replacement is explicit: do not preserve unverifiable old image/call tariffs.
    without_prices = re.sub(r"(?ms)^\[\[ops_prices\]\]\s*\n.*?(?=^\[|\Z)", "", before)
    prices = (HERE / "agent/config.example.toml").read_text().split("[stage]", 1)[0]
    after = without_prices.rstrip() + "\n\n" + prices
    if tomllib.loads(before).get("ops_prices") != tomllib.loads(after).get("ops_prices"):
        print("".join(difflib.unified_diff(before.splitlines(True), after.splitlines(True), fromfile=str(path), tofile=str(path))))
        atomic_write(path, after)
    print("Price templates applied. Integrator must reload node/companion; no services restarted.")


def release(root, binary, manifest_dir, etos, attempts=30):
    base = root / "agents/gamecore-studio"
    base.mkdir(parents=True, exist_ok=True)
    with (base / ".install.lock").open("w") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        with tempfile.TemporaryDirectory(prefix=".release-", dir=base) as temp:
            stage = Path(temp) / "package"
            (stage / "bin").mkdir(parents=True)
            shutil.copyfile(binary, stage / "bin/gamecore-studio")
            (stage / "bin/gamecore-studio").chmod(0o755)
            shutil.copyfile(manifest_dir / "agent.toml", stage / "agent.toml")
            shutil.copytree(manifest_dir / "workers", stage / "workers")
            files = sorted(p for p in stage.rglob("*") if p.is_file())
            checksums = "".join(f"{hashlib.sha256(p.read_bytes()).hexdigest()}  {p.relative_to(stage)}\n" for p in files)
            version = tomllib.loads((stage / "agent.toml").read_text())["version"] + "-" + hashlib.sha256(checksums.encode()).hexdigest()[:16]
            dest = base / version
            (stage / "SHA256SUMS").write_text(checksums)
            if dest.exists():
                for line in checksums.splitlines():
                    digest, name = line.split("  ", 1)
                    file = dest / name
                    if file.is_symlink() or hashlib.sha256(file.read_bytes()).hexdigest() != digest:
                        raise ValueError("installed release checksum mismatch")
            else:
                os.replace(stage, dest)
        current = base / "current"
        if current.exists() and not current.is_symlink():
            raise ValueError("current is a directory; integrator must migrate it to a retained release before atomic symlink installation")
        previous = os.readlink(current) if current.is_symlink() else None
        if previous and Path(previous).is_absolute() and Path(previous) == dest:
            previous = version
        config = base / "state/config.toml"
        config_digest = hashlib.sha256(config.read_bytes() if config.exists() else b"").hexdigest()
        applied = base / ".applied-config.sha256"
        if previous == version and applied.exists() and applied.read_text().strip() == config_digest:
            print(f"unchanged: release {version} (checksums verified)")
            return
        if current.exists():
            old_manifest = tomllib.loads((current / "agent.toml").read_text())
            new_manifest = tomllib.loads((dest / "agent.toml").read_text())
            if old_manifest != new_manifest:
                raise ValueError("manifest authority changed; integrator must register the new manifest with etos before atomic release activation")
        def switch(target):
            link = base / ".current-next"
            link.unlink(missing_ok=True)
            link.symlink_to(target)
            os.replace(link, current)
        try:
            if previous is None:
                subprocess.run([etos, "agent", "install", "--link", str(dest)], check=True)
            switch(version)
            if previous is not None:
                subprocess.run([etos, "agent", "restart", "gamecore-studio"], check=True)
            for _ in range(attempts):
                rows = json.loads(subprocess.check_output([etos, "--json", "agent", "list"]))
                if any(r.get("agent", r.get("name")) == "gamecore-studio" and r.get("state") == "ready" for r in rows):
                    atomic_write(applied, config_digest + "\n")
                    print(f"healthy: {version}; previous release retained: {previous or 'none'}")
                    return
                time.sleep(1)
            raise RuntimeError("new companion failed health check; old release retained")
        except Exception:
            if previous is not None:
                switch(previous)
                subprocess.run([etos, "agent", "restart", "gamecore-studio"], check=False)
            raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    sub = parser.add_subparsers(dest="action", required=True)
    sub.add_parser("prices")
    reg = sub.add_parser("register")
    reg.add_argument("identity")
    reg.add_argument("project")
    rel = sub.add_parser("release")
    rel.add_argument("binary", type=Path)
    rel.add_argument("--etos", default="etos")
    args = parser.parse_args()
    if args.action == "prices":
        apply_prices(args.root)
    elif args.action == "register":
        register(args.root, args.project, args.identity)
    else:
        release(args.root, args.binary, HERE / "agent", args.etos)


if __name__ == "__main__":
    main()
