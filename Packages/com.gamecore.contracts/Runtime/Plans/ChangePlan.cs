// GameCore.Contracts - production shared contract type (GC-003). Unity-free: BCL subset only, no
// UnityEngine/Unity.* reference, no runtime reflection and no second ECS facade (01 s1, P-058).
// Normative sources: docs/game-core/00-core-protocols.md and docs/game-core/05-contracts-and-data-model.md.
// The public surface of this assembly is API-compatible with the frozen W0 reference seam
// (tests/GameCore.ReferenceSeams); additions are reviewed in artifacts/gc-003/HANDOFF.md.
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
            AssemblyEpoch baseEpoch,
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
            BaseEpoch = baseEpoch;
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

        /// <summary>Assembly epoch the plan was validated against; publication must match it (P-027, P-030).</summary>
        public AssemblyEpoch BaseEpoch { get; }

        public ContentHash CatalogHash { get; }

        public ContentHash PlanHash { get; }

        public CompositionDelta Composition { get; }

        public DerivationDelta Derivation { get; }

        public RuntimeDelta Runtime { get; }

        public ResourceStaging Resources { get; }

        public ValidityAndCost Validity { get; }
    }
}
