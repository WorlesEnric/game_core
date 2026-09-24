#!/usr/bin/env python3
"""Validate the Game Core documentation without third-party dependencies.

This is a documentation consistency check, not a Unity or protocol test runner.
Run from any directory: python3 tools/validate_game_core_docs.py
"""

from __future__ import annotations

import argparse
from collections import Counter
from dataclasses import dataclass
import html
import json
from pathlib import Path
import re
import sys
import tempfile
from urllib.parse import unquote, urlsplit


REQUIRED_DOCS = (
    "README.md",
    "00-core-protocols.md",
    "01-architecture.md",
    "02-composition-and-propagation.md",
    "03-runtime-and-execution.md",
    "04-unity-integration.md",
    "05-contracts-and-data-model.md",
    "06-lifecycle-and-recovery.md",
    "07-reference-compositions.md",
    "08-validation-and-performance.md",
    "09-implementation-guide.md",
    "10-decisions-and-open-questions.md",
)
ID_PATTERN = re.compile(r"\b(?:P|GC|TEST)-\d{3}\b")
EXPLICIT_ANCHOR = re.compile(
    r"<(?:a|[a-z][a-z0-9]*)\b[^>]*?\b(?:id|name)\s*=\s*['\"]([^'\"]+)['\"][^>]*>",
    re.IGNORECASE,
)


@dataclass
class Markdown:
    path: Path
    text: str
    visible: str
    anchors: Counter
    links: list[tuple[int, str]]


def without_code(text: str) -> str:
    """Retain line numbers while ignoring code fences and HTML comments."""
    text = re.sub(r"<!--[\s\S]*?-->", lambda m: "\n" * m[0].count("\n"), text)
    result = []
    fence = None
    for line in text.splitlines():
        marker = re.match(r"^ {0,3}(`{3,}|~{3,})", line)
        if fence:
            if marker and marker[1][0] == fence[0] and len(marker[1]) >= len(fence):
                fence = None
            result.append("")
        elif marker:
            fence = marker[1]
            result.append("")
        else:
            result.append(line)
    return "\n".join(result)


def heading_slug(text: str) -> str:
    """GitHub-style anchors for the ATX/Setext headings used by this set."""
    text = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", text)
    text = re.sub(r"<[^>]+>", "", text)
    text = html.unescape(text).lower().strip()
    text = re.sub(r"[^\w\-\s]", "", text, flags=re.UNICODE)
    return text.replace(" ", "-")


def inline_links(line: str):
    """Yield Markdown destinations, including balanced parentheses and titles."""
    # Inline code cannot contain active links. Preserve text outside it.
    line = re.sub(r"(`+).*?\1", "", line)
    position = 0
    while True:
        match = re.search(r"(?<!\\)\]\(", line[position:])
        if not match:
            return
        start = position + match.end()
        cursor = start
        while cursor < len(line) and line[cursor].isspace():
            cursor += 1
        if cursor < len(line) and line[cursor] == "<":
            end = line.find(">", cursor + 1)
            if end < 0:
                return
            yield line[cursor + 1:end]
            position = end + 1
            continue
        destination = []
        depth = 0
        while cursor < len(line):
            char = line[cursor]
            if char == "\\" and cursor + 1 < len(line):
                destination.append(line[cursor + 1])
                cursor += 2
                continue
            if char == "(":
                depth += 1
            elif char == ")":
                if depth == 0:
                    break
                depth -= 1
            elif char.isspace() and depth == 0:
                break
            destination.append(char)
            cursor += 1
        if destination:
            yield "".join(destination)
        position = max(cursor + 1, start + 1)


