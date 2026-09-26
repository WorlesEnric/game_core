#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using GameCore.Gameplay.Traversal;
using GameCore.Rules.Traversal;
using UnityEditor;
using UnityEngine;

namespace GameCore.Validation.Editor
{
    /// <summary>
    /// The Editor half of GC-025's bake/runtime recipe parity check (04 section 6, TEST-020).
    ///
    /// Baking is an Editor workflow, and the player instantiates baked entities or invokes precompiled factories
    /// instead of invoking a baker. This baker therefore converts the authored target document
    /// (<c>Catalogs/CatalogCoverageAuthoring.json</c>) into a committed, compiled artifact
    /// (<c>Assets/GameCore.Validation/Generated/CatalogCoverageBaked.g.cs</c>): the artifact carries stable names and
    /// authored values, and the player derives every identity with the production rule at its use site.
    ///
    /// The bake validates the authored document against the traversal package's own declared vocabulary before it
    /// writes anything, so an authoring document that names a recipe, an applier or a selector the runtime does not
    /// declare fails the bake instead of producing an artifact that disagrees with the runtime recipe. The write is
    /// deterministic (fixed order, LF endings, no timestamps and no machine paths), so re-running it on the same
    /// authoring document reproduces the committed bytes and the gate's `git diff` can prove it.
    ///
    /// Callable from batchmode with
    /// <c>-executeMethod GameCore.Validation.Editor.BakeCatalogCoverageAuthoring.Bake</c>.
    /// </summary>
    public static class BakeCatalogCoverageAuthoring
    {
        /// <summary>Repository-relative authoring document, recorded inside the artifact.</summary>
        public const string RelativeAuthoringPath = "Catalogs/CatalogCoverageAuthoring.json";

        /// <summary>Project-relative artifact the bake writes and the player compiles.</summary>
        public const string RelativeArtifactPath =
            "Assets/GameCore.Validation/Generated/CatalogCoverageBaked.g.cs";

        private const string DescriptionFormat = "gamecore.catalog-coverage-authoring/1";
        private const string ArtifactFormat = "gamecore.catalog-coverage-baked/1";
        private const string BakedBy = "GameCore.Validation.Editor.BakeCatalogCoverageAuthoring.Bake";

        /// <summary>Batchmode entry point: validate, bake, write, refresh. Throws on any inconsistency.</summary>
        public static void Bake()
        {
            string projectRoot = ProjectRoot();
            string authoringPath = Path.Combine(projectRoot, RelativeAuthoringPath);
            string artifactPath = Path.Combine(projectRoot, RelativeArtifactPath);

            if (!File.Exists(authoringPath))
            {
                throw new FileNotFoundException(
                    "the catalog-coverage authoring document is missing; expected " + RelativeAuthoringPath,
                    authoringPath);
            }

            string authoringText = File.ReadAllText(authoringPath, new UTF8Encoding(false));
            AuthoredRunner runner = Parse(authoringText);
            ValidateAgainstVocabulary(runner);

            string artifact = Emit(authoringText, runner);
            File.WriteAllText(artifactPath, artifact, new UTF8Encoding(false));
            AssetDatabase.Refresh();

            Debug.Log(
                "[GC025] baked " + RelativeArtifactPath
                + "; authoringHash=" + Sha256Hex(authoringText)
                + "; bytes=" + Encoding.UTF8.GetByteCount(artifact).ToString(CultureInfo.InvariantCulture)
                + "; role=" + runner.Role);
        }

        /// <summary>Verification-only entry point: fails when the committed artifact is stale.</summary>
        public static void Verify()
        {
            string projectRoot = ProjectRoot();
            string authoringPath = Path.Combine(projectRoot, RelativeAuthoringPath);
            string artifactPath = Path.Combine(projectRoot, RelativeArtifactPath);

            string authoringText = File.ReadAllText(authoringPath, new UTF8Encoding(false));
            string expected = Emit(authoringText, Parse(authoringText));
            string committed = File.ReadAllText(artifactPath, new UTF8Encoding(false));
            if (!string.Equals(expected, committed, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "the committed baked artifact is stale; re-run Bake (" + RelativeArtifactPath + ")");
            }

            Debug.Log("[GC025] verified " + RelativeArtifactPath + "; authoringHash=" + Sha256Hex(authoringText));
        }

