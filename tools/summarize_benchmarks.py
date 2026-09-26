#!/usr/bin/env python3
"""Summarise the raw per-workload sample documents of the GC-026 benchmark and compare them to 08's budget rows.

Normative sources, and the reason each one is read rather than re-invented:

  * docs/game-core/08-validation-and-performance.md, "Instrumentation and reproducible performance method" and
    "Provisional budgets and failure decisions": the method (a deterministic fixture with a recorded seed and shape;
    30 s warmup; five independent 120-second runs for steady workloads; at least 1,000 repetitions for small
    composition changes; per-sample p50/p95/p99/max and total counts; memory categories reported separately) and the
    provisional target table, including its own preface ("initial engineering targets, not measured results or
    universal shipping requirements").
  * tests/GameCore.Benchmarks/Runtime/PerformanceBudgets.cs: the same table row for row (id, workload, phase, metric,
    target, unit, reference sentence, report-only flag). `BUDGETS` below encodes that file; a row whose measurement is
    absent is NotMeasured, never a pass.
  * tests/GameCore.Benchmarks/Runtime/BenchmarkWorkload.cs: the workload catalogue and its report order.
  * tests/GameCore.Benchmarks/Runtime/BenchmarkDocumentWriter.cs: the raw document shape and the CSV column order
    (the counter wire names below are the schema's own id order).
  * tests/GameCore.Benchmarks/Runtime/BenchmarkStatistics.cs: the one percentile definition.
  * Packages/com.gamecore.contracts/Runtime/Telemetry/TelemetrySchema.cs: the counter wire names and which counters
    are gauges (aggregated by max) rather than additive (summed).

What this tool is not: it is not a measurement. It reads what a player run wrote, pools the samples the way 08 says to
pool them (never an average of per-run percentiles), and reports a verdict. Everything it prints is recomputable from
the raw files it lists in its Evidence section.

Percentile definition (identical to BenchmarkStatistics.PercentileOfSorted): sort ascending, index
`ceil(p/100 * N) - 1`, clamped into `[0, N-1]`; p <= 0 returns the minimum and p >= 100 the maximum. Pooling is done
by concatenating every run's per-phase `samples[*].microseconds` and re-deriving; per-run percentiles are never
averaged, because an average of p95s is not a p95.

Exit codes:
  0  every raw document parsed, every correctness gate passed, no budget row MissedTarget (or --expect-miss given);
  1  any gate failed or any budget row MissedTarget;
  2  no raw input found, or a raw document is malformed JSON.

Usage:
  python3 tools/summarize_benchmarks.py --raw artifacts/performance/raw --out artifacts/performance/summary.md \
      --decisions artifacts/performance/BUDGET_DECISIONS.md --machine "$(hostname)" \
      --json artifacts/performance/summarize.json
  python3 tools/summarize_benchmarks.py --self-test
"""

from __future__ import annotations

import argparse
import json
import math
import os
import platform
import re
import sys
import tempfile
from dataclasses import dataclass, field
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]

# ---------------------------------------------------------------------------------------------------------------------
# The workload catalogue (BenchmarkWorkload.cs, report order). `kind` is Steady or Change; a steady workload is
# measured for a wall-clock duration and a change workload over a repetition count, which is why the two are never
# conflated in the tables below.
# ---------------------------------------------------------------------------------------------------------------------


@dataclass(frozen=True)
class Workload:
    id: str
    kind: str
    dimension: str
    default_repetitions: int


WORKLOADS: tuple[Workload, ...] = (
    Workload("idle-command-world", "Steady", "idle", 0),
    Workload("steady-unchanged-10000-steps", "Steady", "no-composition-change", 0),
    Workload("steady-execution-10000-targets", "Steady", "core-execution", 0),
    Workload("update-size-1", "Change", "update-size", 1000),
    Workload("update-size-100", "Change", "update-size", 1000),
    Workload("update-size-10000", "Change", "update-size", 200),
    Workload("whole-world-mode-switch", "Change", "mode-switch", 200),
    Workload("spawn-1000", "Change", "future-spawn", 200),
    Workload("reparent-100", "Change", "reparent", 1000),
    Workload("inactive-target-comparison", "Change", "inactive-targets", 1000),
    Workload("lifecycle-cycles-1000", "Change", "lifecycle", 1000),
)
WORKLOAD_ORDER: dict[str, int] = {workload.id: index for index, workload in enumerate(WORKLOADS)}
WORKLOAD_BY_ID: dict[str, Workload] = {workload.id: workload for workload in WORKLOADS}

# Phase report order (BenchmarkDocumentWriter.PhaseOrder).
PHASES: tuple[str, ...] = ("Warmup", "Prepare", "Wait", "Apply", "EndToEnd", "Step", "Change")

# The counter wire names in schema id order (TelemetrySchema.Name, ids 0..28). The CSV header of every raw document
# uses exactly this order, so a renamed counter changes a header and never a column position.
COUNTER_ORDER: tuple[str, ...] = (
    "control-nodes-visited",
    "candidates-matched",
    "contributions-added",
    "contributions-retracted",
    "strata-evaluated",
    "plan-prepared-bytes",
    "apply-us",
    "assembly-epoch",
    "steps-advanced",
    "service-string-lookups",
    "stage-us",
    "stage-samples",
    "job-wait-us",
    "job-wait-samples",
    "structural-operations",
    "request-high-water",
    "request-overflow",
    "stale-results",
    "live-leases",
    "outstanding-callbacks",
    "retained-event-bytes",
    "retained-event-count",
    "quarantine-bytes",
    "quarantine-entries",
    "lease-bytes",
    "cache-bytes",
    "cache-entries",
    "discarded-callbacks",
    "apply-samples",
)

# Counters the schema aggregates by max (a gauge) rather than by sum (TelemetrySchema.AggregationOf). Pooling a gauge
# across runs by summing it would invent a number no run observed.
GAUGE_COUNTERS: frozenset[str] = frozenset(
    {
        "assembly-epoch",
        "request-high-water",
        "live-leases",
        "outstanding-callbacks",
        "retained-event-bytes",
        "retained-event-count",
        "quarantine-bytes",
        "quarantine-entries",
        "lease-bytes",
        "cache-bytes",
        "cache-entries",
    }
)

# The counters 08 names in its instrumentation sentence; a workload's table lists these even when the delta is zero,
# because "control-nodes-visited=0" is the claim TEST-023 makes.
HEADLINE_COUNTERS: frozenset[str] = frozenset(
    {
        "control-nodes-visited",
        "candidates-matched",
        "contributions-added",
        "contributions-retracted",
        "strata-evaluated",
        "plan-prepared-bytes",
        "apply-us",
        "assembly-epoch",
        "steps-advanced",
        "service-string-lookups",
        "stage-us",
        "stage-samples",
        "job-wait-us",
        "job-wait-samples",
        "structural-operations",
        "request-high-water",
        "request-overflow",
        "stale-results",
        "live-leases",
        "outstanding-callbacks",
        "retained-event-bytes",
        "quarantine-bytes",
    }
)

# The memory categories of BenchmarkMemoryCategories, each reported on its own line (08: never an aggregate process
# total alone). `key` is the JSON field; `entry_key` names a companion count when the category has one.
MEMORY_CATEGORIES: tuple[tuple[str, str, str | None, str], ...] = (
    ("managed heap start", "managedHeapStartBytes", None, "process total, observation only"),
    ("managed heap end", "managedHeapEndBytes", None, "process total, observation only"),
    ("managed allocated", "managedAllocatedBytes", None, "covered threads; thread completeness stated below"),
    ("native containers", "nativeContainerBytes", None, "the world's own resource ledger"),
    ("lease bytes", "leaseBytes", None, "live staged/asset leases"),
    ("retained events", "retainedEventBytes", "retainedEventCount", "committed events still retained"),
    ("cache", "cacheBytes", "cacheEntries", "retained derived caches"),
    ("quarantine", "quarantineBytes", "quarantineEntries", "unfinished work still reaches these"),
    ("live leases", "liveLeases", None, "count"),
    ("outstanding callbacks", "outstandingCallbacks", None, "live activations plus tracked jobs"),
)

# ---------------------------------------------------------------------------------------------------------------------
# The 08 budget table (PerformanceBudgets.cs, row for row). `source` says where the measured number comes from:
#   phase:<metric>     one pooled per-phase distribution (p50/p95/p99/max/mean/count)
#   counter:<wire>     the workload-level counter delta (the document's `counters` object)
#   derived:<metric>   computed from the document's memory block and steps (managed-bytes-per-step)
# ---------------------------------------------------------------------------------------------------------------------


@dataclass(frozen=True)
class BudgetRow:
    id: str
    workload: str
    phase: str
    metric: str
    source: str
    target: float | None
    unit: str
    reference: str
    included: str
    report_only: bool

    def describe_target(self) -> str:
        if self.report_only:
            return "report-only: establish a baseline (no target to compare against)"
        if self.unit == "us":
            return "at most " + fmt_number(self.target) + " us"
        if self.unit == "bytes":
            return "at most " + fmt_number(self.target) + " bytes"
        return "exactly " + fmt_number(self.target)


