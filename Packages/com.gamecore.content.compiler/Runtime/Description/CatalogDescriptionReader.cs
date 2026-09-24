// GameCore.Content.Compiler - catalog description reader and validator (GC-003).
//
// Input form (the documented, versioned declaration document the generator consumes):
//
// {
//   "descriptionFormat": "gamecore.catalog-description/1",
//   "protocolVersion": "1.0",
//   "namespace": "My.Project.Generated",
//   "className": "GameCoreCatalog",
//   "fileName": "GameCoreCatalog.g.cs",
//   "supportedFeatureIds": ["<32 lowercase hex>", ...],
//   "schemas": [
//     {
//       "stableName": "my.project.schema.probe-record",
//       "valueTypeName": "ProbeRecord",
//       "serializerTypeName": "ProbeRecordSerializer",
//       "serializerKeyName": "ProbeRecordSerializerKey",
//       "schemaId": "<32 lowercase hex>",
//       "schemaVersion": 1,
//       "serializerKeyVersion": 1,
//       "ownerPackageId": "<32 lowercase hex>",
//       "required": true,
//       "fields": [
//         { "id": 1, "name": "High", "wireType": "UInt64", "required": true }
//       ]
//     }
//   ],
//   "groups": [
//     {
//       "tableName": "PluginRegistrations",
//       "keysName": "PluginKeys",
//       "lookupMethodName": "TryGetPluginFactory",
//       "interfaceType": "My.Project.IProbePluginFactory",
//       "kind": "PluginFactory",
//       "entries": [
//         {
//           "stableName": "my.project.plugin.fixture",
//           "keyName": "FixturePluginKey",
//           "keyVersion": 1,
//           "ownerPackageId": "...",
//           "implementationId": "...",
//           "implementationExpression": "new My.Project.FixturePluginFactory()"
//         }
//       ]
//     }
//   ],
//   "code": {
//     "usingDirectives": ["My.Project"],
//     "assemblyAttributes": ["RegisterGenericJobType(typeof(My.Project.MyJob<My.Value>))"],
//     "closedGenericRootStatements": ["MyAotRoots.Track(default(My.Job<My.Value>));"]
//   }
// }
//
// Every member is required unless this comment marks it optional. Unknown members reject, so a typo cannot be
// silently ignored and produce a catalog missing a registration.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Content.Compiler.Json;

namespace GameCore.Content.Compiler
{
    /// <summary>Reads and validates the documented catalog description document into an immutable model.</summary>
    public static class CatalogDescriptionReader
    {
        /// <summary>Exactly the description format id this reader accepts.</summary>
        public const string FormatId = "gamecore.catalog-description/1";

        /// <summary>Protocol major implemented by this build (P-055: V1 is 1.0).</summary>
        public const int SupportedProtocolMajor = 1;

        /// <summary>Highest protocol minor implemented by this build (P-055).</summary>
        public const int SupportedProtocolMinor = 0;

        private const int MaxSchemas = 4096;
        private const int MaxGroups = 256;
        private const int MaxEntriesPerGroup = 65536;
        private const int MaxFieldsPerSchema = 4096;

        /// <summary>Parses and validates one description document; throws only for malformed JSON.</summary>
        public static CatalogCompilationResult Read(string json)
        {
            return Read(json, out CatalogDescription? _);
        }

        /// <summary>
        /// Parses and validates one description document and returns both the emitted source and the validated
        /// model. A caller that needs the declarations themselves (a test recomputing the catalog fingerprint,
        /// or a later build step listing the registrations) uses this overload instead of re-parsing.
        /// </summary>
        public static CatalogCompilationResult Read(string json, out CatalogDescription? description)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            description = null;
            CatalogDiagnosticBag diagnostics = new CatalogDiagnosticBag();
            JsonValue root;
            try
            {
                root = JsonReader.Parse(json);
            }
            catch (JsonFormatException error)
            {
                diagnostics.Add(CatalogDiagnosticCode.InvalidDocument, string.Empty, error.Message);
                return new CatalogCompilationResult(null, diagnostics.ToList());
            }

            if (root.Kind != JsonKind.Object)
            {
                diagnostics.Add(CatalogDiagnosticCode.InvalidDocument, string.Empty, "the document root must be an object");
                return new CatalogCompilationResult(null, diagnostics.ToList());
            }

            ValidateMembers(root, diagnostics);

            string descriptionFormat = RequiredString(root, "descriptionFormat", string.Empty, diagnostics) ?? string.Empty;
            if (!string.Equals(descriptionFormat, FormatId, StringComparison.Ordinal))
            {
                diagnostics.Add(
                    CatalogDiagnosticCode.UnsupportedFormat,
                    "descriptionFormat",
                    "expected '" + FormatId + "' but read " + Describe(root.Member("descriptionFormat")));
            }

            string generatedNamespace = RequiredIdentifier(root, "namespace", "namespace", diagnostics) ?? "GameCore.Generated";
            string className = RequiredIdentifier(root, "className", "className", diagnostics) ?? "GameCoreCatalog";
            string fileName = RequiredString(root, "fileName", "fileName", diagnostics) ?? string.Empty;
            ValidateFileName(fileName, diagnostics);

            string protocolVersion = RequiredString(root, "protocolVersion", "protocolVersion", diagnostics) ?? string.Empty;
            ValidateProtocolVersion(protocolVersion, diagnostics);

