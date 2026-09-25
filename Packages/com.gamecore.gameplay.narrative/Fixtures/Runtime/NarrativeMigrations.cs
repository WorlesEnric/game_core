// GameCore.Gameplay.Narrative.Fixtures — the slice's registered migrations and their registry (P-029, P-032).
//
// The conversation domain declares schema version 2 while a target that lived before its chapter covered it holds
// version 1. A version change is never implicit: the plan stages a `Migrate` on bounded scratch, and the migration
// itself is a pure function of the copied value. Two properties matter and are therefore explicit:
//
//   * the migration runs on the planner's *copy*, never on live state, so a refused migration leaves the live value
//     exactly as it was (P-029);
//   * a source value the migration cannot express is refused rather than coerced, so a corrupt value is reported
//     instead of silently becoming a legal conversation node (P-032).
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning;
using GameCore.Planning.Ownership;
using GameCore.Rules.Narrative;

namespace GameCore.Gameplay.Narrative.Fixtures
{
    /// <summary>Registered migration of the conversation node slot: version 1 to 2.</summary>
    public sealed class NarrativeConversationNodeMigration : ISlotMigration
    {
        public FactoryKey Key => NarrativeKeys.ConversationNodeMigration;

        public uint FromVersion => 1U;

        public uint ToVersion => 2U;

        /// <summary>Times the migration body ran; the scenario proves it ran on the copy, not on live state (P-029).</summary>
        public int Invocations { get; private set; }

        public bool TryMigrate(int source, out int migrated)
        {
            Invocations++;
            return NarrativeDialogueRules.TryUpgradeNodeToVersionTwo(source, out migrated);
        }
    }

    /// <summary>Registered migration of the conversation status slot: version 1 to 2.</summary>
    public sealed class NarrativeConversationStatusMigration : ISlotMigration
    {
        public FactoryKey Key => NarrativeKeys.ConversationStatusMigration;

        public uint FromVersion => 1U;

        public uint ToVersion => 2U;

        /// <summary>Times the migration body ran.</summary>
        public int Invocations { get; private set; }

        /// <summary>
        /// A version-1 conversation was never activated by a chapter, so it migrates to the declared idle status; a
        /// status outside the declared domain is refused rather than carried into version 2.
        /// </summary>
        public bool TryMigrate(int source, out int migrated)
        {
            Invocations++;
            migrated = source;
            if (source == NarrativeConversationStatus.Idle
                || source == NarrativeConversationStatus.Requested
                || source == NarrativeConversationStatus.Active
                || source == NarrativeConversationStatus.Closed)
            {
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// The migration registry as the ownership validator sees it: a declared key with a registered version pair, so
    /// `SlotPolicyValidator` can prove a version change has a real migration behind it before any plan is prepared.
    /// </summary>
    public sealed class NarrativeSlotMigrations : ISlotMigrationRegistry
    {
        private readonly SlotMigrationRegistry registry = new SlotMigrationRegistry();

        public NarrativeSlotMigrations()
        {
            registry.Register(
                NarrativeKeys.ConversationNodeMigration,
                new SchemaRef(NarrativeKeys.ConversationDomain.Id, 1U),
                NarrativeKeys.ConversationDomain);
            registry.Register(
                NarrativeKeys.ConversationStatusMigration,
                new SchemaRef(NarrativeKeys.ConversationDomain.Id, 1U),
                NarrativeKeys.ConversationDomain);
        }

        public bool IsRegistered(FactoryKey migrationKey, SchemaRef from, SchemaRef to)
            => registry.IsRegistered(migrationKey, from, to);

        public int Count => registry.Count;
    }
}
