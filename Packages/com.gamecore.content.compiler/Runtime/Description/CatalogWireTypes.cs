// GameCore.Content.Compiler - wire type mapping table (GC-003). Normative source:
// docs/game-core/05-contracts-and-data-model.md s6. A generated schema field must name one of these wire types;
// the mapping decides the generated C# field type and the generated read/write calls.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Content.Compiler
{
    /// <summary>One supported schema field wire type and its generated C# shape.</summary>
    public sealed class CatalogWireType
    {
        internal CatalogWireType(string name, WireType wireType, string csharpType, bool nullable, string writeMethod, string readMethod)
        {
            Name = name;
            WireType = wireType;
            CSharpType = csharpType;
            IsNullableReference = nullable;
            WriteMethod = writeMethod;
            ReadMethod = readMethod;
        }

        /// <summary>Wire type name used in a description document, identical to <see cref="Contracts.WireType"/>.</summary>
        public string Name { get; }

        /// <summary>Envelope wire type this name maps to.</summary>
        public WireType WireType { get; }

        /// <summary>Generated C# field type.</summary>
        public string CSharpType { get; }

        /// <summary>True when the C# type is a nullable reference type in generated code.</summary>
        public bool IsNullableReference { get; }

        /// <summary>Envelope writer method used by the generated serializer.</summary>
        public string WriteMethod { get; }

        /// <summary>Envelope reader method used by the generated deserializer.</summary>
        public string ReadMethod { get; }
    }

    /// <summary>The supported wire type table; anything not listed rejects at generation time.</summary>
    public static class CatalogWireTypes
    {
        private static readonly CatalogWireType[] All =
        {
            new CatalogWireType("Bool", WireType.Bool, "bool", false, "WriteBoolField", "TryReadBool"),
            new CatalogWireType("Bytes", WireType.Bytes, "byte[]", true, "WriteBytesField", "TryReadBytes"),
            new CatalogWireType("Float32", WireType.Float32, "float", false, "WriteFloat32Field", "TryReadFloat32"),
            new CatalogWireType("Float64", WireType.Float64, "double", false, "WriteFloat64Field", "TryReadFloat64"),
            new CatalogWireType("Id128", WireType.Id128, "Id128", false, "WriteId128Field", "TryReadId128"),
            new CatalogWireType("Int32", WireType.Int32, "int", false, "WriteInt32Field", "TryReadInt32"),
            new CatalogWireType("Int64", WireType.Int64, "long", false, "WriteInt64Field", "TryReadInt64"),
            new CatalogWireType("UInt32", WireType.UInt32, "uint", false, "WriteUInt32Field", "TryReadUInt32"),
            new CatalogWireType("UInt64", WireType.UInt64, "ulong", false, "WriteUInt64Field", "TryReadUInt64"),
            new CatalogWireType("Utf8", WireType.Utf8, "string", true, "WriteUtf8Field", "TryReadUtf8"),
        };

        /// <summary>Every supported wire type name in ordinal order.</summary>
        public static IReadOnlyList<CatalogWireType> Entries => Array.AsReadOnly(All);

        /// <summary>Finds a wire type by its exact name; null when the name is not supported.</summary>
        public static CatalogWireType? Find(string? name)
        {
            if (name == null)
            {
                return null;
            }

            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].Name, name, StringComparison.Ordinal))
                {
                    return All[i];
                }
            }

            return null;
        }

        /// <summary>Comma-separated supported names, for diagnostics and the package README.</summary>
        public static string DescribeSupported()
        {
            string[] names = new string[All.Length];
            for (int i = 0; i < All.Length; i++)
            {
                names[i] = All[i].Name;
            }

            return string.Join(", ", names);
        }
    }
}
