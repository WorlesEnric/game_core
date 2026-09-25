// GameCore.Unity.Fixtures — W2 integration gate: the generated-style catalog input of the gate's two providers.
//
// The gate's world is a real owned `Unity.Entities.World` whose step table is the one GC-009's compiler produced and
// GC-005's guarded dispatcher executes, whose command lane is a real GC-007 message plane, and whose targets are
// real ECS storage published by GC-008's assembly publisher. Nothing here is a second implementation of a module:
// the declarations below are generated-style catalog input (04 section 8: registration is data, never reflection),
// and every validator, compiler, planner and publisher they feed is the production one.
//
// The declaration set is deliberately small but covers every Wave 2 surface the gate integrates:
//
//   * four declared stages, one of which carries two per-partition writers of one domain, so GC-007 proves
//     partition-based disjointness and GC-009 compiles the stage DAG out of it (P-034, P-040);
//   * one declared buffer produced by the settle stage and consumed by the later project stage, so the compiled
//     schedule carries a buffer edge and the commit-time drain rule is live (P-041, P-043);
//   * three owned state slots, one of which changes schema version, so the planner stages a migration on scratch
//     (P-029, P-032);
//   * two providers, one that reaches the live targets and one whose recipe has no live target yet, so the gate can
//     publish a real composition revision with no derivable target change (see the handoff's publication 3).
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Derivation.Fixtures;
using GameCore.Planning;
using GameCore.Planning.Ownership;
using GameCore.Unity.Runtime;
using Unity.Entities;
using GameCore.Unity.Runtime.Integration;

namespace GameCore.Unity.Fixtures
{
    /// <summary>One registered slot migration of the gate's catalog: adds a delta, refusing a negative source.</summary>
    public sealed class W2GateQuestMigration : ISlotMigration
    {
        public FactoryKey Key => W2GateKeys.QuestMigration;

        public uint FromVersion => 1U;

        public uint ToVersion => 2U;

        /// <summary>Times the migration body ran; the gate proves it ran on the copy, not on live state (P-029).</summary>
        public int Invocations { get; private set; }

        public bool TryMigrate(int source, out int migrated)
        {
            Invocations++;
            if (source < 0)
            {
                migrated = source;
                return false;
            }

            migrated = source + W2GateKeys.QuestMigrationDelta;
            return true;
        }
    }

    /// <summary>The same migration as GC-007's slot-policy registry sees it: a declared key with a registered pair.</summary>
    public sealed class W2GateSlotMigrations : ISlotMigrationRegistry
    {
        private readonly SlotMigrationRegistry registry = new SlotMigrationRegistry();

        public W2GateSlotMigrations()
        {
            registry.Register(
                W2GateKeys.QuestMigration,
                new SchemaRef(W2GateKeys.QuestDomain.Id, 1U),
                W2GateKeys.QuestDomain);
        }

        public bool IsRegistered(FactoryKey migrationKey, SchemaRef from, SchemaRef to)
            => registry.IsRegistered(migrationKey, from, to);

        public int Count => registry.Count;
    }

    /// <summary>
    /// Generated-style base-layout applier of one spawn recipe (04 section 6): a direct typed install, no discovery.
    /// Derived rows are never installed here; the publisher adds them inside the publication fence (P-024).
    /// </summary>
    public sealed class W2GateRecipeApplier : ISpawnApplier
    {
        private readonly int baseProgress;

        public W2GateRecipeApplier(int baseProgress)
        {
            this.baseProgress = baseProgress;
        }

        public FactoryKey Key => W2GateKeys.RecipeApplier;

        /// <summary>Targets whose base layout this applier installed.</summary>
        public int AppliedCount { get; private set; }