        /// <summary>
        /// Checks every stable name the authored document declares against the traversal package's own vocabulary,
        /// so a bake cannot produce an artifact that disagrees with the runtime recipe it is compared against
        /// (P-004, P-009, P-015).
        /// </summary>
        private static void ValidateAgainstVocabulary(AuthoredRunner runner)
        {
            if (!string.Equals(runner.RecipeDefinitionStableName, TraversalVocabulary.RunnerRecipe, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "the authored runner recipe definition '" + runner.RecipeDefinitionStableName
                    + "' is not the runtime runner recipe '" + TraversalVocabulary.RunnerRecipe + "'");
            }

            if (!string.Equals(runner.RecipeSchemaStableName, TraversalVocabulary.RunnerRecipe, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "the authored runner recipe schema '" + runner.RecipeSchemaStableName
                    + "' is not the runtime runner recipe schema '" + TraversalVocabulary.RunnerRecipe + "'");
            }

            if (!string.Equals(runner.ApplierStableName, "traversal.recipe.applier.runner", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "the authored runner applier '" + runner.ApplierStableName
                    + "' is not the runtime runner applier 'traversal.recipe.applier.runner'");
            }

            string[] requiredLayout = { TraversalVocabulary.RunnerRecipe, TraversalVocabulary.AccelerationTarget };
            if (runner.BaseLayoutSchemaStableNames.Count != requiredLayout.Length)
            {
                throw new InvalidOperationException(
                    "the authored runner base layout declares " + runner.BaseLayoutSchemaStableNames.Count
                    + " selector(s); the runtime runner recipe declares " + requiredLayout.Length);
            }

            for (int i = 0; i < requiredLayout.Length; i++)
            {
                if (!string.Equals(runner.BaseLayoutSchemaStableNames[i], requiredLayout[i], StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "the authored runner base layout selector " + i.ToString(CultureInfo.InvariantCulture)
                        + " is '" + runner.BaseLayoutSchemaStableNames[i] + "'; the runtime runner recipe declares '"
                        + requiredLayout[i] + "'");
                }
            }

            if (runner.DescriptorTagStableNames.Count != 1
                || !string.Equals(runner.DescriptorTagStableNames[0], TraversalVocabulary.RunnerRecipe, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "the authored runner descriptor must advertise exactly the runtime runner recipe tag '"
                    + TraversalVocabulary.RunnerRecipe + "'");
            }

            if (runner.InitialVelocityMilli != new int[] { TraversalVocabulary.SeededVelocityMilli, 0, 0 })
            {
                throw new InvalidOperationException(
                    "the authored runner velocity is not the reference's seeded "
                    + TraversalVocabulary.SeededVelocityMilli.ToString(CultureInfo.InvariantCulture)
                    + " mm/s along x");
            }

            if (runner.RecipeRevision != 1U || runner.RecipeSchemaVersion != 1U || runner.ApplierKeyVersion != 1U)
            {
                throw new InvalidOperationException(
                    "the authored revision/schema/key versions must be the runtime recipe's own (1, 1, 1)");
            }
        }

