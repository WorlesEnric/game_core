// GameCore.Gameplay.Cards — the card plugin's registration data and its precompiled bindings (GC-011).
//
// Normative sources: 04 s8 (registration is data: the precompiled factory key, the system factory keys, the typed
// payload readers and the spawn recipes are all direct typed references, and nothing is discovered by reflection),
// P-024 (a precompiled `SpawnRecipe` carries the base layout and the descriptor a target is spawned from), P-042
// (a command lane is a declared route with an owner, a schema and a bounded ingress buffer), P-043 (a declared
// buffer names its producers, its single consuming owner, its order key, its capacity and its overflow policy) and
// P-009 (an unregistered key is a miss).
//
// Two plugin-level generated bindings exist because the card slice must be resolvable through the *generated*
// catalog, not only through the hand-written fixture table: `CardTablePluginFactory` is the precompiled
// `PluginFactory` registration, and `CardTableSystemFactory` is one `SystemFactory` registration per compiled card
// system key. Both carry their own key and their stable name, so the registered path and the direct path cannot
// drift (P-028).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Execution.Messages;
using GameCore.Rules.Cards;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using GameCore.Unity.Runtime.Time;
using Unity.Entities;

namespace GameCore.Gameplay.Cards
{
    /// <summary>The precompiled plugin factory a generated card catalog registers (a `PluginFactory` entry).</summary>
    public interface ICardTablePluginFactory
    {
        /// <summary>The generated registration key this factory is bound under.</summary>
        FactoryKey Key { get; }

        /// <summary>The registration's stable name, for diagnostics and fingerprints.</summary>
        string StableName { get; }
    }

    /// <summary>
    /// The card table plugin's precompiled factory. A mount resolves it by key through the catalog, and the
    /// instance it produces is the table runtime the four systems serve (04 s8).
    /// </summary>
    public sealed class CardTablePluginFactory : ICardTablePluginFactory
    {
        /// <summary>The stable name of the one `PluginFactory` registration of the card table.</summary>
        public const string RegistrationStableName = "cards.factory.card-table-plugin";

        /// <summary>Binds the factory to its generated registration key.</summary>
        public CardTablePluginFactory(FactoryKey key)
        {
            Key = key;
        }

        /// <inheritdoc />
        public FactoryKey Key { get; }

        /// <inheritdoc />
        public string StableName => RegistrationStableName;
    }

    /// <summary>The precompiled dispatch factory of one compiled card system (a `SystemFactory` entry).</summary>
    public interface ICardTableSystemFactory
    {
        /// <summary>The generated dispatch key this factory is bound under (P-039).</summary>
        FactoryKey Key { get; }

        /// <summary>The registration's stable name, for diagnostics and fingerprints.</summary>
        string StableName { get; }

        /// <summary>The dispatch kind the guarded dispatcher must use for this key (P-040).</summary>
        SystemDispatchKind Kind { get; }
    }

    /// <summary>
    /// One card system's generated factory binding. The concrete <see cref="SystemBase"/> is created by the
    /// generated-style `SystemRegistration` list of <see cref="CardTableRegistration.Systems"/>, which is the same
    /// direct-typed shape the emitter writes; this type is the keyed registration the compiled schedule resolves.
    /// </summary>
    public sealed class CardTableSystemFactory : ICardTableSystemFactory
    {
        /// <summary>Binds one system factory to its generated dispatch key.</summary>
        public CardTableSystemFactory(FactoryKey key)
        {
            Key = key;
            StableName = "cards.system-factory." + key.RegistrationKey.ToString();
        }

        /// <inheritdoc />
        public FactoryKey Key { get; }

        /// <inheritdoc />
        public string StableName { get; }

        /// <inheritdoc />
        public SystemDispatchKind Kind => SystemDispatchKind.ManagedSystem;
    }

    /// <summary>
    /// The card plugin's declared execution surface: the bounded command lanes and their typed readers, the four
    /// systems, and the composition root a card world is created with.
    /// </summary>
    public static class CardTableRegistration
    {
        /// <summary>World name; the host appends the session id, so every world name is an inspectable incarnation.</summary>
        public const string WorldName = "GameCoreCardTableWorld";

        /// <summary>World definition of a card-table world.</summary>
        public static WorldDefinitionId WorldDefinition => CardTableKeys.WorldDefinition;

