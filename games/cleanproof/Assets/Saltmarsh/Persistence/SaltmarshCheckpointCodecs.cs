#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution.Persistence;
using GameCore.Unity.Runtime.Persistence;
using Saltmarsh.GeneratedCheckpoint;

namespace Saltmarsh.Persistence
{
    public static class SaltmarshCheckpointCodecs
    {
        public static bool TryBuild(
            out CheckpointSerializerBindings? bindings,
            out CheckpointCodecSet? codecs,
            out string detail)
        {
            bindings = null;
            codecs = null;
            var header = new CheckpointCatalog.HeaderRecordSerializer();
            var scope = new CheckpointCatalog.ScopeRecordSerializer();
            var install = new CheckpointCatalog.InstallRecordSerializer();
            var selection = new CheckpointCatalog.SelectionRecordSerializer();
            var target = new CheckpointCatalog.TargetRecordSerializer();
            var slot = new CheckpointCatalog.SlotRecordSerializer();
            var grant = new CheckpointCatalog.GrantRecordSerializer();
            var clock = new CheckpointCatalog.ClockRecordSerializer();
            var command = new CheckpointCatalog.CommandRecordSerializer();
            var message = new CheckpointCatalog.MessageRecordSerializer();
            var rngSerializer = new CheckpointCatalog.RngRecordSerializer();
            var cursor = new CheckpointCatalog.CursorRecordSerializer();
            var outbox = new CheckpointCatalog.OutboxRecordSerializer();

            bindings = new CheckpointSerializerBindings(
                new CheckpointRecordSerializer<HeaderRecordValue, CheckpointCatalog.HeaderRecordValue>(
                    header.Schema, header.Serialize, header.TryDeserialize,
                    HeaderToGenerated, HeaderFromGenerated),
                new CheckpointRecordSerializer<ScopeRecordValue, CheckpointCatalog.ScopeRecordValue>(
                    scope.Schema, scope.Serialize, scope.TryDeserialize,
                    ScopeToGenerated, ScopeFromGenerated),
                new CheckpointRecordSerializer<InstallRecordValue, CheckpointCatalog.InstallRecordValue>(
                    install.Schema, install.Serialize, install.TryDeserialize,
                    InstallToGenerated, InstallFromGenerated),
                new CheckpointRecordSerializer<SelectionRecordValue, CheckpointCatalog.SelectionRecordValue>(
                    selection.Schema, selection.Serialize, selection.TryDeserialize,
                    SelectionToGenerated, SelectionFromGenerated),
                new CheckpointRecordSerializer<TargetRecordValue, CheckpointCatalog.TargetRecordValue>(
                    target.Schema, target.Serialize, target.TryDeserialize,
                    TargetToGenerated, TargetFromGenerated),
                new CheckpointRecordSerializer<SlotRecordValue, CheckpointCatalog.SlotRecordValue>(
                    slot.Schema, slot.Serialize, slot.TryDeserialize,
                    SlotToGenerated, SlotFromGenerated),
                new CheckpointRecordSerializer<GrantRecordValue, CheckpointCatalog.GrantRecordValue>(
                    grant.Schema, grant.Serialize, grant.TryDeserialize,
                    GrantToGenerated, GrantFromGenerated),
                new CheckpointRecordSerializer<ClockRecordValue, CheckpointCatalog.ClockRecordValue>(
                    clock.Schema, clock.Serialize, clock.TryDeserialize,
                    ClockToGenerated, ClockFromGenerated),
                new CheckpointRecordSerializer<CommandRecordValue, CheckpointCatalog.CommandRecordValue>(
                    command.Schema, command.Serialize, command.TryDeserialize,
                    CommandToGenerated, CommandFromGenerated),
                new CheckpointRecordSerializer<MessageRecordValue, CheckpointCatalog.MessageRecordValue>(
                    message.Schema, message.Serialize, message.TryDeserialize,
                    MessageToGenerated, MessageFromGenerated),
                new CheckpointRecordSerializer<RngRecordValue, CheckpointCatalog.RngRecordValue>(
                    rngSerializer.Schema, rngSerializer.Serialize, rngSerializer.TryDeserialize,
                    RngToGenerated, RngFromGenerated),
                new CheckpointRecordSerializer<CursorRecordValue, CheckpointCatalog.CursorRecordValue>(
                    cursor.Schema, cursor.Serialize, cursor.TryDeserialize,
                    CursorToGenerated, CursorFromGenerated),
                new CheckpointRecordSerializer<OutboxRecordValue, CheckpointCatalog.OutboxRecordValue>(
                    outbox.Schema, outbox.Serialize, outbox.TryDeserialize,
                    OutboxToGenerated, OutboxFromGenerated));

            codecs = bindings.ToCodecSet();
            if (!codecs.IsComplete)
            {
                detail = "the generated checkpoint catalog is incomplete: " + DescribeKinds(codecs.MissingKinds());
                return false;
            }

            detail = "kinds=" + codecs.CompleteKindCount.ToString(CultureInfo.InvariantCulture)
                + " schemas=" + codecs.Schemas.Count.ToString(CultureInfo.InvariantCulture);
            return true;
        }


