// GameCore.Gameplay.Contracts - the authoritative int32 slots of the entities and world plugins (SADR-004).
//
// Owners and slot ids are pure derivations of stable names, so a binder in one package can read another plugin's
// committed slots without a type dependency on it: presentation in gameplay.entities reads the world plugin's pose
// slots through these ids, and Studio reads residency the same way.
//
//   entities plugin (owner gameplay.entities.owner), on every entity target:
//     entity.alive       0/1
//     entity.variant     variant index (0 = the definition itself)
//     entity.scaleMilli  uniform scale in thousandths
//     entity.visible     0/1
//   world plugin (owner gameplay.world.owner), on every region target:
//     world.residency    RegionResidency 0..3
//     world.visits       times a traveller entered the region
//   world plugin, on every entity target:
//     world.region       stable key of the region the entity is in
//     world.posX/posY/posZ  position in millimetres
//     world.yaw          heading in milliradians
//   world plugin, on the world's anchor region target (the first region in ordinal authoring-id order):
//     world.spawnOrdinal runtime spawns so far (the next spawned target's identity derives from it, P1.7a)
//
// P1.7a: world.posX/posY/posZ/yaw is the authoritative pose of every entity - authored props, the player and NPCs
// alike. The player and NPC kernels write it in the step that moves them; their own player.pos* / npc.pos* slots are
// mirrors kept for change detection and are never read for a decision.
//
// The in-step outbox seam (P1.7a, A1) lives here too: IGameplayStepTap is how a kernel tells the world's narrative
// delivery that it committed an event that owes a delivery, and how a destination kernel claims an obligation's
// request id in the step that applies it.
#nullable enable
using GameCore.Contracts;

namespace GameCore.Gameplay.Contracts
{
    /// <summary>Owners and slot ids of the entities and world plugins.</summary>
    public static class GameplaySlots
    {
        public const uint SchemaVersion = 1U;

        public static readonly OwnerId EntityOwner = GameplayIds.Owner("entities.owner");

        public static readonly OwnerId WorldOwner = GameplayIds.Owner("world.owner");

        public static readonly SlotId Alive = SlotNames.Of("entity", "alive");

        public static readonly SlotId Variant = SlotNames.Of("entity", "variant");

        public static readonly SlotId ScaleMilli = SlotNames.Of("entity", "scaleMilli");

        public static readonly SlotId Visible = SlotNames.Of("entity", "visible");

        public static readonly SlotId Residency = SlotNames.Of("world", "residency");

        public static readonly SlotId Visits = SlotNames.Of("world", "visits");

        public static readonly SlotId Region = SlotNames.Of("world", "region");

        public static readonly SlotId PosX = SlotNames.Of("world", "posX");

        public static readonly SlotId PosY = SlotNames.Of("world", "posY");

        public static readonly SlotId PosZ = SlotNames.Of("world", "posZ");

        public static readonly SlotId Yaw = SlotNames.Of("world", "yaw");

        /// <summary>Runtime spawns so far, on the world's anchor region target (P1.7a).</summary>
        public static readonly SlotId SpawnOrdinal = SlotNames.Of("world", "spawnOrdinal");

        /// <summary>The entity slot of a member name used by binder maps ("alive", "variant", "scaleMilli", "visible").</summary>
        public static bool TryEntitySlot(string member, out SlotId slot)
        {
            switch (member)
            {
                case "alive": slot = Alive; return true;
                case "variant": slot = Variant; return true;
                case "scaleMilli": slot = ScaleMilli; return true;
                case "visible": slot = Visible; return true;
                default: slot = default(SlotId); return false;
            }
        }
    }

    /// <summary>
    /// The ids of player.restoreStamina (P1.7a). They live in the contracts so the narrative delivery port (logic package,
    /// which does not reference the player package) can address the route; the player kernel handles it.
    /// </summary>
    public static class PlayerStaminaIds
    {
        public static readonly RouteId RestoreRoute = GameplayIds.Route("player.route.restore-stamina");

