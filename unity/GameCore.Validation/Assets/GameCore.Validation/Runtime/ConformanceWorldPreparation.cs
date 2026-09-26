// GameCore.Validation.ProbeHost — the conformance world-preparation halves of the three genre hosts.
//
// WHY A PREPARATION HOOK EXISTS AT ALL. A 07 before/after row's "Before" column names authoritative gameplay state:
// `07:101` reads a seat's committed total, `07:174` reads a live conversation, `07:242` reads a runner's committed
// crossing. The runner must therefore see that state as the world's own, not as a value an assertion invented — and a
// reader that substituted a default for a slot nobody wrote would make the row pass for the wrong reason (P-032).
//
// The card market and the course ship their own seeding through `SeedTargets` and need nothing here. The narrative
// slice does not, and the reason is structural rather than an oversight: `Gc013NarrativeHost.SeedTargets` seeds the
// chapter's own targets for the GC-013 sequence, and the world-level `QuestLedger` (07 s3.1: "`QuestLedger` stores
// durable facts at the world level") is seeded by the scenarios that need its facts — `W4GateNarrativeHost` seeds it
// through its own policy case, `Gc018NarrativeHost` maps it when it is live. Adding it to the shared `SeedTargets`
// would change every earlier gate's live-target count and therefore their archived observations, so it belongs here,
// in the conformance run's own preparation, where only the GC-024 worlds pay for it.
//
// The narrative half therefore does four things, all of them through the narrative package's own code:
//
//   1. re-registers gate east under the family's complete-opt-in gate recipe before any publication derives, and
//      installs its base layout through the shared quest-gate recipe (the applier keys on the recipe identity, so
//      the opted-in variant itself matches no layout branch): the selector schema stays the quest-gate's own, so the
//      chapter's gate rule still selects the target while `Conservative` grants the binding 07:176 observes
//      (07:176, P-013, P-015);
//   2. installs each declared recipe's base layout on its live target, exactly as `AssemblyPublisher.Spawn` does for
//      a spawned target — the recipe applier `NarrativeRecipeApplier` owns those slots (P-032, P-033) — and restores
//      afterwards the live state the family's own seeding already wrote (the moved target's conversation node): a
//      base layout initializes slots nobody wrote, so a row an owner already seeded keeps its seeded value and
//      version (07:175, P-025, P-032);
//   3. seeds the world-level ledger at the world root with `NarrativeKeys.QuestLedgerRecipe`, which is where its
//      durable fact value/version slots come from (07 s3.2);
//   4. binds the ledger as the module's root entity, because the quest owner writes a fact to the module's root and a
//      default `Entity.Null` is not a live entity (07 s3.2's `narrative.quest` -> `narrative.gates` edge).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Cards;
using GameCore.Gameplay.Narrative;
using GameCore.Gameplay.Narrative.Fixtures;
using GameCore.Gameplay.Traversal;
using GameCore.Rules.Cards;
using GameCore.Rules.Narrative;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using Unity.Entities;
using RulesNarrativeFacts = GameCore.Rules.Narrative.NarrativeFacts;

namespace GameCore.Validation.ProbeHost
{
    public static partial class Gc013CardsHost
    {
        public sealed partial class CardFamily
        {
            /// <summary>
            /// The card market needs no preparation: `SeedTargets` already installs the table's and every seat's
            /// authoritative storage, which is where 07 s2.4's totals, hands and table version live (07 s2.2).
            /// </summary>
            public bool PrepareConformanceWorld(ConformanceWorld world, out string detail)
            {
                detail = string.Empty;
                if (world == null || world.Host == null)
                {
                    detail = "no world to prepare";
                    return false;
                }

                if (!world.Seeder!.TryGetEntity(CardIdentity.Target(CardVocabulary.TableOne), out Entity table))
                {
                    detail = "the market table is not a live target, so 07 s2.4's rows have no state to read (P-005)";
                    return false;
                }

                if (!world.Host.EntityWorld.EntityManager.HasComponent<CardTableState>(table))
                {
                    detail = "the market table carries no card storage, so its settled state is unobservable (P-032)";
                    return false;
                }

                return true;
            }
        }
    }

