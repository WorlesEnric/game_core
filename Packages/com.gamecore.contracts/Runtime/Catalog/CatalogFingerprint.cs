// GameCore.Contracts - catalog/content fingerprint (GC-003). Normative sources:
// docs/game-core/00-core-protocols.md P-008 (stable ordering), P-028 (catalog/version compatibility),
// P-053 (checkpoints carry catalog/protocol fingerprints) and 05 s6 (canonical byte order).
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Contracts
{
    /// <summary>
    /// Deterministic fingerprint of a generated catalog. The same declarations always produce the same value
    /// regardless of declaration order, registration timing or machine, so a plan, a checkpoint and a build
    /// report can compare one hash (P-028, P-053).
    /// </summary>
    public static class CatalogFingerprint
    {
        /// <summary>
        /// Exact hash input, embedded in generated output so the value can be reproduced without reading the
        /// implementation. Registration timing, declaration order, paths, timestamps and object addresses are
        /// excluded.
        /// </summary>
        public const string Scope =
            "SHA-256 over, in this fixed order: (1) every registered factory key in canonical ascending order "
            + "as 16-byte big-endian id, 4-byte big-endian key version, 4-byte big-endian factory kind, "
            + "16-byte big-endian owner package id, 16-byte big-endian implementation id, 4-byte big-endian "
            + "contract version; (2) every accepted schema in ascending schema-id order as 16-byte big-endian "
            + "id, 4-byte big-endian schema version, one byte 1 when required and 0 when optional, 16-byte "
            + "big-endian serializer key id, 4-byte big-endian serializer key version, 16-byte big-endian owner "
            + "package id; (3) every supported feature id in ascending order as 16 bytes. Declaration order, "
            + "registration timing, machine paths and timestamps are excluded (P-008, P-028, P-053).";

        /// <summary>Computes the fingerprint of one catalog description; order of the inputs is irrelevant.</summary>
        public static ContentHash Compute(
            IReadOnlyList<FactoryRegistration>? factories,
            IReadOnlyList<SchemaRegistration>? schemas,
            IReadOnlyList<Id128>? supportedFeatureIds)
        {
            FactoryRegistration[] factoryTable = CatalogOrdering.SortFactories(factories);
            SchemaRegistration[] schemaTable = CatalogOrdering.SortSchemas(schemas);
            Id128[] featureTable = CatalogOrdering.SortIds(supportedFeatureIds);

            const int FactoryBytes = Id128.SizeInBytes + 4 + 4 + Id128.SizeInBytes + Id128.SizeInBytes + 4;
            const int SchemaBytes = Id128.SizeInBytes + 4 + 1 + Id128.SizeInBytes + 4 + Id128.SizeInBytes;
            byte[] buffer = new byte[(factoryTable.Length * FactoryBytes) + (schemaTable.Length * SchemaBytes) +
                                     (featureTable.Length * Id128.SizeInBytes)];

            int offset = 0;
            for (int i = 0; i < factoryTable.Length; i++)
            {
                FactoryRegistration registration = factoryTable[i];
                Id128Codec.WriteBigEndian(registration.Key.RegistrationKey, buffer, offset);
                offset += Id128.SizeInBytes;
                offset = WriteUInt32(buffer, offset, registration.Key.KeyVersion);
                offset = WriteUInt32(buffer, offset, (uint)registration.Kind);
                Id128Codec.WriteBigEndian(registration.OwnerPackageId, buffer, offset);
                offset += Id128.SizeInBytes;
                Id128Codec.WriteBigEndian(registration.ImplementationId, buffer, offset);
                offset += Id128.SizeInBytes;
                offset = WriteUInt32(buffer, offset, registration.ContractVersion);
            }

            for (int i = 0; i < schemaTable.Length; i++)
            {
                SchemaRegistration registration = schemaTable[i];
                Id128Codec.WriteBigEndian(registration.Schema.Id.Value, buffer, offset);
                offset += Id128.SizeInBytes;
                offset = WriteUInt32(buffer, offset, registration.Schema.Version);
                buffer[offset] = registration.IsRequired ? (byte)1 : (byte)0;
                offset++;
                Id128Codec.WriteBigEndian(registration.Serializer.RegistrationKey, buffer, offset);
                offset += Id128.SizeInBytes;
                offset = WriteUInt32(buffer, offset, registration.Serializer.KeyVersion);
                Id128Codec.WriteBigEndian(registration.OwnerPackageId, buffer, offset);
                offset += Id128.SizeInBytes;
            }

            for (int i = 0; i < featureTable.Length; i++)
            {
                Id128Codec.WriteBigEndian(featureTable[i], buffer, offset);
                offset += Id128.SizeInBytes;
            }

            return ContentHash.Compute(buffer);
        }

        private static int WriteUInt32(byte[] destination, int offset, uint value)
        {
            destination[offset] = (byte)(value >> 24);
            destination[offset + 1] = (byte)(value >> 16);
            destination[offset + 2] = (byte)(value >> 8);
            destination[offset + 3] = (byte)value;
            return offset + 4;
        }
    }
}