        private static CheckpointCatalog.HeaderRecordValue HeaderToGenerated(HeaderRecordValue value)
            => new CheckpointCatalog.HeaderRecordValue(
                value.WorldDefinitionHigh, value.WorldDefinitionLow, value.SourceSessionHigh, value.SourceSessionLow,
                value.ProtocolMajor, value.ProtocolMinor, value.TemporalModel, value.StepDurationTicks,
                value.TicksPerSecond, value.MaxStepsPerPump, value.UsesUnscaledHostClock, value.LogicalStep,
                value.TimeDebtTicks, value.DomainSeconds, value.PendingDemand, value.PropagationMode,
                value.CatalogFingerprintA, value.CatalogFingerprintB, value.CatalogFingerprintC, value.CatalogFingerprintD,
                value.QueuePolicy, value.AdmissionCutoff, value.RejectedQueuedCount, value.LastEventSequence,
                value.ScopeCount, value.InstallCount, value.SelectionCount, value.TargetCount,
                value.SlotCount, value.GrantCount, value.ClockCount, value.CommandCount,
                value.MessageCount, value.RngStreamCount, value.CursorCount, value.SourcePublishedRevision,
                value.SourcePublishedEpoch, value.SourceHostTicksPerSecond, value.ContentRevisionCount,
                value.OutboxCount);

        private static HeaderRecordValue HeaderFromGenerated(CheckpointCatalog.HeaderRecordValue value)
            => new HeaderRecordValue(
                value.WorldDefinitionHigh, value.WorldDefinitionLow, value.SourceSessionHigh, value.SourceSessionLow,
                value.ProtocolMajor, value.ProtocolMinor, value.TemporalModel, value.StepDurationTicks,
                value.TicksPerSecond, value.MaxStepsPerPump, value.UsesUnscaledHostClock, value.LogicalStep,
                value.TimeDebtTicks, value.DomainSeconds, value.PendingDemand, value.PropagationMode,
                value.CatalogFingerprintA, value.CatalogFingerprintB, value.CatalogFingerprintC, value.CatalogFingerprintD,
                value.QueuePolicy, value.AdmissionCutoff, value.RejectedQueuedCount, value.LastEventSequence,
                value.ScopeCount, value.InstallCount, value.SelectionCount, value.TargetCount,
                value.SlotCount, value.GrantCount, value.ClockCount, value.CommandCount,
                value.MessageCount, value.RngStreamCount, value.CursorCount, value.SourcePublishedRevision,
                value.SourcePublishedEpoch, value.SourceHostTicksPerSecond, value.ContentRevisionCount,
                value.OutboxCount);

        private static CheckpointCatalog.ScopeRecordValue ScopeToGenerated(ScopeRecordValue value)
            => new CheckpointCatalog.ScopeRecordValue(
                value.ScopeHigh, value.ScopeLow, value.ParentHigh, value.ParentLow,
                value.Depth, value.Mode, value.InstallCount, value.GrantCount,
                value.ServiceIsolationAll, value.CapabilityIsolationAll,
                value.ServiceIsolationCount, value.CapabilityIsolationCount);

