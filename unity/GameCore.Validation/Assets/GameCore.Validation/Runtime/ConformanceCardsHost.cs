// GameCore.Validation.ProbeHost — the GC-024 card-market conformance host.
//
// `Gc013CardsHost.CardFamily` already declares the whole card slice as a family: its catalog declarations, its
// market scope tree, its live seats and table, the festival/quiet scoring providers it mounts and every composition
// edit the GC-013 sequence submits. This file adds the *GC-024* half — one more partial part of the same class —
// implementing `IConformanceFamily`, so the 07 s2.4 before/after table runs over the same market the earlier gates
// run (P-001).
//
// WHAT THE CARD MARKET NEEDS BEYOND ITS OWN DECLARED SET
//
// Two rows of 07 s2 need a provider the family does not declare, and both are added here as *lane-only* declarations
// (a capability rule and its contract, no stage, buffer or state slot), so the compiled ownership surface every
// earlier gate asserts on is untouched (P-009's "an empty category is explicit"):
//
//   * `07:51`'s nested-retraction row — "a nested festival with `+3` produces `+5` where both providers match, and
//     retraction removes only the departing source's `+2` or `+3` entry". The nested provider must add a SECOND
//     contribution to the same `cards.set-bonus` slot from the seat's own scope, so it is declared with its own rule
//     identity and installation and mounted at `cards.seat-a-scope`;
//   * `07:100`'s reconfiguration row — "the same source contribution now resolves to `+4`". The card package's
//     scoring value is an immutable *declaration* payload (`CardTableDeclarations.SetBonusRule` bakes the bonus into
//     the rule's frozen payload), not configuration, so a new value cannot be applied in place by an O-05
//     reconfigure. The row is executed as the declared-compatible-provider replacement P-025 describes: the same
//     installation identity is unmounted and remounted with the replacement declaration, so the source the binding
//     row names is unchanged while its value becomes `+4`. The intermediate publication (the unmount alone) exists
//     and is not observed by the row, which reads the last published state before and the first after — recorded in
//     the HANDOFF as the one place 07's operation and this package's declaration shape differ.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Gameplay.Cards;
using GameCore.Gameplay.Cards.Fixtures;
using GameCore.Planning;
using GameCore.ReferenceConformance;
using GameCore.Rules.Cards;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using Unity.Entities;

namespace GameCore.Validation.ProbeHost
{
    public static partial class Gc013CardsHost
    {
        /// <summary>Stable name of the conformance run's nested festival provider (07:51's `+3`).</summary>
        private const string ConformanceNestedScoring = "cards.conformance.nested-festival";

        /// <summary>Stable name of the conformance run's replacement festival provider (07:100's `+4`).</summary>
        private const string ConformanceUpperScoring = "cards.conformance.festival-upper";

        /// <summary>Rule-name suffix the conformance providers use, so they never collide with the family's own.</summary>
        private const string ConformanceRuleSuffix = ".set-bonus";

        /// <summary>The card-market half of `IConformanceFamily`: one more partial part of the same family.</summary>
        public sealed partial class CardFamily : IConformanceFamily
        {
            /// <summary>The label every observation of this family's run is qualified with.</summary>
            public string ConformanceLabel => Gc013CardsHost.Label;

            /// <summary>
            /// The two lane-only declarations 07 s2's rows need: the nested `+3` provider and the replacement `+4`
            /// provider. Both declare the same `cards.set-bonus` contract as the family's own festival provider (an
            /// identical contract declaration deduplicates), each with its own rule identity and payload, so the
            /// Additive fold sees three distinct contributions and a retraction can remove exactly one (P-017-P-019).
            /// </summary>
            public IReadOnlyList<CatalogPluginDeclaration> ConformanceDeclarations => conformanceDeclarations;

            private static readonly IReadOnlyList<CatalogPluginDeclaration> conformanceDeclarations =
                new List<CatalogPluginDeclaration>
                {
                    new CatalogPluginDeclaration(
                        CardTableDeclarations.ScoringProvider(
                            CardTableKeys.PluginType(ConformanceNestedScoring),
                            CardTableKeys.PluginFactoryKey,
                            CardTableKeys.ConfigSchema,
                            ConformanceNestedScoring + ConformanceRuleSuffix,
                            CardVocabulary.NestedFestivalBonus),
                        ConfigDocument.Empty),
                    new CatalogPluginDeclaration(
                        CardTableDeclarations.ScoringProvider(
                            CardTableKeys.PluginType(ConformanceUpperScoring),
                            CardTableKeys.PluginFactoryKey,
                            CardTableKeys.ConfigSchema,
                            ConformanceUpperScoring + ConformanceRuleSuffix,
                            ConformanceUpperBonus),
                        ConfigDocument.Empty),
                };

