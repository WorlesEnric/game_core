#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GameCore.Stage.Analysis
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class Policy
    {
        [JsonRequired] public string Mode { get; set; } = "";
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class Rules
    {
        [JsonRequired] public string Schema { get; set; } = "";
        [JsonRequired] public string[] References { get; set; } = Array.Empty<string>();
        [JsonRequired] public string[] SupportSources { get; set; } = Array.Empty<string>();
        [JsonRequired] public Policy Policy { get; set; } = new Policy();
        public static Rules Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            Unique(document.RootElement);
            var rules = JsonSerializer.Deserialize<Rules>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? throw new InvalidDataException();
            if (rules.Schema != "gamecore.stage.analyze/1" || rules.Policy == null || rules.Policy.Mode != "D1" ||
                rules.References == null || rules.SupportSources == null ||
                rules.References.Concat(rules.SupportSources).Any(p => p == null || !Path.IsPathFullyQualified(p))) throw new InvalidDataException();
            return rules;
        }
        private static void Unique(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new InvalidDataException();
                    Unique(property.Value);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Unique(item);
        }
    }
    public static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length != 6 || args[0] != "--root" || args[2] != "--rules" || args[4] != "--out") return 2;
                var root = Path.GetFullPath(args[1]);
                if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
                var rules = Rules.Parse(File.ReadAllText(args[3]));
                var packageRoot = root;
                if (File.Exists(Path.Combine(root, "stage.json")))
                {
                    using var record = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "stage.json")));
                    var name = record.RootElement.GetProperty("package").GetProperty("name").GetString()!;
                    if (!Regex.IsMatch(name, "^[a-z0-9][a-z0-9.-]{2,213}$")) throw new InvalidDataException();
                    packageRoot = Path.Combine(root, "project", "Packages", name);
                }
                else if (Directory.Exists(Path.Combine(root, "package"))) packageRoot = Path.Combine(root, "package");
                if ((File.GetAttributes(packageRoot) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
                using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(packageRoot, "package.json")));
                var package = manifest.RootElement.GetProperty("name").GetString()!;
                if (!Regex.IsMatch(package, "^[a-z0-9][a-z0-9.-]{2,213}$")) throw new InvalidDataException();
                var sources = ReadSources(packageRoot, packageRoot).ToArray();
                if (sources.Length == 0) throw new InvalidDataException();
                var support = rules.SupportSources.SelectMany(p => ReadSources(p, p)).ToArray();
                var references = PlatformReferences().Concat(rules.References.Select(p => MetadataReference.CreateFromFile(Path.GetFullPath(p))));
                var findings = new Scan(package).Run(sources, references, support);
                File.WriteAllText(args[5], JsonSerializer.Serialize(new { schema = "gamecore.stage.findings/1", pass = findings.Count == 0, findings = findings.Select(f => new { rule = f.RuleId, file = f.File, line = f.Line, message = f.Message }) }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
                return findings.Count == 0 ? 0 : 3;
            }
            catch (Exception error) when (error is InvalidDataException || error is IOException || error is UnauthorizedAccessException || error is JsonException || error is ArgumentException || error is KeyNotFoundException || error is BadImageFormatException || error is InvalidOperationException || error is NotSupportedException)
            {
                Console.Error.WriteLine("stage_analyzer_error: invalid input or unavailable trusted analysis context");
                return 2;
            }
        }
        internal static IEnumerable<SyntaxTree> ReadSources(string directory, string relativeRoot)
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
            foreach (var item in Directory.EnumerateFileSystemEntries(directory).OrderBy(x => x, StringComparer.Ordinal))
            {
                var attributes = File.GetAttributes(item);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    foreach (var tree in ReadSources(item, relativeRoot)) yield return tree;
                }
                else if (string.Equals(Path.GetExtension(item), ".cs", StringComparison.OrdinalIgnoreCase))
                    yield return CSharpSyntaxTree.ParseText(File.ReadAllText(item), new CSharpParseOptions(LanguageVersion.CSharp9,
                        preprocessorSymbols: new[] { "UNITY_EDITOR", "UNITY_INCLUDE_TESTS", "UNITY_6000_0_OR_NEWER" }), Path.GetRelativePath(relativeRoot, item));
            }
        }
        public static IEnumerable<MetadataReference> PlatformReferences() =>
            ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidDataException()).Split(Path.PathSeparator)
                .Select(p => MetadataReference.CreateFromFile(p));
    }
}
