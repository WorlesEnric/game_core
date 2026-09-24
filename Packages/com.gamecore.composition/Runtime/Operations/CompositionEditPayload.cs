// GameCore.Composition — composition edit payloads and their canonical codec (O-02 to O-08).
//
// A public proposal carries a frozen payload whose schema the host decodes itself. The payload declares its
// subject explicitly (which scope/install/mode fact is changing); the request's `CompositionEditKind` must agree
// with that subject, so "add" can never be smuggled into a document that means "remove". Unknown subjects, a
// newer payload schema version and a malformed document are rejected before anything is planned (P-027, P-054).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Composition
{
    /// <summary>What one composition edit actually changes (the operation catalogue's O-02 to O-08 rows).</summary>
    public enum CompositionEditSubject
    {
        /// <summary>O-02: create a scope under an existing parent.</summary>
        ScopeCreate = 0,

        /// <summary>O-02: move a scope subtree under a new parent.</summary>
        ScopeReparent = 1,

        /// <summary>O-02: drop a scope, requiring an empty scope or an explicit subtree destruction.</summary>
        ScopeRemove = 2,

        /// <summary>P-016: replace a scope's named isolation sets.</summary>
        ScopeIsolation = 3,

        /// <summary>P-013: replace a scope's Conservative-mode import grants.</summary>
        ScopeGrants = 4,

        /// <summary>O-03: mount a precompiled plugin instance at a scope.</summary>
        InstallMount = 5,

        /// <summary>O-07: unmount an installation.</summary>
        InstallUnmount = 6,

        /// <summary>O-05: reconfigure an installation's immutable configuration patch.</summary>
        InstallReconfigure = 7,

        /// <summary>O-08: set the world propagation mode.</summary>
        ModeSet = 8,

        /// <summary>O-06: suspend an installation, retaining its definition and configuration.</summary>
        InstallSuspend = 9,

        /// <summary>O-04: resume an installation that requested an explicit suspend.</summary>
        InstallResume = 10,
    }

    /// <summary>
    /// One decoded edit payload. Every field of the union is always present in the canonical document (empty
    /// when unused), so the encoding is unambiguous and the same content always produces the same bytes.
    /// </summary>
    public sealed class CompositionEditPayload
    {
        public CompositionEditPayload(
            CompositionEditSubject subject,
            ScopeId scope,
            ScopeId parent,
            bool destroySubtree,
            IsolationSet? serviceIsolation,
            IsolationSet? capabilityIsolation,
            IReadOnlyList<ExclusionRule>? exclusions,
            IReadOnlyList<CapabilityImport>? imports,
            PluginTypeId pluginType,
            PluginInstanceId instance,
            DefinitionRevision configRevision,
            ContentHash configHash,
            ConfigDocument? config,
            int priority,
            IReadOnlyList<ServiceSelection>? selections,
            PropagationMode mode)
        {
            Subject = subject;
            Scope = scope;
            Parent = parent;
            DestroySubtree = destroySubtree;
            ServiceIsolation = serviceIsolation ?? new IsolationSet(false, null);
            CapabilityIsolation = capabilityIsolation ?? new IsolationSet(false, null);
            Exclusions = CanonicalScopeOrder.SortExclusions(exclusions);
            Imports = CanonicalScopeOrder.SortImports(imports);
            PluginType = pluginType;
            Instance = instance;
            ConfigRevision = configRevision;
            ConfigHash = configHash;
            Config = config ?? ConfigDocument.Empty;
            Priority = priority;
            Selections = CanonicalScopeOrder.SortSelections(selections);
            Mode = mode;
        }

        public CompositionEditSubject Subject { get; }

        public ScopeId Scope { get; }

        public ScopeId Parent { get; }

        /// <summary>An explicit subtree disposition for <see cref="CompositionEditSubject.ScopeRemove"/> (P-010).</summary>
        public bool DestroySubtree { get; }

        public IsolationSet ServiceIsolation { get; }

        public IsolationSet CapabilityIsolation { get; }

        public IReadOnlyList<ExclusionRule> Exclusions { get; }

        /// <summary>Conservative-mode imports of a scope (P-013).</summary>
        public IReadOnlyList<CapabilityImport> Imports { get; }

        public PluginTypeId PluginType { get; }

        public PluginInstanceId Instance { get; }

        public DefinitionRevision ConfigRevision { get; }

        public ContentHash ConfigHash { get; }

        /// <summary>For a mount: the full configuration document; for a reconfigure: the patch (mask) document.</summary>
        public ConfigDocument Config { get; }

        public int Priority { get; }

        public IReadOnlyList<ServiceSelection> Selections { get; }

        public PropagationMode Mode { get; }

        /// <summary>The edit kind this subject is expressible as; a mismatch is a malformed proposal (P-027).</summary>
        public CompositionEditKind ExpectedKind
        {
            get
            {
                switch (Subject)
                {
                    case CompositionEditSubject.ScopeCreate:
                    case CompositionEditSubject.InstallMount:
                        return CompositionEditKind.Add;
                    case CompositionEditSubject.ScopeReparent:
                        return CompositionEditKind.Reparent;
                    case CompositionEditSubject.ScopeRemove:
                    case CompositionEditSubject.InstallUnmount:
                        return CompositionEditKind.Remove;
                    default:
                        return CompositionEditKind.Update;
                }
            }
        }
    }

    /// <summary>Canonical codec for <see cref="CompositionEditPayload"/> documents (05 s6, P-054).</summary>
    public static class CompositionEditCodec
    {
        private const int SubjectField = 1;
        private const int ScopeField = 2;
        private const int ParentField = 3;
        private const int DestroySubtreeField = 4;
        private const int ServiceIsolationField = 5;
        private const int CapabilityIsolationField = 6;
        private const int ExclusionsField = 7;
        private const int ImportsField = 8;
        private const int PluginTypeField = 9;
        private const int InstanceField = 10;
        private const int ConfigRevisionField = 11;
        private const int ConfigHashField = 12;
        private const int ConfigField = 13;
        private const int PriorityField = 14;
        private const int SelectionsField = 15;
        private const int ModeField = 16;

        private const int IsolationAllField = 1;
        private const int IsolationContractsField = 2;

        private const int ExclusionKindField = 1;
        private const int ExclusionTargetField = 2;
        private const int ExclusionScopeField = 3;
        private const int ExclusionAtTargetField = 4;
        private const int ExclusionSubtreeField = 5;

        private const int ImportCapabilityField = 1;
        private const int ImportProviderField = 2;

        private const int SelectionContractField = 1;
        private const int SelectionVersionField = 2;
        private const int SelectionProviderField = 3;

        private const int Element = 1;

        /// <summary>Encodes one payload document, checksum included.</summary>
        public static FrozenPayload Encode(CompositionEditPayload payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            return DocumentCodec.WritePayload(
                CompositionSchemas.EditRequest,
                writer =>
                {
                    writer.WriteUInt32Field(SubjectField, (uint)payload.Subject);
                    writer.WriteId128Field(ScopeField, payload.Scope.Value);
                    writer.WriteId128Field(ParentField, payload.Parent.Value);
                    writer.WriteBoolField(DestroySubtreeField, payload.DestroySubtree);
                    writer.WriteBytesField(ServiceIsolationField, EncodeIsolation(payload.ServiceIsolation));
                    writer.WriteBytesField(CapabilityIsolationField, EncodeIsolation(payload.CapabilityIsolation));
                    DocumentCodec.WriteDocumentList(writer, ExclusionsField, Element, EncodeExclusions(payload.Exclusions));
                    DocumentCodec.WriteDocumentList(writer, ImportsField, Element, EncodeImports(payload.Imports));
                    writer.WriteId128Field(PluginTypeField, payload.PluginType.Value);
                    writer.WriteId128Field(InstanceField, payload.Instance.Value);
                    writer.WriteUInt64Field(ConfigRevisionField, payload.ConfigRevision.Value);
                    writer.WriteBytesField(ConfigHashField, payload.ConfigHash.ToArray());
                    writer.WriteBytesField(ConfigField, DocumentCodec.ToBytes(ConfigDocumentCodec.Encode(payload.Config)));
                    DocumentCodec.WriteDocumentList(writer, SelectionsField, Element, EncodeSelections(payload.Selections));
                    writer.WriteInt32Field(PriorityField, payload.Priority);
                    writer.WriteUInt32Field(ModeField, (uint)payload.Mode);
                });
        }

        /// <summary>
        /// Decodes a payload document. A document that is not exactly this shape is rejected: the caller must
        /// never act on a partially understood edit (P-054).
        /// </summary>
        public static bool TryDecode(FrozenPayload frozen, out CompositionEditPayload? payload, out DiagnosticCode code)
        {
            payload = null;
            code = DiagnosticCode.None;

            if (frozen == null)
            {
                throw new ArgumentNullException(nameof(frozen));
            }

            if (DocumentCodec.Open(frozen, CompositionSchemas.EditRequest, out EnvelopeReader reader) != DiagnosticCode.None)
            {
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            if (!reader.TryReadField(out EnvelopeField field) || !DocumentCodec.Expect(field, SubjectField, WireType.UInt32) ||
                !reader.TryReadUInt32(field, out uint rawSubject) || rawSubject > (uint)CompositionEditSubject.InstallResume)
            {
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            if (!reader.TryReadField(out field) || !DocumentCodec.Expect(field, ScopeField, WireType.Id128) ||
                !reader.TryReadId128(field, out Id128 scope))
            {
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            if (!reader.TryReadField(out field) || !DocumentCodec.Expect(field, ParentField, WireType.Id128) ||
                !reader.TryReadId128(field, out Id128 parent))
            {
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            if (!reader.TryReadField(out field) || !DocumentCodec.Expect(field, DestroySubtreeField, WireType.Bool) ||
                !reader.TryReadBool(field, out bool destroySubtree))
            {
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            if (!TryReadIsolation(reader, ServiceIsolationField, out IsolationSet? serviceIsolation) ||
                !TryReadIsolation(reader, CapabilityIsolationField, out IsolationSet? capabilityIsolation))
            {
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            if (!reader.TryReadField(out field) || !DocumentCodec.Expect(field, ExclusionsField, WireType.List) ||
                !DocumentCodec.TryReadDocumentList(reader, field, Element, out List<byte[]> exclusionDocuments) ||
                !TryDecodeExclusions(exclusionDocuments, out List<ExclusionRule>? exclusions))
            {
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            if (!reader.TryReadField(out field) || !DocumentCodec.Expect(field, ImportsField, WireType.List) ||
                !DocumentCodec.TryReadDocumentList(reader, field, Element, out List<byte[]> importDocuments) ||
                !TryDecodeImports(importDocuments, out List<CapabilityImport>? imports))
            {
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            if (!reader.TryReadField(out field) || !DocumentCodec.Expect(field, PluginTypeField, WireType.Id128) ||
                !reader.TryReadId128(field, out Id128 pluginType))
            {
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            if (!reader.TryReadField(out field) || !DocumentCodec.Expect(field, InstanceField, WireType.Id128) ||
                !reader.TryReadId128(field, out Id128 instance))
            {
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            if (!reader.TryReadField(out field) || !DocumentCodec.Expect(field, ConfigRevisionField, WireType.UInt64) ||
                !reader.TryReadUInt64(field, out ulong configRevision))
            {
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            if (!reader.TryReadField(out field) || !DocumentCodec.Expect(field, ConfigHashField, WireType.Bytes) ||
                !reader.TryReadBytes(field, out byte[]? configHashBytes) || configHashBytes == null ||
                configHashBytes.Length != ContentHash.SizeInBytes)
            {
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            if (!reader.TryReadField(out field) || !DocumentCodec.Expect(field, ConfigField, WireType.Bytes) ||
                !reader.TryReadBytes(field, out byte[]? configBytes) || configBytes == null ||
                !ConfigDocumentCodec.TryDecode(new FrozenPayload(configBytes), out ConfigDocument? config) || config == null)
            {
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            if (!reader.TryReadField(out field) || !DocumentCodec.Expect(field, SelectionsField, WireType.List) ||
                !DocumentCodec.TryReadDocumentList(reader, field, Element, out List<byte[]> selectionDocuments) ||
                !TryDecodeSelections(selectionDocuments, out List<ServiceSelection>? selections))
            {
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            if (!reader.TryReadField(out field) || !DocumentCodec.Expect(field, PriorityField, WireType.Int32) ||
                !reader.TryReadInt32(field, out int priority))
            {
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            if (!reader.TryReadField(out field) || !DocumentCodec.Expect(field, ModeField, WireType.UInt32) ||
                !reader.TryReadUInt32(field, out uint rawMode) || rawMode > (uint)PropagationMode.Conservative)
            {
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            if (!DocumentCodec.TryClose(reader))
            {
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            payload = new CompositionEditPayload(
                (CompositionEditSubject)rawSubject,
                new ScopeId(scope),
                new ScopeId(parent),
                destroySubtree,
                serviceIsolation,
                capabilityIsolation,
                exclusions,
                imports,
                new PluginTypeId(pluginType),
                new PluginInstanceId(instance),
                new DefinitionRevision(configRevision),
                new ContentHash(configHashBytes),
                config,
                priority,
                selections,
                (PropagationMode)rawMode);
            return true;
        }

        private static byte[] EncodeIsolation(IsolationSet set) =>
            DocumentCodec.Write(
                CompositionSchemas.IsolationSet,
                writer =>
                {
                    writer.WriteBoolField(IsolationAllField, set.AllContracts);
                    DocumentCodec.WriteIdList(writer, IsolationContractsField, IsolationContractsField, set.Contracts);
                });

        private static bool TryReadIsolation(EnvelopeReader reader, int fieldId, out IsolationSet? set)
        {
            set = null;
            if (!reader.TryReadField(out EnvelopeField field) || !DocumentCodec.Expect(field, fieldId, WireType.Bytes) ||
                !reader.TryReadBytes(field, out byte[]? bytes) || bytes == null)
            {
                return false;
            }

            EnvelopeReader nested = new EnvelopeReader(bytes);
            if (!nested.TryReadHeader(out EnvelopeHeader header) || !header.Schema.Id.Equals(CompositionSchemas.IsolationSet.Id))
            {
                return false;
            }

            if (!nested.TryReadField(out EnvelopeField allField) || !DocumentCodec.Expect(allField, IsolationAllField, WireType.Bool) ||
                !nested.TryReadBool(allField, out bool allContracts))
            {
                return false;
            }

            if (!nested.TryReadField(out EnvelopeField contractsField) ||
                !DocumentCodec.TryReadIdList(nested, contractsField, IsolationContractsField, out List<Id128> contracts) ||
                !DocumentCodec.TryClose(nested))
            {
                return false;
            }

            set = new IsolationSet(allContracts, contracts);
            return true;
        }

        private static List<byte[]> EncodeExclusions(IReadOnlyList<ExclusionRule> exclusions)
        {
            List<byte[]> documents = new List<byte[]>(exclusions.Count);
            for (int i = 0; i < exclusions.Count; i++)
            {
                ExclusionRule rule = exclusions[i];
                documents.Add(DocumentCodec.Write(
                    CompositionSchemas.ExclusionRule,
                    writer =>
                    {
                        writer.WriteUInt32Field(ExclusionKindField, (uint)rule.Kind);
                        writer.WriteId128Field(ExclusionTargetField, rule.TargetId);
                        writer.WriteId128Field(ExclusionScopeField, rule.AtScope.Value);
                        writer.WriteId128Field(ExclusionAtTargetField, rule.AtTarget.Value);
                        writer.WriteBoolField(ExclusionSubtreeField, rule.AppliesToSubtree);
                    }));
            }

            return documents;
        }

        private static bool TryDecodeExclusions(List<byte[]> documents, out List<ExclusionRule>? exclusions)
        {
            exclusions = new List<ExclusionRule>(documents.Count);
            for (int i = 0; i < documents.Count; i++)
            {
                EnvelopeReader reader = new EnvelopeReader(documents[i]);
                if (!reader.TryReadHeader(out EnvelopeHeader header) || !header.Schema.Id.Equals(CompositionSchemas.ExclusionRule.Id))
                {
                    return false;
                }

                if (!reader.TryReadField(out EnvelopeField field) || !DocumentCodec.Expect(field, ExclusionKindField, WireType.UInt32) ||
                    !reader.TryReadUInt32(field, out uint kind) || kind > (uint)ExclusionTargetKind.Provider)
                {
                    return false;
                }

                if (!reader.TryReadField(out field) || !DocumentCodec.Expect(field, ExclusionTargetField, WireType.Id128) ||
                    !reader.TryReadId128(field, out Id128 target))
                {
                    return false;
                }

                if (!reader.TryReadField(out field) || !DocumentCodec.Expect(field, ExclusionScopeField, WireType.Id128) ||
                    !reader.TryReadId128(field, out Id128 atScope))
                {
                    return false;
                }

                if (!reader.TryReadField(out field) || !DocumentCodec.Expect(field, ExclusionAtTargetField, WireType.Id128) ||
                    !reader.TryReadId128(field, out Id128 atTarget))
                {
                    return false;
                }

                if (!reader.TryReadField(out field) || !DocumentCodec.Expect(field, ExclusionSubtreeField, WireType.Bool) ||
                    !reader.TryReadBool(field, out bool subtree) || !DocumentCodec.TryClose(reader))
                {
                    return false;
                }

                exclusions.Add(new ExclusionRule(
                    (ExclusionTargetKind)kind,
                    target,
                    new ScopeId(atScope),
                    new TargetId(atTarget),
                    subtree));
            }

            return true;
        }

        private static List<byte[]> EncodeImports(IReadOnlyList<CapabilityImport> imports)
        {
            List<byte[]> documents = new List<byte[]>(imports.Count);
            for (int i = 0; i < imports.Count; i++)
            {
                CapabilityImport import = imports[i];
                documents.Add(DocumentCodec.Write(
                    CompositionSchemas.CapabilityGrant,
                    writer =>
                    {
                        writer.WriteId128Field(ImportCapabilityField, import.CapabilityId.Value);
                        writer.WriteId128Field(ImportProviderField, import.ProviderInstallationId.Value);
                    }));
            }

            return documents;
        }

        private static bool TryDecodeImports(List<byte[]> documents, out List<CapabilityImport>? imports)
        {
            imports = new List<CapabilityImport>(documents.Count);
            for (int i = 0; i < documents.Count; i++)
            {
                EnvelopeReader reader = new EnvelopeReader(documents[i]);
                if (!reader.TryReadHeader(out EnvelopeHeader header) || !header.Schema.Id.Equals(CompositionSchemas.CapabilityGrant.Id))
                {
                    return false;
                }

                if (!reader.TryReadField(out EnvelopeField field) || !DocumentCodec.Expect(field, ImportCapabilityField, WireType.Id128) ||
                    !reader.TryReadId128(field, out Id128 capability))
                {
                    return false;
                }

                if (!reader.TryReadField(out field) || !DocumentCodec.Expect(field, ImportProviderField, WireType.Id128) ||
                    !reader.TryReadId128(field, out Id128 provider) || !DocumentCodec.TryClose(reader))
                {
                    return false;
                }

                imports.Add(new CapabilityImport(new CapabilityId(capability), new ProviderInstallationId(provider)));
            }

            return true;
        }

        private static List<byte[]> EncodeSelections(IReadOnlyList<ServiceSelection> selections)
        {
            List<byte[]> documents = new List<byte[]>(selections.Count);
            for (int i = 0; i < selections.Count; i++)
            {
                ServiceSelection selection = selections[i];
                documents.Add(DocumentCodec.Write(
                    CompositionSchemas.ServiceSelection,
                    writer =>
                    {
                        writer.WriteId128Field(SelectionContractField, selection.Contract.ContractId);
                        writer.WriteUInt32Field(SelectionVersionField, selection.Contract.Version);
                        writer.WriteId128Field(SelectionProviderField, selection.Provider.Value);
                    }));
            }

            return documents;
        }

        private static bool TryDecodeSelections(List<byte[]> documents, out List<ServiceSelection>? selections)
        {
            selections = new List<ServiceSelection>(documents.Count);
            for (int i = 0; i < documents.Count; i++)
            {
                EnvelopeReader reader = new EnvelopeReader(documents[i]);
                if (!reader.TryReadHeader(out EnvelopeHeader header) || !header.Schema.Id.Equals(CompositionSchemas.ServiceSelection.Id))
                {
                    return false;
                }

                if (!reader.TryReadField(out EnvelopeField field) || !DocumentCodec.Expect(field, SelectionContractField, WireType.Id128) ||
                    !reader.TryReadId128(field, out Id128 contract))
                {
                    return false;
                }

                if (!reader.TryReadField(out field) || !DocumentCodec.Expect(field, SelectionVersionField, WireType.UInt32) ||
                    !reader.TryReadUInt32(field, out uint version))
                {
                    return false;
                }

                if (!reader.TryReadField(out field) || !DocumentCodec.Expect(field, SelectionProviderField, WireType.Id128) ||
                    !reader.TryReadId128(field, out Id128 provider) || !DocumentCodec.TryClose(reader))
                {
                    return false;
                }

                selections.Add(new ServiceSelection(new ContractRef(contract, version), new ProviderInstallationId(provider)));
            }

            return true;
        }
    }
}