BUDGETS: tuple[BudgetRow, ...] = (
    BudgetRow(
        "budget.execution-p95",
        "steady-execution-10000-targets",
        "Step",
        "p95",
        "phase:p95",
        4000.0,
        "us",
        "10,000 integer-rule targets, 1,000 active commands per fixed step: Core execution p95 at most 4 ms.",
        "Scheduling, queries, request arbitration, commit; report native physics/render/audio separately.",
        False,
    ),
    BudgetRow(
        "budget.execution-managed-bytes",
        "steady-execution-10000-targets",
        "Step",
        "managed-bytes-per-step",
        "derived:managed-bytes-per-step",
        0.0,
        "bytes",
        "Stable execution after warmup: 0 managed bytes per logical step in the kernel hot path.",
        "Include worker work; report explicitly enabled diagnostics or plugin allocations separately.",
        False,
    ),
    BudgetRow(
        "budget.unchanged-control-nodes",
        "steady-unchanged-10000-steps",
        "Step",
        "control-nodes-visited",
        "counter:control-nodes-visited",
        0.0,
        "count",
        "No composition changes over 10,000 steps: 0 control-tree visits.",
        "Counters count control work even if a traversal is cached or returns no matches.",
        False,
    ),
    BudgetRow(
        "budget.unchanged-service-lookups",
        "steady-unchanged-10000-steps",
        "Step",
        "service-string-lookups",
        "counter:service-string-lookups",
        0.0,
        "count",
        "No composition changes over 10,000 steps: 0 string service resolutions.",
        "Counters count control work even if a traversal is cached or returns no matches.",
        False,
    ),
    BudgetRow(
        "budget.apply-pause-p95",
        "update-size-100",
        "Apply",
        "p95",
        "phase:p95",
        2000.0,
        "us",
        "Valid plan affecting 100 existing targets: Apply pause p95 at most 2 ms.",
        "Include fencing time in a separate mandatory wait metric; report preparation and end-to-end latency.",
        False,
    ),
    BudgetRow(
        "budget.whole-world-preparation-p95",
        "update-size-10000",
        "Prepare",
        "p95",
        "phase:p95",
        100000.0,
        "us",
        "Whole-world derivation for 10,000 targets: Preparation p95 at most 100 ms.",
        "Full closure/index work; no partial publication to meet the number.",
        False,
    ),
    BudgetRow(
        "budget.spawn-baseline",
        "spawn-1000",
        "Prepare",
        "p95",
        "phase:p95",
        None,
        "us",
        "1,000-target spawn under active capabilities: report prepare/apply p95, native/managed bytes and recipe "
        "reuse; establish a baseline.",
        "No throughput claim until the actual archetype/state footprint is measured.",
        True,
    ),
    BudgetRow(
        "budget.lifecycle-plateau",
        "lifecycle-cycles-1000",
        "Change",
        "p99",
        "phase:p99",
        None,
        "us",
        "1,000 lifecycle cycles with fixed retained data: active counts return to baseline; bounded "
        "cache/native/managed growth plateaus.",
        "Report asset policy, retained events and quarantine separately.",
        True,
    ),
    BudgetRow(
        "budget.idle-steps",
        "idle-command-world",
        "Step",
        "steps-advanced",
        "counter:steps-advanced",
        0.0,
        "count",
        "Idle command-driven world: 0 simulation steps.",
        "Host presentation and pending control-plane operations may still run.",
        False,
    ),
    BudgetRow(
        "budget.idle-stage-updates",
        "idle-command-world",
        "Step",
        "stage-samples",
        "counter:stage-samples",
        0.0,
        "count",
        "Idle command-driven world: 0 simulation-stage updates.",
        "Host presentation and pending control-plane operations may still run.",
        False,
    ),
)
BUDGET_BY_ID: dict[str, BudgetRow] = {row.id: row for row in BUDGETS}

# The report order of the budget table's workloads, so a summary's per-workload sections follow the catalogue.
WORKLOAD_COUNTERS_USED: dict[str, set[str]] = {}
for _row in BUDGETS:
    if _row.source.startswith("counter:"):
        WORKLOAD_COUNTERS_USED.setdefault(_row.workload, set()).add(_row.source.split(":", 1)[1])

VERDICT_NOT_MEASURED = "NotMeasured"
VERDICT_WITHIN = "WithinTarget"
VERDICT_MISSED = "MissedTarget"
VERDICT_REPORT_ONLY = "ReportOnly"


# ---------------------------------------------------------------------------------------------------------------------
# Formatting helpers. Deterministic by construction: no timestamps, no locale-dependent formats.
# ---------------------------------------------------------------------------------------------------------------------


def fmt_number(value: float | None) -> str:
    """One number the way the summary prints it: integers bare, fractions at most three decimals."""
    if value is None:
        return VERDICT_NOT_MEASURED
    if abs(value - round(value)) < 1e-9:
        return str(int(round(value)))
    return f"{value:.3f}".rstrip("0").rstrip(".")


def fmt_us(value: float | None) -> str:
    return VERDICT_NOT_MEASURED if value is None else fmt_number(value) + " us"


def sort_key_run(name: str) -> tuple:
    """Natural order for run directory names, so run2 sorts before run10."""
    return tuple(int(part) if part.isdigit() else part for part in re.split(r"(\d+)", name))


# ---------------------------------------------------------------------------------------------------------------------
# Percentiles and distributions (BenchmarkStatistics.cs, one definition).
# ---------------------------------------------------------------------------------------------------------------------


def percentile_of_sorted(sorted_values: list[int], percentile: float) -> int:
    """Nearest-rank percentile of an ascending list: index ceil(p/100 * N) - 1, clamped. p<=0 -> min, p>=100 -> max."""
    count = len(sorted_values)
    if count == 0:
        return 0
    if percentile <= 0.0:
        return sorted_values[0]
    if percentile >= 100.0:
        return sorted_values[-1]
    rank = math.ceil(percentile / 100.0 * count)
    index = int(rank) - 1
    if index < 0:
        index = 0
    if index >= count:
        index = count - 1
    return sorted_values[index]


@dataclass(frozen=True)
class Distribution:
    count: int
    minimum: int
    maximum: int
    mean: float
    total: int
    p50: int
    p95: int
    p99: int

    @staticmethod
    def of(values: list[int]) -> "Distribution":
        if not values:
            return Distribution(0, 0, 0, 0.0, 0, 0, 0, 0)
        ordered = sorted(values)
        total = sum(ordered)
        return Distribution(
            count=len(ordered),
            minimum=ordered[0],
            maximum=ordered[-1],
            mean=total / len(ordered),
            total=total,
            p50=percentile_of_sorted(ordered, 50.0),
            p95=percentile_of_sorted(ordered, 95.0),
            p99=percentile_of_sorted(ordered, 99.0),
        )

    def metric(self, name: str) -> float:
        return {
            "count": float(self.count),
            "min": float(self.minimum),
            "max": float(self.maximum),
            "mean": float(self.mean),
            "total": float(self.total),
            "p50": float(self.p50),
            "p95": float(self.p95),
            "p99": float(self.p99),
        }.get(name, float("nan"))


# ---------------------------------------------------------------------------------------------------------------------
# Raw document loading.
# ---------------------------------------------------------------------------------------------------------------------


class MalformedDocument(Exception):
    """A raw file that is not the JSON document the writer contract defines."""


# Historical defect, kept only as a fallback: an earlier `BenchmarkDocumentWriter.WriteJson` rendered the counters
# section through its generic array helper, which emitted `"counters": [ "name": value, ... ]` — object members inside
# square brackets. The frozen document contract (and the harness's strict-JSON validation) says `"counters": { ... }`,
# and the writer now hand-builds the object, so this shape is no longer produced. The repair accepts that one shape and
# nothing else, so an older raw document still summarises; `counters_repair_needed` is the predicate that says whether
# it applies, and every repaired file is listed in the summary rather than silently trusted.
_COUNTERS_ARRAY = re.compile(r'("counters"\s*:\s*)\[(.*?)\]', re.DOTALL)
_COUNTER_MEMBER = re.compile(r'^\s*"?(?P<name>[^":]+?)"?\s*:\s*(?P<value>-?\d+)\s*$')


def repair_counters_array(text: str) -> str:
    """Rewrites `"counters": [ "a": 1, "b": 2 ]` into `"counters": { "a": 1, "b": 2 }`; anything else is untouched."""

    def replace(match: re.Match) -> str:
        body = match.group(2)
        if body.lstrip().startswith("{"):
            return match.group(0)
        return match.group(1) + "{" + body + "}"

    return _COUNTERS_ARRAY.sub(replace, text, count=1)


def counters_repair_needed(text: str) -> bool:
    """True only when `text` is invalid JSON *because* its counters section is the legacy array rendering.

    A document whose `"counters"` is a real object — the primary shape the contract defines and the writer now emits —
    must be reported as needing no repair, so a regression back to array rendering fails the self-test instead of
    silently depending on the fallback.
    """
    if repair_counters_array(text) == text:
        return False
    try:
        json.loads(text)
    except ValueError:
        return True
    return False


def load_json_document(path: Path) -> dict:
    """Loads one raw document, repairing the known counters-array rendering before giving up."""
    try:
        text = path.read_text(encoding="utf-8")
    except OSError as exc:
        raise MalformedDocument(f"cannot read: {exc}") from exc
    try:
        data = json.loads(text)
    except ValueError as first_error:
        repaired = repair_counters_array(text)
        if repaired == text:
            raise MalformedDocument(f"not valid JSON: {first_error}") from first_error
        try:
            data = json.loads(repaired)
        except ValueError as second_error:
            raise MalformedDocument(
                f"not valid JSON even after repairing the counters array: {second_error}"
            ) from second_error
    if not isinstance(data, dict):
        raise MalformedDocument("top-level JSON value is not an object")
    return data


def normalise_counters(value: object) -> dict[str, int]:
    """Accepts the contract's object, and tolerates a list of `"name": value` members from the current writer."""
    if isinstance(value, dict):
        result: dict[str, int] = {}
        for key, raw in value.items():
            number = coerce_int(raw)
            if number is not None:
                result[str(key)] = number
        return result
    if isinstance(value, list):
        result = {}
        for item in value:
            if isinstance(item, dict):
                for key, raw in item.items():
                    number = coerce_int(raw)
                    if number is not None:
                        result[str(key)] = number
            elif isinstance(item, str):
                match = _COUNTER_MEMBER.match(item)
                if match is not None:
                    result[match.group("name")] = int(match.group("value"))
        return result
    return {}


def coerce_int(value: object) -> int | None:
    if isinstance(value, bool):
        return None
    if isinstance(value, int):
        return value
    if isinstance(value, float) and value.is_integer():
        return int(value)
    return None


@dataclass
class RawDocument:
    path: Path
    run: str
    data: dict