        /// <summary>
        /// The card table's two bounded lanes (07 s2.3): one ordinary command route and one declared atomic batch
        /// envelope. Both are reliable (`RejectBeforeMutation`), both are owned by the table runtime, and both are
        /// drained by `cards.input`, which is their single consuming stage (P-042, P-043).
        /// </summary>
        public static MessagePlaneRegistration Messages()
        {
            var commandRoute = new CommandRoute(
                CardTableKeys.CommandRoute,
                CardTableKeys.TableOwner,
                CardTableKeys.CommandSchema,
                CardTableKeys.InputStage,
                CardTableKeys.InputStage,
                CardTableKeys.CommandLane,
                CardTableKeys.HostIngressProducer,
                CardTableKeys.DraftCapacity,
                false);

            var batchRoute = new CommandRoute(
                CardTableKeys.BatchRoute,
                CardTableKeys.TableOwner,
                CardTableKeys.BatchSchema,
                CardTableKeys.InputStage,
                CardTableKeys.InputStage,
                CardTableKeys.BatchLane,
                CardTableKeys.HostIngressProducer,
                CardSetRules.MaxCandidates,
                false);

            var commandLane = new MessageBufferDescriptor(
                CardTableKeys.CommandLane,
                CardTableKeys.CommandSchema,
                new[] { CardTableKeys.HostIngressProducer },
                CardTableKeys.TableOwner,
                CardTableKeys.InputStage,
                CardTableKeys.InputStage,
                CardTableKeys.CommandOrderKey,
                BufferLifetime.Step,
                CardTableKeys.DraftCapacity,
                CardTableKeys.DraftCapacity * CardPayloadCodec.CommandBytes,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);

            var batchLane = new MessageBufferDescriptor(
                CardTableKeys.BatchLane,
                CardTableKeys.BatchSchema,
                new[] { CardTableKeys.HostIngressProducer },
                CardTableKeys.TableOwner,
                CardTableKeys.InputStage,
                CardTableKeys.InputStage,
                CardTableKeys.BatchOrderKey,
                BufferLifetime.Step,
                CardSetRules.MaxCandidates,
                CardSetRules.MaxCandidates * CardPayloadCodec.BatchBytes,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);

            return new MessagePlaneRegistration(
                new List<CommandRoute> { commandRoute, batchRoute },
                new List<MessageBufferDescriptor> { commandLane, batchLane },
                null,
                maxPendingRequests: CardTableKeys.DraftCapacity,
                maxRetainedResults: CardTableKeys.DraftCapacity,
                maxRetainedEvents: CardTableKeys.DraftCapacity,
                maxEventsPerStep: CardSetRules.MaxCandidates,
                nextStepCapacity: 2);
        }

        /// <summary>The generated typed readers of the card plane (04 s8): one per declared payload schema.</summary>
        public static CommandPayloadReaders Readers()
        {
            var readers = new CommandPayloadReaders();
            if (!readers.TryBind(new CardCommandReader(), out string commandFailure))
            {
                throw new InvalidOperationException("the card command reader registration failed: " + commandFailure);
            }

            if (!readers.TryBind(new CardBatchReader(), out string batchFailure))
            {
                throw new InvalidOperationException("the card batch reader registration failed: " + batchFailure);
            }

            return readers;
        }

        /// <summary>The card table's four systems, one per declared stage, in the compiled order (P-039).</summary>
        public static IReadOnlyList<SystemRegistration> Systems()
        {
            return new List<SystemRegistration>
            {
                new ManagedSystemRegistration<CardInputSystem>(
                    CardTableKeys.InputSystem, CardTableKeys.InputStage, "CardInputSystem"),
                new ManagedSystemRegistration<CardValidateSystem>(
                    CardTableKeys.ValidateSystem, CardTableKeys.ValidateStage, "CardValidateSystem"),
                new ManagedSystemRegistration<CardCommitSystem>(
                    CardTableKeys.CommitSystem, CardTableKeys.CommitStage, "CardCommitSystem"),
                new ManagedSystemRegistration<CardOutputSystem>(
                    CardTableKeys.OutputSystem, CardTableKeys.OutputStage, "CardOutputSystem"),
            };
        }

        /// <summary>
        /// The generated dispatch-kind table of the card systems (GC-009's own resolver input). Every compiled key
        /// must resolve here, or the schedule adaptation reports a witness instead of guessing (04 s8).
        /// </summary>
        public static ScheduleDispatchKindTable DispatchKinds()
        {
            var kinds = new ScheduleDispatchKindTable();
            for (int i = 0; i < CardTableKeys.SystemKeys.Length; i++)
            {
                kinds.Add(CardTableKeys.SystemKeys[i], SystemDispatchKind.ManagedSystem);
            }

            return kinds;
        }

        /// <summary>
        /// The world's composition root: the compiled schedule's adapted plan becomes the initial step table, and
        /// GC-008's publisher rebinds the group from the same compiled order at every publication (P-040).
        /// </summary>
        public static UnityWorldRegistration Create(
            ScheduleAdaptation adaptation,
            IReadOnlyList<SystemRegistration> systems)
        {
            if (adaptation == null)
            {
                throw new ArgumentNullException(nameof(adaptation));
            }

            if (!adaptation.Succeeded || adaptation.StepPlan == null)
            {
                throw new ArgumentException(
                    "a rejected schedule adaptation has no dispatch table to register: " + adaptation.Explain(),
                    nameof(adaptation));
            }

            return new UnityWorldRegistration(
                WorldName,
                adaptation.Stages,
                systems,
                GuardedDispatchPlan.Empty,
                adaptation.StepPlan,
                GuardedDispatchPlan.Empty,
                null,
                Messages(),
                Readers());
        }

