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

    /// <summary>An authored region as seen from packages that cannot reference the world package.</summary>
    public interface IAuthoredRegion : IAuthoredObject
    {
        /// <summary>True when a world-space point (metres) lies inside the region's bounds.</summary>
        bool ContainsPoint(double x, double y, double z);
    }
}
