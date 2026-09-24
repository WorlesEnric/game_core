// Independent pure oracle (GC-002). Loads canonical case JSON with strict, explicit validation: a malformed
// case is a format error, never a silently skipped case.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameCore.ProtocolFixtures.Fixtures
{
    /// <summary>Loader for fixture case files. The format is documented in Data/result-schema.json.</summary>
    public static class FixtureLoader
    {
        private static readonly Regex RequirementIdPattern = new Regex("^P-[0-9]{3}$", RegexOptions.CultureInvariant);

        private static readonly Regex TestIdPattern = new Regex("^TEST-[0-9]{3}$", RegexOptions.CultureInvariant);

        /// <summary>Case files in stable name order; every file must contain a "cases" array.</summary>
        public static IReadOnlyList<FixtureFile> LoadDirectory(string directory, string repositoryRoot)
        {
            if (!Directory.Exists(directory))
            {
                throw new FixtureFormatException("Fixture directory not found: " + directory);
            }

            string[] files = Directory.GetFiles(directory, "*.json");
            Array.Sort(files, StringComparer.Ordinal);

            List<FixtureFile> loaded = new List<FixtureFile>(files.Length);
            foreach (string file in files)
            {
                loaded.Add(LoadFile(file, repositoryRoot));
            }

            return loaded;
        }

        public static FixtureFile LoadFile(string path, string repositoryRoot)
        {
            if (!File.Exists(path))
            {
                throw new FixtureFormatException("Fixture file not found: " + path);
            }

            string text = File.ReadAllText(path);
            string relative = Relativize(repositoryRoot, path);

            using (JsonDocument document = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }))
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new FixtureFormatException(relative + ": root must be an object.");
                }

                if (!root.TryGetProperty("cases", out JsonElement cases) || cases.ValueKind != JsonValueKind.Array)
                {
                    throw new FixtureFormatException(relative + ": root must contain a 'cases' array.");
                }

                List<FixtureCase> parsed = new List<FixtureCase>();
                foreach (JsonElement element in cases.EnumerateArray())
                {
                    parsed.Add(ParseCase(element, relative));
                }

                return new FixtureFile(relative, parsed);
            }
        }

        public static FixtureCase ParseCaseFromJson(string json)
        {
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                return ParseCase(document.RootElement, "<inline>");
            }
        }

        public static FixtureCase ParseCase(JsonElement element, string sourcePath)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new FixtureFormatException(sourcePath + ": each case must be an object.");
            }

            string caseId = RequireString(element, "caseId", sourcePath);
            string title = RequireString(element, "title", sourcePath);
            string kind = RequireString(element, "kind", sourcePath);
            IReadOnlyList<string> requirementIds = RequirePatternArray(element, "requirementIds", RequirementIdPattern, sourcePath, caseId);
            IReadOnlyList<string> testIds = RequirePatternArray(element, "testIds", TestIdPattern, sourcePath, caseId);

            if (!element.TryGetProperty("expected", out JsonElement expected) || expected.ValueKind != JsonValueKind.Object)
            {
                throw new FixtureFormatException(sourcePath + ": case '" + caseId + "' must contain an 'expected' object.");
            }

            string expectationText = RequireString(expected, "outcome", sourcePath + "/" + caseId);
            FixtureExpectation expectation;
            if (string.Equals(expectationText, "valid", StringComparison.Ordinal))
            {
                expectation = FixtureExpectation.Valid;
            }
            else if (string.Equals(expectationText, "invalid", StringComparison.Ordinal))
            {
                expectation = FixtureExpectation.Invalid;
            }
            else
            {
                throw new FixtureFormatException(
                    sourcePath + ": case '" + caseId + "' has expected.outcome '" + expectationText + "'; expected 'valid' or 'invalid'.");
            }

            string expectedCode = string.Empty;
            if (expected.TryGetProperty("code", out JsonElement codeElement))
            {
                if (codeElement.ValueKind != JsonValueKind.String)
                {
                    throw new FixtureFormatException(sourcePath + ": case '" + caseId + "' expected.code must be a string.");
                }

                expectedCode = codeElement.GetString() ?? string.Empty;
            }

            if (expectation == FixtureExpectation.Invalid && expectedCode.Length == 0)
            {
                throw new FixtureFormatException(sourcePath + ": case '" + caseId + "' is invalid but declares no expected rejection code.");
            }

            if (!element.TryGetProperty("parameters", out JsonElement parameters) || parameters.ValueKind != JsonValueKind.Object)
            {
                throw new FixtureFormatException(sourcePath + ": case '" + caseId + "' must contain a 'parameters' object.");
            }

            // JsonElement borrows its document's memory: clone it so the case outlives the disposed document.
            return new FixtureCase(caseId, title, requirementIds, testIds, kind, expectation, expectedCode, parameters.Clone(), sourcePath);
        }

        private static string Relativize(string repositoryRoot, string path)
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetFullPath(repositoryRoot);
            string prefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;
            return full.StartsWith(prefix, StringComparison.Ordinal) ? full.Substring(prefix.Length).Replace('\\', '/') : full;
        }

        private static string RequireString(JsonElement element, string name, string context)
        {
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            {
                throw new FixtureFormatException(context + ": '" + name + "' must be a non-empty string.");
            }

            string? text = value.GetString();
            if (string.IsNullOrEmpty(text))
            {
                throw new FixtureFormatException(context + ": '" + name + "' must be a non-empty string.");
            }

            return text;
        }

        private static IReadOnlyList<string> RequirePatternArray(
            JsonElement element,
            string name,
            Regex pattern,
            string context,
            string caseId)
        {
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            {
                throw new FixtureFormatException(context + ": case '" + caseId + "' must contain a '" + name + "' array.");
            }

            List<string> values = new List<string>();
            foreach (JsonElement item in value.EnumerateArray())
            {
                string? text = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (text == null || !pattern.IsMatch(text))
                {
                    throw new FixtureFormatException(
                        context + ": case '" + caseId + "' has '" + name + "' entry '" + item.ToString() + "' which does not match " + pattern + ".");
                }

                values.Add(text);
            }

            if (values.Count == 0)
            {
                throw new FixtureFormatException(context + ": case '" + caseId + "' must name at least one id in '" + name + "'.");
            }

            return values;
        }
    }
}
