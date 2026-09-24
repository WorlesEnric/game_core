// GameCore.Composition — the pure edit applier: base state + declared payload → immutable proposal (O-02..O-08).
//
// Every admitted edit is planned here, with no live writes at all: the result is a new `CompositionState` plus
// the composition delta and the resolved service closure of the change (GC-004 DoD: "all admitted edits produce
// immutable proposals; no live ECS writes occur here"). Planning is a pure function of the base state and the
// payload, so the same input always yields the same plan hash (P-027, P-008).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Composition
{
    /// <summary>
    /// One immutable composition proposal. It carries the before/after definitions, the delta, the resolved
    /// service closure, the state dispositions implied by the affected installs' declared slot policies and the
    /// ordered lists of installations to activate and to retire. Nothing in it references live ECS storage.
    /// </summary>
    public sealed class CompositionEditPlan
    {
        public CompositionEditPlan(
            OperationId operation,
            CompositionEditSubject subject,
            ContentHash inputHash,
            CompositionRevision baseRevision,
            AssemblyEpoch baseEpoch,
            CompositionState before,
            CompositionState after,
            CompositionDelta? delta,
            ServiceResolution? services,
            IReadOnlyList<StateDisposition>? stateDispositions,
            IReadOnlyList<PluginInstanceId>? activationOrder,
            IReadOnlyList<PluginInstanceId>? retiredInstances,
            DiagnosticCode code,
            IReadOnlyList<Diagnostic>? diagnostics)
        {
            Operation = operation;
            Subject = subject;
            InputHash = inputHash;
            BaseRevision = baseRevision;
            BaseEpoch = baseEpoch;
            Before = before;
            After = after;
            Delta = delta;
            Services = services;
            StateDispositions = ContractCollections.Freeze(stateDispositions);
            ActivationOrder = ContractCollections.Freeze(activationOrder);
            RetiredInstances = ContractCollections.Freeze(retiredInstances);
            Code = code;
            Diagnostics = ContractCollections.Freeze(diagnostics);
        }

        public OperationId Operation { get; }

        public CompositionEditSubject Subject { get; }

        /// <summary>Canonical hash of the frozen input payload; the ledger's idempotency key (P-050).</summary>
        public ContentHash InputHash { get; }

        public CompositionRevision BaseRevision { get; }

        public AssemblyEpoch BaseEpoch { get; }

        public CompositionState Before { get; }

        public CompositionState After { get; }

        public CompositionDelta? Delta { get; }

        public ServiceResolution? Services { get; }

        /// <summary>Stored/validated state dispositions of affected slots; consumed by later publication phases.</summary>
        public IReadOnlyList<StateDisposition> StateDispositions { get; }

        /// <summary>Providers before consumers (P-012); empty when the plan is rejected.</summary>
        public IReadOnlyList<PluginInstanceId> ActivationOrder { get; }

        /// <summary>Installations this plan removes, already ordered for teardown: consumers before providers (P-012, P-048).</summary>
        public IReadOnlyList<PluginInstanceId> RetiredInstances { get; }

        /// <summary><see cref="DiagnosticCode.None"/> when the plan is valid.</summary>
        public DiagnosticCode Code { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public bool Succeeded => Code == DiagnosticCode.None;

        /// <summary>True when the proposal changes no observable composition fact (P-006: NoChange, no increments).</summary>
        public bool IsNoChange => Succeeded && Before.Fingerprint().Equals(After.Fingerprint());

        /// <summary>
        /// Semantic plan identity: canonical input hash, subject, base revision/epoch and the resulting definition
        /// fingerprint. Lease ids, timestamps and object addresses are excluded (05 s4).
        /// </summary>
        public ContentHash PlanHash()
        {
            byte[] document = DocumentCodec.Write(
                CompositionSchemas.DefinitionFingerprint,
                writer =>
                {
                    writer.WriteId128Field(1, Operation.World.Session);
                    writer.WriteUInt32Field(2, (uint)Subject);
                    writer.WriteBytesField(3, InputHash.ToArray());
                    writer.WriteUInt64Field(4, BaseRevision.Value);
                    writer.WriteUInt64Field(5, BaseEpoch.Value);
                    writer.WriteBytesField(6, After.Fingerprint().ToArray());
                });

            return ContentHash.Compute(document);
        }
    }

    /// <summary>Pure applier: it never publishes, never acquires a resource and never touches live state.</summary>
    public static class CompositionEditApplier
    {
        /// <summary>Canonical input hash of a frozen payload: the ledger's idempotency key (P-050).</summary>
        public static ContentHash InputHashOf(FrozenPayload payload) => ContentHash.Compute(DocumentCodec.ToBytes(payload));

        /// <summary>
        /// Plans one edit. A rejection returns the unchanged base state and a stable diagnostic code; the caller
        /// keeps publishing the old assembly (00 s9: no live writes before publication).
        /// </summary>
        public static CompositionEditPlan Plan(
            CompositionState current,
            CompositionEditPayload payload,
            OperationId operation,
            CompositionRevision expectedRevision,
            ContentHash inputHash,
            IPluginManifestSource manifests)
        {
            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (manifests == null)
            {
                throw new ArgumentNullException(nameof(manifests));
            }

            List<Diagnostic> diagnostics = new List<Diagnostic>();

            if (!operation.World.Equals(current.World))
            {
                diagnostics.Add(Diag(DiagnosticCode.StaleHandle, payload.Subject, operation, "The operation belongs to another world."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.StaleHandle, diagnostics);
            }

            if (expectedRevision.Value != current.Revision.Value)
            {
                // A proposal is checked against the last published revision (00 s9, P-028).
                diagnostics.Add(Diag(DiagnosticCode.StalePlan, payload.Subject, operation, "Expected revision does not match the published revision."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.StalePlan, diagnostics);
            }

            switch (payload.Subject)
            {
                case CompositionEditSubject.ScopeCreate:
                    return PlanScopeCreate(current, payload, operation, inputHash, diagnostics);
                case CompositionEditSubject.ScopeReparent:
                    return PlanScopeReparent(current, payload, operation, inputHash, diagnostics);
                case CompositionEditSubject.ScopeRemove:
                    return PlanScopeRemove(current, payload, operation, inputHash, diagnostics);
                case CompositionEditSubject.ScopeIsolation:
                    return PlanScopeIsolation(current, payload, operation, inputHash, diagnostics);
                case CompositionEditSubject.ScopeGrants:
                    return PlanScopeGrants(current, payload, operation, inputHash, diagnostics);
                case CompositionEditSubject.InstallMount:
                    return PlanMount(current, payload, operation, inputHash, manifests, diagnostics);
                case CompositionEditSubject.InstallUnmount:
                    return PlanUnmount(current, payload, operation, inputHash, diagnostics);
                case CompositionEditSubject.InstallReconfigure:
                    return PlanReconfigure(current, payload, operation, inputHash, manifests, diagnostics);
                case CompositionEditSubject.InstallSuspend:
                    return PlanLifecycle(current, payload, operation, inputHash, InstallationState.Suspended, diagnostics);
                case CompositionEditSubject.InstallResume:
                    return PlanLifecycle(current, payload, operation, inputHash, InstallationState.Preparing, diagnostics);
                case CompositionEditSubject.ModeSet:
                    return PlanModeSet(current, payload, operation, inputHash, diagnostics);
                default:
                    // A subject the payload codec accepted but the applier does not implement cannot be applied.
                    diagnostics.Add(Diag(DiagnosticCode.UnsupportedVersion, payload.Subject, operation, "Unsupported edit subject."));
                    return Rejected(current, payload, operation, inputHash, DiagnosticCode.UnsupportedVersion, diagnostics);
            }
        }

        /// <summary>
        /// O-08: the propagation mode is one world-level setting owned by the world root. Setting the value it
        /// already has is `NoChange` and increments nothing (P-006, P-013).
        /// </summary>
        private static CompositionEditPlan PlanModeSet(
            CompositionState current,
            CompositionEditPayload payload,
            OperationId operation,
            ContentHash inputHash,
            List<Diagnostic> diagnostics)
        {
            if (!payload.Scope.IsDefault && !payload.Scope.Equals(current.Scopes.Root))
            {
                diagnostics.Add(Diag(DiagnosticCode.OwnershipConflict, payload.Subject, operation, "Propagation mode is one world-level setting owned by the world root (P-013)."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.OwnershipConflict, diagnostics);
            }

            CompositionState after = current.With(mode: payload.Mode);
            return Finish(current, after, payload, operation, inputHash, null, null, diagnostics);
        }

        private static CompositionEditPlan PlanScopeCreate(
            CompositionState current,
            CompositionEditPayload payload,
            OperationId operation,
            ContentHash inputHash,
            List<Diagnostic> diagnostics)
        {
            if (payload.Scope.IsDefault)
            {
                diagnostics.Add(Diag(DiagnosticCode.MissingDependency, payload.Subject, operation, "A scope identity must be a real stable id (P-004)."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.MissingDependency, diagnostics);
            }

            if (payload.Scope.Equals(current.Scopes.Root) || payload.Parent.IsDefault)
            {
                diagnostics.Add(Diag(DiagnosticCode.OwnershipConflict, payload.Subject, operation, "The world already has its only root scope (P-010)."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.OwnershipConflict, diagnostics);
            }

            if (!current.Scopes.TryGet(payload.Parent, out ScopeRecord? parent) || parent == null)
            {
                diagnostics.Add(Diag(DiagnosticCode.MissingDependency, payload.Subject, operation, "The declared parent scope does not exist in this world (P-010)."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.MissingDependency, diagnostics);
            }

            DiagnosticCode grantCode = ValidateGrants(current, payload, operation, diagnostics);
            if (grantCode != DiagnosticCode.None)
            {
                return Rejected(current, payload, operation, inputHash, grantCode, diagnostics);
            }

            ScopeRecord record = new ScopeRecord(
                payload.Scope,
                payload.Parent,
                parent.Depth + 1,
                payload.ServiceIsolation,
                payload.CapabilityIsolation,
                payload.Exclusions,
                new ScopeGrants(payload.Imports));

            if (!current.Scopes.TryAdd(record, out ScopeRegistry? next, out DiagnosticCode code) || next == null)
            {
                diagnostics.Add(Diag(code, payload.Subject, operation, "The scope could not be added to the tree."));
                return Rejected(current, payload, operation, inputHash, code, diagnostics);
            }

            CompositionState after = current.With(scopes: next);
            return Finish(current, after, payload, operation, inputHash, null, null, diagnostics);
        }

        private static CompositionEditPlan PlanScopeReparent(
            CompositionState current,
            CompositionEditPayload payload,
            OperationId operation,
            ContentHash inputHash,
            List<Diagnostic> diagnostics)
        {
            if (!current.Scopes.TryReparent(payload.Scope, payload.Parent, out ScopeRegistry? next, out DiagnosticCode code) || next == null)
            {
                diagnostics.Add(Diag(code, payload.Subject, operation, "The scope move was refused."));
                return Rejected(current, payload, operation, inputHash, code, diagnostics);
            }

            CompositionState after = current.With(scopes: next);
            return Finish(current, after, payload, operation, inputHash, null, null, diagnostics);
        }

        private static CompositionEditPlan PlanScopeRemove(
            CompositionState current,
            CompositionEditPayload payload,
            OperationId operation,
            ContentHash inputHash,
            List<Diagnostic> diagnostics)
        {
            if (!current.Scopes.TryGet(payload.Scope, out ScopeRecord? record) || record == null)
            {
                diagnostics.Add(Diag(DiagnosticCode.MissingDependency, payload.Subject, operation, "The scope does not exist in this world."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.MissingDependency, diagnostics);
            }

            if (record.IsRoot)
            {
                diagnostics.Add(Diag(DiagnosticCode.OwnershipConflict, payload.Subject, operation, "The world root scope cannot be removed (P-010)."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.OwnershipConflict, diagnostics);
            }

            List<ScopeId> affectedScopes = new List<ScopeId>();
            affectedScopes.Add(payload.Scope);
            if (payload.DestroySubtree)
            {
                affectedScopes.AddRange(current.Scopes.Descendants(payload.Scope));
            }

            List<PluginInstanceId> affectedInstalls = new List<PluginInstanceId>();
            for (int i = 0; i < affectedScopes.Count; i++)
            {
                affectedInstalls.AddRange(current.InstallsAt(affectedScopes[i]));
            }

            int childScopes = current.Scopes.ChildrenOf(payload.Scope).Count;
            if (!payload.DestroySubtree && (childScopes != 0 || affectedInstalls.Count != 0))
            {
                // P-010: removing a nonempty scope needs an explicit reparent destination or an explicit
                // subtree-destruction disposition that includes every affected installation.
                diagnostics.Add(Diag(DiagnosticCode.OwnershipConflict, payload.Subject, operation, "Removing a nonempty scope requires an explicit DestroySubtree disposition."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.OwnershipConflict, diagnostics);
            }

            List<ScopeRecord> kept = new List<ScopeRecord>();
            IReadOnlyList<ScopeRecord> all = current.Scopes.Scopes;
            for (int i = 0; i < all.Count; i++)
            {
                if (!Contains(affectedScopes, all[i].Scope))
                {
                    kept.Add(all[i]);
                }
            }

            ScopeRegistry nextTree = RebuildWithRoot(current.Scopes.Root, kept);

            List<InstallEntry> nextInstalls = new List<InstallEntry>();
            List<StateDisposition> dispositions = new List<StateDisposition>();
            List<PluginInstanceId> retired = new List<PluginInstanceId>();
            for (int i = 0; i < current.Installs.Count; i++)
            {
                InstallEntry entry = current.Installs[i];
                if (Contains(affectedScopes, entry.Scope))
                {
                    if (!payload.DestroySubtree)
                    {
                        // Unreachable: a non-destroying removal of a scope with installs was already refused.
                        continue;
                    }

                    retired.Add(entry.Instance);
                    dispositions.AddRange(DispositionsFor(entry, payload.Subject, operation, diagnostics));
                    continue;
                }

                nextInstalls.Add(entry);
            }

            if (diagnostics.Count != 0)
            {
                return Rejected(current, payload, operation, inputHash, diagnostics[0].Code, diagnostics);
            }

            CompositionState after = current.With(scopes: nextTree, installs: nextInstalls);
            return Finish(current, after, payload, operation, inputHash, dispositions, retired, diagnostics);
        }

        private static CompositionEditPlan PlanScopeIsolation(
            CompositionState current,
            CompositionEditPayload payload,
            OperationId operation,
            ContentHash inputHash,
            List<Diagnostic> diagnostics)
        {
            if (!current.Scopes.TryGet(payload.Scope, out ScopeRecord? record) || record == null)
            {
                diagnostics.Add(Diag(DiagnosticCode.MissingDependency, payload.Subject, operation, "The scope does not exist in this world."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.MissingDependency, diagnostics);
            }

            if ((payload.ServiceIsolation.AllContracts && payload.ServiceIsolation.Contracts.Count != 0) ||
                (payload.CapabilityIsolation.AllContracts && payload.CapabilityIsolation.Contracts.Count != 0))
            {
                // `*` means every contract, so naming contracts beside it contradicts the declaration (P-016).
                diagnostics.Add(Diag(DiagnosticCode.CapabilityConflict, payload.Subject, operation, "An isolation set cannot name contracts while declaring `*`."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.CapabilityConflict, diagnostics);
            }

            ScopeRecord replacement = new ScopeRecord(
                record.Scope,
                record.Parent,
                record.Depth,
                payload.ServiceIsolation,
                payload.CapabilityIsolation,
                payload.Exclusions,
                record.Grants);

            if (!ReplaceScope(current.Scopes, replacement, out ScopeRegistry? next, out DiagnosticCode code) || next == null)
            {
                diagnostics.Add(Diag(code, payload.Subject, operation, "The isolation edit was refused."));
                return Rejected(current, payload, operation, inputHash, code, diagnostics);
            }

            CompositionState after = current.With(scopes: next);
            return Finish(current, after, payload, operation, inputHash, null, null, diagnostics);
        }

        private static CompositionEditPlan PlanScopeGrants(
            CompositionState current,
            CompositionEditPayload payload,
            OperationId operation,
            ContentHash inputHash,
            List<Diagnostic> diagnostics)
        {
            if (!current.Scopes.TryGet(payload.Scope, out ScopeRecord? record) || record == null)
            {
                diagnostics.Add(Diag(DiagnosticCode.MissingDependency, payload.Subject, operation, "The scope does not exist in this world."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.MissingDependency, diagnostics);
            }

            DiagnosticCode grantCode = ValidateGrants(current, payload, operation, diagnostics);
            if (grantCode != DiagnosticCode.None)
            {
                return Rejected(current, payload, operation, inputHash, grantCode, diagnostics);
            }

            ScopeRecord replacement = new ScopeRecord(
                record.Scope,
                record.Parent,
                record.Depth,
                record.ServiceIsolation,
                record.CapabilityIsolation,
                record.Exclusions,
                new ScopeGrants(payload.Imports));

            if (!ReplaceScope(current.Scopes, replacement, out ScopeRegistry? next, out DiagnosticCode code) || next == null)
            {
                diagnostics.Add(Diag(code, payload.Subject, operation, "The grant edit was refused."));
                return Rejected(current, payload, operation, inputHash, code, diagnostics);
            }

            CompositionState after = current.With(scopes: next);
            return Finish(current, after, payload, operation, inputHash, null, null, diagnostics);
        }

        private static CompositionEditPlan PlanMount(
            CompositionState current,
            CompositionEditPayload payload,
            OperationId operation,
            ContentHash inputHash,
            IPluginManifestSource manifests,
            List<Diagnostic> diagnostics)
        {
            if (payload.Instance.IsDefault || payload.PluginType.IsDefault)
            {
                diagnostics.Add(Diag(DiagnosticCode.MissingDependency, payload.Subject, operation, "A mount requires real plugin type and instance identities (P-004)."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.MissingDependency, diagnostics);
            }

            bool remount = false;
            if (current.TryGetInstall(payload.Instance, out InstallEntry? existing) && existing != null)
            {
                if (existing.State != InstallationState.Disposed)
                {
                    diagnostics.Add(Diag(DiagnosticCode.OwnershipConflict, payload.Subject, operation, "One world rejects duplicate live installation identities (P-004)."));
                    return Rejected(current, payload, operation, inputHash, DiagnosticCode.OwnershipConflict, diagnostics);
                }

                remount = true;
            }

            if (!current.Scopes.TryGet(payload.Scope, out ScopeRecord? _))
            {
                diagnostics.Add(Diag(DiagnosticCode.MissingDependency, payload.Subject, operation, "An installation mounts at one existing scope (P-010)."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.MissingDependency, diagnostics);
            }

            if (!manifests.TryGetManifest(payload.PluginType, out PluginManifest? manifest) || manifest == null)
            {
                // P-009: a missing precompiled factory/catalog entry rejects instead of being discovered later.
                diagnostics.Add(Diag(DiagnosticCode.MissingDependency, payload.Subject, operation, "The catalog has no manifest for the declared plugin type."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.MissingDependency, diagnostics);
            }

            if (!manifest.PluginTypeId.Equals(payload.PluginType))
            {
                diagnostics.Add(Diag(DiagnosticCode.OwnershipConflict, payload.Subject, operation, "The resolved manifest declares a different plugin type."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.OwnershipConflict, diagnostics);
            }

            DiagnosticCode configCode = ValidateConfigDocument(manifests, manifest, payload, operation, diagnostics, out ConfigDocument? effective);
            if (configCode != DiagnosticCode.None || effective == null)
            {
                return Rejected(current, payload, operation, inputHash, configCode, diagnostics);
            }

            // A remount of the same stable installation identity gets a new generation, so every handle and
            // callback token from the removed activation is invalid even though the id was reused (P-005).
            InstallationGeneration generation = InstallationGeneration.First;
            if (remount)
            {
                InstallationGeneration previous = current.TryGetInstall(payload.Instance, out InstallEntry? removed) && removed != null
                    ? removed.Record.Generation
                    : InstallationGeneration.Zero;
                if (!previous.TryIncrement(out generation))
                {
                    diagnostics.Add(Diag(DiagnosticCode.BudgetExceeded, payload.Subject, operation, "The installation generation is exhausted; world recreation is required (P-005)."));
                    return Rejected(current, payload, operation, inputHash, DiagnosticCode.BudgetExceeded, diagnostics);
                }
            }

            InstallRecord record = new InstallRecord(
                payload.Instance,
                payload.PluginType,
                payload.Scope,
                payload.ConfigRevision,
                payload.ConfigHash,
                payload.Priority,
                generation,
                ActivationEpoch.First);

            InstallEntry entry = new InstallEntry(record, manifest, effective, payload.Selections, InstallationState.Registered, null, null);
            CompositionState after = current.WithInstall(entry);

            List<StateDisposition> dispositions = DispositionsFor(entry, payload.Subject, operation, diagnostics);
            if (diagnostics.Count != 0)
            {
                return Rejected(current, payload, operation, inputHash, diagnostics[0].Code, diagnostics);
            }

            return Finish(current, after, payload, operation, inputHash, dispositions, null, diagnostics);
        }

        private static CompositionEditPlan PlanUnmount(
            CompositionState current,
            CompositionEditPayload payload,
            OperationId operation,
            ContentHash inputHash,
            List<Diagnostic> diagnostics)
        {
            if (!current.TryGetInstall(payload.Instance, out InstallEntry? entry) || entry == null)
            {
                diagnostics.Add(Diag(DiagnosticCode.MissingDependency, payload.Subject, operation, "No such installation is registered in this world."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.MissingDependency, diagnostics);
            }

            if (entry.State == InstallationState.Disposed)
            {
                diagnostics.Add(Diag(DiagnosticCode.OwnershipConflict, payload.Subject, operation, "The installation is already disposed."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.OwnershipConflict, diagnostics);
            }

            // Teardown walks the diagram's edges one at a time: an active installation quiesces before it can
            // retire, and a state the diagram gives no teardown edge is refused rather than forced (P-046).
            if (!InstallationStateMachine.TryTeardownPath(entry.State, out IReadOnlyList<InstallationState>? teardown) || teardown == null)
            {
                diagnostics.Add(Diag(DiagnosticCode.OwnershipConflict, payload.Subject, operation, "The installation has no lawful teardown path from " + entry.State + "."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.OwnershipConflict, diagnostics);
            }

            InstallationState cursor = entry.State;
            for (int i = 0; i < teardown.Count; i++)
            {
                LifecycleTransition step = InstallationStateMachine.Request(cursor, teardown[i]);
                if (!step.Allowed)
                {
                    diagnostics.Add(Diag(step.Code, payload.Subject, operation, "The teardown step " + cursor + " -> " + teardown[i] + " is not a legal lifecycle edge."));
                    return Rejected(current, payload, operation, inputHash, step.Code, diagnostics);
                }

                cursor = teardown[i];
            }

            LifecycleTransition toDisposed = InstallationStateMachine.Request(InstallationState.Retiring, InstallationState.Disposed);
            if (!toDisposed.Allowed)
            {
                diagnostics.Add(Diag(toDisposed.Code, payload.Subject, operation, "A retiring installation can only be disposed."));
                return Rejected(current, payload, operation, inputHash, toDisposed.Code, diagnostics);
            }

            List<StateDisposition> dispositions = DispositionsFor(entry, payload.Subject, operation, diagnostics);
            if (diagnostics.Count != 0)
            {
                return Rejected(current, payload, operation, inputHash, diagnostics[0].Code, diagnostics);
            }

            // A published removal keeps a terminal teardown record that stays inspectable (P-046, O-07).
            InstallEntry disposed = entry.With(state: InstallationState.Disposed, bindings: Array.Empty<ServiceBinding>(), diagnostics: null);
            CompositionState after = current.WithInstall(disposed);

            List<PluginInstanceId> retired = new List<PluginInstanceId> { entry.Instance };
            return Finish(current, after, payload, operation, inputHash, dispositions, retired, diagnostics);
        }

        private static CompositionEditPlan PlanReconfigure(
            CompositionState current,
            CompositionEditPayload payload,
            OperationId operation,
            ContentHash inputHash,
            IPluginManifestSource manifests,
            List<Diagnostic> diagnostics)
        {
            if (!current.TryGetInstall(payload.Instance, out InstallEntry? entry) || entry == null)
            {
                diagnostics.Add(Diag(DiagnosticCode.MissingDependency, payload.Subject, operation, "No such installation is registered in this world."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.MissingDependency, diagnostics);
            }

            if (entry.State == InstallationState.Disposed || entry.State == InstallationState.Retiring)
            {
                diagnostics.Add(Diag(DiagnosticCode.OwnershipConflict, payload.Subject, operation, "A retiring or disposed installation cannot be reconfigured."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.OwnershipConflict, diagnostics);
            }

            if (payload.ConfigRevision.Value <= entry.Record.ConfigRevision.Value)
            {
                diagnostics.Add(Diag(DiagnosticCode.UnsupportedVersion, payload.Subject, operation, "A reconfiguration must advance the configuration revision."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.UnsupportedVersion, diagnostics);
            }

            DiagnosticCode patchCode = ValidateConfigPatch(manifests, entry, payload, operation, diagnostics);
            if (patchCode != DiagnosticCode.None)
            {
                return Rejected(current, payload, operation, inputHash, patchCode, diagnostics);
            }

            ConfigComposeResult composed = ConfigComposer.Compose(new[]
            {
                DefaultLayer(manifests, entry),
                new ConfigLayer(ConfigLayerOrigin.InheritedContribution, entry.Instance.Value, entry.Config),
                new ConfigLayer(ConfigLayerOrigin.LocalPatch, entry.Instance.Value, payload.Config),
            });
            if (!composed.Succeeded)
            {
                diagnostics.Add(Diag(composed.Code, payload.Subject, operation, "The configuration layers cannot be composed."));
                return Rejected(current, payload, operation, inputHash, composed.Code, diagnostics);
            }

            ContentHash actualHash = ConfigDocumentCodec.HashOf(composed.Value);
            if (payload.ConfigHash != actualHash)
            {
                // The declaration claims a content hash its own configuration document does not have.
                diagnostics.Add(Diag(DiagnosticCode.UnsupportedVersion, payload.Subject, operation, "The declared configuration hash does not describe the composed configuration."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.UnsupportedVersion, diagnostics);
            }

            ActivationEpoch epoch = entry.Record.ActivationEpoch;
            if (!epoch.TryIncrement(out ActivationEpoch nextEpoch))
            {
                // Counter exhaustion rejects the reconfiguration instead of wrapping (P-005).
                diagnostics.Add(Diag(DiagnosticCode.BudgetExceeded, payload.Subject, operation, "The activation epoch is exhausted; world recreation is required (P-005)."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.BudgetExceeded, diagnostics);
            }

            InstallRecord record = new InstallRecord(
                entry.Record.Instance,
                entry.Record.PluginType,
                entry.Record.Scope,
                payload.ConfigRevision,
                payload.ConfigHash,
                entry.Record.Priority,
                entry.Record.Generation,
                nextEpoch);

            InstallEntry replacement = new InstallEntry(
                record,
                entry.Manifest,
                composed.Value,
                entry.Selections,
                entry.State,
                null,
                null);

            CompositionState after = current.WithInstall(replacement);
            return Finish(current, after, payload, operation, inputHash, null, null, diagnostics);
        }

        private static CompositionEditPlan PlanLifecycle(
            CompositionState current,
            CompositionEditPayload payload,
            OperationId operation,
            ContentHash inputHash,
            InstallationState destination,
            List<Diagnostic> diagnostics)
        {
            if (!current.TryGetInstall(payload.Instance, out InstallEntry? entry) || entry == null)
            {
                diagnostics.Add(Diag(DiagnosticCode.MissingDependency, payload.Subject, operation, "No such installation is registered in this world."));
                return Rejected(current, payload, operation, inputHash, DiagnosticCode.MissingDependency, diagnostics);
            }

            InstallationState from = entry.State;
            InstallationState intermediate = destination == InstallationState.Suspended
                ? InstallationState.Quiescing
                : InstallationState.Preparing;

            LifecycleTransition first = InstallationStateMachine.Request(from, intermediate);
            if (!first.Allowed)
            {
                diagnostics.Add(Diag(first.Code, payload.Subject, operation, "The installation cannot enter " + intermediate + " from " + from + "."));
                return Rejected(current, payload, operation, inputHash, first.Code, diagnostics);
            }

            LifecycleTransition second = InstallationStateMachine.Request(intermediate, destination);
            if (intermediate != destination && !second.Allowed)
            {
                diagnostics.Add(Diag(second.Code, payload.Subject, operation, "The requested lifecycle step is not a legal edge (P-046)."));
                return Rejected(current, payload, operation, inputHash, second.Code, diagnostics);
            }

            List<StateDisposition> dispositions = DispositionsFor(entry, payload.Subject, operation, diagnostics);
            if (diagnostics.Count != 0)
            {
                return Rejected(current, payload, operation, inputHash, diagnostics[0].Code, diagnostics);
            }

            // Suspend retracts the active contribution; resume rederives against the current ancestry, so the
            // resolver decides on Active or WaitingForDependencies from the declared Preparing state (P-046).
            InstallEntry replacement = entry.With(
                state: destination,
                bindings: destination == InstallationState.Suspended ? Array.Empty<ServiceBinding>() : null,
                diagnostics: null);

            CompositionState after = current.WithInstall(replacement);
            return Finish(current, after, payload, operation, inputHash, dispositions, null, diagnostics);
        }

        /// <summary>
        /// Common tail of every successful edit: resolve the service closure of the new definition, rebuild the
        /// install views with their lifecycle states and bindings, and compute the delta. A closure cycle rejects
        /// the whole proposal and keeps the old assembly (P-012).
        /// </summary>
        private static CompositionEditPlan Finish(
            CompositionState before,
            CompositionState after,
            CompositionEditPayload payload,
            OperationId operation,
            ContentHash inputHash,
            IReadOnlyList<StateDisposition>? dispositions,
            IReadOnlyList<PluginInstanceId>? retired,
            List<Diagnostic> diagnostics)
        {

            List<ServiceNode> nodes = new List<ServiceNode>();
            for (int i = 0; i < after.Installs.Count; i++)
            {
                InstallEntry entry = after.Installs[i];
                if (entry.State != InstallationState.Disposed)
                {
                    nodes.Add(entry.ToServiceNode());
                }
            }

            ServiceResolution resolution = ServiceResolver.Resolve(after.Scopes, nodes);
            if (!resolution.Succeeded)
            {
                for (int i = 0; i < resolution.Diagnostics.Count; i++)
                {
                    diagnostics.Add(Stamp(resolution.Diagnostics[i], operation));
                }

                if (resolution.Diagnostics.Count == 0)
                {
                    diagnostics.Add(Diag(resolution.Code, payload.Subject, operation, "The service dependency closure cannot be satisfied."));
                }
                return Rejected(before, payload, operation, inputHash, resolution.Code, diagnostics);
            }

            List<PluginInstanceId> retiredActivations = retired == null
                ? new List<PluginInstanceId>() : new List<PluginInstanceId>(retired);
            List<StateDisposition> allDispositions = dispositions == null
                ? new List<StateDisposition>() : new List<StateDisposition>(dispositions);
            List<InstallEntry> epochInstalls = new List<InstallEntry>(after.Installs.Count);
            bool epochsChanged = false;
            for (int i = 0; i < after.Installs.Count; i++)
            {
                InstallEntry entry = after.Installs[i];
                InstallationState nextState = resolution.TryGet(entry.Instance, out ServiceNodeResolution? resolved) && resolved != null
                    ? resolved.State : entry.State;
                if (before.TryGetInstall(entry.Instance, out InstallEntry? previous) && previous != null)
                {
                    if (nextState == InstallationState.Active &&
                        previous.State != InstallationState.Active && previous.State != InstallationState.Disposed &&
                        entry.Record.ActivationEpoch.Equals(previous.Record.ActivationEpoch))
                    {
                        if (!entry.Record.ActivationEpoch.TryIncrement(out ActivationEpoch epoch))
                        {
                            diagnostics.Add(Diag(DiagnosticCode.BudgetExceeded, payload.Subject, operation, "The activation epoch is exhausted."));
                            return Rejected(before, payload, operation, inputHash, DiagnosticCode.BudgetExceeded, diagnostics);
                        }

                        InstallRecord record = entry.Record;
                        entry = entry.With(record: new InstallRecord(record.Instance, record.PluginType, record.Scope,
                            record.ConfigRevision, record.ConfigHash, record.Priority, record.Generation, epoch));
                        epochsChanged = true;
                    }

                    if (previous.State == InstallationState.Active &&
                        (nextState != InstallationState.Active || !previous.Record.ActivationEpoch.Equals(entry.Record.ActivationEpoch)) &&
                        !retiredActivations.Contains(entry.Instance))
                    {
                        retiredActivations.Add(entry.Instance);
                        if (!entry.Instance.Equals(payload.Instance))
                        {
                            List<Diagnostic> policyDiagnostics = new List<Diagnostic>();
                            allDispositions.AddRange(DispositionsFor(previous, payload.Subject, operation, policyDiagnostics));
                            if (policyDiagnostics.Count != 0)
                            {
                                diagnostics.AddRange(policyDiagnostics);
                                return Rejected(before, payload, operation, inputHash, policyDiagnostics[0].Code, diagnostics);
                            }
                        }
                    }
                }

                epochInstalls.Add(entry);
            }

            if (epochsChanged)
            {
                after = after.With(installs: epochInstalls);
                nodes.Clear();
                for (int i = 0; i < after.Installs.Count; i++)
                {
                    if (after.Installs[i].State != InstallationState.Disposed)
                    {
                        nodes.Add(after.Installs[i].ToServiceNode());
                    }
                }

                resolution = ServiceResolver.Resolve(after.Scopes, nodes);
            }

            List<InstallEntry> resolvedInstalls = new List<InstallEntry>(after.Installs.Count);
            for (int i = 0; i < after.Installs.Count; i++)
            {
                InstallEntry entry = after.Installs[i];
                if (entry.State == InstallationState.Disposed || !resolution.TryGet(entry.Instance, out ServiceNodeResolution? node) || node == null)
                {
                    resolvedInstalls.Add(entry);
                    continue;
                }

                List<Diagnostic> nodeDiagnostics = new List<Diagnostic>(node.Diagnostics.Count);
                for (int d = 0; d < node.Diagnostics.Count; d++)
                {
                    Diagnostic diagnostic = Stamp(node.Diagnostics[d], operation);
                    nodeDiagnostics.Add(diagnostic);
                    if (node.Diagnostics[d].Code != DiagnosticCode.None)
                    {
                        diagnostics.Add(diagnostic);
                    }
                }

                resolvedInstalls.Add(entry.With(state: node.State, bindings: node.Bindings, diagnostics: nodeDiagnostics));
            }

            CompositionState resolvedState = after.With(installs: resolvedInstalls);
            CompositionDelta delta = ComputeDelta(before, resolvedState, payload);
            return new CompositionEditPlan(
                operation,
                payload.Subject,
                inputHash,
                before.Revision,
                before.Epoch,
                before,
                resolvedState,
                delta,
                resolution,
                allDispositions,
                resolution.ActivationOrder,
                OrderForTeardown(before, retiredActivations),
                DiagnosticCode.None,
                diagnostics);
        }

        /// <summary>
        /// Retirement order of the installations this plan removes: the reverse of the *previous* closure's
        /// activation order, so a consumer retires before the provider it bound to (P-012, P-048). The order is
        /// computed over the still-present previous definition and is therefore stable plan data, and a tie is
        /// broken by canonical identity so it never depends on sort implementation details (P-008).
        /// </summary>
        private static IReadOnlyList<PluginInstanceId> OrderForTeardown(CompositionState before, IReadOnlyList<PluginInstanceId>? retired)
        {
            if (retired == null || retired.Count == 0)
            {
                return Array.Empty<PluginInstanceId>();
            }

            List<ServiceNode> nodes = new List<ServiceNode>();
            for (int i = 0; i < before.Installs.Count; i++)
            {
                InstallEntry entry = before.Installs[i];
                if (entry.State != InstallationState.Disposed)
                {
                    nodes.Add(entry.ToServiceNode());
                }
            }

            ServiceResolution closure = ServiceResolver.Resolve(before.Scopes, nodes);
            IReadOnlyList<PluginInstanceId> activation =
                closure.Succeeded ? closure.ActivationOrder : Array.Empty<PluginInstanceId>();

            List<PluginInstanceId> ordered = new List<PluginInstanceId>(retired);
            ordered.Sort((left, right) =>
            {
                int rank = RankIn(activation, right).CompareTo(RankIn(activation, left));
                return rank != 0 ? rank : left.Value.CompareTo(right.Value);
            });

            return ordered;
        }

        private static int RankIn(IReadOnlyList<PluginInstanceId> order, PluginInstanceId instance)
        {
            for (int i = 0; i < order.Count; i++)
            {
                if (order[i].Equals(instance))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Composition delta of one planned edit, using the shared delta DTOs (05 s4).</summary>
        public static CompositionDelta ComputeDelta(CompositionState before, CompositionState after, CompositionEditPayload payload)
        {
            if (before == null)
            {
                throw new ArgumentNullException(nameof(before));
            }

            if (after == null)
            {
                throw new ArgumentNullException(nameof(after));
            }

            List<ScopeEdit> scopeEdits = new List<ScopeEdit>();
            IReadOnlyList<ScopeRecord> beforeScopes = before.Scopes.Scopes;
            IReadOnlyList<ScopeRecord> afterScopes = after.Scopes.Scopes;
            for (int i = 0; i < afterScopes.Count; i++)
            {
                ScopeRecord record = afterScopes[i];
                if (!before.Scopes.TryGet(record.Scope, out ScopeRecord? previous) || previous == null)
                {
                    scopeEdits.Add(new ScopeEdit(CompositionEditKind.Add, record.Scope, default(ScopeId), record.Parent));
                }
                else if (!previous.Parent.Equals(record.Parent))
                {
                    scopeEdits.Add(new ScopeEdit(CompositionEditKind.Reparent, record.Scope, previous.Parent, record.Parent));
                }
            }

            for (int i = 0; i < beforeScopes.Count; i++)
            {
                if (!after.Scopes.TryGet(beforeScopes[i].Scope, out ScopeRecord? _))
                {
                    scopeEdits.Add(new ScopeEdit(CompositionEditKind.Remove, beforeScopes[i].Scope, beforeScopes[i].Parent, default(ScopeId)));
                }
            }

            List<InstallEdit> installEdits = new List<InstallEdit>();
            List<ConfigEdit> configEdits = new List<ConfigEdit>();
            for (int i = 0; i < after.Installs.Count; i++)
            {
                InstallEntry entry = after.Installs[i];
                if (!before.TryGetInstall(entry.Instance, out InstallEntry? previous) || previous == null)
                {
                    installEdits.Add(new InstallEdit(CompositionEditKind.Add, entry.Instance, default(ScopeId), entry.Scope, InstallationState.Disposed, entry.State));
                    continue;
                }

                if (!previous.Scope.Equals(entry.Scope))
                {
                    installEdits.Add(new InstallEdit(CompositionEditKind.Reparent, entry.Instance, previous.Scope, entry.Scope, previous.State, entry.State));
                }
                else if (previous.State != entry.State)
                {
                    installEdits.Add(new InstallEdit(CompositionEditKind.Update, entry.Instance, previous.Scope, entry.Scope, previous.State, entry.State));
                }

                if (!previous.Record.ConfigHash.Equals(entry.Record.ConfigHash))
                {
                    configEdits.Add(new ConfigEdit(
                        entry.Instance,
                        previous.Record.ConfigRevision,
                        entry.Record.ConfigRevision,
                        previous.Record.ConfigHash,
                        entry.Record.ConfigHash));
                }
            }

            ModeEdit? mode = before.Mode != after.Mode ? new ModeEdit(payload.Scope, before.Mode, after.Mode) : (ModeEdit?)null;

            return new CompositionDelta(scopeEdits, installEdits, null, configEdits, mode);
        }

        /// <summary>
        /// Validated state dispositions for one affected installation, derived from its declared slot policies
        /// (P-032, P-033). A `TransferTo` slot without a declared transfer mapping is rejected: a missing policy
        /// is a validation error, never an implicit zero initialization or a quiet deletion.
        /// </summary>
        private static List<StateDisposition> DispositionsFor(
            InstallEntry entry,
            CompositionEditSubject subject,
            OperationId operation,
            List<Diagnostic> diagnostics)
        {
            List<StateDisposition> dispositions = new List<StateDisposition>();
            IReadOnlyList<StateSlotSpec> slots = entry.Manifest.StateSlots;
            for (int i = 0; i < slots.Count; i++)
            {
                StateSlotSpec slot = slots[i];
                StateDispositionKind kind;
                switch (slot.LastSupport)
                {
                    case LastSupportPolicy.RemoveDerived:
                        kind = StateDispositionKind.Retract;
                        break;
                    case LastSupportPolicy.PreserveDormant:
                        kind = StateDispositionKind.Retain;
                        break;
                    default:
                        kind = StateDispositionKind.Transfer;
                        if (slot.TransferPolicy.RegistrationKey.IsDefault)
                        {
                            diagnostics.Add(Diag(DiagnosticCode.MigrationRequired, subject, operation, "A TransferTo slot requires a declared transfer mapping (P-032)."));
                            return dispositions;
                        }

                        break;
                }

                // The slot key is target-scoped at apply time; this stored record carries the declared policy.
                dispositions.Add(new StateDisposition(
                    new StateSlotKey(default(TargetId), slot.Owner, slot.SlotId),
                    kind,
                    default(TargetId),
                    slot.TransferPolicy));
            }

            return dispositions;
        }

        private static DiagnosticCode ValidateGrants(
            CompositionState current,
            CompositionEditPayload payload,
            OperationId operation,
            List<Diagnostic> diagnostics)
        {
            for (int i = 0; i < payload.Imports.Count; i++)
            {
                CapabilityImport import = payload.Imports[i];
                if (import.CapabilityId.IsDefault || import.ProviderInstallationId.IsDefault)
                {
                    diagnostics.Add(Diag(DiagnosticCode.MissingDependency, payload.Subject, operation, "A capability import names real capability and provider identities (P-004)."));
                    return DiagnosticCode.MissingDependency;
                }

                if (!current.TryGetInstall(new PluginInstanceId(import.ProviderInstallationId.Value), out InstallEntry? provider) || provider == null)
                {
                    // A grant that names no provider installation cannot grant anything (P-013).
                    diagnostics.Add(Diag(DiagnosticCode.MissingDependency, payload.Subject, operation, "The imported provider installation is not registered in this world."));
                    return DiagnosticCode.MissingDependency;
                }

                if (provider.State == InstallationState.Disposed)
                {
                    diagnostics.Add(Diag(DiagnosticCode.MissingDependency, payload.Subject, operation, "The imported provider installation is disposed."));
                    return DiagnosticCode.MissingDependency;
                }
            }

            return DiagnosticCode.None;
        }

        private static DiagnosticCode ValidateConfigDocument(
            IPluginManifestSource manifests,
            PluginManifest manifest,
            CompositionEditPayload payload,
            OperationId operation,
            List<Diagnostic> diagnostics,
            out ConfigDocument? effective)
        {
            effective = null;
            if (!manifests.TryGetConfigDefaults(manifest.ConfigSchema, out ConfigDocument? defaults) || defaults == null)
            {
                diagnostics.Add(Diag(DiagnosticCode.MissingDependency, payload.Subject, operation, "The catalog has no configuration schema defaults for this manifest."));
                return DiagnosticCode.MissingDependency;
            }

            IReadOnlyList<ConfigField> fields = payload.Config.Fields;
            for (int i = 0; i < fields.Count; i++)
            {
                if (!defaults.HasField(fields[i].Key))
                {
                    // P-020: the contract declares its fields; an undeclared field is not a silent extension.
                    diagnostics.Add(Diag(DiagnosticCode.UnsupportedVersion, payload.Subject, operation, "The configuration sets a field the declared schema does not have."));
                    return DiagnosticCode.UnsupportedVersion;
                }
            }

            ConfigComposeResult composed = ConfigComposer.Compose(new[]
            {
                new ConfigLayer(ConfigLayerOrigin.SchemaDefaults, manifest.ConfigSchema.Id.Value, defaults),
                new ConfigLayer(ConfigLayerOrigin.LocalPatch, payload.Instance.Value, payload.Config),
            });
            if (!composed.Succeeded)
            {
                diagnostics.Add(Diag(composed.Code, payload.Subject, operation, "The mount configuration cannot be composed with its schema defaults."));
                return composed.Code;
            }

            if (payload.ConfigHash != ConfigDocumentCodec.HashOf(composed.Value))
            {
                diagnostics.Add(Diag(DiagnosticCode.UnsupportedVersion, payload.Subject, operation, "The declared configuration hash does not describe the composed configuration."));
                return DiagnosticCode.UnsupportedVersion;
            }

            // The installation stores the effective configuration, so a later patch composes against the same
            // provenance layers instead of layering the schema defaults twice (P-020).
            effective = composed.Value;
            return DiagnosticCode.None;
        }

        private static DiagnosticCode ValidateConfigPatch(
            IPluginManifestSource manifests,
            InstallEntry entry,
            CompositionEditPayload payload,
            OperationId operation,
            List<Diagnostic> diagnostics)
        {
            if (!manifests.TryGetConfigDefaults(entry.Manifest.ConfigSchema, out ConfigDocument? defaults) || defaults == null)
            {
                diagnostics.Add(Diag(DiagnosticCode.MissingDependency, payload.Subject, operation, "The catalog has no configuration schema defaults for this manifest."));
                return DiagnosticCode.MissingDependency;
            }

            IReadOnlyList<ConfigField> fields = payload.Config.Fields;
            for (int i = 0; i < fields.Count; i++)
            {
                if (!defaults.HasField(fields[i].Key) && !entry.Config.HasField(fields[i].Key))
                {
                    diagnostics.Add(Diag(DiagnosticCode.UnsupportedVersion, payload.Subject, operation, "The patch sets a field the declared schema does not have."));
                    return DiagnosticCode.UnsupportedVersion;
                }
            }

            return DiagnosticCode.None;
        }

        private static ConfigLayer DefaultLayer(IPluginManifestSource manifests, InstallEntry entry)
        {
            if (manifests.TryGetConfigDefaults(entry.Manifest.ConfigSchema, out ConfigDocument? defaults) && defaults != null)
            {
                return new ConfigLayer(ConfigLayerOrigin.SchemaDefaults, entry.Manifest.ConfigSchema.Id.Value, defaults);
            }

            return new ConfigLayer(ConfigLayerOrigin.SchemaDefaults, entry.Manifest.ConfigSchema.Id.Value, ConfigDocument.Empty);
        }

        private static bool ReplaceScope(ScopeRegistry scopes, ScopeRecord replacement, out ScopeRegistry? next, out DiagnosticCode code)
        {
            next = null;
            code = DiagnosticCode.None;

            List<ScopeRecord> records = new List<ScopeRecord>();
            IReadOnlyList<ScopeRecord> all = scopes.Scopes;
            for (int i = 0; i < all.Count; i++)
            {
                records.Add(all[i].Scope.Equals(replacement.Scope) ? replacement : all[i]);
            }

            next = RebuildWithRoot(scopes.Root, records);
            return true;
        }

        private static ScopeRegistry RebuildWithRoot(ScopeId root, List<ScopeRecord> records)
        {
            List<ScopeRecord> rest = new List<ScopeRecord>(records.Count);
            ScopeRecord? rootRecord = null;
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].Scope.Equals(root))
                {
                    rootRecord = records[i];
                }
                else
                {
                    rest.Add(records[i]);
                }
            }

            if (rootRecord == null)
            {
                throw new InvalidOperationException("The world root scope is always present in the desired composition.");
            }

            return new ScopeRegistry(rootRecord, rest);
        }

        private static bool Contains(List<ScopeId> scopes, ScopeId candidate)
        {
            for (int i = 0; i < scopes.Count; i++)
            {
                if (scopes[i].Equals(candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static CompositionEditPlan Rejected(
            CompositionState current,
            CompositionEditPayload payload,
            OperationId operation,
            ContentHash inputHash,
            DiagnosticCode code,
            List<Diagnostic> diagnostics) =>
            new CompositionEditPlan(
                operation,
                payload.Subject,
                inputHash,
                current.Revision,
                current.Epoch,
                current,
                current,
                null,
                null,
                null,
                null,
                null,
                code,
                diagnostics);

        /// <summary>
        /// One validation diagnostic. P-052 requires a stable code, the operation identity, the phase and a
        /// retry classification on every diagnostic, so this assembly never emits a bare code: the classification
        /// table below is the caller's contract for what a retry would have to change.
        /// </summary>
        private static Diagnostic Diag(DiagnosticCode code, CompositionEditSubject subject, OperationId operation, string summary) =>
            new Diagnostic(
                code,
                OperationPhase.Validation,
                operation,
                ContentHash.Empty,
                null,
                null,
                0L,
                0L,
                RetryOf(code),
                summary + " [" + subject + "]");

        /// <summary>
        /// What a caller has to change before retrying (P-049). Every code this applier emits rejects a
        /// declaration the caller submitted — a stale base, a missing catalog entry, an illegal graph, a
        /// conflicting identity, an unsupported version or a missing migration — so repeating the same input
        /// reproduces the same refusal and a retry must change the input. Transient resource failures are
        /// reported by the resource path as values, not as plan diagnostics.
        /// </summary>
        private static RetryClassification RetryOf(DiagnosticCode code)
        {
            _ = code;
            return RetryClassification.RequiresChangedInput;
        }

        /// <summary>
        /// Re-stamps one resolver diagnostic with the owning operation's identity, so a plan's diagnostics always
        /// name the operation they belong to. Every other field is preserved unchanged (P-052).
        /// </summary>
        private static Diagnostic Stamp(Diagnostic source, OperationId operation) =>
            new Diagnostic(
                source.Code,
                source.Phase,
                operation,
                source.PlanHash,
                source.InvolvedIds,
                source.InvolvedKeys,
                source.Count,
                source.BudgetLimit,
                source.Retry,
                source.Summary);
    }
}
