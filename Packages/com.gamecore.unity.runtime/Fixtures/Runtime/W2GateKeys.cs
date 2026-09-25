// GameCore.Unity.Fixtures — W2 integration gate: stable identities, ECS storage and the generated payload reader.
//
// Everything here is hand-written "generated style": stable names derived by the production
// `StableNameKeyDerivation`, direct typed references and no reflection (04 section 8). The narrative identities the
// gate reuses (recipes, capabilities, rule names) are the *same* literals the GC-006 narrative fixture declares, and
// every name goes through the same `FixtureIds` derivation, so the gate cannot silently drift from that fixture.
//
// The gate's own vocabulary (its stages, systems, buffers, domains, slots and the forward recipe) lives in this
// namespace word, so a gate identity can never collide with a package or narrative identity.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Derivation.Fixtures;
using GameCore.Execution.Messages;
using Unity.Entities;

namespace GameCore.Unity.Fixtures
{
    /// <summary>Stable identities of the W2 gate fixture, in their own namespace word.</summary>
    public static class W2GateKeys
    {
        /// <summary>Namespace word of every gate-only key, so a fixture key can never collide with a package key.</summary>
        public const ulong Namespace = 0x5732474154453231UL;

        // ---------------------------------------------------------------- narrative identities (GC-006 fixtures)

        /// <summary>The world root scope of the gate's composition; every target lives here (P-010).</summary>
        public static readonly ScopeId RootScope = FixtureIds.Scope(NarrativeComposition.StoryWorld);

        /// <summary>The villager recipe the gate's live targets and its future target share (07 section 3).</summary>
        public const string VillagerRecipeSchema = NarrativeComposition.VillagerRecipe;

        /// <summary>The quest-gate recipe: a compatible target of another kind (07 section 3).</summary>
        public const string QuestGateRecipeSchema = NarrativeComposition.QuestGateRecipe;

        /// <summary>The decorative-crowd recipe: a target no gate rule selects (P-015 ineligible case).</summary>
        public const string DecorativeCrowdRecipeSchema = NarrativeComposition.DecorativeCrowdRecipe;

        /// <summary>The quest-encounter recipe: also untouched by every gate rule.</summary>
        public const string QuestEncounterRecipeSchema = NarrativeComposition.QuestEncounterRecipe;

        /// <summary>Recipe of the gate's future target: the same recipe the existing villagers use.</summary>
        public static readonly DefinitionRef VillagerRecipe =
            FixtureIds.Recipe(VillagerRecipeSchema + ".definition", VillagerRecipeSchema);

        public static readonly DefinitionRef QuestGateRecipe =
            FixtureIds.Recipe(QuestGateRecipeSchema + ".definition", QuestGateRecipeSchema);

        public static readonly DefinitionRef DecorativeCrowdRecipe =
            FixtureIds.Recipe(DecorativeCrowdRecipeSchema + ".definition", DecorativeCrowdRecipeSchema);

        public static readonly DefinitionRef QuestEncounterRecipe =
            FixtureIds.Recipe(QuestEncounterRecipeSchema + ".definition", QuestEncounterRecipeSchema);

        /// <summary>A live target of the villager recipe: the first provider's binding reaches it (P-013).</summary>
        public static readonly TargetId Mara = FixtureIds.Target(NarrativeComposition.Mara);

        /// <summary>A live target of the quest-gate recipe: the same provider's second rule reaches it.</summary>
        public static readonly TargetId GateEast = FixtureIds.Target(NarrativeComposition.GateEast);

        /// <summary>A live target no gate rule selects, so it stays on its base recipe (P-015).</summary>
        public static readonly TargetId CrowdProp = FixtureIds.Target(NarrativeComposition.CrowdProp);

        /// <summary>A second untouched live target, of another unselected recipe.</summary>
        public static readonly TargetId EncounterOak = FixtureIds.Target(NarrativeComposition.EncounterOak);

