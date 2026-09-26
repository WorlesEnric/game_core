#!/usr/bin/env python3
"""Generate the operator failure-code table from the production sources.

A failure-code table in a document drifts the moment someone adds a code. GC-029 owns the operator
contract, and 09 requires that "failure codes are actionable and maintenance commands are executable",
so the table is generated from what the code actually declares and never hand-maintained:

  * `DiagnosticCode` and `DiagnosticCodeText.Of` in
    `Packages/com.gamecore.contracts/Runtime/Results/Diagnostics.cs` are the protocol's operation
    failure codes. The enum member, its numeric value and the exact literal `Of` returns are all read
    from source, and their agreement is asserted; the `Values` list is compared with `Of` too, because
    a code the parser cannot round-trip is a code an operator cannot act on.
  * `CatalogDiagnosticCode` in `Packages/com.gamecore.content.compiler/Runtime/Description/CatalogDiagnostic.cs`
    is the separate build-time generator code set. It is a different contract from the operation codes
    (a bad catalog description fails a build, not a world operation), so it is a separate table.
  * `NarrativeRefusals` in `Packages/com.gamecore.rules.narrative/Runtime/NarrativeRefusals.cs` is the
    narrative domain's stable refusal vocabulary returned in operation results.

The action/recovery text for each operation code is authored here and joined to the code by name: a code
with no authored text fails the run, and authored text for a code that no longer exists fails too. That
makes the document impossible to leave stale in either direction.

Usage:
    python3 tools/emit_failure_codes.py [--check] [--json <path>] [--output <path>] [--self-test]

`--check` writes nothing and exits nonzero if the committed document differs from what the sources
produce, which is how `tools/reproduce.sh` proves the checked-in table is not stale.

Exit codes: 0 the table was written (or matched, with --check); 1 a disagreement or a missing join.
"""
from __future__ import annotations

import argparse
import difflib
import json
import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
DIAGNOSTIC_ENUM = ROOT / "Packages/com.gamecore.contracts/Runtime/Manifest/ManifestEnums.cs"
DIAGNOSTIC_TEXT = ROOT / "Packages/com.gamecore.contracts/Runtime/Results/Diagnostics.cs"
CATALOG = ROOT / "Packages/com.gamecore.content.compiler/Runtime/Description/CatalogDiagnostic.cs"
REFUSALS = ROOT / "Packages/com.gamecore.rules.narrative/Runtime/NarrativeRefusals.cs"
DOCUMENT_NAME = "failure-codes.md"

# The 00 s9 code list is the normative source of the required set (P-052). It is repeated here only to
# assert that every code the protocol names is actually declared and mapped: the table itself is
# generated from source, so this list cannot silently substitute for one.
P052_REQUIRED = (
    "StaleHandle", "StalePlan", "MissingDependency", "ServiceConflict", "CapabilityConflict",
    "AmbiguousOrder", "Cycle", "Ineligible", "UnsupportedVersion", "OwnershipConflict",
    "BudgetExceeded", "MigrationRequired", "ResourceUnavailable", "Cancelled", "TooLate",
    "IdempotencyConflict", "ResultExpired", "ApplyFault", "TeardownBlocked", "CursorExpired",
)

