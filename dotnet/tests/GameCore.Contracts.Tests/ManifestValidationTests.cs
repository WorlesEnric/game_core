// Manifest validation tests (GC-003). These cover the catalog rejection rules that P-009 requires before
// activation: duplicate ids, unknown schema/contract versions, missing precompiled factories, unsupported
// required protocol features, undeclared access and missing policies. Every rejection must carry a stable
// 00 s9 code and name the declaration that caused it, and a rejected batch must expose no catalog.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using NUnit.Framework;

namespace GameCore.Contracts.Tests
{
    [TestFixture]
    public sealed class ManifestValidationTests
    {
        private static readonly Id128 PackageId = new Id128(0x1000000000000001UL, 0x0000000000000001UL);
        private static readonly Id128 FeatureId = new Id128(0x1000000000000002UL, 0x0000000000000002UL);
        private static readonly Id128 UnknownFeatureId = new Id128(0x1000000000000003UL, 0x0000000000000003UL);

        private static readonly Id128 ManifestKeyId = new Id128(0x2000000000000001UL, 0x0000000000000001UL);
        private static readonly Id128 ConfigSerializerKeyId = new Id128(0x2000000000000002UL, 0x0000000000000002UL);
        private static readonly Id128 ComponentSerializerKeyId = new Id128(0x2000000000000003UL, 0x0000000000000003UL);
        private static readonly Id128 ReducerKeyId = new Id128(0x2000000000000004UL, 0x0000000000000004UL);
        private static readonly Id128 StageFactoryKeyId = new Id128(0x2000000000000005UL, 0x0000000000000005UL);
        private static readonly Id128 SystemKeyId = new Id128(0x2000000000000006UL, 0x0000000000000006UL);
        private static readonly Id128 LayoutKeyId = new Id128(0x2000000000000007UL, 0x0000000000000007UL);
        private static readonly Id128 StatePolicyKeyId = new Id128(0x2000000000000008UL, 0x0000000000000008UL);
        private static readonly Id128 OrderKeyId = new Id128(0x2000000000000009UL, 0x0000000000000009UL);
        private static readonly Id128 SecondSystemKeyId = new Id128(0x200000000000000AUL, 0x000000000000000AUL);
        private static readonly Id128 UnknownKeyId = new Id128(0x20000000000000FFUL, 0x00000000000000FFUL);

        private static readonly Id128 ConfigSchemaId = new Id128(0x3000000000000001UL, 0x0000000000000001UL);
        private static readonly Id128 ComponentSchemaId = new Id128(0x3000000000000002UL, 0x0000000000000002UL);
        private static readonly Id128 UnknownSchemaId = new Id128(0x30000000000000FFUL, 0x00000000000000FFUL);

        private static readonly SchemaRef ConfigSchema = new SchemaRef(new SchemaId(ConfigSchemaId), 1U);
        private static readonly SchemaRef ComponentSchema = new SchemaRef(new SchemaId(ComponentSchemaId), 1U);

        private static readonly Id128 CapabilityA = new Id128(0x4000000000000001UL, 0x0000000000000001UL);
        private static readonly Id128 CapabilityB = new Id128(0x4000000000000002UL, 0x0000000000000002UL);
        private static readonly Id128 RuleId = new Id128(0x5000000000000001UL, 0x0000000000000001UL);
        private static readonly Id128 StageA = new Id128(0x6000000000000001UL, 0x0000000000000001UL);
        private static readonly Id128 StageB = new Id128(0x6000000000000002UL, 0x0000000000000002UL);
        private static readonly Id128 BufferOne = new Id128(0x7000000000000001UL, 0x0000000000000001UL);
        private static readonly Id128 SlotOne = new Id128(0x8000000000000001UL, 0x0000000000000001UL);
        private static readonly Id128 OwnerOne = new Id128(0x9000000000000001UL, 0x0000000000000001UL);
        private static readonly Id128 PluginTypeA = new Id128(0xA000000000000001UL, 0x0000000000000001UL);

        private static readonly ProtocolVersion Protocol = new ProtocolVersion(1, 0);

