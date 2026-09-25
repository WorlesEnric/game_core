// GameCore.Gameplay.Cards — the card plugin's generated-style declarations (GC-011).
//
// Normative sources: 07 s2.3 (the four-stage settlement plan: `cards.input` -> `cards.validate` ->
// `cards.commit` -> `cards.output`), 00 P-034 (one owner per authoritative domain; two writers of one domain need
// a directed order or validated disjoint partitions), P-039/P-040 (declared stages, system keys, buffer ports and
// the compiled DAG), P-043 (a declared buffer has producers, exactly one consuming stage, an order key and a
// bounded capacity) and 04 s8 (registration is data: every declaration below is a direct typed reference).
//
// The shape of this file mirrors `W2GateDeclarations` in the Wave 2 gate fixture: a real `PluginManifest` set that
// `OwnershipSchedulePipeline.Build` validates with the production GC-007 validators and compiles with the
// production GC-009 compiler. Nothing here re-implements a kernel module.
//
// One table runtime owns several entities. That is the card slice's whole point (07 s2.2: "The table entity and
// seat entities span several scopes. One CardTableRuntime instance therefore owns multiple ECS entities"), and it
// is why every one of the five declared domains names the same `CardTableKeys.TableOwner`: a single logical owner
// is what lets `cards.commit` settle the table and several seats as one bounded domain decision (P-034, P-044)
// instead of routing each seat write through a cross-owner request.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Rules.Cards;

namespace GameCore.Gameplay.Cards
{
    /// <summary>
    /// The four-stage settlement plan of 07 s2.3, its four systems, its one declared step buffer and its five
    /// owned state slots, all under one logical owner.
    /// </summary>
    public static class CardTableDeclarations
    {
        /// <summary>Owner package identity of every card declaration; the card gameplay package.</summary>
        public static readonly Id128 OwnerPackage = CardIdentity.Id("cards.package.gameplay");

        /// <summary>
        /// The table runtime's declaration: the settlement plan, its five domains and its declared buffer.
        /// </summary>
        public static PluginManifest TableRuntime(
            PluginTypeId pluginType,
            FactoryKey factoryKey,
            SchemaRef configSchema,
            IReadOnlyList<ServiceExport>? serviceExports,
            IReadOnlyList<ServiceDependency>? serviceDependencies)
        {
            return Manifest(
                pluginType,
                factoryKey,
                configSchema,
                serviceExports,
                serviceDependencies,
                new List<CapabilityContract>(),
                new List<DerivationRule>(),
                Slots(),
                Stages(),
                new List<BufferSpec> { DecisionStepBuffer() });
        }

        /// <summary>
        /// The scoring provider's declaration: one `cards.set-bonus` capability contract and one `Additive` rule
        /// whose selector is the seat recipe. This is the inherited rule modifier contribution: mounting it makes
        /// every compatible existing seat's next committed set score the extra bonus, and a seat created later
        /// inherits the same contribution automatically (P-013, P-015, P-024).
        /// </summary>
        public static PluginManifest ScoringProvider(
            PluginTypeId pluginType,
            FactoryKey factoryKey,
            SchemaRef configSchema,
            string ruleStableName,
            int bonus)
        {
            return Manifest(
                pluginType,
                factoryKey,
                configSchema,
                null,
                null,
                new List<CapabilityContract> { SetBonusContract() },
                new List<DerivationRule> { SetBonusRule(ruleStableName, bonus) },
                null,
                null,
                null);
        }

        /// <summary>
        /// The rule library's declaration. It proposes no derivation rule and owns no state: it exists to export
        /// its definition-lookup service, which is what 07 s2.1 separates from derived scoring configuration
        /// ("This keeps service lookup, derived scoring configuration, and the gameplay act of awarding points
        /// distinct").
        /// </summary>
        public static PluginManifest RuleLibrary(
            PluginTypeId pluginType,
            FactoryKey factoryKey,
            SchemaRef configSchema)
        {
            return Manifest(
                pluginType,
                factoryKey,
                configSchema,
                new List<ServiceExport>
                {
                    new ServiceExport(
                        CardTableKeys.LookupContract,
                        CardTableKeys.LookupServiceFactory,
                        ServiceVisibility.ExportToDescendants,
                        false,
                        ServiceBindingKind.Single),
                },
                null,
                new List<CapabilityContract>(),
                new List<DerivationRule>(),
                null,
                null,
                null);
        }

