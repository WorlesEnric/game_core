#!/usr/bin/env python3
"""Data for the GC-030 release-readiness checks.

Pointers only. This module names *where* a fact is recorded and *what* the record has to say; it
never states a derived status itself. A status that this repository claims about V1 is computed by
`build_release_readiness.py` from the machine-readable records named here, and a revision, catalog
fingerprint or lock digest is always read out of the evidence file and re-checked against the
working tree by `check_revision_consistency.py`.

Two evidence roles exist, and the difference matters:

* `accepted` — the record declares the accepted V1 revision. Every such record must agree, and the
  catalogs, package manifest and package lock inside it must equal the working tree.
* `superseded` / `historical` — an earlier record that legitimately declares a different revision.
  It must be declared here with the reason it may differ; a differing digest is only tolerated when
  a `SUPERSEDED_DIGESTS` row names the exact value and says why it changed. A new, undeclared value
  is a failure, never a silent pass.
"""

# --- The claim being recorded -------------------------------------------------------------------

PROTOCOL_VERSION = "1.0"

#: The one status sentence GC-030 may record. `build_release_readiness.py` refuses to emit it unless
#: the accepted-revision evidence is complete apart from the single owner-deferred row below.
V1_STATUS_COMPLETE = "V1 complete (with owner-approved exception: TEST-023 timing deferred)"

#: The row the project owner approved as an exception, and how the deferral must be worded.
OWNER_EXCEPTION_ROW = "P-060"
OWNER_EXCEPTION_PHRASE = "Deferred by project-owner decision"
OWNER_DECISION_DATE = "2026-09-26"
OWNER_EXCEPTION_SENTENCE = (
    "TEST-023 full-duration timing qualification (P-060 timing rows) is Deferred by "
    "project-owner decision (2026-09-26); its correctness gates passed; the measured diagnostic "
    "numbers and the open whole-world/local prepare-cost miss at 10,000 targets are recorded as a "
    "known open performance issue for post-V1 work."
)

#: Gate statuses that may appear at the accepted revision. Anything else (Fail, Blocked, NotRun,
#: "unknown") makes the completion gate incomplete.
ALLOWED_GATE_STATUSES = ("Pass", "Deferred (owner decision)")

# --- Revision records ---------------------------------------------------------------------------

# format: "kv"   -> `key=value` line or `key: value` line
#         "json" -> JSON document, one key
#         "md"   -> regular expression over the text
ACCEPTED_REVISION_RECORDS = (
    {
        "path": "artifacts/w8-gate/reproduction/environment.txt",
        "format": "kv",
        "key": "revision",
        "describes": "the Wave 8 gate's accepted clean-clone reproduction host record",
    },
    {
        "path": "artifacts/conformance/compatibility.json",
        "format": "json",
        "key": "revision",
        "describes": "GC-028's canonical compatibility report (regenerated from the accepted tree)",
    },
    {
        "path": "artifacts/conformance/compatibility.md",
        "format": "md",
        "regex": r"\|\s*Source revision\s*\|\s*`([0-9a-f]{7,40})`",
        "describes": "the rendered form of the canonical compatibility report",
    },
    {
        "path": "artifacts/w8-gate/matrix/compatibility.json",
        "format": "json",
        "key": "revision",
        "describes": "the compatibility report built inside the accepted gate evidence tree",
    },
    {
        "path": "artifacts/w8-gate/matrix/compatibility.md",
        "format": "md",
        "regex": r"\|\s*Source revision\s*\|\s*`([0-9a-f]{7,40})`",
        "describes": "the rendered form of the accepted gate tree's compatibility report",
    },
    {
        "path": "artifacts/w8-gate/HANDOFF.md",
        "format": "md",
        "regex": r"Accepted source/reproduction revision:\s*`([0-9a-f]{7,40})`",
        "describes": "the Wave 8 gate handoff's declared accepted revision",
    },
    {
        "path": "artifacts/w8-gate/BUILD_REPORT.md",
        "format": "md",
        "regex": r"source/reproduction revision\s*`([0-9a-f]{7,40})`",
        "describes": "the Wave 8 gate build report's declared accepted revision",
    },
)