        private static ICatalog Catalog()
        {
            var factories = new List<FactoryRegistration>
            {
                new FactoryRegistration(new FactoryKey(ManifestKeyId, 1U), FactoryKind.PluginFactory, PackageId, ManifestKeyId, 1U),
                new FactoryRegistration(new FactoryKey(ConfigSerializerKeyId, 1U), FactoryKind.Serializer, PackageId, ConfigSchemaId, 1U),
                new FactoryRegistration(new FactoryKey(ComponentSerializerKeyId, 1U), FactoryKind.Serializer, PackageId, ComponentSchemaId, 1U),
                new FactoryRegistration(new FactoryKey(ReducerKeyId, 1U), FactoryKind.Reducer, PackageId, ReducerKeyId, 1U),
                new FactoryRegistration(new FactoryKey(StageFactoryKeyId, 1U), FactoryKind.SystemFactory, PackageId, StageFactoryKeyId, 1U),
                new FactoryRegistration(new FactoryKey(SystemKeyId, 1U), FactoryKind.SystemFactory, PackageId, SystemKeyId, 1U),
                new FactoryRegistration(new FactoryKey(LayoutKeyId, 1U), FactoryKind.LayoutApply, PackageId, LayoutKeyId, 1U),
                new FactoryRegistration(new FactoryKey(StatePolicyKeyId, 1U), FactoryKind.StatePolicy, PackageId, StatePolicyKeyId, 1U),
                new FactoryRegistration(new FactoryKey(OrderKeyId, 1U), FactoryKind.SystemFactory, PackageId, OrderKeyId, 1U),
                new FactoryRegistration(new FactoryKey(SecondSystemKeyId, 1U), FactoryKind.SystemFactory, PackageId, SecondSystemKeyId, 1U),
            };

            var schemas = new List<SchemaRegistration>
            {
                new SchemaRegistration(ConfigSchema, PackageId, new FactoryKey(ConfigSerializerKeyId, 1U), true),
                new SchemaRegistration(ComponentSchema, PackageId, new FactoryKey(ComponentSerializerKeyId, 1U), true),
            };

            var serializers = new List<ISchemaSerializer>
            {
                new TestSerializer(new FactoryKey(ConfigSerializerKeyId, 1U), ConfigSchema, null, TestSerializer.DefaultFields),
                new TestSerializer(new FactoryKey(ComponentSerializerKeyId, 1U), ComponentSchema, null, TestSerializer.DefaultFields),
            };

            CatalogBuildResult result = ImmutableCatalog.Build(factories, schemas, new[] { FeatureId }, serializers);
            Assert.That(result.Succeeded, Is.True, "the test catalog must build: " + result.Describe());
            return result.Catalog!;
        }

        private static PluginManifest ValidManifest()
        {
            var access = new AccessSet(new[]
            {
                new AccessDeclaration(ComponentSchema, AccessMode.ReadWrite, default(Id128)),
            });

            var capability = new CapabilityContract(
                new CapabilityRef(new CapabilityId(CapabilityA), 1U),
                1,
                new[] { new OutputSlotSchema(new SlotId(SlotOne), ComponentSchema) },
                new[] { new SlotCompositionPolicy(new SlotId(SlotOne), CompositionPolicy.Replace, new FactoryKey(ReducerKeyId, 1U)) },
                null);

            var rule = new DerivationRule(
                new RuleId(RuleId),
                new CapabilityRef(new CapabilityId(CapabilityA), 1U),
                1,
                1U,
                new[] { ComponentSchema },
                default(FactoryKey),
                null,
                PropagationReach.DescendantsOnly,
                false,
                0,
                CompositionPolicy.Replace,
                new FrozenPayload(new byte[] { 1, 2, 3 }));

            var slot = new StateSlotSpec(
                new SlotId(SlotOne),
                new OwnerId(OwnerOne),
                ComponentSchema,
                new FactoryKey(LayoutKeyId, 1U),
                null,
                new FactoryKey(StatePolicyKeyId, 1U),
                new FactoryKey(StatePolicyKeyId, 1U),
                new FactoryKey(StatePolicyKeyId, 1U),
                LastSupportPolicy.PreserveDormant,
                default(FactoryKey),
                null);

            var system = new SystemSpec(
                new FactoryKey(SystemKeyId, 1U),
                SystemMultiplicity.World,
                access,
                null,
                null,
                null,
                null);

            var stageA = new StageSpec(
                new StageId(StageA), 1U, PackageId, HostAffinity.ManagedMain,
                new[] { new FactoryKey(StageFactoryKeyId, 1U) },
                null,
                access,
                null, null, null, null,
                new[] { system },
                null);

            var stageB = new StageSpec(
                new StageId(StageB), 1U, PackageId, HostAffinity.ManagedMain,
                new[] { new FactoryKey(StageFactoryKeyId, 1U) },
                null,
                new AccessSet(null),
                null, null, null, null,
                null,
                null);

            var buffer = new BufferSpec(
                new BufferId(BufferOne),
                ComponentSchema,
                new[] { new FactoryKey(SystemKeyId, 1U) },
                new StageId(StageA),
                new StageId(StageA),
                new FactoryKey(OrderKeyId, 1U),
                BufferLifetime.Step,
                64,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);

            return new PluginManifest(
                new PluginTypeId(PluginTypeA),
                "1.0.0",
                ContentHash.Compute(new byte[] { 9 }),
                new SupportedProtocolRange(1, 0, 0),
                new[] { FeatureId },
                ConfigSchema,
                new FactoryKey(ManifestKeyId, 1U),
                null,
                null,
                new[] { capability },
                new[] { rule },
                null,
                new[] { slot },
                new[] { stageA, stageB },
                new[] { buffer },
                null);
        }

