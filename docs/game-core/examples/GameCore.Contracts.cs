// Representative, compilable C# 9 contract skeleton; no runtime or Unity backend.
// Normative semantics: ../00-core-protocols.md. Production wrappers are generated.
using System;
using System.Collections.Generic;

namespace GameCore.Contracts
{
    public readonly struct Id128 : IEquatable<Id128>, IComparable<Id128>
    {
        public readonly ulong High;
        public readonly ulong Low;
        public Id128(ulong high, ulong low) { High = high; Low = low; }
        public bool Equals(Id128 other) => High == other.High && Low == other.Low;
        public override bool Equals(object? obj) => obj is Id128 other && Equals(other);
        public override int GetHashCode() => unchecked((int)(High ^ (High >> 32) ^ Low ^ (Low >> 32)));
        public int CompareTo(Id128 other)
        {
            int high = High.CompareTo(other.High);
            return high != 0 ? high : Low.CompareTo(other.Low);
        }
    }

    public readonly struct WorldId
    {
        public readonly Id128 Session;
        public WorldId(Id128 session) { Session = session; }
    }

    public readonly struct OperationId
    {
        public readonly WorldId World;
        public readonly Id128 IssuerId;
        public readonly ulong IssuerSequence;
        public OperationId(WorldId world, Id128 issuerId, ulong issuerSequence)
        { World = world; IssuerId = issuerId; IssuerSequence = issuerSequence; }
    }

    public readonly struct TargetHandle
    {
        public readonly WorldId World;
        public readonly int Slot;
        public readonly ulong Generation;
        public TargetHandle(WorldId world, int slot, ulong generation)
        { World = world; Slot = slot; Generation = generation; }
    }

    public readonly struct SnapshotToken
    {
        public readonly WorldId World;
        public readonly ulong AssemblyEpoch;
        public readonly ulong LogicalStepId;
        public SnapshotToken(WorldId world, ulong assemblyEpoch, ulong logicalStepId)
        { World = world; AssemblyEpoch = assemblyEpoch; LogicalStepId = logicalStepId; }
    }

    public readonly struct AsyncWorkToken
    {
        public readonly OperationId Operation;
        public readonly Id128 PluginInstanceId;
        public readonly ulong InstallationGeneration;
        public readonly ulong ActivationEpoch;
        public readonly uint WorkOrdinal;
        public AsyncWorkToken(OperationId operation, Id128 pluginInstanceId,
            ulong installationGeneration, ulong activationEpoch, uint workOrdinal)
        {
            Operation = operation; PluginInstanceId = pluginInstanceId;
            InstallationGeneration = installationGeneration;
            ActivationEpoch = activationEpoch; WorkOrdinal = workOrdinal;
        }
    }

    public readonly struct SchemaRef
    {
        public readonly Id128 Id;
        public readonly uint Version;
        public SchemaRef(Id128 id, uint version) { Id = id; Version = version; }
    }

    public readonly struct DefinitionRef
    {
        public readonly Id128 Id;
        public readonly SchemaRef Schema;
        public readonly ulong Revision;
        public DefinitionRef(Id128 id, SchemaRef schema, ulong revision)
        { Id = id; Schema = schema; Revision = revision; }
    }

    public readonly struct ContributionKey
    {
        public readonly Id128 Provider, Rule, Target, Capability;
        public readonly uint OutputSlot;
        public ContributionKey(Id128 provider, Id128 rule, Id128 target,
            Id128 capability, uint outputSlot)
        { Provider = provider; Rule = rule; Target = target; Capability = capability; OutputSlot = outputSlot; }
    }

    public enum PropagationMode { Automatic, Conservative }
    public enum TemporalModel { FixedStep, CommandDriven }
    public enum CompositionPolicy { Additive, Replace, Ordered, Exclusive, Incompatible }
    public enum LastSupportPolicy { RemoveDerived, PreserveDormant, TransferTo }
    public enum Outcome
    {
        Pending, Accepted, Committed, Published, PublishedWithCleanupErrors, NoChange,
        Rejected, Cancelled, Faulted
    }
    public enum CancelOutcome { Cancelled, TooLate, Unknown, ResultExpired }

    // Immutable payload ownership: clone caller input and expose only a read-only wrapper.
    // Full schema/bounds validation belongs to generated constructors and host admission.
    public sealed class FrozenPayload
    {
        private readonly IReadOnlyList<byte> bytes;
        public IReadOnlyList<byte> Bytes => bytes;
        public FrozenPayload(byte[] source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            bytes = Array.AsReadOnly((byte[])source.Clone());
        }
    }

    public sealed class CompositionProposal
    {
        public OperationId Operation { get; }
        public ulong ExpectedRevision { get; }
        // Hashes are 32-byte SHA-256 values in the complete generated schema.
        public FrozenPayload CatalogHash { get; }
        public SchemaRef EditSchema { get; }
        public FrozenPayload EditPayload { get; }
        public CompositionProposal(OperationId operation, ulong expectedRevision,
            FrozenPayload catalogHash, SchemaRef editSchema, FrozenPayload editPayload)
        {
            Operation = operation; ExpectedRevision = expectedRevision;
            CatalogHash = catalogHash ?? throw new ArgumentNullException(nameof(catalogHash));
            EditSchema = editSchema;
            EditPayload = editPayload ?? throw new ArgumentNullException(nameof(editPayload));
        }
    }

    public sealed class CommandEnvelope
    {
        public OperationId RequestId { get; }
        public Id128 RouteId { get; }
        public Id128 TargetId { get; }
        public SchemaRef Schema { get; }
        public ulong? ExpectedDomainVersion { get; }
        public FrozenPayload Payload { get; }
        public CommandEnvelope(OperationId requestId, Id128 routeId, Id128 targetId,
            SchemaRef schema, ulong? expectedDomainVersion, FrozenPayload payload)
        {
            RequestId = requestId; RouteId = routeId; TargetId = targetId; Schema = schema;
            ExpectedDomainVersion = expectedDomainVersion;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }
    }

    public sealed class OperationResult
    {
        public OperationId Operation { get; }
        public Outcome Outcome { get; }
        public string DiagnosticCode { get; }
        public SnapshotToken? PublishedSnapshot { get; }
        public OperationResult(OperationId operation, Outcome outcome,
            string diagnosticCode, SnapshotToken? publishedSnapshot)
        {
            Operation = operation; Outcome = outcome;
            DiagnosticCode = diagnosticCode ?? throw new ArgumentNullException(nameof(diagnosticCode));
            PublishedSnapshot = publishedSnapshot;
        }
    }

    public interface ICompositionCommands
    {
        OperationId Submit(CompositionProposal proposal);
    }
    public interface ICommandIngress
    {
        OperationId Submit(CommandEnvelope command);
    }
    public interface IOperationReader
    {
        OperationResult Read(OperationId operation);
    }
    public interface IOperationControl
    {
        CancelOutcome Cancel(OperationId cancellationOperation, OperationId target);
    }
    public interface ISnapshotLease : IDisposable
    {
        SnapshotToken Token { get; }
        FrozenPayload State { get; }
    }
    public interface IObservationReader
    {
        ISnapshotLease Acquire(SnapshotToken token);
    }
}