        /// <summary>
        /// The artifact's exact template. Deterministic: fixed declaration order, LF line endings, no timestamps,
        /// no machine paths, no culture-sensitive formatting.
        /// </summary>
        private static string Emit(string authoringText, AuthoredRunner runner)
        {
            StringBuilder outText = new StringBuilder(4 * 1024);
            outText.Append("// <auto-generated />\n");
            outText.Append("// Generated by GameCore.Validation.Editor.BakeCatalogCoverageAuthoring; do not edit by hand.\n");
            outText.Append("//\n");
            outText.Append("// Baked artifact of ").Append(RelativeAuthoringPath)
                .Append(" (04 section 6: baking is an Editor workflow, and the player\n");
            outText.Append("// instantiates baked definitions rather than invoking a baker). Every identity below is a stable name:\n");
            outText.Append("// the player derives the 128-bit ids with the production rule at the use site, so this artifact carries no\n");
            outText.Append("// CLR assembly name, no engine handle and no precomputed identity that could drift from the vocabulary\n");
            outText.Append("// the runtime recipe is declared with (P-004). Regenerating from the same authoring document must reproduce\n");
            outText.Append("// this file byte for byte.\n");
            outText.Append("#nullable enable\n");
            outText.Append('\n');
            outText.Append("namespace GameCore.Validation.Generated\n");
            outText.Append("{\n");
            outText.Append("    /// <summary>\n");
            outText.Append("    /// Editor-baked target definitions the catalog-coverage probe instantiates alongside the runtime recipe\n");
            outText.Append("    /// catalog, so TEST-020's bake/runtime recipe parity is a comparison of two materialized targets\n");
            outText.Append("    /// rather than of two descriptions.\n");
            outText.Append("    /// </summary>\n");
            outText.Append("    public static class CatalogCoverageBaked\n");
            outText.Append("    {\n");
            outText.Append("        /// <summary>Format id of this baked artifact.</summary>\n");
            outText.Append("        public const string Format = \"").Append(ArtifactFormat).Append("\";\n");
            outText.Append('\n');
            outText.Append("        /// <summary>Repository-relative authoring document this artifact was baked from.</summary>\n");
            outText.Append("        public const string AuthoringDocument = \"").Append(RelativeAuthoringPath).Append("\";\n");
            outText.Append('\n');
            outText.Append("        /// <summary>SHA-256 over the UTF-8 bytes of the authoring document, recorded by the bake.</summary>\n");
            outText.Append("        public const string AuthoringHash = \"").Append(Sha256Hex(authoringText)).Append("\";\n");
            outText.Append('\n');
            outText.Append("        /// <summary>Editor entry point that produces this artifact.</summary>\n");
            outText.Append("        public const string BakedBy = \"").Append(BakedBy).Append("\";\n");
            outText.Append('\n');
            outText.Append("        /// <summary>Stable role name of the one baked target.</summary>\n");
            outText.Append("        public const string RunnerRole = \"").Append(runner.Role).Append("\";\n");
            outText.Append('\n');
            outText.Append("        /// <summary>Stable definition name of the baked runner recipe (P-004).</summary>\n");
            outText.Append("        public const string RunnerRecipeDefinitionStableName = \"")
                .Append(runner.RecipeDefinitionStableName).Append("\";\n");
            outText.Append('\n');
            outText.Append("        /// <summary>Stable schema name the baked runner recipe declares; its `SchemaRef` version is below.</summary>\n");
            outText.Append("        public const string RunnerRecipeSchemaStableName = \"")
                .Append(runner.RecipeSchemaStableName).Append("\";\n");
            outText.Append('\n');
            outText.Append("        /// <summary>Schema version the baked runner recipe declares.</summary>\n");
            outText.Append("        public const uint RunnerRecipeSchemaVersion = ")
                .Append(runner.RecipeSchemaVersion.ToString(CultureInfo.InvariantCulture)).Append("U;\n");
            outText.Append('\n');
            outText.Append("        /// <summary>Immutable definition revision the baked runner recipe carries (05 section 2).</summary>\n");
            outText.Append("        public const uint RunnerRecipeRevision = ")
                .Append(runner.RecipeRevision.ToString(CultureInfo.InvariantCulture)).Append("U;\n");
            outText.Append('\n');
            outText.Append("        /// <summary>Stable registration name of the base-layout applier the baked recipe resolves.</summary>\n");
            outText.Append("        public const string RunnerApplierStableName = \"")
                .Append(runner.ApplierStableName).Append("\";\n");
            outText.Append('\n');
            outText.Append("        /// <summary>Key version the baked recipe's applier registration carries.</summary>\n");
            outText.Append("        public const uint RunnerApplierKeyVersion = ")
                .Append(runner.ApplierKeyVersion.ToString(CultureInfo.InvariantCulture)).Append("U;\n");
            outText.Append('\n');
            outText.Append("        /// <summary>Base-layout selector schemas the baked recipe installs, in declared order.</summary>\n");
            outText.Append("        public static readonly string[] RunnerBaseLayoutSchemaStableNames =\n");
            outText.Append("        {\n");
            for (int i = 0; i < runner.BaseLayoutSchemaStableNames.Count; i++)
            {
                outText.Append("            \"").Append(runner.BaseLayoutSchemaStableNames[i]).Append("\",\n");
            }

            outText.Append("        };\n");
            outText.Append('\n');
            outText.Append("        /// <summary>Immutable descriptor tags the baked recipe advertises, in declared order (P-015).</summary>\n");
            outText.Append("        public static readonly string[] RunnerDescriptorTagStableNames =\n");
            outText.Append("        {\n");
            for (int i = 0; i < runner.DescriptorTagStableNames.Count; i++)
            {
                outText.Append("            \"").Append(runner.DescriptorTagStableNames[i]).Append("\",\n");
            }

            outText.Append("        };\n");
            outText.Append('\n');
            outText.Append("        /// <summary>Position the baked recipe installs on a runner, in millimetres.</summary>\n");
            outText.Append("        public const int RunnerInitialPositionXMilli = ")
                .Append(runner.InitialPositionMilli[0].ToString(CultureInfo.InvariantCulture)).Append(";\n");
            outText.Append("        public const int RunnerInitialPositionYMilli = ")
                .Append(runner.InitialPositionMilli[1].ToString(CultureInfo.InvariantCulture)).Append(";\n");
            outText.Append("        public const int RunnerInitialPositionZMilli = ")
                .Append(runner.InitialPositionMilli[2].ToString(CultureInfo.InvariantCulture)).Append(";\n");
            outText.Append('\n');
            outText.Append("        /// <summary>Velocity the baked recipe installs on a runner, in thousandths of a metre per second.</summary>\n");
            outText.Append("        public const int RunnerInitialVelocityXMilli = ")
                .Append(runner.InitialVelocityMilli[0].ToString(CultureInfo.InvariantCulture)).Append(";\n");
            outText.Append("        public const int RunnerInitialVelocityYMilli = ")
                .Append(runner.InitialVelocityMilli[1].ToString(CultureInfo.InvariantCulture)).Append(";\n");
            outText.Append("        public const int RunnerInitialVelocityZMilli = ")
                .Append(runner.InitialVelocityMilli[2].ToString(CultureInfo.InvariantCulture)).Append(";\n");
            outText.Append('\n');
            outText.Append("        /// <summary>Number of baked targets this artifact declares.</summary>\n");
            outText.Append("        public const int BakedTargetCount = 1;\n");
            outText.Append("    }\n");
            outText.Append("}\n");
            return outText.ToString();
        }

