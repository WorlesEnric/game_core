#nullable enable
using GameCore.Contracts;
namespace GameCore.Validation.Probe
{
    /// <summary>
    /// Unmanaged payload used as a fully resolved generic argument for the Burst job instantiation
    /// <c>ProbeAggregateJob&lt;ProbeVector3Value&gt;</c>. It deliberately implements no interface so the Burst
    /// generic instantiation depends only on unmanaged fields.
    /// </summary>
    public struct ProbeVector3Value
    {
        public float X;
        public float Y;
        public float Z;

        public ProbeVector3Value(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>Managed-side scalar projection, used by the closed generic handler below.</summary>
    public interface IProbeScalarSource
    {
        int Scalar { get; }
    }

    /// <summary>
    /// Small unmanaged value used as the fully resolved generic argument of the generated closed generic
    /// handler. Integer-only arithmetic keeps the probe result exactly reproducible on any host.
    /// </summary>
    public struct ProbeAmount : IProbeScalarSource
    {
        public int Units;
        public int Scale;

        public ProbeAmount(int units, int scale)
        {
            Units = units;
            Scale = scale;
        }

        public int Scalar => Units * Scale;

        public override string ToString() => "amount(units=" + Units + ", scale=" + Scale + ")";
    }

    /// <summary>
    /// Generated handler surface. Production generated handlers are closed over concrete value types
    /// (docs/game-core/04-unity-integration.md section 8); this is the qualification-minimal form of that shape.
    /// </summary>
    public interface IProbeHandler<TInput, TOutput>
    {
        /// <summary>Generated registration key of this handler, in the production contract shape (P-009).</summary>
        FactoryKey HandlerKey { get; }

        TOutput Handle(TInput input);
    }

    /// <summary>
    /// Closed generic handler whose instantiation must exist in the IL2CPP player because it is only reached
    /// through the generated catalog at runtime.
    /// </summary>
    public sealed class ProbeScalarHandler<TValue> : IProbeHandler<TValue, int>
        where TValue : struct, IProbeScalarSource
    {
        public ProbeScalarHandler(FactoryKey handlerKey)
        {
            HandlerKey = handlerKey;
        }

        public FactoryKey HandlerKey { get; }

        public int Handle(TValue input) => input.Scalar;
    }

    /// <summary>
    /// Explicit AOT root sink. The generated catalog passes each closed generic instantiation here so that a
    /// concrete, non-reflective use site exists for every instantiation the player must contain.
    /// </summary>
    public static class ProbeAotRoots
    {
        /// <summary>Roots a closed generic job/struct instantiation.</summary>
        public static void TrackJob<TJob>(TJob job)
            where TJob : struct
        {
            // Intentionally empty: the closed generic method instantiation at the call site is the root.
        }

        /// <summary>Roots a closed generic handler instantiation and returns its key for diagnostics.</summary>
        public static FactoryKey TrackHandler<TValue>(IProbeHandler<TValue, int> handler)
        {
            if (handler == null)
            {
                throw new System.ArgumentNullException(nameof(handler));
            }

            return handler.HandlerKey;
        }
    }
}
