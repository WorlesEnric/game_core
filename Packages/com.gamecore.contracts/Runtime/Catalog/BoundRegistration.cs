// GameCore.Contracts - generated catalog binding (GC-003). Normative sources:
// docs/game-core/04-unity-integration.md s8 (the generated registry maps stable ids to direct constructors and
// typed installers) and docs/game-core/05-contracts-and-data-model.md s3 (generated registration keys).
#nullable enable
using System;

namespace GameCore.Contracts
{
    /// <summary>
    /// One generated registration bound to its precompiled implementation. The implementation instance is
    /// created by a direct constructor call in generated code, never by reflection or runtime type lookup
    /// (04 s8). The record is immutable after construction.
    /// </summary>
    /// <typeparam name="TImplementation">The consumer-declared implementation surface, for example a plugin factory or a handler.</typeparam>
    public sealed class BoundRegistration<TImplementation>
        where TImplementation : class
    {
        public BoundRegistration(FactoryKey key, string stableName, TImplementation implementation)
        {
            Key = key;
            StableName = stableName ?? throw new ArgumentNullException(nameof(stableName));
            Implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));
        }

        /// <summary>Generated registration key; the catalog lookup identity of this registration.</summary>
        public FactoryKey Key { get; }

        /// <summary>Stable name the key was derived from, kept for diagnostics that must name a registration.</summary>
        public string StableName { get; }

        /// <summary>The precompiled implementation instance.</summary>
        public TImplementation Implementation { get; }

        public override string ToString() => StableName + "@" + Key;
    }
}