        /// <summary>
        /// Reads the authored document. The document is a small fixed shape, so the baker reads it with the same
        /// member-by-member discipline the content compiler's description reader applies to a catalog description:
        /// unknown members are errors, not ignored, and a missing member is named in the diagnostic.
        /// </summary>
        private static AuthoredRunner Parse(string text)
        {
            object? parsed = MiniJson.Parse(text);
            if (!(parsed is Dictionary<string, object?> root))
            {
                throw new InvalidOperationException("the authoring document must be a JSON object");
            }

            RequireMembers(root, "the authoring document", new[]
            {
                "descriptionFormat", "protocolVersion", "bakedBy", "targets",
            });

            if (!string.Equals(String(root, "descriptionFormat"), DescriptionFormat, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("descriptionFormat must be '" + DescriptionFormat + "'");
            }

            if (!string.Equals(String(root, "protocolVersion"), "1.0", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("protocolVersion must be 1.0");
            }

            if (root["targets"] is not List<object?> targets || targets.Count == 0)
            {
                throw new InvalidOperationException("targets must be a non-empty array");
            }

            AuthoredRunner? runner = null;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] is not Dictionary<string, object?> target)
                {
                    throw new InvalidOperationException("targets[" + i.ToString(CultureInfo.InvariantCulture) + "] must be an object");
                }

                RequireMembers(target, "targets[" + i.ToString(CultureInfo.InvariantCulture) + "]", new[]
                {
                    "role", "recipeDefinitionStableName", "recipeSchemaStableName", "recipeSchemaVersion",
                    "recipeRevision", "applierStableName", "applierKeyVersion", "baseLayoutSchemaStableNames",
                    "descriptorTagStableNames", "initialPositionMilli", "initialVelocityMilli",
                });

                string role = String(target, "role");
                if (!string.Equals(role, "runner", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("the only baked role this artifact declares is 'runner'; read '" + role + "'");
                }

                if (runner != null)
                {
                    throw new InvalidOperationException("the role 'runner' is declared twice");
                }

                runner = new AuthoredRunner(
                    role,
                    String(target, "recipeDefinitionStableName"),
                    String(target, "recipeSchemaStableName"),
                    UInt32(target, "recipeSchemaVersion"),
                    UInt32(target, "recipeRevision"),
                    String(target, "applierStableName"),
                    UInt32(target, "applierKeyVersion"),
                    Strings(target, "baseLayoutSchemaStableNames"),
                    Strings(target, "descriptorTagStableNames"),
                    Ints(target, "initialPositionMilli"),
                    Ints(target, "initialVelocityMilli"));
            }

            return runner ?? throw new InvalidOperationException("the authoring document declares no runner target");
        }

