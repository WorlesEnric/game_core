// Test-only reference seam for the shared GameCore.Contracts surface (see TestOnlyMarker.cs).
// Immutable validated plan from docs/game-core/05-contracts-and-data-model.md s4. A ChangePlan holds
// no captured writable world access, no stale component pointers and no plugin closure delegates.
#nullable enable

namespace GameCore.Contracts
{
    /// <summary>
    /// Validated composition plan. Hashing uses semantic inputs, canonical ids, revisions and generated
    /// handler keys; timestamps and object addresses are excluded (05 s4, P-028).
    /// </summary>
    public sealed class ChangePlan
    {
        public ChangePlan(
            OperationId operation,
            ContentHash inputHash,
            CompositionRevision baseRevision,
            ContentHash catalogHash,
            ContentHash planHash,
            CompositionDelta composition,
            DerivationDelta derivation,
            RuntimeDelta runtime,
            ResourceStaging resources,
            ValidityAndCost validity)
        {
            Operation = operation;
            InputHash = inputHash;
            BaseRevision = baseRevision;
            CatalogHash = catalogHash;
            PlanHash = planHash;
            Composition = composition;
            Derivation = derivation;
            Runtime = runtime;
            Resources = resources;
            Validity = validity;
        }

        public OperationId Operation { get; }

        public ContentHash InputHash { get; }

        public CompositionRevision BaseRevision { get; }

        public ContentHash CatalogHash { get; }

        public ContentHash PlanHash { get; }

        public CompositionDelta Composition { get; }

        public DerivationDelta Derivation { get; }

        public RuntimeDelta Runtime { get; }

        public ResourceStaging Resources { get; }

        public ValidityAndCost Validity { get; }
    }
}