#: GC-029's clean-clone reproduction ran on its own branch revision. Its catalogs, package manifest
#: and package lock are byte-identical to the accepted revision (proved with `git diff --name-only`),
#: and the accepted gate re-ran the same script at the accepted revision, which is why GC-029's
#: published evidence is usable. The declared `superseded_by` record must declare the accepted
#: revision.
SUPERSEDED_REVISION_RECORDS = (
    {
        "path": "artifacts/reproducibility/final-clone/environment.txt",
        "format": "kv",
        "key": "revision",
        "superseded_by": "artifacts/w8-gate/reproduction/environment.txt",
        "content_identity": "catalogs_and_packages",
        "describes": "GC-029's final clean clone of its own branch (input revision of the archived "
                     "reproduction); content-identical on catalogs and package files",
    },
    {
        "path": "artifacts/gc-029/BUILD_REPORT.md",
        "format": "md",
        "regex": r"clean-checkout reproduction and operator contract\*\* at `([0-9a-f]{7,40})`",
        "superseded_by": "artifacts/w8-gate/reproduction/environment.txt",
        "describes": "GC-029's own reproduction report; the same revision and the same content "
                     "identity as the archived final clone",
    },
    {
        "path": "artifacts/conformance/results/compatibility.json",
        "format": "json",
        "key": "revision",
        "superseded_by": "artifacts/w8-gate/reproduction/environment.txt",
        "content_identity": "catalogs",
        "describes": "GC-028's canonical conformance matrix ran on its own branch revision; its "
                     "four generated catalogs and its package manifest are byte-identical to the "
                     "accepted revision and its resolved package graph is identical, while the "
                     "lock text differs in the local packages' nested dependency version strings "
                     "(0.1.0 to 1.0.0) that GC-029's packaging change caused, and the Wave 8 gate "
                     "re-ran the whole matrix at the accepted revision",
    },
)

#: Records that declare a different revision on purpose. Each row says why, and each one's catalogs
#: must still match the working tree (see `CATALOG_DIGEST_PROBE_GLOBS`); only the package files of
#: the older rows may differ, and only where `SUPERSEDED_DIGESTS` names the exact value.
HISTORICAL_REVISION_RECORDS = (
    {
        "path": "artifacts/reproducibility/first-clone/environment.txt",
        "format": "kv",
        "key": "revision",
        "reason": "an earlier clean clone of the GC-029 branch whose own run rewrote the committed "
                  "package lock; it was corrected and re-run, is not cited by any accepted gate "
                  "record, and must never be presented as gate evidence",
    },
    {
        "path": "artifacts/baseline/ENVIRONMENT.md",
        "format": "md",
        "regex": r"\|\s*Source revision\s*\|\s*`([0-9a-f]{7,40})`",
        "reason": "the GC-025 baseline player record, taken before GC-029 published package version "
                  "1.0.0; the manifest and lock digests it records therefore differ, while its "
                  "catalog digests do not",
    },
)

#: The GC-029 first clone is rejected evidence: the accepted gate report must still say so, so that
#: deleting the rejection sentence is caught rather than silently upgrading a rejected run.
REJECTION_DECLARATIONS = (
    {
        "record": "artifacts/reproducibility/first-clone/environment.txt",
        "declared_in": "artifacts/w8-gate/BUILD_REPORT.md",
        "marker": "it was not accepted",
        "describes": "the gate report's statement that the earlier fresh clone was not accepted",
    },
    {
        "record": "artifacts/reproducibility/first-clone/environment.txt",
        "declared_in": "artifacts/w8-gate/HANDOFF.md",
        "marker": "the clone was removed and a new clone of `3895d0c` ran the full reproduction",
        "describes": "the gate handoff's record that the earlier clone of `4eb1479` was discarded "
                     "and a fresh clone re-ran the reproduction",
    },
)

#: Paths whose *content* must be identical between a superseded revision and the accepted revision.
#: `PACKAGE_PATHS` holds the package manifest and lock; `CATALOG_PATHS` the four generated catalog
#: directories. A catalog may never legitimately differ; the package files may only differ when the
#: record's declared `content_identity` is limited to the catalogs and `SUPERSEDED_DIGESTS` explains
#: the package digests.
PACKAGE_PATHS = ("unity/GameCore.Validation/Packages",)

CATALOG_PATHS = (
    "unity/GameCore.Validation/Assets/GameCore.Validation/Generated",
    "unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards",
    "unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCheckpoint",
    "unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedTraversal",
)

CATALOG_PACKAGE_PATHS = PACKAGE_PATHS + CATALOG_PATHS

# --- Catalog and package identity ---------------------------------------------------------------

#: (catalog name, path). The `CatalogFileHash`, `CatalogFingerprint` and `ProtocolVersion` literals
#: inside each generated file are the recorded identity; the file's own sha256 is what the player
#: environment records call `catalog_sha256`.
CATALOGS = (
    ("ProbeCatalog",
     "unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs"),
    ("CardCatalog",
     "unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards/CardCatalog.g.cs"),
    ("CheckpointCatalog",
     "unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCheckpoint/CheckpointCatalog.g.cs"),
    ("TraversalCatalog",
     "unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedTraversal/TraversalCatalog.g.cs"),
)