            IReadOnlyList<string> features = ReadFeatureIds(root, diagnostics);
            IReadOnlyList<CatalogSchemaDeclaration> schemas = ReadSchemas(root, diagnostics);
            IReadOnlyList<CatalogRegistrationGroup> groups = ReadGroups(root, diagnostics);
            CatalogCodeSection code = ReadCodeSection(root, diagnostics);

            CrossCheckStableNames(schemas, groups, diagnostics);
            CrossCheckKeys(groups, diagnostics);
            CrossCheckMemberNames(groups, diagnostics);
            CrossCheckCatalog(schemas, groups, features, diagnostics);

            if (diagnostics.Count != 0)
            {
                return new CatalogCompilationResult(null, diagnostics.ToList());
            }

            string[] majorMinor = protocolVersion.Split('.');
            CatalogDescription validated = new CatalogDescription(
                descriptionFormat,
                majorMinor[0],
                majorMinor[1],
                generatedNamespace,
                className,
                fileName,
                features,
                schemas,
                groups,
                code);

            description = validated;
            return new CatalogCompilationResult(CatalogEmitter.Emit(validated), diagnostics.ToList());
        }

        private static void ValidateMembers(JsonValue root, CatalogDiagnosticBag diagnostics)
        {
            string[] known =
            {
                "descriptionFormat", "protocolVersion", "namespace", "className", "fileName",
                "supportedFeatureIds", "schemas", "groups", "code",
            };

            ReportUnknown(root, string.Empty, known, diagnostics);
        }

        private static void ReportUnknown(JsonValue value, string path, string[] known, CatalogDiagnosticBag diagnostics)
        {
            IReadOnlyList<KeyValuePair<string, JsonValue>> members = value.Members;
            for (int i = 0; i < members.Count; i++)
            {
                bool found = false;
                for (int j = 0; j < known.Length; j++)
                {
                    if (string.Equals(known[j], members[i].Key, StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    diagnostics.Add(
                        CatalogDiagnosticCode.UnknownMember,
                        Join(path, members[i].Key),
                        "unknown member; the accepted members are " + string.Join(", ", known));
                }
            }
        }

        private static void ValidateFileName(string fileName, CatalogDiagnosticBag diagnostics)
        {
            if (fileName.Length == 0)
            {
                return;
            }

            bool valid = fileName.EndsWith(".cs", StringComparison.Ordinal);
            for (int i = 0; valid && i < fileName.Length; i++)
            {
                char c = fileName[i];
                if (c == '/' || c == '\\' || c == ':' || c < 0x20)
                {
                    valid = false;
                }
            }

            if (!valid)
            {
                diagnostics.Add(
                    CatalogDiagnosticCode.InvalidValue,
                    "fileName",
                    "must be a bare C# file name ending in '.cs' with no path separator; read '" + fileName + "'");
            }
        }

        private static void ValidateProtocolVersion(string protocolVersion, CatalogDiagnosticBag diagnostics)
        {
            string[] parts = protocolVersion.Split('.');
            if (parts.Length != 2)
            {
                diagnostics.Add(
                    CatalogDiagnosticCode.InvalidProtocolVersion,
                    "protocolVersion",
                    "expected '<major>.<minor>' with no sign or leading zero; read '" + protocolVersion + "'");
                return;
            }

            int major;
            int minor;
            if (!TryParseCanonicalInt(parts[0], out major) || !TryParseCanonicalInt(parts[1], out minor))
            {
                diagnostics.Add(
                    CatalogDiagnosticCode.InvalidProtocolVersion,
                    "protocolVersion",
                    "expected canonical integer components without a sign or leading zero; read '" + protocolVersion + "'");
                return;
            }

            if (major != SupportedProtocolMajor || minor > SupportedProtocolMinor)
            {
                diagnostics.Add(
                    CatalogDiagnosticCode.InvalidProtocolVersion,
                    "protocolVersion",
                    "this generator implements " + SupportedProtocolMajor.ToString(CultureInfo.InvariantCulture) + "." +
                    SupportedProtocolMinor.ToString(CultureInfo.InvariantCulture) +
                    "; a major change is incompatible and an unimplemented minor cannot be emitted (P-055)");
            }
        }

        private static bool TryParseCanonicalInt(string text, out int value)
        {
            value = 0;
            if (text.Length == 0 || text.Length > 9)
            {
                return false;
            }

            if (text.Length > 1 && text[0] == '0')
            {
                return false;
            }

            int result = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < '0' || c > '9')
                {
                    return false;
                }

                result = (result * 10) + (c - '0');
            }

            value = result;
            return true;
        }

        private static IReadOnlyList<string> ReadFeatureIds(JsonValue root, CatalogDiagnosticBag diagnostics)
        {
            List<string> features = new List<string>();
            JsonValue? element = root.Member("supportedFeatureIds");
            if (element == null || element.IsNull)
            {
                return features;
            }

            if (element.Kind != JsonKind.Array)
            {
                diagnostics.Add(
                    CatalogDiagnosticCode.InvalidValue,
                    "supportedFeatureIds",
                    "must be an array of 32-character lowercase hexadecimal ids; read " + element.Describe());
                return features;
            }

            for (int i = 0; i < element.Items.Count; i++)
            {
                string path = "supportedFeatureIds[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                string? text = RequireString(element.Items[i], path, diagnostics);
                if (text == null)
                {
                    continue;
                }

                if (!IsIdentityHex(text))
                {
                    diagnostics.Add(
                        CatalogDiagnosticCode.InvalidIdentityHex,
                        path,
                        "expected exactly 32 lowercase hexadecimal characters; read '" + text + "'");
                    continue;
                }

                if (IsAllZeroHex(text))
                {
                    diagnostics.Add(
                        CatalogDiagnosticCode.InvalidIdentityHex,
                        path,
                        "the all-zero id is not a declared protocol feature (P-004)");
                    continue;
                }

                features.Add(text);
            }

            return features;
        }

