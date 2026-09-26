// GameCore.ReferenceConformance — the automated assembly-reference audit (P-001, P-057, 04 s2).
//
// GC-024's definition of done includes "kernel assemblies depend on no example package, actor, quest or card type".
// The audit proves it from two independent directions, because neither direction alone can:
//
//   * the BUILD-TIME direction reads every `.asmdef` and every `dotnet/src/**.csproj` in the tree. It is complete
//     over the declared surface — a new kernel assembly cannot enter unaudited — but it sees only declarations, not
//     what the compiler actually bound. A player cannot do this at all (there is no project tree in a player).
//   * the RUNTIME direction (in the qualification project's own `KernelAssemblyAudit`) reads the loaded assembly
//     graph of the process, which is available in an IL2CPP player. It sees what the build really produced, but only
//     for the assemblies this process happened to load.
//
// This file is the build-time half, written in pure C# so it runs in the plain dotnet test project, in EditMode and
// on the build host. It also scans the kernel SOURCES for the genre tokens the kernel must not name: a reference can
// be dropped while a `using GameCore.Rules.Cards;` remains, and only a source scan catches that.
//
// Every verdict is data, and the document it writes is deterministic (canonical ordering, no paths outside the
// repository, no clock), so a run is reproducible byte for byte (P-008, P-060).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace GameCore.ReferenceConformance
{
    /// <summary>What one assembly definition is: kernel, a gameplay/rules family, or something else (tests, tooling).</summary>
    public enum AssemblyClass
    {
        /// <summary>A kernel/generic assembly (04 s2): it may never reference a family assembly.</summary>
        Kernel = 0,

        /// <summary>A gameplay or rules family assembly: it must reference the kernel, never the reverse.</summary>
        Family = 1,

        /// <summary>A generated registration assembly: known to the kernel, referenced only by composition roots.</summary>
        Generated = 2,

        /// <summary>A qualification, test or fixture assembly: allowed to reference both sides.</summary>
        Qualification = 3,
    }

    /// <summary>How one assembly definition declares its references.</summary>
    public enum ReferenceKind
    {
        /// <summary>A Unity `.asmdef` file, whose `references` array holds assembly names.</summary>
        Asmdef = 0,

        /// <summary>An SDK-style `.csproj`, whose `ProjectReference` items hold repository-relative paths.</summary>
        ProjectFile = 1,
    }

    /// <summary>One assembly definition the audit read.</summary>
    public sealed class AssemblyRecord
    {
        public AssemblyRecord(
            string name,
            string path,
            ReferenceKind kind,
            AssemblyClass classification,
            string packageRoot,
            bool noEngineReferences,
            IReadOnlyList<string> references)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Kind = kind;
            Classification = classification;
            PackageRoot = packageRoot ?? string.Empty;
            NoEngineReferences = noEngineReferences;
            References = references ?? Array.Empty<string>();
        }

        /// <summary>The assembly's own name, e.g. <c>GameCore.Composition</c>.</summary>
        public string Name { get; }

        /// <summary>The repository-relative path of the file that declares it.</summary>
        public string Path { get; }

        /// <summary>Which file format declared it.</summary>
        public ReferenceKind Kind { get; }

        /// <summary>What the assembly is.</summary>
        public AssemblyClass Classification { get; }

        /// <summary>The package or project directory the declaration lives in.</summary>
        public string PackageRoot { get; }

        /// <summary>True when the declaration forbids engine references (kernel purity, 01 s1).</summary>
        public bool NoEngineReferences { get; }

        /// <summary>Every declared reference, in the order the declaration lists them.</summary>
        public IReadOnlyList<string> References { get; }

        public override string ToString() => Name + " (" + Path + ")";
    }

    /// <summary>One violated rule: which declaration, which reference, and which clause forbids it.</summary>
    public readonly struct AssemblyViolation
    {
        public AssemblyViolation(string assembly, string path, string reference, string rule)
        {
            Assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Reference = reference ?? throw new ArgumentNullException(nameof(reference));
            Rule = rule ?? throw new ArgumentNullException(nameof(rule));
        }

        /// <summary>The declaring assembly.</summary>
        public string Assembly { get; }

        /// <summary>The declaration's repository-relative path.</summary>
        public string Path { get; }

        /// <summary>The reference that violates the rule.</summary>
        public string Reference { get; }

        /// <summary>The clause that forbids it.</summary>
        public string Rule { get; }

        public override string ToString() => Assembly + " -> " + Reference + " (" + Rule + "): " + Path;
    }

    /// <summary>One genre token the audit found in a kernel source file, which is itself a violation.</summary>
    public readonly struct GenreTokenFinding
    {
        public GenreTokenFinding(string path, int line, string token)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Line = line;
            Token = token ?? throw new ArgumentNullException(nameof(token));
        }

        /// <summary>The kernel source file, repository-relative.</summary>
        public string Path { get; }

        /// <summary>The 1-based line the token appears on.</summary>
        public int Line { get; }

        /// <summary>The genre token that was found.</summary>
        public string Token { get; }

        public override string ToString() => Path + ":" + Line.ToString(CultureInfo.InvariantCulture) + ": " + Token;
    }

    /// <summary>The whole audit: every declaration read, every violation, and the counts that make it falsifiable.</summary>
    public sealed class GenreAuditReport
    {
        private readonly List<AssemblyRecord> assemblies = new List<AssemblyRecord>();
        private readonly List<AssemblyViolation> violations = new List<AssemblyViolation>();
        private readonly List<GenreTokenFinding> kernelTokens = new List<GenreTokenFinding>();
        private readonly List<string> missingPackages = new List<string>();

        /// <summary>Every declaration the audit read, in canonical (name, path) order.</summary>
        public IReadOnlyList<AssemblyRecord> Assemblies => assemblies;

        /// <summary>Every violated reference rule.</summary>
        public IReadOnlyList<AssemblyViolation> Violations => violations;

        /// <summary>Every genre token found in a kernel source file (a different rule from a reference).</summary>
        public IReadOnlyList<GenreTokenFinding> KernelTokens => kernelTokens;

        /// <summary>Kernel or family package directories that do not exist; empty is the required value.</summary>
        public IReadOnlyList<string> MissingPackages => missingPackages;

        /// <summary>The repository root the audit read, or the empty string when none was given.</summary>
        public string RepositoryRoot { get; internal set; } = string.Empty;

        /// <summary>How many `.asmdef` files were read.</summary>
        public int AsmdefCount { get; internal set; }

        /// <summary>How many `.csproj` files were read.</summary>
        public int ProjectFileCount { get; internal set; }

        /// <summary>How many source files inside kernel packages were scanned.</summary>
        public int KernelSourceCount { get; internal set; }

        /// <summary>How many reference assertions the audit evaluated (the falsifiability count).</summary>
        public int ReferenceAssertionCount { get; internal set; }

        /// <summary>True when nothing was violated, nothing was missing, and the audit actually read something.</summary>
        public bool Clean => violations.Count == 0
            && kernelTokens.Count == 0
            && missingPackages.Count == 0
            && AsmdefCount > 0
            && ProjectFileCount > 0
            && KernelSourceCount > 0
            && ReferenceAssertionCount > 0;

        /// <summary>How many declarations of one class were read.</summary>
        public int CountOf(AssemblyClass classification)
        {
            int count = 0;
            for (int i = 0; i < assemblies.Count; i++)
            {
                if (assemblies[i].Classification == classification)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>The kernel assemblies the audit read, in canonical order.</summary>
        public IReadOnlyList<string> KernelAssemblies()
        {
            var names = new List<string>();
            for (int i = 0; i < assemblies.Count; i++)
            {
                if (assemblies[i].Classification == AssemblyClass.Kernel)
                {
                    names.Add(assemblies[i].Name);
                }
            }

            return names;
        }

        /// <summary>The family assemblies the audit read, in canonical order.</summary>
        public IReadOnlyList<string> FamilyAssemblies()
        {
            var names = new List<string>();
            for (int i = 0; i < assemblies.Count; i++)
            {
                if (assemblies[i].Classification == AssemblyClass.Family)
                {
                    names.Add(assemblies[i].Name);
                }
            }

            return names;
        }

        /// <summary>The family assemblies that reference at least one kernel assembly.</summary>
        public IReadOnlyList<string> FamiliesOnKernel()
        {
            var names = new List<string>();
            for (int i = 0; i < assemblies.Count; i++)
            {
                AssemblyRecord record = assemblies[i];
                if (record.Classification != AssemblyClass.Family)
                {
                    continue;
                }

                bool onKernel = false;
                for (int r = 0; r < record.References.Count; r++)
                {
                    if (AssemblyReferenceAudit.IsKernelAssemblyName(record.References[r]))
                    {
                        onKernel = true;
                        break;
                    }
                }

                if (onKernel)
                {
                    names.Add(record.Name);
                }
            }

            return names;
        }

        internal void AddAssembly(AssemblyRecord record) => assemblies.Add(record);

        internal void AddViolation(AssemblyViolation violation) => violations.Add(violation);

        internal void AddKernelToken(GenreTokenFinding finding) => kernelTokens.Add(finding);

        internal void AddMissingPackage(string path) => missingPackages.Add(path);

        /// <summary>
        /// Orders every list canonically, so a document written from this report is deterministic regardless of the
        /// order the file system returned the declarations in (P-008).
        /// </summary>
        internal void Sort()
        {
            assemblies.Sort(delegate (AssemblyRecord left, AssemblyRecord right)
            {
                int order = string.CompareOrdinal(left.Name, right.Name);
                return order != 0 ? order : string.CompareOrdinal(left.Path, right.Path);
            });

            violations.Sort(delegate (AssemblyViolation left, AssemblyViolation right)
            {
                int order = string.CompareOrdinal(left.Assembly, right.Assembly);
                return order != 0 ? order : string.CompareOrdinal(left.Reference, right.Reference);
            });

            kernelTokens.Sort(delegate (GenreTokenFinding left, GenreTokenFinding right)
            {
                int order = string.CompareOrdinal(left.Path, right.Path);
                if (order != 0)
                {
                    return order;
                }

                order = left.Line.CompareTo(right.Line);
                return order != 0 ? order : string.CompareOrdinal(left.Token, right.Token);
            });

            missingPackages.Sort(StringComparer.Ordinal);
        }

        /// <summary>One line naming every count, for a report or a log line.</summary>
        public string Describe()
        {
            var text = new StringBuilder();
            text.Append("kernel=").Append(CountOf(AssemblyClass.Kernel).ToString(CultureInfo.InvariantCulture))
                .Append("; family=").Append(CountOf(AssemblyClass.Family).ToString(CultureInfo.InvariantCulture))
                .Append("; generated=").Append(CountOf(AssemblyClass.Generated).ToString(CultureInfo.InvariantCulture))
                .Append("; qualification=")
                .Append(CountOf(AssemblyClass.Qualification).ToString(CultureInfo.InvariantCulture))
                .Append("; asmdefs=").Append(AsmdefCount.ToString(CultureInfo.InvariantCulture))
                .Append("; projectFiles=").Append(ProjectFileCount.ToString(CultureInfo.InvariantCulture))
                .Append("; kernelSources=").Append(KernelSourceCount.ToString(CultureInfo.InvariantCulture))
                .Append("; referenceAssertions=")
                .Append(ReferenceAssertionCount.ToString(CultureInfo.InvariantCulture))
                .Append("; violations=").Append(violations.Count.ToString(CultureInfo.InvariantCulture))
                .Append("; kernelGenreTokens=").Append(kernelTokens.Count.ToString(CultureInfo.InvariantCulture))
                .Append("; missingPackages=").Append(missingPackages.Count.ToString(CultureInfo.InvariantCulture))
                .Append("; familiesOnKernel=")
                .Append(FamiliesOnKernel().Count.ToString(CultureInfo.InvariantCulture))
                .Append("/").Append(CountOf(AssemblyClass.Family).ToString(CultureInfo.InvariantCulture))
                .Append("; clean=").Append(ConformanceValue.Bool(Clean));
            return text.ToString();
        }
    }

    /// <summary>
    /// The build-time assembly-reference audit of 04 s2: every `.asmdef` and every `dotnet` project in the tree,
    /// classified by what it is, with every forbidden reference reported together with the clause that forbids it, and
    /// every kernel source scanned for genre tokens.
    /// </summary>
    public static class AssemblyReferenceAudit
    {
        /// <summary>Assembly-name prefixes a kernel assembly may never reference (01 s1, 04 s2, P-001).</summary>
        public static readonly IReadOnlyList<string> ForbiddenPrefixes = new[]
        {
            "GameCore.Gameplay.",
            "GameCore.Rules.",
            "GameCore.Validation",
            "GameCore.Generated",
        };

        /// <summary>Assembly names of the kernel (04 s2), including the fixture assemblies the gates run on.</summary>
        public static readonly IReadOnlyList<string> KernelNames = new[]
        {
            "GameCore.Contracts",
            "GameCore.Composition",
            "GameCore.Derivation",
            "GameCore.Derivation.Fixtures",
            "GameCore.Planning",
            "GameCore.Unity.Runtime",
            "GameCore.Unity.Fixtures",
            "GameCore.Unity.Adapters",
            "GameCore.Unity.Adapters.Fixtures",
            "GameCore.Content.Compiler",
            "GameCore.Content.Compiler.Editor",
            "GameCore.Execution",
            "GameCore.Adapters",
        };

        /// <summary>Package directories of the kernel, whose `.asmdef`s the audit reads by directory.</summary>
        public static readonly IReadOnlyList<string> KernelPackages = new[]
        {
            "Packages/com.gamecore.contracts",
            "Packages/com.gamecore.composition",
            "Packages/com.gamecore.derivation",
            "Packages/com.gamecore.planning",
            "Packages/com.gamecore.unity.runtime",
            "Packages/com.gamecore.unity.adapters",
            "Packages/com.gamecore.content.compiler",
        };

        /// <summary>Package directories of the gameplay and rules families: each must reference the kernel.</summary>
        public static readonly IReadOnlyList<string> FamilyPackages = new[]
        {
            "Packages/com.gamecore.gameplay.cards",
            "Packages/com.gamecore.gameplay.narrative",
            "Packages/com.gamecore.gameplay.traversal",
            "Packages/com.gamecore.gameplay.integration",
            "Packages/com.gamecore.rules.cards",
            "Packages/com.gamecore.rules.narrative",
            "Packages/com.gamecore.rules.traversal",
        };

        /// <summary>
        /// The genre tokens a kernel source must not name. They are the domain concepts P-001 keeps out of the kernel
        /// (actor, quest, card, traversal) plus the families' own type names; the scan is additive with the reference
        /// audit because a reference can be dropped while a `using` or a type name remains.
        /// </summary>
        public static readonly IReadOnlyList<string> ForbiddenKernelTokens = new[]
        {
            "GameCore.Gameplay.",
            "GameCore.Rules.",
            "GameCore.Validation.",
            "using GameCore.Gameplay",
            "using GameCore.Rules",
            "CardTableModule",
            "CardTableKeys",
            "CardSeatState",
            "CardHandRow",
            "CardCommittedRow",
            "NarrativeModule",
            "NarrativeKeys",
            "NarrativeTargetMarker",
            "QuestLedger",
            "TraversalModule",
            "TraversalKeys",
            "TraversalPose",
            "TraversalVelocity",
            "RunnerRecipe",
            "CheckpointVolume",
        };

        /// <summary>How many directory levels above the start directory the root search climbs.</summary>
        private const int RepositorySearchDepth = 8;

        /// <summary>Marker file that identifies the repository root, shared with the other fixtures.</summary>
        public const string RootMarker = "docs/game-core/traceability.json";

        /// <summary>Finds the repository root above <paramref name="startDirectory"/>, or null when there is none.</summary>
        public static string? TryFindRepositoryRoot(string startDirectory)
        {
            if (string.IsNullOrEmpty(startDirectory))
            {
                return null;
            }

            DirectoryInfo? directory = new DirectoryInfo(startDirectory);
            for (int depth = 0; depth < RepositorySearchDepth && directory != null; depth++)
            {
                if (File.Exists(Path.Combine(directory.FullName, "docs", "game-core", "traceability.json"))
                    && File.Exists(Path.Combine(directory.FullName, "dotnet", "GameCore.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return null;
        }

        /// <summary>True when an assembly name belongs to the declared kernel set.</summary>
        public static bool IsKernelAssemblyName(string assemblyName)
        {
            for (int i = 0; i < KernelNames.Count; i++)
            {
                if (string.Equals(KernelNames[i], assemblyName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when an assembly name belongs to a gameplay or rules family (P-001).</summary>
        public static bool IsFamilyAssemblyName(string assemblyName)
            => assemblyName.StartsWith("GameCore.Gameplay.", StringComparison.Ordinal)
            || assemblyName.StartsWith("GameCore.Rules.", StringComparison.Ordinal);

        /// <summary>True when a kernel assembly is forbidden from referencing this name.</summary>
        public static bool IsForbiddenReference(string assemblyName)
        {
            for (int i = 0; i < ForbiddenPrefixes.Count; i++)
            {
                if (assemblyName.StartsWith(ForbiddenPrefixes[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Runs the whole audit over a repository root. Every expectation is unconditional: a missing package is a
        /// finding rather than a skip, because an audit that skipped what it could not find would report clean.
        /// </summary>
        public static GenreAuditReport Audit(string repositoryRoot)
        {
            if (repositoryRoot == null)
            {
                throw new ArgumentNullException(nameof(repositoryRoot));
            }

            var report = new GenreAuditReport { RepositoryRoot = repositoryRoot };

            // 1. The kernel's own declarations: no family/generated/qualification reference, ever (04 s2).
            for (int i = 0; i < KernelPackages.Count; i++)
            {
                string directory = Path.Combine(repositoryRoot, KernelPackages[i]);
                if (!Directory.Exists(directory))
                {
                    report.AddMissingPackage(KernelPackages[i]);
                    continue;
                }

                string[] files = SortedFiles(directory, "*.asmdef");
                for (int f = 0; f < files.Length; f++)
                {
                    AssemblyRecord record = ReadAsmdef(repositoryRoot, files[f], AssemblyClass.Kernel);
                    report.AddAssembly(record);
                    report.AsmdefCount++;
                    AssertKernelReferences(report, record);
                }
            }

            // 2. The families: each must reach the kernel, and none may be reached by the kernel.
            for (int i = 0; i < FamilyPackages.Count; i++)
            {
                string directory = Path.Combine(repositoryRoot, FamilyPackages[i]);
                if (!Directory.Exists(directory))
                {
                    report.AddMissingPackage(FamilyPackages[i]);
                    continue;
                }

                string[] files = SortedFiles(directory, "*.asmdef");
                for (int f = 0; f < files.Length; f++)
                {
                    AssemblyRecord record = ReadAsmdef(
                        repositoryRoot, files[f], ClassifyFamilyPackage(repositoryRoot, files[f]));
                    report.AddAssembly(record);
                    report.AsmdefCount++;
                    AssertFamilyReachesKernel(report, record);
                }
            }

            // 3. Every other declaration in the tree, so a new assembly cannot enter unaudited.
            string[] allAsmdefs = SortedFiles(repositoryRoot, "*.asmdef");
            for (int f = 0; f < allAsmdefs.Length; f++)
            {
                string relative = Relative(repositoryRoot, allAsmdefs[f]);
                if (AlreadyRead(report, relative))
                {
                    continue;
                }

                AssemblyRecord record = ReadAsmdef(
                    repositoryRoot, allAsmdefs[f], ClassifyOutsideKernelFamily(repositoryRoot, allAsmdefs[f]));
                report.AddAssembly(record);
                report.AsmdefCount++;
            }

            // 4. The plain-dotnet half: an SDK-style project is the other declaration of the same boundary, and the
            // reference to a family assembly is a `ProjectReference` path rather than an assembly name.
            string[] projects = SortedFiles(Path.Combine(repositoryRoot, "dotnet", "src"), "*.csproj");
            for (int p = 0; p < projects.Length; p++)
            {
                AssemblyRecord record = ReadProject(repositoryRoot, projects[p], AssemblyClass.Kernel);
                report.AddAssembly(record);
                report.ProjectFileCount++;
                AssertProjectReferences(report, record);
            }

            // A dotnet project that compiles a family's sources is a family project: the audit reads it so the
            // "families reach the kernel" direction is checked on both halves of the tree.
            string[] testProjects = SortedFiles(Path.Combine(repositoryRoot, "dotnet", "tests"), "*.csproj");
            for (int p = 0; p < testProjects.Length; p++)
            {
                AssemblyRecord record = ReadProject(repositoryRoot, testProjects[p], AssemblyClass.Qualification);
                report.AddAssembly(record);
                report.ProjectFileCount++;
            }

            // 5. The kernel sources themselves: a genre token in a kernel file is a finding even with no reference.
            for (int i = 0; i < KernelPackages.Count; i++)
            {
                string directory = Path.Combine(repositoryRoot, KernelPackages[i]);
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                string[] sources = SortedFiles(directory, "*.cs");
                for (int s = 0; s < sources.Length; s++)
                {
                    report.KernelSourceCount++;
                    ScanKernelSource(report, repositoryRoot, sources[s]);
                }
            }

            Sort(report);
            return report;
        }

        // ------------------------------------------------------------------ reading

        private static AssemblyRecord ReadAsmdef(string repositoryRoot, string path, AssemblyClass classification)
        {
            string text = File.ReadAllText(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string? declared = ReadJsonString(text, "name");
            if (!string.IsNullOrEmpty(declared))
            {
                name = declared!;
            }

            bool noEngine = text.IndexOf("\"noEngineReferences\": true", StringComparison.Ordinal) >= 0;
            List<string> references = ReadJsonStringArray(text, "references");
            string packageRoot = PackageRootOf(repositoryRoot, path);
            return new AssemblyRecord(
                name, Relative(repositoryRoot, path), ReferenceKind.Asmdef, classification, packageRoot, noEngine,
                references);
        }

        private static AssemblyRecord ReadProject(string repositoryRoot, string path, AssemblyClass classification)
        {
            string text = File.ReadAllText(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string? declared = ReadElement(text, "AssemblyName");
            if (!string.IsNullOrEmpty(declared))
            {
                name = declared!;
            }

            List<string> references = ReadProjectReferences(text);
            string packageRoot = ProjectRootOf(repositoryRoot, path);
            return new AssemblyRecord(
                name,
                Relative(repositoryRoot, path),
                ReferenceKind.ProjectFile,
                classification,
                packageRoot,
                false,
                references);
        }

        /// <summary>
        /// Classifies a declaration that lives inside a gameplay or rules package. Every package in the family list
        /// is a family; a generated registration assembly inside one is `Generated`, because its allowed references
        /// are the composition root's rather than a family's (04 s2).
        /// </summary>
        private static AssemblyClass ClassifyFamilyPackage(string repositoryRoot, string path)
        {
            AssemblyRecord probe = ReadAsmdef(repositoryRoot, path, AssemblyClass.Family);
            if (probe.Name.StartsWith("GameCore.Generated", StringComparison.Ordinal))
            {
                return AssemblyClass.Generated;
            }

            return AssemblyClass.Family;
        }

        /// <summary>
        /// Classifies a declaration outside the kernel and the family packages: a generated registration assembly is
        /// `Generated`, and everything else (tests, fixtures, the qualification project, tooling) is
        /// `Qualification`, which may reference both sides.
        /// </summary>
        private static AssemblyClass ClassifyOutsideKernelFamily(string repositoryRoot, string path)
        {
            string relative = Relative(repositoryRoot, path);
            for (int i = 0; i < FamilyPackages.Count; i++)
            {
                if (relative.StartsWith(FamilyPackages[i] + "/", StringComparison.Ordinal))
                {
                    return AssemblyClass.Family;
                }
            }

            string name = Path.GetFileNameWithoutExtension(path);
            return name.StartsWith("GameCore.Generated", StringComparison.Ordinal)
                ? AssemblyClass.Generated
                : AssemblyClass.Qualification;
        }

        private static bool AlreadyRead(GenreAuditReport report, string relativePath)
        {
            for (int i = 0; i < report.Assemblies.Count; i++)
            {
                if (string.Equals(report.Assemblies[i].Path, relativePath, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ assertions

        private static void AssertKernelReferences(GenreAuditReport report, AssemblyRecord record)
        {
            for (int r = 0; r < record.References.Count; r++)
            {
                report.ReferenceAssertionCount++;
                string reference = record.References[r];
                if (IsForbiddenReference(reference))
                {
                    report.AddViolation(new AssemblyViolation(
                        record.Name,
                        record.Path,
                        reference,
                        "P-001 / 04 s2: a kernel assembly never references a gameplay, rules, qualification or"
                        + " generated assembly"));
                    continue;
                }

                if (record.NoEngineReferences && reference.StartsWith("Unity", StringComparison.Ordinal))
                {
                    report.AddViolation(new AssemblyViolation(
                        record.Name,
                        record.Path,
                        reference,
                        "01 s1: a declaration that sets noEngineReferences may not reference an engine assembly"));
                }
            }
        }

        private static void AssertFamilyReachesKernel(GenreAuditReport report, AssemblyRecord record)
        {
            bool onKernel = false;
            for (int r = 0; r < record.References.Count; r++)
            {
                report.ReferenceAssertionCount++;
                if (IsKernelAssemblyName(record.References[r]))
                {
                    onKernel = true;
                }
            }

            if (!onKernel)
            {
                report.AddViolation(new AssemblyViolation(
                    record.Name,
                    record.Path,
                    "<no kernel reference>",
                    "P-001 / 04 s2: a gameplay or rules assembly must reference the kernel it runs on"));
            }
        }

        private static void AssertProjectReferences(GenreAuditReport report, AssemblyRecord record)
        {
            for (int r = 0; r < record.References.Count; r++)
            {
                report.ReferenceAssertionCount++;
                string reference = record.References[r];
                // A `dotnet` project names its reference as a path; the file name is the referenced assembly.
                string referenced = Path.GetFileNameWithoutExtension(reference.Replace('\\', '/').TrimEnd('/'));
                if (IsForbiddenReference(referenced))
                {
                    report.AddViolation(new AssemblyViolation(
                        record.Name,
                        record.Path,
                        reference,
                        "P-001 / 04 s2: a kernel dotnet project never references a gameplay, rules, qualification or"
                        + " generated project"));
                }
            }
        }

        private static void ScanKernelSource(GenreAuditReport report, string repositoryRoot, string path)
        {
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                for (int t = 0; t < ForbiddenKernelTokens.Count; t++)
                {
                    if (line.IndexOf(ForbiddenKernelTokens[t], StringComparison.Ordinal) >= 0)
                    {
                        report.AddKernelToken(new GenreTokenFinding(
                            Relative(repositoryRoot, path), i + 1, ForbiddenKernelTokens[t]));
                    }
                }
            }
        }

        // ------------------------------------------------------------------ helpers

        private static void Sort(GenreAuditReport report) => report.Sort();

        private static string[] SortedFiles(string directory, string pattern)
        {
            if (!Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }

            string[] files = Directory.GetFiles(directory, pattern, SearchOption.AllDirectories);
            var kept = new List<string>(files.Length);
            for (int i = 0; i < files.Length; i++)
            {
                string normalised = files[i].Replace('\\', '/');
                if (normalised.IndexOf("/bin/", StringComparison.Ordinal) >= 0
                    || normalised.IndexOf("/obj/", StringComparison.Ordinal) >= 0)
                {
                    continue;
                }

                kept.Add(files[i]);
            }

            string[] sorted = kept.ToArray();
            Array.Sort(sorted, StringComparer.Ordinal);
            return sorted;
        }

        private static string PackageRootOf(string repositoryRoot, string path)
        {
            string relative = Relative(repositoryRoot, path);
            int index = relative.IndexOf('/', "Packages/".Length);
            if (relative.StartsWith("Packages/", StringComparison.Ordinal) && index > 0)
            {
                return relative.Substring(0, index);
            }

            // A fixture package at the repository root, e.g. `tests/GameCore.Replay/Runtime/...`.
            string[] parts = relative.Split('/');
            return parts.Length >= 2 ? parts[0] + "/" + parts[1] : relative;
        }

        private static string ProjectRootOf(string repositoryRoot, string path)
        {
            string relative = Relative(repositoryRoot, path);
            int slash = relative.LastIndexOf('/');
            return slash > 0 ? relative.Substring(0, slash) : relative;
        }

        /// <summary>A repository-relative, slash-separated path (Path.GetRelativePath is not in netstandard2.1).</summary>
        private static string Relative(string repositoryRoot, string path)
        {
            string normalisedRoot = repositoryRoot.Replace('\\', '/').TrimEnd('/') + "/";
            string normalisedPath = path.Replace('\\', '/');
            return normalisedPath.StartsWith(normalisedRoot, StringComparison.Ordinal)
                ? normalisedPath.Substring(normalisedRoot.Length)
                : normalisedPath;
        }

        /// <summary>Reads one JSON string value; the declaration format is flat, so a scanner is enough (04 s8).</summary>
        private static string? ReadJsonString(string json, string key)
        {
            int keyIndex = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (keyIndex < 0)
            {
                return null;
            }

            int colon = json.IndexOf(':', keyIndex);
            if (colon < 0)
            {
                return null;
            }

            int open = json.IndexOf('"', colon);
            if (open < 0)
            {
                return null;
            }

            int close = json.IndexOf('"', open + 1);
            return close < 0 ? null : json.Substring(open + 1, close - open - 1);
        }

        /// <summary>Reads one JSON string array; used for an `.asmdef`'s `references`.</summary>
        private static List<string> ReadJsonStringArray(string json, string key)
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

        /// <summary>Reads one XML element's text; used for a `.csproj`'s `AssemblyName`.</summary>
        private static string? ReadElement(string xml, string name)
        {
            int open = xml.IndexOf("<" + name + ">", StringComparison.Ordinal);
            if (open < 0)
            {
                return null;
            }

            int start = open + name.Length + 2;
            int close = xml.IndexOf("</" + name + ">", start, StringComparison.Ordinal);
            return close < 0 ? null : xml.Substring(start, close - start).Trim();
        }

        /// <summary>Reads every `ProjectReference` include path of an SDK-style project file.</summary>
        private static List<string> ReadProjectReferences(string xml)
        {
            var values = new List<string>();
            int index = 0;
            while (index < xml.Length)
            {
                int open = xml.IndexOf("<ProjectReference", index, StringComparison.Ordinal);
                if (open < 0)
                {
                    break;
                }

                int close = xml.IndexOf("/>", open, StringComparison.Ordinal);
                int end = close < 0 ? xml.IndexOf('>', open) : close;
                if (end < 0)
                {
                    break;
                }

                string element = xml.Substring(open, end - open);
                string? include = Attribute(element, "Include");
                if (!string.IsNullOrEmpty(include))
                {
                    values.Add(include!);
                }

                index = end + 1;
            }

            return values;
        }

        private static string? Attribute(string element, string name)
        {
            int index = element.IndexOf(name + "=\"", StringComparison.Ordinal);
            if (index < 0)
            {
                return null;
            }

            int start = index + name.Length + 2;
            int close = element.IndexOf('"', start);
            return close < 0 ? null : element.Substring(start, close - start);
        }
    }
}