@dataclass
class WorkloadData:
    workload: Workload | None
    workload_id: str
    documents: list[RawDocument] = field(default_factory=list)
    phase_samples: dict[str, list[int]] = field(default_factory=dict)
    declared_phases: dict[str, dict] = field(default_factory=dict)
    counters: dict[str, int] = field(default_factory=dict)
    counters_reported: bool = False
    steps_advanced: int = 0
    managed_allocated: int = 0
    managed_thread_complete: bool = True
    memory_runs: list[dict] = field(default_factory=list)
    windows: list[tuple[str, int]] = field(default_factory=list)
    warmups: list[tuple[str, int]] = field(default_factory=list)
    gates: list[tuple[str, str, bool, str]] = field(default_factory=list)
    self_consistency: list[str] = field(default_factory=list)
    kind: str = "Unknown"
    dimension: str = ""

    @property
    def id(self) -> str:
        return self.workload_id


def build_workload_data(workload: Workload | None, workload_id: str, documents: list[RawDocument]) -> WorkloadData:
    data = WorkloadData(workload=workload, workload_id=workload_id, documents=documents)
    data.kind = documents[0].data.get("kind", workload.kind if workload else "Unknown")
    data.dimension = documents[0].data.get("dimension", workload.dimension if workload else "")

    additive: dict[str, int] = {}
    gauges: dict[str, int] = {}
    counters_reported = True
    steps_advanced = 0
    managed_allocated = 0
    managed_thread_complete = True
    all_passed = True

    for document in documents:
        raw = document.data

        # Every sample, so a percentile can be re-derived rather than trusted.
        samples = raw.get("samples")
        if isinstance(samples, list):
            for sample in samples:
                if not isinstance(sample, dict):
                    continue
                phase = sample.get("phase")
                microseconds = coerce_int(sample.get("microseconds"))
                if isinstance(phase, str) and microseconds is not None and microseconds >= 0:
                    data.phase_samples.setdefault(phase, []).append(microseconds)

        # The document's own declared distributions, kept only for the self-consistency audit.
        phases = raw.get("phases")
        if isinstance(phases, list):
            for entry in phases:
                if isinstance(entry, dict) and isinstance(entry.get("phase"), str):
                    data.declared_phases.setdefault(entry["phase"], entry)

        if "counters" not in raw:
            counters_reported = False
        else:
            for wire, value in normalise_counters(raw.get("counters")).items():
                if wire in GAUGE_COUNTERS:
                    gauges[wire] = max(gauges.get(wire, 0), value)
                else:
                    additive[wire] = additive.get(wire, 0) + value

        step_value = coerce_int(raw.get("stepsAdvanced"))
        if step_value is not None:
            steps_advanced += step_value

        memory = raw.get("memory")
        if isinstance(memory, dict):
            data.memory_runs.append(memory)
            allocated = coerce_int(memory.get("managedAllocatedBytes"))
            if allocated is not None:
                managed_allocated += allocated
            if memory.get("managedAllocationIsThreadComplete") is not True:
                managed_thread_complete = False
        else:
            data.memory_runs.append({})

        window = coerce_int(raw.get("windowMicroseconds"))
        data.windows.append((document.run, window if window is not None else 0))

        # 08 asks a warmup to be reported as the interval before the measured window, so the summarizer carries it
        # rather than dropping it: a workload whose warmup was cut short by its own cap must be visible in the summary.
        warmup = coerce_int(raw.get("warmupMicroseconds"))
        data.warmups.append((document.run, warmup if warmup is not None else 0))

        if raw.get("passed") is not True:
            all_passed = False

        gates = raw.get("gates")
        if isinstance(gates, list) and gates:
            for gate in gates:
                if isinstance(gate, dict):
                    data.gates.append(
                        (
                            document.run,
                            str(gate.get("name", "<unnamed>")),
                            gate.get("passed") is True,
                            str(gate.get("detail", "")),
                        )
                    )
        else:
            all_passed = False

        data.self_consistency.extend(audit_document(document))

    data.counters = dict(additive)
    for wire, value in gauges.items():
        data.counters[wire] = max(data.counters.get(wire, 0), value)
    data.counters_reported = counters_reported
    data.steps_advanced = steps_advanced if steps_advanced > 0 else additive.get("steps-advanced", 0)
    data.managed_allocated = managed_allocated
    data.managed_thread_complete = managed_thread_complete
    data.passed = all_passed and bool(data.gates)
    return data


def audit_document(document: RawDocument) -> list[str]:
    """Checks one document against itself: its declared per-phase distributions must equal its own samples."""
    problems: list[str] = []
    grouped: dict[str, list[int]] = {}
    samples = document.data.get("samples")
    if isinstance(samples, list):
        for sample in samples:
            if not isinstance(sample, dict):
                continue
            phase = sample.get("phase")
            microseconds = coerce_int(sample.get("microseconds"))
            if isinstance(phase, str) and microseconds is not None:
                grouped.setdefault(phase, []).append(microseconds)

    declared = {entry.get("phase"): entry for entry in document.data.get("phases", []) if isinstance(entry, dict)}
    for phase in sorted(set(grouped) | set(declared), key=lambda name: (PHASES.index(name) if name in PHASES else len(PHASES), name)):
        values = grouped.get(phase, [])
        entry = declared.get(phase)
        if entry is None:
            problems.append(f"{document.path}: phase {phase} has samples but no declared distribution")
            continue
        if not values:
            problems.append(f"{document.path}: phase {phase} is declared but has no samples")
            continue
        computed = Distribution.of(values)
        mismatches: list[str] = []
        for metric, expected in (
            ("count", computed.count),
            ("min", computed.minimum),
            ("p50", computed.p50),
            ("p95", computed.p95),
            ("p99", computed.p99),
            ("max", computed.maximum),
            ("total", computed.total),
        ):
            actual = coerce_int(entry.get(metric))
            if actual != expected:
                mismatches.append(f"{metric}: declares {entry.get(metric)}, samples give {expected}")
        declared_mean = entry.get("mean")
        if isinstance(declared_mean, (int, float)) and abs(float(declared_mean) - computed.mean) > 5e-4:
            mismatches.append(f"mean: declares {declared_mean}, samples give {computed.mean:.3f}")
        if mismatches:
            # One line per (document, phase): a defect in a phase is one finding, not seven.
            problems.append(f"{document.path}: phase {phase} disagrees with its own samples — " + "; ".join(mismatches))
    return problems


def workload_sort_key(workload_id: str) -> tuple:
    return (0, WORKLOAD_ORDER[workload_id]) if workload_id in WORKLOAD_ORDER else (1, workload_id)


# ---------------------------------------------------------------------------------------------------------------------
# The run context: hardware, configuration facts and resolved configuration.
# ---------------------------------------------------------------------------------------------------------------------


def parse_environment(text: str) -> dict[str, str]:
    environment: dict[str, str] = {}
    for line in text.splitlines():
        if ":" in line:
            key, value = line.split(":", 1)
            environment[key.strip()] = value.strip()
    return environment


def proc_cpuinfo_model() -> str | None:
    path = Path("/proc/cpuinfo")
    if not path.is_file():
        return None
    try:
        for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
            if line.lower().startswith("model name"):
                return line.split(":", 1)[1].strip()
    except OSError:
        return None
    return None


CONFIG_FACTS: tuple[str, ...] = (
    "task",
    "mode",
    "result",
    "unityVersion",
    "declaredUnityVersion",
    "declaredTarget",
    "platform",
    "architecture",
    "processorType",
    "scriptingBackend",
    "isIl2Cpp",
    "managedStrippingLevel",
    "burstCompilerEnabled",
    "catalogFingerprint",
    "catalogFileHash",
)


@dataclass
class RunContext:
    run_dirs: list[Path] = field(default_factory=list)
    probe_documents: dict[str, dict] = field(default_factory=dict)
    environment: dict[str, str] = field(default_factory=dict)
    environment_path: Path | None = None
    evidence: list[Path] = field(default_factory=list)
    repairs: list[str] = field(default_factory=list)


def collect_context(raw_roots: list[Path]) -> RunContext:
    context = RunContext()
    sample_paths: list[Path] = []
    for root in raw_roots:
        if root.is_dir():
            sample_paths.extend(sorted(root.rglob("*.samples.json")))
    run_dirs = sorted({path.parent for path in sample_paths}, key=lambda path: sort_key_run(path.name))
    context.run_dirs = run_dirs

    for run_dir in run_dirs:
        probe = run_dir / "probe-benchmark.json"
        if probe.is_file():
            try:
                loaded = json.loads(probe.read_text(encoding="utf-8"))
                context.probe_documents[run_dir.name] = loaded if isinstance(loaded, dict) else {}
            except (OSError, ValueError):
                context.probe_documents[run_dir.name] = {}
            context.evidence.append(probe)

    candidates: list[Path] = []
    for root in raw_roots:
        candidates.append(root / "environment.txt")
        candidates.append(root.parent / "environment.txt")
    for run_dir in run_dirs:
        candidates.append(run_dir / "environment.txt")
    for candidate in candidates:
        if candidate.is_file():
            try:
                context.environment = parse_environment(candidate.read_text(encoding="utf-8"))
            except OSError:
                continue
            context.environment_path = candidate
            context.evidence.append(candidate)
            break
    return context


def hardware_rows(machine: str, context: RunContext) -> list[tuple[str, str]]:
    rows: list[tuple[str, str]] = [("machine", machine or "<unset>")]
    environment = context.environment
    for key in ("host", "uname_m", "nproc", "cpu_model", "lsb_release", "date_utc", "player", "player_bytes", "player_sha256"):
        if key in environment:
            rows.append((key, environment[key]))
    if "cpu_model" not in environment:
        model = proc_cpuinfo_model()
        if model:
            rows.append(("cpu_model (/proc/cpuinfo)", model))
    if "nproc" not in environment:
        rows.append(("cpu_count (os.cpu_count)", str(os.cpu_count() or "<unknown>")))
    if not environment:
        rows.append(("platform", platform.platform()))
        rows.append(("uname_m", platform.machine()))
        rows.append(("processor", platform.processor() or "<unknown>"))
    return rows


