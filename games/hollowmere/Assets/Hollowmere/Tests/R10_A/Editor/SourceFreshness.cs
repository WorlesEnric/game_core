#nullable enable
using System;
using System.IO;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Studio.UI;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Hollowmere.R10_A
{
    // A real worker-format authored edit, creator Apply and ordinary History Undo.
    // No worker/provider task, manual bake, generated-source patch or custom undo.
    public static class SourceFreshness
    {
        private const string EntityPath = "Assets/Hollowmere/Interactables/Definitions/PickupCoinsEntity.asset";
        private const string WorldRoot = "Assets/Hollowmere/World/";

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("R10_A_01 freshness: " + message);
        }

        private static void Save(string directory, string name, JToken value) =>
            StudioPaths.WriteAllTextAtomic(Path.Combine(directory, name), value.ToString());

        private static string Fingerprint()
        {
            string? fingerprint = new ReflectionAdmissionCatalog().WorldFingerprint(out string? problem);
            Require(fingerprint != null && fingerprint.Length == 64, problem ?? "read-only world verification failed");
            return fingerprint!;
        }

        private static JObject BakedFiles(string project)
        {
            var files = new JObject();
            foreach (string relative in new[] {
                WorldRoot + "Catalog/HollowmereCatalog.catalog.json", WorldRoot + "Catalog/Hollowmere.bake.json",
                WorldRoot + "Generated/HollowmereCatalog.g.cs", WorldRoot + "Hollowmere.manifest.asset",
            }) files[relative] = ContentStamp.Sha256Hex(File.ReadAllBytes(Path.Combine(project, relative)));
            return files;
        }

        public static void Exercise(StudioRuntime runtime, string evidence)
        {
            Require(!EditorApplication.isPlaying, "the authored edit must run in saved Edit mode");
            string output = Path.Combine(evidence, "worker-edit-undo");
            Require(!Directory.Exists(output), "previous freshness witnesses must remain intact");
            Directory.CreateDirectory(output);
            string project = runtime.Paths.ProjectRoot;
            StageAdmission admission = StageAdmission.Of(runtime);
            // Saves through the product project.save action, then publishes read-only source context.
            admission.PrepareStageWorldSnapshot();
            string snapshotPath = Path.Combine(project, StageWorldSnapshot.RelativePath);
            File.Copy(snapshotPath, Path.Combine(output, "before-snapshot.json"));
            string assetPath = Path.Combine(project, EntityPath);
            byte[] beforeBytes = File.ReadAllBytes(assetPath);
            File.WriteAllBytes(Path.Combine(output, "before.asset"), beforeBytes);
            JObject bakedBefore = BakedFiles(project);
            string beforeFingerprint = Fingerprint();
            string committedFingerprint = (string)JObject.Parse(File.ReadAllText(
                Path.Combine(project, WorldRoot + "Catalog/Hollowmere.bake.json")))["catalogFingerprint"]!;
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(EntityPath)
                ?? throw new InvalidOperationException("R10_A_01 entity definition is absent");
            var member = runtime.Identity.Describe(asset)?.FindMember("interactionKind")
                ?? throw new InvalidOperationException("R10_A_01 structural interactionKind metadata is absent");
            JToken beforeValue = runtime.Resolver.Codec.ReadMember(asset, member);
            string changedValue = ((string?)beforeValue ?? string.Empty) + "r10-a-freshness";
            ChangeSet typed = new ManualEditCommitter(runtime).Build(asset, member, new JValue(changedValue))
                ?? throw new InvalidOperationException("R10_A_01 cannot resolve the existing entity definition");
            var candidate = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId,
                new Intent("R10_A_01 retained worker structural edit before mechanism Stage", IntentOrigin.Agent),
                typed.Operations, requirements: CandidateRequirements.Implied(runtime, typed));
            Save(output, "candidate.json", StudioJson.ToToken(candidate));
            runtime.Index.Rebuild();
            CandidateCoordinator coordinator = StudioUiSession.Context.Candidates;
            CandidateEntry entry = coordinator.Add(candidate.Id, candidate,
                runtime.Registry.Catalog.Revision ?? runtime.Registry.Catalog.ComputeRevision());
            StagedChangeSet preview = coordinator.Preview(entry);
            Save(output, "preview.json", new JObject { ["ok"] = preview.Ok,
                ["diagnostics"] = JArray.FromObject(preview.AllDiagnostics) });
            Require(preview.Ok, "worker candidate preview refused; retained diagnostics explain why");
            ApplyReport applied = coordinator.Apply(entry);
            Save(output, "applied-journal.json", StudioJson.ToToken(applied.Entry));
            Require(applied.Ok && runtime.Journal.Read(candidate.Id)?.EffectiveState == ChangeSetState.Applied,
                "creator Apply refused; use retained journal rather than rewriting the fixture");
            // A currently edited structural world cannot be represented by its
            // still-loaded generated catalog. Explicit Stage must fail closed,
            // rather than export authority that runtime restoration cannot match.
            string? editedSnapshotRefusal = null;
            try { admission.PrepareStageWorldSnapshot(); }
            catch (InvalidOperationException error) when (error.Message.StartsWith("bake_stale:", StringComparison.Ordinal))
            {
                editedSnapshotRefusal = error.Message;
            }
            Save(output, "applied-snapshot-refusal.json", new JObject {
                ["refused"] = editedSnapshotRefusal != null, ["detail"] = editedSnapshotRefusal,
            });
            File.Copy(assetPath, Path.Combine(output, "applied.asset"));
            string appliedFingerprint = Fingerprint();
            JObject bakedApplied = BakedFiles(project);
            Save(output, "applied.json", new JObject {
                ["changeSetId"] = candidate.Id, ["before"] = beforeValue,
                ["applied"] = runtime.Resolver.Codec.ReadMember(asset, member),
                ["beforeFingerprint"] = beforeFingerprint, ["committedFingerprint"] = committedFingerprint,
                ["appliedFingerprint"] = appliedFingerprint, ["staleCommittedBake"] = appliedFingerprint != committedFingerprint,
                ["bakedFilesUnchanged"] = JToken.DeepEquals(bakedBefore, bakedApplied),
            });
            // Always use normal Undo before evaluating freshness assertions. Any
            // refusal keeps its actual journal and authored bytes for recovery.
            HistoryResult undone = runtime.History.Undo(candidate.Id);
            Save(output, "undo.json", new JObject { ["ok"] = undone.Ok, ["state"] = undone.State?.ToString(),
                ["diagnostics"] = JArray.FromObject(undone.Diagnostics) });
            Require(undone.Ok && undone.State == ChangeSetState.Undone, "normal History Undo refused");
            admission.PrepareStageWorldSnapshot();
            File.Copy(snapshotPath, Path.Combine(output, "restored-snapshot.json"));
            File.Copy(assetPath, Path.Combine(output, "restored.asset"));
            string restoredFingerprint = Fingerprint();
            JObject bakedAfter = BakedFiles(project);
            string beforeSha = ContentStamp.Sha256Hex(beforeBytes);
            string restoredSha = ContentStamp.Sha256Hex(File.ReadAllBytes(assetPath));
            var result = new JObject {
                ["changeSetId"] = candidate.Id, ["origin"] = "Agent", ["field"] = "interactionKind", ["path"] = EntityPath,
                ["before"] = beforeValue, ["restored"] = runtime.Resolver.Codec.ReadMember(asset, member),
                ["beforeSha256"] = beforeSha, ["restoredSha256"] = restoredSha,
                ["beforeFingerprint"] = beforeFingerprint, ["appliedFingerprint"] = appliedFingerprint,
                ["restoredFingerprint"] = restoredFingerprint, ["committedFingerprint"] = committedFingerprint,
                ["editedSnapshotRefused"] = editedSnapshotRefusal != null,
                ["staleCommittedBakeObserved"] = appliedFingerprint != committedFingerprint,
                ["bakedFilesBefore"] = bakedBefore, ["bakedFilesAfter"] = bakedAfter,
                ["bakedFilesUnchanged"] = JToken.DeepEquals(bakedBefore, bakedApplied) && JToken.DeepEquals(bakedBefore, bakedAfter),
                ["snapshotAfterNormalUndo"] = true,
            };
            Save(output, "result.json", result);
            Require(editedSnapshotRefusal != null, "Stage accepted a changed structure with stale generated runtime code");
            Require(appliedFingerprint != beforeFingerprint && appliedFingerprint != committedFingerprint,
                "the normal worker edit did not produce a stale committed structural fingerprint");
            Require(beforeSha == restoredSha && beforeFingerprint == restoredFingerprint
                && JToken.DeepEquals(beforeValue, runtime.Resolver.Codec.ReadMember(asset, member)),
                "normal Undo did not restore the original authored bytes and computed fingerprint");
            Require((bool)result["bakedFilesUnchanged"]!, "freshness export wrote generated bake outputs");
        }
    }
}