        private static void RequireMembers(Dictionary<string, object?> owner, string where, string[] known)
        {
            foreach (string key in owner.Keys)
            {
                bool found = false;
                for (int i = 0; i < known.Length; i++)
                {
                    if (string.Equals(known[i], key, StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    throw new InvalidOperationException(where + ": unknown member '" + key + "'");
                }
            }

            for (int i = 0; i < known.Length; i++)
            {
                if (!owner.ContainsKey(known[i]))
                {
                    throw new InvalidOperationException(where + ": member '" + known[i] + "' is required");
                }
            }
        }

        private static string String(Dictionary<string, object?> owner, string key)
        {
            if (owner[key] is string value && value.Length != 0)
            {
                return value;
            }

            throw new InvalidOperationException("'" + key + "' must be a non-empty string");
        }

        private static uint UInt32(Dictionary<string, object?> owner, string key)
        {
            if (owner[key] is int value && value > 0)
            {
                return (uint)value;
            }

            throw new InvalidOperationException("'" + key + "' must be a positive integer");
        }

        private static List<string> Strings(Dictionary<string, object?> owner, string key)
        {
            if (owner[key] is not List<object?> items)
            {
                throw new InvalidOperationException("'" + key + "' must be an array");
            }

            var values = new List<string>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is string text && text.Length != 0)
                {
                    values.Add(text);
                    continue;
                }

                throw new InvalidOperationException("'" + key + "[" + i.ToString(CultureInfo.InvariantCulture) + "]' must be a non-empty string");
            }

            return values;
        }