            /// <summary>The value `07:100` reconfigures the festival contribution to.</summary>
            private const int ConformanceUpperBonus = 4;

            private const uint ConformanceSpawnedSeatOrdinal = CardTableKeys.SeatCOrdinal + 1U;

            /// <summary>
            /// Executes one operation key of a `ConformanceScript` against one live card world. Every payload comes
            /// from the card package's own builders (`CardTablePayloads`, `CardLifecyclePayloads`), so the edits are
            /// byte-for-byte the shape the family's own scenarios submit (P-002, P-042).
            /// </summary>
            public ConformanceOperationResult Apply(string operation, int operand, ConformanceWorld world)
            {
                if (world == null)
                {
                    return Unsupported("no world");
                }

                switch (operation)
                {
                    case ConformanceOperations.MountProvider:
                        return world.PublishEdit(
                            CardTablePayloads.Mount(
                                declarations[1].Manifest,
                                CardTableFixture.FestivalScoringInstance,
                                CardIdentity.Scope(CardVocabulary.LeagueA)),
                            "mount-festival");

                    case ConformanceOperations.MountSecondProvider:
                        return world.PublishEdit(
                            CardTablePayloads.Mount(
                                declarations[2].Manifest,
                                CardTableFixture.QuietScoringInstance,
                                CardIdentity.Scope(CardVocabulary.LeagueB)),
                            "mount-quiet");

                    case ConformanceOperations.MountNestedProvider:
                        return world.PublishEdit(
                            CardTablePayloads.Mount(
                                conformanceDeclarations[0].Manifest,
                                CardTableKeys.Instance(ConformanceNestedScoring),
                                CardIdentity.Scope(CardVocabulary.SeatAScope)),
                            "mount-nested-festival");

                    case ConformanceOperations.UnmountProvider:
                        return world.PublishEdit(
                            CardTablePayloads.Unmount(CardTableFixture.FestivalScoringInstance), "unmount-festival");

                    case ConformanceOperations.UnmountSecondProvider:
                        return world.PublishEdit(
                            CardTablePayloads.Unmount(CardTableFixture.QuietScoringInstance), "unmount-quiet");

                    case ConformanceOperations.ReconfigureProvider:
                        return Reconfigure(world, operand);

                    case ConformanceOperations.MountConflictProvider:
                        return world.PublishEdit(
                            CardTablePayloads.Mount(
                                declarations[4].Manifest, DrawPolicyOneInstance, DrawPolicyOneScope),
                            "mount-draw-policy-one");

                    case ConformanceOperations.MountConflictSecondProvider:
                        return world.PublishEdit(
                            CardTablePayloads.Mount(
                                declarations[5].Manifest, DrawPolicyTwoInstance, DrawPolicyTwoScope),
                            "mount-draw-policy-two");

                    case ConformanceOperations.ReparentMovedScope:
                        return world.PublishEdit(
                            CardTablePayloads.ScopeReparent(
                                CardIdentity.Scope(CardVocabulary.SeatAScope),
                                CardIdentity.Scope(CardVocabulary.LeagueB)),
                            "reparent-seat-a");

                    case ConformanceOperations.ModeAutomatic:
                        return SwitchMode(world, PropagationMode.Automatic, "mode-automatic");

                    case ConformanceOperations.ModeConservative:
                        return SwitchMode(world, PropagationMode.Conservative, "mode-conservative");

                    case ConformanceOperations.SuspendProvider:
                        return world.PublishEdit(
                            CardLifecyclePayloads.Suspend(CardTableFixture.FestivalScoringInstance),
                            "suspend-festival");

                    case ConformanceOperations.ResumeProvider:
                        return world.PublishEdit(
                            CardLifecyclePayloads.Resume(CardTableFixture.FestivalScoringInstance),
                            "resume-festival");

                    case ConformanceOperations.SpawnFutureTarget:
                        return SpawnFuture(world, operand);

                    case ConformanceOperations.SeedOptedInTarget:
                        return SeedOptedIn(world);

                    case ConformanceOperations.ApplyExclusion:
                        return ExcludeSeat(world);

                    case ConformanceOperations.CommitCommand:
                        return CommitSets(world, operand);

                    case ConformanceOperations.CommitCommandRejected:
                        return CommitRejected(world);

                    default:
                        return Unsupported("the card market declares no '" + operation + "' operation");
                }
            }

