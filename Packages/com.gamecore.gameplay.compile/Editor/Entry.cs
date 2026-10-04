// GameCore.Gameplay.Compile.Editor - Entry.Bake and Entry.Verify (P1.1).
//
// Bake reads one WorldDefinition and its region scenes (WorldReader), validates the model (BakeValidator), and only
// when nothing is wrong writes, in this order:
//
//   1. <dir>/Catalog/<World>Catalog.catalog.json   the gamecore.catalog-description/1 document
//   2. <generated dir>/<World>Catalog.g.cs (+ coverage)  through the content compiler (CatalogGenerator)
//   3. <dir>/Catalog/<World>.bake.json            the canonical bake report, carrying the catalog fingerprint
//   4. <dir>/<World>.manifest.asset               the RegionManifest the runtime boots from
//   5. the content stamp of every entity definition (only when it changed)
//
// Everything is sorted by authoring id, every id is GUID-derived and every text file is LF/UTF-8 without BOM, so two
// bakes of the same content are byte-identical. Verify recomputes everything in memory and compares it with the files
// on disk byte for byte (and the manifest by its recorded bake-report hash) without writing anything.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using GameCore.Content.Compiler;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.World;
using UnityEditor;
using UnityEngine;

namespace GameCore.Gameplay.Compile
{
    /// <summary>Where one world's bake outputs live.</summary>
    public sealed class BakePaths
    {
        public BakePaths(string worldAssetPath, string generatedDirectory, CatalogNaming naming)
        {
            WorldAssetPath = worldAssetPath;
            string directory = Path.GetDirectoryName(worldAssetPath)!.Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(worldAssetPath);
            Naming = naming;
            DescriptionPath = directory + "/Catalog/" + naming.ClassName + ".catalog.json";
            ReportPath = directory + "/Catalog/" + name + ".bake.json";
            ManifestPath = directory + "/" + name + ".manifest.asset";
            GeneratedPath = generatedDirectory.TrimEnd('/') + "/" + naming.FileName;
        }

        public string WorldAssetPath { get; }

        public CatalogNaming Naming { get; }

        public string DescriptionPath { get; }

        public string ReportPath { get; }

        public string ManifestPath { get; }

        public string GeneratedPath { get; }

        /// <summary>
        /// The conventional paths of a world asset: catalog class <c>&lt;World&gt;Catalog</c> in namespace
        /// <c>&lt;World&gt;.Generated</c>, generated into <c>&lt;dir&gt;/Generated</c>.
        /// </summary>
        public static BakePaths ConventionFor(string worldAssetPath)
        {
            string directory = Path.GetDirectoryName(worldAssetPath)!.Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(worldAssetPath);
            return new BakePaths(worldAssetPath, directory + "/Generated", new CatalogNaming(name + ".Generated", name + "Catalog"));
        }
    }

    /// <summary>The outcome of a bake or a verify.</summary>
    public sealed class BakeResult
    {
        public BakeResult(bool succeeded, IReadOnlyList<GameplayDiagnostic> diagnostics, string summary)
        {
            Succeeded = succeeded;
            Diagnostics = diagnostics;
            Summary = summary;
        }

        public bool Succeeded { get; }

        public IReadOnlyList<GameplayDiagnostic> Diagnostics { get; }

        public string Summary { get; }

        public string CatalogFingerprint { get; internal set; } = string.Empty;

        public int Regions { get; internal set; }

        public int Portals { get; internal set; }

        public int Definitions { get; internal set; }

        public int Entities { get; internal set; }

        /// <summary>Files whose bytes changed (Bake) or differ from the recomputed bytes (Verify).</summary>
        public List<string> ChangedFiles { get; } = new List<string>();

        public long ElapsedMilliseconds { get; internal set; }

        public override string ToString()
        {
            var builder = new StringBuilder(Summary);
            for (int i = 0; i < Diagnostics.Count; i++)
            {
                builder.Append('\n').Append(Diagnostics[i]);
            }

            return builder.ToString();
        }
    }

    /// <summary>The gameplay compile entry points.</summary>
    public static class Entry
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>Bakes the project's one WorldDefinition with the conventional paths.</summary>
        [MenuItem("GameCore/Gameplay/Bake World")]
        public static void BakeMenu()
        {
            BakeResult result = Bake();
            if (result.Succeeded)
            {
                UnityEngine.Debug.Log("[GameCore] " + result);
            }
            else
            {
                UnityEngine.Debug.LogError("[GameCore] " + result);
            }
        }