        private static int[] Ints(Dictionary<string, object?> owner, string key)
        {
            if (owner[key] is not Dictionary<string, object?> vector)
            {
                throw new InvalidOperationException("'" + key + "' must be an object");
            }

            RequireMembers(vector, key, new[] { "x", "y", "z" });
            var values = new int[3];
            string[] axes = { "x", "y", "z" };
            for (int i = 0; i < axes.Length; i++)
            {
                if (vector[axes[i]] is int number)
                {
                    values[i] = number;
                    continue;
                }

                throw new InvalidOperationException("'" + key + "." + axes[i] + "' must be an integer");
            }

            return values;
        }

        private static string Sha256Hex(string content)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(new UTF8Encoding(false).GetBytes(content));
                var builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private static string ProjectRoot()
        {
            string dataPath = Application.dataPath.TrimEnd('/', '\\');
            string? root = Path.GetDirectoryName(dataPath);
            if (string.IsNullOrEmpty(root))
            {
                throw new InvalidOperationException(
                    "cannot derive the project root from Application.dataPath=" + Application.dataPath);
            }

            return root;
        }

        /// <summary>One authored runner target, as the bake validated it.</summary>
        private sealed class AuthoredRunner
        {
            internal AuthoredRunner(
                string role,
                string recipeDefinitionStableName,
                string recipeSchemaStableName,
                uint recipeSchemaVersion,
                uint recipeRevision,
                string applierStableName,
                uint applierKeyVersion,
                List<string> baseLayoutSchemaStableNames,
                List<string> descriptorTagStableNames,
                int[] initialPositionMilli,
                int[] initialVelocityMilli)
            {
                Role = role;
                RecipeDefinitionStableName = recipeDefinitionStableName;
                RecipeSchemaStableName = recipeSchemaStableName;
                RecipeSchemaVersion = recipeSchemaVersion;
                RecipeRevision = recipeRevision;
                ApplierStableName = applierStableName;
                ApplierKeyVersion = applierKeyVersion;
                BaseLayoutSchemaStableNames = baseLayoutSchemaStableNames;
                DescriptorTagStableNames = descriptorTagStableNames;
                InitialPositionMilli = initialPositionMilli;
                InitialVelocityMilli = initialVelocityMilli;
            }

            internal string Role { get; }

            internal string RecipeDefinitionStableName { get; }

            internal string RecipeSchemaStableName { get; }

            internal uint RecipeSchemaVersion { get; }

            internal uint RecipeRevision { get; }

            internal string ApplierStableName { get; }

            internal uint ApplierKeyVersion { get; }

            internal List<string> BaseLayoutSchemaStableNames { get; }

            internal List<string> DescriptorTagStableNames { get; }

            internal int[] InitialPositionMilli { get; }

            internal int[] InitialVelocityMilli { get; }
        }

        /// <summary>
        /// A minimal JSON reader for the authored document. It lives here, in Editor-only code, because the baker
        /// must not depend on the content compiler's catalog description reader: that reader validates catalog
        /// descriptions, and this document is an authoring document, not a catalog.
        /// </summary>
        private static class MiniJson
        {
            internal static object? Parse(string text)
            {
                int index = 0;
                object? value = ParseValue(text, ref index);
                SkipWhitespace(text, ref index);
                if (index != text.Length)
                {
                    throw new InvalidOperationException("trailing content at offset " + index.ToString(CultureInfo.InvariantCulture));
                }

                return value;
            }

            private static object? ParseValue(string text, ref int index)
            {
                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                {
                    throw new InvalidOperationException("unexpected end of document");
                }

                char c = text[index];
                switch (c)
                {
                    case '{':
                        return ParseObject(text, ref index);
                    case '[':
                        return ParseArray(text, ref index);
                    case '"':
                        return ParseString(text, ref index);
                    case 't':
                        Expect(text, ref index, "true");
                        return true;
                    case 'f':
                        Expect(text, ref index, "false");
                        return false;
                    case 'n':
                        Expect(text, ref index, "null");
                        return null;
                    default:
                        return ParseNumber(text, ref index);
                }
            }