# Action and recovery for each protocol operation code. Authored, one entry per declared code.
OPERATION_ACTIONS = {
    "StaleHandle": (
        "A handle (target, scope, installation, snapshot token or runtime slot) names state that no "
        "longer exists, or whose world incarnation has been replaced.",
        "Re-resolve the handle from the current published snapshot (O-17 Observe / O-25 Explain). Never "
        "retry the same handle: a retired object is never resurrected (P-005)."),
    "StalePlan": (
        "The plan was validated against a base composition revision that another publication replaced "
        "while the plan was being prepared.",
        "Replan against the current revision with a NEW operation id (P-050). Inspecting the status of "
        "the old id returns the same terminal rejection; it never applies late."),
    "MissingDependency": (
        "A required service, capability or provider is absent for this mount, so the installation "
        "cannot reach Active.",
        "The installation is published WaitingForDependencies rather than failed. Mount or activate "
        "the provider; the dependency watcher then retries automatically (O-04). There is nothing to "
        "retry by hand."),
    "ServiceConflict": (
        "Two providers claim the same single-binding service contract in one scope, or a selection "
        "rule matched more than one candidate where exactly one is required.",
        "Resolve the ambiguity in the composition: remove one binding, narrow a visibility domain, or "
        "declare an explicit selection. The old visible assembly is intact."),
    "CapabilityConflict": (
        "Two capability contributions are mutually incompatible, or a slot's composition policy "
        "cannot combine the candidate set.",
        "Read the explanation page (O-25) to see the losing candidate and the exclusion that produced "
        "it, then declare an exclusion, an import/opt-in, or a different reducer policy (P-016)."),
    "AmbiguousOrder": (
        "The compiled schedule cannot order two systems: their declared inner-DAG edges and access "
        "declarations leave the relative order undetermined.",
        "Declare an explicit stage or inner-stage edge. Order is never resolved by declaration order, "
        "worker scheduling or a stable-sort accident (P-040)."),
    "Cycle": (
        "A dependency, scope-ancestry or scheduling graph contains a cycle.",
        "Break the cycle in the declarations, not at runtime. Cycle detection is a validation failure "
        "before any live write, so the previous assembly is untouched."),
    "Ineligible": (
        "A target does not satisfy a rule's selector, an opt-in requirement, or a Conservative-mode "
        "grant predicate, so no contribution was derived for it.",
        "This is the designed outcome of Conservative mode and of a selective predicate. If the "
        "target should have received the capability, add the missing import/opt-in or widen the "
        "selector (P-013)."),
    "UnsupportedVersion": (
        "A manifest, schema, catalog or protocol version is not one this build supports, or a required "
        "feature id is unknown.",
        "Deploy a catalog/manifest built for this protocol version, or add the migration. There is no "
        "silent fallback to an older mode, an older combat phase or a different backend (P-055)."),
    "OwnershipConflict": (
        "A write was attempted on state whose owner is another installation, or a proposed owner "
        "transfer is not compatible.",
        "Only the owning installation mutates its slots. Route the change through the owner's command "
        "port, or declare a compatible TransferTo with an explicit migration (P-034)."),
    "BudgetExceeded": (
        "A bounded enumeration, buffer or derivation quota was reached: examined candidates, emitted "
        "contributions, affected targets, temporary storage, or the configured apply-cost estimate.",
        "This is a correctness bound, not a tunable performance miss. Narrow the proposal's scope, split "
        "the edit into bounded operations, or raise the configured limit deliberately at the host. An "
        "overrun after writes began is reported and the world must finish or fault (P-022, P-031)."),
    "MigrationRequired": (
        "State exists in a schema version that needs an explicit directed migration which is not "
        "registered, or two paths reach the same destination version.",
        "Register exactly one directed migration for the requested source/target pair and republish. "
        "A failed migration leaves the old live state authoritative (P-054)."),
    "ResourceUnavailable": (
        "A staged acquisition failed: an asset or service could not be prepared, or its factory "
        "reported failure.",
        "Staged resources are released or quarantined, never leaked. The declared "
        "FailureClassification decides: Retriable may be retried as a new attempt; "
        "CorrectnessRequiresChangedInput needs different input; Fatal needs the world recreated "
        "(O-10, P-049)."),
    "Cancelled": (
        "The operation was cancelled before it touched live state, or before the cancellation cutoff "
        "at the committed boundary.",
        "No new epoch was created and no live write happened. Retry with a new operation id if the "
        "change is still wanted; a duplicate of the same id retrieves the same terminal result."),
    "TooLate": (
        "Cancellation, suspend or teardown arrived after the serialized cutoff (Applying or executing), "
        "so it was refused rather than silently applied.",
        "Wait for the true terminal result of the in-flight operation (O-18). TooLate never implies a "
        "rollback, and no compensating gameplay transaction is generated."),
    "IdempotencyConflict": (
        "The same operation id was reused with a different payload, or an idempotency key was reused "
        "across a payload that must be identical.",
        "Use a fresh operation id for a genuinely new attempt. Reusing an id is only for retrieving "
        "the SAME attempt (P-050)."),
    "ResultExpired": (
        "The operation ledger no longer retains a result for the requested id, because the bounded "
        "ledger reclaimed it.",
        "Treat the operation as unknown: query the current published state instead of the historical "
        "status. Terminal results are retained for a bounded window by design, not forever."),
    "ApplyFault": (
        "An exception or structural failure occurred AFTER the first live write, so authoritative "
        "storage may be inconsistent.",
        "This is a fail-stop: admission is closed and the world does not resume execution. The last "
        "committed snapshot is the only valid view. Recover with O-22 RecoverWorld from a checkpoint "
        "or the initial definition; never attempt a partial rollback (P-031)."),
    "TeardownBlocked": (
        "Cleanup cannot proceed because a job fence has not completed, a runtime user is still pinned, "
        "or a disposer threw so its resource was quarantined.",
        "Wait safely: a timeout is not proof that a job stopped and never frees a buffer. New "
        "acquisition is rejected or the world is stopped per host policy; retained resources stay "
        "observable in the resource ledger (O-19, P-048)."),
    "CursorExpired": (
        "A snapshot or committed-event cursor is older than the bounded retention window, so the "
        "reader cannot continue incrementally.",
        "Resynchronize: acquire a fresh snapshot token and resume from it (O-17). Repeated reads with "
        "the same valid token remain identical."),
    "SnapshotBackpressure": (
        "A new snapshot lease was refused because the bounded retention pool is fully leased by live "
        "readers; leased memory is never overwritten to make room (P-007).",
        "Release snapshot leases the reader no longer needs, then retry the acquisition. This is "
        "backpressure, not corruption: existing tokens keep their immutable data."),
    "None": (
        "No failure. This value is the success sentinel of a result record, not an error to act on.",
        "Nothing to do; the absence of a code means the operation reported its normal outcome."),
}