        /// <summary>player.restoreStamina: amount, optional request id.</summary>
        public static readonly SchemaRef RestoreCommand = GameplayIds.Schema("player.command.restore-stamina", 1U);

        /// <summary>StaminaRestored: A = amount asked, B = stamina before, C = stamina after.</summary>
        public static readonly SchemaRef RestoredEvent = GameplayIds.Schema("player.event.stamina-restored", 1U);
    }

    /// <summary>How a destination kernel treats a request id it is about to apply (P1.7a, A1).</summary>
    public enum ObligationClaim
    {
        /// <summary>Not an obligation request id: the destination's own request ring (if any) decides.</summary>
        NotObligation = 0,

        /// <summary>An open obligation of this world's outbox: apply it, then <see cref="IGameplayStepTap.Settle"/>.</summary>
        Apply = 1,

        /// <summary>An obligation that is settled (or unknown to this world's outbox): refuse with IdempotencyConflict.</summary>
        AlreadyApplied = 2,
    }

    /// <summary>
    /// The in-step outbox seam of a gameplay world (P1.7a, A1). The world's narrative delivery implements it; kernels hold
    /// it through <see cref="IGameplayStepTapHost"/>. Every call happens inside the executing step, on the main thread.
    /// </summary>
    public interface IGameplayStepTap
    {
        /// <summary>
        /// A kernel committed <paramref name="schema"/>/<paramref name="payload"/> in the executing step. Any delivery the
        /// event owes (an action, a reward, a rule trigger, a quest signal) becomes an outbox obligation in the same step,
        /// so a capture taken after the pump holds it.
        /// </summary>
        void OnCommitted(SchemaRef schema, FrozenPayload payload, OperationId causal);

        /// <summary>
        /// An interactable's action reference owed by a committed use or trigger entry, recorded as an obligation in the
        /// executing step. True when the tap took it (the host-side dispatcher then does not run it again).
        /// </summary>
        bool OnActionDemand(string actionRef, string subjectAuthoringId, int subjectKey, int actorKey, int state, OperationId causal);

        /// <summary>Room for <paramref name="obligations"/> more obligations in the outbox (refuse before mutation).</summary>
        bool HasRoom(int obligations);

        /// <summary>Claims <paramref name="requestId"/> at its destination, in-step.</summary>
        ObligationClaim Claim(int requestId);

        /// <summary>Settles a claimed obligation after the destination committed its effect (acknowledged in-step).</summary>
        void Settle(int requestId);
    }

    /// <summary>A kernel module that commits events or applies obligations through the world's step tap.</summary>
    public interface IGameplayStepTapHost
    {
        IGameplayStepTap? StepTap { get; set; }
    }

    /// <summary>The claim rule every destination kernel applies to a request id (P1.7a, A1).</summary>
    public static class GameplayObligations
    {
        /// <summary>
        /// <see cref="ObligationClaim.NotObligation"/> for a non-obligation id; otherwise the tap's claim, or
        /// AlreadyApplied when the world has no tap (no outbox could have issued the id).
        /// </summary>
        public static ObligationClaim Claim(IGameplayStepTap? tap, int requestId)
        {
            if (!GameplayRequestIds.IsObligation(requestId))
            {
                return ObligationClaim.NotObligation;
            }

            return tap != null ? tap.Claim(requestId) : ObligationClaim.AlreadyApplied;
        }

        /// <summary>Settles an obligation id after its effect committed (no-op for other ids).</summary>
        public static void Settle(IGameplayStepTap? tap, int requestId)
        {
            if (tap != null && GameplayRequestIds.IsObligation(requestId))
            {
                tap.Settle(requestId);
            }
        }
    }

    /// <summary>An authored region as seen from packages that cannot reference the world package.</summary>
    public interface IAuthoredRegion : IAuthoredObject
    {
        /// <summary>True when a world-space point (metres) lies inside the region's bounds.</summary>
        bool ContainsPoint(double x, double y, double z);
    }
}