def config_fact_rows(context: RunContext) -> list[tuple[str, str]]:
    rows: list[tuple[str, str]] = []
    names = sorted(context.probe_documents, key=sort_key_run)
    if not names:
        rows.append(("<probe result>", "not recorded: no probe-benchmark.json beside the raw sample documents"))
        return rows
    def render(value: object) -> str:
        # JSON booleans, not Python ones: the summary quotes the probe result, so `true` is what the document says.
        if value is True:
            return "true"
        if value is False:
            return "false"
        return str(value)

    for fact in CONFIG_FACTS:
        values = []
        for name in names:
            if fact in context.probe_documents[name]:
                values.append(render(context.probe_documents[name][fact]))
        if not values:
            rows.append((fact, "not recorded"))
        elif len(set(values)) == 1:
            rows.append((fact, values[0]))
        else:
            rows.append((fact, "DISAGREES ACROSS RUNS: " + ", ".join(sorted(set(values)))))
    return rows


def resolved_config_rows(by_workload: dict[str, WorkloadData]) -> list[tuple[str, str]]:
    def distinct(field_name: str, document_field: str) -> list[str]:
        values = set()
        for data in by_workload.values():
            for document in data.documents:
                value = document.data.get(document_field)
                if value is not None:
                    values.add(str(value))
        return sorted(values, key=lambda item: (0, int(item)) if item.lstrip("-").isdigit() else (1, item))

    rows: list[tuple[str, str]] = []
    rows.append(("run count", str(len({document.run for data in by_workload.values() for document in data.documents}))))
    rows.append(("workloads", ", ".join(sorted(by_workload, key=workload_sort_key))))
    for label, field_name in (
        ("scopes", "scopes"),
        ("targets", "targets"),
        ("warmupSeconds", "warmupSeconds"),
        ("durationSeconds", "durationSeconds"),
        ("seed", "seed"),
    ):
        values = distinct(label, field_name)
        rows.append((label, ", ".join(values) if values else "not recorded"))
    return rows


# ---------------------------------------------------------------------------------------------------------------------
# Budget measurement and verdicts.
# ---------------------------------------------------------------------------------------------------------------------


@dataclass
class Measurement:
    value: float | None
    detail: str


@dataclass
class BudgetResult:
    row: BudgetRow
    measurement: Measurement
    verdict: str


def measure_budget(row: BudgetRow, data: WorkloadData | None) -> BudgetResult:
    if data is None:
        return BudgetResult(
            row,
            Measurement(None, f"no raw sample document for workload '{row.workload}'"),
            VERDICT_NOT_MEASURED,
        )

    measurement: Measurement
    if row.source.startswith("phase:"):
        metric = row.source.split(":", 1)[1]
        values = data.phase_samples.get(row.phase, [])
        if not values:
            measurement = Measurement(
                None, f"workload '{row.workload}' has no samples in phase '{row.phase}'"
            )
        else:
            value = Distribution.of(values).metric(metric)
            measurement = Measurement(
                value,
                f"pooled {len(values)} sample(s) over {len(data.documents)} run(s); {metric} from the sample arrays",
            )
    elif row.source.startswith("counter:"):
        wire = row.source.split(":", 1)[1]
        if not data.counters_reported:
            measurement = Measurement(None, f"workload '{row.workload}' reported no counters object")
        else:
            value = float(data.counters.get(wire, 0))
            measurement = Measurement(
                value,
                f"counters['{wire}'] pooled delta over {len(data.documents)} run(s)"
                + (" (absent key means the writer recorded a zero)" if wire not in data.counters else ""),
            )
    elif row.source == "derived:managed-bytes-per-step":
        steps = data.steps_advanced
        if steps <= 0:
            measurement = Measurement(None, "no committed steps were recorded, so bytes-per-step is undefined")
        else:
            value = data.managed_allocated / steps
            if value == 0.0 and not data.managed_thread_complete:
                measurement = Measurement(
                    None,
                    "the managed allocation counter was not thread-complete, so a zero reading cannot prove "
                    "worker allocation is zero (08 section 3)",
                )
            else:
                measurement = Measurement(
                    value,
                    f"{data.managed_allocated} managed byte(s) over {steps} committed step(s); "
                    f"thread-complete={str(data.managed_thread_complete).lower()}",
                )
    else:
        measurement = Measurement(None, f"unknown budget source '{row.source}'")

    if measurement.value is None:
        verdict = VERDICT_NOT_MEASURED
    elif row.report_only:
        verdict = VERDICT_REPORT_ONLY
    elif measurement.value <= row.target + 1e-9:
        verdict = VERDICT_WITHIN
    else:
        verdict = VERDICT_MISSED
    return BudgetResult(row, measurement, verdict)


def gate_rows(by_workload: dict[str, WorkloadData]) -> list[tuple[str, str, int, str, str]]:
    """One row per (workload, gate name): verdict pass only when every run's gate passed."""
    rows: list[tuple[str, str, int, str, str]] = []
    for workload_id in sorted(by_workload, key=workload_sort_key):
        data = by_workload[workload_id]
        if not data.gates:
            rows.append((workload_id, "<no gate results reported>", len(data.documents), "FAIL", "the raw document carried no gates array"))
            continue
        order: list[str] = []
        grouped: dict[str, list[tuple[str, bool, str]]] = {}
        for run, name, passed, detail in data.gates:
            if name not in grouped:
                grouped[name] = []
                order.append(name)
            grouped[name].append((run, passed, detail))
        for name in order:
            results = grouped[name]
            ok = all(passed for _, passed, _ in results)
            detail = next((detail for _, passed, detail in results if not passed), results[0][2])
            rows.append((workload_id, name, len(results), "pass" if ok else "FAIL", detail))
    return rows

def measured_text(result: "BudgetResult") -> str:
    """One measured number with the unit the budget row is expressed in; NotMeasured when the run produced none."""
    if result.measurement.value is None:
        return VERDICT_NOT_MEASURED
    if result.row.unit == "us":
        return fmt_number(result.measurement.value) + " us"
    if result.row.unit == "bytes":
        return fmt_number(result.measurement.value) + " bytes"
    return fmt_number(result.measurement.value)


def markdown_table(headers: list[str], rows: list[list[str]]) -> list[str]:
    lines = ["| " + " | ".join(headers) + " |", "|" + "|".join(" --- " for _ in headers) + "|"]
    for row in rows:
        lines.append("| " + " | ".join(row) + " |")
    return lines