        public void ApplyBaseLayout(EntityManager entityManager, Entity entity, SpawnRecipe recipe)
        {
            // The recipe's base layout is the target's own state storage plus its initial value: the quest slot the
            // plan migrates and the command owner writes. Derived rows are never installed here — the publisher
            // adds them inside the publication fence (P-024, P-032, 04 section 6).
            var slots = entityManager.AddBuffer<GameCore.Unity.Runtime.TargetSlotState>(entity);
            slots.Add(new GameCore.Unity.Runtime.TargetSlotState
            {
                Slot = W2GateKeys.QuestSlot,
                Owner = W2GateKeys.QuestOwner,
                SchemaVersion = 1U,
                Value = baseProgress,
                Active = 1,
            });
            AppliedCount++;
            _ = recipe;
        }
    }

    /// <summary>Generated-style manifest declarations of the gate's two providers and the gate's own vocabulary.</summary>
    public static class W2GateDeclarations
    {
        /// <summary>Value the first provider contributes to every compatible existing target.</summary>
        public const int VillagerBindingValue = 41;

        /// <summary>Value the first provider contributes to the quest-gate recipe's targets.</summary>
        public const int GateBindingValue = 42;

        /// <summary>Value the forward provider would contribute to its (not yet live) recipe.</summary>
        public const int ForwardBindingValue = 71;

        /// <summary>
        /// Provider 1: binds a narrative villager and a quest gate automatically, with no per-instance import
        /// (P-013, P-015), and declares this catalog revision's whole execution and ownership surface.
        /// </summary>
        public static PluginManifest BindingProvider(
            PluginTypeId pluginType,
            FactoryKey factoryKey,
            SchemaRef configSchema)
        {
            return Manifest(
                pluginType,
                factoryKey,
                configSchema,
                new List<CapabilityContract>
                {
                    Contract(W2GateKeys.VillagerBindingCapability, W2GateKeys.VillagerBindingSchema),
                    Contract(W2GateKeys.GateBindingCapability, W2GateKeys.GateBindingSchema),
                },
                new List<DerivationRule>
                {
                    Rule(
                        W2GateKeys.VillagerRule,
                        W2GateKeys.VillagerBindingCapability,
                        W2GateKeys.VillagerRecipeSchema,
                        VillagerBindingValue),
                    Rule(
                        W2GateKeys.GateRule,
                        W2GateKeys.GateBindingCapability,
                        W2GateKeys.QuestGateRecipeSchema,
                        GateBindingValue),
                },
                Stages(),
                new List<BufferSpec> { StepBuffer() },
                Slots());
        }

        /// <summary>
        /// Provider 2 ("forward"): it declares a rule for a recipe no live target uses yet, so mounting it publishes
        /// a real composition revision whose derivation has no target change. The world's assembly for that
        /// publication is the spawn the gate performs, which keeps both counters on one series (P-006).
        /// </summary>
        public static PluginManifest ForwardProvider(
            PluginTypeId pluginType,
            FactoryKey factoryKey,
            SchemaRef configSchema)
        {
            return Manifest(
                pluginType,
                factoryKey,
                configSchema,
                new List<CapabilityContract>
                {
                    Contract(W2GateKeys.ForwardBindingCapability, W2GateKeys.ForwardBindingSchema),
                },
                new List<DerivationRule>
                {
                    Rule(
                        W2GateKeys.ForwardRule,
                        W2GateKeys.ForwardBindingCapability,
                        W2GateKeys.ForwardRecipeSchema,
                        ForwardBindingValue),
                },
                null,
                null,
                null);
        }

        /// <summary>One capability contract with exactly one output slot and one composition policy (P-017, P-019).</summary>
        public static CapabilityContract Contract(string capabilityName, string schemaName)
        {
            SlotId slot = FixtureIds.Slot(capabilityName + ".slot");

            return new CapabilityContract(
                FixtureIds.CapabilityRef(capabilityName, 1U),
                0,
                new List<OutputSlotSchema> { new OutputSlotSchema(slot, FixtureIds.SchemaRef(schemaName, 1U)) },
                new List<SlotCompositionPolicy> { new SlotCompositionPolicy(slot, CompositionPolicy.Replace, default(FactoryKey)) },
                null);
        }

