// GameCore.Studio.Edit - the tool registry (docs/studio/03-authoring-contracts.md s4, s5).
// Holds the built-in tools and every [AuthorOperation] method of the loaded assemblies (TypeCache). The catalog is
// P0.3's ToolCatalogBuilder entries for the methods, the built-in entries, and the Unity-specific object-type pass
// (AuthorableTypeRegistry: non-public [SerializeField] members included). Exported as tool-catalog.json.
#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace GameCore.Studio.Edit
{
    /// <summary>Built-in and plugin tools, the catalog, and direct invocation.</summary>
    public sealed class ToolRegistry
    {
        private readonly StudioRuntime _runtime;
        private readonly SortedDictionary<string, IStudioTool> _builtIns = new SortedDictionary<string, IStudioTool>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, IStudioTool> _plugins = new SortedDictionary<string, IStudioTool>(StringComparer.Ordinal);
        private readonly List<Diagnostic> _problems = new List<Diagnostic>();
        private bool _discovered;
        private ToolCatalog? _catalog;

        public ToolRegistry(StudioRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            foreach (IStudioTool tool in BuiltInTools.Create())
            {
                _builtIns[tool.Entry.Id] = tool;
            }
        }

        /// <summary>Where [AuthorOperation] methods come from (TypeCache by default).</summary>
        public Func<IEnumerable<MethodInfo>> MethodSource { get; set; } = AuthoringTypeCache.ToolMethods;

        /// <summary>Declaration problems (duplicate ids, bad signatures), as CandidateInvalid diagnostics.</summary>
        public IReadOnlyList<Diagnostic> Problems
        {
            get
            {
                EnsureDiscovered();
                return _problems;
            }
        }

        /// <summary>The project catalog (built on first use).</summary>
        public ToolCatalog Catalog => _catalog ??= BuildCatalog();

        /// <summary>Every tool, built-ins first.</summary>
        public IReadOnlyList<IStudioTool> All
        {
            get
            {
                EnsureDiscovered();
                List<IStudioTool> tools = new List<IStudioTool>(_builtIns.Values);
                tools.AddRange(_plugins.Values);
                return tools;
            }
        }

        /// <summary>Registers (or, with <paramref name="replace"/>, replaces) a tool; refuses to shadow a built-in.</summary>
        public void Register(IStudioTool tool, bool replace = false)
        {
            if (tool == null)
            {
                throw new ArgumentNullException(nameof(tool));
            }

            string id = tool.Entry.Id;
            if (_builtIns.ContainsKey(id))
            {
                throw new InvalidOperationException("Tool '" + id + "' is a built-in and cannot be registered by a plugin.");
            }

            if (_plugins.ContainsKey(id) && !replace)
            {
                throw new InvalidOperationException("Tool '" + id + "' is already registered.");
            }

            _plugins[id] = tool;
            _catalog = null;
        }

        /// <summary>The tool with <paramref name="toolId"/>, or null.</summary>
        public IStudioTool? Find(string toolId)
        {
            if (_builtIns.TryGetValue(toolId, out IStudioTool? builtIn))
            {
                return builtIn;
            }

            EnsureDiscovered();
            return _plugins.TryGetValue(toolId, out IStudioTool? plugin) ? plugin : null;
        }

        /// <summary>Forgets discovered plugin tools and the catalog (rediscovered on next use).</summary>
        public void Invalidate()
        {
            List<string> reflected = new List<string>();
            foreach (KeyValuePair<string, IStudioTool> pair in _plugins)
            {
                if (pair.Value is ReflectedTool)
                {
                    reflected.Add(pair.Key);
                }
            }

            foreach (string id in reflected)
            {
                _plugins.Remove(id);
            }

            _problems.Clear();
            _discovered = false;
            _catalog = null;
        }

        /// <summary>
        /// The catalog: object types from the Unity pass, every non-internal tool entry (built-ins and plugins), sorted,
        /// with its content revision minted (requests carry it as toolCatalogRevision; 03 s9).
        /// </summary>
        public ToolCatalog BuildCatalog()
        {
            EnsureDiscovered();
            List<ToolEntry> tools = new List<ToolEntry>();
            foreach (IStudioTool tool in All)
            {
                if (!tool.Internal)
                {
                    tools.Add(tool.Entry);
                }
            }

            tools.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
            return new ToolCatalog(_runtime.Types.ObjectTypeEntries(), tools).WithRevision();
        }

        /// <summary>Writes tool-catalog.json (default: Library/GameCoreStudio/tool-catalog.json) and returns the path.</summary>
        public string Export(string? path = null)
        {
            string target = path ?? _runtime.Paths.CatalogPath;
            _catalog = BuildCatalog();
            StudioPaths.WriteAllTextAtomic(target, StudioJson.Serialize(_catalog));
            return target;
        }

        /// <summary>Applies one operation with a prepared context (the engine's per-op step; read-only tools directly).</summary>
        public OperationResult Invoke(Operation operation, EditContext context)
        {
            IStudioTool? tool = Find(operation.Tool);
            if (tool == null)
            {
                return OperationResult.Refused(DiagnosticCodes.UnknownTool, "Tool '" + operation.Tool + "' is not registered.");
            }

            return tool.Apply(context);
        }

        /// <summary>
        /// Invokes a read-only tool (inspect/query) or a direct tool (preview/history/project) outside any change set and
        /// returns its output. Other mutating tools are refused here: they go through the edit engine so they are staged,
        /// applied and journaled.
        /// </summary>
        public OperationResult Invoke(string toolId, AuthoringRef? target, JObject? args)
        {
            IStudioTool? tool = Find(toolId);
            if (tool == null)
            {
                return OperationResult.Refused(DiagnosticCodes.UnknownTool, "Tool '" + toolId + "' is not registered.");
            }

            if (!tool.ReadOnly && !(tool is IDirectTool))
            {
                return OperationResult.Refused(DiagnosticCodes.Refused, "Tool '" + toolId + "' changes the project; submit it in a change set (ChangeSetEngine.Stage/Apply).");
            }

            Operation operation = new Operation("op1", toolId, target, args, null, Preconditions.None);
            ChangeSet changeSet = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent(toolId, IntentOrigin.Manual), new[] { operation });
            UnityEngine.Object? resolved = target == null || target.Kind == AuthoringKind.Location ? null : _runtime.Resolver.Find(target);
            if (tool.Entry.TargetRequired && target != null && target.Kind != AuthoringKind.Location && resolved == null)
            {
                return OperationResult.Refused(DiagnosticCodes.StaleTarget, "The target does not resolve.");
            }

            EditContext context = new EditContext(_runtime, changeSet, operation, resolved, false, null);
            ToolStageResult staged = tool.Stage(context);
            if (!staged.Ok)
            {
                return OperationResult.Refused(staged.Diagnostics[0].Code, staged.Diagnostics[0].Message);
            }

            return tool.Apply(context);
        }

        private void EnsureDiscovered()
        {
            if (_discovered)
            {
                return;
            }

            _discovered = true;
            List<MethodInfo> methods = new List<MethodInfo>(MethodSource());
            methods.Sort((left, right) =>
            {
                int compare = string.CompareOrdinal(left.DeclaringType?.FullName, right.DeclaringType?.FullName);
                return compare != 0 ? compare : left.MetadataToken.CompareTo(right.MetadataToken);
            });
            foreach (MethodInfo method in methods)
            {
                ToolEntry entry;
                try
                {
                    entry = AuthoringMetadata.BuildToolEntry(method);
                }
                catch (Exception error) when (error is InvalidOperationException || error is ArgumentException)
                {
                    _problems.Add(StudioDiagnostics.General(DiagnosticCodes.CandidateInvalid, "[AuthorOperation] " + method.DeclaringType?.FullName + "." + method.Name + ": " + error.Message));
                    continue;
                }

                if (_builtIns.ContainsKey(entry.Id) || _plugins.ContainsKey(entry.Id))
                {
                    _problems.Add(StudioDiagnostics.General(
                        DiagnosticCodes.CandidateInvalid,
                        "Tool '" + entry.Id + "' declared by " + method.DeclaringType?.FullName + "." + method.Name + " is already registered; the declaration is ignored."));
                    continue;
                }

                _plugins[entry.Id] = new ReflectedTool(method, entry);
            }

            _catalog = null;
        }
    }
}