def parse_markdown(path: Path) -> Markdown:
    text = path.read_text(encoding="utf-8")
    visible = without_code(text)
    anchors = Counter(EXPLICIT_ANCHOR.findall(visible))
    slug_counts = Counter()
    lines = visible.splitlines()
    links = []
    for index, line in enumerate(lines):
        atx = re.match(r"^ {0,3}#{1,6}\s+(.+?)\s*#*\s*$", line)
        setext = index + 1 < len(lines) and re.fullmatch(r" {0,3}(?:=+|-+)\s*", lines[index + 1])
        heading = atx[1] if atx else line.strip() if setext and line.strip() else None
        if heading:
            slug = heading_slug(heading)
            count = slug_counts[slug]
            anchors[slug if not count else f"{slug}-{count}"] += 1
            slug_counts[slug] += 1
        links.extend((index + 1, item) for item in inline_links(line))
        reference = re.match(r"^ {0,3}\[[^\]]+\]:\s*(?:<([^>]+)>|(\S+))", line)
        if reference:
            links.append((index + 1, reference[1] or reference[2]))
        # Also check explicit relative HTML href/src links.
        links.extend((index + 1, item) for item in re.findall(r"\b(?:href|src)=['\"]([^'\"]+)['\"]", line))
    return Markdown(path, text, visible, anchors, links)


