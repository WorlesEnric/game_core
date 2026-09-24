// GameCore.Content.Compiler - catalog description model (GC-003). This is the documented input form of the
// build-time generator: a C#-free, engine-free declaration document that names stable identities, generated
// registration keys, schemas with explicit field ids and wire types, generated-code binding expressions and
// code-injection directives. The generator turns it into deterministic C# source; nothing in this model is a
// runtime type.
#nullable enable
using System.Collections.Generic;

namespace GameCore.Content.Compiler
{
    /// <summary>One declared schema field: explicit field id, wire type and requiredness (05 s6).</summary>
    public sealed class CatalogFieldDeclaration
    {
        public CatalogFieldDeclaration(int fieldId, string name, string wireType, bool required)
        {
            FieldId = fieldId;
            Name = name;
            WireType = wireType;
            Required = required;
        }

        /// <summary>Explicit positive field id; 0 is reserved for the envelope checksum.</summary>
        public int FieldId { get; }

        /// <summary>Generated member name in the value type, already a valid C# identifier.</summary>
        public string Name { get; }

        /// <summary>Wire type name, one of the supported mapping entries in <see cref="CatalogWireTypes"/>.</summary>
        public string WireType { get; }

        /// <summary>True when the field must be present in every document of this schema (05 s6).</summary>
        public bool Required { get; }
    }

    /// <summary>One declared schema with its generated value type, serializer and registration keys.</summary>
    public sealed class CatalogSchemaDeclaration
    {
        public CatalogSchemaDeclaration(
            string stableName,
            string valueTypeName,
            string serializerTypeName,
            string serializerKeyName,
            uint schemaVersion,
            uint serializerKeyVersion,
            string schemaIdHex,
            string ownerPackageIdHex,
            bool isRequired,
            IReadOnlyList<CatalogFieldDeclaration> fields)
        {
            StableName = stableName;
            ValueTypeName = valueTypeName;
            SerializerTypeName = serializerTypeName;
            SerializerKeyName = serializerKeyName;
            SchemaVersion = schemaVersion;
            SerializerKeyVersion = serializerKeyVersion;
            SchemaIdHex = schemaIdHex;
            OwnerPackageIdHex = ownerPackageIdHex;
            IsRequired = isRequired;
            Fields = CatalogCollections.Freeze(fields);
        }

        /// <summary>Stable schema name; diagnostic only, never the runtime identity (P-004).</summary>
        public string StableName { get; }

        /// <summary>Generated value type name, for example <c>ProbeRecordValue</c>.</summary>
        public string ValueTypeName { get; }

        /// <summary>Generated serializer class name, for example <c>ProbeRecordSerializer</c>.</summary>
        public string SerializerTypeName { get; }

        /// <summary>Generated constant name of this serializer's registration key.</summary>
        public string SerializerKeyName { get; }

        /// <summary>Declared schema version; content compatibility, not liveness (P-006).</summary>
        public uint SchemaVersion { get; }

        /// <summary>Generated key version of the serializer registration (P-009).</summary>
        public uint SerializerKeyVersion { get; }

        /// <summary>Schema identity as exactly 32 lowercase hexadecimal characters.</summary>
        public string SchemaIdHex { get; }

        /// <summary>Owning package identity as exactly 32 lowercase hexadecimal characters; all-zero means kernel.</summary>
        public string OwnerPackageIdHex { get; }

        /// <summary>True when an unknown version of this schema must reject instead of being skipped (05 s6).</summary>
        public bool IsRequired { get; }

        /// <summary>Declared fields in ascending field-id order.</summary>
        public IReadOnlyList<CatalogFieldDeclaration> Fields { get; }
    }

    /// <summary>One entry of a generated registration group.</summary>
    public sealed class CatalogRegistrationEntry
    {
        public CatalogRegistrationEntry(
            string stableName,
            string keyName,
            uint keyVersion,
            string ownerPackageIdHex,
            string implementationIdHex,
            string implementationExpression)
        {
            StableName = stableName;
            KeyName = keyName;
            KeyVersion = keyVersion;
            OwnerPackageIdHex = ownerPackageIdHex;
            ImplementationIdHex = implementationIdHex;
            ImplementationExpression = implementationExpression;
        }

        /// <summary>Stable name the 128-bit key is derived from (P-004).</summary>
        public string StableName { get; }

        /// <summary>Generated constant name of this registration's key, for example <c>FixturePluginKey</c>.</summary>
        public string KeyName { get; }

        /// <summary>Generated key version (P-009).</summary>
        public uint KeyVersion { get; }

        /// <summary>Owning package identity as 32 lowercase hexadecimal characters.</summary>
        public string OwnerPackageIdHex { get; }

        /// <summary>Stable identity of the precompiled implementation as 32 lowercase hexadecimal characters.</summary>
        public string ImplementationIdHex { get; }

        /// <summary>Generated-code expression producing the implementation; may use the <c>{factoryKey}</c> placeholder.</summary>
        public string ImplementationExpression { get; }
    }

    /// <summary>
    /// A generated registration group: one typed, keyed table plus its lookup method. The group declares the
    /// consumer interface its entries implement, so generated lookup methods return a typed implementation
    /// instead of <see cref="object"/> (04 s8).
    /// </summary>
    public sealed class CatalogRegistrationGroup
    {
        public CatalogRegistrationGroup(
            string tableName,
            string keysName,
            string lookupMethodName,
            string interfaceType,
            string kind,
            IReadOnlyList<CatalogRegistrationEntry> entries)
        {
            TableName = tableName;
            KeysName = keysName;
            LookupMethodName = lookupMethodName;
            InterfaceType = interfaceType;
            Kind = kind;
            Entries = CatalogCollections.Freeze(entries);
        }