        [MenuItem("GameCore/Gameplay/Verify World Bake")]
        public static void VerifyMenu()
        {
            BakeResult result = Verify();
            if (result.Succeeded)
            {
                UnityEngine.Debug.Log("[GameCore] " + result);
            }
            else
            {
                UnityEngine.Debug.LogError("[GameCore] " + result);
            }
        }

        public static BakeResult Bake()
        {
            WorldDefinition? world = FindWorld(out string path);
            if (world == null)
            {
                return Failed(GameplayDiagnosticCodes.WorldMissingStartRegion, "no single WorldDefinition asset in the project");
            }

            return Bake(world, BakePaths.ConventionFor(path));
        }

        public static BakeResult Verify()
        {
            WorldDefinition? world = FindWorld(out string path);
            if (world == null)
            {
                return Failed(GameplayDiagnosticCodes.WorldMissingStartRegion, "no single WorldDefinition asset in the project");
            }

            return Verify(world, BakePaths.ConventionFor(path));
        }

        /// <summary>Bakes <paramref name="world"/> into <paramref name="paths"/>.</summary>
        public static BakeResult Bake(WorldDefinition world, BakePaths paths) => Bake(world, paths, true);

        /// <summary>
        /// Bakes <paramref name="world"/>; with <paramref name="refreshAssetDatabase"/> false the generated C# is written
        /// but not imported (no script compilation, no domain reload: batch and test bakes), and Unity imports it on
        /// the next project load.
        /// </summary>
        public static BakeResult Bake(WorldDefinition world, BakePaths paths, bool refreshAssetDatabase)
        {
            Stopwatch clock = Stopwatch.StartNew();
            BakeStaleMarker.BeginBake();
            try
            {
                return BakeCore(world, paths, clock, refreshAssetDatabase);
            }
            finally
            {
                BakeStaleMarker.EndBake(clock.ElapsedMilliseconds);
            }
        }

        private static BakeResult BakeCore(WorldDefinition world, BakePaths paths, Stopwatch clock, bool refreshAssetDatabase)
        {
            Outputs? outputs = Compute(world, paths, out BakeResult? failure);
            if (outputs == null)
            {
                return failure!;
            }

            var result = Success(outputs, "baked " + paths.WorldAssetPath);
            WriteIfChanged(paths.DescriptionPath, outputs.Description, result);
            CatalogGenerationReport generated = CatalogGenerator.GenerateFromText(outputs.Description, paths.GeneratedPath);
            if (!generated.Succeeded)
            {
                return Failed(GameplayDiagnosticCodes.BakeVerifyMismatch, "the content compiler refused the description: " + generated.Summary);
            }

            if (generated.WroteFile)
            {
                result.ChangedFiles.Add(paths.GeneratedPath);
            }

            WriteIfChanged(paths.ReportPath, outputs.Report, result);
            if (WriteManifest(paths.ManifestPath, outputs))
            {
                result.ChangedFiles.Add(paths.ManifestPath);
            }

            foreach (KeyValuePair<string, EntityDefinition> pair in outputs.Read.Definitions)
            {
                BakedDefinition? baked = outputs.Read.World.FindDefinition(pair.Key);
                if (baked != null && !string.Equals(pair.Value.ContentStamp, baked.ContentHash, StringComparison.Ordinal))
                {
                    pair.Value.SetContentStamp(baked.ContentHash);
                    EditorUtility.SetDirty(pair.Value);
                    result.ChangedFiles.Add(AssetDatabase.GetAssetPath(pair.Value));
                }
            }

            AssetDatabase.SaveAssets();
            if (refreshAssetDatabase)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            BakeStaleMarker.Clear();
            result.ElapsedMilliseconds = clock.ElapsedMilliseconds;
            return result;
        }

