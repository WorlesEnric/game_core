// GameCore.Rules.Gameplay.Entities - pure transitions of an authored entity's state (P1.1).
//
// An entity's authoritative state is four int32 slots owned by the entities plugin: alive (0/1), variant (index into
// its definition's variants), scaleMilli (thousandths) and visible (0/1). Every transition here is a pure function:
// it either returns the next state or a refusal, and a refused transition changes nothing. Despawn is logical: the
// variant, scale and visibility survive it, so a respawn restores the entity exactly as it was overridden.
#nullable enable
namespace GameCore.Rules.Gameplay.Entities
{
    /// <summary>The four authoritative slots of one entity.</summary>
    public readonly struct EntityState
    {
        public EntityState(int alive, int variant, int scaleMilli, int visible)
        {
            Alive = alive;
            Variant = variant;
            ScaleMilli = scaleMilli;
            Visible = visible;
        }

        public int Alive { get; }

        public int Variant { get; }

        public int ScaleMilli { get; }

        public int Visible { get; }

        public bool IsAlive => Alive != 0;

        public bool IsVisible => Visible != 0;

        public EntityState WithAlive(bool alive) => new EntityState(alive ? 1 : 0, Variant, ScaleMilli, Visible);

        public EntityState WithVariant(int variant) => new EntityState(Alive, variant, ScaleMilli, Visible);

        public EntityState WithScale(int scaleMilli) => new EntityState(Alive, Variant, scaleMilli, Visible);

        public EntityState WithVisible(bool visible) => new EntityState(Alive, Variant, ScaleMilli, visible ? 1 : 0);

        public override string ToString() =>
            "entity(alive=" + Alive + ", variant=" + Variant + ", scale=" + ScaleMilli + ", visible=" + Visible + ")";
    }

    /// <summary>Why an entity transition was refused.</summary>
    public enum EntityRefusal
    {
        None = 0,
        AlreadyAlive = 1,
        AlreadyDead = 2,
        NotAlive = 3,
        VariantOutOfRange = 4,
        ScaleOutOfRange = 5,
        Unchanged = 6,
    }

    /// <summary>The result of one transition: the next state, or a refusal and the unchanged state.</summary>
    public readonly struct EntityTransition
    {
        private EntityTransition(EntityRefusal refusal, EntityState state)
        {
            Refusal = refusal;
            State = state;
        }

        public EntityRefusal Refusal { get; }

        public EntityState State { get; }

        public bool Accepted => Refusal == EntityRefusal.None;

        public static EntityTransition Accept(EntityState next) => new EntityTransition(EntityRefusal.None, next);

        public static EntityTransition Refuse(EntityRefusal refusal, EntityState unchanged) => new EntityTransition(refusal, unchanged);

        public override string ToString() => Accepted ? "accepted " + State : "refused " + Refusal;
    }

    /// <summary>Pure transitions of an entity's state.</summary>
    public static class EntityRules
    {
        /// <summary>Smallest authored scale: 0.001.</summary>
        public const int MinScaleMilli = 1;

        /// <summary>Largest authored scale: 100.0.</summary>
        public const int MaxScaleMilli = 100000;

        public const int DefaultScaleMilli = 1000;

        /// <summary>The state of a freshly placed entity.</summary>
        public static EntityState Placed(int variant, int scaleMilli, bool visible, bool alive) =>
            new EntityState(alive ? 1 : 0, variant, scaleMilli, visible ? 1 : 0);

        /// <summary>Spawn: a dead entity becomes alive; its visibility, variant and scale are kept.</summary>
        public static EntityTransition Spawn(EntityState state) => Spawn(state, true);

        /// <summary>Spawn with an explicit visibility override, or null to retain the definition/committed default.</summary>
        public static EntityTransition Spawn(EntityState state, bool? visible)
        {
            if (state.IsAlive)
            {
                return EntityTransition.Refuse(EntityRefusal.AlreadyAlive, state);
            }

            return EntityTransition.Accept(visible.HasValue ? state.WithAlive(true).WithVisible(visible.Value) : state.WithAlive(true));
        }

        /// <summary>Despawn: an alive entity becomes dead. Logical only; every other slot survives.</summary>
        public static EntityTransition Despawn(EntityState state)
        {
            if (!state.IsAlive)
            {
                return EntityTransition.Refuse(EntityRefusal.AlreadyDead, state);
            }

            return EntityTransition.Accept(state.WithAlive(false));
        }

        /// <summary>Selects one of the definition's <paramref name="variantCount"/> variants on an alive entity.</summary>
        public static EntityTransition SetVariant(EntityState state, int variant, int variantCount)
        {
            if (variant < 0 || variant >= (variantCount < 1 ? 1 : variantCount))
            {
                return EntityTransition.Refuse(EntityRefusal.VariantOutOfRange, state);
            }

            if (!state.IsAlive)
            {
                return EntityTransition.Refuse(EntityRefusal.NotAlive, state);
            }

            if (state.Variant == variant)
            {
                return EntityTransition.Refuse(EntityRefusal.Unchanged, state);
            }

            return EntityTransition.Accept(state.WithVariant(variant));
        }

        /// <summary>Shows or hides an entity; a dead entity's visibility may change (it applies on respawn).</summary>
        public static EntityTransition SetVisible(EntityState state, bool visible)
        {
            if (state.IsVisible == visible)
            {
                return EntityTransition.Refuse(EntityRefusal.Unchanged, state);
            }

            return EntityTransition.Accept(state.WithVisible(visible));
        }

        /// <summary>Rescales an entity within [<see cref="MinScaleMilli"/>, <see cref="MaxScaleMilli"/>].</summary>
        public static EntityTransition SetScale(EntityState state, int scaleMilli)
        {
            if (!IsScaleInRange(scaleMilli))
            {
                return EntityTransition.Refuse(EntityRefusal.ScaleOutOfRange, state);
            }

            if (state.ScaleMilli == scaleMilli)
            {
                return EntityTransition.Refuse(EntityRefusal.Unchanged, state);
            }

            return EntityTransition.Accept(state.WithScale(scaleMilli));
        }

        public static bool IsScaleInRange(int scaleMilli) => scaleMilli >= MinScaleMilli && scaleMilli <= MaxScaleMilli;

        /// <summary>True when the entity has a view: it is alive and visible.</summary>
        public static bool IsPresented(EntityState state) => state.IsAlive && state.IsVisible;
    }
}