# Action and recovery for each generated build-time code.
CATALOG_ACTIONS = {
    "InvalidDocument": ("The catalog description is not well-formed JSON, or its root is not an object.",
                        "Fix the document's syntax. The diagnostic names the document path."),
    "UnsupportedFormat": ("The description format id is missing or not the supported one.",
                          "Set the format id to the one this compiler accepts."),
    "UnknownMember": ("A member name is not part of the documented input form.",
                      "Remove the member or correct its spelling; the diagnostic names it."),
    "MissingMember": ("A required member is absent.", "Add the named member to the document."),
    "InvalidValue": ("A member has the wrong JSON kind, or a value outside its accepted domain.",
                     "Correct the named member's value."),
    "InvalidIdentityHex": ("An identity is not exactly 32 lowercase hexadecimal characters.",
                           "Rewrite the identity as 32 lowercase hex characters."),
    "InvalidStableName": ("A stable name is empty or uses characters outside the accepted set.",
                          "Use the accepted stable-name character set."),
    "InvalidIdentifier": ("A generated name would not be a valid C# identifier.",
                          "Rename the declaration so its generated form is a legal identifier."),
    "InvalidCodeFragment": ("A declared type or expression is not an acceptable generated-code fragment.",
                            "Replace it with a fragment the generator accepts."),
    "DuplicateStableName": ("Two declarations use the same stable name.",
                            "Give each declaration a unique stable name."),
    "DuplicateSchemaId": ("Two schemas declare the same schema identity.",
                          "Make schema identities unique."),
    "DuplicateFieldId": ("Two fields in one schema declare the same field id.",
                         "Make field ids unique within the schema."),
    "DuplicateMemberName": ("Two generated members would use the same name.",
                            "Disambiguate the declarations so the generated members differ."),
    "ReservedFieldId": ("A field id is not positive; id 0 is reserved for the envelope checksum.",
                        "Use a positive field id."),
    "UnsupportedWireType": ("A field declares a wire type the mapping does not support.",
                            "Use a supported wire type."),
    "InvalidProtocolVersion": ("The declared protocol version is not a supported major/minor pair.",
                               "Declare a supported protocol major plus min/max minor (P-055)."),
    "InvalidCatalog": ("The description does not produce a valid catalog under the production catalog rules.",
                       "Fix the catalog contents; the diagnostic names the offending member."),
}