#: The catalog whose file digest the player environment records call `catalog_sha256`
#: (`tools/unity/build_probe.sh` hashes exactly this file).
ENVIRONMENT_CATALOG_FILE = CATALOGS[0][1]

MANIFEST_PATH = "unity/GameCore.Validation/Packages/manifest.json"
LOCK_PATH = "unity/GameCore.Validation/Packages/packages-lock.json"

#: A disposable clone the gate script creates and deletes. Its manifest exists only during a run, so
#: a record that hashes it cannot be re-verified here; that is declared, not ignored.
RELEASE_PROJECT_MANIFEST = "unity/GameCore.ReleaseCheck/Packages/manifest.json"

#: Every record that declares a digest, with the kind each key carries.
#:   manifest -> sha256 of MANIFEST_PATH (or of the record's `manifest_path` override)
#:   lock     -> sha256 of LOCK_PATH
#:   catalog  -> sha256 of ENVIRONMENT_CATALOG_FILE
#:   player   -> recorded-only; all records that declare one must agree (the player binary is a
#:               build product and is not committed)
DIGEST_RECORDS = (
    {
        "path": "artifacts/w8-gate/matrix/toolchain/environment.txt",
        "format": "kv",
        "fields": (
            ("packages_manifest_sha256", "manifest"),
            ("catalog_sha256", "catalog"),
            ("player_sha256", "player"),
        ),
        "describes": "the accepted gate's qualification-player environment record",
    },
    {
        "path": "artifacts/w8-gate/matrix/release/environment.txt",
        "format": "kv",
        "manifest_path": RELEASE_PROJECT_MANIFEST,
        "fields": (
            ("packages_manifest_sha256", "manifest"),
            ("catalog_sha256", "catalog"),
            ("player_sha256", "player"),
        ),
        "describes": "the accepted gate's marker-free release environment record",
    },
    {
        "path": "artifacts/w8-gate/matrix/benchmark/environment.txt",
        "format": "kv",
        "fields": (("player_sha256", "player"),),
        "describes": "the accepted gate's short-diagnostic benchmark environment record",
    },
    {
        "path": "artifacts/conformance/results/toolchain/environment.txt",
        "format": "kv",
        "fields": (
            ("packages_manifest_sha256", "manifest"),
            ("catalog_sha256", "catalog"),
            ("player_sha256", "player"),
        ),
        "describes": "GC-028's canonical conformance tree, qualification-player record",
    },
    {
        "path": "artifacts/conformance/results/release/environment.txt",
        "format": "kv",
        "manifest_path": RELEASE_PROJECT_MANIFEST,
        "fields": (
            ("packages_manifest_sha256", "manifest"),
            ("catalog_sha256", "catalog"),
            ("player_sha256", "player"),
        ),
        "describes": "GC-028's canonical conformance tree, marker-free release record",
    },
    {
        "path": "artifacts/conformance/results/benchmark/environment.txt",
        "format": "kv",
        "fields": (("player_sha256", "player"),),
        "describes": "GC-028's canonical conformance tree, short-diagnostic benchmark record",
    },
    {
        "path": "artifacts/w7-gate/toolchain/environment.txt",
        "format": "kv",
        "fields": (
            ("packages_manifest_sha256", "manifest"),
            ("catalog_sha256", "catalog"),
            ("player_sha256", "player"),
        ),
        "describes": "the Wave 7 gate's qualification-player environment record",
    },
    {
        "path": "artifacts/w7-gate/release/environment.txt",
        "format": "kv",
        "manifest_path": RELEASE_PROJECT_MANIFEST,
        "fields": (
            ("packages_manifest_sha256", "manifest"),
            ("catalog_sha256", "catalog"),
            ("player_sha256", "player"),
        ),
        "describes": "the Wave 7 gate's marker-free release environment record",
    },
    {
        "path": "artifacts/baseline/build/qualification/environment.txt",
        "format": "kv",
        "fields": (
            ("packages_manifest_sha256", "manifest"),
            ("catalog_sha256", "catalog"),
            ("player_sha256", "player"),
        ),
        "describes": "the GC-025 baseline qualification-player record",
    },
    {
        "path": "artifacts/baseline/build/release/environment.txt",
        "format": "kv",
        "manifest_path": RELEASE_PROJECT_MANIFEST,
        "fields": (
            ("packages_manifest_sha256", "manifest"),
            ("catalog_sha256", "catalog"),
            ("player_sha256", "player"),
        ),
        "describes": "the GC-025 baseline release record",
    },
    {
        "path": "artifacts/baseline/build/environment.txt",
        "format": "kv",
        "fields": (
            ("packages_manifest_sha256", "manifest"),
            ("packages_lock_sha256", "lock"),
        ),
        "describes": "the GC-025 baseline build record",
    },
    {
        "path": "artifacts/performance/environment.txt",
        "format": "kv",
        "fields": (("player_sha256", "player"),),
        "describes": "the GC-026 benchmark environment record",
    },
)