class Validator:
    def __init__(self, root: Path, registry: Path):
        self.root = root.resolve()
        self.directory = self.root / "docs/game-core"
        self.registry = registry.resolve()
        self.documents: dict[Path, Markdown] = {}
        self.errors: list[str] = []

    def error(self, message: str):
        self.errors.append(message)

    def label(self, path: Path) -> str:
        try:
            return str(path.relative_to(self.root))
        except ValueError:
            return str(path)

    def document(self, path: Path) -> Markdown | None:
        path = path.resolve()
        if path not in self.documents:
            try:
                self.documents[path] = parse_markdown(path)
            except (OSError, UnicodeError) as error:
                self.error(f"{self.label(path)}: cannot read Markdown: {error}")
                return None
        return self.documents[path]

    def target(self, source: Path, destination: str, location: str, require_anchor=False) -> tuple[Path, str] | None:
        try:
            url = urlsplit(html.unescape(destination))
        except ValueError as error:
            self.error(f"{location}: malformed link {destination!r}: {error}")
            return None
        if url.scheme or url.netloc or destination.startswith("//"):
            if require_anchor:
                self.error(f"{location}: registry source must be a local Markdown anchor")
            return None
        path = (source.parent / unquote(url.path)).resolve() if url.path else source.resolve()
        fragment = unquote(url.fragment)
        if not path.exists():
            self.error(f"{location}: missing local target {destination!r}")
            return None
        if require_anchor and (path.suffix.lower() != ".md" or not fragment):
            self.error(f"{location}: registry source needs a Markdown file and explicit anchor")
            return None
        if fragment and path.suffix.lower() == ".md":
            document = self.document(path)
            if document and fragment not in document.anchors:
                self.error(f"{location}: missing anchor #{fragment} in {self.label(path)}")
        return path, fragment

    def check_documents(self):
        for name in REQUIRED_DOCS:
            path = self.directory / name
            if not path.is_file():
                self.error(f"Missing required document: {self.label(path)}")
        for path in sorted(self.directory.rglob("*.md")):
            self.document(path)
        for document in list(self.documents.values()):
            for anchor, count in document.anchors.items():
                if count > 1:
                    self.error(f"{self.label(document.path)}: duplicate anchor #{anchor} ({count})")
            for line, link in document.links:
                self.target(document.path, link, f"{self.label(document.path)}:{line}")

    def load_registry(self) -> dict | None:
        try:
            data = json.loads(self.registry.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError) as error:
            self.error(f"Cannot read traceability registry {self.label(self.registry)}: {error}")
            return None
        if not isinstance(data, dict):
            self.error("Traceability registry root must be an object")
            return None
        return data

    def check_registry(self):
        data = self.load_registry()
        if data is None:
            return
        groups = {}
        source_documents = {
            "requirements": "00-core-protocols.md",
            "tasks": "09-implementation-guide.md",
            "tests": "08-validation-and-performance.md",
        }
        for group, prefix in (("requirements", "P"), ("tasks", "GC"), ("tests", "TEST")):
            entries = data.get(group)
            if not isinstance(entries, list) or not entries:
                self.error(f"Registry {group} must be a nonempty array")
                groups[group] = {}
                continue
            records = {}
            for record in entries:
                if not isinstance(record, dict) or not isinstance(record.get("id"), str):
                    self.error(f"Registry {group} entry must be an object with a string id")
                    continue
                identifier = record["id"]
                if not re.fullmatch(rf"{prefix}-\d{{3}}", identifier):
                    self.error(f"Invalid {group} id: {identifier}")
                if identifier in records:
                    self.error(f"Duplicate registry definition: {identifier}")
                    continue
                records[identifier] = record
                source = record.get("source")
                if not isinstance(source, str):
                    self.error(f"{identifier}: source must be a local document#anchor string")
                    continue
                target = self.target(self.registry, source, identifier, require_anchor=True)
                if target and target[1] != identifier.lower():
                    self.error(f"{identifier}: source anchor must be #{identifier.lower()}")
                if target and target[0] != self.directory / source_documents[group]:
                    self.error(f"{identifier}: defining source must be {source_documents[group]}")
            groups[group] = records
        requirements, tasks, tests = (groups[name] for name in ("requirements", "tasks", "tests"))
        wave_records = data.get("waves")
        waves = {}
        if not isinstance(wave_records, list) or not wave_records:
            self.error("Registry waves must be a nonempty array")
        else:
            for record in wave_records:
                if not isinstance(record, dict):
                    self.error("Each wave must be an object")
                    continue
                identifier = record.get("id")
                if not isinstance(identifier, int) or isinstance(identifier, bool) or identifier < 0:
                    self.error("Wave id must be a nonnegative integer")
                    continue
                if identifier in waves:
                    self.error(f"Duplicate wave {identifier}")
                waves[identifier] = record
                for field in ("entry", "exit"):
                    if not isinstance(record.get(field), str) or not record[field].strip():
                        self.error(f"Wave {identifier}: {field} gate must be a nonempty string")
            if sorted(waves) != list(range(len(waves))):
                self.error("Waves must be contiguous and start at 0")
        known_ids = set(requirements) | set(tasks) | set(tests)
        for document in list(self.documents.values()):
            for identifier in set(ID_PATTERN.findall(document.visible)):
                if identifier not in known_ids:
                    self.error(f"{self.label(document.path)}: unregistered ID {identifier}")
        for identifier in known_ids:
            definitions = sum(document.anchors[identifier.lower()] for document in self.documents.values())
            if definitions != 1:
                self.error(f"{identifier}: expected exactly one defining anchor, found {definitions}")

        def refs(record, field, candidates, nonempty=True):
            values = record.get(field)
            if not isinstance(values, list) or any(not isinstance(value, str) for value in values):
                self.error(f"{record['id']}: {field} must be a string array")
                return []
            if nonempty and not values:
                self.error(f"{record['id']}: {field} must not be empty")
            for value, count in Counter(values).items():
                if count > 1:
                    self.error(f"{record['id']}: duplicate {field} reference {value}")
                if value not in candidates:
                    self.error(f"{record['id']}: unknown {field} reference {value}")
            return values

        requirement_tasks = Counter()
        requirement_tests = Counter()
        test_tasks = Counter()
        edges = {}
        wave_tasks = Counter()
        test_requirements = {}
        for identifier, record in tests.items():
            test_requirements[identifier] = refs(record, "requirements", requirements)
            requirement_tests.update(test_requirements[identifier])
        for identifier, record in tasks.items():
            task_requirements = refs(record, "requirements", requirements)
            requirement_tasks.update(task_requirements)
            task_tests = refs(record, "tests", tests)
            test_tasks.update(task_tests)
            tested_requirements = {
                requirement
                for test in task_tests
                for requirement in test_requirements.get(test, [])
            }
            for requirement in task_requirements:
                if requirement not in tested_requirements:
                    self.error(f"{identifier}: {requirement} is not covered by this task's tests")
            edges[identifier] = refs(record, "depends_on", tasks, nonempty=False)
            wave = record.get("wave")
            if not isinstance(wave, int) or isinstance(wave, bool) or wave < 0:
                self.error(f"{identifier}: wave must be a nonnegative integer")
                continue
            if wave not in waves:
                self.error(f"{identifier}: unknown wave {wave}")
            wave_tasks[wave] += 1
            for dependency in edges[identifier]:
                dependency_wave = tasks.get(dependency, {}).get("wave")
                if isinstance(dependency_wave, int) and dependency_wave >= wave:
                    self.error(f"{identifier}: dependency {dependency} must be in an earlier wave ({dependency_wave} >= {wave})")
        for identifier in requirements:
            if not requirement_tasks[identifier]:
                self.error(f"{identifier}: no implementing task")
            if not requirement_tests[identifier]:
                self.error(f"{identifier}: no acceptance test")
        for identifier in tests:
            if not test_tasks[identifier]:
                self.error(f"{identifier}: no task executes this acceptance test")
        for wave in waves:
            if not wave_tasks[wave]:
                self.error(f"Wave {wave}: no tasks")
        self.check_dag(edges)
        self.check_task_metadata(tasks)
        self.check_test_metadata(test_requirements)

    def check_test_metadata(self, tests: dict):
        path = self.directory / "08-validation-and-performance.md"
        if not path.is_file():
            return
        document = self.document(path)
        if document is None:
            return
        current = None
        metadata = {}
        for line in document.visible.splitlines():
            heading = re.match(r"^#{1,6}\s+(TEST-\d{3})\b", line)
            if heading:
                current = heading[1]
            if line.startswith("Protocol coverage:"):
                if not current:
                    self.error("Test coverage metadata appears before a test heading")
                    continue
                if current in metadata:
                    self.error(f"{current}: duplicate Markdown protocol coverage")
                metadata[current] = re.findall(r"\bP-\d{3}\b", line)
        for identifier, requirements in tests.items():
            if identifier not in metadata:
                self.error(f"{identifier}: missing Markdown 'Protocol coverage:' line")
            elif metadata[identifier] != requirements:
                self.error(f"{identifier}: Markdown requirement coverage disagrees with traceability.json")

    def check_task_metadata(self, tasks: dict):
        path = self.directory / "09-implementation-guide.md"
        if not path.is_file():
            return
        document = self.document(path)
        if document is None:
            return
        current = None
        metadata = {}
        for line_number, line in enumerate(document.visible.splitlines(), 1):
            definition = re.search(r'<a\s+id=[\'\"](gc-\d{3})[\'\"]', line, re.IGNORECASE)
            heading = re.match(r"^#{1,6}\s+(GC-\d{3})\b", line)
            if definition or heading:
                current = (definition[1] if definition else heading[1]).upper()
            match = re.fullmatch(r"\s*Wave:\s*(\d+);\s*Depends:\s*(.*?)\s*", line)
            if match:
                if not current:
                    self.error(f"{self.label(path)}:{line_number}: task metadata has no defining task")
                    continue
                if current in metadata:
                    self.error(f"{current}: duplicate Markdown task metadata")
                depends = [] if match[2] == "none" else [item.strip() for item in match[2].split(",")]
                metadata[current] = (int(match[1]), depends)
        for identifier, task in tasks.items():
            if identifier not in metadata:
                self.error(f"{identifier}: missing Markdown metadata 'Wave: N; Depends: ids or none'")
            elif metadata[identifier] != (task.get("wave"), task.get("depends_on")):
                self.error(f"{identifier}: Markdown wave/dependencies disagree with traceability.json")
        for identifier in metadata.keys() - tasks.keys():
            self.error(f"{identifier}: Markdown task is not registered")

    def check_dag(self, edges: dict[str, list[str]]):
        visited = set()
        active = []

        def visit(identifier):
            if identifier in active:
                cycle = active[active.index(identifier):] + [identifier]
                self.error("Task dependency cycle: " + " -> ".join(cycle))
                return
            if identifier in visited:
                return
            active.append(identifier)
            for dependency in edges.get(identifier, []):
                visit(dependency)
            active.pop()
            visited.add(identifier)

        for identifier in edges:
            visit(identifier)

    def run(self) -> int:
        self.check_documents()
        self.check_registry()
        if self.errors:
            print(f"Game Core documentation validation FAILED ({len(self.errors)} errors):", file=sys.stderr)
            for error in self.errors:
                print(f"  - {error}", file=sys.stderr)
            return 1
        print(f"Game Core documentation validation passed: {len(self.documents)} Markdown documents; local links, anchors, IDs, traceability, task DAG and wave ordering checked.")
        print("No Unity builds, gameplay tests, runtime benchmarks, or remote URL checks were executed.")
        return 0


