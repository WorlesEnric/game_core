// GameCore.Contracts - generated-serializer base class (GC-003). Normative source:
// docs/game-core/05-contracts-and-data-model.md s6, 04-unity-integration.md s8. A generated serializer subclass
// supplies only its registration key, its schema, its declared feature set and its field table; the header,
// required-feature gate, declared-field walk and checksum rules are enforced once, here.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Contracts
{
    /// <summary>Base of every generated serializer: schema identity plus document validation.</summary>
    public abstract class GeneratedSerializerBase : ISchemaSerializer
    {
        private readonly Id128[] knownFeatureIds;

        protected GeneratedSerializerBase(FactoryKey key, SchemaRef schema, IReadOnlyList<Id128>? knownFeatureIds)
        {
            Key = key;
            Schema = schema;
            this.knownFeatureIds = CatalogOrdering.SortIds(knownFeatureIds);
        }

        /// <summary>Generated registration key of this serializer; the catalog schema entry points at it.</summary>
        public FactoryKey Key { get; }

        /// <summary>Exact schema/version this serializer accepts.</summary>
        public SchemaRef Schema { get; }

        /// <summary>Feature ids this build knows; an unknown required feature rejects before the body is read (P-055).</summary>
        public IReadOnlyList<Id128> KnownFeatureIds => Array.AsReadOnly(knownFeatureIds);

        /// <summary>Declared fields of this schema in ascending field-id order (05 s6).</summary>
        protected abstract IReadOnlyList<GeneratedFieldSlot> DeclaredFields { get; }

        /// <summary>Validates one document without producing a value; a false result reports the exact error.</summary>
        public bool TryValidate(byte[] document, out EnvelopeError error) =>
            TryReadDeclaredFields(document, new GeneratedFieldBuffer(), out error);

        /// <summary>
        /// Validates the document and records every present declared field into <paramref name="buffer"/>. The
        /// generated deserializer then re-reads each recorded field through <see cref="EnvelopeReader.TrySeekTo"/>.
        /// </summary>
        protected bool TryReadDeclaredFields(byte[] document, GeneratedFieldBuffer buffer, out EnvelopeError error) =>
            GeneratedEnvelopeReader.TryReadDeclaredFields(document, Schema, KnownFeatureIds, DeclaredFields, buffer, out error);

        /// <summary>Creates a canonical writer for this schema, declaring this build's feature ids in the header.</summary>
        protected EnvelopeWriter CreateWriter() => new EnvelopeWriter(new EnvelopeHeader(1, 0, Schema, KnownFeatureIds));
    }
}