        /// <summary>
        /// One derivation rule from a recipe selector to one output capability. Its payload is the integer value the
        /// effective slot publishes, in the canonical encoding of 05 section 6, so a derived assembly transfers into
        /// a binding row without a second interpretation (P-017).
        /// </summary>
        public static DerivationRule Rule(
            string ruleName,
            string outputCapability,
            string selectorSchema,
            int value)
        {
            return new DerivationRule(
                FixtureIds.Rule(ruleName),
                FixtureIds.CapabilityRef(outputCapability, 1U),
                0,
                1U,
                new List<SchemaRef> { FixtureIds.SchemaRef(selectorSchema, 1U) },
                W2GateKeys.AlwaysPredicate,
                null,
                PropagationReach.SelfAndDescendants,
                true,
                0,
                CompositionPolicy.Replace,
                IntegrationSlotValues.WriteInt32(value));
        }

        /// <summary>
        /// The declared stages of the gate world: one owner per domain, one per-partition pair GC-007 must prove
        /// disjoint, and an explicit required stage order so the compiled DAG's edges are declared, not inferred
        /// (P-034, P-039, P-040).
        /// </summary>
        public static IReadOnlyList<StageSpec> Stages()
        {
            var command = new StageSpec(
                W2GateKeys.CommandStage,
                1U,
                W2GateKeys.OwnerPackage,
                HostAffinity.ManagedMain,
                null,
                null,
                new AccessSet(new[] { new AccessDeclaration(W2GateKeys.QuestDomain, AccessMode.ReadWrite, default(Id128)) }),
                null,
                null,
                null,
                null,
                new List<SystemSpec> { Entry(W2GateKeys.CommandSystem, SystemMultiplicity.World, W2GateKeys.QuestDomain) },
                null);

            var settle = new StageSpec(
                W2GateKeys.SettleStage,
                1U,
                W2GateKeys.OwnerPackage,
                HostAffinity.ManagedMain,
                null,
                null,
                new AccessSet(new[] { new AccessDeclaration(W2GateKeys.TrailDomain, AccessMode.ReadWrite, default(Id128)) }),
                null,
                new List<StageId> { W2GateKeys.CommandStage },
                null,
                null,
                new List<SystemSpec> { Entry(W2GateKeys.SettleSystem, SystemMultiplicity.World, W2GateKeys.TrailDomain) },
                null);

            var claim = new StageSpec(
                W2GateKeys.ClaimStage,
                1U,
                W2GateKeys.OwnerPackage,
                HostAffinity.ManagedMain,
                null,
                null,
                new AccessSet(new[] { new AccessDeclaration(W2GateKeys.TraitDomain, AccessMode.ReadWrite, default(Id128)) }),
                null,
                new List<StageId> { W2GateKeys.SettleStage },
                null,
                null,
                new List<SystemSpec>
                {
                    // Two writers of one domain, each generated one partition: neither needs a stage edge (P-034).
                    Entry(W2GateKeys.ClaimLeftSystem, SystemMultiplicity.PerPartition, W2GateKeys.TraitDomain),
                    Entry(W2GateKeys.ClaimRightSystem, SystemMultiplicity.PerPartition, W2GateKeys.TraitDomain),
                },
                null);

            var project = new StageSpec(
                W2GateKeys.ProjectStage,
                1U,
                W2GateKeys.OwnerPackage,
                HostAffinity.ManagedMain,
                null,
                null,
                new AccessSet(new[] { new AccessDeclaration(W2GateKeys.TrailDomain, AccessMode.ReadWrite, default(Id128)) }),
                null,
                new List<StageId> { W2GateKeys.SettleStage },
                null,
                null,
                new List<SystemSpec> { Entry(W2GateKeys.ProjectSystem, SystemMultiplicity.World, W2GateKeys.TrailDomain) },
                null);

            return new List<StageSpec> { command, settle, claim, project };
        }

        /// <summary>One system entry of a declared stage with its single owned domain (P-034, P-039).</summary>
        public static SystemSpec Entry(FactoryKey key, SystemMultiplicity multiplicity, SchemaRef domain)
        {
            return new SystemSpec(
                key,
                multiplicity,
                new AccessSet(new[] { new AccessDeclaration(domain, AccessMode.ReadWrite, default(Id128)) }),
                null,
                null,
                null,
                null);
        }