        /// <summary>The target that does not exist yet: one spawn publishes it fully assembled (P-024).</summary>
        public static readonly TargetId FutureVillager = FixtureIds.Target("w2.target.future-villager");

        /// <summary>Stable names of the rules the gate's providers declare, in the narrative fixture's vocabulary.</summary>
        public static string VillagerRule => NarrativeComposition.DialogueRule("chapter-one");

        public static string GateRule => NarrativeComposition.GateRule("chapter-one");

        // ---------------------------------------------------------------- derived capabilities and schemas

        public const string VillagerBindingCapability = "w2.capability.villager-binding";

        public const string VillagerBindingSchema = "w2.schema.villager-binding";

        public const string GateBindingCapability = "w2.capability.gate-binding";

        public const string GateBindingSchema = "w2.schema.gate-binding";

        public const string ForwardBindingCapability = "w2.capability.forward-binding";

        public const string ForwardBindingSchema = "w2.schema.forward-binding";

        /// <summary>
        /// Recipe of the forward provider's rule, and of no live target: mounting that provider publishes a real
        /// composition revision whose derivation changes no target (the gate handoff's publication 3).
        /// </summary>
        public const string ForwardRecipeSchema = "w2.recipe.forward-villager";

        public static readonly DefinitionRef ForwardRecipe =
            FixtureIds.Recipe(ForwardRecipeSchema + ".definition", ForwardRecipeSchema);

        public const string ForwardRule = "w2.rule.forward-binding";

        /// <summary>
        /// Stable name of the generated always-accepting predicate: the value source registers it by *name*
        /// (`FixtureValueSource.RegisterAlwaysPredicate`), and the manifest declares the key derived from that name.
        /// </summary>
        public const string AlwaysPredicateName = "w2.predicate.always";

        /// <summary>The generated registration key of that predicate, as the declared rule carries it (P-009).</summary>
        public static readonly FactoryKey AlwaysPredicate = FixtureIds.Key(AlwaysPredicateName);

        // ---------------------------------------------------------------- gate-only identities

        /// <summary>Issuer of every gate-minted operation identity (P-050).</summary>
        public static readonly Id128 Issuer = FixtureIds.Id("w2.issuer");

        /// <summary>Owning package of every gate-declared stage.</summary>
        public static readonly Id128 OwnerPackage = FixtureIds.Id("w2.package");

        public static readonly StageId CommandStage = new StageId(FixtureIds.Id("w2.stage.command"));
        public static readonly StageId SettleStage = new StageId(FixtureIds.Id("w2.stage.settle"));
        public static readonly StageId ClaimStage = new StageId(FixtureIds.Id("w2.stage.claim"));
        public static readonly StageId ProjectStage = new StageId(FixtureIds.Id("w2.stage.project"));

        public static readonly FactoryKey CommandSystem = FixtureIds.Key("w2.system.command");
        public static readonly FactoryKey SettleSystem = FixtureIds.Key("w2.system.settle");
        public static readonly FactoryKey ClaimLeftSystem = FixtureIds.Key("w2.system.claim.left");
        public static readonly FactoryKey ClaimRightSystem = FixtureIds.Key("w2.system.claim.right");
        public static readonly FactoryKey ProjectSystem = FixtureIds.Key("w2.system.project");

        /// <summary>Declared producer of the host-ingress command lane; the host appends admitted commands (P-043).</summary>
        public static readonly FactoryKey HostIngressProducer = FixtureIds.Key("w2.producer.host");

        public static readonly BufferId CommandBuffer = new BufferId(FixtureIds.Id("w2.buffer.command"));
        public static readonly BufferId StepBuffer = new BufferId(FixtureIds.Id("w2.buffer.step"));
        public static readonly RouteId CommandRoute = new RouteId(FixtureIds.Id("w2.route.command"));

