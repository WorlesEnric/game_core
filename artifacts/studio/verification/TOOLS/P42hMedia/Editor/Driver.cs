#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.World;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Model;
using GameCore.Studio.UI;
using Hollowmere.Boot;
using Hollowmere.P2_1.Evidence;
using Hollowmere.UiAudio;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace P42h.Media
{
    // Main runs this harness graphically and owns the read-only ledger observer. No test framework,
    // synthetic gateway, direct node call, candidate repair, or repeat-generation recovery path.
    public sealed class Driver : ScriptableSingleton<Driver>
    {
        private const string Prefix = "P42h.Media.";
        private const string EntityPath = "Assets/Hollowmere/Npcs/Definitions/MarenEntity.asset";
        private const string NpcPath = "Assets/Hollowmere/Npcs/Definitions/Maren.asset";
        private const string BehaviourPath = "Assets/Hollowmere/Npcs/Definitions/MarenBehaviour.asset";
        private const string DialoguePath = "Assets/Hollowmere/Dialogue/Graphs/Maren.asset";
        private const string VillagePath = "Assets/Hollowmere/Regions/ThornwickVillage.unity";
        private const string BootPath = "Assets/Hollowmere/Boot/Boot.unity";
        private const string WorldPath = "Assets/Hollowmere/World/Hollowmere.asset";
        private const double CeilingUsd = 0.20;
        [NonSerialized] private Task<MediaImport>? generation;
        private static StudioUiContext Context => StudioUiSession.Context;
        private static string Output => SessionState.GetString(Prefix + "out", "");
        private static string Mode => SessionState.GetString(Prefix + "mode", "");
        private static string Row => Mode == "portrait" ? "W-EDIT-03" : "W-AI-01";
        private static int Phase { get => SessionState.GetInt(Prefix + "phase", 0); set => SessionState.SetInt(Prefix + "phase", value); }
        private static string AssetPath => SessionState.GetString(Prefix + "asset", "");
        private static string ImportId => SessionState.GetString(Prefix + "import", "");
        private static string Digest => SessionState.GetString(Prefix + "digest", "");
        private static EntityDefinition Definition => AssetDatabase.LoadAssetAtPath<EntityDefinition>(EntityPath)
            ?? throw new Unavailable("Maren entity definition is unavailable at " + EntityPath);

        public static void RunPortrait() => Begin("portrait");
        public static void RunRobe() => Begin("robe");

        public static void ResumePortrait()
        {
            string output = Path.GetFullPath(Environment.GetEnvironmentVariable("GAMECORE_P42H_OUT") ?? throw new InvalidOperationException("GAMECORE_P42H_OUT required"));
            var generated = JObject.Parse(File.ReadAllText(Path.Combine(output, "generate.json")));
            Require((bool?)generated["ok"] == true, "Only a proven successful original import can resume");
            SessionState.SetString(Prefix + "out", output);
            SessionState.SetString(Prefix + "mode", "portrait");
            SessionState.SetString(Prefix + "start", DateTime.UtcNow.ToString("o"));
            SessionState.SetString(Prefix + "asset", (string)generated["assetPath"]!);
            SessionState.SetString(Prefix + "import", (string)generated["journalId"]!);
            SessionState.SetString(Prefix + "digest", (string)generated["providerSha256"]!);
            SessionState.SetString(Prefix + "checkpoint", "");
            SessionState.SetInt(Prefix + "generationCalls", 1);
            SessionState.SetBool(Prefix + "resume", true);
            SessionState.SetBool(Prefix + "active", true);
            Phase = 0;
            Write("resume", new JObject { ["generationIssued"] = false, ["originalImport"] = ImportId, ["originalDigest"] = Digest });
            Hook();
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Begin(string mode)
        {
            string output = Environment.GetEnvironmentVariable("GAMECORE_P42H_OUT") ?? "";
            if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("GAMECORE_P42H_OUT is required");
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);
            // A new process must never resubmit an uncertain paid call from an earlier invocation.
            if (File.Exists(Path.Combine(output, "invocation.json")))
                throw new InvalidOperationException("Output already has an invocation; do not regenerate. Retain and inspect its receipt.");
            SessionState.SetString(Prefix + "out", output);
            SessionState.SetString(Prefix + "mode", mode);
            SessionState.SetString(Prefix + "start", DateTime.UtcNow.ToString("o"));
            SessionState.SetString(Prefix + "take", Guid.NewGuid().ToString("N"));
            SessionState.SetString(Prefix + "asset", "Assets/Hollowmere/Generated/P42h/" + mode + "-" + SessionState.GetString(Prefix + "take", "") + ".png");
            SessionState.SetString(Prefix + "import", "");
            SessionState.SetString(Prefix + "digest", "");
            SessionState.SetString(Prefix + "assignment", "");
            SessionState.SetString(Prefix + "checkpoint", "");
            SessionState.SetInt(Prefix + "generationCalls", 0);
            SessionState.SetBool(Prefix + "resume", false);
            SessionState.SetBool(Prefix + "active", true);
            Phase = 0;
            Write("invocation", new JObject { ["row"] = Row, ["mode"] = mode, ["startedUtc"] = DateTime.UtcNow.ToString("o"),
                ["project"] = Path.GetFullPath(Path.Combine(Application.dataPath, "..")), ["maxCostUsd"] = CeilingUsd,
                ["maximumImageCalls"] = 1, ["assetPath"] = AssetPath, ["requestChangeSetId"] = null,
                ["authority"] = "Production app-owned direct media; omit changeSetId. Local journal IDs are never sent as request authority." });
            Hook();
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(Prefix + "active", false) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                if ((DateTime.UtcNow - DateTime.Parse(SessionState.GetString(Prefix + "start", ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)).TotalMinutes > 20)
                    throw new Unavailable("Bounded 20-minute harness deadline; no retry or substitute generation is permitted");
                instance.Advance();
            }
            catch (Unavailable error) { Finish("BLOCKED", error.Message); }
            catch (Exception error) { Write("error", new JObject { ["type"] = error.GetType().FullName, ["message"] = EtosRedaction.Redact(error.ToString()) }); Finish("FAIL", error.Message); }
        }

        private void Advance()
        {
            switch (Phase)
            {
                case 0:
                    if (EditorApplication.isPlaying || Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                        throw new Unavailable("Requires graphical Editor, outside Play; use the graphical unity-batch adapter on :1");
                    EditorSceneManager.OpenScene(VillagePath);
                    EditorApplication.ExecuteMenuItem("GameCore/Studio/Open Studio");
                    StudioHistoryWindow.Open();
                    P42hImportedImageWindow.Open(AssetPath);
                    EtosStudioSession.Start();
                    Phase = 1;
                    return;
                case 1:
                    if (!Context.Gateway.Status.AgentReady || EtosStudioSession.Gateway == null) return;
                    if (SessionState.GetBool(Prefix + "resume", false))
                    {
                        Require(Context.Runtime.Journal.Read(ImportId)?.EffectiveState == ChangeSetState.Applied, "Original portrait import is not Applied");
                        Require(Sha(Context.Runtime.Paths.Absolute(AssetPath)) == Digest, "Original portrait bytes changed");
                        Phase = 12;
                        return;
                    }
                    if (Mode == "robe") PreflightRobe();
                    if (!Checkpoint("before")) return;
                    if (Mode == "portrait") { Phase = 10; return; }
                    PreparePlay("before", 2);
                    return;
                case 2: EnterPreparedPlay(3); return;
                case 3:
                    if (!ObservePlay("before")) return;
                    Phase = 4;
                    EditorApplication.ExitPlaymode();
                    return;
                case 4:
                    if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
                    EditorSceneManager.OpenScene(VillagePath);
                    // Bake may refresh content stamps; baseline the already-baked authored bytes,
                    // not a stale pre-bake file image. No generation has happened yet.
                    PreflightRobe();
                    Phase = 10;
                    return;
                case 10:
                    if (!Context.Gateway.Status.AgentReady || EtosStudioSession.Gateway == null) return;
                    if (File.Exists(Context.Runtime.Paths.Absolute(AssetPath))) throw new Unavailable("Generated destination already exists; no pre-existing media can qualify");
                    if (SessionState.GetInt(Prefix + "generationCalls", 0) != 0) throw new Unavailable("Generation was already issued; refusing to submit again");
                    string prompt = Mode == "portrait"
                        ? "A painted head-and-shoulders portrait of Maren, a kindly adult village healer in a misty marsh fantasy village, muted natural colours, plain background, no lettering."
                        : "A seamless tileable texture of soft green woven wool cloth for Maren the village healer's green robe, flat even lighting, no seams, no text, green cloth filling the whole image.";
                    prompt += " Qualification take " + SessionState.GetString(Prefix + "take", "") + "; do not draw this identifier.";
                    Write("request", new JObject { ["op"] = "generate.image", ["prompt"] = prompt, ["maxCostUsd"] = CeilingUsd, ["changeSetId"] = null,
                        ["note"] = "No worker prompt submission, TTS, describe, direct node call or client-created request ID." });
                    SessionState.SetInt(Prefix + "generationCalls", 1);
                    Phase = 11; // Persist before dispatch; reload can only refuse, never generate twice.
                    generation = new EtosMediaGenerator(Context.Gateway, Context.Runtime, EtosStudioSession.Gateway.Queue)
                        .GenerateImageAsync(prompt, AssetPath, null, CeilingUsd, null);
                    return;
                case 11:
                    if (generation == null) throw new Unavailable("Domain reload during paid operation lost its Task; inspect companion receipt. Automatic resubmission is forbidden.");
                    if (!generation.IsCompleted) return;
                    RetainGeneration(generation.GetAwaiter().GetResult());
                    generation = null;
                    Phase = 12;
                    return;
                case 12:
                    if (!Checkpoint("generated")) return;
                    CaptureImported("applied", true);
                    Phase = Mode == "portrait" ? 20 : 30;
                    return;
                case 20:
                    History(ImportId, true, "portrait-undo");
                    CaptureImported("undone", false);
                    Phase = 21;
                    return;
                case 21:
                    if (!Checkpoint("undone")) return;
                    History(ImportId, false, "portrait-redo");
                    CaptureImported("redone", true);
                    Phase = 22;
                    return;
                case 22:
                    if (!Checkpoint("redone")) return;
                    Require(SessionState.GetInt(Prefix + "generationCalls", 0) == 1, "Portrait redo submitted generation again");
                    History(ImportId, true, "portrait-cleanup-undo");
                    Phase = 23;
                    return;
                case 23:
                    if (!Checkpoint("final")) return;
                    Finish("PASS", "One current image generated/imported through the production media service; normal History undo/redo restored identical retained bytes with unchanged companion charge ledger; cleanup used normal History undo.");
                    return;
                case 30:
                    ApplyRobe();
                    PreparePlay("applied", 31);
                    return;
                case 31: EnterPreparedPlay(32); return;
                case 32:
                    if (!ObservePlay("applied")) return;
                    Phase = 33;
                    EditorApplication.ExitPlaymode();
                    return;
                case 33:
                    if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
                    EditorSceneManager.OpenScene(VillagePath);
                    History(SessionState.GetString(Prefix + "assignment", ""), true, "robe-undo");
                    VerifyDefinition(false);
                    PreparePlay("undone", 34);
                    return;
                case 34: EnterPreparedPlay(35); return;
                case 35:
                    if (!ObservePlay("undone")) return;
                    Phase = 36;
                    EditorApplication.ExitPlaymode();
                    return;
                case 36:
                    if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
                    if (!Checkpoint("undone")) return;
                    EditorSceneManager.OpenScene(VillagePath);
                    History(ImportId, true, "robe-import-cleanup-undo");
                    Phase = 37;
                    return;
                case 37:
                    if (!Checkpoint("final")) return;
                    Finish("PASS", "One real green cloth image passed through entity.setMaterialTexture; the real Hollowmere view used it in the body material slot, behaviour inputs and runtime profile stayed equal, and normal History undo restored the authored binding and rendered material. No tint substitute or direct binding call.");
                    return;
                default: throw new InvalidOperationException("Unknown persisted media phase " + Phase);
            }
        }

        private static void RetainGeneration(MediaImport media)
        {
            Write("generate", new JObject { ["ok"] = media.Ok, ["provider"] = media.Result.Provider, ["providerSha256"] = media.Result.Sha256,
                ["providerBytes"] = media.Result.Bytes?.LongLength, ["opState"] = media.Result.State?.DeepClone(),
                ["refusal"] = media.Result.Refusal == null ? null : StudioJson.ToToken(media.Result.Refusal),
                ["diagnostics"] = new JArray(media.Diagnostics.Select(StudioJson.ToToken)),
                ["assetPath"] = media.AssetPath, ["artifact"] = media.Artifact == null ? null : StudioJson.ToToken(media.Artifact),
                ["journalId"] = media.Report?.Entry.Id, ["journalState"] = media.Report?.State.ToString(), ["maxCostUsd"] = CeilingUsd,
                ["requestId"] = null, ["taskIds"] = new JArray(), ["identityNote"] = "Direct-media app route has no worker request/task. ledger-generated.json supplies actual producer key/jobId/etosRef." });
            if (!media.Ok) throw new Unavailable("Production media refused or import failed: " + (media.Problem?.Code ?? "no_artifact") + ": " + (media.Problem?.Message ?? "no successful journaled import"));
            Require(media.Artifact != null && media.Report != null && media.Result.Bytes != null && media.Result.Sha256 != null, "Successful media result omitted artifact/journal/bytes");
            SessionState.SetString(Prefix + "import", media.Report!.Entry.Id);
            SessionState.SetString(Prefix + "digest", media.Artifact!.Sha256);
            Require(media.Artifact.Sha256 == media.Result.Sha256, "Harness requested no resizing; provider and retained digest must be identical");
            Require(Sha(Context.Runtime.Paths.Absolute(AssetPath)) == Digest, "Generated file differs from verified provider bytes");
            Require(media.Result.State != null && (string?)media.Result.State["state"] == "succeeded", "Provider did not report a terminal succeeded image job");
            File.WriteAllBytes(Path.Combine(Output, "generated.png"), Context.Runtime.Artifacts.Read(Digest));
            CopyJournal(ImportId, "import-applied");
            AssetDatabase.SaveAssets();
        }

        private static void PreflightRobe()
        {
            if (Context.Runtime.Registry.Catalog.FindTool("entity.setMaterialTexture") == null)
                throw new Unavailable("Installed catalog lacks entity.setMaterialTexture; no tint fallback is allowed");
            var renderers = Definition.Prefab != null ? Definition.Prefab.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            var bodies = renderers.Select((renderer, index) => new { renderer, index }).Where(value => value.renderer.name == "Body").ToArray();
            if (bodies.Length != 1 || bodies[0].renderer.sharedMaterials.Length != 1)
                throw new Unavailable("Expected the existing Hollowmere healer Body renderer with one material slot; refuse ambiguous robe target");
            int rendererIndex = bodies[0].index;
            var binding = new MaterialTextureBinding { renderer = rendererIndex, slot = 0, property = "_BaseMap" };
            string problem = MaterialTextureBinding.Validate(Definition, binding);
            if (problem.Length > 0) throw new Unavailable("Real robe material seam unavailable: " + problem);
            SessionState.SetInt(Prefix + "renderer", rendererIndex);
            SelectHealer();
            SessionState.SetString(Prefix + "definitionContent", DefinitionCanonicalizer.ContentStamp(Definition));
            SessionState.SetString(Prefix + "definitionRecipe", DefinitionCanonicalizer.StructuralStamp(Definition));
            Write("authored-before", AuthoredSnapshot());
            Write("material-target", new JObject { ["tool"] = "entity.setMaterialTexture", ["entityPath"] = EntityPath,
                ["entityId"] = Definition.AuthoringId, ["renderer"] = rendererIndex, ["rendererName"] = "Body", ["slot"] = 0,
                ["property"] = "_BaseMap", ["sharedMaterial"] = AssetDatabase.GetAssetPath(bodies[0].renderer.sharedMaterial),
                ["shader"] = bodies[0].renderer.sharedMaterial.shader.name,
                ["implementation"] = "Production view presenter applies per-material MaterialPropertyBlock; shared material asset must remain unchanged." });
        }

        private static void SelectHealer()
        {
            var placed = UnityEngine.Object.FindObjectsByType<AuthoredEntity>(FindObjectsSortMode.None).Where(entity => entity.Definition == Definition).ToArray();
            if (placed.Length != 1) throw new Unavailable("Village must contain exactly one placed Maren entity");
            SessionState.SetString(Prefix + "healerId", placed[0].AuthoringId);
            var target = Context.Runtime.Resolver.BuildRef(placed[0], null, true) ?? throw new Unavailable("No production AuthoringRef for placed healer");
            Context.Selection.Set(new[] { target });
            Write("healer-selection", new JObject { ["selection"] = StudioJson.ToToken(target), ["name"] = placed[0].name,
                ["intent"] = "Give her a green robe. Apply the generated green cloth texture; do not change behaviour, dialogue, position or tint." });
        }

        private static void ApplyRobe()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetPath) ?? throw new InvalidOperationException("Generated robe Texture2D missing");
            Context.Runtime.Index.Rebuild();
            var target = Context.Runtime.Resolver.BuildRef(Definition, null, true) ?? throw new Unavailable("No definition AuthoringRef");
            var source = Context.Runtime.Resolver.BuildRef(texture) ?? throw new Unavailable("No generated texture AuthoringRef");
            var operation = new Operation("op1", "entity.setMaterialTexture", target, new JObject
            {
                ["texture"] = StudioJson.ToToken(source), ["renderer"] = SessionState.GetInt(Prefix + "renderer", -1), ["slot"] = 0, ["property"] = "_BaseMap",
            });
            var change = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId,
                new Intent("Give Maren a green robe using the newly generated cloth texture; preserve behaviour", IntentOrigin.Manual), new[] { operation });
            // This is an explicit manual engine operation, not a modified worker candidate or server-owned request.
            Write("assignment-request", StudioJson.ToToken(change) as JObject ?? new JObject());
            var report = Context.Runtime.Engine.Apply(change);
            SessionState.SetString(Prefix + "assignment", change.Id);
            Write("assignment-report", new JObject { ["ok"] = report.Ok, ["state"] = report.State.ToString(),
                ["diagnostics"] = new JArray(report.Diagnostics.Select(StudioJson.ToToken)), ["outcomes"] = new JArray(report.Outcomes.Select(StudioJson.ToToken)) });
            CopyJournal(change.Id, "assignment-applied");
            Require(report.Ok && report.State == ChangeSetState.Applied, "Engine refused the real texture assignment; inspect assignment-report.json");
            AssetDatabase.SaveAssets();
            VerifyDefinition(true);
            StudioHistoryWindow.Open();
            EditorWindow.GetWindow<StudioHistoryWindow>().View?.Select(change.Id);
        }

        private static JObject AuthoredSnapshot()
        {
            var files = new JObject();
            foreach (string path in new[] { NpcPath, BehaviourPath, DialoguePath, VillagePath }) files[path] = Sha(Context.Runtime.Paths.Absolute(path));
            return new JObject { ["files"] = files, ["entityContent"] = DefinitionCanonicalizer.ContentStamp(Definition),
                ["entityRecipe"] = DefinitionCanonicalizer.StructuralStamp(Definition),
                ["materialBindings"] = new JArray(Definition.MaterialTextures.Select(value => new JObject { ["renderer"] = value.renderer, ["slot"] = value.slot,
                    ["property"] = value.property, ["texture"] = AssetDatabase.GetAssetPath(value.texture) })) };
        }

        private static void VerifyDefinition(bool applied)
        {
            JObject before = Read("authored-before");
            JObject actual = AuthoredSnapshot();
            Write(applied ? "authored-applied" : "authored-undone", actual);
            Require(JToken.DeepEquals(before["files"], actual["files"]), "Behaviour, NPC, dialogue or village bytes changed");
            Require((string?)actual["entityRecipe"] == (string?)before["entityRecipe"], "Presentation changed the entity's structural recipe");
            if (applied)
            {
                Require((string?)actual["entityContent"] != (string?)before["entityContent"], "Texture assignment did not change presentation content");
                Require(Definition.MaterialTextures.Any(binding => binding.renderer == SessionState.GetInt(Prefix + "renderer", -1) && binding.slot == 0
                    && binding.property == "_BaseMap" && AssetDatabase.GetAssetPath(binding.texture) == AssetPath), "Definition does not reference the generated robe texture");
            }
            else
            {
                Require((string?)actual["entityContent"] == (string?)before["entityContent"], "Normal History undo did not restore entity content");
                Require(JToken.DeepEquals(before["materialBindings"], actual["materialBindings"]), "Normal History undo did not restore original material bindings");
            }
        }

        private static void PreparePlay(string label, int next)
        {
            AssetDatabase.SaveAssets();
            var world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(WorldPath) ?? throw new Unavailable("Hollowmere world asset missing");
            var bake = Entry.Bake(world, BakePaths.ConventionFor(WorldPath), false);
            Write("bake-" + label, new JObject { ["succeeded"] = bake.Succeeded, ["detail"] = bake.ToString() });
            if (!bake.Succeeded) throw new Unavailable("Cannot observe real presentation: world bake refused; " + bake);
            SessionState.SetBool(Prefix + "newgame", false);
            SessionState.SetInt(Prefix + "stableOwner", 0);
            SessionState.SetInt(Prefix + "stableFrames", 0);
            SessionState.SetString(Prefix + "playStarted", DateTime.UtcNow.ToString("o"));
            Phase = next;
            AssetDatabase.Refresh();
        }

        private static void EnterPreparedPlay(int next)
        {
            EditorSceneManager.OpenScene(BootPath);
            Phase = next;
            EditorApplication.EnterPlaymode();
        }

        private static bool ObservePlay(string label)
        {
            if ((DateTime.UtcNow - DateTime.Parse(SessionState.GetString(Prefix + "playStarted", ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)).TotalSeconds > 150)
                throw new Unavailable("Real Hollowmere Play did not settle for " + label + "; no preview-only substitute");
            if (!EditorApplication.isPlaying) return false;
            var boot = UnityEngine.Object.FindFirstObjectByType<GameBoot>();
            var rig = UnityEngine.Object.FindFirstObjectByType<HollowmereUiAudio>();
            if (boot?.Narrative == null || rig == null) return false;
            if (!SessionState.GetBool(Prefix + "newgame", false))
            {
                Require(rig.Ui.Dispatcher.Dispatch("newgame").Accepted, "Real newgame UI action refused");
                SessionState.SetBool(Prefix + "newgame", true);
                return false;
            }
            if (rig.Ui.Screen != GameCore.Rules.Gameplay.Ui.UiScreen.Hud || boot.NpcExtension == null || boot.World?.Views == null || !boot.World.Views.IsActive) return false;
            int owner = boot.GetInstanceID();
            int frames = SessionState.GetInt(Prefix + "stableOwner", 0) == owner ? SessionState.GetInt(Prefix + "stableFrames", 0) + 1 : 0;
            SessionState.SetInt(Prefix + "stableOwner", owner);
            SessionState.SetInt(Prefix + "stableFrames", frames);
            if (frames < 20) return false;
            var records = boot.NpcExtension.Records.Where(value => value.AuthoringId == SessionState.GetString(Prefix + "healerId", "")).ToArray();
            Require(records.Length == 1, "Real world does not have exactly one selected healer record");
            NpcRecord npc = records[0];
            if (!boot.World.Views.TryGetView(npc.Target, out GameObject? view) || view == null) return false;
            var renderers = view.GetComponentsInChildren<Renderer>(true);
            int index = SessionState.GetInt(Prefix + "renderer", -1);
            Require(index >= 0 && index < renderers.Length && renderers[index].name == "Body", "Live view body renderer differs from preflight");
            Renderer body = renderers[index];
            var block = new MaterialPropertyBlock();
            body.GetPropertyBlock(block, 0);
            Texture? assigned = block.GetTexture("_BaseMap");
            if (assigned == null) { body.GetPropertyBlock(block); assigned = block.GetTexture("_BaseMap"); }
            Texture? effective = assigned != null ? assigned : body.sharedMaterials[0].GetTexture("_BaseMap");
            string effectivePath = AssetDatabase.GetAssetPath(effective);
            var behaviour = new JObject { ["npcId"] = npc.AuthoringId, ["dialogue"] = npc.DialogueGraph,
                ["profile"] = JToken.FromObject(npc.Profile), ["route"] = JToken.FromObject(npc.Route), ["phases"] = JToken.FromObject(npc.Phases),
                ["dayLengthMilliseconds"] = npc.DayLengthMilliseconds, ["startOffsetMilliseconds"] = npc.StartOffsetMilliseconds };
            var observation = new JObject { ["label"] = label, ["realGameBoot"] = true, ["viewName"] = view.name,
                ["viewFromProductionRegistry"] = true, ["bodyRenderer"] = body.name, ["renderer"] = index, ["slot"] = 0, ["property"] = "_BaseMap",
                ["effectiveTexture"] = effectivePath, ["propertyBlockTexture"] = AssetDatabase.GetAssetPath(assigned),
                ["sharedMaterial"] = AssetDatabase.GetAssetPath(body.sharedMaterials[0]),
                ["sharedMaterialHash"] = Sha(Context.Runtime.Paths.Absolute(AssetDatabase.GetAssetPath(body.sharedMaterials[0]))),
                ["sharedTexture"] = AssetDatabase.GetAssetPath(body.sharedMaterials[0].GetTexture("_BaseMap")), ["behaviour"] = behaviour };
            Write("play-" + label, observation);
            if (label == "applied")
            {
                Require(effectivePath == AssetPath && assigned != null, "Actual production material slot does not use generated robe texture");
                var baseline = Read("play-before");
                Require(JToken.DeepEquals(baseline["behaviour"], behaviour), "Real runtime behaviour profile/route/schedule/dialogue changed");
                Require(JToken.DeepEquals(baseline["sharedMaterialHash"], observation["sharedMaterialHash"]), "Texture assignment mutated shared material asset");
            }
            else if (label == "undone")
            {
                var baseline = Read("play-before");
                Require(JToken.DeepEquals(baseline["behaviour"], behaviour), "Runtime behaviour differs after undo");
                Require(JToken.DeepEquals(baseline["effectiveTexture"], observation["effectiveTexture"]), "Real material slot was not restored after History undo");
                Require(JToken.DeepEquals(baseline["sharedMaterialHash"], observation["sharedMaterialHash"]), "Shared material bytes changed across undo");
            }
            var viewport = StudioViewportWindow.Open();
            viewport.SetMode(ViewportMode.Select);
            viewport.Renderer.ForceFreeCamera = true;
            Bounds bounds = body.bounds;
            bounds.Expand(0.65f);
            viewport.Renderer.Frame(bounds);
            Require(viewport.RenderNow(), "Studio viewport cannot render the actual healer");
            byte[]? png = viewport.EncodeViewportPng();
            Require(png != null && png.Length > 0, "Actual healer viewport capture unavailable");
            File.WriteAllBytes(Path.Combine(Output, "viewport-" + label + ".png"), png!);
            CaptureStudio("studio-" + label);
            return true;
        }

        private static void CaptureImported(string label, bool exists)
        {
            string full = Context.Runtime.Paths.Absolute(AssetPath);
            bool actual = File.Exists(full);
            byte[] retained = Context.Runtime.Artifacts.Read(Digest);
            string retainedHash = ContentStamp.Sha256Hex(retained);
            Write("portrait-" + label, new JObject { ["assetExists"] = actual, ["assetPath"] = AssetPath,
                ["fileSha256"] = actual ? Sha(full) : null, ["retainedSha256"] = retainedHash, ["retainedBytes"] = retained.LongLength,
                ["casPath"] = Context.Runtime.Artifacts.PathOf(Digest), ["journalId"] = ImportId });
            Require(actual == exists && retainedHash == Digest, "Import/undo/redo or retained artifact state is incorrect at " + label);
            if (exists)
            {
                Require(Sha(full) == Digest && AssetDatabase.LoadAssetAtPath<Texture2D>(AssetPath) != null, "Restored portrait is not the verified imported image");
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetPath);
                EditorGUIUtility.PingObject(Selection.activeObject);
                File.WriteAllBytes(Path.Combine(Output, "portrait-" + label + ".png"), File.ReadAllBytes(full));
            }
            StudioHistoryWindow.Open();
            EditorWindow.GetWindow<StudioHistoryWindow>().View?.Select(ImportId);
            CaptureStudio("history-" + label);
        }

        private static void History(string id, bool undo, string label)
        {
            StudioHistoryWindow.Open();
            var panel = EditorWindow.GetWindow<StudioHistoryWindow>().View ?? throw new Unavailable("Normal Studio History panel is unavailable");
            panel.Select(id);
            HistoryResult result = undo ? panel.Undo(id) : panel.Redo(id);
            Write(label, new JObject { ["ok"] = result.Ok, ["changeSetId"] = result.ChangeSetId, ["state"] = result.State?.ToString(),
                ["diagnostics"] = new JArray(result.Diagnostics.Select(StudioJson.ToToken)), ["surface"] = "StudioHistoryWindow.View." + (undo ? "Undo" : "Redo") });
            CopyJournal(id, label + "-journal");
            Require(result.Ok, "Normal History " + (undo ? "undo" : "redo") + " refused " + id);
            AssetDatabase.SaveAssets();
        }

        private static bool Checkpoint(string label)
        {
            string current = SessionState.GetString(Prefix + "checkpoint", "");
            if (current != label)
            {
                SessionState.SetString(Prefix + "checkpoint", label);
                SessionState.SetString(Prefix + "checkpointStarted", DateTime.UtcNow.ToString("o"));
                Write("checkpoint", new JObject { ["row"] = Row, ["label"] = label, ["utc"] = DateTime.UtcNow.ToString("o"),
                    ["generationCalls"] = SessionState.GetInt(Prefix + "generationCalls", 0), ["providerSha256"] = Digest,
                    ["instruction"] = "ledger.py must snapshot this checkpoint before the harness proceeds" });
            }
            string path = Path.Combine(Output, "ledger-" + label + ".json");
            if (!File.Exists(path))
            {
                if ((DateTime.UtcNow - DateTime.Parse(SessionState.GetString(Prefix + "checkpointStarted", ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)).TotalSeconds > 120)
                    throw new Unavailable("Read-only ledger observer did not acknowledge checkpoint " + label + "; usage unchanged is unproved");
                return false;
            }
            JObject receipt = JObject.Parse(File.ReadAllText(path));
            if ((string?)receipt["status"] != "PASS") throw new Unavailable("Ledger checkpoint " + label + ": " + (string?)receipt["reason"]);
            Require((string?)receipt["label"] == label && (string?)receipt["row"] == Row, "Ledger observer returned a mismatched checkpoint");
            return true;
        }

        private static void CaptureStudio(string name)
        {
            string? problem = UnityWindowCapture.CaptureStudio(Path.Combine(Output, name + ".png"), false);
            if (problem != null) throw new Unavailable("Graphical Studio evidence unavailable: " + problem);
        }

        private static void CopyJournal(string id, string name)
        {
            ChangeSet? entry = Context.Runtime.Journal.Read(id);
            Require(entry != null, "Journal entry missing: " + id);
            File.WriteAllText(Path.Combine(Output, name + ".json"), StudioJson.Serialize(entry!));
        }

        private static void Finish(string status, string reason)
        {
            string ledger = Path.Combine(Output, "ledger-generated.json");
            JObject? generationReceipt = File.Exists(ledger) ? JObject.Parse(File.ReadAllText(ledger)) : null;
            Write("result", new JObject { ["row"] = Row, ["status"] = status == "PASS" ? "BLOCKED" : status,
                ["runtimeStatus"] = status, ["reason"] = status == "PASS" ? "Runtime assertions passed; actual portrait/green-robe pixels require Main's visual review via review.py. " + EtosRedaction.Redact(reason) : EtosRedaction.Redact(reason),
                ["phase"] = Phase, ["endedUtc"] = DateTime.UtcNow.ToString("o"), ["mode"] = Mode,
                ["runtimeReason"] = EtosRedaction.Redact(reason),
                ["generationCalls"] = SessionState.GetInt(Prefix + "generationCalls", 0), ["maximumCostUsdPerImage"] = CeilingUsd,
                ["requestId"] = null, ["taskIds"] = new JArray(), ["mediaIdentity"] = generationReceipt?["identity"]?.DeepClone(),
                ["importChangeSetId"] = ImportId, ["assignmentChangeSetId"] = SessionState.GetString(Prefix + "assignment", ""),
                ["identityNote"] = "No worker request/task exists for this app-owned direct-media route. Actual companion effect key and node jobId come only from the ledger; local journal IDs are not request authority.",
                ["accountingScope"] = "Binding companion tariff/charge ledger and original node-reported generation usage; no upstream provider invoice assertion.",
                ["visualEvidence"] = Mode == "portrait" ? "generated.png; portrait-applied.png; portrait-redone.png; history-*.png" : "viewport-before.png; viewport-applied.png; viewport-undone.png; studio-*.png",
                ["cleanup"] = status == "PASS" ? "Normal History undo removed imported file; CAS and receipts retained." : "Do not repair candidate or repeat generation. Inspect saved journal IDs and use normal History recovery/undo." });
            SessionState.SetBool(Prefix + "active", false);
            EditorApplication.Exit(status == "PASS" ? 0 : 1);
        }

        private static string Sha(string path) => File.Exists(path) ? ContentStamp.Sha256Hex(File.ReadAllBytes(path)) : throw new Unavailable("Required evidence source file missing: " + path);
        private static JObject Read(string name) => JObject.Parse(File.ReadAllText(Path.Combine(Output, name + ".json")));
        private static void Write(string name, JObject value) => File.WriteAllText(Path.Combine(Output, name + ".json"), value.ToString());
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private sealed class Unavailable : Exception { public Unavailable(string message) : base(message) { } }
    }
}
