#!/usr/bin/env python3
"""Scratch-only R8-A acceptance. Run via the harness process supervisor, never installed ETOS.

Prerequisites: freshly built companion and Client.dll, local ETOS binaries, existing
localhost/gc-designer:current and gamecore-stage:6000.0.75f1-v1 Docker images, exact
provisioned stage cache, licensed Unity 6000.0.75f1. No models or paid ops are called.
The local scripted model selects one shell tool; query responses come only from ETOS.
"""
import argparse
import base64
import fcntl
import gzip
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
import os
from pathlib import Path
import re
import shlex
import shutil
import signal
import socket
import sqlite3
import subprocess
import threading
import time

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[5]
PROJECT = REPO / "games/hollowmere"
BIN = Path.home() / ".local/opt/etos/bin"
CLIENT = REPO / "studio/agent/tools/r8-a/Client/bin/Debug/net8.0/Client.dll"
loader = importlib.util.spec_from_file_location("r8a_redaction", REPO / "studio/stage/redact.py")
redaction = importlib.util.module_from_spec(loader)
loader.loader.exec_module(redaction)


def require(value, message):
    if not value:
        raise RuntimeError(message)


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(redaction.redact(json.dumps(value, indent=2, ensure_ascii=False)))
    temporary.replace(path)


def load(path):
    return json.loads(path.read_text())


def sha(data):
    return hashlib.sha256(data).hexdigest()


def port():
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        return probe.getsockname()[1]


def command(args, env=None, check=True):
    process = subprocess.run([str(arg) for arg in args], cwd=REPO, env=env,
                             capture_output=True, text=True)
    require(not check or process.returncode == 0,
            "Command failed: " + str(args[0]) + " " + redaction.redact(process.stderr + process.stdout))
    return process


def clean_environment():
    # Deliberately do not inherit provider credentials, installed ETOS_ROOT or auth paths.
    return {name: os.environ[name] for name in ("HOME", "USER", "LOGNAME", "PATH", "LANG", "LC_ALL", "DISPLAY", "XDG_RUNTIME_DIR") if name in os.environ}


def write_executable(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text)
    path.chmod(0o755)


