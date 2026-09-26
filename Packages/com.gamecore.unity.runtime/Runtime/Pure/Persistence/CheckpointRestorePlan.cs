// GameCore.Unity.Runtime (engine-free part, compiled as GameCore.Execution) - restore validation and plan (GC-018).
//
// Normative sources: 00 O-21 ("validate schema/catalog, rebuild identities/composition, repair references, restore
// state and cursors before Running"; "Unsupported schema/migration rejects without affecting existing world";
// "duplicate returns same restored session". Probe: "old callback cannot target restored entity" — P-049,
// P-053–P-055), P-004 (a new session id, and installed/spawn keys come from the checkpoint rather than creation
// order), P-049 (recovery creates a new WorldId; old callbacks and handles never become valid), P-054 (generated
// serializers and directed migrations; unknown required schema/content rejects) and 06 s7 ("Restore first validates
// catalog/schema versions and directed migration paths. Build an unexposed world with a fresh WorldId, reconstruct
// scope/target stable IDs, rederive capabilities, allocate recipes, restore owner slots, repair stable references,
// restore clocks/RNG/outbox cursors, then publish.").
//
// This file is the *validation and planning* half: it turns a verified document into an ordered, fully checked plan
// that names exactly what must be rebuilt, and refuses — with one actionable reason — anything it cannot rebuild.
// Nothing here allocates a world; the Unity half applies the plan to an unexposed staging world.
//
// The order of the checks is deliberate and is the order the protocol states them in: catalog/protocol identity
// first (a document from another content revision is not this build's document at all), then the schema/migration
// graph, then identities and references, then the per-kind content requirements. A restore therefore reports the
// cheapest decisive reason rather than a symptom list.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Execution.Persistence
{
    /// <summary>Why a restore could not produce a plan; every value maps to a protocol code (P-052).</summary>
    public enum RestoreRefusal
    {
        None = 0,

        /// <summary>The document is not this build's protocol major/minor (P-055).</summary>
        UnsupportedProtocol = 1,

        /// <summary>The document names another catalog fingerprint: its content revision is not this build's (P-028).</summary>
        CatalogMismatch = 2,

        /// <summary>A declared schema version has no unique path to this build's version (P-054).</summary>
        MigrationRejected = 3,

        /// <summary>The document declares no scope tree, or one that is not a single rooted tree (P-010).</summary>
        InvalidComposition = 4,

        /// <summary>A required identity or reference does not resolve inside the document (P-004, 05 s6).</summary>
        CorruptReference = 5,

        /// <summary>The document does not carry the state its own declarations require (P-032).</summary>
        MissingRequiredState = 6,

        /// <summary>The reserved session is already live or already reserved (P-050).</summary>
        ReservationRejected = 7,

        /// <summary>
        /// The document's outbox section contradicts itself: two rows claim one obligation identity or one
        /// destination cursor, or a cursor disagrees with the terminal records it summarises. An ambiguous outbox is
        /// refused rather than restored, because a restore that guessed which obligation was live could deliver a
        /// mutation twice (GC-021, P-045, P-053).
        /// </summary>
        InvalidOutbox = 8,
    }

    /// <summary>
    /// Everything one restore will rebuild, in the order it must be rebuilt. Every list holds stable identities and
    /// values only; no member of this plan is a runtime handle, an entity index or a lease (P-005).
    /// </summary>
    public sealed class RestorePlan
    {
        internal RestorePlan(
            WorldId targetSession,
            HeaderRecordValue header,
            CheckpointIdentityTable identities,
            IReadOnlyList<ScopeRecordValue> scopes,
            IReadOnlyList<InstallRecordValue> installs,
            IReadOnlyList<SelectionRecordValue> selections,
            IReadOnlyList<TargetRecordValue> targets,
            IReadOnlyList<SlotRecordValue> slots,
            IReadOnlyList<GrantRecordValue> grants,
            IReadOnlyList<ClockRecordValue> clocks,
            IReadOnlyList<CommandRecordValue> commands,
            IReadOnlyList<MessageRecordValue> messages,
            IReadOnlyList<RngRecordValue> rngStreams,
            IReadOnlyList<OutboxRecordValue> outbox,
            IReadOnlyList<CursorRecordValue> cursors,
            IReadOnlyList<MigrationPlan> migrations,
            ContentHash documentHash)
        {
            TargetSession = targetSession;
            Header = header;
            Identities = identities;
            Scopes = scopes;
            Installs = installs;
            Selections = selections;
            Targets = targets;
            Slots = slots;
            Grants = grants;
            Clocks = clocks;
            Commands = commands;
            Messages = messages;
            RngStreams = rngStreams;
            Outbox = outbox;
            Cursors = cursors;
            Migrations = migrations;
            DocumentHash = documentHash;
        }

        /// <summary>The fresh caller-reserved session this plan restores into; never the captured session (P-004).</summary>
        public WorldId TargetSession { get; }

        public HeaderRecordValue Header { get; }

        public CheckpointIdentityTable Identities { get; }

        /// <summary>Scopes in document order; the first is the root, which the registry validates on rebuild (P-010).</summary>
        public IReadOnlyList<ScopeRecordValue> Scopes { get; }

        public IReadOnlyList<InstallRecordValue> Installs { get; }

        public IReadOnlyList<SelectionRecordValue> Selections { get; }

        public IReadOnlyList<TargetRecordValue> Targets { get; }

        /// <summary>Active and dormant slots; dormant rows are restored with `Active == false` (P-032).</summary>
        public IReadOnlyList<SlotRecordValue> Slots { get; }

        public IReadOnlyList<GrantRecordValue> Grants { get; }

        public IReadOnlyList<ClockRecordValue> Clocks { get; }

        /// <summary>Queued commands to re-admit after publication, or empty when the capture rejected them (P-053).</summary>
        public IReadOnlyList<CommandRecordValue> Commands { get; }

        /// <summary>Bounded next-step messages to re-stage before the first restored step (P-043).</summary>
        public IReadOnlyList<MessageRecordValue> Messages { get; }

        public IReadOnlyList<RngRecordValue> RngStreams { get; }

        public IReadOnlyList<CursorRecordValue> Cursors { get; }

        /// <summary>
        /// Outbox rows to reinstate before the restored world is exposed: open obligations, their terminal records
        /// and the per-destination delivery cursors (GC-021, P-053). Reinstating them is what makes a committed
        /// delivery obligation survive the source world's unload (P-045).
        /// </summary>
        public IReadOnlyList<OutboxRecordValue> Outbox { get; }

        /// <summary>Non-empty migrations, each with a unique path, to run before the state is written (P-054).</summary>
        public IReadOnlyList<MigrationPlan> Migrations { get; }

        /// <summary>SHA-256 of the document this plan was built from, so a caller can prove which blob it restores.</summary>
        public ContentHash DocumentHash { get; }

        /// <summary>Counts of what will be rebuilt, in the header's declaration order (P-053).</summary>
        public CheckpointCounts Counts => new CheckpointCounts(
            Scopes.Count,
            Installs.Count,
            Selections.Count,
            Targets.Count,
            Slots.Count,
            Grants.Count,
            Clocks.Count,
            Commands.Count,
            Messages.Count,
            RngStreams.Count,
            Cursors.Count,
            Outbox.Count);

        /// <summary>Dormant slots of this plan, which a restore must retain rather than skip (P-032).</summary>
        public int DormantSlotCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Slots.Count; i++)
                {
                    if (!Slots[i].Active)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>True when the plan needs no migration step and every declared reference resolved (P-053).</summary>
        public bool IsDirect => Migrations.Count == 0;

        public override string ToString() =>
            "restorePlan(" + TargetSession.Session.ToString() + "," + Counts.ToString()
            + ",dormant=" + DormantSlotCount.ToString(CultureInfo.InvariantCulture)
            + ",migrations=" + Migrations.Count.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>The outcome of one restore validation: a plan, or the single reason it was refused (P-052).</summary>
    public sealed class RestorePlanResult
    {
        private RestorePlanResult(
            RestorePlan? plan,
            RestoreRefusal refusal,
            DiagnosticCode code,
            string detail,
            IReadOnlyList<Diagnostic>? diagnostics)
        {
            Plan = plan;
            Refusal = refusal;
            Code = code;
            Detail = detail;
            Diagnostics = diagnostics ?? Array.Empty<Diagnostic>();
        }

        /// <summary>Null exactly when the restore was refused; a partial plan is never produced (P-053).</summary>
        public RestorePlan? Plan { get; }

        public RestoreRefusal Refusal { get; }

        public DiagnosticCode Code { get; }

        public string Detail { get; }

        /// <summary>Every corrupt reference found, when the refusal is a reference failure (P-052).</summary>
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public bool Succeeded => Plan != null;

        internal static RestorePlanResult Success(RestorePlan plan) =>
            new RestorePlanResult(plan, RestoreRefusal.None, DiagnosticCode.None, string.Empty, null);

        internal static RestorePlanResult Refused(
            RestoreRefusal refusal,
            DiagnosticCode code,
            string detail) =>
            new RestorePlanResult(null, refusal, code, detail, null);

        internal static RestorePlanResult Corrupt(
            RestoreRefusal refusal,
            DiagnosticCode code,
            string detail,
            IReadOnlyList<Diagnostic> diagnostics) =>
            new RestorePlanResult(null, refusal, code, detail, diagnostics);

        public override string ToString() =>
            Succeeded ? Plan!.ToString() : "refused(" + Refusal.ToString() + ":" + DiagnosticCodeText.Of(Code) + ")";
    }

    /// <summary>One restore request: the document, the fresh session and the catalog surface it restores into.</summary>
    public sealed class CheckpointRestoreRequest
    {
        public CheckpointRestoreRequest(
            WorldId targetSession,
            CheckpointDocument document,
            CheckpointCodecSet codecs,
            CheckpointMigrationRegistry migrations,
            ContentHash catalogFingerprint,
            IReadOnlyList<SchemaRef>? allocatedSchemas = null,
            bool requireCatalogMatch = true)
        {
            TargetSession = targetSession;
            Document = document ?? throw new ArgumentNullException(nameof(document));
            Codecs = codecs ?? throw new ArgumentNullException(nameof(codecs));
            Migrations = migrations ?? throw new ArgumentNullException(nameof(migrations));
            CatalogFingerprint = catalogFingerprint;
            AllocatedSchemas = allocatedSchemas;
            RequireCatalogMatch = requireCatalogMatch;
        }

        public WorldId TargetSession { get; }

        public CheckpointDocument Document { get; }

        public CheckpointCodecSet Codecs { get; }

        public CheckpointMigrationRegistry Migrations { get; }

        /// <summary>Fingerprint of the catalog this build carries; compared with the document's own (P-028).</summary>
        public ContentHash CatalogFingerprint { get; }

        /// <summary>
        /// Schemas this build can allocate, as (id, current version) pairs. When supplied, every captured schema
        /// version is planned against its current version and a missing or ambiguous path refuses the restore (P-054).
        /// </summary>
        public IReadOnlyList<SchemaRef>? AllocatedSchemas { get; }

        /// <summary>
        /// True when a catalog-fingerprint mismatch refuses. It defaults to true and is only relaxed by a caller that
        /// has independently proven the content is compatible, because restoring state into a different content
        /// revision would rewrite saved state behind the caller's back (06 s7).
        /// </summary>
        public bool RequireCatalogMatch { get; }
    }

    /// <summary>Validates one verified document into a complete, ordered restore plan (O-21, P-053, P-054).</summary>
    public static class CheckpointRestorePlanner
    {
        /// <summary>
        /// Validates and plans one restore. A refusal never yields a partial plan, and never mutates anything: the
        /// caller stages a world only after this succeeds (O-21's "rejects without affecting existing world").
        /// </summary>
        public static RestorePlanResult Plan(CheckpointRestoreRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            CheckpointDocument document = request.Document;
            HeaderRecordValue header = document.Header;

            if (!header.IsSupportedProtocol)
            {
                return RestorePlanResult.Refused(
                    RestoreRefusal.UnsupportedProtocol,
                    DiagnosticCode.UnsupportedVersion,
                    "the checkpoint declares protocol "
                    + header.ProtocolMajor.ToString(CultureInfo.InvariantCulture) + "."
                    + header.ProtocolMinor.ToString(CultureInfo.InvariantCulture) + " and this build supports "
                    + CheckpointFormat.ProtocolMajor.ToString(CultureInfo.InvariantCulture) + "."
                    + CheckpointFormat.ProtocolMinor.ToString(CultureInfo.InvariantCulture)
                    + "; a document from another protocol major is not restored (P-055).");
            }

            if (request.TargetSession.Session.IsDefault)
            {
                return RestorePlanResult.Refused(
                    RestoreRefusal.ReservationRejected,
                    DiagnosticCode.UnsupportedVersion,
                    "a restore requires a caller-reserved fresh session id (P-050).");
            }

            if (request.TargetSession.Session.Equals(header.SourceSession.Session))
            {
                // Restoring a session into itself would make every old handle in the checkpoint valid again (P-049).
                return RestorePlanResult.Refused(
                    RestoreRefusal.ReservationRejected,
                    DiagnosticCode.IdempotencyConflict,
                    "the restore target names the captured session " + header.SourceSession.Session.ToString()
                    + "; recovery always creates a new WorldId so old callbacks and handles never become valid"
                    + " (P-004, P-049).");
            }

            if (request.RequireCatalogMatch && !header.CatalogFingerprint.Equals(request.CatalogFingerprint))
            {
                return RestorePlanResult.Refused(
                    RestoreRefusal.CatalogMismatch,
                    DiagnosticCode.UnsupportedVersion,
                    "the checkpoint records catalog fingerprint " + header.CatalogFingerprint.ToHex()
                    + " and this build carries " + request.CatalogFingerprint.ToHex()
                    + "; state from another content revision is not rewritten into this one (P-028, 06 s7).");
            }

            // Every record category is decoded exactly once; a decode failure here is a corruption the container
            // could not see, because framing validates a record's shape but not its value.
            if (!document.TryReadRecords(CheckpointRecordKind.Scope, out IReadOnlyList<ScopeRecordValue> scopes,
                    out DiagnosticCode code, out string detail)
                || !document.TryReadRecords(CheckpointRecordKind.Install, out IReadOnlyList<InstallRecordValue> installs,
                    out code, out detail)
                || !document.TryReadRecords(CheckpointRecordKind.Selection, out IReadOnlyList<SelectionRecordValue> selections,
                    out code, out detail)
                || !document.TryReadRecords(CheckpointRecordKind.Target, out IReadOnlyList<TargetRecordValue> targets,
                    out code, out detail)
                || !document.TryReadRecords(CheckpointRecordKind.Slot, out IReadOnlyList<SlotRecordValue> slots,
                    out code, out detail)
                || !document.TryReadRecords(CheckpointRecordKind.Grant, out IReadOnlyList<GrantRecordValue> grants,
                    out code, out detail)
                || !document.TryReadRecords(CheckpointRecordKind.Clock, out IReadOnlyList<ClockRecordValue> clocks,
                    out code, out detail)
                || !document.TryReadRecords(CheckpointRecordKind.Command, out IReadOnlyList<CommandRecordValue> commands,
                    out code, out detail)
                || !document.TryReadRecords(CheckpointRecordKind.Message, out IReadOnlyList<MessageRecordValue> messages,
                    out code, out detail)
                || !document.TryReadRecords(CheckpointRecordKind.Rng, out IReadOnlyList<RngRecordValue> rngStreams,
                    out code, out detail)
                || !document.TryReadRecords(CheckpointRecordKind.Cursor, out IReadOnlyList<CursorRecordValue> cursors,
                    out code, out detail)
                || !document.TryReadRecords(CheckpointRecordKind.Outbox, out IReadOnlyList<OutboxRecordValue> outbox,
                    out code, out detail))
            {
                return RestorePlanResult.Refused(RestoreRefusal.CorruptReference, code, detail);
            }

            // Every captured schema version must have exactly one path to this build's version, and every target's
            // recipe schema too: a persistent reference carries a required schema version, so a moved schema is
            // migrated rather than rebuilt at the wrong revision (P-007, P-054). Planning is done once, before any
            // identity is trusted, so a refused restore reports the migration that failed rather than a symptom.
            var migrations = new List<MigrationPlan>();
            if (request.AllocatedSchemas != null)
            {
                var captured = new List<SchemaRef>(1 + clocks.Count + targets.Count);
                captured.Add(CheckpointFormat.DocumentSchema);
                for (int i = 0; i < clocks.Count; i++)
                {
                    if (!clocks[i].IsDeclaration && !clocks[i].PayloadSchema.Id.Value.IsDefault)
                    {
                        captured.Add(clocks[i].PayloadSchema);
                    }
                }

                for (int i = 0; i < targets.Count; i++)
                {
                    captured.Add(targets[i].Recipe.Schema);
                }

                if (!request.Migrations.TryPlanAll(
                        captured,
                        request.AllocatedSchemas,
                        out IReadOnlyList<MigrationPlan> planned,
                        out MigrationPlan? refused,
                        out string migrationDetail))
                {
                    return RestorePlanResult.Refused(
                        RestoreRefusal.MigrationRejected,
                        refused == null ? DiagnosticCode.MigrationRequired : refused.Code,
                        migrationDetail);
                }

                for (int i = 0; i < planned.Count; i++)
                {
                    migrations.Add(planned[i]);
                }
            }
            else if (!request.Migrations.IsWellFormed)
            {
                // A caller that supplies no allocated schema set still cannot restore through a malformed graph, and
                // a document from another schema version is refused rather than silently written as this one (P-054).
                return RestorePlanResult.Refused(
                    RestoreRefusal.MigrationRejected,
                    DiagnosticCode.OwnershipConflict,
                    "the migration registry is not well formed: " + request.Migrations.Rejections[0]);
            }

            if (!ValidateComposition(scopes, targets, out string compositionDetail))
            {
                return RestorePlanResult.Refused(
                    RestoreRefusal.InvalidComposition,
                    DiagnosticCode.MissingDependency,
                    compositionDetail);
            }

            var identities = CheckpointIdentityTable.Build(
                request.TargetSession,
                scopes,
                installs,
                targets,
                slots,
                clocks,
                messages,
                rngStreams,
                default(OperationId));

            if (!identities.ValidateReferences(
                    scopes,
                    installs,
                    targets,
                    slots,
                    selections,
                    grants,
                    clocks,
                    commands,
                    messages))
            {
                return RestorePlanResult.Corrupt(
                    RestoreRefusal.CorruptReference,
                    DiagnosticCode.MissingDependency,
                    "the checkpoint declares " + identities.Diagnostics.Count.ToString(CultureInfo.InvariantCulture)
                    + " unresolved required reference(s); the first is: "
                    + (identities.Diagnostics.Count == 0 ? "unknown" : identities.Diagnostics[0].Summary),
                    identities.Diagnostics);
            }

            if (!ValidateState(slots, out string stateDetail))
            {
                return RestorePlanResult.Refused(
                    RestoreRefusal.MissingRequiredState,
                    DiagnosticCode.MigrationRequired,
                    stateDetail);
            }

            if (!ValidateOutbox(outbox, out string outboxDetail))
            {
                return RestorePlanResult.Refused(
                    RestoreRefusal.InvalidOutbox,
                    DiagnosticCode.OwnershipConflict,
                    outboxDetail);
            }

            var plan = new RestorePlan(
                request.TargetSession,
                header,
                identities,
                scopes,
                installs,
                selections,
                targets,
                slots,
                grants,
                clocks,
                commands,
                messages,
                rngStreams,
                rngStreams,
                outbox,
                cursors,
                migrations,
                document.DocumentHash);

            return RestorePlanResult.Success(plan);
        }

        /// <summary>
        /// Validates the scope graph: exactly one root, every other scope's parent declared, and no cycle. A tree
        /// that fails this cannot be rebuilt as one rooted acyclic tree per world (P-010).
        /// </summary>
        private static bool ValidateComposition(
            IReadOnlyList<ScopeRecordValue> scopes,
            IReadOnlyList<TargetRecordValue> targets,
            out string detail)
        {
            detail = string.Empty;

            if (scopes.Count == 0)
            {
                detail = "the checkpoint declares no scope; a world requires exactly one rooted scope tree (P-010).";
                return false;
            }

            int roots = 0;
            var byId = new Dictionary<Id128, ScopeRecordValue>(scopes.Count);
            for (int i = 0; i < scopes.Count; i++)
            {
                ScopeRecordValue scope = scopes[i];
                if (byId.ContainsKey(scope.Scope.Value))
                {
                    detail = "scope " + scope.Scope.ToString()
                        + " is declared more than once (P-004).";
                    return false;
                }

                byId.Add(scope.Scope.Value, scope);
                if (scope.IsRoot)
                {
                    roots++;
                }
            }

            if (roots != 1)
            {
                detail = "the checkpoint declares " + roots.ToString(CultureInfo.InvariantCulture)
                    + " root scopes; a world has exactly one rooted tree (P-010).";
                return false;
            }

            // Walk every scope to the root; a missing parent or a cycle is a composition this build cannot rebuild.
            for (int i = 0; i < scopes.Count; i++)
            {
                ScopeRecordValue scope = scopes[i];
                var seen = new HashSet<Id128>();
                Id128 cursor = scope.Scope.Value;
                int depth = 0;
                while (!cursor.IsDefault)
                {
                    if (!seen.Add(cursor))
                    {
                        detail = "scope " + scope.Scope.ToString()
                            + " participates in a parent cycle; scopes form one rooted acyclic tree (P-010).";
                        return false;
                    }

                    if (depth++ > scopes.Count)
                    {
                        detail = "scope " + scope.Scope.ToString()
                            + " has a parent chain longer than the document's scope count (P-010).";
                        return false;
                    }

                    if (!byId.TryGetValue(cursor, out ScopeRecordValue current))
                    {
                        detail = "scope " + cursor.ToString()
                            + " is named as a parent but the checkpoint does not declare it (P-010).";
                        return false;
                    }

                    cursor = current.Parent.Value;
                }
            }

            // The root scope must own at least the targets nothing else claims; a target outside the tree would be
            // restored into no scope at all (P-010).
            for (int i = 0; i < targets.Count; i++)
            {
                if (!byId.ContainsKey(targets[i].Scope.Value))
                {
                    detail = "target " + targets[i].Target.ToString() + " names owner scope "
                        + targets[i].Scope.ToString() + " which the checkpoint does not declare (P-010).";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Validates owner state: one version per (target, owner, slot) and no two writers for one key. A duplicate
        /// key would restore two answers for one slot, which is a corrupt reference rather than a precedence question
        /// (P-032, P-034).
        /// </summary>
        private static bool ValidateState(IReadOnlyList<SlotRecordValue> slots, out string detail)
        {
            detail = string.Empty;

            var seen = new Dictionary<StateSlotKey, SlotRecordValue>();
            for (int i = 0; i < slots.Count; i++)
            {
                SlotRecordValue slot = slots[i];
                if (seen.ContainsKey(slot.Key))
                {
                    detail = "slot " + slot.Key.ToString()
                        + " appears more than once; one state slot has one authoritative value (P-032).";
                    return false;
                }

                seen.Add(slot.Key, slot);
            }

            return true;
        }

        /// <summary>
        /// Validates the outbox section before a restore reinstates it (GC-021). Three rules, each of which the
        /// protocol states somewhere else and each of which makes a restored outbox unambiguous:
        ///
        /// 1. Every row names a non-default obligation, destination and external idempotency key (P-004: an all-zero
        ///    identity is not a catalog or delivery identity).
        /// 2. One row kind appears at most once per obligation identity, and at most one cursor row exists per
        ///    destination (P-008: no two records claim one answer).
        /// 3. A cursor's recorded retained-terminal count equals the terminal rows this document carries for that
        ///    destination, so a restore cannot believe it retains a terminal record the document dropped (P-045's
        ///    retention bound would otherwise silently become a silent delete).
        ///
        /// The record version is checked too: a row written by a revision this build does not implement is refused
        /// rather than decoded as if it were current (P-054, P-055).
        /// </summary>
        private static bool ValidateOutbox(IReadOnlyList<OutboxRecordValue> outbox, out string detail)
        {
            detail = string.Empty;

            if (outbox.Count == 0)
            {
                return true;
            }

            var obligations = new HashSet<Id128>();
            var terminals = new HashSet<Id128>();
            var cursors = new HashSet<Id128>();
            var terminalCounts = new Dictionary<Id128, int>();

            for (int i = 0; i < outbox.Count; i++)
            {
                OutboxRecordValue row = outbox[i];
                if (row.RecordVersion != OutboxRecordValue.CurrentRecordVersion)
                {
                    detail = "an outbox row is written in record version "
                        + row.RecordVersion.ToString(CultureInfo.InvariantCulture) + " and this build implements "
                        + OutboxRecordValue.CurrentRecordVersion.ToString(CultureInfo.InvariantCulture)
                        + "; a row this build cannot decode is refused (P-054).";
                    return false;
                }

                if (!IsDeclaredOutboxRow(row.Row))
                {
                    detail = "an outbox row declares row kind "
                        + row.RowKind.ToString(CultureInfo.InvariantCulture)
                        + ", which names no outbox fact this build implements (P-054).";
                    return false;
                }

                if (row.Row == OutboxRowKind.Cursor)
                {
                    if (row.DestinationId.IsDefault)
                    {
                        detail = "outbox row " + i.ToString(CultureInfo.InvariantCulture)
                            + " is a delivery cursor with a default destination identity; an all-zero identity is "
                            + "never an identity (P-004).";
                        return false;
                    }

                    if (!cursors.Add(row.DestinationId))
                    {
                        detail = "destination " + row.DestinationId.ToString()
                            + " carries two delivery cursors; one destination has one cursor (P-008).";
                        return false;
                    }
                }
                else if (row.OutboxId.IsDefault || row.DestinationId.IsDefault || row.IdempotencyKey.IsDefault)
                {
                    detail = "outbox row " + i.ToString(CultureInfo.InvariantCulture)
                        + " has a default obligation, destination or external idempotency key; an all-zero identity "
                        + "is never an identity (P-004).";
                    return false;
                }
                else if (row.Row == OutboxRowKind.Obligation)
                {
                    if (!obligations.Add(row.OutboxId))
                    {
                        detail = "obligation " + row.OutboxId.ToString()
                            + " appears twice in the outbox section; one obligation has one record (P-008).";
                        return false;
                    }
                }
                else
                {
                    if (!terminals.Add(row.OutboxId))
                    {
                        detail = "obligation " + row.OutboxId.ToString()
                            + " carries two terminal records; a delivery has one outcome (P-008).";
                        return false;
                    }

                    terminalCounts.TryGetValue(row.DestinationId, out int seen);
                    terminalCounts[row.DestinationId] = seen + 1;
                }
            }

            for (int i = 0; i < outbox.Count; i++)
            {
                OutboxRecordValue row = outbox[i];
                if (row.Row != OutboxRowKind.Cursor)
                {
                    continue;
                }

                terminalCounts.TryGetValue(row.DestinationId, out int actual);
                if (row.RetainedTerminalCount != (uint)actual)
                {
                    detail = "the delivery cursor for destination " + row.DestinationId.ToString() + " records "
                        + row.RetainedTerminalCount.ToString(CultureInfo.InvariantCulture)
                        + " retained terminal record(s) but the document carries "
                        + actual.ToString(CultureInfo.InvariantCulture)
                        + "; a cursor that disagrees with the records it summarises is refused rather than restored "
                        + "(P-045, P-053).";
                    return false;
                }
            }

            // A terminal record without an obligation is a record of work this document never claimed; it is refused
            // because a restore cannot tell which world's obligation it closes (P-053).
            foreach (Id128 terminal in terminals)
            {
                if (!obligations.Contains(terminal))
                {
                    detail = "terminal record " + terminal.ToString()
                        + " closes an obligation the checkpoint does not carry; a terminal record never outlives its "
                        + "obligation inside one document (P-053).";
                    return false;
                }
            }

            return true;
        }

        private static bool IsDeclaredOutboxRow(OutboxRowKind row) =>
            row == OutboxRowKind.Obligation || row == OutboxRowKind.Terminal || row == OutboxRowKind.Cursor;
    }
}
