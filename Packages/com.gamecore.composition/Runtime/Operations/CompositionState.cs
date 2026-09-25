// GameCore.Composition — the desired composition state: scopes, installs, mode and their inspection views.
//
// One `CompositionState` is one immutable image of the desired composition (05 s1): the scope tree plus one
// record per installation, each carrying its explicit saved assembly data, its effective configuration and its
// last resolved service bindings. Editing produces a new state; nothing here is ever mutated in place, which is
// what makes "all admitted edits produce immutable proposals" true by construction (GC-004 DoD, P-027).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Composition
{
    /// <summary>Where plugin manifests and their configuration schemas come from (generated catalog in production).</summary>
    public interface IPluginManifestSource
    {
        /// <summary>Resolves a precompiled plugin type; a miss is reported, never substituted (P-009).</summary>
        bool TryGetManifest(PluginTypeId pluginType, out PluginManifest? manifest);

        /// <summary>
        /// Declared configuration schema defaults of a config schema: the lowest configuration layer (P-020).
        /// A miss means the schema is unknown for configuration purposes.
        /// </summary>
        bool TryGetConfigDefaults(SchemaRef schema, out ConfigDocument? defaults);
    }

    /// <summary>
    /// One installation in the desired composition: its saved record, its current lifecycle state, its
    /// immutable configuration and the service bindings of its last resolution (P-046).
    /// </summary>
    public sealed class InstallEntry
    {
        public InstallEntry(
            InstallRecord record,
            PluginManifest manifest,
            ConfigDocument config,
            IReadOnlyList<ServiceSelection>? selections,
            InstallationState state,
            IReadOnlyList<ServiceBinding>? bindings,
            IReadOnlyList<Diagnostic>? diagnostics)
        {
            Record = record ?? throw new ArgumentNullException(nameof(record));
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Selections = CanonicalScopeOrder.SortSelections(selections);
            State = state;
            Bindings = ContractCollections.Freeze(bindings);
            Diagnostics = ContractCollections.Freeze(diagnostics);
        }

        public InstallRecord Record { get; }

        public PluginManifest Manifest { get; }

        /// <summary>Effective configuration document after layered composition (P-020).</summary>
        public ConfigDocument Config { get; }

        public IReadOnlyList<ServiceSelection> Selections { get; }

        /// <summary>Lifecycle state; <see cref="InstallationState.WaitingForDependencies"/> after a missing required provider.</summary>
        public InstallationState State { get; }

        /// <summary>Epoch-bound bindings from the resolution of the containing state (P-007).</summary>
        public IReadOnlyList<ServiceBinding> Bindings { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public PluginInstanceId Instance => Record.Instance;

        public ScopeId Scope => Record.Scope;

        public InstallEntry With(
            InstallRecord? record = null,
            ConfigDocument? config = null,
            InstallationState? state = null,
            IReadOnlyList<ServiceBinding>? bindings = null,
            IReadOnlyList<Diagnostic>? diagnostics = null) =>
            new InstallEntry(
                record ?? Record,
                Manifest,
                config ?? Config,
                Selections,
                state ?? State,
                bindings ?? Bindings,
                diagnostics ?? Diagnostics);

        /// <summary>Snapshot view of this installation (P-046).</summary>
        public InstallSnapshot ToSnapshot() => new InstallSnapshot(Record, State, Bindings, Diagnostics);

        /// <summary>The resolver's pure view of this installation (P-011).</summary>
        public ServiceNode ToServiceNode() =>
            new ServiceNode(
                Instance,
                Record.PluginType,
                Scope,
                State,
                Record.ActivationEpoch,
                Manifest.ServiceExports,
                Manifest.ServiceDependencies,
                Selections);
    }

    /// <summary>
    /// Immutable desired composition: one world, one scope tree, one record per installation, one world-level
    /// mode. The committed instance is authoritative; staged instances are private to unpublished operations.
    /// </summary>
    public sealed class CompositionState
    {
        private readonly Dictionary<Id128, InstallEntry> byInstance;
        private readonly List<InstallEntry> canonicalInstalls;

        public CompositionState(
            WorldId world,
            CompositionRevision revision,
            AssemblyEpoch epoch,
            LogicalStepId step,
            PropagationMode mode,
            ScopeRegistry scopes,
            IReadOnlyList<InstallEntry>? installs)
        {
            World = world;
            Revision = revision;
            Epoch = epoch;
            Step = step;
            Mode = mode;
            Scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));

            byInstance = new Dictionary<Id128, InstallEntry>();
            canonicalInstalls = new List<InstallEntry>();
            if (installs != null)
            {
                for (int i = 0; i < installs.Count; i++)
                {
                    InstallEntry entry = installs[i];
                    if (entry == null)
                    {
                        throw new ArgumentException("An install entry must not be null.", nameof(installs));
                    }

                    if (byInstance.ContainsKey(entry.Instance.Value))
                    {
                        // One world rejects duplicate live stable ids in a category (P-004).
                        throw new ArgumentException("An installation identity appears once in one world.", nameof(installs));
                    }

                    byInstance.Add(entry.Instance.Value, entry);
                    canonicalInstalls.Add(entry);
                }
            }

            canonicalInstalls.Sort(CompareInstalls);
        }

        /// <summary>Opens a fresh unexposed world state at the pre-publication revision/epoch 0 (05 s2).</summary>
        public static CompositionState CreateEmpty(WorldId world, ScopeRecord root, PropagationMode mode) =>
            CreateEmpty(world, root, mode, CompositionRevision.Zero, AssemblyEpoch.Zero);

        /// <summary>
        /// Opens a state at an explicit published assembly revision/epoch: the world's initial assembly is 1/1, so a
        /// lane joined to that world starts there and keeps one publication series (05 s2, P-006).
        /// </summary>
        public static CompositionState CreateEmpty(
            WorldId world,
            ScopeRecord root,
            PropagationMode mode,
            CompositionRevision revision,
            AssemblyEpoch epoch) =>
            new CompositionState(
                world,
                revision,
                epoch,
                LogicalStepId.Zero,
                mode,
                new ScopeRegistry(root, null),
                null);

        public WorldId World { get; }

        public CompositionRevision Revision { get; }

        public AssemblyEpoch Epoch { get; }

        public LogicalStepId Step { get; }

        /// <summary>One world-level propagation mode; <see cref="PropagationMode.Automatic"/> is the default (P-013).</summary>
        public PropagationMode Mode { get; }

        public ScopeRegistry Scopes { get; }

        /// <summary>Installations in canonical identity order, independent of mount order (P-008).</summary>
        public IReadOnlyList<InstallEntry> Installs => canonicalInstalls;

        public int InstallCount => canonicalInstalls.Count;

        public bool TryGetInstall(PluginInstanceId instance, out InstallEntry? entry)
        {
            if (byInstance.TryGetValue(instance.Value, out InstallEntry? found))
            {
                entry = found;
                return true;
            }

            entry = null;
            return false;
        }

        /// <summary>Installs registered directly at one scope, in canonical order (never descendants).</summary>
        public IReadOnlyList<PluginInstanceId> InstallsAt(ScopeId scope)
        {
            List<PluginInstanceId> atScope = new List<PluginInstanceId>();
            for (int i = 0; i < canonicalInstalls.Count; i++)
            {
                if (canonicalInstalls[i].Scope.Equals(scope) && canonicalInstalls[i].State != InstallationState.Disposed)
                {
                    atScope.Add(canonicalInstalls[i].Instance);
                }
            }

            return atScope;
        }

        /// <summary>
        /// A state with the given installs replaced/added and the given scope tree, at the same identities. The
        /// lifecycle resolution is left as supplied by the caller, so this is pure data assembly.
        /// </summary>
        public CompositionState With(
            ScopeRegistry? scopes = null,
            IReadOnlyList<InstallEntry>? installs = null,
            CompositionRevision? revision = null,
            AssemblyEpoch? epoch = null,
            LogicalStepId? step = null,
            PropagationMode? mode = null) =>
            new CompositionState(
                World,
                revision ?? Revision,
                epoch ?? Epoch,
                step ?? Step,
                mode ?? Mode,
                scopes ?? Scopes,
                installs ?? canonicalInstalls);

        /// <summary>Replaces one installation's entry, keeping canonical order deterministic.</summary>
        public CompositionState WithInstall(InstallEntry replacement)
        {
            if (replacement == null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }

            List<InstallEntry> next = new List<InstallEntry>(canonicalInstalls.Count + 1);
            bool replaced = false;
            for (int i = 0; i < canonicalInstalls.Count; i++)
            {
                if (canonicalInstalls[i].Instance.Equals(replacement.Instance))
                {
                    next.Add(replacement);
                    replaced = true;
                }
                else
                {
                    next.Add(canonicalInstalls[i]);
                }
            }

            if (!replaced)
            {
                next.Add(replacement);
            }

            return With(installs: next);
        }

        /// <summary>Removes one installation entirely (used by an unmount that has already published its removal).</summary>
        public CompositionState WithoutInstall(PluginInstanceId instance)
        {
            List<InstallEntry> next = new List<InstallEntry>(canonicalInstalls.Count);
            for (int i = 0; i < canonicalInstalls.Count; i++)
            {
                if (!canonicalInstalls[i].Instance.Equals(instance))
                {
                    next.Add(canonicalInstalls[i]);
                }
            }

            return With(installs: next);
        }

        /// <summary>
        /// Committed inspection view. Scopes carry their direct installs so a reader never has to walk
        /// descendants to answer a membership question (P-010).
        /// </summary>
        public CompositionStateSnapshot ToSnapshot()
        {
            List<ScopeSnapshot> scopes = new List<ScopeSnapshot>(Scopes.Count);
            IReadOnlyList<ScopeRecord> records = Scopes.Scopes;
            for (int i = 0; i < records.Count; i++)
            {
                scopes.Add(records[i].ToSnapshot(Mode, InstallsAt(records[i].Scope)));
            }

            List<InstallSnapshot> installs = new List<InstallSnapshot>(canonicalInstalls.Count);
            for (int i = 0; i < canonicalInstalls.Count; i++)
            {
                installs.Add(canonicalInstalls[i].ToSnapshot());
            }

            return new CompositionStateSnapshot(World, Revision, Epoch, Step, Mode, scopes, installs);
        }

        /// <summary>
        /// Canonical semantic fingerprint of the desired composition definition: scope records, install records,
        /// configuration hashes and mode. Timestamps, lease ids and object addresses are excluded, so the same
        /// definition always hashes the same (05 s4, P-027).
        /// </summary>
        public ContentHash Fingerprint()
        {
            byte[] document = DocumentCodec.Write(
                CompositionSchemas.DefinitionFingerprint,
                writer =>
                {
                    writer.WriteId128Field(1, World.Session);
                    writer.WriteUInt32Field(2, (uint)Mode);

                    IReadOnlyList<ScopeRecord> records = Scopes.Scopes;
                    List<byte[]> scopeDocuments = new List<byte[]>(records.Count);
                    for (int i = 0; i < records.Count; i++)
                    {
                        ScopeRecord record = records[i];
                        scopeDocuments.Add(DocumentCodec.Write(
                            CompositionSchemas.DefinitionFingerprint,
                            scope =>
                            {
                                scope.WriteId128Field(1, record.Scope.Value);
                                scope.WriteId128Field(2, record.Parent.Value);
                                scope.WriteUInt32Field(3, (uint)record.Depth);
                                scope.WriteBoolField(4, record.ServiceIsolation.AllContracts);
                                DocumentCodec.WriteIdList(scope, 5, 5, record.ServiceIsolation.Contracts);
                                scope.WriteBoolField(6, record.CapabilityIsolation.AllContracts);
                                DocumentCodec.WriteIdList(scope, 7, 7, record.CapabilityIsolation.Contracts);
                                scope.WriteUInt32Field(8, (uint)record.Exclusions.Count);
                                scope.WriteUInt32Field(9, (uint)record.Grants.Imports.Count);
                                for (int g = 0; g < record.Grants.Imports.Count; g++)
                                {
                                    scope.WriteId128Field(10, record.Grants.Imports[g].CapabilityId.Value);
                                    scope.WriteId128Field(11, record.Grants.Imports[g].ProviderInstallationId.Value);
                                }
                            }));
                    }

                    DocumentCodec.WriteDocumentList(writer, 10, 10, scopeDocuments);

                    List<byte[]> installDocuments = new List<byte[]>(canonicalInstalls.Count);
                    for (int i = 0; i < canonicalInstalls.Count; i++)
                    {
                        InstallEntry entry = canonicalInstalls[i];
                        installDocuments.Add(DocumentCodec.Write(
                            CompositionSchemas.DefinitionFingerprint,
                            install =>
                            {
                                install.WriteId128Field(1, entry.Instance.Value);
                                install.WriteId128Field(2, entry.Record.PluginType.Value);
                                install.WriteId128Field(3, entry.Scope.Value);
                                install.WriteUInt64Field(4, entry.Record.ConfigRevision.Value);
                                install.WriteBytesField(5, entry.Record.ConfigHash.ToArray());
                                install.WriteInt32Field(6, entry.Record.Priority);
                                install.WriteUInt64Field(7, entry.Record.Generation.Value);
                                install.WriteUInt32Field(8, (uint)entry.State);
                            }));
                    }

                    DocumentCodec.WriteDocumentList(writer, 20, 20, installDocuments);
                });

            return ContentHash.Compute(document);
        }

        private static int CompareInstalls(InstallEntry left, InstallEntry right) => left.Instance.Value.CompareTo(right.Instance.Value);
    }
}