        /// <summary>
        /// The declared buffer: the settle stage produces it and the later project stage consumes it, so the compiled
        /// schedule carries a real buffer edge and a deferred playback point (P-041, P-043).
        /// </summary>
        public static BufferSpec StepBuffer()
        {
            return new BufferSpec(
                W2GateKeys.StepBuffer,
                W2GateKeys.StepBufferSchema,
                new List<FactoryKey> { W2GateKeys.SettleSystem },
                W2GateKeys.SettleStage,
                W2GateKeys.ProjectStage,
                W2GateKeys.StepBufferOrderKey,
                BufferLifetime.Step,
                2,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);
        }

        /// <summary>
        /// The three owned state slots. The quest domain declares schema version 2 while the world seeds live state
        /// at version 1, so the plan stages a registered migration on bounded scratch (P-029, P-032).
        /// </summary>
        public static IReadOnlyList<StateSlotSpec> Slots()
        {
            var quest = new StateSlotSpec(
                W2GateKeys.QuestSlot,
                W2GateKeys.QuestOwner,
                W2GateKeys.QuestDomain,
                W2GateKeys.QuestLayout,
                new List<FieldOwnership> { new FieldOwnership(W2GateKeys.QuestDomain, W2GateKeys.QuestProgressField.RegistrationKey) },
                W2GateKeys.QuestInit,
                W2GateKeys.QuestConfigChange,
                W2GateKeys.QuestMigration,
                LastSupportPolicy.PreserveDormant,
                W2GateKeys.QuestTransfer,
                new List<FactoryKey> { W2GateKeys.QuestMigration });

            var trait = new StateSlotSpec(
                W2GateKeys.TraitSlot,
                W2GateKeys.TraitOwner,
                W2GateKeys.TraitDomain,
                W2GateKeys.TraitLayout,
                new List<FieldOwnership>
                {
                    new FieldOwnership(W2GateKeys.TraitDomain, W2GateKeys.TraitLeftField.RegistrationKey),
                    new FieldOwnership(W2GateKeys.TraitDomain, W2GateKeys.TraitRightField.RegistrationKey),
                },
                W2GateKeys.TraitInit,
                W2GateKeys.TraitConfigChange,
                default(FactoryKey),
                LastSupportPolicy.PreserveDormant,
                default(FactoryKey),
                null);

            var trail = new StateSlotSpec(
                W2GateKeys.TrailSlot,
                W2GateKeys.TrailOwner,
                W2GateKeys.TrailDomain,
                W2GateKeys.TrailLayout,
                new List<FieldOwnership>
                {
                    new FieldOwnership(W2GateKeys.TrailDomain, W2GateKeys.TrailStepsField.RegistrationKey),
                    new FieldOwnership(W2GateKeys.TrailDomain, W2GateKeys.TrailProjectedField.RegistrationKey),
                    new FieldOwnership(W2GateKeys.TrailDomain, W2GateKeys.TrailWaitedField.RegistrationKey),
                },
                W2GateKeys.TrailInit,
                W2GateKeys.TrailConfigChange,
                default(FactoryKey),
                LastSupportPolicy.RemoveDerived,
                default(FactoryKey),
                null);

            return new List<StateSlotSpec> { quest, trait, trail };
        }

        private static PluginManifest Manifest(
            PluginTypeId pluginType,
            FactoryKey factoryKey,
            SchemaRef configSchema,
            IReadOnlyList<CapabilityContract>? contracts,
            IReadOnlyList<DerivationRule>? rules,
            IReadOnlyList<StageSpec>? stages,
            IReadOnlyList<BufferSpec>? buffers,
            IReadOnlyList<StateSlotSpec>? slots)
        {
            return new PluginManifest(
                pluginType,
                "1.0.0",
                ContentHash.Empty,
                new SupportedProtocolRange(1, 0, 0),
                null,
                configSchema,
                factoryKey,
                null,
                null,
                contracts,
                rules,
                null,
                slots,
                stages,
                buffers,
                null);
        }
    }
}
