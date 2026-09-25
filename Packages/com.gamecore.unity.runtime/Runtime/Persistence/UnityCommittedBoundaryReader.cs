// GameCore.Unity.Runtime - the committed-boundary reader over real ECS storage (GC-018).
//
// Normative sources: 00 P-053 (a checkpoint is taken at a committed boundary and contains stable scope/install/target
// identities, descriptors, explicit imports/overrides/exclusions, mode, active and dormant authoritative state,
// clocks, bounded pending next-step messages and cursors) and 04 s5 (the stable-ID index stores only
// `TargetId`-to-`Entity` lookup information; raw `Entity.Index`/`Entity.Version` are world/session-local handles and
// are never a domain identity).
//
// This is the Unity half of the seam in `GameCore.Execution.Persistence.CommittedBoundary`. It reads one world at an
// end-of-step or idle boundary and copies everything into engine-free records, so the capture itself never touches
// ECS and never runs while a step is in flight. Two properties are load-bearing:
//
//   * **It refuses outside a boundary.** `IsAtCommittedBoundary` is false while a pump is executing, while an apply
//     is in progress, or once the world is faulted/stopping; the capture then refuses before reading anything
//     (P-030, P-053).
//   * **It copies, never aliases.** Every list it builds is a new managed list of immutable record values. No
//     `Entity`, `DynamicBuffer`, `NativeArray` or `SystemHandle` escapes this file, and no `Entity.Index`/
//     `Entity.Version` is written into a record (P-005).
//
// The observation lease (GC-016) is the sibling mechanism for *retained* images: when that task's bounded lease
// interface is present, this reader is the place that would hold one for the duration of a read. The frozen shape
// here does not depend on it, so both tasks integrate without either owning the other's storage.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Execution.Persistence;
using GameCore.Execution.Time;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;
using Unity.Entities;

namespace GameCore.Unity.Runtime.Persistence
{
    /// <summary>
    /// Reads one owned world's committed boundary. A caller must publish a <see cref="CaptureContext"/> for a world
    /// before it can be captured: the context is the explicit statement of which scope tree, which target recipes,
    /// which declared buffers and which clocks the capture should read, because none of those is discoverable from
    /// ECS storage alone (P-004, P-015, P-043).
    /// </summary>
    public sealed class UnityCommittedBoundaryReader : ICommittedBoundaryReader
    {
        private readonly UnityWorldHost world;
        private readonly CaptureContext context;

        public UnityCommittedBoundaryReader(UnityWorldHost world, CaptureContext context)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.context = context ?? throw new ArgumentNullException(nameof(context));

            if (!world.World.Session.Equals(context.World.Session))
            {
                throw new ArgumentException(
                    "The capture context belongs to another world incarnation than the host (P-004).",
                    nameof(context));
            }
        }

        public WorldId World => world.World;

        /// <summary>
        /// True only at an end-of-step or idle boundary of a runnable world. A pump in progress, an apply in
        /// progress, a faulted world and a stopping/disposed world all refuse, so a capture never reads a moving
        /// world (P-030, P-031, P-053).
        /// </summary>
        public bool IsAtCommittedBoundary
        {
            get
            {
                if (world.Lifecycle != WorldLifecycleState.Running && world.Lifecycle != WorldLifecycleState.Paused)
                {
                    return false;
                }

                if (world.IsPumping)
                {
                    return false;
                }

                if (world.Driver.IsFaulted)
                {
                    return false;
                }

                // The published assembly must agree with the world's own epoch mirror: a disagreement means a
                // publication is mid-flight, which is not a boundary (P-006, P-030).
                if (context.Publisher != null)
                {
                    return AssemblyPublisher.MatchesPublishedAssembly(
                        context.LaneRevision,
                        context.LaneEpoch,
                        context.Publisher.PublishedRevision,
                        world.CurrentEpoch);
                }

                return true;
            }
        }