        private static ScopeRecordValue ScopeFromGenerated(CheckpointCatalog.ScopeRecordValue value)
            => new ScopeRecordValue(
                value.ScopeHigh, value.ScopeLow, value.ParentHigh, value.ParentLow,
                value.Depth, value.Mode, value.InstallCount, value.GrantCount,
                value.ServiceIsolationAll, value.CapabilityIsolationAll,
                value.ServiceIsolationCount, value.CapabilityIsolationCount);

        private static CheckpointCatalog.InstallRecordValue InstallToGenerated(InstallRecordValue value)
            => new CheckpointCatalog.InstallRecordValue(
                value.InstanceHigh, value.InstanceLow, value.PluginTypeHigh, value.PluginTypeLow,
                value.ScopeHigh, value.ScopeLow, value.ConfigRevision, value.ConfigHashA,
                value.ConfigHashB, value.ConfigHashC, value.ConfigHashD, value.Priority,
                value.Generation, value.ActivationEpoch, value.State, value.ConfigFieldCount,
                value.ConfigBytes, value.SelectionCount, value.HasConfigDocument);

        private static InstallRecordValue InstallFromGenerated(CheckpointCatalog.InstallRecordValue value)
            => new InstallRecordValue(
                value.InstanceHigh, value.InstanceLow, value.PluginTypeHigh, value.PluginTypeLow,
                value.ScopeHigh, value.ScopeLow, value.ConfigRevision, value.ConfigHashA,
                value.ConfigHashB, value.ConfigHashC, value.ConfigHashD, value.Priority,
                value.Generation, value.ActivationEpoch, value.State, value.ConfigFieldCount,
                value.ConfigBytes, value.SelectionCount, value.HasConfigDocument);

        private static CheckpointCatalog.SelectionRecordValue SelectionToGenerated(SelectionRecordValue value)
            => new CheckpointCatalog.SelectionRecordValue(
                value.InstanceHigh, value.InstanceLow, value.ContractHigh, value.ContractLow,
                value.ContractVersion, value.ProviderHigh, value.ProviderLow, value.Order);

        private static SelectionRecordValue SelectionFromGenerated(CheckpointCatalog.SelectionRecordValue value)
            => new SelectionRecordValue(
                value.InstanceHigh, value.InstanceLow, value.ContractHigh, value.ContractLow,
                value.ContractVersion, value.ProviderHigh, value.ProviderLow, value.Order);

        private static CheckpointCatalog.TargetRecordValue TargetToGenerated(TargetRecordValue value)
            => new CheckpointCatalog.TargetRecordValue(
                value.TargetHigh, value.TargetLow, value.ScopeHigh, value.ScopeLow,
                value.DefinitionHigh, value.DefinitionLow, value.SchemaHigh, value.SchemaLow,
                value.SchemaVersion, value.ContentRevision, value.SourceSlot, value.SourceGeneration);

        private static TargetRecordValue TargetFromGenerated(CheckpointCatalog.TargetRecordValue value)
            => new TargetRecordValue(
                value.TargetHigh, value.TargetLow, value.ScopeHigh, value.ScopeLow,
                value.DefinitionHigh, value.DefinitionLow, value.SchemaHigh, value.SchemaLow,
                value.SchemaVersion, value.ContentRevision, value.SourceSlot, value.SourceGeneration);

        private static CheckpointCatalog.SlotRecordValue SlotToGenerated(SlotRecordValue value)
            => new CheckpointCatalog.SlotRecordValue(
                value.TargetHigh, value.TargetLow, value.OwnerHigh, value.OwnerLow,
                value.SlotHigh, value.SlotLow, value.SchemaVersion, value.Value, value.Active);

        private static SlotRecordValue SlotFromGenerated(CheckpointCatalog.SlotRecordValue value)
            => new SlotRecordValue(
                value.TargetHigh, value.TargetLow, value.OwnerHigh, value.OwnerLow,
                value.SlotHigh, value.SlotLow, value.SchemaVersion, value.Value, value.Active);

