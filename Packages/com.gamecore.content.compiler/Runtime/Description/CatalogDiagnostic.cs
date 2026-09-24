// GameCore.Content.Compiler - build-time diagnostics (GC-003). A generation failure is a value with a stable
// code and the exact document path that produced it, so a bad catalog description names the offending member
// instead of failing with an exception and a stack trace.
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace GameCore.Content.Compiler
{
    /// <summary>Stable build-time diagnostic codes. These are generator codes, not protocol operation codes.</summary>
    public enum CatalogDiagnosticCode
    {
        /// <summary>The document is not well-formed JSON, or its root is not an object.</summary>
        InvalidDocument = 0,

        /// <summary>The description format id is missing or not the supported one.</summary>
        UnsupportedFormat = 1,

        /// <summary>A member name is not part of the documented input form.</summary>
        UnknownMember = 2,

        /// <summary>A required member is absent.</summary>
        MissingMember = 3,

        /// <summary>A member has the wrong JSON kind, or a value outside its accepted domain.</summary>
        InvalidValue = 4,

        /// <summary>An identity is not exactly 32 lowercase hexadecimal characters.</summary>
        InvalidIdentityHex = 5,

        /// <summary>A stable name is empty or uses characters outside the accepted set.</summary>
        InvalidStableName = 6,

        /// <summary>A generated name would not be a valid C# identifier.</summary>
        InvalidIdentifier = 7,

        /// <summary>A declared type or expression is not an acceptable generated-code fragment.</summary>
        InvalidCodeFragment = 8,

        /// <summary>Two declarations use the same stable name.</summary>
        DuplicateStableName = 9,

        /// <summary>Two schemas declare the same schema identity.</summary>
        DuplicateSchemaId = 10,

        /// <summary>Two fields in one schema declare the same field id.</summary>
        DuplicateFieldId = 12,

        /// <summary>Two generated members would use the same name.</summary>
        DuplicateMemberName = 13,

        /// <summary>A field id is not positive; id 0 is reserved for the envelope checksum.</summary>
        ReservedFieldId = 14,

        /// <summary>A field declares a wire type the mapping does not support.</summary>
        UnsupportedWireType = 15,

        /// <summary>The declared protocol version is not a supported major/minor pair.</summary>
        InvalidProtocolVersion = 16,

        /// <summary>The description does not produce a valid catalog under the production catalog rules.</summary>
        InvalidCatalog = 17,
    }

    /// <summary>One build-time rejection: stable code, document path and message.</summary>
    public sealed class CatalogDiagnostic
    {
        public CatalogDiagnostic(CatalogDiagnosticCode code, string path, string message)
        {
            Code = code;
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public CatalogDiagnosticCode Code { get; }

        /// <summary>Document path of the offending member, for example <c>schemas[1].fields[2].wireType</c>.</summary>
        public string Path { get; }

        /// <summary>Human-readable reason; never empty.</summary>
        public string Message { get; }

        public override string ToString() =>
            Code + ": " + (Path.Length == 0 ? "<document>" : Path) + ": " + Message;
    }

    /// <summary>Collects build-time diagnostics in document order.</summary>
    internal sealed class CatalogDiagnosticBag
    {
        private readonly List<CatalogDiagnostic> diagnostics = new List<CatalogDiagnostic>();

        internal int Count => diagnostics.Count;

        internal void Add(CatalogDiagnosticCode code, string path, string message) =>
            diagnostics.Add(new CatalogDiagnostic(code, path, message));

        internal IReadOnlyList<CatalogDiagnostic> ToList() => diagnostics.AsReadOnly();

        internal string Describe(int limit)
        {
            StringBuilder builder = new StringBuilder();
            int count = Math.Min(limit, diagnostics.Count);
            for (int i = 0; i < count; i++)
            {
                if (i != 0)
                {
                    builder.Append('\n');
                }

                builder.Append("  - ").Append(diagnostics[i].ToString());
            }

            if (diagnostics.Count > count)
            {
                builder.Append("\n  - ... ").Append(diagnostics.Count - count).Append(" more");
            }

            return builder.ToString();
        }
    }

    /// <summary>Result of compiling one catalog description document.</summary>
    public sealed class CatalogCompilationResult
    {
        internal CatalogCompilationResult(string? generatedCode, IReadOnlyList<CatalogDiagnostic> diagnostics)
        {
            GeneratedCode = generatedCode;
            Diagnostics = diagnostics ?? Array.Empty<CatalogDiagnostic>();
        }

        /// <summary>Generated C# source with LF line endings, or null when the description was rejected.</summary>
        public string? GeneratedCode { get; }

        /// <summary>Build-time rejections; empty on success.</summary>
        public IReadOnlyList<CatalogDiagnostic> Diagnostics { get; }

        public bool Succeeded => GeneratedCode != null;

        /// <summary>Number of characters in the generated source; zero when compilation failed.</summary>
        public int GeneratedLength => GeneratedCode?.Length ?? 0;

        /// <summary>One-line or multi-line summary suitable for a build log.</summary>
        public string Describe()
        {
            if (Succeeded)
            {
                return "ok (" + GeneratedLength + " characters)";
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("rejected with ").Append(Diagnostics.Count).Append(" diagnostic(s):");
            for (int i = 0; i < Diagnostics.Count; i++)
            {
                builder.Append("\n  - ").Append(Diagnostics[i].ToString());
            }

            return builder.ToString();
        }
    }
}