        public bool TryRead(
            WorldId requested,
            out CommittedBoundarySnapshot? snapshot,
            out BoundaryRefusal refusal,
            out DiagnosticCode code,
            out string detail)
        {
            snapshot = null;
            code = DiagnosticCode.None;
            detail = string.Empty;

            if (!requested.Session.Equals(World.Session))
            {
                refusal = BoundaryRefusal.ForeignWorld;
                code = DiagnosticCode.StaleHandle;
                detail = "the boundary reader owns world " + World.Session.ToString()
                    + " and the request names " + requested.Session.ToString() + " (P-004).";
                return false;
            }

            if (!IsAtCommittedBoundary)
            {
                refusal = BoundaryRefusal.NotAtBoundary;
                code = world.Driver.IsFaulted ? DiagnosticCode.ApplyFault : DiagnosticCode.TooLate;
                detail = "world " + world.DiagnosticName + " is not at a committed boundary (lifecycle "
                    + world.Lifecycle.ToString() + ", pumping " + world.IsPumping.ToString()
                    + ", faulted " + world.Driver.IsFaulted.ToString()
                    + "); a checkpoint is taken at an end-of-step or idle boundary (P-053).";
                return false;
            }

            try
            {
                snapshot = Read();
                refusal = BoundaryRefusal.None;
                return true;
            }
            catch (Exception exception)
            {
                refusal = BoundaryRefusal.IncompleteBoundary;
                code = DiagnosticCode.ResourceUnavailable;
                detail = "reading the committed boundary of world " + world.DiagnosticName + " failed: "
                    + exception.GetType().FullName + ": " + exception.Message;
                snapshot = null;
                return false;
            }
        }

        private CommittedBoundarySnapshot Read()
        {
            EntityManager entityManager = world.EntityWorld.EntityManager;

            IReadOnlyList<LiveTarget> liveTargets = context.Targets.Targets;
            var targets = new List<TargetRecordValue>(liveTargets.Count);
            var slots = new List<SlotRecordValue>();

            for (int i = 0; i < liveTargets.Count; i++)
            {
                LiveTarget live = liveTargets[i];
                TargetId target = live.Target;
                if (!context.Registry.TryGetHandle(target, out TargetHandle handle))
                {
                    throw new InvalidOperationException(
                        "live target " + target.ToString() + " has no handle in this world's target registry (P-005).");
                }

                targets.Add(new TargetRecordValue(
                    target.Value.High,
                    target.Value.Low,
                    live.Scope.Value.High,
                    live.Scope.Value.Low,
                    live.Recipe.Id.Value.High,
                    live.Recipe.Id.Value.Low,
                    live.Recipe.Schema.Id.Value.High,
                    live.Recipe.Schema.Id.Value.Low,
                    live.Recipe.Schema.Version,
                    live.Recipe.Revision.Value,
                    handle.Slot,
                    handle.Generation));

                // Active and dormant rows both live in the same buffer; the `Active` byte distinguishes them, and a
                // checkpoint that skipped the dormant rows would silently delete durable progress (P-032). The slot
                // row is copied as a stable (TargetId, OwnerId, SlotId) key, never as an entity.
                if (!context.Registry.TryResolveTarget(target, out _, out Entity entity))
                {
                    continue;
                }

                if (!entityManager.HasBuffer<TargetSlotState>(entity))
                {
                    continue;
                }

                DynamicBuffer<TargetSlotState> rows = entityManager.GetBuffer<TargetSlotState>(entity);
                for (int s = 0; s < rows.Length; s++)
                {
                    TargetSlotState row = rows[s];
                    slots.Add(new SlotRecordValue(
                        target.Value.High,
                        target.Value.Low,
                        row.Owner.Value.High,
                        row.Owner.Value.Low,
                        row.Slot.Value.High,
                        row.Slot.Value.Low,
                        row.SchemaVersion,
                        row.Value,
                        row.Active != 0));
                }
            }

            ReadComposition(
                out IReadOnlyList<ScopeRecordValue> scopes,
                out IReadOnlyList<InstallRecordValue> installs,
                out IReadOnlyList<SelectionRecordValue> selections,
                out IReadOnlyList<GrantRecordValue> grants);
            ReadClocks(out IReadOnlyList<ClockRecordValue> clocks);
            ReadPlane(
                out IReadOnlyList<CommandRecordValue> commands,
                out IReadOnlyList<MessageRecordValue> messages,
                out IReadOnlyList<CursorRecordValue> cursors);

            return new CommittedBoundarySnapshot(
                world.World,
                context.Definition,
                world.TemporalModel,
                context.StepDurationTicks,
                context.TicksPerSecond,
                context.MaxStepsPerPump,
                context.UsesUnscaledHostClock,
                world.HostTicksPerSecond,
                world.CurrentStep,
                world.RetainedDebt,
                world.DomainSeconds,
                world.PendingDemand,
                context.Mode,
                world.PublishedCompositionRevision,
                world.CurrentEpoch,
                context.CatalogFingerprint,
                world.Messages == null ? EventSequence.Zero : world.Messages.LastEventSequence,
                world.Messages == null ? AdmissionSequence.Zero : world.Messages.Requests.LastAdmissionSequence,
                scopes,
                installs,
                selections,
                targets,
                slots,
                grants,
                clocks,
                commands,
                messages,
                context.Rng.ToRecords(),
                cursors);
        }