#: A digest that differs from the working tree is only accepted when this table names the exact
#: recorded value and the change that explains it. Catalog digests are deliberately absent here:
#: a catalog may never legitimately drift.
SUPERSEDED_DIGESTS = (
    {
        "path": "artifacts/baseline/build/environment.txt",
        "field": "packages_manifest_sha256",
        "value": "301020ec2f42426b20deccfbdc2deb60abd28ef3b811d7c3e2bc7e23af8f2b84",
        "reason": "GC-025 recorded the baseline before GC-029 published package version 1.0.0 and "
                  "asmdef-derived dependencies; GC-029 then rewrote the manifest",
    },
    {
        "path": "artifacts/baseline/build/environment.txt",
        "field": "packages_lock_sha256",
        "value": "726e06ab3dd55e5d70974779298f3835e0afac1aec83c6a26c821e437edfea63",
        "reason": "the same packaging change: GC-029 committed the pinned Editor's actual resolved "
                  "dependency graph, changing the lock",
    },
    {
        "path": "artifacts/baseline/build/qualification/environment.txt",
        "field": "packages_manifest_sha256",
        "value": "301020ec2f42426b20deccfbdc2deb60abd28ef3b811d7c3e2bc7e23af8f2b84",
        "reason": "the same pre-packaging manifest as the GC-025 baseline build record",
    },
)

#: Digests that cannot be re-derived here because the file they hash is a disposable build product.
UNVERIFIABLE_DIGESTS = (
    {
        "path": "artifacts/w8-gate/matrix/release/environment.txt",
        "field": "packages_manifest_sha256",
        "value": "b2b41d61bb1fe5bda3000598ca660bb5dcf045907c546b470abe24c33c25583f",
        "reason": "hashes the manifest of the disposable GameCore.ReleaseCheck clone, which the gate "
                  "creates and deletes; only the qualification project's manifest is committed",
    },
    {
        "path": "artifacts/w7-gate/release/environment.txt",
        "field": "packages_manifest_sha256",
        "value": "b2b41d61bb1fe5bda3000598ca660bb5dcf045907c546b470abe24c33c25583f",
        "reason": "same disposable release clone; the recorded value is kept only so that a change "
                  "to the record is visible",
    },
    {
        "path": "artifacts/baseline/build/release/environment.txt",
        "field": "packages_manifest_sha256",
        "value": "acce07046a69e238f774b4e137393a5fe8f21ddb7e2e271cf7a6ed1dd4c4c26d",
        "reason": "same disposable release clone, from the GC-025 baseline",
    },
    {
        "path": "artifacts/conformance/results/release/environment.txt",
        "field": "packages_manifest_sha256",
        "value": "b2b41d61bb1fe5bda3000598ca660bb5dcf045907c546b470abe24c33c25583f",
        "reason": "same disposable release clone, from the GC-028 canonical conformance tree",
    },
)

#: Player documents that carry the catalog identity of the exact catalog they ran against. Every
#: document matched here must reproduce the working tree's `CatalogFileHash`/`CatalogFingerprint`,
#: which is how "the same catalog revision" is proven for the accepted and historical runs alike.
CATALOG_DIGEST_PROBE_GLOBS = (
    "artifacts/w8-gate/matrix/toolchain/probe-*.json",
    "artifacts/w8-gate/matrix/release/probe-*.json",
    "artifacts/w7-gate/toolchain/probe-*.json",
    "artifacts/w7-gate/release/probe-*.json",
    "artifacts/reproducibility/final-clone/probe/probe-*.json",
    "artifacts/reproducibility/final-clone/release/probe-*.json",
    "artifacts/conformance/results/toolchain/probe-*.json",
    "artifacts/conformance/results/release/probe-*.json",
    "artifacts/baseline/build/probe/probe-*.json",
)

#: Names a probe corpus may contain without declaring a catalog identity (replay trace sidecars are
#: data, not verdict documents; `w8-gate/HANDOFF.md` records the same exclusion for the index).
CATALOG_DIGEST_EXCLUDED_SUFFIXES = (".trace.json",)

#: Catalog ledgers: JSON documents that list all four catalogs with their recorded digests.
CATALOG_LEDGER_RECORDS = (
    {
        "path": "artifacts/baseline/build/qualification/catalog-ledger.json",
        "describes": "the GC-025 baseline qualification build ledger",
    },
    {
        "path": "artifacts/baseline/build/release/catalog-ledger.json",
        "describes": "the GC-025 baseline release build ledger",
    },
    {
        "path": "artifacts/baseline/catalog-reachability.json",
        "describes": "the committed catalog reachability manifest",
    },
)