class LocalToolSelector(BaseHTTPRequestHandler):
    """Deterministic tool selection only. Has no query database and never returns query rows."""
    def log_message(self, *args):
        pass

    def do_POST(self):
        size = int(self.headers.get("Content-Length", "0"))
        require(0 <= size <= 4 * 1024 * 1024, "Model request too large")
        body = json.loads(self.rfile.read(size))
        messages = body.get("messages", [])
        tools = body.get("tools", [])
        shell = next((tool.get("function", {}) for tool in tools if tool.get("function", {}).get("name") == "shell"), None)
        completed = any(message.get("role") == "tool" for message in messages)
        require(shell is not None or completed, "Local selector expected worker shell tool, not an unrelated model invocation")
        if completed:
            message = {"role": "assistant", "content": "The worker has completed its actual ETOS query and written /outputs/clarification.json. No changes or media requested."}
            reason = "stop"
        else:
            script = base64.b64encode((HERE / "worker-query.py").read_bytes()).decode()
            code = "import base64;exec(compile(base64.b64decode(" + repr(script) + "),'r8a-worker-query','exec'))"
            arguments = {"command": "python3 -c " + shlex.quote(code), "timeout": 180, "background": False}
            message = {"role": "assistant", "content": None, "tool_calls": [{"id": "r8a_actual_query", "type": "function",
                       "function": {"name": "shell", "arguments": json.dumps(arguments)}}]}
            reason = "tool_calls"
        self.server.calls += 1
        save(self.server.evidence / "local-selector.json", {"calls": self.server.calls,
             "provider": "loopback deterministic shell selection", "externalProviderCalls": 0,
             "queryResponsesFabricated": False, "workerScriptSha256": sha((HERE / "worker-query.py").read_bytes())})
        common = {"id": "r8a-local", "created": int(time.time()), "model": body.get("model", "query-selector")}
        if body.get("stream"):
            delta = dict(message)
            if "tool_calls" in delta:
                delta["tool_calls"][0]["index"] = 0
            chunks = [{**common, "object": "chat.completion.chunk", "choices": [{"index": 0, "delta": delta, "finish_reason": None}]},
                      {**common, "object": "chat.completion.chunk", "choices": [{"index": 0, "delta": {}, "finish_reason": reason}],
                       "usage": {"prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2}}]
            data = ("".join("data: " + json.dumps(chunk) + "\n\n" for chunk in chunks) + "data: [DONE]\n\n").encode()
            content_type = "text/event-stream"
        else:
            data = json.dumps({**common, "object": "chat.completion", "choices": [{"index": 0, "message": message, "finish_reason": reason}],
                               "usage": {"prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2}}).encode()
            content_type = "application/json"
        self.send_response(200)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)


def retained_stream(process, path):
    with path.open("w") as output:
        for line in iter(process.stdout.readline, ""):
            output.write(redaction.redact(line))
            output.flush()


def prepare(args):
    evidence = args.evidence.resolve()
    require(not evidence.exists(), "Evidence directory must be fresh")
    require(evidence.is_relative_to(REPO / ".evidence/r8-a"), "Evidence must be under this checkout .evidence/r8-a")
    require(not (PROJECT / "UserSettings/GameCoreStudio.json").exists(), "Existing ETOS settings must not be inspected or overwritten")
    require(args.companion.is_file() and CLIENT.is_file(), "Build companion and standalone product client first")
    require(args.cache.is_dir(), "Exact versioned stage cache is required")
    evidence.mkdir(parents=True)
    import tempfile
    root = Path(tempfile.mkdtemp(prefix="r8a-", dir="/tmp"))
    root.chmod(0o700)
    api, gateway, broker, model = port(), port(), port(), port()
    project_guid = re.search(r"productGUID: ([a-fA-F0-9]{32})", (PROJECT / "ProjectSettings/ProjectSettings.asset").read_text())[1]
    project = sha((project_guid.lower() + "\n" + str(PROJECT)).encode())
    owner = sha(json.dumps(["gamecore-unity", project], separators=(",", ":")).encode())
    candidate = evidence / "candidate"
    shutil.copytree(REPO / "samples/mechanisms/pressure-plate/candidate", candidate)
    cs = load(candidate / "change-set.json")
    # Valid ULID envelope identity only; actual package/proposal bytes remain untouched.
    alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"
    value = (int(time.time() * 1000) << 80) | int.from_bytes(os.urandom(10), "big")
    encoded = ""
    for _ in range(26):
        encoded = alphabet[value & 31] + encoded
        value >>= 5
    cs["id"] = "cs_" + encoded
    save(candidate / "change-set.json", cs)
    environment = clean_environment()
    environment.update(ETOS_ROOT=str(root), R8A_FIXTURE_TOKEN="public-local-selector", NO_PROXY="127.0.0.1,localhost")
    command([BIN / "etosd", "init", "--root", root, "--name", "r8a", "--owner", "r8a"], environment)
    (root / "bin").mkdir(exist_ok=True)
    shutil.copyfile(BIN / "etos-musl", root / "bin/etos")
    (root / "bin/etos").chmod(0o755)
    (root / "etos.toml").write_text(f'''models = "models.toml"
share_roots = []
[node]
name = "r8a"
owner = "r8a"
local = true
[gateway]
listen = "127.0.0.1:{gateway}"
[containers]
runtime = "docker"
default_image = "localhost/gc-designer:current"
etos_binary = "bin/etos"
ready_timeout = "180s"
sockets = "unix"
[broker]
listen = "172.17.0.1:{broker}"
authority = "172.17.0.1:{broker}"
[api]
listen = "127.0.0.1:{api}"
agent_budget_usd = 1.0
[ssh]
web = false
[actors]
confinement = "sandbox"
''')
    (root / "models.toml").write_text(f'''[endpoints.local]
base_url = "http://127.0.0.1:{model}/v1"
credential = "env:R8A_FIXTURE_TOKEN"
[[models]]
id = "local/query-selector"
aliases = ["default", "fast"]
roles = ["worker", "planning", "controller", "compaction"]
cost_class = "low"
[models.sla.cost]
input = 1.0
cached_input = 1.0
output = 1.0
[models.capabilities]
context_window = 200000
max_output = 32000
tools = true
parallel_tools = false
''')
    agent = root / "source-agent"
    (agent / "bin").mkdir(parents=True)
    shutil.copy2(args.companion, agent / "bin/gamecore-studio")
    stage_root = root / "slots"
    cache = stage_root / owner / "_warm" / args.cache.name
    cache.parent.mkdir(parents=True)
    command([REPO / "studio/stage/provision-cache.sh", args.cache, "--verify"], environment)
    command(["cp", "-a", "--reflink=auto", args.cache, cache], environment)
    (agent / "agent.toml").write_text(f'''agent = "gamecore-studio"
version = "0.1.0"
label = "R8-A scratch companion"
sdk = ">=1, <2"
grants = ["logger", "query", "changes", "tasks", "files", "topics", "ops", "providers", "realtime", "proxy", "services"]
[process]
command = "bin/gamecore-studio"
restart = "always"
env = {{ GAMECORE_STAGE_ROOT = {json.dumps(str(stage_root))}, GAMECORE_STAGE_REPO = {json.dumps(str(REPO))} }}
[[worker]]
name = "gc-designer"
model = "local/query-selector"
''')
    state = root / "agents/gamecore-studio/state"
    state.mkdir(parents=True)
    (state / "config.toml").write_text('allowed_apps = ["gamecore-unity", "r8a-foreign"]\nreask_on_invalid = false\nfollow_wait_ms = 100\n'
        + '[stage]\nconfinement = "docker"\n[stage.projects]\n' + json.dumps(project) + ' = ' + json.dumps(str(PROJECT)) + '\n')
    config = {"root": str(root), "evidence": str(evidence), "nodeUrl": f"http://127.0.0.1:{api}", "modelPort": model,
              "projectId": project, "candidate": str(candidate), "pairingFile": str(root / "scratch-app.json"),
              "foreignPairingFile": str(root / "scratch-foreign.json"), "stageRoot": str(stage_root), "owner": owner,
              "state": str(state), "companionSha256": sha(args.companion.read_bytes()), "sourceRevision": command(["git", "rev-parse", "HEAD"]).stdout.strip()}
    save(evidence / "config.json", config)
    return config, environment


def etos(config, environment, *args, check=True):
    return command([BIN / "etos", *args], environment, check=check)


def editor(config, phase):
    environment = clean_environment()
    environment.update(UNITY=str(HERE / "unity.sh"), GAMECORE_R8A_PAIRING_PATH=config["pairingFile"], GAMECORE_ETOS_AUTOSTART="1")
    output = Path(config["evidence"])
    args = [REPO / "studio/tools/unity-batch.sh", "--project", PROJECT, "--log-dir", output / (phase + "-logs"),
            "--label", "r8-a-" + phase, "--timeout", "900", "--attempts", "1", "--",
            "-executeMethod", "Hollowmere.R8_A.Acceptance.Run", "-gcR8AConfig", output / "config.json", "-gcR8APhase", phase]
    result = command(args, environment, check=False)
    (output / (phase + "-launcher.log")).write_text(redaction.redact(result.stdout + result.stderr))
    require(result.returncode == 0, "Batch Editor " + phase + " failed; see retained logs")
    expected = "cancel-editor-result.json" if phase == "cancel" else "editor-result.json"
    require(load(output / expected)["status"] == "PASS", "Batch phase did not produce a passing receipt")


def docker(*args, check=True):
    return command(["/usr/bin/docker", *args], clean_environment(), check=check)


def labelled_containers(config, job):
    identity = sha(str(Path(config["state"]) / "stage-control" / job).encode())
    found = docker("ps", "-aq", "--filter", "label=gamecore.stage.job=" + identity).stdout.split()
    require(all(re.fullmatch(r"[a-f0-9]{12,64}", item) for item in found), "Docker returned an invalid container identity")
    return identity, found


def stage_and_pause(config, environment):
    output = Path(config["evidence"])
    command(["dotnet", CLIENT, "queue", output / "config.json"], environment)
    queued = load(output / "stage-queued.json")
    job = queued["jobId"]
    slot = Path(config["stageRoot"]) / config["owner"] / queued["slot"]
    prefix = "gc-stage-" + sha(str(slot).encode())[:32] + "-"
    deadline = time.monotonic() + 300
    while time.monotonic() < deadline:
        label, containers = labelled_containers(config, job)
        for container in containers:
            # Only fixed nonsecret fields. Never Docker inspect Config.Env.
            info = json.loads(docker("inspect", "--format", '{{json .State}}', container).stdout)
            name = docker("inspect", "--format", '{{.Name}}', container).stdout.strip().lstrip("/")
            executable = docker("inspect", "--format", '{{.Path}} {{json .Args}}', container).stdout.strip()
            if not info.get("Running") or not name.startswith(prefix) or "dotnet" not in executable:
                continue
            docker("pause", container)
            paused = json.loads(docker("inspect", "--format", '{{json .State}}', container).stdout)
            require(paused.get("Paused") is True, "Owned dotnet container was not paused")
            with (slot.parent / ".locks" / slot.name).open("r+") as stage_lock:
                try:
                    fcntl.flock(stage_lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
                except BlockingIOError:
                    pass
                else:
                    raise RuntimeError("Running stage did not hold its allocator slot lock")
            save(output / "container-running.json", {"jobId": job, "containerId": container, "containerName": name,
                 "jobLabel": label, "executable": executable, "stateBefore": info, "pausedForSerialEditor": paused,
                 "intervention": "docker pause of exact job-labelled real dotnet process; no substitute process",
                 "stageSlotLockHeld": True, "slot": str(slot)})
            # No owned stage Unity process may run while the batch UI Editor starts.
            owners = allocator_owners()
            require(not any(str(slot) in row.get("cmdline", "") for row in owners), "Stage still owns a Unity allocator reservation")
            save(output / "allocator-before-ui.json", owners)
            return job, slot
        time.sleep(0.05)
    raise RuntimeError("No real job-labelled dotnet Docker container reached running state before deadline")


def allocator_owners():
    directory = Path.home() / "wkspace/gc-studio/.unity-slots"
    result = []
    for file in directory.glob("slot*.owner"):
        text = file.read_text()
        match = re.search(r"pid=(\d+)", text)
        pid = int(match[1]) if match else 0
        proc = Path("/proc") / str(pid) / "cmdline"
        result.append({"ownerFile": str(file), "pid": pid,
                       "cmdline": proc.read_bytes().replace(b"\0", b" ").decode(errors="replace") if proc.exists() else "exited"})
    return result


def collect(config, job, slot):
    output = Path(config["evidence"])
    label, remaining = labelled_containers(config, job)
    require(not remaining, "Cancelled job still has a Docker container")
    lock = slot.parent / ".locks" / slot.name
    with lock.open("r+") as handle:
        fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
        fcntl.flock(handle, fcntl.LOCK_UN)
    owners = allocator_owners()
    require(not any(str(slot) in row.get("cmdline", "") for row in owners), "Cancelled stage retained a Unity allocator owner")
    with sqlite3.connect((Path(config["state"]) / "ledger.db").as_uri() + "?mode=ro", uri=True) as db:
        record = db.execute("SELECT state,slot,verdict FROM stage_jobs WHERE job_id=?", (job,)).fetchone()
        require(record is not None and record[0] == "cancelled" and record[2] is None, "Durable cancellation did not clear verdict")
        # Issuance authority is the exact stage_jobs.verdict value, not a separate table.
        authority = int(record[2] is not None)
    require(not (slot / "out/verdict.json").exists(), "Cancelled stage retained a verdict file")
    save(output / "stage-resources.json", {"jobId": job, "jobLabel": label, "containersRemaining": remaining,
         "stageSlotLockAcquired": True, "stageState": record[0], "verdict": record[2], "issuedVerdicts": authority,
         "allocatorOwners": owners, "ownedAllocatorSlotsRemaining": 0})
    trace = collect_query(config)
    require(any(row["name"] == "shell" and "r8a-worker-query" in row["args"].get("command", "") for row in trace), "Real worker shell trace missing")
    # Never copy node/agent state or credentials, even on failure. Product stage logs are redacted.
    logs = output / "stage-logs"
    for source in (slot / "out").rglob("*"):
        if source.is_file() and source.suffix in (".log", ".json", ".xml", ".txt"):
            target = logs / source.relative_to(slot / "out")
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(redaction.redact(source.read_text(errors="replace")))


def collect_query(config):
    output = Path(config["evidence"])
    if not (output / "query-request-state.json").exists():
        return []
    query = load(output / "query-request-state.json")
    tasks = query.get("tasks", []) or ([query["taskId"]] if query.get("taskId") else [])
    trace = []
    with sqlite3.connect((Path(config["root"]) / "node.db").as_uri() + "?mode=ro", uri=True) as db:
        for task in tasks:
            task = task.get("taskId", task.get("id")) if isinstance(task, dict) else task
            for turn, index, name, args, outcome in db.execute("SELECT turn,idx,name,args,outcome FROM tool_call WHERE task=? ORDER BY turn,idx", (task,)):
                trace.append({"taskId": task, "turn": turn, "index": index, "name": name,
                              "args": json.loads(args), "outcome": json.loads(outcome) if outcome else None})
    save(output / "worker-tool-trace.json", trace)
    return trace


def retain(evidence, destination):
    require(evidence.is_relative_to(REPO / ".evidence/r8-a"), "Unexpected source")
    require(destination.is_relative_to(HERE / "Evidence~") and not destination.exists(), "Retention requires fresh R8_A/Evidence~ directory")
    destination.mkdir(parents=True)
    files = []
    # Only public generated evidence. Candidate archives aren't recopied; manifest hashes bind them.
    for source in evidence.rglob("*"):
        if not source.is_file() or source.is_symlink() or "candidate" in source.relative_to(evidence).parts:
            continue
        if source.suffix not in (".json", ".jsonl", ".txt", ".log", ".xml"):
            continue
        data = redaction.redact(source.read_text(errors="replace")).encode()
        target = destination / source.relative_to(evidence)
        target.parent.mkdir(parents=True, exist_ok=True)
        target = target.with_name(target.name + ".gz")
        target.write_bytes(gzip.compress(data, mtime=0))
        files.append({"path": str(target.relative_to(destination)), "sha256": sha(data), "uncompressedBytes": len(data)})
    save(destination / "manifest.json", {"files": files, "authority": "real scratch ETOS node, real companion, real Docker worker and stage; local deterministic tool selection only"})


def run(args):
    config, environment = prepare(args)
    output = Path(config["evidence"])
    selector = ThreadingHTTPServer(("127.0.0.1", config["modelPort"]), LocalToolSelector)
    selector.evidence, selector.calls = output, 0
    threading.Thread(target=selector.serve_forever, daemon=True).start()
    daemon = subprocess.Popen([str(BIN / "etosd"), "run", "--root", config["root"]], env=environment,
                              stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, start_new_session=True)
    reader = threading.Thread(target=retained_stream, args=(daemon, output / "scratch-node.log"), daemon=True)
    reader.start()
    job = slot = None
    try:
        deadline = time.monotonic() + 60
        while etos(config, environment, "node", "status", check=False).returncode:
            require(daemon.poll() is None and time.monotonic() < deadline, "Scratch ETOS node did not become ready")
            time.sleep(0.2)
        etos(config, environment, "worker", "create", "gc-designer", "--image", "localhost/gc-designer:current", "--network", "offline", "--model", "local/query-selector")
        etos(config, environment, "agent", "install", Path(config["root"]) / "source-agent")
        for name, pairing in (("gamecore-unity", config["pairingFile"]), ("r8a-foreign", config["foreignPairingFile"])):
            app = Path(config["root"]) / ("app-" + name)
            app.mkdir()
            (app / "app.toml").write_text('app = ' + json.dumps(name) + '\nlabel = "R8-A scratch app"\nroutes = ["proxy", "query", "changes", "entrances"]\nuses = ["gamecore-studio"]\n')
            etos(config, environment, "app", "install", app)
            etos(config, environment, "app", "pair", name, "--approve", "--out", pairing)
        time.sleep(2)
        print("R8_A_READY " + str(output / "config.json"), flush=True)
        editor(config, "query")
        job, slot = stage_and_pause(config, environment)
        editor(config, "cancel")
        collect(config, job, slot)
        # Restart only this freshly installed scratch companion. Durable cancellation must survive.
        etos(config, environment, "agent", "restart", "gamecore-studio")
        deadline = time.monotonic() + 30
        while True:
            status = command(["dotnet", CLIENT, "read", output / "config.json"], environment, check=False)
            if status.returncode == 0:
                break
            require(time.monotonic() < deadline, "Cancelled state/verdict did not survive scratch companion restart")
            time.sleep(0.2)
        save(output / "result.json", {"status": "PASS", "rows": ["W-ETOS-04", "W-REC-03"],
             "scratchRoot": config["root"], "stageJobId": job, "sourceRevision": config["sourceRevision"],
             "companionSha256": config["companionSha256"], "installedServiceTouched": False,
             "externalProviderCalls": 0, "sequentialBatchEditors": True, "durableAfterCompanionRestart": True})
        print("R8_A_PASS " + str(output), flush=True)
    except Exception as error:
        save(output / "failure.json", {"status": "FAIL", "error": str(error), "scratchRoot": config["root"]})
        raise
    finally:
        try:
            collect_query(config)
        except Exception as error:
            save(output / "trace-retention-error.json", {"error": str(error)})
        # Ask the real service to tear down our own running job before stopping the scratch node.
        # No container belonging to another run is ever signalled or removed.
        if (output / "stage-queued.json").exists() and not (output / "stage-cancelled.json").exists():
            command(["dotnet", CLIENT, "cancel", output / "config.json"], environment, check=False)
        if daemon.poll() is None:
            os.killpg(daemon.pid, signal.SIGTERM)
        try:
            daemon.wait(timeout=30)
        except subprocess.TimeoutExpired:
            os.killpg(daemon.pid, signal.SIGKILL)
            daemon.wait(timeout=10)
        selector.shutdown()
        reader.join(timeout=5)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    modes = parser.add_subparsers(dest="mode", required=True)
    execution = modes.add_parser("run")
    execution.add_argument("--companion", type=Path, required=True)
    execution.add_argument("--cache", type=Path, required=True)
    execution.add_argument("--evidence", type=Path, required=True)
    retention = modes.add_parser("retain")
    retention.add_argument("evidence", type=Path)
    retention.add_argument("destination", type=Path)
    args = parser.parse_args()
    if args.mode == "run":
        run(args)
    else:
        retain(args.evidence.resolve(), args.destination.resolve())


if __name__ == "__main__":
    main()