        private static ManifestValidationReport Validate(PluginManifest manifest) =>
            ManifestValidator.Validate(new[] { manifest }, Catalog(), new[] { FeatureId }, Protocol);

        private static void AssertRejected(ManifestValidationReport report, DiagnosticCode code, string summaryFragment)
        {
            Assert.That(report.IsValid, Is.False, "the manifest must be rejected");
            Assert.That(report.ValidatedPluginTypes, Is.Empty, "a rejected batch accepts no plugin type");

            foreach (Diagnostic diagnostic in report.Diagnostics)
            {
                Assert.That(diagnostic.Phase, Is.EqualTo(OperationPhase.Validation));
                if (diagnostic.Code == code && diagnostic.Summary.Contains(summaryFragment))
                {
                    return;
                }
            }

            Assert.Fail(
                "expected " + code + " mentioning '" + summaryFragment + "', but got:\n" + report.Describe());
        }

        [Test]
        public void ValidManifestIsAccepted()
        {
            ManifestValidationReport report = Validate(ValidManifest());
            Assert.That(report.IsValid, Is.True, report.Describe());
            Assert.That(report.ValidatedPluginTypes.Count, Is.EqualTo(1));
            Assert.That(report.Describe(), Does.Contain("accepted 1 manifest"));
        }

        [Test]
        public void DuplicatePluginTypeIdAcrossTheBatchRejects()
        {
            PluginManifest manifest = ValidManifest();
            ManifestValidationReport report = ManifestValidator.Validate(
                new[] { manifest, ValidManifest() },
                Catalog(),
                new[] { FeatureId },
                Protocol);

            AssertRejected(report, DiagnosticCode.OwnershipConflict, "duplicate plugin type id");
        }

        [Test]
        public void DuplicateCapabilityContractRejectsAsCapabilityConflict()
        {
            PluginManifest manifest = ValidManifest();
            var capability = new CapabilityContract(
                new CapabilityRef(new CapabilityId(CapabilityA), 1U),
                1,
                null,
                null,
                null);

            ManifestValidationReport report = Validate(WithCapabilities(manifest, new[] { manifest.CapabilityContracts[0], capability }));
            AssertRejected(report, DiagnosticCode.CapabilityConflict, "duplicate capability contract id");
        }

        [Test]
        public void CapabilitySlotWithoutCompositionPolicyRejects()
        {
            PluginManifest manifest = ValidManifest();
            var capability = new CapabilityContract(
                new CapabilityRef(new CapabilityId(CapabilityB), 1U),
                1,
                new[] { new OutputSlotSchema(new SlotId(SlotOne), ComponentSchema) },
                null,
                null);

            ManifestValidationReport report = Validate(WithCapabilities(manifest, new[] { capability }));
            AssertRejected(report, DiagnosticCode.CapabilityConflict, "without a composition policy");
        }