def self_test() -> int:
    """Exercise genuine failures against independent temporary documentation."""
    failures = []
    cases = (
        ("valid fixture", None, None),
        ("missing file", "[broken](missing.md)\n", "missing local target"),
        ("missing anchor", "[broken](00-core-protocols.md#absent)\n", "missing anchor"),
        ("dangling ID", "P-999\n", "unregistered ID"),
        ("duplicate anchor", '<a id="p-001"></a>\n', "defining anchor"),
        ("dependency cycle", "cycle", "Task dependency cycle"),
        ("invalid wave ordering", "wave", "must be in an earlier wave"),
        ("Markdown graph drift", "metadata", "Markdown wave/dependencies disagree"),
        ("uncovered requirement", "coverage", "no acceptance test"),
    )
    for name, mutation, expected in cases:
        with tempfile.TemporaryDirectory(prefix="game-core-docs-check-") as temporary:
            root = Path(temporary)
            directory = root / "docs/game-core"
            directory.mkdir(parents=True)
            for document in REQUIRED_DOCS:
                (directory / document).write_text("# Fixture\n", encoding="utf-8")
            (directory / "00-core-protocols.md").write_text('<a id="p-001"></a>\n## Requirement\n', encoding="utf-8")
            (directory / "08-validation-and-performance.md").write_text('<a id="test-001"></a>\n## TEST-001 — Test\nProtocol coverage: [P-001](00-core-protocols.md#p-001).\n', encoding="utf-8")
            guide = '<a id="gc-001"></a>\n## Task\nWave: 0; Depends: none\n<a id="gc-002"></a>\n## Next task\nWave: 1; Depends: GC-001\n'
            registry = {
                "requirements": [{"id": "P-001", "source": "00-core-protocols.md#p-001"}],
                "tests": [{"id": "TEST-001", "source": "08-validation-and-performance.md#test-001", "requirements": ["P-001"]}],
                "tasks": [
                    {"id": "GC-001", "source": "09-implementation-guide.md#gc-001", "wave": 0, "depends_on": [], "requirements": ["P-001"], "tests": ["TEST-001"]},
                    {"id": "GC-002", "source": "09-implementation-guide.md#gc-002", "wave": 1, "depends_on": ["GC-001"], "requirements": ["P-001"], "tests": ["TEST-001"]},
                ],
                "waves": [{"id": 0, "entry": "Start", "exit": "First gate"}, {"id": 1, "entry": "First gate", "exit": "Final gate"}],
            }
            if mutation == "cycle":
                registry["tasks"][0]["depends_on"] = ["GC-002"]
            elif mutation == "wave":
                registry["tasks"][1]["wave"] = 0
            elif mutation == "metadata":
                guide = guide.replace("Wave: 1;", "Wave: 2;")
            elif mutation == "coverage":
                registry["tests"][0]["requirements"] = []
            elif mutation:
                with (directory / "README.md").open("a", encoding="utf-8") as stream:
                    stream.write(mutation)
            (directory / "09-implementation-guide.md").write_text(guide, encoding="utf-8")
            registry_path = directory / "traceability.json"
            registry_path.write_text(json.dumps(registry), encoding="utf-8")
            validator = Validator(root, registry_path)
            validator.check_documents()
            validator.check_registry()
            if expected is None and validator.errors:
                failures.append(f"{name}: unexpected errors {validator.errors}")
            elif expected is not None and not any(expected in error for error in validator.errors):
                failures.append(f"{name}: expected {expected!r}, got {validator.errors}")
    if failures:
        print("Documentation validator self-test FAILED:\n" + "\n".join(failures), file=sys.stderr)
        return 1
    print(f"Documentation validator self-test passed: {len(cases)} isolated positive/negative fixtures.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1], help="Repository root")
    parser.add_argument("--traceability", type=Path, help="Registry path; default: ROOT/docs/game-core/traceability.json")
    parser.add_argument("--self-test", action="store_true", help="Test validator behavior using temporary fixtures")
    args = parser.parse_args()
    if args.self_test:
        return self_test()
    root = args.root.resolve()
    registry = args.traceability or root / "docs/game-core/traceability.json"
    return Validator(root, registry).run()


if __name__ == "__main__":
    raise SystemExit(main())