        /// <summary>
        /// The `cards.set-bonus` capability contract: one output slot, `Additive`, and the registered Int32 sum
        /// reducer of the card rules package (P-017, P-019). The slot id is derived exactly as a contract
        /// declaration derives it (`&lt;capability&gt;.slot-0`), which is the identity `CardVocabulary` publishes.
        /// </summary>
        public static CapabilityContract SetBonusContract()
        {
            return new CapabilityContract(
                CardVocabulary.SetBonusContract,
                CardVocabulary.BonusStratum,
                new List<OutputSlotSchema>
                {
                    new OutputSlotSchema(CardVocabulary.SetBonusSlot, CardVocabulary.EffectiveSetBonusSchemaRef),
                },
                new List<SlotCompositionPolicy>
                {
                    // The reducer is the version carrier of the fold (05 s3): key `cards.reducer.int32-sum`, v1.
                    new SlotCompositionPolicy(
                        CardVocabulary.SetBonusSlot,
                        CompositionPolicy.Additive,
                        CardVocabulary.BonusReducerKey),
                },
                null);
        }

        /// <summary>
        /// One scoring provider's `cards.set-bonus` rule. The payload is exactly one fixed-width big-endian int32
        /// (05 s6), the same encoding `CardPayloadCodec` writes and `IntegrationSlotValues` reads, so the derived
        /// value a binding row carries and the value the settlement adds cannot disagree.
        /// </summary>
        public static DerivationRule SetBonusRule(string ruleStableName, int bonus)
        {
            return new DerivationRule(
                CardIdentity.Rule(ruleStableName),
                CardVocabulary.SetBonusContract,
                CardVocabulary.BonusStratum,
                1U,
                new List<SchemaRef> { CardVocabulary.SelectorSchema(CardVocabulary.CardSeatRecipe) },
                CardVocabulary.AlwaysPredicateKey,
                null,
                PropagationReach.SelfAndDescendants,
                true,
                0,
                CompositionPolicy.Additive,
                WriteInt32(bonus));
        }

        /// <summary>
        /// The declared stages, in dispatch order, each naming the stage it must follow. The chain is declared
        /// rather than inferred because P-040 forbids the scheduler from inventing gameplay order: a settlement
        /// validates before it commits, and the compiled DAG must contain that edge as a declared one.
        /// </summary>
        public static IReadOnlyList<StageSpec> Stages()
        {
            // 1. `cards.input`: it decodes the admitted envelopes into the owner's bounded command draft. It writes
            //    no authoritative state, which is why its only write claim is the draft domain.
            var input = Stage(
                CardTableKeys.InputStage,
                "cards.stage.input",
                null,
                new AccessSet(new[]
                {
                    new AccessDeclaration(CardTableKeys.CommandDraftDomain, AccessMode.ReadWrite, default(Id128)),
                }),
                new List<SystemSpec>
                {
                    System(
                        CardTableKeys.InputSystem,
                        new AccessSet(new[]
                        {
                            new AccessDeclaration(CardTableKeys.CommandDraftDomain, AccessMode.ReadWrite, default(Id128)),
                        })),
                });

            // 2. `cards.validate`: it reads the authoritative state and builds one bounded draft. It cannot write a
            //    card, a score or the table version, so a rejection after it has run has changed nothing (P-044).
            var validate = Stage(
                CardTableKeys.ValidateStage,
                "cards.stage.validate",
                new List<StageId> { CardTableKeys.InputStage },
                new AccessSet(new[]
                {
                    new AccessDeclaration(CardTableKeys.DecisionDraftDomain, AccessMode.ReadWrite, default(Id128)),
                }),
                new List<SystemSpec>
                {
                    System(
                        CardTableKeys.ValidateSystem,
                        new AccessSet(new[]
                        {
                            new AccessDeclaration(CardTableKeys.DecisionDraftDomain, AccessMode.ReadWrite, default(Id128)),
                            new AccessDeclaration(CardTableKeys.CommandDraftDomain, AccessMode.Read, default(Id128)),
                            new AccessDeclaration(CardTableKeys.TableDomain, AccessMode.Read, default(Id128)),
                            new AccessDeclaration(CardTableKeys.SeatDomain, AccessMode.Read, default(Id128)),
                        })),
                });

            // 3. `cards.commit`: the sole writer of the table and of every affected seat (07 s2.3). Its three write
            //    claims all name one owner, so one stage settles several entities as one domain decision.
            var commit = Stage(
                CardTableKeys.CommitStage,
                "cards.stage.commit",
                new List<StageId> { CardTableKeys.ValidateStage },
                new AccessSet(new[]
                {
                    new AccessDeclaration(CardTableKeys.TableDomain, AccessMode.ReadWrite, default(Id128)),
                    new AccessDeclaration(CardTableKeys.SeatDomain, AccessMode.ReadWrite, default(Id128)),
                }),
                new List<SystemSpec>
                {
                    System(
                        CardTableKeys.CommitSystem,
                        new AccessSet(new[]
                        {
                            new AccessDeclaration(CardTableKeys.TableDomain, AccessMode.ReadWrite, default(Id128)),
                            new AccessDeclaration(CardTableKeys.SeatDomain, AccessMode.ReadWrite, default(Id128)),
                            new AccessDeclaration(CardTableKeys.DecisionDraftDomain, AccessMode.Read, default(Id128)),
                        })),
                });

            // 4. `cards.output`: it reads the completed owner state and prepares the coherent committed output. It
            //    reads the draft and the cards but writes only its own output domain, so no score prediction can
            //    become authority (07 s2.3).
            var output = Stage(
                CardTableKeys.OutputStage,
                "cards.stage.output",
                new List<StageId> { CardTableKeys.CommitStage },
                new AccessSet(new[]
                {
                    new AccessDeclaration(CardTableKeys.OutputDomain, AccessMode.ReadWrite, default(Id128)),
                }),
                new List<SystemSpec>
                {
                    System(
                        CardTableKeys.OutputSystem,
                        new AccessSet(new[]
                        {
                            new AccessDeclaration(CardTableKeys.OutputDomain, AccessMode.ReadWrite, default(Id128)),
                            new AccessDeclaration(CardTableKeys.DecisionDraftDomain, AccessMode.Read, default(Id128)),
                            new AccessDeclaration(CardTableKeys.TableDomain, AccessMode.Read, default(Id128)),
                            new AccessDeclaration(CardTableKeys.SeatDomain, AccessMode.Read, default(Id128)),
                        })),
                });

            return new List<StageSpec> { input, validate, commit, output };
        }

