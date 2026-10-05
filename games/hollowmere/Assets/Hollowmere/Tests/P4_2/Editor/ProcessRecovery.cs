// R2-03/R2-38: kill this Editor at a real file-write/checkpoint boundary, then recover in a new process.
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Hollowmere.P4_2
{
    public static class ProcessRecovery
    {
        private const string Folder = "Assets/Hollowmere/Tests/P4_2/Fixture";
        private const string Asset = Folder + "/data.txt";
        private const string Second = Folder + "/second.txt";

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length) throw new InvalidOperationException("Missing " + name);
            return args[index + 1];
        }

        private static StudioRuntime Open(string state)
        {
            string project = Directory.GetParent(Application.dataPath)!.FullName;
            return StudioRuntime.Create(new StudioRuntimeOptions
            {
                Paths = new StudioPaths(project, Path.Combine(state, "state"), "p42-process-recovery"),
                SearchFolders = new[] { Folder },
                LoadIndexCache = false,
            });
        }

        public static void Crash()
        {
            string output = Arg("-p42State");
            string mode = Arg("-p42Recovery");
            Directory.CreateDirectory(output);
            if (File.Exists(Path.Combine(output, "crash.json"))) throw new InvalidOperationException("Refusing to overwrite a retained crash attempt.");
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Asset, "original", new UTF8Encoding(false));
            AssetDatabase.ImportAsset(Asset, ImportAssetOptions.ForceSynchronousImport);
            File.Copy(Asset + ".meta", Path.Combine(output, "preimage.meta"));
            using StudioRuntime runtime = Open(output);
            byte[] bytes = Encoding.UTF8.GetBytes("replacement");
            string digest = runtime.Artifacts.Put(bytes, null);
            Operation Import(string id, string path) => new Operation(id, BuiltInToolIds.AssetImport, null,
                new JObject { ["path"] = path, ["artifact"] = new JObject { ["artifact"] = "sha256:" + digest } });
            ChangeSet change = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId,
                new Intent("P4.2 real Editor death " + mode, IntentOrigin.Manual),
                new[] { Import("op1", Asset), Import("op2", Second) },
                artifacts: new[] { new ArtifactRef(digest, "text/plain", bytes.Length, "data.txt") });
            File.WriteAllText(Path.Combine(output, "candidate.json"), StudioJson.Serialize(change));
            runtime.Engine.Options.FaultHook = (point, opId) =>
            {
                bool reached = mode == "rollback" ? point == EngineFaultPoint.AfterFileWrite : point == EngineFaultPoint.AfterOperation && opId == "op1";
                if (!reached) return;
                var process = System.Diagnostics.Process.GetCurrentProcess();
                var marker = new JObject
                {
                    ["changeSetId"] = change.Id, ["mode"] = mode, ["point"] = point.ToString(),
                    ["pid"] = process.Id, ["utc"] = DateTime.UtcNow.ToString("o"),
                    ["mutatedBytesObserved"] = File.ReadAllText(Asset) == "replacement",
                };
                File.WriteAllText(Path.Combine(output, "crash.json"), marker.ToString());
                // SIGKILL of this test Editor only: no managed catch/finally can perform a rollback.
                process.Kill();
            };
            runtime.Engine.Apply(change);
            throw new InvalidOperationException("The intended real-process kill point was not reached.");
        }

        public static void Recover()
        {
            string output = Arg("-p42State");
            JObject crash = JObject.Parse(File.ReadAllText(Path.Combine(output, "crash.json")));
            string id = (string)crash["changeSetId"]!;
            string mode = (string)crash["mode"]!;
            using StudioRuntime runtime = Open(output);
            ChangeSet before = runtime.Journal.Read(id) ?? throw new InvalidOperationException("Journal missing after killed Editor.");
            if (before.State != ChangeSetState.Interrupted || !runtime.History.Interrupted().Any(x => x.Id == id))
                throw new InvalidOperationException("Killed operation is not discoverable as Interrupted.");
            File.WriteAllText(Path.Combine(output, "interrupted.json"), StudioJson.Serialize(before));
            bool uncertainResumeRefused = false;
            HistoryResult result;
            if (mode == "rollback")
            {
                uncertainResumeRefused = !runtime.History.ResumeInterrupted(id).Ok;
                if (!uncertainResumeRefused) throw new InvalidOperationException("Unknown file-write completion must not be replayed.");
                result = runtime.History.RollbackInterrupted(id);
                if (!result.Ok || File.ReadAllText(Asset) != "original" ||
                    !File.ReadAllBytes(Asset + ".meta").SequenceEqual(File.ReadAllBytes(Path.Combine(output, "preimage.meta"))))
                    throw new InvalidOperationException("Rollback failed to restore exact file/meta preimages.");
            }
            else
            {
                result = runtime.History.ResumeInterrupted(id);
                if (!result.Ok || result.State != ChangeSetState.Applied || File.ReadAllText(Asset) != "replacement" || File.ReadAllText(Second) != "replacement")
                    throw new InvalidOperationException("Checkpointed operation did not resume to Applied.");
            }
            ChangeSet after = runtime.Journal.Read(id)!;
            File.WriteAllText(Path.Combine(output, "recovered.json"), StudioJson.Serialize(after));
            File.WriteAllText(Path.Combine(output, "recovery-result.json"), new JObject
            {
                ["ok"] = true, ["mode"] = mode, ["changeSetId"] = id,
                ["originalPid"] = crash["pid"], ["recoveryPid"] = System.Diagnostics.Process.GetCurrentProcess().Id,
                ["uncertainResumeRefused"] = uncertainResumeRefused, ["finalState"] = after.EffectiveState.ToString(),
                ["utc"] = DateTime.UtcNow.ToString("o"),
            }.ToString());
            AssetDatabase.DeleteAsset(Folder);
            EditorApplication.Exit(0);
        }
    }
}