        [Test]
        public void CapabilityStratumOutsideTheDeclaredRangeRejects()
        {
            PluginManifest manifest = ValidManifest();
            var capability = new CapabilityContract(
                new CapabilityRef(new CapabilityId(CapabilityB), 1U),
                64,
                null,
                null,
                null);

            ManifestValidationReport report = Validate(WithCapabilities(manifest, new[] { capability }));
            AssertRejected(report, DiagnosticCode.Ineligible, "capability strata are 0..31");
        }

        [Test]
        public void RuleReadingItsOwnStratumRejects()
        {
            PluginManifest manifest = ValidManifest();
            var capability = new CapabilityContract(
                new CapabilityRef(new CapabilityId(CapabilityB), 1U),
                1,
                new[] { new OutputSlotSchema(new SlotId(SlotOne), ComponentSchema) },
                new[] { new SlotCompositionPolicy(new SlotId(SlotOne), CompositionPolicy.Replace, new FactoryKey(ReducerKeyId, 1U)) },
                null);

            // The rule outputs stratum 1 and reads a stratum-1 capability: only strictly lower strata are legal.
            var rule = new DerivationRule(
                new RuleId(RuleId),
                new CapabilityRef(new CapabilityId(CapabilityA), 1U),
                1,
                1U,
                new[] { ComponentSchema },
                default(FactoryKey),
                new[] { new CapabilityRef(new CapabilityId(CapabilityB), 1U) },
                PropagationReach.DescendantsOnly,
                false,
                0,
                CompositionPolicy.Replace,
                new FrozenPayload(new byte[] { 4 }));

            ManifestValidationReport report = ManifestValidator.Validate(
                new[] { WithRules(WithCapabilities(manifest, new[] { capability }), new[] { rule }) },
                Catalog(),
                new[] { FeatureId },
                Protocol);

            AssertRejected(report, DiagnosticCode.Ineligible, "reads stratum 1");
        }

        [Test]
        public void RuleOutputtingAnUndeclaredCapabilityRejects()
        {
            PluginManifest manifest = ValidManifest();
            var rule = new DerivationRule(
                new RuleId(RuleId),
                new CapabilityRef(new CapabilityId(CapabilityB), 1U),
                1,
                1U,
                null,
                default(FactoryKey),
                null,
                PropagationReach.DescendantsOnly,
                false,
                0,
                CompositionPolicy.Replace,
                new FrozenPayload(new byte[] { 5 }));

            ManifestValidationReport report = Validate(WithRules(manifest, new[] { rule }));
            AssertRejected(report, DiagnosticCode.MissingDependency, "no manifest in this catalog declares that capability contract");
        }

        [Test]
        public void UnsupportedProtocolRangeRejects()
        {
            PluginManifest manifest = ValidManifest();
            ManifestValidationReport report = ManifestValidator.Validate(
                new[] { WithProtocolRange(manifest, new SupportedProtocolRange(2, 0, 5)) },
                Catalog(),
                new[] { FeatureId },
                Protocol);

            AssertRejected(report, DiagnosticCode.UnsupportedVersion, "supports protocol 2.0-5");
        }

        [Test]
        public void UnknownRequiredFeatureRejects()
        {
            PluginManifest manifest = ValidManifest();
            ManifestValidationReport report = ManifestValidator.Validate(
                new[] { WithFeatures(manifest, new[] { FeatureId, UnknownFeatureId }) },
                Catalog(),
                new[] { FeatureId },
                Protocol);

            AssertRejected(report, DiagnosticCode.UnsupportedVersion, "requires unsupported protocol feature");
        }

        [Test]
        public void UnknownConfigSchemaRejects()
        {
            PluginManifest manifest = ValidManifest();
            ManifestValidationReport report = ValidateWithSchema(manifest, new SchemaRef(new SchemaId(UnknownSchemaId), 1U));
            AssertRejected(report, DiagnosticCode.MissingDependency, "config schema");
        }

        [Test]
        public void UnacceptedSchemaVersionRejectsAsUnsupportedVersion()
        {
            PluginManifest manifest = ValidManifest();
            ManifestValidationReport report = ValidateWithSchema(manifest, new SchemaRef(new SchemaId(ConfigSchemaId), 4U));
            AssertRejected(report, DiagnosticCode.UnsupportedVersion, "config schema");
        }