        /// <summary>
        /// The declared step buffer between `cards.validate` and `cards.commit` (07 s2.3, P-043): the validate
        /// stage produces it, the commit stage is its single consumer, and its lifetime is one step. The schedule
        /// therefore carries a real producer-before-consumer edge and a deferred playback point, and its
        /// <see cref="BufferOverflowPolicy.RejectBeforeMutation"/> bound is what makes a batch larger than the
        /// declared capacity reject before any mutation instead of being silently dropped.
        /// </summary>
        public static BufferSpec DecisionStepBuffer()
        {
            return new BufferSpec(
                CardTableKeys.DecisionBuffer,
                CardTableKeys.DecisionDraftDomain,
                new List<FactoryKey> { CardTableKeys.ValidateSystem },
                CardTableKeys.ValidateStage,
                CardTableKeys.CommitStage,
                CardTableKeys.DecisionOrderKey,
                BufferLifetime.Step,
                CardTableKeys.DraftCapacity,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);
        }

        /// <summary>
        /// The five owned domains. Each carries schema version 1 and no migration key: a card table's authoritative
        /// state is created with its world, so a version change would need a registered pure migration
        /// (`OwnershipSchedulePipeline` refuses a version above 1 without one), and this slice declares none.
        /// </summary>
        public static IReadOnlyList<StateSlotSpec> Slots()
        {
            return new List<StateSlotSpec>
            {
                // The table's own state: `TableState { ActiveSeat, TurnNumber, Version }` (07 s2.2). It is durable
                // gameplay state, so losing the last supporting contribution preserves it dormant rather than
                // deleting committed history (P-032; 07 s2.4's `PreserveDormant` for the table executor).
                Slot(
                    CardTableKeys.TableSlot,
                    CardTableKeys.TableDomain,
                    CardTableKeys.TableLayout,
                    new[]
                    {
                        CardTableKeys.ActiveSeatField,
                        CardTableKeys.TurnNumberField,
                        CardTableKeys.TableVersionField,
                    },
                    LastSupportPolicy.PreserveDormant),
                Slot(
                    CardTableKeys.SeatSlot,
                    CardTableKeys.SeatDomain,
                    CardTableKeys.SeatLayout,
                    new[]
                    {
                        CardTableKeys.SeatOrdinalField,
                        CardTableKeys.SeatScoreField,
                        CardTableKeys.HandRowsField,
                    },
                    LastSupportPolicy.PreserveDormant),
                Slot(
                    CardTableKeys.CommandDraftSlot,
                    CardTableKeys.CommandDraftDomain,
                    CardTableKeys.CommandDraftLayout,
                    new[] { CardTableKeys.CommandDraftField },
                    LastSupportPolicy.PreserveDormant),
                // A draft is step-scoped derived data: it is disposable, and losing its support must not preserve
                // a stale decision as if it were state (P-032, P-019).
                Slot(
                    CardTableKeys.DecisionDraftSlot,
                    CardTableKeys.DecisionDraftDomain,
                    CardTableKeys.DecisionDraftLayout,
                    new[] { CardTableKeys.DecisionDraftField },
                    LastSupportPolicy.RemoveDerived),
                Slot(
                    CardTableKeys.OutputSlot,
                    CardTableKeys.OutputDomain,
                    CardTableKeys.OutputLayout,
                    new[] { CardTableKeys.OutputField },
                    LastSupportPolicy.PreserveDormant),
            };
        }