        /// <summary>
        /// Reads the committed composition: the scope tree with its parent edges, boundaries, grants and per-scope
        /// membership, the installations with their configuration document, and the instance-level selections. The
        /// committed state is the authority, so a staged edit is never captured (00 s9).
        /// </summary>
        private void ReadComposition(
            out IReadOnlyList<ScopeRecordValue> scopes,
            out IReadOnlyList<InstallRecordValue> installs,
            out IReadOnlyList<SelectionRecordValue> selections,
            out IReadOnlyList<GrantRecordValue> grants)
        {
            var scopeRows = new List<ScopeRecordValue>();
            var installRows = new List<InstallRecordValue>();
            var selectionRows = new List<SelectionRecordValue>();
            var grantRows = new List<GrantRecordValue>();

            if (context.Lane != null)
            {
                CompositionState committed = context.Lane.Committed;
                IReadOnlyList<ScopeRecord> scopeRecords = committed.Scopes.Scopes;
                for (int i = 0; i < scopeRecords.Count; i++)
                {
                    ScopeRecord scope = scopeRecords[i];
                    scopeRows.Add(new ScopeRecordValue(
                        scope.Scope.Value.High,
                        scope.Scope.Value.Low,
                        scope.Parent.Value.High,
                        scope.Parent.Value.Low,
                        (uint)scope.Depth,
                        (uint)committed.Mode,
                        (uint)committed.InstallsAt(scope.Scope).Count,
                        0U,
                        scope.ServiceIsolation.AllContracts,
                        scope.CapabilityIsolation.AllContracts,
                        (uint)scope.ServiceIsolation.Contracts.Count,
                        (uint)scope.CapabilityIsolation.Contracts.Count));

                    AppendBoundaryGrants(grantRows, scope);
                }

                for (int i = 0; i < committed.Installs.Count; i++)
                {
                    InstallEntry entry = committed.Installs[i];
                    byte[]? config = null;
                    uint fieldCount = 0U;
                    bool hasConfig = !entry.Config.IsEmpty;
                    if (hasConfig)
                    {
                        FrozenPayload encoded = ConfigDocumentCodec.Encode(entry.Config);
                        config = new byte[encoded.Length];
                        for (int b = 0; b < encoded.Length; b++)
                        {
                            config[b] = encoded.Bytes[b];
                        }

                        fieldCount = (uint)entry.Config.Count;
                    }

                    CanonicalId32.Split(entry.Record.ConfigHash, out ulong h0, out ulong h1, out ulong h2, out ulong h3);
                    installRows.Add(new InstallRecordValue(
                        entry.Instance.Value.High,
                        entry.Instance.Value.Low,
                        entry.Record.PluginType.Value.High,
                        entry.Record.PluginType.Value.Low,
                        entry.Scope.Value.High,
                        entry.Scope.Value.Low,
                        entry.Record.ConfigRevision.Value,
                        h0,
                        h1,
                        h2,
                        h3,
                        entry.Record.Priority,
                        entry.Record.Generation.Value,
                        entry.Record.ActivationEpoch.Value,
                        (uint)entry.State,
                        fieldCount,
                        config,
                        (uint)entry.Selections.Count,
                        hasConfig));

                    for (int s = 0; s < entry.Selections.Count; s++)
                    {
                        ServiceSelection selection = entry.Selections[s];
                        selectionRows.Add(new SelectionRecordValue(
                            entry.Instance.Value.High,
                            entry.Instance.Value.Low,
                            selection.Contract.ContractId.High,
                            selection.Contract.ContractId.Low,
                            selection.Contract.Version,
                            selection.Provider.Value.High,
                            selection.Provider.Value.Low,
                            (uint)s));
                    }
                }
            }

            scopes = scopeRows;
            installs = installRows;
            selections = selectionRows;
            grants = grantRows;
        }