        /// <summary>Order key of the bounded command lane (P-008: order is a declared key, not arrival timing).</summary>
        public static readonly FactoryKey CommandOrderKey = FixtureIds.Key("w2.order.command");

        /// <summary>Order key of the declared gameplay step buffer.</summary>
        public static readonly FactoryKey StepBufferOrderKey = FixtureIds.Key("w2.order.step-buffer");

        /// <summary>Schema of one admitted command payload; one generated int32 field (05 section 6).</summary>
        public static readonly SchemaRef CommandSchema = FixtureIds.SchemaRef("w2.schema.command", 1U);

        /// <summary>Schema of one committed command result event; one generated int32 field.</summary>
        public static readonly SchemaRef ResultSchema = FixtureIds.SchemaRef("w2.schema.command-result", 1U);

        /// <summary>Schema of the declared gameplay step buffer the settle stage produces for the project stage.</summary>
        public static readonly SchemaRef StepBufferSchema = FixtureIds.SchemaRef("w2.schema.step-buffer", 1U);

        // ---------------------------------------------------------------- domains, owners, slots and fields

        /// <summary>Authoritative domain A: quest state, declared at version 2 so live state must migrate (P-032).</summary>
        public static readonly SchemaRef QuestDomain = FixtureIds.SchemaRef("w2.domain.quest-state", 2U);

        /// <summary>Authoritative domain B: the villager trait, written by two per-partition writers (P-040).</summary>
        public static readonly SchemaRef TraitDomain = FixtureIds.SchemaRef("w2.domain.villager-trait", 1U);

        /// <summary>Authoritative domain C: the observable step trail, written by two ordered stages.</summary>
        public static readonly SchemaRef TrailDomain = FixtureIds.SchemaRef("w2.domain.step-trail", 1U);

        public static readonly OwnerId QuestOwner = new OwnerId(FixtureIds.Id("w2.owner.quest"));
        public static readonly OwnerId TraitOwner = new OwnerId(FixtureIds.Id("w2.owner.trait"));
        public static readonly OwnerId TrailOwner = new OwnerId(FixtureIds.Id("w2.owner.trail"));

        public static readonly SlotId QuestSlot = FixtureIds.Slot("w2.slot.quest");
        public static readonly SlotId TraitSlot = FixtureIds.Slot("w2.slot.villager-trait");
        public static readonly SlotId TrailSlot = FixtureIds.Slot("w2.slot.step-trail");

        public static readonly FactoryKey QuestLayout = FixtureIds.Key("w2.layout.quest");
        public static readonly FactoryKey TraitLayout = FixtureIds.Key("w2.layout.villager-trait");
        public static readonly FactoryKey TrailLayout = FixtureIds.Key("w2.layout.step-trail");

        public static readonly FactoryKey QuestInit = FixtureIds.Key("w2.init.quest");
        public static readonly FactoryKey TraitInit = FixtureIds.Key("w2.init.villager-trait");
        public static readonly FactoryKey TrailInit = FixtureIds.Key("w2.init.step-trail");

        public static readonly FactoryKey QuestConfigChange = FixtureIds.Key("w2.config-change.quest");
        public static readonly FactoryKey TraitConfigChange = FixtureIds.Key("w2.config-change.villager-trait");
        public static readonly FactoryKey TrailConfigChange = FixtureIds.Key("w2.config-change.step-trail");

        public static readonly FactoryKey QuestTransfer = FixtureIds.Key("w2.transfer.quest");

        /// <summary>Physical fields of domain A's component layout; every one belongs to its single slot (P-032).</summary>
        public static readonly FactoryKey QuestProgressField = FixtureIds.Key("w2.field.quest.progress");

        /// <summary>Physical fields of domain B's component layout: one per per-partition writer (P-034, P-040).</summary>
        public static readonly FactoryKey TraitLeftField = FixtureIds.Key("w2.field.trait.left");

        public static readonly FactoryKey TraitRightField = FixtureIds.Key("w2.field.trait.right");