        [Test]
        public void MissingPrecompiledFactoryKeyRejects()
        {
            PluginManifest manifest = ValidManifest();
            ManifestValidationReport report = ManifestValidator.Validate(
                new[] { WithFactoryKey(manifest, new FactoryKey(UnknownKeyId, 1U)) },
                Catalog(),
                new[] { FeatureId },
                Protocol);

            AssertRejected(report, DiagnosticCode.MissingDependency, "precompiled factory");
        }

        [Test]
        public void SystemWithoutAnAccessSetRejects()
        {
            PluginManifest manifest = ValidManifest();
            var system = new SystemSpec(
                new FactoryKey(SystemKeyId, 1U),
                SystemMultiplicity.World,
                new AccessSet(null),
                null,
                null,
                null,
                null);

            ManifestValidationReport report = Validate(WithStages(manifest, new[] { WithSystems(manifest.Stages[0], new[] { system }) }));
            AssertRejected(report, DiagnosticCode.OwnershipConflict, "declares no access set");
        }

        [Test]
        public void StateSlotWithoutAnOwnerRejects()
        {
            PluginManifest manifest = ValidManifest();
            var slot = new StateSlotSpec(
                new SlotId(SlotOne),
                default(OwnerId),
                ComponentSchema,
                new FactoryKey(LayoutKeyId, 1U),
                null,
                new FactoryKey(StatePolicyKeyId, 1U),
                new FactoryKey(StatePolicyKeyId, 1U),
                new FactoryKey(StatePolicyKeyId, 1U),
                LastSupportPolicy.PreserveDormant,
                default(FactoryKey),
                null);

            ManifestValidationReport report = ManifestValidator.Validate(
                new[] { WithStateSlots(manifest, new[] { slot }) },
                Catalog(),
                new[] { FeatureId },
                Protocol);

            AssertRejected(report, DiagnosticCode.MissingDependency, "default zero owner");
        }

        [Test]
        public void TransferToWithoutATransferPolicyRejects()
        {
            PluginManifest manifest = ValidManifest();
            var slot = new StateSlotSpec(
                new SlotId(SlotOne),
                new OwnerId(OwnerOne),
                ComponentSchema,
                new FactoryKey(LayoutKeyId, 1U),
                null,
                new FactoryKey(StatePolicyKeyId, 1U),
                new FactoryKey(StatePolicyKeyId, 1U),
                new FactoryKey(StatePolicyKeyId, 1U),
                LastSupportPolicy.TransferTo,
                default(FactoryKey),
                null);

            ManifestValidationReport report = ManifestValidator.Validate(
                new[] { WithStateSlots(manifest, new[] { slot }) },
                Catalog(),
                new[] { FeatureId },
                Protocol);

            AssertRejected(report, DiagnosticCode.MissingDependency, "without a registered owner-transfer policy");
        }

        [Test]
        public void BufferWithoutAPositiveCapacityRejects()
        {
            PluginManifest manifest = ValidManifest();
            var buffer = new BufferSpec(
                new BufferId(BufferOne),
                ComponentSchema,
                null,
                new StageId(StageA),
                new StageId(StageA),
                new FactoryKey(OrderKeyId, 1U),
                BufferLifetime.Step,
                0,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);

            ManifestValidationReport report = ManifestValidator.Validate(
                new[] { WithBuffers(manifest, new[] { buffer }) },
                Catalog(),
                new[] { FeatureId },
                Protocol);

            AssertRejected(report, DiagnosticCode.BudgetExceeded, "bounded buffer requires a positive bound");
        }

        [Test]
        public void BufferNamingAnUnknownStageRejects()
        {
            PluginManifest manifest = ValidManifest();
            var buffer = new BufferSpec(
                new BufferId(BufferOne),
                ComponentSchema,
                null,
                new StageId(StageA),
                new StageId(new Id128(0x60000000000000FFUL, 0x00000000000000FFUL)),
                new FactoryKey(OrderKeyId, 1U),
                BufferLifetime.Step,
                4,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);

            ManifestValidationReport report = ManifestValidator.Validate(
                new[] { WithBuffers(manifest, new[] { buffer }) },
                Catalog(),
                new[] { FeatureId },
                Protocol);

            AssertRejected(report, DiagnosticCode.MissingDependency, "consumer stage");
        }