        /// <summary>
        /// Records one scope's explicit grants: its Conservative-mode imports, its exclusions and its named
        /// isolation members. These are composition inputs rather than derived data, so a restored world gets the
        /// same boundaries it was captured with instead of reopening one (P-013, P-016).
        /// </summary>
        private static void AppendBoundaryGrants(List<GrantRecordValue> grants, ScopeRecord scope)
        {
            IReadOnlyList<CapabilityImport> imports = scope.Grants.Imports;
            for (int i = 0; i < imports.Count; i++)
            {
                CapabilityImport import = imports[i];
                grants.Add(new GrantRecordValue(
                    (uint)GrantKind.ScopeImport,
                    scope.Scope.Value.High,
                    scope.Scope.Value.Low,
                    0UL,
                    0UL,
                    import.CapabilityId.Value.High,
                    import.CapabilityId.Value.Low,
                    0U,
                    import.ProviderInstallationId.Value.High,
                    import.ProviderInstallationId.Value.Low,
                    0UL,
                    0UL,
                    0UL,
                    0UL,
                    0U,
                    import.CapabilityId.Value.High,
                    import.CapabilityId.Value.Low,
                    false,
                    false,
                    0U,
                    (uint)i));
            }

            IReadOnlyList<ExclusionRule> exclusions = scope.Exclusions;
            for (int i = 0; i < exclusions.Count; i++)
            {
                ExclusionRule rule = exclusions[i];
                grants.Add(new GrantRecordValue(
                    (uint)GrantKind.Exclusion,
                    scope.Scope.Value.High,
                    scope.Scope.Value.Low,
                    rule.AtTarget.Value.High,
                    rule.AtTarget.Value.Low,
                    rule.Kind == ExclusionTargetKind.Capability ? rule.TargetId.High : 0UL,
                    rule.Kind == ExclusionTargetKind.Capability ? rule.TargetId.Low : 0UL,
                    0U,
                    rule.Kind == ExclusionTargetKind.Provider ? rule.TargetId.High : 0UL,
                    rule.Kind == ExclusionTargetKind.Provider ? rule.TargetId.Low : 0UL,
                    rule.Kind == ExclusionTargetKind.Rule ? rule.TargetId.High : 0UL,
                    rule.Kind == ExclusionTargetKind.Rule ? rule.TargetId.Low : 0UL,
                    0UL,
                    0UL,
                    0U,
                    rule.TargetId.High,
                    rule.TargetId.Low,
                    rule.AppliesToSubtree,
                    false,
                    (uint)rule.Kind,
                    (uint)i));
            }

            IReadOnlyList<Id128> serviceMembers = scope.ServiceIsolation.Contracts;
            for (int i = 0; i < serviceMembers.Count; i++)
            {
                grants.Add(IsolationMember(scope, serviceMembers[i], GrantKind.ServiceIsolationMember, scope.ServiceIsolation.AllContracts, i));
            }

            IReadOnlyList<Id128> capabilityMembers = scope.CapabilityIsolation.Contracts;
            for (int i = 0; i < capabilityMembers.Count; i++)
            {
                grants.Add(IsolationMember(scope, capabilityMembers[i], GrantKind.CapabilityIsolationMember, scope.CapabilityIsolation.AllContracts, i));
            }
        }

