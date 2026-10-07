#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.Entities;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hollowmere.R8_B
{
    // Retained real-provider qualification, not an offline test. All mutations target
    // owned assets and a private Studio state root. No settings, installed node or worker.
    public sealed class MediaQualification : ScriptableSingleton<MediaQualification>
    {
        private const string Folder = "Assets/Hollowmere/Tests/R8_B/Media/Generated";
        private const string ImagePath = Folder + "/cloth.png";
        private const string TamperedPath = Folder + "/tampered.png";
        [NonSerialized] private string output = "";
        [NonSerialized] private JObject config = null!;
        [NonSerialized] private StudioRuntime runtime = null!;
        [NonSerialized] private CompanionClient client = null!;
        [NonSerialized] private EtosAgentGateway gateway = null!;
        [NonSerialized] private MainThreadQueue queue = null!;
        [NonSerialized] private EtosMediaGenerator media = null!;
        [NonSerialized] private EntityDefinition definition = null!;
        [NonSerialized] private Task<HelloInfo>? hello;
        [NonSerialized] private Task<OpResult>? generation;
        [NonSerialized] private Task<VerifiedArtifact>? tamperedDownload;
        [NonSerialized] private OpResult generated = null!;
        [NonSerialized] private string importId = "", assignmentId = "", digest = "", beforeContent = "", recipe = "";
        [NonSerialized] private int phase, generationCalls, rendererIndex;
        [NonSerialized] private DateTime started;
        private readonly object EvidenceLock = new object();

        public static void Run() => instance.Begin();

        private void Begin()
        {
            output = "";
            try
            {
                string path = Environment.GetEnvironmentVariable("GAMECORE_R8_MEDIA_CONFIG") ?? throw new InvalidOperationException("Scratch config required");
                config = JObject.Parse(File.ReadAllText(path));
                output = (string)config["evidence"]!;
                Require((int)config["maximumImageCalls"]! == 1 && (double)config["maxCostUsd"]! == 0.20, "Missing one-image authorization");
                Require((int)config["maximumTtsCalls"]! == 0 && (int)config["maximumDescribeCalls"]! == 0, "Non-image paid ops forbidden");
                Require(!Application.isBatchMode && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Graphical :1 Editor required");
                Require(Environment.GetEnvironmentVariable("DISPLAY") == ":1", "Wrong owned display");
                Require(!EditorApplication.isPlaying, "Must start outside Play");
                Require(!File.Exists(Path.Combine(output, "dispatch-reservation.json")), "Paid call already reserved; never regenerate after an uncertain outcome");
                Require(!AssetDatabase.IsValidFolder(Folder), "Owned destination already exists; preserve prior attempt");
                started = DateTime.UtcNow;
                Directory.CreateDirectory(Folder);
                AssetDatabase.Refresh();
                var original = AssetDatabase.LoadAssetAtPath<EntityDefinition>("Assets/Hollowmere/Npcs/Definitions/MarenEntity.asset");
                Require(original?.Prefab != null, "Existing Hollowmere material target unavailable");
                definition = ScriptableObject.CreateInstance<EntityDefinition>();
                definition.name = "R8MediaTarget";
                definition.Configure(original!.Prefab, original.DefaultScaleMilli, original.StartsVisible, original.StartsAlive);
                definition.EnsureAuthoringId();
                AssetDatabase.CreateAsset(definition, Folder + "/Target.asset");
                AssetDatabase.SaveAssets();
                var bodies = definition.Prefab!.GetComponentsInChildren<Renderer>(true).Select((value, index) => new { value, index })
                    .Where(value => value.value.name == "Body").ToArray();
                Require(bodies.Length == 1 && bodies[0].value.sharedMaterials.Length == 1, "Material slot ambiguous");
                rendererIndex = bodies[0].index;
                Require(MaterialTextureBinding.Validate(definition, new MaterialTextureBinding { renderer = rendererIndex, slot = 0, property = "_BaseMap" }) == "", "Material binding invalid");
                runtime = StudioRuntime.Create(new StudioRuntimeOptions
                {
                    Paths = new StudioPaths((string)config["project"]!, Path.Combine((string)config["root"]!, "unity-state"), "r8-b-media"),
                    SearchFolders = new[] { Folder }, LoadIndexCache = false,
                });
                Require(runtime.Registry.Catalog.FindTool("entity.setMaterialTexture") != null, "Typed material tool absent");
                beforeContent = DefinitionCanonicalizer.ContentStamp(definition);
                recipe = DefinitionCanonicalizer.StructuralStamp(definition);
                // Only the production resolver reads the newly paired scratch credential.
                var credentials = EtosCredentials.FromKeyFile((string)config["pairing"]!);
                Require(credentials.NodeUrl == (string)config["nodeUrl"]!, "Scratch pairing URL mismatch");
                var uri = new Uri(credentials.NodeUrl!);
                Require(uri.IsLoopback && uri.Port != 7410, "Installed node is forbidden");
                client = new CompanionClient(new EtosClientOptions { NodeUrl = credentials.NodeUrl!, ProjectId = (string)config["projectId"]!,
                    DefaultMaxCostUsd = 0.20, TempDirectory = Path.Combine((string)config["root"]!, "downloads") }, credentials);
                client.Exchanged += exchange =>
                {
                    lock (EvidenceLock) File.AppendAllText(Path.Combine(output, "http-exchanges.jsonl"), new JObject
                    { ["method"] = exchange.Method, ["path"] = exchange.Path, ["status"] = exchange.Status, ["milliseconds"] = exchange.Milliseconds, ["code"] = exchange.Code }.ToString(Newtonsoft.Json.Formatting.None) + "\n");
                };
                queue = new MainThreadQueue();
                gateway = new EtosAgentGateway(client, runtime, queue, new MemoryCursorStore(), new EtosGatewayOptions
                { MaxCostUsd = 0.20, OpReplayWindow = TimeSpan.Zero, AutoImport = false });
                media = new EtosMediaGenerator(gateway, runtime, queue);
                Write("invocation", new JObject { ["row"] = "W-ETOS-07", ["startedUtc"] = started.ToString("o"),
                    ["imagePath"] = ImagePath, ["target"] = Folder + "/Target.asset", ["targetSource"] = "New owned entity definition referencing existing Maren prefab; no game asset mutated",
                    ["display"] = ":1", ["nodeUrl"] = client.NodeUrl, ["maximumImageCalls"] = 1, ["maxCostUsd"] = 0.20, ["automaticReplay"] = false });
                phase = 0;
                hello = client.HelloAsync();
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(false, error); }
        }

        private void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                Require((DateTime.UtcNow - started).TotalMinutes < 15, "Qualification deadline; automatic replay forbidden");
                queue.Pump();
                switch (phase)
                {
                    case 0:
                        if (!hello!.IsCompleted) return;
                        Write("hello", hello.GetAwaiter().GetResult().Raw);
                        Require(hello.Result.Provider("image") == "live", "Scratch image provider is not live");
                        var tariff = hello.Result.Raw["tariffs"]?.SingleOrDefault(value => (string?)value["op"] == "image")?["tariff"];
                        Require((string?)tariff?["kind"] == "operator" && (double?)tariff?["perUnitUsd"] == 0.20
                            && (string?)tariff?["provider"] == "echo-images" && (string?)tariff?["model"] == "gpt-image-2"
                            && (string?)tariff?["unit"] == "image", "Authenticated hello lacks the exact operator-bound image tariff");
                        Require(hello.Result.Provider("tts") == "not_configured" && hello.Result.Provider("describe") == "not_configured", "Scratch node exposes unintended paid providers");
                        phase = 1;
                        return;
                    case 1:
                        if (!Checkpoint("before")) return;
                        var request = new JObject { ["op"] = "generate.image", ["spec"] = new JObject
                        { ["prompt"] = "A seamless flat green woven wool cloth texture for a fantasy game robe, even lighting, no objects, no lettering. Qualification " + Guid.NewGuid().ToString("N") + "; do not draw this identifier.", ["count"] = 1, ["size"] = "1024x1024" }, ["max_cost_usd"] = 0.20 };
                        // FileMode.CreateNew is a durable global one-call reservation for this retained run.
                        using (var stream = new FileStream(Path.Combine(output, "dispatch-reservation.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        using (var writer = new StreamWriter(stream)) { writer.Write(request.ToString()); writer.Flush(); stream.Flush(true); }
                        generationCalls = 1;
                        phase = 2;
                        generation = gateway.GenerateAsync(new OpRequest("generate.image", (JObject)request["spec"]!, 0.20), CancellationToken.None);
                        return;
                    case 2:
                        if (!generation!.IsCompleted) return;
                        generated = generation.GetAwaiter().GetResult();
                        Write("generate", new JObject { ["succeeded"] = generated.Succeeded, ["provider"] = generated.Provider,
                            ["providerSha256"] = generated.Sha256, ["bytes"] = generated.Bytes?.Length, ["opState"] = generated.State,
                            ["refusal"] = generated.Refusal == null ? null : StudioJson.ToToken(generated.Refusal), ["maxCostUsd"] = 0.20 });
                        Require(generated.Succeeded && generated.Bytes != null && generated.Sha256 != null, "Provider/download refused; no substitute image");
                        digest = generated.Sha256!;
                        Require(ContentStamp.Sha256Hex(generated.Bytes!) == digest, "Verified download digest mismatch");
                        Require((string?)generated.State?["state"] == "succeeded", "Generation is not terminal succeeded");
                        File.WriteAllBytes(Path.Combine(output, "generated.png"), generated.Bytes!);
                        var imported = media.ImportOnMain(generated, ImagePath, "texture", "R8-B real generated texture verified import", null);
                        Write("import-report", Report(imported));
                        Require(imported.Ok && imported.Artifact!.Sha256 == digest, "Production media import failed");
                        importId = imported.Report!.Entry.Id;
                        Journal(importId, "import-applied");
                        Observe("applied", true, false);
                        phase = 3;
                        return;
                    case 3:
                        if (!Checkpoint("generated")) return;
                        runtime.Index.Rebuild();
                        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ImagePath);
                        var assignment = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId,
                            new Intent("Assign the newly generated verified texture through the typed material tool", IntentOrigin.Manual),
                            new[] { new Operation("texture", "entity.setMaterialTexture", runtime.Resolver.BuildRef(definition, AuthorScope.Definition, true),
                                new JObject { ["texture"] = StudioJson.ToToken(runtime.Resolver.BuildRef(texture)!), ["renderer"] = rendererIndex, ["slot"] = 0, ["property"] = "_BaseMap" }) });
                        Write("assignment-request", (JObject)StudioJson.ToToken(assignment));
                        var applied = runtime.Engine.Apply(assignment);
                        assignmentId = assignment.Id;
                        Write("assignment-report", new JObject { ["ok"] = applied.Ok, ["state"] = applied.State.ToString(),
                            ["diagnostics"] = new JArray(applied.Diagnostics.Select(StudioJson.ToToken)) });
                        Require(applied.Ok, "Typed material assignment refused");
                        Journal(assignmentId, "assignment-applied");
                        Observe("assigned", true, true);
                        phase = 4;
                        return;
                    case 4:
                        History(assignmentId, true, "assignment-undo");
                        Observe("assignment-undone", true, false);
                        History(importId, true, "import-undo");
                        Observe("undone", false, false);
                        phase = 5;
                        return;
                    case 5:
                        if (!Checkpoint("undone")) return;
                        History(importId, false, "import-redo");
                        Observe("import-redone", true, false);
                        History(assignmentId, false, "assignment-redo");
                        Observe("redone", true, true);
                        phase = 6;
                        return;
                    case 6:
                        if (!Checkpoint("redone")) return;
                        History(assignmentId, true, "assignment-final-undo");
                        History(importId, true, "import-final-undo");
                        Observe("final-undone", false, false);
                        ProveTamperedImport();
                        client.Options.DownloadTamperHook = temporary =>
                        {
                            byte[] bytes = File.ReadAllBytes(temporary);
                            string before = ContentStamp.Sha256Hex(bytes);
                            bytes[bytes.Length / 2] ^= 1;
                            File.WriteAllBytes(temporary, bytes);
                            File.WriteAllBytes(Path.Combine(output, "tampered-download.bin"), bytes);
                            Write("download-tamper", new JObject { ["beforeSha256"] = before, ["afterSha256"] = ContentStamp.Sha256Hex(bytes), ["bytes"] = bytes.Length });
                        };
                        tamperedDownload = client.DownloadArtifactAsync(digest, generated.Bytes!.Length);
                        phase = 7;
                        return;
                    case 7:
                        if (!tamperedDownload!.IsCompleted) return;
                        try { tamperedDownload.GetAwaiter().GetResult(); throw new InvalidOperationException("Tampered download was accepted"); }
                        catch (EtosException error)
                        {
                            Write("download-refusal", new JObject { ["code"] = error.Code, ["message"] = EtosRedaction.Redact(error.Message), ["assetExists"] = File.Exists(runtime.Paths.Absolute(TamperedPath)) });
                            Require(error.Code == EtosCodes.ArtifactDigestMismatch, "Download failed for a different reason");
                        }
                        client.Options.DownloadTamperHook = null;
                        phase = 8;
                        return;
                    case 8:
                        if (!Checkpoint("final")) return;
                        Require(generationCalls == 1, "Unexpected generation count");
                        Finish(true, null);
                        return;
                }
            }
            catch (Exception error) { Finish(false, error); }
        }

        private void ProveTamperedImport()
        {
            byte[] corrupted = (byte[])generated.Bytes!.Clone();
            corrupted[corrupted.Length / 2] ^= 1;
            int before = runtime.Journal.List().Count;
            var result = media.ImportOnMain(new OpResult(digest, generated.MediaType, corrupted, null, null, generated.Name, generated.Provider),
                TamperedPath, "texture", "Must refuse altered downloaded bytes", null);
            Write("tampered-import", new JObject { ["ok"] = result.Ok, ["expectedSha256"] = digest,
                ["actualSha256"] = ContentStamp.Sha256Hex(corrupted), ["problem"] = result.Problem == null ? null : StudioJson.ToToken(result.Problem),
                ["journalBefore"] = before, ["journalAfter"] = runtime.Journal.List().Count,
                ["assetExists"] = File.Exists(runtime.Paths.Absolute(TamperedPath)), ["metaExists"] = File.Exists(runtime.Paths.Absolute(TamperedPath) + ".meta") });
            Require(!result.Ok && result.Problem?.Code == EtosCodes.ArtifactDigestMismatch, "Media adapter accepted altered bytes");
            Require(runtime.Journal.List().Count == before && !File.Exists(runtime.Paths.Absolute(TamperedPath)) && !File.Exists(runtime.Paths.Absolute(TamperedPath) + ".meta"), "Tamper refusal wrote an import");
        }

        private void Observe(string label, bool file, bool assigned)
        {
            AssetDatabase.SaveAssets();
            bool exists = File.Exists(runtime.Paths.Absolute(ImagePath));
            string? actual = exists ? ContentStamp.Sha256Hex(File.ReadAllBytes(runtime.Paths.Absolute(ImagePath))) : null;
            string retained = ContentStamp.Sha256Hex(runtime.Artifacts.Read(digest));
            bool binding = definition.MaterialTextures.Any(value => value.renderer == rendererIndex && value.slot == 0 && value.property == "_BaseMap" && AssetDatabase.GetAssetPath(value.texture) == ImagePath);
            Write("observe-" + label, new JObject { ["assetExists"] = exists, ["fileSha256"] = actual, ["retainedSha256"] = retained,
                ["boundToGeneratedTexture"] = binding, ["entityContent"] = DefinitionCanonicalizer.ContentStamp(definition),
                ["entityRecipe"] = DefinitionCanonicalizer.StructuralStamp(definition), ["generationCalls"] = generationCalls });
            Require(exists == file && retained == digest && (!exists || actual == digest), "Import History did not preserve exact provider bytes");
            Require(binding == assigned, "Material assignment History did not restore expected binding");
            Require(DefinitionCanonicalizer.StructuralStamp(definition) == recipe, "Texture changed entity structural recipe");
            if (!assigned) Require(DefinitionCanonicalizer.ContentStamp(definition) == beforeContent, "Undo did not restore original entity content");
            if (exists) File.WriteAllBytes(Path.Combine(output, "image-" + label + ".png"), File.ReadAllBytes(runtime.Paths.Absolute(ImagePath)));
        }

        private void History(string id, bool undo, string label)
        {
            HistoryResult result = undo ? runtime.History.Undo(id) : runtime.History.Redo(id);
            Write(label, new JObject { ["ok"] = result.Ok, ["state"] = result.State?.ToString(), ["changeSetId"] = id,
                ["surface"] = "HistoryService." + (undo ? "Undo" : "Redo"), ["diagnostics"] = new JArray(result.Diagnostics.Select(StudioJson.ToToken)) });
            Journal(id, label + "-journal");
            Require(result.Ok, "Normal History refused " + label);
        }

        private bool Checkpoint(string label)
        {
            Write("checkpoint", new JObject { ["row"] = "W-ETOS-07", ["label"] = label, ["generationCalls"] = generationCalls });
            string path = Path.Combine(output, "ledger-" + label + ".json");
            if (!File.Exists(path)) return false;
            Require((string?)JObject.Parse(File.ReadAllText(path))["status"] == "PASS", "Read-only ledger checkpoint failed: " + label);
            return true;
        }

        private static JObject Report(MediaImport result) => new JObject { ["ok"] = result.Ok, ["assetPath"] = result.AssetPath,
            ["journalId"] = result.Report?.Entry.Id, ["state"] = result.Report?.State.ToString(),
            ["artifact"] = result.Artifact == null ? null : StudioJson.ToToken(result.Artifact), ["diagnostics"] = new JArray(result.Diagnostics.Select(StudioJson.ToToken)) };
        private void Journal(string id, string name) => Write(name, (JObject)StudioJson.ToToken(runtime.Journal.Read(id)!));
        private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
        private void Write(string name, JObject value)
        {
            lock (EvidenceLock)
            {
                string path = Path.Combine(output, name + ".json");
                File.WriteAllText(path + ".tmp", EtosRedaction.Redact(value.ToString()));
                if (File.Exists(path)) File.Delete(path);
                File.Move(path + ".tmp", path);
            }
        }
        private void Finish(bool success, Exception? error)
        {
            EditorApplication.update -= Tick;
            if (output.Length > 0) Write("result", new JObject { ["row"] = "W-ETOS-07", ["status"] = success ? "PASS" : "FAIL",
                ["endedUtc"] = DateTime.UtcNow.ToString("o"), ["phase"] = phase, ["generationCalls"] = generationCalls,
                ["maxCostUsd"] = 0.20, ["ttsCalls"] = 0, ["describeCalls"] = 0, ["providerSha256"] = digest,
                ["importChangeSetId"] = importId, ["assignmentChangeSetId"] = assignmentId,
                ["reason"] = success ? "One real scratch-provider image; verified download/media import; typed material apply/undo/redo; no regenerated usage; both altered-download and altered-import digest checks refused before import." : EtosRedaction.Redact(error?.ToString() ?? "unknown") });
            gateway?.Dispose();
            client?.Dispose();
            runtime?.Dispose();
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