def render_summary(
    *,
    machine: str,
    context: RunContext,
    by_workload: dict[str, WorkloadData],
    budget_results: list[BudgetResult],
    gates: list[tuple[str, str, int, str, str]],
    self_consistency: list[str],
    expect_miss: bool,
) -> str:
    missed = [result for result in budget_results if result.verdict == VERDICT_MISSED]
    failed_gates = [row for row in gates if row[3] != "pass"]
    not_measured = [result for result in budget_results if result.verdict == VERDICT_NOT_MEASURED]

    verdict = "PASS"
    reasons: list[str] = []
    if failed_gates:
        verdict = "FAIL"
        reasons.append(f"{len(failed_gates)} correctness gate(s) failed")
    if missed:
        verdict = "FAIL" if not expect_miss else "PASS (misses accepted under --expect-miss)"
        reasons.append(f"{len(missed)} budget row(s) MissedTarget")
    if not reasons:
        reasons.append("every budget row is WithinTarget or ReportOnly and every gate passed")

    lines: list[str] = []
    lines.append("# GC-026 benchmark summary")
    lines.append("")
    lines.append(f"Verdict: **{verdict}** — " + "; ".join(reasons) + ".")
    lines.append("")
    lines.append(
        "Status: measured diagnostic evidence from the raw sample documents listed under Evidence. "
        "Check Resolved configuration and Wall-clock window per run before comparing against 08's full method; "
        "a short or partial run does not qualify the five-run gate."
    )
    lines.append("")
    hardware = dict(hardware_rows(machine, context))
    lines.append(
        "Hardware: "
        + ", ".join(f"{key}={value}" for key, value in hardware.items())
        + "."
    )
    lines.append("")

    lines.append("## Hardware")
    lines.append("")
    lines.extend(markdown_table(["fact", "value"], [[key, value] for key, value in hardware.items()]))
    lines.append("")

    lines.append("## Build and configuration facts")
    lines.append("")
    lines.append(
        "Every measurement must name its hardware and its build/config; these are the fields the probe player recorded "
        "in its result JSON (the same block every other probe mode in this repository writes)."
    )
    lines.append("")
    lines.extend(
        markdown_table(["fact", "value"], [[fact, value] for fact, value in config_fact_rows(context)])
    )
    lines.append("")

    lines.append("## Resolved configuration")
    lines.append("")
    lines.extend(
        markdown_table(["knob", "resolved value"], [[key, value] for key, value in resolved_config_rows(by_workload)])
    )
    lines.append("")

    lines.append("## Wall-clock window per run")
    lines.append("")
    window_rows: list[list[str]] = []
    for workload_id in sorted(by_workload, key=workload_sort_key):
        data = by_workload[workload_id]
        executed: list[str] = []
        requested = None
        for document in data.documents:
            requested = coerce_int(document.data.get("repetitionsRequested"))
            done = coerce_int(document.data.get("repetitionsExecuted"))
            executed.append(f"{document.run}={done if done is not None else '?'}")
        window_rows.append(
            [
                workload_id,
                data.kind,
                data.documents[0].data.get("warmupSeconds", "?").__str__(),
                data.documents[0].data.get("durationSeconds", "?").__str__(),
                str(requested) if requested is not None else "-",
                ", ".join(executed),
                ", ".join(f"{run}={window} us" for run, window in sorted(data.windows, key=lambda item: sort_key_run(item[0]))),
                ", ".join(
                    f"{run}={warmup} us"
                    for run, warmup in sorted(data.warmups, key=lambda item: sort_key_run(item[0]))
                ),
            ]
        )
    lines.extend(
        markdown_table(
            [
                "workload",
                "kind",
                "warmup s",
                "duration s",
                "repetitionsRequested",
                "repetitionsExecuted",
                "measured window per run",
                "warmup actually spent per run",
            ],
            window_rows,
        )
    )
    lines.append("")

    lines.append("## Per-workload phase distributions")
    lines.append("")
    lines.append(
        "Samples are pooled across every run and the percentiles are re-derived from the pooled sample array with the "
        "nearest-rank definition: sort ascending, index `ceil(p/100 * N) - 1`, clamped into `[0, N-1]`; p <= 0 returns "
        "the minimum and p >= 100 the maximum. Per-run percentiles are never averaged — an average of p95s is not a p95."
    )
    lines.append("")
    phase_rows: list[list[str]] = []
    for workload_id in sorted(by_workload, key=workload_sort_key):
        data = by_workload[workload_id]
        phases = [phase for phase in PHASES if data.phase_samples.get(phase)]
        extra = sorted(set(data.phase_samples) - set(PHASES))
        for phase in phases + extra:
            distribution = Distribution.of(data.phase_samples[phase])
            phase_rows.append(
                [
                    workload_id,
                    data.kind,
                    phase,
                    str(distribution.count),
                    fmt_number(distribution.p50),
                    fmt_number(distribution.p95),
                    fmt_number(distribution.p99),
                    fmt_number(distribution.maximum),
                    fmt_number(distribution.mean),
                    fmt_number(distribution.total),
                ]
            )
    if not phase_rows:
        phase_rows.append(["<none>", "-", "-", "0", "-", "-", "-", "-", "-", "-"])
    lines.extend(
        markdown_table(
            ["workload", "kind", "phase", "samples", "p50 us", "p95 us", "p99 us", "max us", "mean us", "total us"],
            phase_rows,
        )
    )
    lines.append("")

    lines.append("## Counter deltas")
    lines.append("")
    lines.append(
        "Workload-level totals from each document's `counters` object, pooled across runs with the schema's own "
        "aggregation policy (sum for an additive counter, max for a gauge). A counter absent from the object is a "
        "recorded zero; the correctness gates below pair a zero reading with a positive control so a zero caused by a "
        "compiled-out counter cannot pass as a measurement."
    )
    lines.append("")
    lines.append(
        "The 08-named counters appear as `name=delta`; a counter named by a budget row for that workload is always "
        "listed, zero included. Every other counter is listed only when it moved, so the table stays readable. The "
        "full column order of the raw CSV is: "
        + ", ".join(f"`{wire}`" for wire in COUNTER_ORDER)
        + "."
    )
    lines.append("")
    counter_rows: list[list[str]] = []
    for workload_id in sorted(by_workload, key=workload_sort_key):
        data = by_workload[workload_id]
        if not data.counters_reported:
            counter_rows.append([workload_id, "<no counters object>", "the raw document carried no counters object"])
            continue
        used = WORKLOAD_COUNTERS_USED.get(workload_id, set())
        listed = [
            wire
            for wire in COUNTER_ORDER
            if data.counters.get(wire, 0) != 0 or wire in used
        ]
        moved = [wire for wire in COUNTER_ORDER if data.counters.get(wire, 0) != 0]
        counter_rows.append(
            [
                workload_id,
                ", ".join(f"{wire}={data.counters.get(wire, 0)}" for wire in listed) or "<all listed counters are zero>",
                ", ".join(f"{wire}={data.counters[wire]}" for wire in moved) or "<none>",
            ]
        )
    if not counter_rows:
        counter_rows.append(["<none>", "-", "-"])
    lines.extend(markdown_table(["workload", "listed counters (name=delta)", "counters that moved"], counter_rows))
    lines.append("")


    lines.append("## Correctness gates")
    lines.append("")
    lines.append(
        "Asserted by the probe, not inferred here: zero stable control-tree scans, zero string service lookups, an "
        "idle world advancing zero steps, no duplicated authoritative state, and a live-instrumentation positive "
        "control (a counter that moved somewhere in the run, so a zero reading is not a compiled-out counter). A "
        "single failed gate fails this whole summary."
    )
    lines.append("")
    gate_table = [[workload, name, str(runs), verdict_word, detail] for workload, name, runs, verdict_word, detail in gates]
    if not gate_table:
        gate_table.append(["<none>", "<no gate results reported>", "0", "FAIL", "no raw document carried a gates array"])
    lines.extend(markdown_table(["workload", "gate", "runs", "verdict", "detail"], gate_table))
    lines.append("")

    lines.append("## Memory categories")
    lines.append("")
    lines.append(
        "08 requires managed heap, native containers, retained catalogs/caches, asset leases, events and quarantined "
        "work to be reported separately, and an aggregate process total alone is never evidence. Values are the range "
        "across the runs; the managed-allocation reading states whether it covered every declared thread, because a "
        "main-thread-only zero cannot prove worker allocation is zero."
    )
    lines.append("")
    memory_rows: list[list[str]] = []
    for workload_id in sorted(by_workload, key=workload_sort_key):
        data = by_workload[workload_id]
        if not data.memory_runs:
            memory_rows.append([workload_id, "<no memory block>", "-", "-", "-", "the raw document carried no memory block"])
            continue
        thread_complete = all(run.get("managedAllocationIsThreadComplete") is True for run in data.memory_runs)
        for label, key, entry_key, note in MEMORY_CATEGORIES:
            values = [coerce_int(run.get(key)) for run in data.memory_runs]
            values = [value for value in values if value is not None]
            if not values:
                continue
            reading = range_text(values)
            entries = ""
            if entry_key is not None:
                entry_values = [coerce_int(run.get(entry_key)) for run in data.memory_runs]
                entry_values = [value for value in entry_values if value is not None]
                entries = range_text(entry_values) if entry_values else ""
            thread_note = (
                ("thread-complete=" + ("true" if thread_complete else "false"))
                if key == "managedAllocatedBytes"
                else "n/a"
            )
            memory_rows.append([workload_id, label, reading, entries, thread_note, note])
    if not memory_rows:
        memory_rows.append(["<none>", "-", "-", "-", "-", "-"])
    lines.extend(markdown_table(["workload", "category", "value", "entries", "thread-complete", "note"], memory_rows))
    lines.append("")

    lines.append("## Budget table")
    lines.append("")
    lines.append(
        "The 08 table row for row (encoded from PerformanceBudgets.cs). NotMeasured is not a pass: a row whose "
        "measurement is absent is reported as NotMeasured. ReportOnly rows are baselines 08 asks to be established, "
        "not numbers to be met."
    )
    lines.append("")
    budget_rows = [
        [
            result.row.id,
            result.row.workload,
            result.row.phase,
            result.row.metric,
            result.row.describe_target(),
            measured_text(result),
            result.verdict,
            result.row.reference,
        ]
        for result in budget_results
    ]
    lines.extend(
        markdown_table(
            ["budget id", "workload", "phase", "metric", "target", "measured", "verdict", "08 reference"],
            budget_rows,
        )
    )
    lines.append("")
    lines.append("Measurement provenance (what each measured number was derived from):")
    lines.append("")
    lines.extend(f"- `{result.row.id}`: {result.measurement.detail}." for result in budget_results)
    lines.append("")

    lines.append("## Provisional targets")
    lines.append("")
    lines.append(
        "> The following numbers are initial engineering targets, not measured results or universal shipping "
        "requirements. Apply them on the recorded baseline machine; retain the measurements if later product evidence "
        "justifies revising a target. A quota in the protocol is a correctness bound and remains mandatory even when a "
        "performance target changes."
    )
    lines.append("")
    lines.append(
        "A quota in the protocol (P-022's candidate/contribution/target/byte counts, the apply-cost estimate) is a "
        "correctness bound enforced by the derivation engine; it is reported with the fixture configuration and a "
        "performance revision may never move it."
    )
    lines.append("")
    if missed:
        lines.append(
            "Budget rows MissedTarget (each is a decision for `BUDGET_DECISIONS.md` — either an accepted revision with "
            "the retained measurement, or a fix):"
        )
        lines.append("")
        for result in missed:
            lines.append(
                f"- `{result.row.id}` (`{result.row.workload}`/{result.row.phase}/{result.row.metric}): measured "
                f"{fmt_number(result.measurement.value)} against {result.row.describe_target()}. See BUDGET_DECISIONS.md."
            )
        if expect_miss:
            lines.append("")
            lines.append(
                "`--expect-miss` was given: these misses are treated as explicitly accepted budget revisions. The flag "
                "is used only when a budget revision is explicitly accepted; it never suppresses a failed gate."
            )
    else:
        lines.append("No budget row is MissedTarget.")
    if not_measured:
        lines.append("")
        lines.append(
            "Rows NotMeasured in this run (absent, never a pass): "
            + ", ".join(f"`{result.row.id}`" for result in not_measured)
            + "."
        )
    lines.append("")

    lines.append("## Raw document self-consistency")
    lines.append("")
    if self_consistency:
        lines.append(
            "Each raw document must agree with itself: its declared per-phase distributions must equal what its own "
            "samples give. These disagreements are raw-data defects, not budget results:"
        )
        lines.append("")
        lines.extend(f"- {problem}" for problem in self_consistency)
    else:
        lines.append(
            "Every raw document's declared per-phase distributions equal what its own `samples` array gives."
        )
    if context.repairs:
        lines.append("")
        lines.append("Counters-section repairs applied while reading (the writer rendered it as an array of members):")
        lines.append("")
        lines.extend(f"- {path}" for path in sorted(context.repairs))
    lines.append("")

    lines.append("## Evidence")
    lines.append("")
    lines.append("Every raw file this summary read, so each number above is recomputable from these alone:")
    lines.append("")
    evidence = sorted(
        {document.path for data in by_workload.values() for document in data.documents} | set(context.evidence),
        key=lambda path: str(path),
    )
    for path in evidence:
        lines.append(f"- `{path}`")
    lines.append("")
    return "\n".join(lines)


def range_text(values: list[int]) -> str:
    lowest = min(values)
    highest = max(values)
    return str(lowest) if lowest == highest else f"{lowest}..{highest}"


# ---------------------------------------------------------------------------------------------------------------------
# The decisions template. Never overwrites an existing file: a human's record of an accepted revision must not be lost.
# ---------------------------------------------------------------------------------------------------------------------