        private static GrantRecordValue IsolationMember(
            ScopeRecord scope,
            Id128 contract,
            GrantKind kind,
            bool allContracts,
            int order) =>
            new GrantRecordValue(
                (uint)kind,
                scope.Scope.Value.High,
                scope.Scope.Value.Low,
                0UL,
                0UL,
                0UL,
                0UL,
                0U,
                0UL,
                0UL,
                0UL,
                0UL,
                contract.High,
                contract.Low,
                0U,
                contract.High,
                contract.Low,
                false,
                allContracts,
                0U,
                (uint)order);

        /// <summary>Reads the plugin clocks: every declaration, and every pending wake with its remaining delay.</summary>
        private void ReadClocks(out IReadOnlyList<ClockRecordValue> clocks)
        {
            var rows = new List<ClockRecordValue>();

            for (int i = 0; i < context.ClockSpecs.Count; i++)
            {
                PluginClockSpec spec = context.ClockSpecs[i];
                rows.Add(new ClockRecordValue(
                    (uint)ClockRowKind.Declaration,
                    spec.ClockId.High,
                    spec.ClockId.Low,
                    (uint)spec.Kind,
                    (uint)spec.OnPause,
                    spec.Persists,
                    0UL,
                    0UL,
                    0UL,
                    0UL,
                    0U,
                    0UL,
                    0UL,
                    0UL,
                    (uint)ClockWakeState.Pending,
                    (uint)i));

                // A transient clock is declared but its value is not saved, which is the spec's own statement (P-038).
                if (!spec.Persists)
                {
                    continue;
                }

                IReadOnlyList<WakeRecord> wakes = context.Clocks == null
                    ? Array.Empty<WakeRecord>()
                    : context.Clocks.WakesOf(spec.ClockId);
                for (int w = 0; w < wakes.Count; w++)
                {
                    WakeRecord wake = wakes[w];
                    if (wake.IsCancelled)
                    {
                        // A cancelled wake is not pending work; recording it would resurrect a deferred timer (P-038).
                        continue;
                    }

                    rows.Add(new ClockRecordValue(
                        (uint)ClockRowKind.Wake,
                        spec.ClockId.High,
                        spec.ClockId.Low,
                        (uint)spec.Kind,
                        (uint)spec.OnPause,
                        spec.Persists,
                        wake.WakeId.High,
                        wake.WakeId.Low,
                        wake.PayloadSchema.Id.Value.High,
                        wake.PayloadSchema.Id.Value.Low,
                        wake.PayloadSchema.Version,
                        wake.ScheduledAtSequence,
                        wake.RemainingSteps,
                        wake.RemainingTicks,
                        (uint)(wake.IsConsumed
                            ? ClockWakeState.Consumed
                            : (wake.IsDue ? ClockWakeState.Due : ClockWakeState.Pending)),
                        (uint)w));
                }
            }

            clocks = rows;
        }