        /// <summary>One declared stage with its single system and its declared predecessor.</summary>
        private static StageSpec Stage(
            StageId stage,
            string stageStableName,
            IReadOnlyList<StageId>? requiredAfter,
            AccessSet readWriteSet,
            IReadOnlyList<SystemSpec> systems)
        {
            return new StageSpec(
                stage,
                1U,
                OwnerPackage,
                HostAffinity.ManagedMain,
                new List<FactoryKey> { CardIdentity.Key(stageStableName) },
                null,
                readWriteSet,
                null,
                requiredAfter,
                null,
                null,
                systems,
                null);
        }

        /// <summary>One system entry with its declared access set (never empty: undeclared access rejects, P-039).</summary>
        private static SystemSpec System(FactoryKey key, AccessSet access)
        {
            return new SystemSpec(key, SystemMultiplicity.World, access, null, null, null, null);
        }

        /// <summary>One owned state slot: domain, layout, physical fields and its last-support disposition.</summary>
        private static StateSlotSpec Slot(
            SlotId slot,
            SchemaRef domain,
            FactoryKey layout,
            IReadOnlyList<FactoryKey> fields,
            LastSupportPolicy lastSupport)
        {
            var ownership = new List<FieldOwnership>(fields.Count);
            for (int i = 0; i < fields.Count; i++)
            {
                ownership.Add(new FieldOwnership(domain, fields[i].RegistrationKey));
            }

            return new StateSlotSpec(
                slot,
                CardTableKeys.TableOwner,
                domain,
                layout,
                ownership,
                default(FactoryKey),
                default(FactoryKey),
                default(FactoryKey),
                lastSupport,
                default(FactoryKey),
                null);
        }

        private static PluginManifest Manifest(
            PluginTypeId pluginType,
            FactoryKey factoryKey,
            SchemaRef configSchema,
            IReadOnlyList<ServiceExport>? serviceExports,
            IReadOnlyList<ServiceDependency>? serviceDependencies,
            IReadOnlyList<CapabilityContract>? contracts,
            IReadOnlyList<DerivationRule>? rules,
            IReadOnlyList<StateSlotSpec>? slots,
            IReadOnlyList<StageSpec>? stages,
            IReadOnlyList<BufferSpec>? buffers)
        {
            return new PluginManifest(
                pluginType,
                "1.0.0",
                ContentHash.Empty,
                new SupportedProtocolRange(1, 0, 0),
                null,
                configSchema,
                factoryKey,
                serviceExports,
                serviceDependencies,
                contracts,
                rules,
                null,
                slots,
                stages,
                buffers,
                null);
        }

        /// <summary>
        /// Exactly one fixed-width big-endian int32, the canonical scalar of 05 s6. This is the same four bytes
        /// `IntegrationSlotValues.WriteInt32` writes, so a derived slot value and a command payload agree.
        /// </summary>
        public static FrozenPayload WriteInt32(int value)
        {
            uint raw = unchecked((uint)value);
            return new FrozenPayload(new[]
            {
                (byte)(raw >> 24),
                (byte)(raw >> 16),
                (byte)(raw >> 8),
                (byte)raw,
            });
        }

        /// <summary>Reads the one int32 scalar of a 4-byte payload; false for any other length or for null.</summary>
        public static bool TryReadInt32(IReadOnlyList<byte>? payload, out int value)
        {
            value = 0;
            if (payload == null || payload.Count != 4)
            {
                return false;
            }

            uint raw = ((uint)payload[0] << 24)
                | ((uint)payload[1] << 16)
                | ((uint)payload[2] << 8)
                | payload[3];
            value = unchecked((int)raw);
            return true;
        }
    }
}