            /// <summary>
            /// Reads one canonical field of `ConformanceFields` out of the live card world. Derived rows come from
            /// the published assembly (P-030), owned state from the card package's own ECS storage (P-034), and the
            /// projections are computed from the same rules the settlement system applies (07 s2.3).
            /// </summary>
            public bool TryReadField(string field, ConformanceWorld world, out string value, out string detail)
            {
                value = ConformanceValue.None;
                detail = string.Empty;
                if (world == null || world.Host == null)
                {
                    detail = "no world";
                    return false;
                }

                if (string.Equals(field, ConformanceFields.WorldMode, StringComparison.Ordinal))
                {
                    value = world.Lane!.Committed.Mode == PropagationMode.Conservative
                        ? "conservative"
                        : "automatic";
                    return true;
                }

                // Derived rows: the effective `cards.set-bonus` value and the installation supporting it.
                if (TryParseSeatField(field, ".bonus", out uint bonusSeat))
                {
                    return TryReadBonus(world, bonusSeat, out value, out detail);
                }

                if (TryParseSeatField(field, ".bonus-provider", out uint providerSeat))
                {
                    return TryReadBonusProvider(world, providerSeat, out value, out detail);
                }

                if (TryParseSeatField(field, ".total", out uint totalSeat))
                {
                    return TryReadSeatScore(world, totalSeat, out value, out detail);
                }

                if (TryParseSeatField(field, ".hand", out uint handSeat))
                {
                    return TryReadHand(world, handSeat, out value, out detail);
                }

                if (TryParseSeatField(field, ".hand-size", out uint sizeSeat))
                {
                    return TryReadHandSize(world, sizeSeat, out value, out detail);
                }

                if (TryParseSeatField(field, ".seated", out uint seatedSeat))
                {
                    return TryReadSeated(world, seatedSeat, out value, out detail);
                }

                if (TryParseSeatField(field, ".next-award", out uint awardSeat))
                {
                    return TryReadNextAward(world, awardSeat, out value, out detail);
                }

                // The complete-opt-in seat (P-013): its own subject, never one of the automatic ordinals, because
                // 07:103/07:104 name it precisely as the target that keeps the contribution in Conservative while
                // every automatically eligible seat loses it.
                if (string.Equals(field, ConformanceFields.OptedInSeatBonus, StringComparison.Ordinal))
                {
                    return TryReadBonusOf(world, OptedInTarget, out value, out detail);
                }

                if (string.Equals(field, ConformanceFields.OptedInSeatBonusProvider, StringComparison.Ordinal))
                {
                    return TryReadBonusProviderOf(world, OptedInTarget, out value, out detail);
                }

                if (string.Equals(field, ConformanceFields.OptedInSeatNextAward, StringComparison.Ordinal))
                {
                    return TryReadNextAwardOf(world, OptedInTarget, out value, out detail);
                }

                if (string.Equals(field, ConformanceFields.TableActiveSeat, StringComparison.Ordinal))
                {
                    return TryReadTable(
                        world, delegate (CardTableState table)
                        {
                            return table.ActiveSeat.ToString(CultureInfo.InvariantCulture);
                        }, out value, out detail);
                }

                if (string.Equals(field, ConformanceFields.TableTurn, StringComparison.Ordinal))
                {
                    return TryReadTable(
                        world, delegate (CardTableState table)
                        {
                            return table.TurnNumber.ToString(CultureInfo.InvariantCulture);
                        }, out value, out detail);
                }

                if (string.Equals(field, ConformanceFields.TableVersion, StringComparison.Ordinal))
                {
                    return TryReadTable(
                        world, delegate (CardTableState table)
                        {
                            return table.TableVersion.ToString(CultureInfo.InvariantCulture);
                        }, out value, out detail);
                }

                detail = "the card market owns no field '" + field + "'";
                return false;
            }

            // ------------------------------------------------------------------ operations

            /// <summary>
            /// 07:100's reconfiguration. The replacement declaration carries the new value under the SAME installation
            /// identity, so the derived row's provenance is the same source and the seat's committed total survives
            /// (P-025, P-032). Both halves must publish for the row to be observed.
            /// </summary>
            private ConformanceOperationResult Reconfigure(ConformanceWorld world, int operand)
            {
                int replacement = operand > 0 ? operand : ConformanceUpperBonus;
                if (replacement != ConformanceUpperBonus)
                {
                    return Unsupported(
                        "the conformance replacement declaration carries +" + ConformanceUpperBonus
                        + ", so a reconfiguration to +" + replacement
                        + " has no declaration to resolve (P-009: a mount resolves a manifest the catalog registered)");
                }

                ConformanceOperationResult unmounted = world.PublishEdit(
                    CardTablePayloads.Unmount(CardTableFixture.FestivalScoringInstance), "reconfigure-retract");
                if (!unmounted.Published)
                {
                    return unmounted;
                }

                ConformanceOperationResult mounted = world.PublishEdit(
                    CardTablePayloads.Mount(
                        conformanceDeclarations[1].Manifest,
                        CardTableFixture.FestivalScoringInstance,
                        CardIdentity.Scope(CardVocabulary.LeagueA)),
                    "reconfigure-mount");
                return mounted;
            }