#: Local package manifests whose version must equal the accepted packaging version.
PACKAGE_MANIFEST_GLOB = "Packages/com.gamecore.*/package.json"
ACCEPTED_PACKAGE_VERSION = "1.0.0"

# --- Per-gate record registry -------------------------------------------------------------------

#: Pointer data for the evidence manifest: which report and which result tree belong to each gate,
#: how to read the gate's own verdict sentence out of its report (a marker the report must contain,
#: or None when the report records commands and results but no single verdict), and where the report
#: declares its revision (a regular expression, or None when it declares none). No status is stated
#: here: the manifest's statuses come from the accepted requirement index, the accepted suite matrix
#: and these markers.
GATE_RECORDS = (
    {"id": "GC-001", "kind": "task", "report": "artifacts/gc-001/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-001",),
     "verdict_marker": "Pass for GC-001's Linux IL2CPP qualification scope",
     "revision_prefix": "Started from `origin/gc-001` at `",
     "revision_suffix": "`"},
    {"id": "GC-002", "kind": "task", "report": "artifacts/gc-002/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-002",),
     "verdict_marker": "Current result: Round 2 passes",
     "revision_prefix": "Started from `origin/gc-002` at `",
     "revision_suffix": "`"},
    {"id": "GC-003", "kind": "task", "report": "artifacts/gc-003/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-003",),
     "verdict_marker": "Pass for the requested GC-003 Linux build/test gates",
     "revision_prefix": "The starting revision was `",
     "revision_suffix": "`"},
    {"id": "GC-004", "kind": "task", "report": "artifacts/gc-004/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-004",),
     "verdict_marker": "Release build Pass; 124 tests passed, 0 failed, 0 skipped; "
                       "cancellation identity blocker resolved",
     "revision_prefix": "Starting revision: `",
     "revision_suffix": "`"},
    {"id": "GC-005", "kind": "task", "report": "artifacts/gc-005/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-005",),
     "verdict_marker": "Pass for the GC-005 build-host gates",
     "revision_prefix": None,
     "revision_suffix": None,
     "note": "the report records the checked-out branch and its commands, not a revision hash"},
    {"id": "GC-006", "kind": "task", "report": "artifacts/gc-006/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-006",),
     "verdict_marker": None,
     "revision_prefix": "Starting revision tested after reset: `",
     "revision_suffix": "`",
     "note": "the report records host facts and the static-check result, not one verdict sentence"},
    {"id": "GC-007", "kind": "task", "report": "artifacts/gc-007/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-007",),
     "verdict_marker": None,
     "revision_prefix": None,
     "revision_suffix": None,
     "note": "the report records the host, the commands and their outputs, not one verdict"},
    {"id": "GC-008", "kind": "task", "report": "artifacts/gc-008/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-008",),
     "verdict_marker": None,
     "revision_prefix": None,
     "revision_suffix": None,
     "note": "the report records the host, the commands and their outputs, not one verdict"},
    {"id": "GC-009", "kind": "task", "report": "artifacts/gc-009/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-009",),
     "verdict_marker": None,
     "revision_prefix": "reset --hard origin/gc-009` at `",
     "revision_suffix": "`",
     "note": "the report records the checkout commands and their outputs, not one verdict"},
    {"id": "GC-010", "kind": "task", "report": "artifacts/gc-010/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-010",),
     "verdict_marker": None,
     "revision_prefix": "Source revision initially `",
     "revision_suffix": "`",
     "note": "the report records host facts and per-suite results, not one verdict sentence"},
    {"id": "GC-011", "kind": "task", "report": "artifacts/gc-011/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-011",),
     "verdict_marker": "Result: Pass for the GC-011 card slice and every executed regression suite",
     "revision_prefix": None,
     "revision_suffix": None,
     "note": "the report records the checkout and the executed suites, not a revision hash"},
    {"id": "GC-012", "kind": "task", "report": "artifacts/gc-012/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-012",),
     "verdict_marker": None,
     "revision_prefix": "Baseline fetched/reset as requested: `",
     "revision_suffix": "`",
     "note": "the report records the baseline hash and per-command results, not one verdict"},
    {"id": "GC-013", "kind": "task", "report": "artifacts/gc-013/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-013",),
     "verdict_marker": None,
     "revision_prefix": "Source base: `",
     "revision_suffix": "`",
     "note": "the report records the source base and per-command results, not one verdict"},
    {"id": "GC-014", "kind": "task", "report": "artifacts/gc-014/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-014",),
     "verdict_marker": None,
     "revision_prefix": "Initial commit: `",
     "revision_suffix": "`",
     "note": "the report records the host and per-command results, not one verdict sentence"},
    {"id": "GC-015", "kind": "task", "report": "artifacts/gc-015/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-015",),
     "verdict_marker": None,
     "revision_prefix": "reset to `origin/gc-015` at `",
     "revision_suffix": "`",
     "note": "the report records the branch reset and per-command results, not one verdict"},
    {"id": "GC-016", "kind": "task", "report": "artifacts/gc-016/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-016",),
     "verdict_marker": None,
     "revision_prefix": None,
     "revision_suffix": None,
     "note": "the report records the checkout command (`git fetch origin && git checkout gc-016 && "
             "git reset --hard origin/gc-016`, as requested) and per-command results, but no "
             "revision hash"},
    {"id": "GC-017", "kind": "task", "report": "artifacts/gc-017/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-017",),
     "verdict_marker": None,
     "revision_prefix": None,
     "revision_suffix": None,
     "note": "the report records the executed commands and their outputs, not one verdict"},
    {"id": "GC-018", "kind": "task", "report": "artifacts/gc-018/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-018",),
     "verdict_marker": "Result: GREEN on the Linux build host",
     "revision_prefix": "Source revision when the runs were executed: `",
     "revision_suffix": "`"},
    {"id": "GC-019", "kind": "task", "report": "artifacts/gc-019/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-019",),
     "verdict_marker": None,
     "revision_prefix": "reset --hard origin/gc-019` at `",
     "revision_suffix": "`",
     "note": "the report records the host and per-gate results, not one verdict sentence"},
    {"id": "GC-020", "kind": "task", "report": "artifacts/gc-020/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-020",),
     "verdict_marker": None,
     "revision_prefix": None,
     "revision_suffix": None,
     "note": "the report records the host, the checkout command and per-gate results, not one "
             "verdict; the accepted index carries the clause-level status instead"},
    {"id": "GC-021", "kind": "task", "report": "artifacts/gc-021/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-021",),
     "verdict_marker": None,
     "revision_prefix": None,
     "revision_suffix": None,
     "note": "the report records the toolchain and per-command results, not one verdict sentence"},
    {"id": "GC-022", "kind": "task", "report": "artifacts/gc-022/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-022",),
     "verdict_marker": "Pass** on branch `gc-022` at evidence commit",
     "revision_prefix": "Revision used by the passing gate: `",
     "revision_suffix": "`"},
    {"id": "GC-023", "kind": "task", "report": "artifacts/gc-023/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-023",),
     "verdict_marker": "Pass for executed builds and suites; full TEST-023 performance protocol "
                       "NotRun",
     "revision_prefix": None,
     "revision_suffix": None,
     "note": "the task's own report left the full TEST-023 performance protocol NotRun; at the "
             "accepted revision the owner decision records that row as Deferred (owner decision), "
             "which is what the index below reports"},
    {"id": "GC-024", "kind": "task", "report": "artifacts/gc-024/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-024",),
     "verdict_marker": None,
     "revision_prefix": None,
     "revision_suffix": None,
     "note": "the report records the host, the owner's repetition-cap change and per-gate results, "
             "not one verdict sentence"},
    {"id": "GC-025", "kind": "task", "report": "artifacts/gc-025/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-025",),
     "verdict_marker": "Verdict: Pass for the selected StandaloneLinux64 IL2CPP/High/Burst profile",
     "revision_prefix": None,
     "revision_suffix": None},
    {"id": "GC-026", "kind": "task", "report": "artifacts/gc-026/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-026",),
     "verdict_marker": "Status: partial; full TEST-023 benchmark qualification is Blocked",
     "revision_prefix": None,
     "revision_suffix": None,
     "note": "the task's own report left the full benchmark qualification Blocked; the project "
             "owner then decided the timing qualification is deferred, and the accepted index "
             "reports that single row as Deferred (owner decision)"},
    {"id": "GC-027", "kind": "task", "report": "artifacts/gc-027/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-027",),
     "verdict_marker": "Pass for the executed GC-027 qualification and marker-free release checks",
     "revision_prefix": None,
     "revision_suffix": None},
    {"id": "GC-028", "kind": "task", "report": "artifacts/gc-028/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-028", "artifacts/conformance"),
     "verdict_marker": "the GC-028 executable matrix is 24/24 Pass",
     "revision_prefix": None,
     "revision_suffix": None,
     "note": "the report defers the source revision to the compatibility report, which records the "
             "accepted revision checked by the consistency tool"},
    {"id": "GC-029", "kind": "task", "report": "artifacts/gc-029/BUILD_REPORT.md",
     "evidence": ("artifacts/gc-029", "artifacts/reproducibility", "docs/operator",
                  "tools/reproduce.sh", "tools/check_package_metadata.py",
                  "tools/emit_failure_codes.py", "tools/check_operator_docs.py"),
     "verdict_marker": "Pass for the GC-029 clean-checkout reproduction and operator contract",
     "revision_prefix": "clean-checkout reproduction and operator contract** at `",
     "revision_suffix": "`",
     "note": "recorded at GC-029's own branch revision; the catalogs, manifest and lock are "
             "byte-identical to the accepted revision and the accepted gate re-ran the same script "
             "at the accepted revision"},
    {"id": "GC-030", "kind": "task", "report": "artifacts/release-readiness/HANDOFF.md",
     "evidence": ("artifacts/release-readiness",),
     "verdict_marker": None,
     "revision_prefix": None,
     "revision_suffix": None,
     "note": "this task's own record; its status is the status this manifest computes"},
)