    public static partial class Gc020TraversalHost
    {
        public sealed partial class CourseFamily
        {
            /// <summary>
            /// The course needs no preparation: `SeedTargets` already installs the runners' pose/velocity storage and
            /// binds the course entity, which is where 07 s4.3's motion and progress state live (07 s4.2).
            /// </summary>
            public bool PrepareConformanceWorld(ConformanceWorld world, out string detail)
            {
                detail = string.Empty;
                if (world == null || world.Host == null)
                {
                    detail = "no world to prepare";
                    return false;
                }

                TraversalModule? module = world.Runtime != null && world.Runtime.Traversal != null
                    ? world.Runtime.Traversal.Module
                    : null;
                if (module == null)
                {
                    detail = "the course's own module is not attached, so its motion state has no owner (P-043)";
                    return false;
                }

                if (module.CourseEntity == Entity.Null)
                {
                    detail = "the course entity is not bound, so 07 s4.3's committed progress is unobservable (P-034)";
                    return false;
                }

                return true;
            }
        }
    }

    public static partial class Gc013NarrativeHost
    {
        public sealed partial class NarrativeFamily
        {
            /// <summary>
            /// Prepares the narrative state 07 s3.3's rows read as their before values: gate east re-registered
            /// under its complete-opt-in recipe, every declared recipe's base layout on its live target with the
            /// already-seeded conversation state restored, the world-level quest ledger with its durable fact slots,
            /// and the ledger bound as the module's root entity. Each step is the narrative package's own declaration
            /// in action; a step that cannot be done reports the reason instead of leaving a slot unobserved
            /// (P-032, P-034).
            /// </summary>
            public bool PrepareConformanceWorld(ConformanceWorld world, out string detail)
            {
                detail = string.Empty;
                if (world == null || world.Host == null || world.Targets == null || world.Seeder == null)
                {
                    detail = "the world or its target index is missing";
                    return false;
                }


                EntityManager entityManager = world.Host.EntityWorld.EntityManager;
                var applier = new NarrativeRecipeApplier();

                // 1. The gate east target carries 07 s3.3's complete target opt-in: the mode rows state the gate is
                //    the one target with an opt-in for its binding, which the bare `QuestGateRecipe` descriptor
                //    (no opt-in) cannot deliver — P-013 denies an un-granted descendant in `Conservative`. The
                //    family's own `SeedTargets` registers the shared recipe, so the conformance preparation
                //    re-registers the same live target with the opted-in variant before any publication derives:
                //    the recipe catalog holds both, the selector schema is the quest-gate's own (so the chapter's
                //    gate rule still selects the target), and the base layout below installs the same storage the
                //    shared recipe's applier installs (P-013, P-015, P-024).
                DiagnosticCode gateCode = DiagnosticCode.None;
                string gateDetail = string.Empty;
                if (!world.Targets.TryRetire(NarrativeKeys.GateEast)
                    || !world.Targets.TryRegister(
                        NarrativeKeys.GateEast,
                        NarrativeKeys.VillageScope,
                        Gc013NarrativeHost.OptedInGateRecipe,
                        out gateCode,
                        out gateDetail))
                {
                    detail = "re-registering gate east with its complete-opt-in recipe was refused: "
                        + gateCode + ": " + gateDetail;
                    return false;
                }

                if (!world.Seeder.TryGetEntity(NarrativeKeys.GateEast, out Entity gate) || gate == Entity.Null)
                {
                    detail = "gate east has no live entity after its re-registration (P-005)";
                    return false;
                }

                // The base layout the package's own applier installs for a gate — the closed decision and the
                // evaluated version — is the same storage the shared recipe installs, so the target's owned state
                // is identical to the one every other gate of this package carries (P-024, P-032).
                SpawnRecipeCatalog packageRecipes = NarrativeRecipes.Catalog(applier);
                if (!packageRecipes.TryResolve(
                        NarrativeKeys.QuestGateRecipe, out SpawnRecipe? gateRecipe, out DiagnosticCode layoutCode)
                    || gateRecipe == null)
                {
                    detail = "the quest-gate recipe is not declared by this package: " + layoutCode;
                    return false;
                }

                applier.ApplyBaseLayout(entityManager, gate, gateRecipe);

                // The moved target's conversation node is real gameplay state its owner already seeded: the layout
                // pass below initializes slots nobody wrote, so the value is snapshotted here and restored after it,
                // keeping both the seeded value and its schema version (P-025, P-032).
                if (!world.Seeder.TryGetEntity(NarrativeKeys.Mara, out Entity mara)
                    || !NarrativeState.TryRead(
                        entityManager,
                        mara,
                        NarrativeKeys.DialogueOwner,
                        NarrativeKeys.ConversationNodeSlot,
                        out int seededNode,
                        out uint seededNodeVersion))
                {
                    detail = "the moved target's conversation node is not live state this world owns (P-005, P-032)";
                    return false;
                }

                // 2. The recipe base layouts. `LiveTargetSeeder.TrySeed` creates the entity, its identity and its
                //    published stamp; the declared recipe's own applier installs the slots this package's owners
                //    write (P-032). `AssemblyPublisher.Spawn` does this for a spawned target, so doing it here keeps
                //    a seeded target and a spawned target carrying the same storage (04 s6, P-024).
                SpawnRecipeCatalog recipes = NarrativeRecipes.Catalog(applier);
                IReadOnlyList<LiveTarget> live = world.Targets.Targets;
                for (int i = 0; i < live.Count; i++)
                {
                    if (!world.Seeder.TryGetEntity(live[i].Target, out Entity entity) || entity == Entity.Null)
                    {
                        detail = "live target " + live[i].Target.ToString()
                            + " has no live entity, so its base layout cannot be installed (P-005)";
                        return false;
                    }

                    if (entityManager.HasComponent<NarrativeTargetMarker>(entity))
                    {
                        // A target the world already laid out (the ledger below, or a second preparation pass) keeps
                        // its storage: re-installing would rewrite state the table has already read (P-033).
                        continue;
                    }

                    if (!recipes.TryResolve(live[i].Recipe, out SpawnRecipe? recipe, out DiagnosticCode code)
                        || recipe == null)
                    {
                        // A target whose recipe this package does not declare is skipped rather than failed: the
                        // narrative slice shares the composition with other families in the combined world.
                        _ = code;
                        continue;
                    }
                    applier.ApplyBaseLayout(entityManager, entity, recipe);
                }

                // The live state the family's own seeding already wrote. A base layout initializes a slot nobody
                //    has written, so the layout pass above re-seeds the moved target's conversation node with the
                //    value and schema version snapshotted before it: the choice that opens the permit conversation is
                //    legal only at the node the conversation actually sits on, and 07:175's "no old node ID is
                //    interpreted in the new graph" reads that same node (07:174, 07:175, P-025, P-032).
                if (!world.Seeder.TrySeedSlot(
                        NarrativeKeys.Mara,
                        NarrativeKeys.DialogueOwner,
                        NarrativeKeys.ConversationNodeSlot,
                        seededNodeVersion,
                        seededNode,
                        out DiagnosticCode restoreCode,
                        out string restoreDetail))
                {
                    detail = "restoring the moved target's conversation node was refused: "
                        + restoreCode + ": " + restoreDetail;
                    return false;
                }

                // 3. The world-level ledger and its durable facts (07 s3.1, s3.2). It is seeded at the world root, so
                //    it is NOT under any chapter provider and its facts outlive a chapter's unload (07:174).
                if (!world.Seeder.TrySeed(
                        NarrativeKeys.QuestLedger,
                        NarrativeKeys.RootScope,
                        NarrativeKeys.QuestLedgerRecipe,
                        out TargetHandle _,
                        out DiagnosticCode seedCode,
                        out string seedDetail))
                {
                    detail = "seeding the world-level quest ledger was refused: " + seedCode + ": " + seedDetail;
                    return false;
                }

                if (!world.Seeder.TryGetEntity(NarrativeKeys.QuestLedger, out Entity ledger) || ledger == Entity.Null)
                {
                    detail = "the quest ledger was seeded but the registry cannot resolve it (P-005)";
                    return false;
                }

                if (recipes.TryResolve(
                        NarrativeKeys.QuestLedgerRecipe, out SpawnRecipe? ledgerRecipe, out DiagnosticCode ledgerCode)
                    && ledgerRecipe != null)
                {
                    applier.ApplyBaseLayout(entityManager, ledger, ledgerRecipe);
                }
                else
                {
                    detail = "the ledger's own recipe is not declared by this package: " + ledgerCode;
                    return false;
                }

                // 4. The module's root entity: `narrative.quest` writes a fact to the trail's root entity, and a
                //    default `Entity.Null` is not a live entity, so the transition would land nowhere (07 s3.2).
                NarrativeModule? module = world.Runtime != null && world.Runtime.Adapters != null
                    ? world.Runtime.Adapters.Narrative
                    : null;
                if (module == null)
                {
                    detail = "the narrative module is not attached, so the prepared facts have no owner (P-043)";
                    return false;
                }

                module.MapTarget(NarrativeKeys.QuestLedger, ledger);
                module.SetRootEntity(ledger);

                // A ledger seeded after the module's own target mapping must also be visible to the derivation: the
                // index already holds it (TrySeed registered it), so the next publication derives its (empty)
                // assembly. Reporting the counts makes that checkable rather than assumed (P-015).
                if (!world.Targets.Contains(NarrativeKeys.QuestLedger))
                {
                    detail = "the ledger is not part of the live target index, so no publication would derive for it";
                    return false;
                }

                _ = RulesNarrativeFacts.InitialValue;
                return true;
            }
        }
    }
}