            private static Dictionary<string, object?> ParseObject(string text, ref int index)
            {
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                index++;
                SkipWhitespace(text, ref index);
                if (index < text.Length && text[index] == '}')
                {
                    index++;
                    return result;
                }

                while (true)
                {
                    SkipWhitespace(text, ref index);
                    string key = ParseString(text, ref index);
                    SkipWhitespace(text, ref index);
                    if (index >= text.Length || text[index] != ':')
                    {
                        throw new InvalidOperationException("expected ':' after member '" + key + "'");
                    }

                    index++;
                    if (result.ContainsKey(key))
                    {
                        throw new InvalidOperationException("member '" + key + "' is declared twice");
                    }

                    result.Add(key, ParseValue(text, ref index));
                    SkipWhitespace(text, ref index);
                    if (index < text.Length && text[index] == ',')
                    {
                        index++;
                        continue;
                    }

                    if (index < text.Length && text[index] == '}')
                    {
                        index++;
                        return result;
                    }

                    throw new InvalidOperationException("expected ',' or '}' in an object");
                }
            }

            private static List<object?> ParseArray(string text, ref int index)
            {
                var result = new List<object?>();
                index++;
                SkipWhitespace(text, ref index);
                if (index < text.Length && text[index] == ']')
                {
                    index++;
                    return result;
                }

                while (true)
                {
                    result.Add(ParseValue(text, ref index));
                    SkipWhitespace(text, ref index);
                    if (index < text.Length && text[index] == ',')
                    {
                        index++;
                        continue;
                    }

                    if (index < text.Length && text[index] == ']')
                    {
                        index++;
                        return result;
                    }

                    throw new InvalidOperationException("expected ',' or ']' in an array");
                }
            }

            private static string ParseString(string text, ref int index)
            {
                if (index >= text.Length || text[index] != '"')
                {
                    throw new InvalidOperationException("expected a string at offset " + index.ToString(CultureInfo.InvariantCulture));
                }

                index++;
                var builder = new StringBuilder();
                while (index < text.Length)
                {
                    char c = text[index++];
                    if (c == '"')
                    {
                        return builder.ToString();
                    }

                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }

                    if (index >= text.Length)
                    {
                        throw new InvalidOperationException("unterminated escape sequence");
                    }

                    char escape = text[index++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (index + 4 > text.Length)
                            {
                                throw new InvalidOperationException("truncated \\u escape sequence");
                            }

                            builder.Append((char)int.Parse(text.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            index += 4;
                            break;
                        default:
                            throw new InvalidOperationException("unknown escape '\\" + escape + "'");
                    }
                }

                throw new InvalidOperationException("unterminated string");
            }

            private static object ParseNumber(string text, ref int index)
            {
                int start = index;
                if (index < text.Length && (text[index] == '-' || text[index] == '+'))
                {
                    index++;
                }

                while (index < text.Length && text[index] >= '0' && text[index] <= '9')
                {
                    index++;
                }

                if (index < text.Length && (text[index] == '.' || text[index] == 'e' || text[index] == 'E'))
                {
                    throw new InvalidOperationException("a fractional number is not a valid authored value");
                }

                string digits = text.Substring(start, index - start);
                if (digits.Length == 0)
                {
                    throw new InvalidOperationException("expected a number at offset " + start.ToString(CultureInfo.InvariantCulture));
                }

                return int.Parse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            }

            private static void Expect(string text, ref int index, string literal)
            {
                if (index + literal.Length > text.Length
                    || !string.Equals(text.Substring(index, literal.Length), literal, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("expected '" + literal + "' at offset " + index.ToString(CultureInfo.InvariantCulture));
                }

                index += literal.Length;
            }

            private static void SkipWhitespace(string text, ref int index)
            {
                while (index < text.Length)
                {
                    char c = text[index];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                    {
                        index++;
                        continue;
                    }

                    return;
                }
            }
        }
    }
}