        private static CheckpointCatalog.GrantRecordValue GrantToGenerated(GrantRecordValue value)
            => new CheckpointCatalog.GrantRecordValue(
                value.Kind, value.ScopeHigh, value.ScopeLow, value.TargetHigh, value.TargetLow,
                value.CapabilityHigh, value.CapabilityLow, value.CapabilityVersion,
                value.ProviderHigh, value.ProviderLow, value.RuleHigh, value.RuleLow,
                value.ContractHigh, value.ContractLow, value.ContractVersion,
                value.SubjectHigh, value.SubjectLow, value.AppliesToSubtree, value.AllContracts,
                value.ExclusionKind, value.Order);

        private static GrantRecordValue GrantFromGenerated(CheckpointCatalog.GrantRecordValue value)
            => new GrantRecordValue(
                value.Kind, value.ScopeHigh, value.ScopeLow, value.TargetHigh, value.TargetLow,
                value.CapabilityHigh, value.CapabilityLow, value.CapabilityVersion,
                value.ProviderHigh, value.ProviderLow, value.RuleHigh, value.RuleLow,
                value.ContractHigh, value.ContractLow, value.ContractVersion,
                value.SubjectHigh, value.SubjectLow, value.AppliesToSubtree, value.AllContracts,
                value.ExclusionKind, value.Order);

        private static CheckpointCatalog.ClockRecordValue ClockToGenerated(ClockRecordValue value)
            => new CheckpointCatalog.ClockRecordValue(
                value.RowKind, value.ClockHigh, value.ClockLow, value.ClockKind,
                value.PausePolicy, value.Persists, value.WakeHigh, value.WakeLow,
                value.PayloadSchemaHigh, value.PayloadSchemaLow, value.PayloadSchemaVersion,
                value.ScheduledAtSequence, value.RemainingSteps, value.RemainingTicks,
                value.WakeState, value.Order);

        private static ClockRecordValue ClockFromGenerated(CheckpointCatalog.ClockRecordValue value)
            => new ClockRecordValue(
                value.RowKind, value.ClockHigh, value.ClockLow, value.ClockKind,
                value.PausePolicy, value.Persists, value.WakeHigh, value.WakeLow,
                value.PayloadSchemaHigh, value.PayloadSchemaLow, value.PayloadSchemaVersion,
                value.ScheduledAtSequence, value.RemainingSteps, value.RemainingTicks,
                value.WakeState, value.Order);

        private static CheckpointCatalog.CommandRecordValue CommandToGenerated(CommandRecordValue value)
            => new CheckpointCatalog.CommandRecordValue(
                value.IssuerHigh, value.IssuerLow, value.IssuerSequence, value.RouteHigh, value.RouteLow,
                value.TargetHigh, value.TargetLow, value.SchemaHigh, value.SchemaLow,
                value.SchemaVersion, value.AdmittedStep, value.AdmittedEpoch,
                value.AdmissionSequence, value.OrderOrdinal, value.OriginKind,
                value.InputHashA, value.InputHashB, value.InputHashC, value.InputHashD,
                value.Payload);

        private static CommandRecordValue CommandFromGenerated(CheckpointCatalog.CommandRecordValue value)
            => new CommandRecordValue(
                value.IssuerHigh, value.IssuerLow, value.IssuerSequence, value.RouteHigh, value.RouteLow,
                value.TargetHigh, value.TargetLow, value.SchemaHigh, value.SchemaLow,
                value.SchemaVersion, value.AdmittedStep, value.AdmittedEpoch,
                value.AdmissionSequence, value.OrderOrdinal, value.OriginKind,
                value.InputHashA, value.InputHashB, value.InputHashC, value.InputHashD,
                value.Payload);

        private static CheckpointCatalog.MessageRecordValue MessageToGenerated(MessageRecordValue value)
            => new CheckpointCatalog.MessageRecordValue(
                value.Step, value.Epoch, value.RequestIssuerHigh, value.RequestIssuerLow,
                value.RequestSequence, value.RouteHigh, value.RouteLow, value.OwnerHigh,
                value.OwnerLow, value.TargetHigh, value.TargetLow, value.PayloadSchemaHigh,
                value.PayloadSchemaLow, value.PayloadSchemaVersion, value.MessageKind,
                value.OrderAdmitted, value.OrderOrdinal, value.OriginKeyHigh, value.OriginKeyLow,
                value.ProducerKeyHigh, value.ProducerKeyLow, value.ProducerKeyVersion,
                value.BufferHigh, value.BufferLow, value.HasPayload, value.HasRequest,
                value.IsOutcome, value.Payload);