#: Wave gates. Requirements and tests are derived from `docs/game-core/traceability.json` by the
#: wave each task is registered in, so a wave's coverage cannot drift from the registry.
WAVE_RECORDS = (
    {"id": "W1", "wave": 1, "report": "artifacts/w1-gate/BUILD_REPORT.md",
     "evidence": ("artifacts/w1-gate",),
     "verdict_marker": "Result: Pass on the tested revision",
     "revision_prefix": "Result: Pass on the tested revision** `",
     "revision_suffix": "`"},
    {"id": "W2", "wave": 2, "report": "artifacts/w2-gate/BUILD_REPORT.md",
     "evidence": ("artifacts/w2-gate",),
     "verdict_marker": "Result: Pass.",
     "revision_prefix": "Starting revision was `",
     "revision_suffix": "`"},
    {"id": "W3", "wave": 3, "report": "artifacts/w3-gate/BUILD_REPORT.md",
     "evidence": ("artifacts/w3-gate",),
     "verdict_marker": None,
     "revision_prefix": "Source initially reset to `origin/w3-gate` at `",
     "revision_suffix": "`",
     "note": "the report states the gate's commands, dates and results without a single verdict "
             "sentence; the accepted index carries the requirement status"},
    {"id": "W4", "wave": 4, "report": "artifacts/w4-gate/BUILD_REPORT.md",
     "evidence": ("artifacts/w4-gate",),
     "verdict_marker": "Pass — Wave 4 integration gate.",
     "revision_prefix": "Starting revision: `",
     "revision_suffix": "`"},
    {"id": "W5", "wave": 5, "report": "artifacts/w5-gate/BUILD_REPORT.md",
     "evidence": ("artifacts/w5-gate",),
     "verdict_marker": "Verdict: Pass for the scripted Wave 5 integration gate.",
     "revision_prefix": "Starting revision `",
     "revision_suffix": "`",
     "note": "the gate's verdict covers its scripted integration gate; P-049 and O-22 were Partial "
             "at that revision and are carried by the later accepted index"},
    {"id": "W6", "wave": 6, "report": "artifacts/w6-gate/BUILD_REPORT.md",
     "evidence": ("artifacts/w6-gate",),
     "verdict_marker": "Pass for the executed Wave 6 gate",
     "revision_prefix": "accepted gate ran from code commit `",
     "revision_suffix": "`"},
    {"id": "W7", "wave": 7, "report": "artifacts/w7-gate/BUILD_REPORT.md",
     "evidence": ("artifacts/w7-gate",),
     "verdict_marker": "Result: PASS for the Wave 7 integration gate",
     "revision_prefix": "Wave 7 integration gate on `w7-gate` (`",
     "revision_suffix": "`"},
    {"id": "W8", "wave": 8, "report": "artifacts/w8-gate/BUILD_REPORT.md",
     "evidence": ("artifacts/w8-gate",),
     "verdict_marker": "Pass for the specified Wave 8 integration gate",
     "revision_prefix": "source/reproduction revision `",
     "revision_suffix": "`"},
    {"id": "W9", "wave": 9, "report": "artifacts/release-readiness/HANDOFF.md",
     "evidence": ("artifacts/release-readiness",),
     "verdict_marker": None,
     "revision_prefix": None,
     "revision_suffix": None,
     "note": "the completion gate this task records; its status is the status this manifest "
             "computes"},
)