def decisions_template() -> str:
    lines: list[str] = []
    lines.append("# GC-026 budget decisions")
    lines.append("")
    lines.append(
        "08's provisional numbers are initial engineering targets, not measured results or universal shipping "
        "requirements. Apply them on the recorded baseline machine; retain the measurements if later product evidence "
        "justifies revising a target."
    )
    lines.append("")
    lines.append(
        "One section per budget row. Every field is `NotRun (pending orchestrator build host)` until a player run "
        "records a measurement on the build host; this file is never regenerated by `tools/summarize_benchmarks.py` "
        "once it exists, so an accepted revision recorded here survives every later summary."
    )
    lines.append("")
    for row in BUDGETS:
        lines.append(f"## {row.id}")
        lines.append("")
        lines.append(f"- workload / phase / metric: `{row.workload}` / `{row.phase}` / `{row.metric}`")
        lines.append(f"- Target (08): {row.describe_target()} — {row.reference}")
        lines.append(f"- Measured: {PLACEHOLDER}")
        lines.append(f"- Cause: {PLACEHOLDER}")
        lines.append(f"- Decision (accepted revision | fix): {PLACEHOLDER}")
        lines.append(f"- Evidence: {PLACEHOLDER}")
        lines.append("")
    lines.append("## Correctness bounds (not revisable here)")
    lines.append("")
    lines.append(
        "A quota in the protocol is a correctness bound and remains mandatory even when a performance target changes. "
        "P-022's default reference limits are 1,000,000 candidates, 250,000 contributions, 100,000 targets, 128 MiB "
        "temporary storage, 2 s preparation and 8 ms estimated application. A benchmark that exceeds one of these is a "
        "correctness finding reported with the fixture configuration, not a budget miss and not something this record "
        "may revise."
    )
    lines.append("")
    return "\n".join(lines)


PLACEHOLDER = "NotRun (pending orchestrator build host)"


# ---------------------------------------------------------------------------------------------------------------------
# The analysis.
# ---------------------------------------------------------------------------------------------------------------------


@dataclass
class Analysis:
    exit_code: int
    summary: str
    budget_results: list[BudgetResult] = field(default_factory=list)
    gates: list[tuple[str, str, int, str, str]] = field(default_factory=list)
    missed: list[str] = field(default_factory=list)
    failed_gates: list[str] = field(default_factory=list)
    problems: list[str] = field(default_factory=list)
    counts: dict[str, int] = field(default_factory=dict)
    decisions_written: bool = False
    decisions_existed: bool = False
    out_written: bool = False
    json_written: bool = False


def analyse(
    raw_roots: list[Path],
    *,
    machine: str,
    out_path: Path | None = None,
    decisions_path: Path | None = None,
    json_path: Path | None = None,
    expect_miss: bool = False,
    context: RunContext | None = None,
) -> Analysis:
    """Reads every raw sample document, pools it, decides the budget rows and writes the outputs it was given."""
    analysis = Analysis(exit_code=0, summary="")
    context = context if context is not None else collect_context(raw_roots)

    documents: list[RawDocument] = []
    for root in raw_roots:
        if not root.is_dir():
            analysis.problems.append(f"raw directory does not exist: {root}")
            continue
        paths = sorted(root.rglob("*.samples.json"), key=lambda path: str(path))
        for path in paths:
            try:
                data = load_json_document(path)
            except MalformedDocument as exc:
                analysis.problems.append(f"{path}: {exc}")
                continue
            documents.append(RawDocument(path=path, run=path.parent.name, data=data))
            if "counters" in data and not isinstance(data["counters"], (dict, list)):
                analysis.problems.append(f"{path}: counters is neither an object nor a list")
            # A legacy document that needed the counters repair is recorded here, so a repaired file is never
            # silently trusted; it is the same predicate the self-test pins the primary shape against.
            try:
                raw_text = path.read_text(encoding="utf-8")
            except OSError:
                continue
            if counters_repair_needed(raw_text):
                context.repairs.append(path)

    if analysis.problems:
        analysis.exit_code = 2
        analysis.summary = "malformed raw input:\n" + "\n".join(f"- {problem}" for problem in analysis.problems) + "\n"
        return analysis

    if not documents:
        analysis.exit_code = 2
        analysis.summary = (
            "no raw input found: no `*.samples.json` document under any --raw directory. Run "
            "`tools/run_benchmarks.sh` on the build host first; NotMeasured is not a pass.\n"
        )
        return analysis


    grouped: dict[str, list[RawDocument]] = {}
    for document in documents:
        workload_id = document.data.get("workload")
        key = workload_id if isinstance(workload_id, str) and workload_id else "<unnamed-workload>"
        grouped.setdefault(key, []).append(document)

    by_workload: dict[str, WorkloadData] = {}
    for workload_id in sorted(grouped, key=workload_sort_key):
        records = sorted(grouped[workload_id], key=lambda document: sort_key_run(document.run))
        by_workload[workload_id] = build_workload_data(WORKLOAD_BY_ID.get(workload_id), workload_id, records)

    budget_results = [measure_budget(row, by_workload.get(row.workload)) for row in BUDGETS]
    gates = gate_rows(by_workload)
    self_consistency = [problem for data in by_workload.values() for problem in data.self_consistency]

    failed_gates = [f"{workload}/{name}" for workload, name, _, verdict, _ in gates if verdict != "pass"]
    missed = [result.row.id for result in budget_results if result.verdict == VERDICT_MISSED]

    if failed_gates or (missed and not expect_miss):
        analysis.exit_code = 1
    else:
        analysis.exit_code = 0

    analysis.summary = render_summary(
        machine=machine,
        context=context,
        by_workload=by_workload,
        budget_results=budget_results,
        gates=gates,
        self_consistency=self_consistency,
        expect_miss=expect_miss,
    )
    analysis.budget_results = budget_results
    analysis.gates = gates
    analysis.missed = missed
    analysis.failed_gates = failed_gates
    analysis.counts = {
        "documents": len(documents),
        "runs": len({document.run for document in documents}),
        "workloads": len(by_workload),
        "gates": len(gates),
        "gatesFailed": len(failed_gates),
        "budgets": len(budget_results),
        "missedTarget": len(missed),
        "notMeasured": sum(1 for result in budget_results if result.verdict == VERDICT_NOT_MEASURED),
    }

    if out_path is not None:
        out_path.parent.mkdir(parents=True, exist_ok=True)
        out_path.write_text(analysis.summary, encoding="utf-8")
        analysis.out_written = True

    if decisions_path is not None:
        if decisions_path.exists():
            analysis.decisions_existed = True
        else:
            decisions_path.parent.mkdir(parents=True, exist_ok=True)
            decisions_path.write_text(decisions_template(), encoding="utf-8")
            analysis.decisions_written = True

    if json_path is not None:
        payload = {
            "artifact": "gamecore.benchmark.summary/1",
            "machine": machine,
            "exitCode": analysis.exit_code,
            "counts": analysis.counts,
            "gatesFailed": failed_gates,
            "missedTarget": missed,
            "budgets": [
                {
                    "id": result.row.id,
                    "workload": result.row.workload,
                    "phase": result.row.phase,
                    "metric": result.row.metric,
                    "target": result.row.target,
                    "reportOnly": result.row.report_only,
                    "measured": result.measurement.value,
                    "verdict": result.verdict,
                    "detail": result.measurement.detail,
                    "reference": result.row.reference,
                }
                for result in budget_results
            ],
            "gates": [
                {"workload": workload, "gate": name, "runs": runs, "verdict": verdict, "detail": detail}
                for workload, name, runs, verdict, detail in gates
            ],
            "evidence": sorted(
                str(path)
                for path in {document.path for data in by_workload.values() for document in data.documents}
                | set(context.evidence)
            ),
            "selfConsistency": self_consistency,
        }
        json_path.parent.mkdir(parents=True, exist_ok=True)
        json_path.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        analysis.json_written = True

    return analysis


# ---------------------------------------------------------------------------------------------------------------------
# Self-test: builds synthetic raw documents and asserts this tool's own logic. Uses no real raw data.
# ---------------------------------------------------------------------------------------------------------------------


def synthetic_document(
    workload: str,
    kind: str,
    *,
    run_ordinal: int = 1,
    phase_samples: dict[str, list[int]] | None = None,
    counters: dict[str, int] | None = None,
    counters_key: object = ...,  # type: ignore[assignment]
    gates: list[dict] | None = None,
    memory: dict | None = None,
    steps_advanced: int = 0,
    repetitions_requested: int = 0,
    repetitions_executed: int = 0,
    passed: bool = True,
    scopes: int = 1000,
    targets: int = 10000,
    warmup: int = 30,
    duration: int = 120,
) -> dict:
    """One raw sample document in the frozen shape, so the summarizer is tested against its own contract."""
    phase_samples = phase_samples or {}
    samples: list[dict] = []
    phases: list[dict] = []
    ordinal = 0
    for phase in PHASES:
        values = phase_samples.get(phase, [])
        if not values:
            continue
        for value in values:
            ordinal += 1
            samples.append({"ordinal": ordinal, "phase": phase, "microseconds": value, "counters": ""})
        distribution = Distribution.of(values)
        phases.append(
            {
                "phase": phase,
                "count": distribution.count,
                "min": distribution.minimum,
                "p50": distribution.p50,
                "p95": distribution.p95,
                "p99": distribution.p99,
                "max": distribution.maximum,
                "mean": round(distribution.mean, 3),
                "total": distribution.total,
            }
        )
    document = {
        "artifact": "gamecore.benchmark.samples/1",
        "workload": workload,
        "kind": kind,
        "dimension": "self-test",
        "runOrdinal": run_ordinal,
        "seed": 20260926,
        "scopes": scopes,
        "targets": targets,
        "warmupSeconds": warmup,
        "durationSeconds": duration,
        "repetitionsRequested": repetitions_requested,
        "repetitionsExecuted": repetitions_executed,
        "stepsAdvanced": steps_advanced,
        "windowMicroseconds": sum(sum(values) for values in phase_samples.values()),
        "passed": passed,
        "gates": gates if gates is not None else [{"name": "instruments-live", "passed": True, "detail": "moved=1"}],
        "notes": [],
        "phases": phases,
        "memory": memory
        if memory is not None
        else {
            "managedHeapStartBytes": 0,
            "managedHeapEndBytes": 0,
            "managedAllocatedBytes": 0,
            "managedAllocationIsThreadComplete": True,
            "nativeContainerBytes": 0,
            "leaseBytes": 0,
            "retainedEventBytes": 0,
            "retainedEventCount": 0,
            "cacheBytes": 0,
            "cacheEntries": 0,
            "quarantineBytes": 0,
            "quarantineEntries": 0,
            "liveLeases": 0,
            "outstandingCallbacks": 0,
        },
        "samples": samples,
    }
    if counters_key is ...:
        document["counters"] = dict(counters or {})
    else:
        document["counters"] = counters_key
    return document