        /// <summary>Generated table member name, for example <c>PluginRegistrations</c>.</summary>
        public string TableName { get; }

        /// <summary>Generated key-list member name, for example <c>PluginKeys</c>.</summary>
        public string KeysName { get; }

        /// <summary>Generated lookup method name, for example <c>TryGetPluginFactory</c>.</summary>
        public string LookupMethodName { get; }

        /// <summary>Consumer interface type expression every entry implements.</summary>
        public string InterfaceType { get; }

        /// <summary>Factory kind name from <see cref="GameCore.Contracts.FactoryKind"/>.</summary>
        public string Kind { get; }

        /// <summary>Group entries; the emitter sorts them by derived key, not by declaration order.</summary>
        public IReadOnlyList<CatalogRegistrationEntry> Entries { get; }
    }

    /// <summary>Declared code fragments the generator copies verbatim into its output.</summary>
    /// <remarks>
    /// These directives exist because a generated registry must contain direct constructor references, closed
    /// generic instantiations and assembly attributes that no engine-free generator can synthesize from data
    /// (04 s8). They are validated for shape (single line, no statement terminator, no comment, bounded length)
    /// but not type-checked: the compiler of the consuming project is the type check, so a directive that does
    /// not compile fails the build instead of silently disappearing.
    /// </remarks>
    public sealed class CatalogCodeSection
    {
        public CatalogCodeSection(
            IReadOnlyList<string> usingDirectives,
            IReadOnlyList<string> assemblyAttributes,
            IReadOnlyList<string> closedGenericRootStatements)
        {
            UsingDirectives = CatalogCollections.Freeze(usingDirectives);
            AssemblyAttributes = CatalogCollections.Freeze(assemblyAttributes);
            ClosedGenericRootStatements = CatalogCollections.Freeze(closedGenericRootStatements);
        }

        /// <summary>Name or alias name of an extra using directive; <c>using GameCore.Contracts</c> is always emitted.</summary>
        public IReadOnlyList<string> UsingDirectives { get; }

        /// <summary>Assembly-level attribute expressions, emitted as <c>[assembly: &lt;text&gt;]</c>.</summary>
        public IReadOnlyList<string> AssemblyAttributes { get; }

        /// <summary>Statements forming the generated closed-generic AOT root method (04 s8 item 3).</summary>
        public IReadOnlyList<string> ClosedGenericRootStatements { get; }
    }

    /// <summary>
    /// A complete, validated catalog description. Instances are produced by
    /// <see cref="CatalogDescriptionReader"/>; the constructor is internal so an invalid document cannot reach
    /// the emitter.
    /// </summary>
    public sealed class CatalogDescription
    {
        internal CatalogDescription(
            string descriptionFormat,
            string protocolMajor,
            string protocolMinor,
            string generatedNamespace,
            string className,
            string generatedFileName,
            IReadOnlyList<string> supportedFeatureHexIds,
            IReadOnlyList<CatalogSchemaDeclaration> schemas,
            IReadOnlyList<CatalogRegistrationGroup> groups,
            CatalogCodeSection code)
        {
            DescriptionFormat = descriptionFormat;
            ProtocolMajor = protocolMajor;
            ProtocolMinor = protocolMinor;
            GeneratedNamespace = generatedNamespace;
            ClassName = className;
            GeneratedFileName = generatedFileName;
            SupportedFeatureHexIds = CatalogCollections.Freeze(supportedFeatureHexIds);
            Schemas = CatalogCollections.Freeze(schemas);
            Groups = CatalogCollections.Freeze(groups);
            Code = code;
        }

        /// <summary>Description format identifier; must equal <see cref="CatalogDescriptionReader.FormatId"/>.</summary>
        public string DescriptionFormat { get; }

        /// <summary>Protocol major parsed from the declared version, as canonical decimal text.</summary>
        public string ProtocolMajor { get; }

        /// <summary>Protocol minor parsed from the declared version, as canonical decimal text.</summary>
        public string ProtocolMinor { get; }

        /// <summary>Namespace of the generated class.</summary>
        public string GeneratedNamespace { get; }

        /// <summary>Name of the generated static class.</summary>
        public string ClassName { get; }

        /// <summary>File name recorded inside the generated file; the write path is a caller parameter.</summary>
        public string GeneratedFileName { get; }

        /// <summary>Supported protocol feature ids as 32-character lowercase hexadecimal text.</summary>
        public IReadOnlyList<string> SupportedFeatureHexIds { get; }

        /// <summary>Schemas sorted by schema identity.</summary>
        public IReadOnlyList<CatalogSchemaDeclaration> Schemas { get; }

        /// <summary>Registration groups in declaration order; entry order inside a group is canonicalized.</summary>
        public IReadOnlyList<CatalogRegistrationGroup> Groups { get; }

        /// <summary>Declared code injection directives.</summary>
        public CatalogCodeSection Code { get; }
    }

    /// <summary>Defensive copies for catalog description collections.</summary>
    internal static class CatalogCollections
    {
        internal static IReadOnlyList<T> Freeze<T>(IReadOnlyList<T>? source)
        {
            if (source == null || source.Count == 0)
            {
                return System.Array.Empty<T>();
            }

            T[] copy = new T[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            return System.Array.AsReadOnly(copy);
        }
    }
}