        private static IReadOnlyList<CatalogSchemaDeclaration> ReadSchemas(JsonValue root, CatalogDiagnosticBag diagnostics)
        {
            List<CatalogSchemaDeclaration> schemas = new List<CatalogSchemaDeclaration>();
            JsonValue? element = root.Member("schemas");
            if (element == null || element.IsNull)
            {
                return schemas;
            }

            if (element.Kind != JsonKind.Array)
            {
                diagnostics.Add(
                    CatalogDiagnosticCode.InvalidValue,
                    "schemas",
                    "must be an array of schema declarations; read " + element.Describe());
                return schemas;
            }

            if (element.Items.Count > MaxSchemas)
            {
                diagnostics.Add(
                    CatalogDiagnosticCode.InvalidValue,
                    "schemas",
                    "declares more than " + MaxSchemas.ToString(CultureInfo.InvariantCulture) + " schemas");
                return schemas;
            }

            for (int i = 0; i < element.Items.Count; i++)
            {
                string path = "schemas[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                JsonValue item = element.Items[i];
                if (item.Kind != JsonKind.Object)
                {
                    diagnostics.Add(CatalogDiagnosticCode.InvalidValue, path, "a schema declaration must be an object");
                    continue;
                }

                ReportUnknown(
                    item,
                    path,
                    new[]
                    {
                        "stableName", "valueTypeName", "serializerTypeName", "serializerKeyName", "schemaId",
                        "schemaVersion", "serializerKeyVersion", "ownerPackageId", "required", "fields",
                    },
                    diagnostics);

                string? stableName = RequiredStableName(item, path, diagnostics);
                string? valueTypeName = RequiredIdentifier(item, "valueTypeName", Join(path, "valueTypeName"), diagnostics);
                string? serializerTypeName = RequiredIdentifier(item, "serializerTypeName", Join(path, "serializerTypeName"), diagnostics);
                string? serializerKeyName = RequiredIdentifier(item, "serializerKeyName", Join(path, "serializerKeyName"), diagnostics);
                string? schemaId = RequiredIdentity(item, "schemaId", Join(path, "schemaId"), allowAllZero: false, diagnostics);
                uint schemaVersion = RequiredUInt32(item, "schemaVersion", Join(path, "schemaVersion"), diagnostics);
                uint serializerKeyVersion = RequiredUInt32(item, "serializerKeyVersion", Join(path, "serializerKeyVersion"), diagnostics);
                string? ownerPackageId = RequiredIdentity(item, "ownerPackageId", Join(path, "ownerPackageId"), allowAllZero: true, diagnostics);
                bool isRequired = RequiredBool(item, "required", Join(path, "required"), diagnostics);
                IReadOnlyList<CatalogFieldDeclaration> fields = ReadFields(item, path, diagnostics);

                if (stableName == null || valueTypeName == null || serializerTypeName == null || serializerKeyName == null ||
                    schemaId == null || ownerPackageId == null)
                {
                    continue;
                }

                schemas.Add(new CatalogSchemaDeclaration(
                    stableName,
                    valueTypeName,
                    serializerTypeName,
                    serializerKeyName,
                    schemaVersion,
                    serializerKeyVersion,
                    schemaId,
                    ownerPackageId,
                    isRequired,
                    fields));
            }

            return schemas;
        }

