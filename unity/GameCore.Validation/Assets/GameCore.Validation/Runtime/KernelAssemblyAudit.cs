// GameCore.Validation.ProbeHost — the W3 gate's kernel-separation audit (P-001, P-059; 04 section 2).
//
// Wave 3 runs two genuinely different compositions on ONE kernel, and that kernel must not know either of them: the
// dependency edge between a kernel assembly and a gameplay package points one way only (04 section 2). Two
// inspections prove it from the two directions the host can reach, and neither substitutes for the other:
//
//   * AuditLoadedAssemblies() reads the loaded assembly graph of the running process, which is also available inside
//     an IL2CPP player: every declared kernel assembly's referenced assemblies are checked against the gameplay,
//     rules, validation and generated prefixes, and every loaded gameplay/rules assembly must reference at least one
//     kernel assembly. A declared kernel assembly is also required to be loaded exactly once, so "the same kernel"
//     cannot be two copies of one assembly. It sees only the assemblies this host actually loads.
//   * AuditAsmdefReferences(repositoryRoot) reads the build-time `.asmdef` files, which a player cannot see: every
//     assembly definition inside a kernel package must reference no gameplay/rules/validation/generated assembly,
//     and every gameplay/rules assembly definition must reference the kernel. It is discovery-complete over the
//     kernel packages but needs the project tree, so it runs in the Editor (and on the build host), not in a player.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>Result of one loaded-assembly separation audit of the running process.</summary>
    public sealed class LoadedAssemblyReport
    {
        internal LoadedAssemblyReport(
            IReadOnlyList<string> kernelAssemblies,
            IReadOnlyList<string> duplicateKernelAssemblies,
            IReadOnlyList<string> forbiddenReferences,
            IReadOnlyList<string> inspectionFailures,
            IReadOnlyList<string> gameplayFamilyAssemblies,
            IReadOnlyList<string> gameplayFamilyOnKernel,
            int kernelReferenceCount,
            string identity)
        {
            KernelAssemblies = kernelAssemblies;
            DuplicateKernelAssemblies = duplicateKernelAssemblies;
            ForbiddenReferences = forbiddenReferences;
            InspectionFailures = inspectionFailures;
            GameplayFamilyAssemblies = gameplayFamilyAssemblies;
            GameplayFamilyOnKernel = gameplayFamilyOnKernel;
            KernelReferenceCount = kernelReferenceCount;
            Identity = identity;
        }

        /// <summary>Loaded assemblies of the declared kernel set, in canonical name order.</summary>
        public IReadOnlyList<string> KernelAssemblies { get; }

        /// <summary>Kernel assembly names loaded more than once: two copies of one kernel assembly are a defect.</summary>
        public IReadOnlyList<string> DuplicateKernelAssemblies { get; }

        /// <summary>Kernel-to-gameplay/rules/validation/generated edges found; empty is the required value.</summary>
        public IReadOnlyList<string> ForbiddenReferences { get; }

        /// <summary>Assemblies whose referenced assemblies could not be read; empty is the required value.</summary>
        public IReadOnlyList<string> InspectionFailures { get; }

        /// <summary>Loaded gameplay and rules assemblies, in canonical name order.</summary>
        public IReadOnlyList<string> GameplayFamilyAssemblies { get; }

        /// <summary>Gameplay/rules assemblies that reference at least one kernel assembly (04 section 2).</summary>
        public IReadOnlyList<string> GameplayFamilyOnKernel { get; }

        /// <summary>Total referenced assemblies examined across the declared kernel set.</summary>
        public int KernelReferenceCount { get; }

        /// <summary>
        /// Identity of the loaded kernel image: one `name=fullName` line per kernel assembly. Two audits of the same
        /// process return the same text unless the loaded kernel set changed between them.
        /// </summary>
        public string Identity { get; }

        /// <summary>True when no forbidden edge, no duplicate kernel assembly and no unreadable reference was found.</summary>
        public bool Clean =>
            ForbiddenReferences.Count == 0
            && DuplicateKernelAssemblies.Count == 0
            && InspectionFailures.Count == 0;

        /// <summary>One-line digest naming every value this audit observed.</summary>
        public string Describe() =>
            "kernelAssemblies=" + Join(KernelAssemblies)
            + "; duplicateKernelAssemblies=" + Join(DuplicateKernelAssemblies)
            + "; kernelReferenceCount=" + KernelReferenceCount.ToString(CultureInfo.InvariantCulture)
            + "; forbiddenReferences=" + Join(ForbiddenReferences)
            + "; inspectionFailures=" + Join(InspectionFailures)
            + "; gameplayFamilyAssemblies=" + Join(GameplayFamilyAssemblies)
            + "; gameplayFamilyOnKernel=" + Join(GameplayFamilyOnKernel);

        private static string Join(IReadOnlyList<string> values) =>
            values.Count == 0 ? "<none>" : string.Join(",", ToArray(values));

        private static string[] ToArray(IReadOnlyList<string> values)
        {
            var array = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                array[i] = values[i];
            }

            return array;
        }
    }

    /// <summary>Result of one build-time `.asmdef` reference audit of the repository.</summary>
    public sealed class AsmdefReferenceReport
    {
        internal AsmdefReferenceReport(
            IReadOnlyList<string> kernelAsmdefs,
            IReadOnlyList<string> gameplayAsmdefs,
            IReadOnlyList<string> gameplayAsmdefsOnKernel,
            IReadOnlyList<string> violations,
            IReadOnlyList<string> missingPackages,
            int kernelReferenceCount)
        {
            KernelAsmdefs = kernelAsmdefs;
            GameplayAsmdefs = gameplayAsmdefs;
            GameplayAsmdefsOnKernel = gameplayAsmdefsOnKernel;
            Violations = violations;
            MissingPackages = missingPackages;
            KernelReferenceCount = kernelReferenceCount;
        }

        /// <summary>Every assembly definition found inside a kernel package, as a repository-relative path.</summary>
        public IReadOnlyList<string> KernelAsmdefs { get; }

        /// <summary>Every assembly definition found inside a gameplay or rules package.</summary>
        public IReadOnlyList<string> GameplayAsmdefs { get; }

        /// <summary>Gameplay/rules assembly definitions that reference at least one kernel assembly.</summary>
        public IReadOnlyList<string> GameplayAsmdefsOnKernel { get; }

        /// <summary>Kernel assembly definitions referencing a gameplay/rules/validation/generated assembly.</summary>
        public IReadOnlyList<string> Violations { get; }

        /// <summary>Kernel or gameplay package directories that do not exist; empty is the required value.</summary>
        public IReadOnlyList<string> MissingPackages { get; }

        /// <summary>Total references declared by the kernel assembly definitions.</summary>
        public int KernelReferenceCount { get; }

        /// <summary>True when every expected package exists and no kernel definition references gameplay.</summary>
        public bool Clean => Violations.Count == 0 && MissingPackages.Count == 0;

        /// <summary>One-line digest naming every value this audit observed.</summary>
        public string Describe() =>
            "kernelAsmdefs=" + KernelAsmdefs.Count.ToString(CultureInfo.InvariantCulture)
            + "; kernelReferenceCount=" + KernelReferenceCount.ToString(CultureInfo.InvariantCulture)
            + "; gameplayAsmdefs=" + GameplayAsmdefs.Count.ToString(CultureInfo.InvariantCulture)
            + "; gameplayAsmdefsOnKernel=" + GameplayAsmdefsOnKernel.Count.ToString(CultureInfo.InvariantCulture)
            + "; violations=" + (Violations.Count == 0 ? "<none>" : string.Join(",", Copy(Violations)))
            + "; missingPackages=" + (MissingPackages.Count == 0 ? "<none>" : string.Join(",", Copy(MissingPackages)));

        private static string[] Copy(IReadOnlyList<string> values)
        {
            var array = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                array[i] = values[i];
            }

            return array;
        }
    }

    /// <summary>
    /// The W3 gate's kernel-separation audit. The declared sets come from 04 section 2, which names every assembly of
    /// the kernel and gives each gameplay package exactly two allowed references into it.
    /// </summary>
    public static class KernelAssemblyAudit
    {
        /// <summary>
        /// The kernel assemblies of 04 section 2: every row above `GameCore.Gameplay.&lt;Name&gt;`, plus the kernel
        /// fixture assemblies the gates run on and the Editor-only content compiler. No gameplay or rules assembly
        /// belongs here, which is what makes the negative direction below meaningful.
        /// </summary>
        public static readonly IReadOnlyList<string> KernelAssemblyNames = new[]
        {
            "GameCore.Contracts",
            "GameCore.Composition",
            "GameCore.Derivation",
            "GameCore.Derivation.Fixtures",
            "GameCore.Planning",
            "GameCore.Unity.Runtime",
            "GameCore.Unity.Fixtures",
            "GameCore.Unity.Adapters",
            "GameCore.Content.Compiler",
            "GameCore.Content.Compiler.Editor",
        };

        /// <summary>Assembly-name prefixes a kernel assembly must never reference (04 section 2).</summary>
        private static readonly string[] ForbiddenPrefixes =
        {
            "GameCore.Gameplay.",
            "GameCore.Rules.",
            "GameCore.Validation.",
            "GameCore.Generated",
        };

        /// <summary>Package directories of the kernel, whose assembly definitions are audited by directory.</summary>
        private static readonly string[] KernelPackages =
        {
            "com.gamecore.contracts",
            "com.gamecore.composition",
            "com.gamecore.derivation",
            "com.gamecore.planning",
            "com.gamecore.unity.runtime",
            "com.gamecore.unity.adapters",
            "com.gamecore.content.compiler",
        };

        /// <summary>Package directories of the two Wave 3 gameplay families; every one must reference the kernel.</summary>
        private static readonly string[] GameplayPackages =
        {
            "com.gamecore.gameplay.cards",
            "com.gamecore.gameplay.narrative",
            "com.gamecore.rules.cards",
            "com.gamecore.rules.narrative",
        };

        private const int RepositorySearchDepth = 8;

        /// <summary>
        /// Reads the loaded assembly graph of this process. Works in the Editor and in an IL2CPP player; the player
        /// runs the same code path, so "the kernel references no gameplay package" is asserted on the shipped
        /// assemblies rather than only on the build tree.
        /// </summary>
        public static LoadedAssemblyReport AuditLoadedAssemblies()
        {
            var duplicates = new List<string>();
            var forbidden = new List<string>();
            var failures = new List<string>();
            var kernelNames = new List<string>();
            var identity = new List<string>();
            var gameplayNames = new List<string>();
            var gameplayOnKernel = new List<string>();
            int references = 0;

            Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
            var kernel = new List<Assembly>();
            var gameplay = new List<Assembly>();
            for (int i = 0; i < loaded.Length; i++)
            {
                string name = TrySimpleName(loaded[i], failures);
                if (name.Length == 0)
                {
                    continue;
                }

                if (IsKernelName(name))
                {
                    kernel.Add(loaded[i]);
                }
                else if (IsGameplayFamilyName(name))
                {
                    gameplay.Add(loaded[i]);
                }
            }

            kernel.Sort(CompareAssemblies);
            gameplay.Sort(CompareAssemblies);

            for (int i = 0; i < kernel.Count; i++)
            {
                string name = TrySimpleName(kernel[i], failures);
                if (kernelNames.Count != 0 && string.Equals(kernelNames[kernelNames.Count - 1], name, StringComparison.Ordinal))
                {
                    duplicates.Add(name);
                }

                kernelNames.Add(name);
                identity.Add(name + "=" + (kernel[i].FullName ?? name));
                if (!TryReadReferences(kernel[i], out AssemblyName[] referenced, out string failure))
                {
                    failures.Add(name + ": " + failure);
                    continue;
                }

                for (int r = 0; r < referenced.Length; r++)
                {
                    references++;
                    string referencedName = referenced[r].Name ?? string.Empty;
                    if (IsForbiddenReference(referencedName))
                    {
                        forbidden.Add(name + " -> " + referencedName);
                    }
                }
            }

            for (int i = 0; i < gameplay.Count; i++)
            {
                string name = TrySimpleName(gameplay[i], failures);
                gameplayNames.Add(name);
                if (!TryReadReferences(gameplay[i], out AssemblyName[] referenced, out string failure))
                {
                    failures.Add(name + ": " + failure);
                    continue;
                }

                for (int r = 0; r < referenced.Length; r++)
                {
                    if (IsKernelName(referenced[r].Name ?? string.Empty))
                    {
                        gameplayOnKernel.Add(name);
                        break;
                    }
                }
            }

            return new LoadedAssemblyReport(
                kernelNames,
                duplicates,
                forbidden,
                failures,
                gameplayNames,
                gameplayOnKernel,
                references,
                string.Join("\n", identity.ToArray()));
        }

        /// <summary>
        /// Audits the build-time assembly definitions of the kernel and gameplay packages under
        /// <paramref name="repositoryRoot"/>. Discovery-complete over the kernel package directories, so a new kernel
        /// assembly cannot enter the tree unaudited; requires the project tree, so the Editor and the build host call
        /// it and a player does not.
        /// </summary>
        public static AsmdefReferenceReport AuditAsmdefReferences(string repositoryRoot)
        {
            if (repositoryRoot == null)
            {
                throw new ArgumentNullException(nameof(repositoryRoot));
            }

            var kernelAsmdefs = new List<string>();
            var gameplayAsmdefs = new List<string>();
            var gameplayOnKernel = new List<string>();
            var violations = new List<string>();
            var missing = new List<string>();
            int references = 0;

            for (int i = 0; i < KernelPackages.Length; i++)
            {
                string directory = Path.Combine(repositoryRoot, "Packages", KernelPackages[i]);
                if (!Directory.Exists(directory))
                {
                    missing.Add("Packages/" + KernelPackages[i]);
                    continue;
                }

                string[] files = SortedAsmdefs(directory);
                for (int f = 0; f < files.Length; f++)
                {
                    string relative = Relative(repositoryRoot, files[f]);
                    kernelAsmdefs.Add(relative);
                    List<string> declared = ReadStringArray(File.ReadAllText(files[f]), "references");
                    references += declared.Count;
                    for (int r = 0; r < declared.Count; r++)
                    {
                        if (IsForbiddenReference(declared[r]))
                        {
                            violations.Add(relative + " -> " + declared[r]);
                        }
                    }
                }
            }

            for (int i = 0; i < GameplayPackages.Length; i++)
            {
                string directory = Path.Combine(repositoryRoot, "Packages", GameplayPackages[i]);
                if (!Directory.Exists(directory))
                {
                    missing.Add("Packages/" + GameplayPackages[i]);
                    continue;
                }

                string[] files = SortedAsmdefs(directory);
                for (int f = 0; f < files.Length; f++)
                {
                    string relative = Relative(repositoryRoot, files[f]);
                    gameplayAsmdefs.Add(relative);
                    List<string> declared = ReadStringArray(File.ReadAllText(files[f]), "references");
                    for (int r = 0; r < declared.Count; r++)
                    {
                        if (IsKernelName(declared[r]))
                        {
                            gameplayOnKernel.Add(relative);
                            break;
                        }
                    }
                }
            }

            return new AsmdefReferenceReport(
                kernelAsmdefs,
                gameplayAsmdefs,
                gameplayOnKernel,
                violations,
                missing,
                references);
        }

        /// <summary>
        /// Finds the repository root above <paramref name="startDirectory"/>, or null when this host has no project
        /// tree (an IL2CPP player). The marker is the kernel's own Unity runtime assembly definition plus the plain
        /// dotnet solution, so a nested worktree or an Editor launched from elsewhere both resolve correctly.
        /// </summary>
        public static string? TryFindRepositoryRoot(string startDirectory)
        {
            if (string.IsNullOrEmpty(startDirectory))
            {
                return null;
            }

            DirectoryInfo? directory = new DirectoryInfo(startDirectory);
            for (int depth = 0; depth < RepositorySearchDepth && directory != null; depth++)
            {
                bool kernel = File.Exists(Path.Combine(
                    directory.FullName, "Packages", "com.gamecore.unity.runtime", "Runtime", "GameCore.Unity.Runtime.asmdef"));
                bool dotnet = File.Exists(Path.Combine(directory.FullName, "dotnet", "GameCore.sln"));
                if (kernel && dotnet)
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return null;
        }

        /// <summary>True when this assembly name belongs to the declared kernel set.</summary>
        public static bool IsKernelName(string assemblyName)
        {
            for (int i = 0; i < KernelAssemblyNames.Count; i++)
            {
                if (string.Equals(KernelAssemblyNames[i], assemblyName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when this assembly name belongs to a gameplay or rules package (P-001, 04 section 2).</summary>
        public static bool IsGameplayFamilyName(string assemblyName) =>
            assemblyName.StartsWith("GameCore.Gameplay.", StringComparison.Ordinal)
            || assemblyName.StartsWith("GameCore.Rules.", StringComparison.Ordinal);

        private static bool IsForbiddenReference(string assemblyName)
        {
            for (int i = 0; i < ForbiddenPrefixes.Length; i++)
            {
                if (assemblyName.StartsWith(ForbiddenPrefixes[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The simple name of one loaded assembly, or the empty string when the runtime cannot report it; the
        /// failure is recorded rather than thrown, because an audit that dies on one assembly would report nothing.
        /// </summary>
        private static string TrySimpleName(Assembly assembly, List<string>? failures)
        {
            try
            {
                AssemblyName name = assembly.GetName();
                return name.Name ?? string.Empty;
            }
            catch (Exception exception)
            {
                if (failures != null)
                {
                    failures.Add("<unnamed>: " + exception.GetType().FullName + ": " + exception.Message);
                }

                return string.Empty;
            }
        }

        private static int CompareAssemblies(Assembly left, Assembly right) =>
            string.CompareOrdinal(TrySimpleName(left, null), TrySimpleName(right, null));

        private static bool TryReadReferences(Assembly assembly, out AssemblyName[] referenced, out string failure)
        {
            try
            {
                referenced = assembly.GetReferencedAssemblies();
                failure = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                referenced = Array.Empty<AssemblyName>();
                failure = exception.GetType().FullName + ": " + exception.Message;
                return false;
            }
        }

        private static string[] SortedAsmdefs(string directory)
        {
            string[] files = Directory.GetFiles(directory, "*.asmdef", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }

        /// <summary>
        /// A repository-relative, slash-separated path. Implemented by prefix stripping rather than
        /// <c>Path.GetRelativePath</c>, which is not in the .NET Standard 2.0 surface Unity may compile against.
        /// </summary>
        private static string Relative(string repositoryRoot, string path)
        {
            string normalisedRoot = repositoryRoot.Replace('\\', '/').TrimEnd('/') + "/";
            string normalisedPath = path.Replace('\\', '/');
            return normalisedPath.StartsWith(normalisedRoot, StringComparison.Ordinal)
                ? normalisedPath.Substring(normalisedRoot.Length)
                : normalisedPath;
        }

        /// <summary>
        /// Reads the string array of one JSON key. The assembly-definition format is fixed and flat, so a scanner
        /// that walks the file once is enough and it keeps this audit free of a JSON dependency in the player.
        /// </summary>
        private static List<string> ReadStringArray(string json, string key)
        {
            var values = new List<string>();
            int keyIndex = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (keyIndex < 0)
            {
                return values;
            }

            int open = json.IndexOf('[', keyIndex);
            int close = open < 0 ? -1 : json.IndexOf(']', open);
            if (open < 0 || close < 0)
            {
                return values;
            }

            string body = json.Substring(open + 1, close - open - 1);
            int index = 0;
            while (index < body.Length)
            {
                int quote = body.IndexOf('"', index);
                if (quote < 0)
                {
                    break;
                }

                int end = body.IndexOf('"', quote + 1);
                if (end < 0)
                {
                    break;
                }

                string value = body.Substring(quote + 1, end - quote - 1);
                if (value.Length != 0)
                {
                    values.Add(value);
                }

                index = end + 1;
            }

            return values;
        }
    }
}
