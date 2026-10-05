// GameCore.Gameplay.Compile.Editor - the bake extension point (P1.4; additive hook for plugin packages).
//
// Entry.Bake / Entry.Verify bake the world (regions, portals, entity definitions) and the catalog the world boots with.
// Plugin packages beyond entities and world (dialogue, quest, inventory, logic, and later ones) need two things from
// the same bake: their catalog registrations (plugin factory, command system, applier, layouts, schemas) in the
// generated catalog, and their own definition outputs written and verified together with the world's. An extension:
//
//   Contribute  (Bake and Verify) validates the package's content for this world and adds catalog schemas/entries;
//               any diagnostic refuses the bake exactly like a world diagnostic
//   Write       (Bake only, after the world outputs) writes the package's outputs; changed paths are reported
//   Verify      (Verify only) compares the package's outputs on disk with the recomputed ones, writing nothing
//
// Extensions are found with TypeCache (every non-abstract IGameplayBakeExtension with a parameterless constructor),
// instantiated once per bake, and run in ordinal ExtensionId order, so the description stays deterministic. With no
// extension contributing anything the catalog description is byte-identical to the P1.1 output.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.World;
using UnityEditor;

namespace GameCore.Gameplay.Compile
{
    /// <summary>A plugin package's part of the gameplay bake.</summary>
    public interface IGameplayBakeExtension
    {
        /// <summary>Stable id (ordinal order of the run), e.g. <c>gameplay.logic</c>.</summary>
        string ExtensionId { get; }

        /// <summary>Validates and contributes catalog registrations (Bake and Verify).</summary>
        void Contribute(GameplayBakeContext context);

        /// <summary>Writes the package's outputs (Bake only); adds every changed path to <paramref name="changedFiles"/>.</summary>
        void Write(GameplayBakeContext context, ICollection<string> changedFiles);

        /// <summary>Compares the package's outputs on disk with the recomputed ones (Verify only).</summary>
        void Verify(GameplayBakeContext context, ICollection<GameplayDiagnostic> mismatches);
    }

    /// <summary>What the extensions of one bake share.</summary>
    public sealed class GameplayBakeContext
    {
        private readonly List<GameplayCatalogNames.SchemaName> schemas = new List<GameplayCatalogNames.SchemaName>();
        private readonly List<GameplayCatalogNames.EntryName> entries = new List<GameplayCatalogNames.EntryName>();
        private readonly HashSet<string> schemaNames = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> entryNames = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<GameplayDiagnostic> diagnostics = new List<GameplayDiagnostic>();
        private readonly Dictionary<string, object> state = new Dictionary<string, object>(StringComparer.Ordinal);
        private readonly List<IGameplayBakeExtension> extensions = new List<IGameplayBakeExtension>();

        public GameplayBakeContext(WorldDefinition world, BakePaths paths, BakedWorld baked)
        {
            World = world;
            Paths = paths;
            Baked = baked;
        }

        public WorldDefinition World { get; }

        public BakePaths Paths { get; }

        public BakedWorld Baked { get; }

        public IReadOnlyList<GameplayCatalogNames.SchemaName> Schemas => schemas;

        public IReadOnlyList<GameplayCatalogNames.EntryName> Entries => entries;

        public IReadOnlyList<GameplayDiagnostic> Diagnostics => diagnostics;

        public IReadOnlyList<IGameplayBakeExtension> Extensions => extensions;

        /// <summary>Adds a catalog schema once (by schema name); the static gameplay schemas are always present.</summary>
        public void AddSchema(GameplayCatalogNames.SchemaName schema)
        {
            if (schema != null && schemaNames.Add(schema.Schema))
            {
                schemas.Add(schema);
            }
        }

        /// <summary>Adds a catalog entry once (by registration name).</summary>
        public void AddEntry(GameplayCatalogNames.EntryName entry)
        {
            if (entry != null && entryNames.Add(entry.Name))
            {
                entries.Add(entry);
            }
        }

        public void AddDiagnostic(GameplayDiagnostic diagnostic)
        {
            if (diagnostic != null)
            {
                diagnostics.Add(diagnostic);
            }
        }

        /// <summary>Per-extension scratch state between Contribute and Write/Verify.</summary>
        public void Put(string key, object value) => state[key] = value;

        public bool TryGet<T>(string key, out T? value)
            where T : class
        {
            if (state.TryGetValue(key, out object? found) && found is T typed)
            {
                value = typed;
                return true;
            }

            value = null;
            return false;
        }

        internal void Use(IGameplayBakeExtension extension) => extensions.Add(extension);
    }

    /// <summary>Discovery and execution of the bake extensions.</summary>
    public static class GameplayBakeExtensions
    {
        /// <summary>Instantiates every extension, sorted by ExtensionId.</summary>
        public static List<IGameplayBakeExtension> Discover()
        {
            var found = new List<IGameplayBakeExtension>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IGameplayBakeExtension>())
            {
                if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) == null)
                {
                    continue;
                }

                if (Activator.CreateInstance(type) is IGameplayBakeExtension extension)
                {
                    found.Add(extension);
                }
            }

            found.Sort((l, r) => string.CompareOrdinal(l.ExtensionId, r.ExtensionId));
            return found;
        }

        /// <summary>Runs Contribute of every extension; an extension that throws becomes a diagnostic.</summary>
        public static GameplayBakeContext Contribute(WorldDefinition world, BakePaths paths, BakedWorld baked)
        {
            var context = new GameplayBakeContext(world, paths, baked);
            List<IGameplayBakeExtension> extensions = Discover();
            for (int i = 0; i < extensions.Count; i++)
            {
                context.Use(extensions[i]);
                try
                {
                    extensions[i].Contribute(context);
                }
                catch (Exception exception)
                {
                    context.AddDiagnostic(new GameplayDiagnostic(GameplayDiagnosticCodes.BakeVerifyMismatch, extensions[i].ExtensionId,
                        "bake extension failed: " + exception.Message));
                }
            }

            return context;
        }

        public static void Write(GameplayBakeContext context, ICollection<string> changedFiles)
        {
            for (int i = 0; i < context.Extensions.Count; i++)
            {
                context.Extensions[i].Write(context, changedFiles);
            }
        }

        public static void Verify(GameplayBakeContext context, ICollection<GameplayDiagnostic> mismatches)
        {
            for (int i = 0; i < context.Extensions.Count; i++)
            {
                context.Extensions[i].Verify(context, mismatches);
            }
        }
    }
}