        [Test]
        public void StageCycleRejectsAsCycle()
        {
            PluginManifest manifest = ValidManifest();
            StageSpec stageA = WithStageEdges(manifest.Stages[0], new[] { new StageId(StageB) }, null);
            StageSpec stageB = WithStageEdges(manifest.Stages[1], new[] { new StageId(StageA) }, null);

            ManifestValidationReport report = Validate(WithStages(manifest, new[] { stageA, stageB }));
            AssertRejected(report, DiagnosticCode.Cycle, "cycle");
        }

        [Test]
        public void OverlappingWriteAccessWithoutAnEdgeRejects()
        {
            PluginManifest manifest = ValidManifest();
            var access = new AccessSet(new[]
            {
                new AccessDeclaration(ComponentSchema, AccessMode.Write, default(Id128)),
            });

            var first = new SystemSpec(
                new FactoryKey(SystemKeyId, 1U),
                SystemMultiplicity.World,
                access,
                null,
                null,
                null,
                null);
            var second = new SystemSpec(
                new FactoryKey(SecondSystemKeyId, 1U),
                SystemMultiplicity.World,
                access,
                null,
                null,
                null,
                null);

            ManifestValidationReport report = ManifestValidator.Validate(
                new[] { WithStages(manifest, new[] { WithSystems(manifest.Stages[0], new[] { first, second }) }) },
                Catalog(),
                new[] { FeatureId },
                Protocol);

            AssertRejected(report, DiagnosticCode.AmbiguousOrder, "overlapping write access");
        }

        [Test]
        public void PartitionedWritersWithDistinctPartitionsAreAccepted()
        {
            PluginManifest manifest = ValidManifest();
            var partitionOne = new AccessSet(new[]
            {
                new AccessDeclaration(ComponentSchema, AccessMode.Write, new Id128(1UL, 1UL)),
            });
            var partitionTwo = new AccessSet(new[]
            {
                new AccessDeclaration(ComponentSchema, AccessMode.Write, new Id128(2UL, 2UL)),
            });

            var first = new SystemSpec(new FactoryKey(SystemKeyId, 1U), SystemMultiplicity.World, partitionOne, null, null, null, null);
            var second = new SystemSpec(
                new FactoryKey(SecondSystemKeyId, 1U),
                SystemMultiplicity.World,
                partitionTwo,
                null,
                null,
                null,
                null);
            ManifestValidationReport report = ManifestValidator.Validate(
                new[] { WithStages(manifest, new[] { WithSystems(manifest.Stages[0], new[] { first, second }) }) },
                Catalog(),
                new[] { FeatureId },
                Protocol);

            Assert.That(report.IsValid, Is.True, report.Describe());
        }

        private static ManifestValidationReport ValidateWithSchema(PluginManifest manifest, SchemaRef configSchema)
        {
            return ManifestValidator.Validate(
                new[] { WithConfigSchema(manifest, configSchema) },
                Catalog(),
                new[] { FeatureId },
                Protocol);
        }

        private static PluginManifest WithCapabilities(PluginManifest source, IReadOnlyList<CapabilityContract> capabilities) =>
            new PluginManifest(
                source.PluginTypeId, source.PackageVersion, source.PackageContentHash, source.ProtocolRange,
                source.RequiredFeatureIds, source.ConfigSchema, source.FactoryKey, source.ServiceExports,
                source.ServiceDependencies, capabilities, source.DerivationRules, source.TargetDescriptors,
                source.StateSlots, source.Stages, source.Buffers, source.Resources);

        private static PluginManifest WithRules(PluginManifest source, IReadOnlyList<DerivationRule> rules) =>
            new PluginManifest(
                source.PluginTypeId, source.PackageVersion, source.PackageContentHash, source.ProtocolRange,
                source.RequiredFeatureIds, source.ConfigSchema, source.FactoryKey, source.ServiceExports,
                source.ServiceDependencies, source.CapabilityContracts, rules, source.TargetDescriptors,
                source.StateSlots, source.Stages, source.Buffers, source.Resources);

        private static PluginManifest WithStateSlots(PluginManifest source, IReadOnlyList<StateSlotSpec> slots) =>
            new PluginManifest(
                source.PluginTypeId, source.PackageVersion, source.PackageContentHash, source.ProtocolRange,
                source.RequiredFeatureIds, source.ConfigSchema, source.FactoryKey, source.ServiceExports,
                source.ServiceDependencies, source.CapabilityContracts, source.DerivationRules, source.TargetDescriptors,
                slots, source.Stages, source.Buffers, source.Resources);