        /// <summary>Recomputes the bake in memory and compares it with the files on disk; writes nothing.</summary>
        public static BakeResult Verify(WorldDefinition world, BakePaths paths)
        {
            Stopwatch clock = Stopwatch.StartNew();
            Outputs? outputs = Compute(world, paths, out BakeResult? failure);
            if (outputs == null)
            {
                return failure!;
            }

            var mismatches = new List<GameplayDiagnostic>();
            Compare(paths.DescriptionPath, outputs.Description, mismatches);
            Compare(paths.GeneratedPath, outputs.GeneratedCode, mismatches);
            Compare(paths.ReportPath, outputs.Report, mismatches);
            RegionManifest? manifest = AssetDatabase.LoadAssetAtPath<RegionManifest>(paths.ManifestPath);
            if (manifest == null || !string.Equals(manifest.BakeReportHash, DefinitionHashing.Sha256Hex(outputs.Report), StringComparison.Ordinal))
            {
                mismatches.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.BakeVerifyMismatch, paths.ManifestPath,
                    "the region manifest does not record this bake"));
            }

            var result = new BakeResult(mismatches.Count == 0, mismatches,
                (mismatches.Count == 0 ? "verified " : "stale bake of ") + paths.WorldAssetPath);
            Fill(result, outputs);
            for (int i = 0; i < mismatches.Count; i++)
            {
                result.ChangedFiles.Add(mismatches[i].SubjectId);
            }

            result.ElapsedMilliseconds = clock.ElapsedMilliseconds;
            return result;
        }

        /// <summary>The bake outputs of a world, computed in memory (the catalog text through the content compiler).</summary>
        public static string ComputeReport(WorldDefinition world, BakePaths paths, out BakeResult? failure)
        {
            Outputs? outputs = Compute(world, paths, out failure);
            return outputs != null ? outputs.Report : string.Empty;
        }

        private sealed class Outputs
        {
            public Outputs(WorldReadResult read, string description, string generatedCode, string fingerprint, string report, string catalogTypeName)
            {
                CatalogTypeName = catalogTypeName;
                Read = read;
                Description = description;
                GeneratedCode = generatedCode;
                Fingerprint = fingerprint;
                Report = report;
            }

            public WorldReadResult Read { get; }

            public string Description { get; }

            public string GeneratedCode { get; }

            public string Fingerprint { get; }

            public string Report { get; }

            public string CatalogTypeName { get; }
        }

        private static Outputs? Compute(WorldDefinition world, BakePaths paths, out BakeResult? failure)
        {
            failure = null;
            if (world == null)
            {
                failure = Failed(GameplayDiagnosticCodes.WorldMissingStartRegion, "no world definition");
                return null;
            }

            WorldReadResult read = WorldReader.Read(world);
            var diagnostics = new List<GameplayDiagnostic>(read.Diagnostics);
            diagnostics.AddRange(BakeValidator.Validate(read.World));
            if (diagnostics.Count > 0)
            {
                failure = new BakeResult(false, diagnostics, "bake of " + paths.WorldAssetPath + " refused with " + diagnostics.Count + " problem(s)");
                return null;
            }

            string description = CatalogDescriptionWriter.Write(read.World, paths.Naming);
            CatalogCompilationResult compiled = CatalogDescriptionReader.Read(description);
            if (!compiled.Succeeded)
            {
                failure = Failed(GameplayDiagnosticCodes.BakeVerifyMismatch, "the catalog description was rejected: " + compiled.Describe());
                return null;
            }

            string code = compiled.GeneratedCode!;
            string fingerprint = CatalogGenerator.ExtractStringConstant(code, "CatalogFingerprint") ?? string.Empty;
            string report = BakeReportWriter.Write(read.World, fingerprint);
            return new Outputs(read, description, code, fingerprint, report, paths.Naming.Namespace + "." + paths.Naming.ClassName);
        }

        private static bool WriteManifest(string path, Outputs outputs)
        {
            BakedWorld world = outputs.Read.World;
            var regions = new List<ManifestRegion>();
            for (int i = 0; i < world.Regions.Count; i++)
            {
                BakedRegion region = world.Regions[i];
                regions.Add(new ManifestRegion
                {
                    authoringId = region.AuthoringId,
                    name = region.Name,
                    scenePath = region.ScenePath,
                    key = AuthoringIds.StableKey(region.AuthoringId),
                    spawnX = region.SpawnX,
                    spawnY = region.SpawnY,
                    spawnZ = region.SpawnZ,
                    spawnYaw = region.SpawnYaw,
                });
            }

            var portals = new List<ManifestPortal>();
            for (int i = 0; i < world.Portals.Count; i++)
            {
                BakedPortal portal = world.Portals[i];
                portals.Add(new ManifestPortal
                {
                    authoringId = portal.AuthoringId,
                    name = portal.Name,
                    key = AuthoringIds.StableKey(portal.AuthoringId),
                    regionA = portal.RegionA,
                    regionB = portal.RegionB,
                    arrivalAX = portal.ArrivalA.X,
                    arrivalAY = portal.ArrivalA.Y,
                    arrivalAZ = portal.ArrivalA.Z,
                    arrivalAYaw = portal.ArrivalA.Yaw,
                    arrivalBX = portal.ArrivalB.X,
                    arrivalBY = portal.ArrivalB.Y,
                    arrivalBZ = portal.ArrivalB.Z,
                    arrivalBYaw = portal.ArrivalB.Yaw,
                });
            }

            var definitions = new List<ManifestDefinition>();
            for (int i = 0; i < world.Definitions.Count; i++)
            {
                BakedDefinition definition = world.Definitions[i];
                outputs.Read.Definitions.TryGetValue(definition.AuthoringId, out EntityDefinition? asset);
                definitions.Add(new ManifestDefinition
                {
                    authoringId = definition.AuthoringId,
                    name = definition.Name,
                    definition = asset,
                    contentStamp = definition.ContentHash,
                    variantCount = definition.VariantCount,
                });
            }

            var entities = new List<ManifestEntity>();
            for (int i = 0; i < world.Entities.Count; i++)
            {
                BakedEntity entity = world.Entities[i];
                var overrides = new List<OverrideEntry>();
                foreach (KeyValuePair<string, string> item in entity.Overrides)
                {
                    overrides.Add(new OverrideEntry(item.Key, item.Value));
                }

                entities.Add(new ManifestEntity
                {
                    authoringId = entity.AuthoringId,
                    name = entity.Name,
                    regionId = entity.RegionId,
                    definitionId = entity.DefinitionId,
                    x = entity.X,
                    y = entity.Y,
                    z = entity.Z,
                    yaw = entity.Yaw,
                    variant = entity.Variant,
                    scaleMilli = entity.ScaleMilli,
                    visible = entity.Visible,
                    alive = entity.Alive,
                    overrides = overrides,
                });
            }

            string reportHash = DefinitionHashing.Sha256Hex(outputs.Report);
            RegionManifest? manifest = AssetDatabase.LoadAssetAtPath<RegionManifest>(path);
            if (manifest != null && string.Equals(manifest.BakeReportHash, reportHash, StringComparison.Ordinal))
            {
                return false;
            }

            bool created = manifest == null;
            if (manifest == null)
            {
                manifest = ScriptableObject.CreateInstance<RegionManifest>();
            }

            manifest.Assign(world.WorldId, world.WorldName, world.StartRegionId, world.FocusEntityId, world.PreloadNeighbours,
                outputs.Fingerprint, outputs.CatalogTypeName, reportHash, regions, portals, definitions, entities);
            if (created)
            {
                EnsureDirectory(path);
                AssetDatabase.CreateAsset(manifest, path);
            }
            else
            {
                EditorUtility.SetDirty(manifest);
            }

            return true;
        }

        private static void WriteIfChanged(string path, string text, BakeResult result)
        {
            string full = Path.GetFullPath(path);
            if (File.Exists(full) && string.Equals(File.ReadAllText(full, Utf8), text, StringComparison.Ordinal))
            {
                return;
            }

            EnsureDirectory(path);
            File.WriteAllText(full, text, Utf8);
            result.ChangedFiles.Add(path);
        }

        private static void Compare(string path, string expected, List<GameplayDiagnostic> mismatches)
        {
            string full = Path.GetFullPath(path);
            if (!File.Exists(full) || !string.Equals(File.ReadAllText(full, Utf8), expected, StringComparison.Ordinal))
            {
                mismatches.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.BakeVerifyMismatch, path, "the file on disk is not the bake of the current content"));
            }
        }

        private static void EnsureDirectory(string path)
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static WorldDefinition? FindWorld(out string path)
        {
            path = string.Empty;
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(WorldDefinition));
            if (guids.Length != 1)
            {
                return null;
            }

            path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<WorldDefinition>(path);
        }

        private static BakeResult Success(Outputs outputs, string summary)
        {
            var result = new BakeResult(true, Array.Empty<GameplayDiagnostic>(), summary);
            Fill(result, outputs);
            return result;
        }

        private static void Fill(BakeResult result, Outputs outputs)
        {
            result.CatalogFingerprint = outputs.Fingerprint;
            result.Regions = outputs.Read.World.Regions.Count;
            result.Portals = outputs.Read.World.Portals.Count;
            result.Definitions = outputs.Read.World.Definitions.Count;
            result.Entities = outputs.Read.World.Entities.Count;
        }

        private static BakeResult Failed(string code, string message) =>
            new BakeResult(false, new[] { new GameplayDiagnostic(code, string.Empty, message) }, message);
    }
}