            /// <summary>
            /// 07:103/07:104/07:108's mode edits, published through the card package's own `ModeSet` payload. The
            /// lane this world owns carries no published-consequence validator of its own, so the kernel's
            /// `DerivationModeSwitchValidator` is consulted here on the proposed composition first: a switch whose
            /// closure the kernel refuses (07:108's unresolved `Exclusive` pair) is refused before anything is
            /// staged, which keeps the old mode and the old assembly exactly as P-014 demands.
            /// </summary>
            private ConformanceOperationResult SwitchMode(
                ConformanceWorld world, PropagationMode mode, string label)
            {
                if (world.Lane == null || world.Targets == null)
                {
                    return Unsupported(label + ": the world or its composition lane is missing");
                }

                CompositionState before = world.Lane.Committed;
                CompositionState after = before.With(mode: mode);
                var validator = new DerivationModeSwitchValidator(values, () => TargetView(world));
                EditValidationResult check = validator.Validate(
                    before, after, CompositionChangeSet.Of(before, after));
                if (!check.Accepted)
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Refused,
                        label + ": the mode switch was refused (" + DiagnosticCodeText.Of(check.Code)
                        + ": " + check.Detail + ")");
                }

                return world.PublishEdit(CardTablePayloads.ModeSet(mode), label);
            }

            /// <summary>
            /// The live target view the mode-switch validator derives the proposed composition over: the same
            /// `LiveTargetIndex` the pipeline itself derives with, read lazily so a target spawned since the lane
            /// opened is part of the check (P-024).
            /// </summary>
            private static IReadOnlyList<DerivationTarget> TargetView(ConformanceWorld world)
            {
                if (world.Targets == null)
                {
                    return Array.Empty<DerivationTarget>();
                }

                DerivationInputTargets view = world.Targets.BuildDerivationTargets();
                return view.Succeeded ? view.Targets : Array.Empty<DerivationTarget>();
            }