        private static PluginManifest WithStages(PluginManifest source, IReadOnlyList<StageSpec> stages) =>
            new PluginManifest(
                source.PluginTypeId, source.PackageVersion, source.PackageContentHash, source.ProtocolRange,
                source.RequiredFeatureIds, source.ConfigSchema, source.FactoryKey, source.ServiceExports,
                source.ServiceDependencies, source.CapabilityContracts, source.DerivationRules, source.TargetDescriptors,
                source.StateSlots, stages, source.Buffers, source.Resources);

        private static PluginManifest WithBuffers(PluginManifest source, IReadOnlyList<BufferSpec> buffers) =>
            new PluginManifest(
                source.PluginTypeId, source.PackageVersion, source.PackageContentHash, source.ProtocolRange,
                source.RequiredFeatureIds, source.ConfigSchema, source.FactoryKey, source.ServiceExports,
                source.ServiceDependencies, source.CapabilityContracts, source.DerivationRules, source.TargetDescriptors,
                source.StateSlots, source.Stages, buffers, source.Resources);

        private static PluginManifest WithProtocolRange(PluginManifest source, SupportedProtocolRange range) =>
            new PluginManifest(
                source.PluginTypeId, source.PackageVersion, source.PackageContentHash, range,
                source.RequiredFeatureIds, source.ConfigSchema, source.FactoryKey, source.ServiceExports,
                source.ServiceDependencies, source.CapabilityContracts, source.DerivationRules, source.TargetDescriptors,
                source.StateSlots, source.Stages, source.Buffers, source.Resources);

        private static PluginManifest WithFeatures(PluginManifest source, IReadOnlyList<Id128> features) =>
            new PluginManifest(
                source.PluginTypeId, source.PackageVersion, source.PackageContentHash, source.ProtocolRange,
                features, source.ConfigSchema, source.FactoryKey, source.ServiceExports,
                source.ServiceDependencies, source.CapabilityContracts, source.DerivationRules, source.TargetDescriptors,
                source.StateSlots, source.Stages, source.Buffers, source.Resources);

        private static PluginManifest WithConfigSchema(PluginManifest source, SchemaRef configSchema) =>
            new PluginManifest(
                source.PluginTypeId, source.PackageVersion, source.PackageContentHash, source.ProtocolRange,
                source.RequiredFeatureIds, configSchema, source.FactoryKey, source.ServiceExports,
                source.ServiceDependencies, source.CapabilityContracts, source.DerivationRules, source.TargetDescriptors,
                source.StateSlots, source.Stages, source.Buffers, source.Resources);

        private static PluginManifest WithFactoryKey(PluginManifest source, FactoryKey factoryKey) =>
            new PluginManifest(
                source.PluginTypeId, source.PackageVersion, source.PackageContentHash, source.ProtocolRange,
                source.RequiredFeatureIds, source.ConfigSchema, factoryKey, source.ServiceExports,
                source.ServiceDependencies, source.CapabilityContracts, source.DerivationRules, source.TargetDescriptors,
                source.StateSlots, source.Stages, source.Buffers, source.Resources);

        private static StageSpec WithSystems(StageSpec source, IReadOnlyList<SystemSpec> systems) =>
            new StageSpec(
                source.StageId, source.StageVersion, source.OwnerPackageId, source.Affinity, source.FactoryKeys,
                source.ActivationMemberships, source.ReadWriteSet, source.RequiredBefore, source.RequiredAfter,
                source.OptionalBefore, source.OptionalAfter, systems, source.BufferPorts);

        private static StageSpec WithStageEdges(StageSpec source, IReadOnlyList<StageId> requiredBefore, IReadOnlyList<StageId> requiredAfter) =>
            new StageSpec(
                source.StageId, source.StageVersion, source.OwnerPackageId, source.Affinity, source.FactoryKeys,
                source.ActivationMemberships, source.ReadWriteSet, requiredBefore, requiredAfter,
                source.OptionalBefore, source.OptionalAfter, source.Systems, source.BufferPorts);
    }
}