        /// <summary>Creation request of one command-driven card world (O-01, P-035).</summary>
        public static WorldCreateRequest CommandDrivenRequest(WorldId world, OperationId operation, ContentHash catalogHash)
        {
            return new WorldCreateRequest(
                world,
                WorldDefinition,
                TemporalModel.CommandDriven,
                PropagationMode.Automatic,
                catalogHash,
                operation,
                null);
        }
    }

    /// <summary>
    /// The card recipes' base-layout appliers (04 s6, P-024). Each installs only the recipe's base components and
    /// buffers; the derived binding rows are added by the publisher inside the publication fence, so a spawned
    /// target is never visible half-assembled.
    /// </summary>
    public sealed class CardSeatApplier : ISpawnApplier
    {
        /// <summary>Card ordinal assigned to the next seat this applier installs, so a spawned seat is seated.</summary>
        public uint NextOrdinal { get; set; }

        /// <summary>Score a newly installed seat starts from.</summary>
        public int InitialScore { get; set; }

        /// <inheritdoc />
        public FactoryKey Key => CardIdentity.Key("cards.recipe.applier.card-seat");

        /// <summary>Seats whose base layout this applier installed.</summary>
        public int AppliedCount { get; private set; }

        /// <inheritdoc />
        public void ApplyBaseLayout(EntityManager entityManager, Entity entity, SpawnRecipe recipe)
        {
            CardTableAccess.InstallSeatStorage(entityManager, entity, NextOrdinal, InitialScore);
            AppliedCount++;
            _ = recipe;
        }
    }

    /// <summary>The market table's base-layout applier: the table state, its market/deck rows and its drafts.</summary>
    public sealed class MarketTableApplier : ISpawnApplier
    {
        /// <inheritdoc />
        public FactoryKey Key => CardIdentity.Key("cards.recipe.applier.market-table");

        /// <summary>Tables whose base layout this applier installed.</summary>
        public int AppliedCount { get; private set; }

        /// <inheritdoc />
        public void ApplyBaseLayout(EntityManager entityManager, Entity entity, SpawnRecipe recipe)
        {
            CardTableAccess.InstallTableStorage(entityManager, entity, CardTableKeys.SeededTableVersion);
            AppliedCount++;
            _ = recipe;
        }
    }

    /// <summary>The card slice's precompiled spawn recipes and the closed catalog a publisher resolves them from.</summary>
    public static class CardTableRecipes
    {
        /// <summary>The seat recipe: a compatible target of every scoring rule (07 s2.1).</summary>
        public static SpawnRecipe Seat(CardSeatApplier applier)
        {
            return Recipe(
                CardTableKeys.SeatRecipe,
                CardVocabulary.CardSeatRecipe,
                new List<SchemaRef>
                {
                    CardVocabulary.SelectorSchema(CardVocabulary.CardSeatRecipe),
                    CardTableKeys.SeatDomain,
                },
                applier);
        }

        /// <summary>The market table recipe: the only recipe the market binding rule selects (07 s2.1).</summary>
        public static SpawnRecipe MarketTable(MarketTableApplier applier)
        {
            return Recipe(
                CardTableKeys.MarketTableRecipe,
                CardVocabulary.MarketTableRecipe,
                new List<SchemaRef>
                {
                    CardVocabulary.SelectorSchema(CardVocabulary.MarketTableRecipe),
                    CardTableKeys.TableDomain,
                },
                applier);
        }

        /// <summary>
        /// The scoreboard view recipe: it advertises neither scoring schema, so no card rule selects it and it
        /// stays on its base layout (P-015's ineligible case).
        /// </summary>
        public static SpawnRecipe ScoreboardView(MarketTableApplier applier)
        {
            return Recipe(
                CardTableKeys.ScoreboardViewRecipe,
                CardVocabulary.ScoreboardViewRecipe,
                new List<SchemaRef> { CardVocabulary.SelectorSchema(CardVocabulary.ScoreboardViewRecipe) },
                applier);
        }

        /// <summary>The world's closed recipe catalog over the given applier instances (P-015, P-024).</summary>
        public static SpawnRecipeCatalog Catalog(CardSeatApplier seatApplier, MarketTableApplier tableApplier)
        {
            return new SpawnRecipeCatalog(new List<SpawnRecipe>
            {
                Seat(seatApplier),
                MarketTable(tableApplier),
                ScoreboardView(tableApplier),
            });
        }

        /// <summary>
        /// One recipe with its immutable descriptor. The descriptor's supported schemas are the recipe's own
        /// selector schema plus the domains a settlement addresses, which is exactly what a derivation rule's
        /// selector and a target's declared state both read (P-015).
        /// </summary>
        private static SpawnRecipe Recipe(
            DefinitionRef recipe,
            string recipeStableName,
            IReadOnlyList<SchemaRef> supportedSchemas,
            ISpawnApplier applier)
        {
            var descriptor = new TargetDescriptor(
                recipe,
                supportedSchemas,
                null,
                new List<Id128> { CardIdentity.Id(recipeStableName) },
                default(AssetAdapterDescriptor),
                null,
                null,
                null,
                null);

            return new SpawnRecipe(recipe, descriptor, supportedSchemas, applier);
        }
    }
}