            /// <summary>
            /// P-024's spawn: a neutral publication first (a scope no live target lives in), then the spawn itself, so
            /// the new seat becomes visible fully assembled before its first step, with the same derived `+2` the
            /// existing seats have (07:99). The seat's ordinal is the one its target identity names, so the two agree.
            /// </summary>
            private ConformanceOperationResult SpawnFuture(ConformanceWorld world, int operand)
            {
                if (operand != 0)
                {
                    return Unsupported(
                        "the card market declares exactly one future seat; operand " + operand
                        + " names no declared recipe (P-009)");
                }

                if (world.Pipeline == null || world.Targets == null)
                {
                    return Unsupported("the world or its pipeline is missing");
                }

                TargetId target = FutureTarget;
                ScopeId scope = FutureScope;
                DefinitionRef recipe = FutureRecipe;
                // FutureScope is the SeatB branch below LeagueA, so 07's spawned D inherits in Automatic and
                // loses the descendant reach in Conservative just like the existing non-opted-in seats.
                // GC-013's default future ordinal is not 07's seat D; publish the spawn with the ordinal its
                // target name denotes, instead of reinstalling already-published ECS storage after the fence.
                seatApplier.NextOrdinal = ConformanceSpawnedSeatOrdinal;
                seatApplier.InitialScore = CardTableKeys.SeededSeatScore;

                ConformanceOperationResult neutral = world.StageNeutralPublication(
                    SpareScopeEdits[0], "spawn-neutral-publication");
                if (!neutral.Published)
                {
                    return neutral;
                }

                DerivedAssemblyReport spawn = world.Pipeline.PublishSpawn(
                    world.NextOperation(), target, recipe, scope);
                if (spawn.Outcome != DerivedAssemblyOutcome.Published)
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Refused,
                        "the spawn of " + target.ToString() + " was refused: " + spawn.Describe());
                }

                if (!world.Targets.TryRegister(target, scope, recipe, out DiagnosticCode code, out string detail))
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Refused,
                        "the spawned target could not be registered: " + code + ": " + detail);
                }

                if (!world.Seeder!.TryGetEntity(target, out Entity _))
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Refused,
                        "the spawned target has no live entity (P-005)");
                }

                return new ConformanceOperationResult(
                    ConformanceOperationOutcome.Published,
                    "spawned " + target.ToString() + " fully assembled (P-024)");
            }

            /// <summary>
            /// Seeds and publishes the complete-opt-in seat exactly as the family's own sequence does: the descriptor
            /// is immutable assembly input (P-015), so it cannot be published as a later edit.
            /// </summary>
            private ConformanceOperationResult SeedOptedIn(ConformanceWorld world)
            {
                if (world.Host == null || world.Targets == null || world.Seeder == null)
                {
                    return Unsupported("the world or its target index is missing");
                }

                if (!SeedOptedInTarget(new Gc013WorldContext(world.Host, world.Targets, world.Seeder)))
                {
                    return Unsupported("seeding the opted-in seat was refused");
                }

                // The same mode-direction world also spawns seat D, and SpawnFuture consumes SpareScopeEdits[0] as
                // its neutral NoTargetChange publication. The opted-in setup therefore publishes the other spare.
                return world.PublishEdit(SpareScopeEdits[1], "seed-opted-in-publication");
            }

            /// <summary>
            /// P-016's exclusion on one target: a scope-isolation edit on the seat's own scope declaring one
            /// capability exclusion of that scope. A scope-stored exclusion with no target selector applies to the
            /// scope it is stored on (never to its subtree), and `SeatB`'s scope holds exactly the one seat the row
            /// names, so the rule excludes `cards.set-bonus` for seat B alone. The scope's existing isolation sets
            /// and exclusion rules are read back from the committed composition, so the edit changes only what it
            /// says it changes.
            /// </summary>
            private ConformanceOperationResult ExcludeSeat(ConformanceWorld world)
            {
                if (world.Lane == null)
                {
                    return Unsupported("the world has no composition lane");
                }

                ScopeId scope = CardIdentity.Scope(CardVocabulary.SeatBScope);
                if (!world.Lane.Committed.Scopes.TryGet(scope, out ScopeRecord? record) || record == null)
                {
                    return Unsupported("the seat's scope is not part of the committed composition");
                }

                var exclusions = new List<ExclusionRule>(record.Exclusions)
                {
                    new ExclusionRule(
                        ExclusionTargetKind.Capability,
                        CardVocabulary.SetBonusCapability.Value,
                        scope,
                        default(TargetId),
                        false),
                };

                var payload = new CompositionEditPayload(
                    CompositionEditSubject.ScopeIsolation,
                    scope,
                    record.Parent,
                    false,
                    record.ServiceIsolation,
                    record.CapabilityIsolation,
                    exclusions,
                    null,
                    default(PluginTypeId),
                    default(PluginInstanceId),
                    DefinitionRevision.Zero,
                    ContentHash.Empty,
                    null,
                    0,
                    null,
                    PropagationMode.Automatic);

                return world.PublishEdit(payload, "exclude-seat-b");
            }

            /// <summary>
            /// Submits the card family's own settlement command `operand` times, pumping one command-driven step after
            /// each, so every accepted set commits exactly one logical step (P-036, P-042). The payload is the shape
            /// 07 s2.3's example names: seat A plays its first three held cards against the seeded table version.
            /// </summary>
            private ConformanceOperationResult CommitSets(ConformanceWorld world, int operand)
            {
                int copies = operand <= 0 ? 1 : operand;
                ConformanceOperationResult last = Unsupported("no command was submitted");
                for (int i = 0; i < copies; i++)
                {
                    if (!TrySubmitOneSet(world, i, out ConformanceOperationResult result))
                    {
                        return result;
                    }

                    last = result;
                }

                return last;
            }

            /// <summary>One settlement: the three seeded cards of the active seat against the live table version.</summary>
            private bool TrySubmitOneSet(
                ConformanceWorld world, int ordinal, out ConformanceOperationResult result)
            {
                result = Unsupported("the world is missing");
                if (world.Host == null || world.Seeder == null)
                {
                    return false;
                }

                TargetId table = CardIdentity.Target(CardVocabulary.TableOne);
                if (!world.Seeder.TryGetEntity(table, out Entity tableEntity))
                {
                    result = Unsupported("the market table is not a live target");
                    return false;
                }

                CardTableState state = CardTableAccess.ReadTable(
                    world.Host.EntityWorld.EntityManager, tableEntity);
                if (state.IsClosed)
                {
                    result = new ConformanceOperationResult(
                        ConformanceOperationOutcome.Refused, "the table is closed, so it accepts no settlement");
                    return false;
                }

                uint activeSeat = state.ActiveSeat;
                CardId heldFirst = CardTableKeys.SeatCard(activeSeat, ordinal * CardSetRules.SetCardCount);
                CardId heldSecond = CardTableKeys.SeatCard(activeSeat, (ordinal * CardSetRules.SetCardCount) + 1);
                CardId heldThird = CardTableKeys.SeatCard(activeSeat, (ordinal * CardSetRules.SetCardCount) + 2);
                if (!HoldsCard(world, activeSeat, heldFirst)
                    || !HoldsCard(world, activeSeat, heldSecond)
                    || !HoldsCard(world, activeSeat, heldThird))
                {
                    result = new ConformanceOperationResult(
                        ConformanceOperationOutcome.Refused,
                        "seat ordinal " + activeSeat.ToString(CultureInfo.InvariantCulture)
                        + " does not hold the three cards this set names (07 s2.3's conservation check)");
                    return false;
                }

                var payload = new CardCommandPayload(
                    CardCommandKind.SubmitSet,
                    activeSeat,
                    activeSeat,
                    state.TableVersion,
                    heldFirst,
                    heldSecond,
                    heldThird);
                var envelope = new CommandEnvelope(
                    world.NextOperation(),
                    CardTableKeys.CommandRoute,
                    table,
                    CardTableKeys.CommandSchema,
                    null,
                    CardPayloadCodec.WriteCommand(payload));
                result = world.SubmitAndPump(envelope, "submit-set");
                return true;
            }

            /// <summary>
            /// A set the rules must refuse: the request names a card the active seat does not hold, so
            /// `cards.commit` writes nothing and the table version does not move (P-044, TEST-013).
            /// </summary>
            private ConformanceOperationResult CommitRejected(ConformanceWorld world)
            {
                if (world.Host == null || world.Seeder == null)
                {
                    return Unsupported("the world is missing");
                }

                TargetId table = CardIdentity.Target(CardVocabulary.TableOne);
                if (!world.Seeder.TryGetEntity(table, out Entity tableEntity))
                {
                    return Unsupported("the market table is not a live target");
                }

                CardTableState state = CardTableAccess.ReadTable(world.Host.EntityWorld.EntityManager, tableEntity);
                var payload = new CardCommandPayload(
                    CardCommandKind.SubmitSet,
                    state.ActiveSeat,
                    state.ActiveSeat,
                    state.TableVersion,
                    CardTableKeys.MarketCard(0),
                    CardTableKeys.MarketCard(1),
                    CardTableKeys.MarketCard(2));
                var envelope = new CommandEnvelope(
                    world.NextOperation(),
                    CardTableKeys.CommandRoute,
                    table,
                    CardTableKeys.CommandSchema,
                    null,
                    CardPayloadCodec.WriteCommand(payload));
                return world.SubmitAndPump(envelope, "submit-rejected-set");
            }

            // ------------------------------------------------------------------ readings

            /// <summary>
            /// The effective `cards.set-bonus` value of one seat, read from the published assembly: the active
            /// binding row the slot composes into, or `none` when nothing supports it (P-030, P-019).
            /// </summary>
            private bool TryReadBonus(ConformanceWorld world, uint ordinal, out string value, out string detail)
                => TryReadBonusOf(world, SeatTargetOf(ordinal), out value, out detail);

            /// <summary>
            /// The same effective `cards.set-bonus` reading for an arbitrary live target, which is what the
            /// complete-opt-in seat needs: its identity is its own (P-013), not one of the automatic seat ordinals.
            /// </summary>
            private bool TryReadBonusOf(ConformanceWorld world, TargetId target, out string value, out string detail)
            {
                value = ConformanceValue.None;
                detail = string.Empty;
                if (world.Publisher == null)
                {
                    detail = "the world publishes no assembly";
                    return false;
                }

                IReadOnlyList<CapabilityBinding> rows = world.Publisher.ReadBindingRows(target);
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].IsActive
                        && rows[i].OutputSlot == 0U
                        && rows[i].Capability.Equals(CardVocabulary.SetBonusCapability))
                    {
                        value = rows[i].Value.ToString(CultureInfo.InvariantCulture);
                        return true;
                    }
                }

                detail = "no cards.set-bonus row is published for " + target.ToString();
                return false;
            }

            /// <summary>The stable name of the installation supporting one seat's scoring row (P-017).</summary>
            private bool TryReadBonusProvider(
                ConformanceWorld world, uint ordinal, out string value, out string detail)
                => TryReadBonusProviderOf(world, SeatTargetOf(ordinal), out value, out detail);

            /// <summary>
            /// The same supporting-installation reading for an arbitrary live target (P-013, P-017): the
            /// complete-opt-in seat's row is read by its own identity, never through a seat ordinal.
            /// </summary>
            private bool TryReadBonusProviderOf(
                ConformanceWorld world, TargetId target, out string value, out string detail)
            {
                value = ConformanceValue.None;
                detail = string.Empty;
                if (world.Publisher == null)
                {
                    detail = "the world publishes no assembly";
                    return false;
                }

                IReadOnlyList<CapabilityBinding> rows = world.Publisher.ReadBindingRows(target);
                for (int i = 0; i < rows.Count; i++)
                {
                    if (!rows[i].IsActive
                        || rows[i].OutputSlot != 0U
                        || !rows[i].Capability.Equals(CardVocabulary.SetBonusCapability))
                    {
                        continue;
                    }

                    // The provider identity is compared against this package's own installation identities, so the
                    // label is the catalog name and never a hex id (P-004).
                    if (rows[i].Provider.Value.Equals(CardTableKeys.Instance(CardVocabulary.FestivalScoring).Value))
                    {
                        value = CardVocabulary.FestivalScoring;
                        return true;
                    }

                    if (rows[i].Provider.Value.Equals(CardTableKeys.Instance(CardVocabulary.QuietScoring).Value))
                    {
                        value = CardVocabulary.QuietScoring;
                        return true;
                    }

                    if (rows[i].Provider.Value.Equals(CardTableKeys.Instance(ConformanceNestedScoring).Value))
                    {
                        value = ConformanceNestedScoring;
                        return true;
                    }

                    if (rows[i].Provider.Value.Equals(CardTableKeys.Instance(ConformanceUpperScoring).Value))
                    {
                        value = ConformanceUpperScoring;
                        return true;
                    }

                    value = "<unrecognised-installation>";
                    detail = "the scoring row names an installation this run does not declare";
                    return false;
                }

                detail = "no cards.set-bonus row is published for " + target.ToString();
                return false;
            }

            /// <summary>`SeatScore { Total }`, read from the seat component its single owner writes (P-034).</summary>
            private bool TryReadSeatScore(ConformanceWorld world, uint ordinal, out string value, out string detail)
            {
                value = ConformanceValue.None;
                detail = string.Empty;
                if (!TrySeatEntity(world, ordinal, out Entity seat, out detail))
                {
                    return false;
                }

                CardSeatState state = world.Host!.EntityWorld.EntityManager.GetComponentData<CardSeatState>(seat);
                value = state.Score.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            /// <summary>The seat's held cards as a canonical ascending set of `c&lt;id&gt;` tokens (07 s2.2).</summary>
            private bool TryReadHand(ConformanceWorld world, uint ordinal, out string value, out string detail)
            {
                value = ConformanceValue.None;
                detail = string.Empty;
                if (!TrySeatEntity(world, ordinal, out Entity seat, out detail))
                {
                    return false;
                }

                CardHand hand = CardTableAccess.ReadHand(world.Host!.EntityWorld.EntityManager, seat, ordinal);
                var cards = new List<string>(hand.Count);
                for (int i = 0; i < hand.Count; i++)
                {
                    cards.Add("c" + hand.Card(i).Value.ToString(CultureInfo.InvariantCulture));
                }

                cards.Sort(StringComparer.Ordinal);
                value = ConformanceValue.Set(cards);
                return true;
            }

            /// <summary>The seat's held card count, which is the conservation half of a settlement (P-044).</summary>
            private bool TryReadHandSize(ConformanceWorld world, uint ordinal, out string value, out string detail)
            {
                value = ConformanceValue.None;
                detail = string.Empty;
                if (!TrySeatEntity(world, ordinal, out Entity seat, out detail))
                {
                    return false;
                }

                CardHand hand = CardTableAccess.ReadHand(world.Host!.EntityWorld.EntityManager, seat, ordinal);
                value = hand.Count.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            /// <summary>Whether the seat is seated and therefore accepts commands (07 s2.2).</summary>
            private bool TryReadSeated(ConformanceWorld world, uint ordinal, out string value, out string detail)
            {
                value = ConformanceValue.None;
                detail = string.Empty;
                if (!TrySeatEntity(world, ordinal, out Entity seat, out detail))
                {
                    return false;
                }

                CardSeatState state = world.Host!.EntityWorld.EntityManager.GetComponentData<CardSeatState>(seat);
                value = ConformanceValue.Bool(state.Seated != 0);
                return true;
            }

            /// <summary>
            /// 07 s2.3's "their next valid sets award 12": the base set score plus the effective bonus, computed
            /// through the rules package's own delta rather than restated here (P-019).
            /// </summary>
            private bool TryReadNextAward(ConformanceWorld world, uint ordinal, out string value, out string detail)
                => TryReadNextAwardOf(world, SeatTargetOf(ordinal), out value, out detail);

            /// <summary>The same award projection for an arbitrary live target (07 s2.3, P-013, P-019).</summary>
            private bool TryReadNextAwardOf(ConformanceWorld world, TargetId target, out string value, out string detail)
            {
                value = ConformanceValue.None;
                detail = string.Empty;
                if (!TryReadBonusOf(world, target, out string bonusToken, out detail))
                {
                    value = CardSetRules.BaseSetScore.ToString(CultureInfo.InvariantCulture);
                    detail = "no contribution supports this target, so the next set awards the base score";
                    return true;
                }

                if (!int.TryParse(bonusToken, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int bonus))
                {
                    detail = "the effective bonus '" + bonusToken + "' is not an integer";
                    return false;
                }

                value = (CardSetRules.BaseSetScore + bonus).ToString(CultureInfo.InvariantCulture);
                return true;
            }

            /// <summary>Reads one value off the market table's own `CardTableState` (07 s2.2).</summary>
            private bool TryReadTable(
                ConformanceWorld world,
                Func<CardTableState, string> read,
                out string value,
                out string detail)
            {
                value = ConformanceValue.None;
                detail = string.Empty;
                if (world.Host == null || world.Seeder == null)
                {
                    detail = "the world is missing";
                    return false;
                }

                TargetId table = CardIdentity.Target(CardVocabulary.TableOne);
                if (!world.Seeder.TryGetEntity(table, out Entity entity))
                {
                    detail = "the market table is not a live target";
                    return false;
                }

                value = read(CardTableAccess.ReadTable(world.Host.EntityWorld.EntityManager, entity));
                return true;
            }

            private bool TrySeatEntity(
                ConformanceWorld world, uint ordinal, out Entity seat, out string detail)
            {
                seat = Entity.Null;
                detail = string.Empty;
                if (world.Seeder == null)
                {
                    detail = "the world has no target seeder";
                    return false;
                }

                TargetId target = SeatTargetOf(ordinal);
                if (!world.Seeder.TryGetEntity(target, out seat) || seat == Entity.Null)
                {
                    detail = target.ToString() + " is not a live target of this world (P-005)";
                    return false;
                }

                if (!world.Host!.EntityWorld.EntityManager.HasComponent<CardSeatState>(seat))
                {
                    detail = target.ToString() + " carries no card seat storage";
                    return false;
                }

                return true;
            }

            private bool HoldsCard(ConformanceWorld world, uint ordinal, CardId card)
            {
                if (!TrySeatEntity(world, ordinal, out Entity seat, out string _))
                {
                    return false;
                }

                CardHand hand = CardTableAccess.ReadHand(world.Host!.EntityWorld.EntityManager, seat, ordinal);
                return hand.Holds(card);
            }

            private static ConformanceOperationResult Unsupported(string detail)
                => new ConformanceOperationResult(ConformanceOperationOutcome.Unsupported, detail);

            private static bool TryParseSeatField(string field, string suffix, out uint ordinal)
            {
                ordinal = 0U;
                if (!field.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return false;
                }

                string subject = field.Substring(0, field.Length - suffix.Length);
                for (uint candidate = CardTableKeys.SeatAOrdinal; candidate <= CardTableKeys.SeatCOrdinal; candidate++)
                {
                    if (string.Equals(ConformanceFields.Seat(candidate), subject, StringComparison.Ordinal))
                    {
                        ordinal = candidate;
                        return true;
                    }
                }

                // 07:99's spawned seat D and 07:103's future descendants are named by their own ordinals.
                if (string.Equals(ConformanceFields.Seat(3U), subject, StringComparison.Ordinal))
                {
                    ordinal = 3U;
                    return true;
                }

                if (string.Equals("practice-seat", subject, StringComparison.Ordinal))
                {
                    ordinal = CardTableKeys.PracticeOrdinal;
                    return true;
                }

                return false;
            }

            /// <summary>
            /// The target identity one seat ordinal reads: the market's automatic seats and the spawned seat D
            /// through the fixture's own mapping, and the practice seat through its own stable identity, because the
            /// fixture maps every ordinal past C to seat D and the practice seat's ordinal is deliberately not seat
            /// D's (P-008, 07 s2.1).
            /// </summary>
            private static TargetId SeatTargetOf(uint ordinal) =>
                ordinal == CardTableKeys.PracticeOrdinal
                    ? CardIdentity.Target(CardVocabulary.PracticeSeat)
                    : CardTableFixture.SeatTarget(ordinal);
        }
    }
}