def read(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8")
    except (OSError, UnicodeError) as error:
        raise SystemExit(f"emit_failure_codes.py: cannot read {path}: {error}")


def parse_enum(text: str, name: str):
    """The members of one enum as an ordered {member: value} dict, from its declaration only."""
    match = re.search(r"public enum " + name + r"\s*\{(.*?)\n    \}", text, re.S)
    if not match:
        raise SystemExit(f"emit_failure_codes.py: enum {name} not found")
    members = {}
    for member, value in re.findall(r"^\s*(\w+)\s*=\s*(-?\d+)\s*,", match[1], re.M):
        members[member] = int(value)
    return members


def parse_code_text(text: str):
    """`DiagnosticCodeText.Of`'s case map: {member: literal}."""
    match = re.search(r"public static string Of\(DiagnosticCode code\)\s*\{(.*?)\n        \}", text, re.S)
    if not match:
        raise SystemExit("emit_failure_codes.py: DiagnosticCodeText.Of not found")
    return dict(re.findall(r"case DiagnosticCode\.(\w+):\s*return\s*\"([^\"]*)\";", match[1]))


def parse_values_list(text: str):
    """The ordered members of `DiagnosticCodeText.Values`."""
    match = re.search(r"public static IReadOnlyList<DiagnosticCode> Values \{ get; \} = Array\.AsReadOnly\(new\[\]\s*\{(.*?)\}\)", text, re.S)
    if not match:
        raise SystemExit("emit_failure_codes.py: DiagnosticCodeText.Values not found")
    return re.findall(r"DiagnosticCode\.(\w+),", match[1])


def parse_string_constants(text: str):
    """`public const string NAME = "value";` pairs from a refusal-vocabulary class."""
    return re.findall(r'public const string (\w+)\s*=\s*"([^"]*)";', text)


def build_table(root: Path):
    """The generated document and a machine-readable report. Raises on any disagreement."""
    enum_source = read(root / DIAGNOSTIC_ENUM.relative_to(ROOT))
    text_source = read(root / DIAGNOSTIC_TEXT.relative_to(ROOT))
    catalog = read(root / CATALOG.relative_to(ROOT))
    refusals_source = read(root / REFUSALS.relative_to(ROOT))

    codes = parse_enum(enum_source, "DiagnosticCode")
    literals = parse_code_text(text_source)
    ordered = parse_values_list(text_source)
    catalog_codes = parse_enum(catalog, "CatalogDiagnosticCode")
    refusal_constants = parse_string_constants(refusals_source)

    problems = []
    for member in codes:
        if member not in literals:
            problems.append(f"DiagnosticCode.{member} has no DiagnosticCodeText.Of literal")
    for member in literals:
        if member not in codes:
            problems.append(f"DiagnosticCodeText.Of maps {member}, which DiagnosticCode does not declare")
        elif literals[member] != member:
            problems.append(
                f"DiagnosticCode.{member} maps to literal {literals[member]!r}, which is not the member name")
    for member in ordered:
        if member not in codes:
            problems.append(f"DiagnosticCodeText.Values lists {member}, which DiagnosticCode does not declare")
    missing_from_values = sorted(set(literals) - set(ordered) - {"None"})
    if missing_from_values:
        problems.append(
            "DiagnosticCodeText.Values omits declared code(s): " + ", ".join(missing_from_values))
    for required in P052_REQUIRED:
        if required not in codes:
            problems.append(f"P-052 requires code {required}, which DiagnosticCode does not declare")
        elif required not in ordered:
            problems.append(f"P-052 requires code {required}, which DiagnosticCodeText.Values omits")

    documented = set(OPERATION_ACTIONS)
    declared = set(codes)
    for member in sorted(declared - documented):
        problems.append(f"DiagnosticCode.{member} has no operator action/recovery text")
    for member in sorted(documented - declared):
        problems.append(f"operator text exists for {member}, which DiagnosticCode does not declare")
    for member in sorted(set(catalog_codes) - set(CATALOG_ACTIONS)):
        problems.append(f"CatalogDiagnosticCode.{member} has no operator action text")
    for member in sorted(set(CATALOG_ACTIONS) - set(catalog_codes)):
        problems.append(f"catalog action text exists for {member}, which CatalogDiagnosticCode does not declare")

    lines = []
    lines.append("# Failure codes and operator actions\n")
    lines.append("Generated by `tools/emit_failure_codes.py` from the production sources. Do not edit this file by")
    lines.append("hand: `tools/reproduce.sh` regenerates it with `--check` and fails if the committed table differs")
    lines.append("from what the sources declare.\n")
    lines.append("Sources:\n")
    lines.append(
        "- `Packages/com.gamecore.contracts/Runtime/Manifest/ManifestEnums.cs` — the `DiagnosticCode` enum.")
    lines.append(
        "- `Packages/com.gamecore.contracts/Runtime/Results/Diagnostics.cs` — `DiagnosticCodeText`, which maps")
    lines.append("  each code to its exact literal and lists the non-`None` codes in normative order.")
    lines.append("- `Packages/com.gamecore.content.compiler/Runtime/Description/CatalogDiagnostic.cs` —")
    lines.append("  `CatalogDiagnosticCode` (build-time catalog generation codes, a separate contract).")
    lines.append("- `Packages/com.gamecore.rules.narrative/Runtime/NarrativeRefusals.cs` — the narrative domain's")
    lines.append("  stable refusal vocabulary, returned inside operation results rather than as a protocol code.\n")
    lines.append("## 1. Protocol operation codes (`DiagnosticCode`)\n")
    lines.append("Every code below is declared by `DiagnosticCode`, has an exact literal from")
    lines.append("`DiagnosticCodeText.Of` (the member name itself), and appears in `DiagnosticCodeText.Values`.")
    lines.append("A `Diagnostic` carries the code, phase, operation id, plan hash, involved ids/keys, counts, the")
    lines.append("configured budget limit and a retry classification (P-052).\n")
    lines.append("The **Typical retry** column is an operator convenience only. Retry classification is a property")
    lines.append("of the concrete failure and is carried per diagnostic by `Diagnostic.Retry`, which is")
    lines.append("authoritative; read it from the diagnostic you received rather than inferring it from the code.\n")
    lines.append("| Code | Value | Typical retry | Meaning | Operator action |")
    lines.append("| --- | ---: | --- | --- | --- |")
    for member in [m for m in codes if m != "None"] + ["None"]:
        meaning, action = OPERATION_ACTIONS.get(
            member, ("(undocumented)", "**No operator action is documented for this code.**"))
        retry = classify_retry(member)
        lines.append(
            f"| `{literals.get(member, '(no literal)')}` | {codes[member]} | {retry} | {meaning} | {action} |")
    lines.append("")
    lines.append(f"Declared codes: {len(codes)} ({len(codes) - 1} failures plus the `None` success sentinel).")
    lines.append("`DiagnosticCodeText.Values` lists")
    lines.append(f"{len(ordered)} non-`None` codes in 00 s9 order; P-052's required set of {len(P052_REQUIRED)}")
    lines.append("is fully present, and `SnapshotBackpressure` follows it because P-007 requires it.\n")
    lines.append("## 2. Build-time catalog codes (`CatalogDiagnosticCode`)\n")
    lines.append("These are raised by the content compiler while compiling a catalog description document, so they")
    lines.append("fail a build rather than a world operation. Each diagnostic carries the code, the document path and")
    lines.append("a message.\n")
    lines.append("| Code | Value | Meaning | Operator action |")
    lines.append("| --- | ---: | --- | --- |")
    for member, value in sorted(catalog_codes.items(), key=lambda item: item[1]):
        meaning, action = CATALOG_ACTIONS.get(
            member, ("(undocumented)", "**No operator action is documented for this code.**"))
        lines.append(f"| `{member}` | {value} | {meaning} | {action} |")
    lines.append("")
    lines.append("## 3. Narrative domain refusals (`NarrativeRefusals`)\n")
    lines.append("Pure-rule refusals returned as a stable string code inside a narrative operation result. They are")
    lines.append("NOT protocol `DiagnosticCode` values: a refused dialogue choice or an unchanged fact is a normal")
    lines.append("domain outcome, not a kernel failure.\n")
    lines.append("| Constant | Code string |")
    lines.append("| --- | --- |")
    for name, value in refusal_constants:
        shown = value if value else "(empty: accepted)"
        lines.append(f"| `{name}` | `{shown}` |")
    lines.append("")
    return "\n".join(lines) + "\n", {
        "diagnostic_codes": codes,
        "diagnostic_literals": literals,
        "values_order": ordered,
        "catalog_codes": catalog_codes,
        "narrative_refusals": dict(refusal_constants),
    }, problems


# Retry classification is a property of the failure, not of the code, so the mapping is authored here
# beside the action text. `RetryClassification` itself is declared in the contracts.
RETRY_BY_CODE = {
    "StaleHandle": "NotRetryable", "StalePlan": "NotRetryable", "MissingDependency": "RetrySameInput",
    "ServiceConflict": "RequiresChangedInput", "CapabilityConflict": "RequiresChangedInput",
    "AmbiguousOrder": "RequiresChangedInput", "Cycle": "RequiresChangedInput",
    "Ineligible": "RequiresChangedInput", "UnsupportedVersion": "RequiresChangedInput",
    "OwnershipConflict": "RequiresChangedInput", "BudgetExceeded": "RequiresChangedInput",
    "MigrationRequired": "RequiresChangedInput", "ResourceUnavailable": "RetrySameInput",
    "Cancelled": "RetrySameInput", "TooLate": "NotRetryable", "IdempotencyConflict": "RequiresChangedInput",
    "ResultExpired": "RetrySameInput", "ApplyFault": "NotRetryable", "TeardownBlocked": "RetrySameInput",
    "CursorExpired": "RetrySameInput", "SnapshotBackpressure": "RetrySameInput", "None": "NotRetryable",
}

def classify_retry(member):
    return f"`{RETRY_BY_CODE.get(member, 'Undocumented')}`"


def self_test():
    """Falsify the generator's own rules against the real sources, and against planted defects."""
    failures = 0

    def check(label, condition, detail=""):
        nonlocal failures
        if condition:
            print(f"   ok   {label}")
        else:
            failures += 1
            print(f"   FAIL {label} {detail}")

    document, report, problems = build_table(ROOT)
    check("the real sources produce a table with no disagreement", problems == [], str(problems))
    check("the table contains every declared operation code",
          all(f"`{code}`" in document for code in report["diagnostic_codes"]))
    check("the table contains every declared catalog code",
          all(f"`{code}`" in document for code in report["catalog_codes"]))
    check("the table contains every narrative refusal",
          all(f"`{name}`" in document for name, _ in report["narrative_refusals"].items()))

    import tempfile
    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        for path in (DIAGNOSTIC_ENUM, DIAGNOSTIC_TEXT, CATALOG, REFUSALS):
            (root / path.parent.relative_to(ROOT)).mkdir(parents=True, exist_ok=True)

        def plant(enum_text=None, text_text=None):
            """Copy the real sources, substitute one of them, and rebuild the table."""
            (root / DIAGNOSTIC_ENUM.relative_to(ROOT)).write_text(
                enum_text if enum_text is not None else read(DIAGNOSTIC_ENUM), encoding="utf-8")
            (root / DIAGNOSTIC_TEXT.relative_to(ROOT)).write_text(
                text_text if text_text is not None else read(DIAGNOSTIC_TEXT), encoding="utf-8")
            (root / CATALOG.relative_to(ROOT)).write_text(read(CATALOG), encoding="utf-8")
            (root / REFUSALS.relative_to(ROOT)).write_text(read(REFUSALS), encoding="utf-8")
            return build_table(root)[2]

        real_enum = read(DIAGNOSTIC_ENUM)
        real_text = read(DIAGNOSTIC_TEXT)

        problems = plant(enum_text=real_enum.replace(
            "        Cycle = 7,\n", "        Cycle = 7,\n        NotDocumented = 99,\n"))
        check("an undocumented new code is reported",
              any("NotDocumented has no operator action" in p for p in problems), str(problems))

        problems = plant(text_text=real_text.replace(
            'case DiagnosticCode.Cycle: return "Cycle";', 'case DiagnosticCode.Cycle: return "Loop";'))
        check("a literal that is not the member name is reported",
              any("is not the member name" in p for p in problems), str(problems))

        problems = plant(text_text=real_text.replace("            DiagnosticCode.Cycle,\n", ""))
        check("a code missing from Values is reported",
              any("omits declared code(s)" in p and "Cycle" in p for p in problems), str(problems))

        problems = plant(enum_text=real_enum.replace("        Ineligible = 8,\n", ""))
        check("a P-052 required code that is no longer declared is reported",
              any("P-052 requires code Ineligible" in p for p in problems), str(problems))

        problems = plant(enum_text=real_enum.replace(
            "        SnapshotBackpressure = 21,\n", "        SnapshotBackpressure = 21,\n        Extra = 22,\n"))
        check("a second undocumented code is reported",
              any("Extra has no operator action" in p for p in problems), str(problems))

    if failures:
        print(f"emit_failure_codes.py --self-test FAILED ({failures} case(s))")
        return 1
    print("emit_failure_codes.py --self-test passed (8 cases).")
    return 0


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--output", metavar="PATH",
                        default=str(ROOT / "docs/operator" / DOCUMENT_NAME),
                        help="where to write the table (default: docs/operator/failure-codes.md)")
    parser.add_argument("--json", metavar="PATH", help="write the parsed code sets here as well")
    parser.add_argument("--check", action="store_true",
                        help="write nothing; fail if the committed table differs from the sources")
    parser.add_argument("--self-test", action="store_true",
                        help="falsify this generator against the real sources and planted defects, then exit")
    parser.add_argument("--root", default=str(ROOT), help="repository root (default: this file's parent)")
    args = parser.parse_args(argv[1:])

    if args.self_test:
        return self_test()

    document, report, problems = build_table(Path(args.root).resolve())

    if args.json:
        Path(args.json).parent.mkdir(parents=True, exist_ok=True)
        Path(args.json).write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")

    if problems:
        print(f"emit_failure_codes.py: the sources and the operator text disagree ({len(problems)}):",
              file=sys.stderr)
        for problem in problems:
            print(f"  - {problem}", file=sys.stderr)
        return 1

    output = Path(args.output)
    if args.check:
        if not output.exists():
            print(f"emit_failure_codes.py: {output} does not exist; run without --check to generate it",
                  file=sys.stderr)
            return 1
        committed = output.read_text(encoding="utf-8")
        if committed != document:
            print(f"emit_failure_codes.py: {output} is stale. Regenerate it.", file=sys.stderr)
            diff = difflib.unified_diff(committed.splitlines(), document.splitlines(),
                                        fromfile="committed", tofile="generated", lineterm="")
            for line in list(diff)[:40]:
                print(f"  {line}", file=sys.stderr)
            return 1
        print(f"failure-code table up to date: {output} "
              f"({len(report['diagnostic_codes'])} operation codes, "
              f"{len(report['catalog_codes'])} catalog codes, "
              f"{len(report['narrative_refusals'])} narrative refusals)")
        print("No Unity, no .NET and no player run was required by this check.")
        return 0

    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(document, encoding="utf-8")
    print(f"wrote {output} from source: {len(report['diagnostic_codes'])} operation codes "
          f"(P-052 requires {len(P052_REQUIRED)}), {len(report['catalog_codes'])} catalog codes, "
          f"{len(report['narrative_refusals'])} narrative refusals")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