        private static IReadOnlyList<CatalogFieldDeclaration> ReadFields(JsonValue schema, string schemaPath, CatalogDiagnosticBag diagnostics)
        {
            List<CatalogFieldDeclaration> fields = new List<CatalogFieldDeclaration>();
            JsonValue? element = schema.Member("fields");
            string path = Join(schemaPath, "fields");
            if (element == null || element.IsNull)
            {
                return fields;
            }

            if (element.Kind != JsonKind.Array)
            {
                diagnostics.Add(CatalogDiagnosticCode.InvalidValue, path, "must be an array of field declarations; read " + element.Describe());
                return fields;
            }

            if (element.Items.Count > MaxFieldsPerSchema)
            {
                diagnostics.Add(
                    CatalogDiagnosticCode.InvalidValue,
                    path,
                    "declares more than " + MaxFieldsPerSchema.ToString(CultureInfo.InvariantCulture) + " fields; the envelope field-id space is bounded");
                return fields;
            }

            HashSet<int> seenIds = new HashSet<int>();
            HashSet<string> seenNames = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < element.Items.Count; i++)
            {
                string fieldPath = path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                JsonValue item = element.Items[i];
                if (item.Kind != JsonKind.Object)
                {
                    diagnostics.Add(CatalogDiagnosticCode.InvalidValue, fieldPath, "a field declaration must be an object");
                    continue;
                }

                ReportUnknown(item, fieldPath, new[] { "id", "name", "wireType", "required" }, diagnostics);

                int fieldId = RequiredInt32(item, "id", Join(fieldPath, "id"), diagnostics);
                string? name = RequiredIdentifier(item, "name", Join(fieldPath, "name"), diagnostics);
                string? wireTypeName = RequiredString(item, "wireType", Join(fieldPath, "wireType"), diagnostics);
                bool required = RequiredBool(item, "required", Join(fieldPath, "required"), diagnostics);

                if (fieldId <= EnvelopeFormat.ChecksumFieldId)
                {
                    diagnostics.Add(
                        CatalogDiagnosticCode.ReservedFieldId,
                        Join(fieldPath, "id"),
                        "field id " + fieldId.ToString(CultureInfo.InvariantCulture) +
                        " is reserved; declared field ids are positive (05 s6)");
                    continue;
                }

                if (!seenIds.Add(fieldId))
                {
                    diagnostics.Add(
                        CatalogDiagnosticCode.DuplicateFieldId,
                        Join(fieldPath, "id"),
                        "field id " + fieldId.ToString(CultureInfo.InvariantCulture) + " is declared twice in this schema");
                    continue;
                }

                if (name != null && !seenNames.Add(name))
                {
                    diagnostics.Add(
                        CatalogDiagnosticCode.DuplicateMemberName,
                        Join(fieldPath, "name"),
                        "field name '" + name + "' is declared twice in this schema");
                    continue;
                }

                if (wireTypeName != null && CatalogWireTypes.Find(wireTypeName) == null)
                {
                    diagnostics.Add(
                        CatalogDiagnosticCode.UnsupportedWireType,
                        Join(fieldPath, "wireType"),
                        "unsupported wire type '" + wireTypeName + "'; supported types are " +
                        CatalogWireTypes.DescribeSupported());
                    continue;
                }

                if (name == null || wireTypeName == null)
                {
                    continue;
                }

                fields.Add(new CatalogFieldDeclaration(fieldId, name, wireTypeName, required));
            }