def write_synthetic_run(root: Path, run: str, documents: list[dict]) -> Path:
    """Writes one `<run>/<workload>.samples.json` per document, exactly as the contract shape."""
    run_dir = root / run
    run_dir.mkdir(parents=True, exist_ok=True)
    for document in documents:
        path = run_dir / f"{document['workload']}.samples.json"
        path.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
    return run_dir


def clean_documents() -> list[dict]:
    """The self-test's baseline: every budget row WithinTarget/ReportOnly and every gate passing."""
    return [
        synthetic_document("idle-command-world", "Steady", phase_samples={"Step": [500] * 4}, steps_advanced=0),
        synthetic_document(
            "steady-unchanged-10000-steps", "Steady", phase_samples={"Step": [100] * 20}, counters={}, steps_advanced=10000
        ),
        synthetic_document(
            "steady-execution-10000-targets",
            "Steady",
            phase_samples={"Step": [1000] * 20},
            steps_advanced=20000,
        ),
        synthetic_document(
            "update-size-1", "Change", phase_samples={"Change": [900] * 10}, counters={"steps-advanced": 10}, steps_advanced=10,
            repetitions_requested=1000, repetitions_executed=1000,
        ),
        synthetic_document(
            "update-size-100",
            "Change",
            phase_samples={"Prepare": [500] * 10, "Apply": [1000] * 10, "EndToEnd": [1600] * 10},
            steps_advanced=10,
            repetitions_requested=1000,
            repetitions_executed=1000,
        ),
        synthetic_document(
            "update-size-10000",
            "Change",
            phase_samples={"Prepare": [50000] * 5, "Apply": [9000] * 5},
            steps_advanced=5,
            repetitions_requested=200,
            repetitions_executed=200,
        ),
        synthetic_document(
            "whole-world-mode-switch",
            "Change",
            phase_samples={"Prepare": [4000] * 5, "Apply": [3000] * 5},
            steps_advanced=5,
            repetitions_requested=200,
            repetitions_executed=200,
        ),
        synthetic_document(
            "spawn-1000",
            "Change",
            phase_samples={"Prepare": [7000] * 5, "Apply": [2000] * 5},
            steps_advanced=5,
            repetitions_requested=200,
            repetitions_executed=200,
        ),
        synthetic_document(
            "reparent-100", "Change", phase_samples={"Change": [1200] * 10}, steps_advanced=10,
            repetitions_requested=1000, repetitions_executed=1000,
        ),
        synthetic_document(
            "inactive-target-comparison",
            "Change",
            phase_samples={"Change": [1100] * 10},
            steps_advanced=10,
            repetitions_requested=1000,
            repetitions_executed=1000,
        ),
        synthetic_document(
            "lifecycle-cycles-1000",
            "Change",
            phase_samples={"Change": [1300] * 10},
            steps_advanced=10,
            repetitions_requested=1000,
            repetitions_executed=1000,
        ),
    ]