        /// <summary>Physical fields of domain C's component layout: steps, the projected read, and the fence flag.</summary>
        public static readonly FactoryKey TrailStepsField = FixtureIds.Key("w2.field.trail.steps");

        public static readonly FactoryKey TrailProjectedField = FixtureIds.Key("w2.field.trail.projected");

        public static readonly FactoryKey TrailWaitedField = FixtureIds.Key("w2.field.trail.waited");

        /// <summary>Registered migration of domain A from schema version 1 to 2 (P-032).</summary>
        public static readonly FactoryKey QuestMigration = FixtureIds.Key("w2.migration.quest.v1-v2");

        /// <summary>Generated base-layout applier key of every gate recipe (04 section 6).</summary>
        public static readonly FactoryKey RecipeApplier = FixtureIds.Key("w2.recipe.applier.gate-target");

        /// <summary>The live quest value every seeded target starts from, at schema version 1.</summary>
        public const int SeededQuestValue = 100;

        /// <summary>Value delta the registered migration adds when it moves version 1 to 2.</summary>
        public const int QuestMigrationDelta = 10;

        /// <summary>Live quest value the publication leaves behind: the migrated value (P-029).</summary>
        public const int MigratedQuestValue = SeededQuestValue + QuestMigrationDelta;

        /// <summary>The progress one admitted command adds to the addressed target (P-042, P-044).</summary>
        public const int CommandProgressDelta = 5;

        /// <summary>Quest progress the addressed target holds after exactly one command committed.</summary>
        public const int CommandedQuestValue = MigratedQuestValue + CommandProgressDelta;

        /// <summary>Stable clock identity of the gate's one registered domain clock (GC-009 temporal driver).</summary>
        public static readonly Id128 DomainClock = FixtureIds.Id("w2.clock.domain");

        public static PluginTypeId PluginTypeId(ulong ordinal) => new PluginTypeId(new Id128(Namespace, 0x0200UL + ordinal));

        public static PluginInstanceId Instance(ulong ordinal) =>
            new PluginInstanceId(new Id128(Namespace, 0x0300UL + ordinal));

        public static OperationId Operation(WorldId world, ulong sequence) => new OperationId(world, Issuer, sequence);
    }


    /// <summary>Domain B: the trait two per-partition writers independently own a field of (P-034, P-040).</summary>
    public struct W2GateTrait : IComponentData
    {
        public int LeftValue;

        public int RightValue;
    }

    /// <summary>
    /// Domain C: the step trail. The settle stage advances the step count and the later project stage records the
    /// value it read through the producer fence, so both ordered writers and the dependent read are observable
    /// (P-034, P-041).
    /// </summary>
    public struct W2GateTrail : IComponentData
    {
        public int Steps;

        public int ProjectedValue;

        /// <summary>1 when the project stage really combined the settle stage's native producer fence (P-041).</summary>
        public byte WaitedOnNativeFence;
    }

    /// <summary>
    /// Generated-style reader of the gate's command payload: exactly one big-endian int32 scalar, the canonical
    /// encoding of 05 section 6 (04 section 8, P-042).
    /// </summary>
    public sealed class W2GatePayloadReader : ICommandPayloadReader<int>
    {
        /// <summary>Bytes of one canonical int32 scalar, as the gate's payloads are written.</summary>
        public const int PayloadBytes = 4;

        public SchemaRef Schema => W2GateKeys.CommandSchema;

        public int Read(IReadOnlyList<byte> payload)
        {
            if (payload == null || payload.Count != PayloadBytes)
            {
                throw new System.ArgumentException(
                    "the gate command payload is exactly one 4-byte canonical int32 scalar.", nameof(payload));
            }

            uint raw = ((uint)payload[0] << 24) | ((uint)payload[1] << 16) | ((uint)payload[2] << 8) | payload[3];
            return unchecked((int)raw);
        }
    }
}