            // Canonical field order is ascending field id, which is also the wire order and the generated
            // constructor/parameter order; declaration order in the document is therefore irrelevant (P-008).
            fields.Sort(CompareFields);
            return fields;
        }

        private static int CompareFields(CatalogFieldDeclaration left, CatalogFieldDeclaration right) =>
            left.FieldId.CompareTo(right.FieldId);

        private static IReadOnlyList<CatalogRegistrationGroup> ReadGroups(JsonValue root, CatalogDiagnosticBag diagnostics)
        {
            List<CatalogRegistrationGroup> groups = new List<CatalogRegistrationGroup>();
            JsonValue? element = root.Member("groups");
            if (element == null || element.IsNull)
            {
                return groups;
            }

            if (element.Kind != JsonKind.Array)
            {
                diagnostics.Add(CatalogDiagnosticCode.InvalidValue, "groups", "must be an array of registration groups; read " + element.Describe());
                return groups;
            }

            if (element.Items.Count > MaxGroups)
            {
                diagnostics.Add(
                    CatalogDiagnosticCode.InvalidValue,
                    "groups",
                    "declares more than " + MaxGroups.ToString(CultureInfo.InvariantCulture) + " registration groups");
                return groups;
            }

            for (int i = 0; i < element.Items.Count; i++)
            {
                string path = "groups[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                JsonValue item = element.Items[i];
                if (item.Kind != JsonKind.Object)
                {
                    diagnostics.Add(CatalogDiagnosticCode.InvalidValue, path, "a registration group must be an object");
                    continue;
                }

                ReportUnknown(
                    item,
                    path,
                    new[] { "tableName", "keysName", "lookupMethodName", "interfaceType", "kind", "entries" },
                    diagnostics);

                string? tableName = RequiredIdentifier(item, "tableName", Join(path, "tableName"), diagnostics);
                string? keysName = RequiredIdentifier(item, "keysName", Join(path, "keysName"), diagnostics);
                string? lookupMethodName = RequiredIdentifier(item, "lookupMethodName", Join(path, "lookupMethodName"), diagnostics);
                string? interfaceType = RequiredTypeExpression(item, "interfaceType", Join(path, "interfaceType"), diagnostics);
                string? kind = RequiredFactoryKind(item, path, diagnostics);
                IReadOnlyList<CatalogRegistrationEntry> entries = ReadEntries(item, path, diagnostics);

                if (tableName == null || keysName == null || lookupMethodName == null || interfaceType == null || kind == null)
                {
                    continue;
                }

                groups.Add(new CatalogRegistrationGroup(tableName, keysName, lookupMethodName, interfaceType, kind, entries));
            }

            return groups;
        }

        private static IReadOnlyList<CatalogRegistrationEntry> ReadEntries(JsonValue group, string groupPath, CatalogDiagnosticBag diagnostics)
        {
            List<CatalogRegistrationEntry> entries = new List<CatalogRegistrationEntry>();
            JsonValue? element = group.Member("entries");
            string path = Join(groupPath, "entries");
            if (element == null || element.IsNull)
            {
                return entries;
            }

            if (element.Kind != JsonKind.Array)
            {
                diagnostics.Add(CatalogDiagnosticCode.InvalidValue, path, "must be an array of registrations; read " + element.Describe());
                return entries;
            }

            if (element.Items.Count > MaxEntriesPerGroup)
            {
                diagnostics.Add(
                    CatalogDiagnosticCode.InvalidValue,
                    path,
                    "declares more than " + MaxEntriesPerGroup.ToString(CultureInfo.InvariantCulture) + " registrations");
                return entries;
            }

            for (int i = 0; i < element.Items.Count; i++)
            {
                string entryPath = path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                JsonValue item = element.Items[i];
                if (item.Kind != JsonKind.Object)
                {
                    diagnostics.Add(CatalogDiagnosticCode.InvalidValue, entryPath, "a registration must be an object");
                    continue;
                }

                ReportUnknown(
                    item,
                    entryPath,
                    new[] { "stableName", "keyName", "keyVersion", "ownerPackageId", "implementationId", "implementationExpression" },
                    diagnostics);

                string? stableName = RequiredStableName(item, entryPath, diagnostics);
                string? keyName = RequiredIdentifier(item, "keyName", Join(entryPath, "keyName"), diagnostics);
                uint keyVersion = RequiredUInt32(item, "keyVersion", Join(entryPath, "keyVersion"), diagnostics);
                string? ownerPackageId = RequiredIdentity(item, "ownerPackageId", Join(entryPath, "ownerPackageId"), allowAllZero: true, diagnostics);
                string? implementationId = RequiredIdentity(item, "implementationId", Join(entryPath, "implementationId"), allowAllZero: false, diagnostics);
                string? expression = RequiredCodeFragment(item, "implementationExpression", Join(entryPath, "implementationExpression"), diagnostics);

                if (stableName == null || keyName == null || ownerPackageId == null || implementationId == null || expression == null)
                {
                    continue;
                }

                entries.Add(new CatalogRegistrationEntry(stableName, keyName, keyVersion, ownerPackageId, implementationId, expression));
            }

            return entries;
        }

        private static CatalogCodeSection ReadCodeSection(JsonValue root, CatalogDiagnosticBag diagnostics)
        {
            JsonValue? element = root.Member("code");
            if (element == null || element.IsNull)
            {
                return new CatalogCodeSection(null, null, null);
            }

            if (element.Kind != JsonKind.Object)
            {
                diagnostics.Add(CatalogDiagnosticCode.InvalidValue, "code", "must be an object of code directives; read " + element.Describe());
                return new CatalogCodeSection(null, null, null);
            }

            ReportUnknown(element, "code", new[] { "usingDirectives", "assemblyAttributes", "closedGenericRootStatements" }, diagnostics);

            IReadOnlyList<string> usingDirectives = ReadIdentifierArray(element, "usingDirectives", diagnostics);
            IReadOnlyList<string> assemblyAttributes = ReadCodeFragmentArray(
                element,
                "assemblyAttributes",
                allowStatementAttributes: true,
                diagnostics);
            IReadOnlyList<string> closedGenericRoots = ReadCodeFragmentArray(
                element,
                "closedGenericRootStatements",
                allowStatementAttributes: false,
                diagnostics);

            return new CatalogCodeSection(usingDirectives, assemblyAttributes, closedGenericRoots);
        }

        private static IReadOnlyList<string> ReadIdentifierArray(JsonValue owner, string member, CatalogDiagnosticBag diagnostics)
        {
            List<string> values = new List<string>();
            JsonValue? element = owner.Member(member);
            string path = "code." + member;
            if (element == null || element.IsNull)
            {
                return values;
            }

            if (element.Kind != JsonKind.Array)
            {
                diagnostics.Add(CatalogDiagnosticCode.InvalidValue, path, "must be an array of strings; read " + element.Describe());
                return values;
            }

            for (int i = 0; i < element.Items.Count; i++)
            {
                string itemPath = path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                string? text = RequireString(element.Items[i], itemPath, diagnostics);
                if (text == null)
                {
                    continue;
                }

                if (!IsDottedIdentifier(text))
                {
                    diagnostics.Add(
                        CatalogDiagnosticCode.InvalidIdentifier,
                        itemPath,
                        "'" + text + "' is not a dotted C# identifier");
                    continue;
                }

                values.Add(text);
            }

            return values;
        }

        private static IReadOnlyList<string> ReadCodeFragmentArray(
            JsonValue owner,
            string member,
            bool allowStatementAttributes,
            CatalogDiagnosticBag diagnostics)
        {
            List<string> values = new List<string>();
            JsonValue? element = owner.Member(member);
            string path = "code." + member;
            if (element == null || element.IsNull)
            {
                return values;
            }

            if (element.Kind != JsonKind.Array)
            {
                diagnostics.Add(CatalogDiagnosticCode.InvalidValue, path, "must be an array of strings; read " + element.Describe());
                return values;
            }

            for (int i = 0; i < element.Items.Count; i++)
            {
                string itemPath = path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                string? text = RequireString(element.Items[i], itemPath, diagnostics);
                if (text == null)
                {
                    continue;
                }

                string? reason = CatalogCodeFragments.DescribeRejection(text, allowStatementAttributes);
                if (reason != null)
                {
                    diagnostics.Add(CatalogDiagnosticCode.InvalidCodeFragment, itemPath, reason);
                    continue;
                }

                values.Add(text);
            }

            return values;
        }

        private static void CrossCheckStableNames(
            IReadOnlyList<CatalogSchemaDeclaration> schemas,
            IReadOnlyList<CatalogRegistrationGroup> groups,
            CatalogDiagnosticBag diagnostics)
        {
            Dictionary<string, string> owners = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < schemas.Count; i++)
            {
                string path = "schemas[" + i.ToString(CultureInfo.InvariantCulture) + "].stableName";
                string key = schemas[i].StableName;
                if (owners.TryGetValue(key, out string? previous))
                {
                    diagnostics.Add(
                        CatalogDiagnosticCode.DuplicateStableName,
                        path,
                        "stable name '" + key + "' is already used by " + previous +
                        "; a stable name maps to exactly one derived key (P-004)");
                    continue;
                }

                owners.Add(key, path);
            }

            for (int g = 0; g < groups.Count; g++)
            {
                for (int e = 0; e < groups[g].Entries.Count; e++)
                {
                    string path = "groups[" + g.ToString(CultureInfo.InvariantCulture) + "].entries[" +
                                  e.ToString(CultureInfo.InvariantCulture) + "].stableName";
                    string key = groups[g].Entries[e].StableName;
                    if (owners.TryGetValue(key, out string? previous))
                    {
                        diagnostics.Add(
                            CatalogDiagnosticCode.DuplicateStableName,
                            path,
                            "stable name '" + key + "' is already used by " + previous +
                            "; a stable name maps to exactly one derived key (P-004)");
                        continue;
                    }

                    owners.Add(key, path);
                }
            }
        }

        /// <summary>
        /// Checks the derived registration keys of one description. Two distinct stable names cannot derive the
        /// same key without a SHA-256 collision, so the reachable duplicate is a repeated stable name, which
        /// <see cref="CrossCheckStableNames"/> rejects before any key is derived. This method therefore enforces
        /// the remaining key rule: no two registrations may declare the same generated key constant name.
        /// </summary>
        private static void CrossCheckKeys(IReadOnlyList<CatalogRegistrationGroup> groups, CatalogDiagnosticBag diagnostics)
        {
            Dictionary<string, string> keyNames = new Dictionary<string, string>(StringComparer.Ordinal);

            for (int g = 0; g < groups.Count; g++)
            {
                for (int e = 0; e < groups[g].Entries.Count; e++)
                {
                    CatalogRegistrationEntry entry = groups[g].Entries[e];
                    string path = "groups[" + g.ToString(CultureInfo.InvariantCulture) + "].entries[" +
                                  e.ToString(CultureInfo.InvariantCulture) + "]";

                    if (keyNames.TryGetValue(entry.KeyName, out string? previousName))
                    {
                        diagnostics.Add(
                            CatalogDiagnosticCode.DuplicateMemberName,
                            Join(path, "keyName"),
                            "generated key constant '" + entry.KeyName + "' is already declared by " + previousName);
                    }
                    else
                    {
                        keyNames.Add(entry.KeyName, Join(path, "keyName"));
                    }
                }
            }
        }

        private static void CrossCheckMemberNames(IReadOnlyList<CatalogRegistrationGroup> groups, CatalogDiagnosticBag diagnostics)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            string[] fixedMembers = { "GeneratedFileName", "HashAlgorithm", "CatalogFileHash", "CatalogFileHashScope", "CatalogFingerprintScope" };
            for (int i = 0; i < fixedMembers.Length; i++)
            {
                seen.Add(fixedMembers[i]);
            }

            for (int g = 0; g < groups.Count; g++)
            {
                string path = "groups[" + g.ToString(CultureInfo.InvariantCulture) + "]";
                string[] members =
                {
                    groups[g].TableName,
                    groups[g].TableName + "CatalogRegistrations",
                    groups[g].KeysName,
                    groups[g].LookupMethodName,
                };
                for (int m = 0; m < members.Length; m++)
                {
                    if (!seen.Add(members[m]))
                    {
                        diagnostics.Add(
                            CatalogDiagnosticCode.DuplicateMemberName,
                            path,
                            "generated member name '" + members[m] + "' is already declared");
                    }
                }

                for (int e = 0; e < groups[g].Entries.Count; e++)
                {
                    string keyName = groups[g].Entries[e].KeyName;
                    if (!seen.Add(keyName))
                    {
                        diagnostics.Add(
                            CatalogDiagnosticCode.DuplicateMemberName,
                            path,
                            "generated key constant '" + keyName + "' is already declared");
                    }
                }
            }
        }

        private static void CrossCheckCatalog(
            IReadOnlyList<CatalogSchemaDeclaration> schemas,
            IReadOnlyList<CatalogRegistrationGroup> groups,
            IReadOnlyList<string> features,
            CatalogDiagnosticBag diagnostics)
        {
            // The generator drives the production catalog rules so a description cannot emit a catalog that
            // ImmutableCatalog.Build would reject at runtime (P-009).
            HashSet<string> schemaIds = new HashSet<string>(StringComparer.Ordinal);
            List<FactoryRegistration> factories = new List<FactoryRegistration>();
            List<SchemaRegistration> schemaRegistrations = new List<SchemaRegistration>();

            for (int g = 0; g < groups.Count; g++)
            {
                CatalogRegistrationGroup group = groups[g];
                FactoryKind kind = CatalogFactoryKinds.Parse(group.Kind);

                for (int e = 0; e < group.Entries.Count; e++)
                {
                    CatalogRegistrationEntry entry = group.Entries[e];
                    Id128 key = StableNameKeyDerivation.Derive(entry.StableName);
                    factories.Add(new FactoryRegistration(
                        new FactoryKey(key, entry.KeyVersion),
                        kind,
                        ParseIdentity(entry.OwnerPackageIdHex),
                        ParseIdentity(entry.ImplementationIdHex),
                        entry.KeyVersion));
                }
            }

            for (int s = 0; s < schemas.Count; s++)
            {
                CatalogSchemaDeclaration schema = schemas[s];
                if (!schemaIds.Add(schema.SchemaIdHex))
                {
                    diagnostics.Add(
                        CatalogDiagnosticCode.DuplicateSchemaId,
                        "schemas[" + s.ToString(CultureInfo.InvariantCulture) + "].schemaId",
                        "schema identity " + schema.SchemaIdHex + " is declared by more than one schema");
                    continue;
                }

                Id128 serializerKey = StableNameKeyDerivation.Derive(schema.StableName);
                factories.Add(new FactoryRegistration(
                    new FactoryKey(serializerKey, schema.SerializerKeyVersion),
                    FactoryKind.Serializer,
                    ParseIdentity(schema.OwnerPackageIdHex),
                    ParseIdentity(schema.SchemaIdHex),
                    schema.SchemaVersion));

                schemaRegistrations.Add(new SchemaRegistration(
                    new SchemaRef(new SchemaId(ParseIdentity(schema.SchemaIdHex)), schema.SchemaVersion),
                    ParseIdentity(schema.OwnerPackageIdHex),
                    new FactoryKey(serializerKey, schema.SerializerKeyVersion),
                    schema.IsRequired));
            }

            List<Id128> featureIds = new List<Id128>();
            for (int i = 0; i < features.Count; i++)
            {
                featureIds.Add(ParseIdentity(features[i]));
            }

            CatalogBuildResult build = ImmutableCatalog.Build(factories, schemaRegistrations, featureIds, null);
            if (!build.Succeeded)
            {
                diagnostics.Add(
                    CatalogDiagnosticCode.InvalidCatalog,
                    string.Empty,
                    "the description does not produce a valid catalog: " + build.Describe());
            }
        }

        private static Id128 ParseIdentity(string hex)
        {
            if (!Id128Codec.TryParseHex(hex, out Id128 value))
            {
                throw new CatalogDescriptionException("'" + hex + "' is not a canonical 32-character lowercase hexadecimal id.");
            }

            return value;
        }

        private static string? RequiredString(JsonValue owner, string member, string path, CatalogDiagnosticBag diagnostics)
        {
            JsonValue? element = owner.Member(member);
            if (element == null)
            {
                diagnostics.Add(CatalogDiagnosticCode.MissingMember, path, "required member '" + member + "' is absent");
                return null;
            }

            return RequireString(element, path, diagnostics);
        }

        private static string? RequireString(JsonValue element, string path, CatalogDiagnosticBag diagnostics)
        {
            if (element.Kind != JsonKind.String)
            {
                diagnostics.Add(CatalogDiagnosticCode.InvalidValue, path, "must be a string; read " + element.Describe());
                return null;
            }

            return element.Text;
        }

        private static string? RequiredIdentifier(JsonValue owner, string member, string path, CatalogDiagnosticBag diagnostics)
        {
            string? text = RequiredString(owner, member, path, diagnostics);
            if (text == null)
            {
                return null;
            }

            if (!IsIdentifier(text))
            {
                diagnostics.Add(
                    CatalogDiagnosticCode.InvalidIdentifier,
                    path,
                    "'" + text + "' is not a valid C# identifier");
                return null;
            }

            return text;
        }

        private static string? RequiredTypeExpression(JsonValue owner, string member, string path, CatalogDiagnosticBag diagnostics)
        {
            string? text = RequiredString(owner, member, path, diagnostics);
            if (text == null)
            {
                return null;
            }

            if (!IsTypeExpression(text))
            {
                diagnostics.Add(
                    CatalogDiagnosticCode.InvalidIdentifier,
                    path,
                    "'" + text + "' is not a valid C# type expression (letters, digits, '.', '_', '<', '>', ',', ' ', '?', '[', ']')");
                return null;
            }

            return text;
        }

        private static string? RequiredCodeFragment(JsonValue owner, string member, string path, CatalogDiagnosticBag diagnostics)
        {
            string? text = RequiredString(owner, member, path, diagnostics);
            if (text == null)
            {
                return null;
            }

            string? reason = CatalogCodeFragments.DescribeRejection(text, allowStatementAttributes: false);
            if (reason != null)
            {
                diagnostics.Add(CatalogDiagnosticCode.InvalidCodeFragment, path, reason);
                return null;
            }

            return text;
        }

        private static string? RequiredStableName(JsonValue owner, string path, CatalogDiagnosticBag diagnostics)
        {
            string? text = RequiredString(owner, "stableName", Join(path, "stableName"), diagnostics);
            if (text == null)
            {
                return null;
            }

            if (!StableNameKeyDerivation.IsCanonicalStableName(text))
            {
                diagnostics.Add(
                    CatalogDiagnosticCode.InvalidStableName,
                    Join(path, "stableName"),
                    "'" + text + "' is not a canonical stable name; only " + StableNameKeyDerivation.AllowedCharacters +
                    " are accepted and the name cannot start or end with '.'");
                return null;
            }

            return text;
        }

        private static string? RequiredIdentity(
            JsonValue owner,
            string member,
            string path,
            bool allowAllZero,
            CatalogDiagnosticBag diagnostics)
        {
            string? text = RequiredString(owner, member, path, diagnostics);
            if (text == null)
            {
                return null;
            }

            if (!IsIdentityHex(text))
            {
                diagnostics.Add(
                    CatalogDiagnosticCode.InvalidIdentityHex,
                    path,
                    "expected exactly 32 lowercase hexadecimal characters; read '" + text + "'");
                return null;
            }

            if (!allowAllZero && IsAllZeroHex(text))
            {
                diagnostics.Add(
                    CatalogDiagnosticCode.InvalidIdentityHex,
                    path,
                    "the all-zero id is not a catalog identity (P-004)");
                return null;
            }

            return text;
        }

        private static uint RequiredUInt32(JsonValue owner, string member, string path, CatalogDiagnosticBag diagnostics)
        {
            JsonValue? element = owner.Member(member);
            if (element == null)
            {
                diagnostics.Add(CatalogDiagnosticCode.MissingMember, path, "required member '" + member + "' is absent");
                return 0U;
            }

            if (element.Kind != JsonKind.Number)
            {
                diagnostics.Add(CatalogDiagnosticCode.InvalidValue, path, "must be a number; read " + element.Describe());
                return 0U;
            }

            uint value;
            if (!uint.TryParse(element.Text, NumberStyles.None, CultureInfo.InvariantCulture, out value))
            {
                diagnostics.Add(
                    CatalogDiagnosticCode.InvalidValue,
                    path,
                    "must be an unsigned 32-bit decimal integer; read " + element.Text);
                return 0U;
            }

            if (value == 0U)
            {
                diagnostics.Add(
                    CatalogDiagnosticCode.InvalidValue,
                    path,
                    "must be a positive version; version 0 is not an accepted declaration");
            }

            return value;
        }

        private static int RequiredInt32(JsonValue owner, string member, string path, CatalogDiagnosticBag diagnostics)
        {
            JsonValue? element = owner.Member(member);
            if (element == null)
            {
                diagnostics.Add(CatalogDiagnosticCode.MissingMember, path, "required member '" + member + "' is absent");
                return 0;
            }

            if (element.Kind != JsonKind.Number)
            {
                diagnostics.Add(CatalogDiagnosticCode.InvalidValue, path, "must be a number; read " + element.Describe());
                return 0;
            }

            int value;
            if (!int.TryParse(element.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
            {
                diagnostics.Add(
                    CatalogDiagnosticCode.InvalidValue,
                    path,
                    "must be a signed 32-bit decimal integer; read " + element.Text);
                return 0;
            }

            return value;
        }

        private static bool RequiredBool(JsonValue owner, string member, string path, CatalogDiagnosticBag diagnostics)
        {
            JsonValue? element = owner.Member(member);
            if (element == null)
            {
                diagnostics.Add(CatalogDiagnosticCode.MissingMember, path, "required member '" + member + "' is absent");
                return false;
            }

            if (element.Kind != JsonKind.Boolean)
            {
                diagnostics.Add(CatalogDiagnosticCode.InvalidValue, path, "must be true or false; read " + element.Describe());
                return false;
            }

            return string.Equals(element.Text, "true", StringComparison.Ordinal);
        }

        private static string? RequiredFactoryKind(JsonValue group, string groupPath, CatalogDiagnosticBag diagnostics)
        {
            string path = Join(groupPath, "kind");
            string? text = RequiredString(group, "kind", path, diagnostics);
            if (text == null)
            {
                return null;
            }

            if (!CatalogFactoryKinds.IsKnown(text))
            {
                diagnostics.Add(
                    CatalogDiagnosticCode.InvalidValue,
                    path,
                    "unknown factory kind '" + text + "'; the accepted kinds are " + CatalogFactoryKinds.DescribeSupported());
                return null;
            }

            return text;
        }

        private static bool IsIdentityHex(string text)
        {
            if (text.Length != 32)
            {
                return false;
            }

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAllZeroHex(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '0')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsIdentifier(string text)
        {
            if (text.Length == 0 || !IsIdentifierStart(text[0]))
            {
                return false;
            }

            for (int i = 1; i < text.Length; i++)
            {
                char c = text[i];
                if (!IsIdentifierStart(c) && !(c >= '0' && c <= '9'))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsDottedIdentifier(string text)
        {
            string[] parts = text.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                if (!IsIdentifier(parts[i]))
                {
                    return false;
                }
            }

            return parts.Length != 0;
        }

        private static bool IsIdentifierStart(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';

        private static bool IsTypeExpression(string text)
        {
            if (text.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool allowed = IsIdentifierStart(c) || (c >= '0' && c <= '9') || c == '.' || c == '_' ||
                               c == '<' || c == '>' || c == ',' || c == ' ' || c == '?' || c == '[' || c == ']';
                if (!allowed)
                {
                    return false;
                }
            }

            return true;
        }

        private static string Join(string prefix, string member) => prefix.Length == 0 ? member : prefix + "." + member;

        private static string Describe(JsonValue? value) => value == null ? "nothing (member absent)" : value.Describe();
    }

    /// <summary>Thrown when a validated description cannot be materialized; a generator defect, not user input.</summary>
    public sealed class CatalogDescriptionException : Exception
    {
        public CatalogDescriptionException(string message)
            : base(message)
        {
        }
    }
}
