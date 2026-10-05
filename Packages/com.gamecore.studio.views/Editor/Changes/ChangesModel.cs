// GameCore.Studio.Views - W-VIEW-06 model, part 2: the diagnostics console, the package dependency graph and the
// journal timeline.
//
// Diagnostics console: every [AuthorValidator] class (model or mirror attribute, found through TypeCache) is asked
// through its public static Validate(<object>) overloads - one parameter assignable from an indexed object - and its
// ValidateScene(Scene) for every open scene. Results are read by member name (Code, SubjectId, Message; the gameplay
// GameplayDiagnostic shape) and de-duplicated (several validators delegate to the same logic validator).
//
// Dependencies: the com.gamecore.* packages Unity registered for the project, their package.json dependencies, and
// the dependencies their asmdefs imply. Problems are a read-only C# re-implementation of tools/check_package_metadata.py
// rules: version 1.0.0, unity 6000.0, displayName/description present (metadata); the com.gamecore.* dependency set
// equals the asmdef-derived set (rules 1-3); Newtonsoft.Json.dll as a precompiled reference needs
// com.unity.nuget.newtonsoft-json (rule 9); a kernel package never depends on gameplay, Studio or rules.gameplay
// (rule 11). Engine allowlist pins and lock-file rules stay with the Python tool.
//
// Journal timeline: every journal record (newest first) grouped by UTC day, with the intent origin, state, operation
// count and the artifact bytes it carries; two entries compare their before stamps (baseVersions) and after stamps
// (each outcome's undo inverse after-witnesses).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Studio.Views
{
    /// <summary>Runs the project's authoring validators over the indexed objects and the open scenes.</summary>
    public static class ValidatorConsole
    {
        public static IReadOnlyList<Type> ValidatorTypes()
        {
            List<Type> types = new List<Type>();
            HashSet<Type> seen = new HashSet<Type>();
            foreach (Type type in TypeCache.GetTypesWithAttribute<AuthorValidatorAttribute>())
            {
                if (seen.Add(type))
                {
                    types.Add(type);
                }
            }

            foreach (Type attribute in TypeCache.GetTypesDerivedFrom<Attribute>())
            {
                if (!AuthoringMetadata.IsMirror(attribute, AuthoringMetadata.AuthorValidatorName))
                {
                    continue;
                }

                foreach (Type type in TypeCache.GetTypesWithAttribute(attribute))
                {
                    if (seen.Add(type))
                    {
                        types.Add(type);
                    }
                }
            }

            types.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            return types;
        }

        /// <summary>The validator id of a validator class (its attribute's Id, else its type name).</summary>
        public static string IdOf(Type validator)
        {
            foreach (object attribute in validator.GetCustomAttributes(false))
            {
                if (attribute.GetType().Name == AuthoringMetadata.AuthorValidatorName)
                {
                    PropertyInfo? id = attribute.GetType().GetProperty("Id");
                    if (id?.GetValue(attribute) is string text && text.Length > 0)
                    {
                        return text;
                    }
                }
            }

            return validator.Name;
        }

        /// <summary>Every validator result for the indexed objects of <paramref name="graph"/> and the open scenes.</summary>
        public static IReadOnlyList<DiagnosticRow> Run(StudioRuntime runtime, IndexGraph graph)
        {
            List<DiagnosticRow> rows = new List<DiagnosticRow>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<KeyValuePair<UnityEngine.Object, AuthoringRef>> targets = new List<KeyValuePair<UnityEngine.Object, AuthoringRef>>();
            foreach (IndexNode node in graph.Nodes)
            {
                UnityEngine.Object? target = runtime.Resolver.Find(node.Ref);
                if (target != null)
                {
                    targets.Add(new KeyValuePair<UnityEngine.Object, AuthoringRef>(target, node.Ref));
                }
            }

            foreach (Type validator in ValidatorTypes())
            {
                string id = IdOf(validator);
                foreach (MethodInfo method in validator.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != 1)
                    {
                        continue;
                    }

                    Type parameter = parameters[0].ParameterType;
                    if (method.Name == "ValidateScene" && parameter == typeof(Scene))
                    {
                        for (int i = 0; i < SceneManager.sceneCount; i++)
                        {
                            Scene scene = SceneManager.GetSceneAt(i);
                            if (scene.isLoaded)
                            {
                                Collect(rows, seen, id, Call(method, scene), null);
                            }
                        }

                        continue;
                    }

                    if (method.Name != "Validate" || !typeof(UnityEngine.Object).IsAssignableFrom(parameter))
                    {
                        continue;
                    }

                    foreach (KeyValuePair<UnityEngine.Object, AuthoringRef> target in targets)
                    {
                        if (parameter.IsInstanceOfType(target.Key))
                        {
                            Collect(rows, seen, id, Call(method, target.Key), target.Value);
                        }
                    }
                }
            }

            return rows;
        }

        private static object? Call(MethodInfo method, object argument)
        {
            try
            {
                return method.Invoke(null, new[] { argument });
            }
            catch (TargetInvocationException error)
            {
                return new[] { new Diagnostic("ValidatorFailed", method.DeclaringType?.Name + "." + method.Name + " threw: " + (error.InnerException ?? error).Message) };
            }
        }

        private static void Collect(List<DiagnosticRow> rows, HashSet<string> seen, string source, object? result, AuthoringRef? subject)
        {
            if (!(result is System.Collections.IEnumerable items))
            {
                return;
            }

            foreach (object? item in items)
            {
                if (item == null)
                {
                    continue;
                }

                Diagnostic diagnostic;
                string subjectId;
                if (item is Diagnostic studio)
                {
                    diagnostic = studio;
                    subjectId = studio.Where?.ToString() ?? string.Empty;
                }
                else
                {
                    string code = item.GetType().GetProperty("Code")?.GetValue(item) as string ?? "Diagnostic";
                    string message = item.GetType().GetProperty("Message")?.GetValue(item) as string ?? item.ToString();
                    subjectId = item.GetType().GetProperty("SubjectId")?.GetValue(item) as string ?? string.Empty;
                    diagnostic = new Diagnostic(code, message, null, subject == null ? null : DiagnosticWhere.At(SemanticIndexService.EdgeRef(subject)), subjectId.Length > 0 ? new JObject { ["subject"] = subjectId } : null);
                }

                if (seen.Add(diagnostic.Code + "|" + subjectId + "|" + diagnostic.Message))
                {
                    rows.Add(new DiagnosticRow(source, diagnostic, null) { Where = diagnostic.Where?.Ref ?? subject });
                }
            }
        }
    }

    /// <summary>One package of the dependency graph.</summary>
    public sealed class PackageNode
    {
        public PackageNode(string name, string directory, string version, string unity, string displayName, string description, IReadOnlyList<string> declared)
        {
            Name = name;
            Directory = directory;
            Version = version;
            Unity = unity;
            DisplayName = displayName;
            Description = description;
            Declared = declared;
        }

        public string Name { get; }

        public string Directory { get; }

        public string Version { get; }

        public string Unity { get; }

        public string DisplayName { get; }

        public string Description { get; }

        /// <summary>package.json dependencies (all, sorted).</summary>
        public IReadOnlyList<string> Declared { get; }

        /// <summary>Packages the asmdefs reference (com.gamecore.* plus newtonsoft for the precompiled DLL).</summary>
        public SortedSet<string> Derived { get; } = new SortedSet<string>(StringComparer.Ordinal);

        public List<string> Problems { get; } = new List<string>();

        /// <summary>0 kernel, 1 rules, 2 unity, 3 gameplay, 4 studio.</summary>
        public int Layer => PackageGraph.LayerOf(Name);
    }

    public sealed class PackageGraph
    {
        public const string NewtonsoftPackage = "com.unity.nuget.newtonsoft-json";

        public static readonly IReadOnlyList<string> KernelPackages = new[]
        {
            "com.gamecore.contracts", "com.gamecore.composition", "com.gamecore.derivation", "com.gamecore.planning",
            "com.gamecore.content.compiler", "com.gamecore.rules.cards", "com.gamecore.rules.narrative", "com.gamecore.rules.traversal",
        };

        public static readonly IReadOnlyList<string> LayerNames = new[] { "kernel", "rules", "unity", "gameplay", "studio" };

        public PackageGraph(IReadOnlyList<PackageNode> packages)
        {
            Packages = packages;
        }

        public IReadOnlyList<PackageNode> Packages { get; }

        public int ProblemCount
        {
            get
            {
                int count = 0;
                foreach (PackageNode package in Packages)
                {
                    count += package.Problems.Count;
                }

                return count;
            }
        }

        public PackageNode? Find(string name)
        {
            foreach (PackageNode package in Packages)
            {
                if (package.Name == name)
                {
                    return package;
                }
            }

            return null;
        }

        public static int LayerOf(string name)
        {
            foreach (string kernel in KernelPackages)
            {
                if (kernel == name)
                {
                    return 0;
                }
            }

            if (name.StartsWith("com.gamecore.rules.", StringComparison.Ordinal))
            {
                return 1;
            }

            if (name.StartsWith("com.gamecore.unity.", StringComparison.Ordinal))
            {
                return 2;
            }

            if (name.StartsWith("com.gamecore.gameplay.", StringComparison.Ordinal))
            {
                return 3;
            }

            if (name.StartsWith("com.gamecore.studio.", StringComparison.Ordinal))
            {
                return 4;
            }

            return 2;
        }

        /// <summary>The project's registered com.gamecore.* packages.</summary>
        public static PackageGraph ForProject()
        {
            List<string> directories = new List<string>();
            foreach (UnityEditor.PackageManager.PackageInfo info in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
            {
                if (info.name.StartsWith("com.gamecore.", StringComparison.Ordinal) && !string.IsNullOrEmpty(info.resolvedPath))
                {
                    directories.Add(info.resolvedPath);
                }
            }

            return FromDirectories(directories);
        }

        /// <summary>The packages in <paramref name="directories"/> (each holding a package.json).</summary>
        public static PackageGraph FromDirectories(IEnumerable<string> directories)
        {
            List<PackageNode> packages = new List<PackageNode>();
            Dictionary<string, string> assemblyToPackage = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, List<JObject>> asmdefs = new Dictionary<string, List<JObject>>(StringComparer.Ordinal);
            foreach (string directory in directories)
            {
                string manifestPath = Path.Combine(directory, "package.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                JObject manifest;
                try
                {
                    manifest = JObject.Parse(File.ReadAllText(manifestPath));
                }
                catch (Exception error) when (error is IOException || error is Newtonsoft.Json.JsonException)
                {
                    continue;
                }

                string name = manifest.Value<string>("name") ?? Path.GetFileName(directory);
                List<string> declared = new List<string>();
                if (manifest["dependencies"] is JObject dependencies)
                {
                    foreach (JProperty dependency in dependencies.Properties())
                    {
                        declared.Add(dependency.Name);
                    }
                }

                declared.Sort(StringComparer.Ordinal);
                packages.Add(new PackageNode(name, directory, manifest.Value<string>("version") ?? string.Empty, manifest.Value<string>("unity") ?? string.Empty, manifest.Value<string>("displayName") ?? string.Empty, manifest.Value<string>("description") ?? string.Empty, declared));
                List<JObject> definitions = new List<JObject>();
                foreach (string path in Directory.GetFiles(directory, "*.asmdef", SearchOption.AllDirectories))
                {
                    try
                    {
                        JObject asmdef = JObject.Parse(File.ReadAllText(path));
                        definitions.Add(asmdef);
                        string? assembly = asmdef.Value<string>("name");
                        if (!string.IsNullOrEmpty(assembly) && !assemblyToPackage.ContainsKey(assembly!))
                        {
                            assemblyToPackage.Add(assembly!, name);
                        }
                    }
                    catch (Exception error) when (error is IOException || error is Newtonsoft.Json.JsonException)
                    {
                        continue;
                    }
                }

                asmdefs[name] = definitions;
            }

            foreach (PackageNode package in packages)
            {
                foreach (JObject asmdef in asmdefs[package.Name])
                {
                    if (asmdef["references"] is JArray references)
                    {
                        foreach (JToken reference in references)
                        {
                            string text = reference.ToString();
                            if (assemblyToPackage.TryGetValue(text, out string? owner) && owner != package.Name)
                            {
                                package.Derived.Add(owner);
                            }
                        }
                    }

                    if (asmdef["precompiledReferences"] is JArray precompiled)
                    {
                        foreach (JToken dll in precompiled)
                        {
                            if (dll.ToString() == "Newtonsoft.Json.dll")
                            {
                                package.Derived.Add(NewtonsoftPackage);
                            }
                        }
                    }
                }

                Check(package);
            }

            packages.Sort((a, b) =>
            {
                int layer = a.Layer.CompareTo(b.Layer);
                return layer != 0 ? layer : string.CompareOrdinal(a.Name, b.Name);
            });
            return new PackageGraph(packages);
        }

        private static void Check(PackageNode package)
        {
            if (package.Version != "1.0.0")
            {
                package.Problems.Add("version " + package.Version + " (expected 1.0.0)");
            }

            if (package.Unity != "6000.0")
            {
                package.Problems.Add("unity " + package.Unity + " (expected 6000.0)");
            }

            if (package.DisplayName.Trim().Length == 0 || package.Description.Trim().Length == 0)
            {
                package.Problems.Add("displayName and description must be present");
            }

            SortedSet<string> declared = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string dependency in package.Declared)
            {
                if (dependency.StartsWith("com.gamecore.", StringComparison.Ordinal) || dependency == NewtonsoftPackage)
                {
                    declared.Add(dependency);
                }
            }

            foreach (string missing in package.Derived)
            {
                if (!declared.Contains(missing))
                {
                    package.Problems.Add("missing dependency " + missing + " (an asmdef references it)");
                }
            }

            foreach (string extra in declared)
            {
                if (!package.Derived.Contains(extra))
                {
                    package.Problems.Add("extra dependency " + extra + " (no asmdef references it)");
                }
            }

            if (package.Layer == 0)
            {
                foreach (string dependency in package.Declared)
                {
                    if (dependency.StartsWith("com.gamecore.gameplay.", StringComparison.Ordinal) || dependency.StartsWith("com.gamecore.studio.", StringComparison.Ordinal) || dependency == "com.gamecore.rules.gameplay")
                    {
                        package.Problems.Add("kernel package depends on " + dependency + " (P-057/SADR-014)");
                    }
                }
            }
        }
    }

    /// <summary>One journal entry on the timeline.</summary>
    public sealed class TimelineEntry
    {
        public TimelineEntry(JournalRecord record, ChangeSet? entry, long journalBytes)
        {
            Record = record;
            Entry = entry;
            Time = SafeTime(record.Id);
            Origin = entry?.Intent.Origin.ToString() ?? "unknown";
            JournalBytes = journalBytes;
            long artifacts = 0;
            if (entry?.Artifacts != null)
            {
                foreach (ArtifactRef artifact in entry.Artifacts)
                {
                    artifacts += artifact.Bytes;
                }
            }

            ArtifactBytes = artifacts;
        }

        public JournalRecord Record { get; }

        public ChangeSet? Entry { get; }

        public string Id => Record.Id;

        public DateTime Time { get; }

        public string Day => Time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public string Origin { get; }

        public long JournalBytes { get; }

        public long ArtifactBytes { get; }

        public int OperationCount => Entry?.Operations.Count ?? 0;

        /// <summary>Target identity key -> stamp before the change set (baseVersions).</summary>
        public Dictionary<string, string> Before()
        {
            Dictionary<string, string> stamps = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Entry?.BaseVersions != null)
            {
                foreach (BaseVersion version in Entry.BaseVersions)
                {
                    stamps[version.Ref.IdentityKey] = version.Stamp;
                }
            }

            if (Entry != null)
            {
                foreach (Operation operation in Entry.Operations)
                {
                    if (operation.Target?.Stamp != null && !stamps.ContainsKey(operation.Target.IdentityKey))
                    {
                        stamps[operation.Target.IdentityKey] = operation.Target.Stamp;
                    }
                }
            }

            return stamps;
        }

        /// <summary>Target identity key -> stamp after the change set (each outcome's undo after-witnesses).</summary>
        public Dictionary<string, string> After()
        {
            Dictionary<string, string> stamps = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Entry?.Outcomes == null)
            {
                return stamps;
            }

            foreach (OperationOutcome outcome in Entry.Outcomes)
            {
                UndoPayload? undo = UndoPayload.Parse(outcome.Undo?.Inverse, out _);
                if (undo == null)
                {
                    continue;
                }

                foreach (StampWitness witness in undo.After)
                {
                    stamps[witness.Ref.IdentityKey] = witness.Stamp;
                }
            }

            return stamps;
        }

        private static DateTime SafeTime(string id)
        {
            try
            {
                return Journal.TimeOf(id);
            }
            catch (Exception error) when (error is FormatException || error is ArgumentException)
            {
                return DateTime.MinValue;
            }
        }
    }

    /// <summary>One row of a stamp diff between two journal entries.</summary>
    public sealed class StampDiffRow
    {
        public StampDiffRow(string key, string? beforeA, string? afterA, string? beforeB, string? afterB)
        {
            Key = key;
            BeforeA = beforeA;
            AfterA = afterA;
            BeforeB = beforeB;
            AfterB = afterB;
        }

        public string Key { get; }

        public string? BeforeA { get; }

        public string? AfterA { get; }

        public string? BeforeB { get; }

        public string? AfterB { get; }

        /// <summary>The target changed between A's result and B's starting point (someone else edited it).</summary>
        public bool ChangedBetween => AfterA != null && BeforeB != null && !string.Equals(AfterA, BeforeB, StringComparison.Ordinal);
    }

    public static class JournalTimeline
    {
        public const int MaxEntries = 500;

        /// <summary>The newest journal entries (up to <see cref="MaxEntries"/>), optionally of one origin.</summary>
        public static IReadOnlyList<TimelineEntry> Load(StudioRuntime runtime, string? origin = null)
        {
            List<JournalRecord> records = new List<JournalRecord>(runtime.Journal.List());
            records.Sort((a, b) => string.CompareOrdinal(b.Id, a.Id));
            List<TimelineEntry> entries = new List<TimelineEntry>();
            foreach (JournalRecord record in records)
            {
                if (entries.Count >= MaxEntries)
                {
                    break;
                }

                ChangeSet? entry = runtime.Journal.Read(record.Id);
                TimelineEntry item = new TimelineEntry(record, entry, File.Exists(record.Path) ? new FileInfo(record.Path).Length : 0L);
                if (origin == null || string.Equals(item.Origin, origin, StringComparison.OrdinalIgnoreCase))
                {
                    entries.Add(item);
                }
            }

            return entries;
        }

        /// <summary>Before/after stamps of every target either entry touched.</summary>
        public static IReadOnlyList<StampDiffRow> Diff(TimelineEntry a, TimelineEntry b)
        {
            Dictionary<string, string> beforeA = a.Before();
            Dictionary<string, string> afterA = a.After();
            Dictionary<string, string> beforeB = b.Before();
            Dictionary<string, string> afterB = b.After();
            SortedSet<string> keys = new SortedSet<string>(StringComparer.Ordinal);
            keys.UnionWith(beforeA.Keys);
            keys.UnionWith(afterA.Keys);
            keys.UnionWith(beforeB.Keys);
            keys.UnionWith(afterB.Keys);
            List<StampDiffRow> rows = new List<StampDiffRow>();
            foreach (string key in keys)
            {
                rows.Add(new StampDiffRow(key, Get(beforeA, key), Get(afterA, key), Get(beforeB, key), Get(afterB, key)));
            }

            return rows;
        }

        public static string Bytes(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes + " B";
            }

            return bytes < 1024 * 1024 ? (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB" : (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        private static string? Get(Dictionary<string, string> map, string key) => map.TryGetValue(key, out string? value) ? value : null;
    }
}