        private static MessageRecordValue MessageFromGenerated(CheckpointCatalog.MessageRecordValue value)
            => new MessageRecordValue(
                value.Step, value.Epoch, value.RequestIssuerHigh, value.RequestIssuerLow,
                value.RequestSequence, value.RouteHigh, value.RouteLow, value.OwnerHigh,
                value.OwnerLow, value.TargetHigh, value.TargetLow, value.PayloadSchemaHigh,
                value.PayloadSchemaLow, value.PayloadSchemaVersion, value.MessageKind,
                value.OrderAdmitted, value.OrderOrdinal, value.OriginKeyHigh, value.OriginKeyLow,
                value.ProducerKeyHigh, value.ProducerKeyLow, value.ProducerKeyVersion,
                value.BufferHigh, value.BufferLow, value.HasPayload, value.HasRequest,
                value.IsOutcome, value.Payload);

        private static CheckpointCatalog.RngRecordValue RngToGenerated(RngRecordValue value)
            => new CheckpointCatalog.RngRecordValue(
                value.StreamHigh, value.StreamLow, value.State, value.StreamKey, value.DrawCount);

        private static RngRecordValue RngFromGenerated(CheckpointCatalog.RngRecordValue value)
            => new RngRecordValue(
                value.StreamHigh, value.StreamLow, value.State, value.StreamKey, value.DrawCount);

        private static CheckpointCatalog.CursorRecordValue CursorToGenerated(CursorRecordValue value)
            => new CheckpointCatalog.CursorRecordValue(
                value.RowKind, value.IssuerHigh, value.IssuerLow, value.Sequence,
                value.SessionHigh, value.SessionLow);

        private static CursorRecordValue CursorFromGenerated(CheckpointCatalog.CursorRecordValue value)
            => new CursorRecordValue(
                value.RowKind, value.IssuerHigh, value.IssuerLow, value.Sequence,
                value.SessionHigh, value.SessionLow);

        private static CheckpointCatalog.OutboxRecordValue OutboxToGenerated(OutboxRecordValue value)
            => new CheckpointCatalog.OutboxRecordValue(
                value.RowKind, value.RecordVersion, value.OutboxHigh, value.OutboxLow,
                value.DestinationHigh, value.DestinationLow, value.IdempotencyHigh, value.IdempotencyLow,
                value.SourceEventSequence, value.SourceStep, value.SourceEpoch,
                value.CausalIssuerHigh, value.CausalIssuerLow, value.CausalIssuerSequence,
                value.PayloadSchemaHigh, value.PayloadSchemaLow, value.PayloadSchemaVersion,
                value.DeliveryState, value.ReasonCode, value.AttemptCount, value.Durability,
                value.OrderOrdinal, value.CursorHigh, value.CursorLow, value.CursorCount,
                value.PrunedCount, value.Payload);

        private static OutboxRecordValue OutboxFromGenerated(CheckpointCatalog.OutboxRecordValue value)
            => new OutboxRecordValue(
                value.RowKind, value.RecordVersion, value.OutboxHigh, value.OutboxLow,
                value.DestinationHigh, value.DestinationLow, value.IdempotencyHigh, value.IdempotencyLow,
                value.SourceEventSequence, value.SourceStep, value.SourceEpoch,
                value.CausalIssuerHigh, value.CausalIssuerLow, value.CausalIssuerSequence,
                value.PayloadSchemaHigh, value.PayloadSchemaLow, value.PayloadSchemaVersion,
                value.DeliveryState, value.ReasonCode, value.AttemptCount, value.Durability,
                value.OrderOrdinal, value.CursorHigh, value.CursorLow, value.CursorCount,
                value.PrunedCount, value.Payload);


        private static string DescribeKinds(IReadOnlyList<CheckpointRecordKind> kinds)
        {
            if (kinds.Count == 0)
            {
                return "no record kind";
            }

            var names = new List<string>(kinds.Count);
            for (int i = 0; i < kinds.Count; i++)
            {
                names.Add(kinds[i].ToString());
            }

            return string.Join(",", names.ToArray());
        }
    }
}