        /// <summary>
        /// Reads the bounded message plane: queued external commands, retained next-step messages with their payload
        /// bytes, the committed-event cursor and the per-issuer deduplication high-water marks.
        /// </summary>
        private void ReadPlane(
            out IReadOnlyList<CommandRecordValue> commands,
            out IReadOnlyList<MessageRecordValue> messages,
            out IReadOnlyList<CursorRecordValue> cursors)
        {
            var commandRows = new List<CommandRecordValue>();
            var messageRows = new List<MessageRecordValue>();
            var cursorRows = new List<CursorRecordValue>();

            WorldMessagePlane? plane = world.Messages;
            if (plane == null)
            {
                commands = commandRows;
                messages = messageRows;
                cursors = cursorRows;
                return;
            }

            IReadOnlyList<RequestRow> pending = plane.Requests.PendingRows();
            for (int i = 0; i < pending.Count; i++)
            {
                RequestRow row = pending[i];
                if (row.Origin != RequestOrigin.External)
                {
                    // Only an externally admitted command is a re-admittable input; an internal request is
                    // step-local and does not survive the boundary (P-042).
                    continue;
                }

                MessageOrderKey order = row.Order;
                CanonicalId32.Split(row.InputHash, out ulong h0, out ulong h1, out ulong h2, out ulong h3);
                byte[]? payload = null;
                if (context.CommandPayloads.TryGetValue(row.Request, out FrozenPayload? frozen) && frozen != null)
                {
                    payload = new byte[frozen.Length];
                    for (int b = 0; b < frozen.Length; b++)
                    {
                        payload[b] = frozen.Bytes[b];
                    }
                }

                commandRows.Add(new CommandRecordValue(
                    row.Request.IssuerId.High,
                    row.Request.IssuerId.Low,
                    row.Request.IssuerSequence,
                    row.Route.Value.High,
                    row.Route.Value.Low,
                    row.Target.Value.High,
                    row.Target.Value.Low,
                    row.Schema.Id.Value.High,
                    row.Schema.Id.Value.Low,
                    row.Schema.Version,
                    row.AdmittedStep.Value,
                    row.AdmittedEpoch.Value,
                    order.Admitted.Value,
                    order.Ordinal,
                    (uint)RequestOrigin.External,
                    h0,
                    h1,
                    h2,
                    h3,
                    payload));
            }

            for (int i = 0; i < context.NextStepBuffers.Count; i++)
            {
                BufferId buffer = context.NextStepBuffers[i];
                if (!plane.Schedule.TryGetBuffer(buffer, out BoundedMessageBuffer? bounded) || bounded == null)
                {
                    continue;
                }

                IReadOnlyList<StepMessage> rows = bounded.Ordered();
                for (int r = 0; r < rows.Count; r++)
                {
                    StepMessage row = rows[r];
                    byte[]? payload = null;
                    if (row.HasPayload)
                    {
                        byte[] raw = bounded.PayloadOf(row);
                        payload = raw.Length == 0 ? null : raw;
                    }

                    messageRows.Add(new MessageRecordValue(
                        row.Step.Value,
                        row.Epoch.Value,
                        row.Request.IssuerId.High,
                        row.Request.IssuerId.Low,
                        row.Request.IssuerSequence,
                        row.Route.Value.High,
                        row.Route.Value.Low,
                        row.Owner.Value.High,
                        row.Owner.Value.Low,
                        row.Target.Value.High,
                        row.Target.Value.Low,
                        row.PayloadSchema.Id.Value.High,
                        row.PayloadSchema.Id.Value.Low,
                        row.PayloadSchema.Version,
                        (uint)row.Kind,
                        row.Order.Admitted.Value,
                        row.Order.Ordinal,
                        row.Order.OriginKey.High,
                        row.Order.OriginKey.Low,
                        row.Producer.RegistrationKey.High,
                        row.Producer.RegistrationKey.Low,
                        row.Producer.KeyVersion,
                        buffer.Value.High,
                        buffer.Value.Low,
                        row.HasPayload,
                        row.HasRequest,
                        row.IsOutcome,
                        payload));
                }
            }

            cursorRows.Add(new CursorRecordValue(
                (uint)CursorRowKind.EventCursor,
                0UL,
                0UL,
                plane.LastEventSequence.Value,
                world.World.Session.High,
                world.World.Session.Low));

            // Per-issuer high-water marks: the value a restored world needs so duplicate suppression still holds for
            // an issuer whose commands were admitted before the capture (P-050, P-053).
            IReadOnlyList<RequestRow> retained = plane.Requests.Rows();
            var watermarks = new Dictionary<Id128, ulong>();
            for (int i = 0; i < retained.Count; i++)
            {
                RequestRow row = retained[i];
                Id128 issuer = row.Request.IssuerId;
                if (!watermarks.TryGetValue(issuer, out ulong high) || row.Request.IssuerSequence > high)
                {
                    watermarks[issuer] = row.Request.IssuerSequence;
                }
            }

            var issuers = new List<Id128>(watermarks.Keys);
            issuers.Sort();
            for (int i = 0; i < issuers.Count; i++)
            {
                cursorRows.Add(new CursorRecordValue(
                    (uint)CursorRowKind.IssuerHighWater,
                    issuers[i].High,
                    issuers[i].Low,
                    watermarks[issuers[i]],
                    world.World.Session.High,
                    world.World.Session.Low));
            }

            commands = commandRows;
            messages = messageRows;
            cursors = cursorRows;
        }

        public override string ToString() =>
            "boundaryReader(" + world.DiagnosticName + ",atBoundary=" + IsAtCommittedBoundary.ToString() + ")";
    }
}