# --- Accepted-revision machine-readable sources for the completion condition ---------------------

#: The accepted revision has two independent full evidence trees. Both are read, and their derived
#: facts (requirement statuses, suite verdicts, family verdicts, diagnostic budget rows, catalog
#: digests) must agree; only the recorded root path and revision may differ.
ACCEPTED_EVIDENCE_TREES = (
    {
        "root": "artifacts/w8-gate/matrix",
        "label": "Wave 8 gate matrix tree (the gate's own run at the accepted revision)",
    },
    {
        "root": "artifacts/conformance/results",
        "label": "GC-028 canonical conformance tree (GC-028's own run; superseded revision, "
                 "identical catalogs and resolved package graph)",
    },
)
EVIDENCE_INDEX_NAME = "evidence-index.json"
SUITE_MATRIX_NAME = "suite-status.json"
FAMILY_AUDIT_NAME = "family-audit.json"
BENCHMARK_SUMMARY_NAME = "benchmark/summarize.json"

#: The published summary GC-028 generated from the accepted gate tree (its `evidenceRoot` must say
#: which tree it came from, so a summary built from a stale tree cannot pass unnoticed).
CANONICAL_EVIDENCE_INDEX = "artifacts/conformance/" + EVIDENCE_INDEX_NAME
W7_DERIVED_SUITE_MATRIX = "artifacts/conformance/suite-status-w7-gate.json"
BUDGET_DECISION_RECORD = "artifacts/performance/BUDGET_DECISIONS.md"
BUDGET_TEMPLATE_SNAPSHOT = "artifacts/w8-gate/matrix/benchmark/BUDGET_DECISIONS.md"
DECISION_DOC = "docs/game-core/10-decisions-and-open-questions.md"
SUPPORTED_PROFILE = "artifacts/release-readiness/supported-profile.md"
TRACEABILITY = "docs/game-core/traceability.json"