def run_self_test() -> int:
    checks: list[tuple[str, bool, str]] = []

    def check(name: str, ok: bool, detail: str = "") -> None:
        checks.append((name, ok, detail))

    # --- unit: the percentile definition matches BenchmarkStatistics.PercentileOfSorted ---------------
    values = [10, 20, 30, 40, 50]
    check("percentile p95 pools nearest-rank", percentile_of_sorted(values, 95.0) == 50, str(percentile_of_sorted(values, 95.0)))
    check("percentile p50 nearest-rank", percentile_of_sorted(values, 50.0) == 30, str(percentile_of_sorted(values, 50.0)))
    check("percentile p0 is the minimum", percentile_of_sorted(values, 0.0) == 10)
    check("percentile p100 is the maximum", percentile_of_sorted(values, 100.0) == 50)
    check("percentile of an empty list is zero", percentile_of_sorted([], 95.0) == 0)
    single = [7]
    check("percentile clamps on one sample", percentile_of_sorted(single, 99.0) == 7)
    hundred = list(range(1, 101))
    check(
        "percentile matches ceil(p/100*N)-1 on 100 samples",
        percentile_of_sorted(hundred, 95.0) == 95 and percentile_of_sorted(hundred, 99.0) == 99,
        f"p95={percentile_of_sorted(hundred, 95.0)} p99={percentile_of_sorted(hundred, 99.0)}",
    )

    # --- unit: the counters normaliser accepts the contract and tolerates the legacy array form -----
    check("counters object", normalise_counters({"a": 1, "b": 2}) == {"a": 1, "b": 2})
    check("counters list of members", normalise_counters(['"a": 1', '"b": 2']) == {"a": 1, "b": 2})
    check("counters missing", normalise_counters(None) == {} and normalise_counters("x") == {})

    # The primary shape: a real `"counters": { ... }` object (what the contract defines and the writer now emits) is
    # valid JSON on its own, so the repair must NOT be needed and must NOT be taken. This pins the primary path, so a
    # regression back to array rendering fails here instead of being silently absorbed by the legacy fallback.
    primary = json.dumps(
        {
            "artifact": "gamecore.benchmark.samples/1",
            "workload": "w",
            "counters": {"control-nodes-visited": 0, "steps-advanced": 120},
        },
        indent=2,
    )
    check("the primary counters object needs no repair", not counters_repair_needed(primary))
    check("the primary counters object is untouched by the repair", repair_counters_array(primary) == primary)
    check(
        "the primary counters object parses without the repair path",
        json.loads(primary)["counters"] == {"control-nodes-visited": 0, "steps-advanced": 120},
    )
    check(
        "an empty primary object needs no repair",
        not counters_repair_needed('{"counters": {}}') and not counters_repair_needed('{"counters": []}'),
    )
    # The legacy array rendering is the only shape the fallback claims, and it does report that it is needed.
    broken = '{\n  "workload": "w",\n  "counters": [\n    "a": 1,\n    "b": 2\n  ]\n}\n'
    check("the legacy counters array reports repair needed", counters_repair_needed(broken))
    repaired = json.loads(repair_counters_array(broken))
    check("counters-array repair yields an object", repaired["counters"] == {"a": 1, "b": 2}, str(repaired))
    valid = '{"counters": {"a": 1}}'
    check("repair leaves valid JSON untouched", repair_counters_array(valid) == valid)
    check("repair leaves an empty array valid", json.loads(repair_counters_array('{"counters": []}'))["counters"] == {})
    check(
        "plainly malformed JSON is not reported as a counters repair",
        not counters_repair_needed("{ not json at all"),
    )

    with tempfile.TemporaryDirectory(prefix="gc026-selftest-") as temporary:
        root = Path(temporary)

        # --- scenario 1: a clean set of raw documents -> exit 0, ReportOnly, pooled percentiles -------
        clean_root = root / "clean"
        write_synthetic_run(clean_root, "run1", clean_documents())
        second = []
        for document in clean_documents():
            copy = json.loads(json.dumps(document))
            copy["runOrdinal"] = 2
            for sample in copy["samples"]:
                sample["microseconds"] = sample["microseconds"] + 1
            second.append(copy)
        write_synthetic_run(clean_root, "run2", second)

        clean_context = RunContext()
        clean = analyse(
            [clean_root],
            machine="self-test",
            out_path=root / "clean-summary.md",
            decisions_path=root / "decisions.md",
            json_path=root / "clean-summary.json",
            expect_miss=False,
            context=clean_context,
        )
        check("clean set exits 0", clean.exit_code == 0, f"exit={clean.exit_code} problems={clean.problems}")
        check("clean set has no failed gate", not clean.failed_gates, str(clean.failed_gates))
        check("clean set has no missed target", not clean.missed, str(clean.missed))
        check("clean set parsed both runs", clean.counts.get("runs") == 2, str(clean.counts))
        check("clean set wrote the summary", clean.out_written and (root / "clean-summary.md").is_file())
        check("clean set wrote the decisions template", clean.decisions_written and (root / "decisions.md").is_file())
        check("clean set wrote the json", clean.json_written and (root / "clean-summary.json").is_file())
        # The primary shape takes no repair path: every synthetic document above wrote a real counters object, so the
        # summarizer must not have recorded a single repaired file.
        check(
            "the primary counters object is read without any repair being taken",
            clean_context.repairs == [],
            str(clean_context.repairs),
        )
        check(
            "a summary of only primary-shape documents reports no repairs",
            "Counters-section repairs applied" not in clean.summary,
        )

        verdicts = {result.row.id: result.verdict for result in clean.budget_results}
        check(
            "spawn baseline is ReportOnly",
            verdicts.get("budget.spawn-baseline") == VERDICT_REPORT_ONLY,
            str(verdicts.get("budget.spawn-baseline")),
        )
        check(
            "lifecycle plateau is ReportOnly",
            verdicts.get("budget.lifecycle-plateau") == VERDICT_REPORT_ONLY,
            str(verdicts.get("budget.lifecycle-plateau")),
        )
        check(
            "idle rows are WithinTarget at zero",
            verdicts.get("budget.idle-steps") == VERDICT_WITHIN
            and verdicts.get("budget.idle-stage-updates") == VERDICT_WITHIN,
            f"steps={verdicts.get('budget.idle-steps')} stage={verdicts.get('budget.idle-stage-updates')}",
        )
        check(
            "managed bytes per step is WithinTarget at zero",
            verdicts.get("budget.execution-managed-bytes") == VERDICT_WITHIN,
            str(verdicts.get("budget.execution-managed-bytes")),
        )
        # run1 p95=1000, run2 p95=1001; pooled p95 must be 1001 (the max), not the average 1000.5.
        execution_p95 = next(result for result in clean.budget_results if result.row.id == "budget.execution-p95")
        check(
            "pooled p95 is the pooled nearest-rank, not an average of run p95s",
            execution_p95.measurement.value == 1001.0,
            str(execution_p95.measurement.value),
        )
        check(
            "summary states the pooled percentile definition",
            "ceil(p/100 * N) - 1" in clean.summary,
        )
        check("summary never prints a bare pass for a zero", "NotMeasured is not a pass" in clean.summary)

        # the decisions file must survive a second run untouched
        (root / "decisions.md").write_text("# edited by a human\n", encoding="utf-8")
        again = analyse([clean_root], machine="self-test", decisions_path=root / "decisions.md")
        check("an existing decisions file is not overwritten", again.decisions_existed and not again.decisions_written)
        check(
            "the human decisions file is intact",
            (root / "decisions.md").read_text(encoding="utf-8") == "# edited by a human\n",
        )

        # --- the legacy array-of-members counters shape is repaired, and the repair is recorded ---------
        broken_root = root / "broken"
        document = synthetic_document(
            "steady-unchanged-10000-steps",
            "Steady",
            phase_samples={"Step": [100] * 5},
            counters={"control-nodes-visited": 0, "steps-advanced": 100},
            steps_advanced=100,
        )
        member_text = ",\n".join(f'    "{key}": {value}' for key, value in document["counters"].items())
        text = json.dumps(document, indent=2).replace(
            json.dumps(document["counters"], indent=2).replace("\n", "\n  "), "[\n" + member_text + "\n  ]"
        )
        broken_dir = broken_root / "run1"
        broken_dir.mkdir(parents=True)
        broken_path = broken_dir / "steady-unchanged-10000-steps.samples.json"
        broken_path.write_text(text + "\n", encoding="utf-8")
        broken_context = RunContext()
        repaired_run = analyse([broken_root], machine="self-test", context=broken_context)
        check("the counters-array document is repaired, not rejected", repaired_run.exit_code != 2, str(repaired_run.problems))
        check(
            "the repaired counters are readable",
            repaired_run.counts.get("budgets") == len(BUDGETS),
        )
        check(
            "the legacy document is recorded as repaired",
            [str(path) for path in broken_context.repairs] == [str(broken_path)],
            str(broken_context.repairs),
        )
        check(
            "the summary names the legacy document it repaired",
            "Counters-section repairs applied" in repaired_run.summary
            and str(broken_path) in repaired_run.summary,
        )

        # --- scenario 2: a missing phase -> NotMeasured, never a pass ---------------------------------
        missing_root = root / "missing-phase"
        documents = clean_documents()
        for index, document in enumerate(documents):
            if document["workload"] == "update-size-100":
                documents[index] = synthetic_document(
                    "update-size-100",
                    "Change",
                    phase_samples={"Prepare": [500] * 10, "EndToEnd": [1600] * 10},
                    steps_advanced=10,
                    repetitions_requested=1000,
                    repetitions_executed=1000,
                )
        write_synthetic_run(missing_root, "run1", documents)
        missing = analyse([missing_root], machine="self-test")
        apply_row = next(result for result in missing.budget_results if result.row.id == "budget.apply-pause-p95")
        check("a missing phase is NotMeasured", apply_row.verdict == VERDICT_NOT_MEASURED, apply_row.verdict)
        check("a missing phase does not fail the run by itself", missing.exit_code == 0, f"exit={missing.exit_code}")
        check("NotMeasured carries a reason", "no samples in phase" in apply_row.measurement.detail, apply_row.measurement.detail)

        # --- scenario 3: a missed row and a failed gate -> exit 1 -------------------------------------
        bad_root = root / "bad"
        documents = clean_documents()
        for index, document in enumerate(documents):
            if document["workload"] == "steady-execution-10000-targets":
                documents[index] = synthetic_document(
                    "steady-execution-10000-targets",
                    "Steady",
                    phase_samples={"Step": [5000] * 20},
                    steps_advanced=20000,
                    gates=[{"name": "instruments-live", "passed": False, "detail": "moved=0"}],
                    passed=False,
                )
        write_synthetic_run(bad_root, "run1", documents)
        bad = analyse([bad_root], machine="self-test")
        check("a missed target exits 1", bad.exit_code == 1, f"exit={bad.exit_code}")
        check("the missed row is named", "budget.execution-p95" in bad.missed, str(bad.missed))
        check(
            "a failed gate is named",
            any("instruments-live" in name for name in bad.failed_gates),
            str(bad.failed_gates),
        )
        # --expect-miss clears the miss, but never a failed gate
        expected = analyse([bad_root], machine="self-test", expect_miss=True)
        check("--expect-miss clears a miss but not a gate", expected.exit_code == 1, f"exit={expected.exit_code}")
        accepted_root = root / "accepted"
        documents = clean_documents()
        for index, document in enumerate(documents):
            if document["workload"] == "steady-execution-10000-targets":
                documents[index] = synthetic_document(
                    "steady-execution-10000-targets", "Steady", phase_samples={"Step": [5000] * 20}, steps_advanced=20000
                )
        write_synthetic_run(accepted_root, "run1", documents)
        check(
            "--expect-miss accepts an accepted revision",
            analyse([accepted_root], machine="self-test", expect_miss=True).exit_code == 0,
        )
        check(
            "without --expect-miss the same data fails",
            analyse([accepted_root], machine="self-test").exit_code == 1,
        )

        # --- scenario 4/5: no raw input, malformed raw input -> exit 2 --------------------------------
        empty = analyse([root / "does-not-exist"], machine="self-test")
        check("no raw input exits 2", empty.exit_code == 2, f"exit={empty.exit_code}")
        malformed_root = root / "malformed"
        (malformed_root / "run1").mkdir(parents=True)
        (malformed_root / "run1" / "broken.samples.json").write_text("{ this is not json", encoding="utf-8")
        malformed = analyse([malformed_root], machine="self-test")
        check("malformed JSON exits 2", malformed.exit_code == 2, f"exit={malformed.exit_code}")

        # --- determinism: two summaries of the same raw data are identical ----------------------------
        first_text = analyse([clean_root], machine="self-test").summary
        second_text = analyse([clean_root], machine="self-test").summary
        check("two summaries of the same raw data are identical", first_text == second_text)

        # --- evidence completeness --------------------------------------------------------------------
        raw_paths = sorted(str(path) for path in clean_root.rglob("*.samples.json"))
        check(
            "the summary lists every raw file read",
            bool(raw_paths) and all(path in clean.summary for path in raw_paths),
            f"{len(raw_paths)} raw file(s) checked",
        )
        check(
            "the summary states NotMeasured instead of a zero for an absent phase",
            "budget.apply-pause-p95" in missing.summary and "NotMeasured" in missing.summary,
        )
        check(
            "the budget table quotes the 08 reference sentence",
            "Apply pause p95 at most 2 ms" in clean.summary,
        )
        check(
            "the memory categories are reported separately, never as one process total",
            "native containers" in clean.summary and "quarantine" in clean.summary and "thread-complete" in clean.summary,
        )

    failures = [name for name, ok, _ in checks if not ok]
    for name, ok, detail in checks:
        marker = "ok  " if ok else "FAIL"
        suffix = f" ({detail})" if detail and not ok else ""
        print(f"{marker} {name}{suffix}")
    print(f"self-test: {len(checks) - len(failures)}/{len(checks)} checks passed")
    if failures:
        print("failed: " + ", ".join(failures), file=sys.stderr)
        return 1
    return 0


# ---------------------------------------------------------------------------------------------------------------------
# CLI.
# ---------------------------------------------------------------------------------------------------------------------


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="summarize_benchmarks.py",
        description=(
            "Pool the GC-026 raw per-workload sample documents and compare them to 08's provisional budget table. "
            "Exit codes: 0 all parsed / every gate passed / no MissedTarget, 1 a gate failed or a target was missed, "
            "2 no raw input or malformed raw JSON."
        ),
    )
    parser.add_argument(
        "--raw",
        action="append",
        default=None,
        metavar="DIR",
        help="directory holding <run>/*.samples.json; repeatable (default: artifacts/performance/raw)",
    )
    parser.add_argument("--out", default=None, metavar="PATH", help="write the human-readable summary.md")
    parser.add_argument(
        "--decisions",
        default=None,
        metavar="PATH",
        help="create BUDGET_DECISIONS.md from the template only when it does not already exist",
    )
    parser.add_argument("--machine", default="", metavar="NAME", help="recorded baseline machine name")
    parser.add_argument("--json", default=None, metavar="PATH", help="write the machine-readable result JSON")
    parser.add_argument(
        "--expect-miss",
        action="store_true",
        help="used only when a budget revision is explicitly accepted; a MissedTarget stops failing the summary",
    )
    parser.add_argument(
        "--self-test",
        action="store_true",
        help="build synthetic raw documents in a temp dir and assert this tool's own logic (no real raw data needed)",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if args.self_test:
        return run_self_test()

    raw_roots = [Path(item) for item in (args.raw or ["artifacts/performance/raw"])]
    analysis = analyse(
        raw_roots,
        machine=args.machine,
        out_path=Path(args.out) if args.out else None,
        decisions_path=Path(args.decisions) if args.decisions else None,
        json_path=Path(args.json) if args.json else None,
        expect_miss=args.expect_miss,
    )

    if analysis.decisions_existed:
        print(f"note: {args.decisions} already exists; left untouched (measured values are in {args.out or 'stdout'}).")
    elif analysis.decisions_written:
        print(f"note: created {args.decisions} from the template; fill its fields when a run measures something.")
    if analysis.out_written:
        print(f"summary: {args.out}")
    if analysis.json_written:
        print(f"json   : {args.json}")
    if analysis.exit_code == 2:
        print(analysis.summary, file=sys.stderr)
    else:
        print(
            "counts : "
            + ", ".join(f"{key}={value}" for key, value in sorted(analysis.counts.items()))
        )
        if analysis.missed:
            print("missed : " + ", ".join(analysis.missed), file=sys.stderr)
        if analysis.failed_gates:
            print("gates  : FAILED " + ", ".join(analysis.failed_gates), file=sys.stderr)
    return analysis.exit_code


if __name__ == "__main__":
    sys.exit(main())
