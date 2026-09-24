// GameCore.Contracts - generated serializer seam (GC-003). Normative source:
// docs/game-core/05-contracts-and-data-model.md s6 and P-054: generated serializers replace reflection-based
// type construction, and a document is only trusted after its header, required features, shape and checksum
// pass.
#nullable enable

namespace GameCore.Contracts
{
    /// <summary>
    /// One generated per-schema serializer binding. A concrete generated class also exposes strongly typed
    /// write/read methods for its own value type; this interface is what a catalog registry needs: the stable
    /// registration key, the schema it serves, and the envelope-level document check (05 s6, P-054).
    /// </summary>
    /// <remarks>
    /// The interface deliberately exposes no value type: <see cref="GameCore.Contracts.ImmutableCatalog"/>
    /// must be able to enumerate, validate and bind serializers without knowing a plugin's generated types, and
    /// a consumer that needs typed access already knows its own generated serializer class name (04 s8).
    /// </remarks>
    public interface ISchemaSerializer
    {
        /// <summary>Generated registration key of this serializer; the catalog's schema entry points at it.</summary>
        FactoryKey Key { get; }

        /// <summary>Exact schema/version this serializer accepts.</summary>
        SchemaRef Schema { get; }

        /// <summary>
        /// Validates one canonical envelope document: magic/version header, the schema and version this
        /// serializer declares, the required-feature gate and the trailing checksum. Non-throwing; a false
        /// result reports the exact envelope error (05 s6, P-055).
        /// </summary>
        bool TryValidate(byte[] document, out EnvelopeError error);
    }
}