#: The accepted suite matrix must report every required TEST suite as Pass.
EXPECTED_SUITE_TOTAL = 24
EXPECTED_REQUIREMENT_TOTAL = 60
EXPECTED_OPERATION_TOTAL = 26

#: The short diagnostic the owner accepted as the correctness evidence for TEST-023. Its shape is
#: asserted so that a longer, or a differently shaped, run cannot be substituted silently.
EXPECTED_DIAGNOSTIC_GATES = 19
EXPECTED_DIAGNOSTIC_WORKLOADS = 11
EXPECTED_DIAGNOSTIC_REPETITIONS = 5
EXPECTED_DIAGNOSTIC_MISSED_TARGETS = ("budget.whole-world-preparation-p95",)

#: The budget row the deferral covers; the decision record must name it.
DEFERRED_BUDGET_ROW = EXPECTED_DIAGNOSTIC_MISSED_TARGETS[0]

#: Corpus problems that an accepted tree's own evidence index reports, each with the reason it is
#: not a requirement failure. A problem that is not declared here fails the completion condition.
KNOWN_CORPUS_PROBLEMS = (
    {
        "tree": "artifacts/conformance/results",
        "problem": "unreadable probe result: artifacts/conformance/results/toolchain/"
                   "probe-replay.json.trace.json",
        "reason": "GC-028's canonical tree was indexed before the replay trace sidecar exclusion; "
                  "a sidecar is frame data, not a verdict document, and the accepted gate tree's "
                  "index (built after that fix, which the Wave 8 gate report records) reports no "
                  "corpus problems at all",
    },
    {
        "tree": "artifacts/conformance/results",
        "problem": "unreadable probe result: artifacts/conformance/results/toolchain/"
                   "probe-replay.json.run2.trace.json",
        "reason": "the second run's trace sidecar: same exclusion, same stale index",
    },
)

#: The W7-derived suite matrix is a historical derivation and must never be read as the accepted
#: suite status; the tool asserts that the accepted trees carry their own matrices.
W7_DERIVED_MATRIX_NOTE = (
    "derived from the Wave 7 gate corpus, not from a run at the accepted revision; it is recorded "
    "for history and is not accepted-revision evidence"
)

#: Documents that state the implementation status. Each entry is checked against the status this
#: tool computes, so a document cannot claim completion while the evidence says otherwise.
#:   mode "status"  -> a regular expression whose single capture must equal V1_STATUS_COMPLETE
#:   mode "mention" -> the exact status sentence must appear
DOCS_STATUS_SOURCES = (
    {"path": DECISION_DOC, "mode": "status",
     "regex": r"V1 status:\s*(.+?)\s*\.",
     "describes": "the binding decision entry that records the owner-approved exception"},
    {"path": "docs/game-core/README.md", "mode": "mention"},
    {"path": "docs/game-core/09-implementation-guide.md", "mode": "mention"},
    {"path": "docs/game-core/00-core-protocols.md", "mode": "mention"},
    {"path": "docs/game-core/07-reference-compositions.md", "mode": "mention"},
    {"path": "README.md", "mode": "mention"},
    {"path": SUPPORTED_PROFILE, "mode": "mention"},
    {"path": "docs/operator/deferred-scope.md", "mode": "exception"},
)

#: Written by the builder; both files are generated, never hand-edited.
MANIFEST_JSON = "evidence-manifest.json"
MANIFEST_MD = "evidence-manifest.md"
STATUS_JSON = "status.json"
