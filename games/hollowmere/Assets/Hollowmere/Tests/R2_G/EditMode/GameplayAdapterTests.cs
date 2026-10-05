#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Gameplay.Audio;
using GameCore.Gameplay.Audio.Editor;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Dialogue;
using GameCore.Gameplay.Dialogue.Editor;
using GameCore.Gameplay.World.Editor;
using GameCore.Studio.Edit;
using GameCore.Rules.Gameplay.Dialogue;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using Object = UnityEngine.Object;
using Diagnostic = GameCore.Studio.Model.Diagnostic;
using OperationResult = GameCore.Studio.Edit.OperationResult;

namespace Hollowmere.R2_G.EditMode.Tests
{
    public sealed class MediaSessionFixture : ScriptableObject, IMediaGenerationGatewayProvider
    {
        public IMediaGenerationGateway? MediaGateway { get; set; }
    }

    public sealed class CountingMedia : ISoundEffectGenerationGateway
    {
        public int Calls { get; private set; }
        public MediaGenerationResult RequestVoiceLine(VoiceGenerationRequest request)
        {
            Calls++;
            return new MediaGenerationResult(MediaGenerationStatus.Requested, "voice", request.Text);
        }
        public MediaGenerationResult RequestSoundEffect(string clipId, string description, int durationMs)
        {
            Calls++;
            return new MediaGenerationResult(MediaGenerationStatus.Requested, "sfx", description);
        }
    }

    public sealed class GameplayAdapterTests
    {
        private StudioRuntime runtime = null!;
        private string state = string.Empty;
        private LiveGateway live = null!;

        [SetUp]
        public void SetUp()
        {
            state = Path.Combine(Path.GetTempPath(), "r2-g-" + Guid.NewGuid().ToString("N"));
            live = new LiveGateway();
            runtime = StudioRuntime.Create(new StudioRuntimeOptions
            {
                Paths = new StudioPaths(Directory.GetParent(Application.dataPath)!.FullName, state),
                TypeSource = () => Array.Empty<Type>(), ToolMethodSource = () => Array.Empty<MethodInfo>(),
                LoadIndexCache = false, Live = live, Engine = new EngineOptions { PlayModeProbe = () => true },
            });
        }

        [TearDown]
        public void TearDown()
        {
            runtime.Dispose();
            if (Directory.Exists(state)) Directory.Delete(state, true);
        }

        [Test]
        public void R2_41_ToolsUseRegisteredGatewayAndObserveReplacementAndUnregister()
        {
            var session = ScriptableObject.CreateInstance<MediaSessionFixture>();
            var graph = ScriptableObject.CreateInstance<DialogueGraphDefinition>();
            var bank = ScriptableObject.CreateInstance<AudioBankDefinition>();
            try
            {
                graph.AddNode(new DialogueNodeEntry { kind = DialogueNodeKind.Line, text = "Hello" });
                var first = new CountingMedia();
                session.MediaGateway = first;
                Assert.That(DialogueTools.GenerateVoice(graph, 0).Status, Is.EqualTo(MediaGenerationStatus.Requested));
                Assert.That(AudioTools.GenerateVoice(bank, "voice", "Hello").Status, Is.EqualTo(MediaGenerationStatus.Requested));
                Assert.That(AudioTools.GenerateSfx(bank, "sound", "A bell", 500).Status, Is.EqualTo(MediaGenerationStatus.Requested));
                Assert.That(first.Calls, Is.EqualTo(3));
                var second = new CountingMedia();
                session.MediaGateway = second;
                AudioTools.GenerateVoice(bank, "voice", "Hello");
                Assert.That(second.Calls, Is.EqualTo(1));
                Assert.That(first.Calls, Is.EqualTo(3));
                session.MediaGateway = null;
                Assert.That(DialogueTools.GenerateVoice(graph, 0).Status, Is.EqualTo(MediaGenerationStatus.NotConfigured));
            }
            finally { Object.DestroyImmediate(session); Object.DestroyImmediate(graph); Object.DestroyImmediate(bank); }
        }

        [Test]
        public void R2_41_RegisteredToolIdsReachGatewayThroughChangeSetEngine()
        {
            string folder = "Assets/Hollowmere/Tests/R2_G/MediaScratch";
            AssetDatabase.CreateFolder("Assets/Hollowmere/Tests/R2_G", "MediaScratch");
            var session = ScriptableObject.CreateInstance<MediaSessionFixture>();
            var gateway = new CountingMedia();
            session.MediaGateway = gateway;
            var graph = ScriptableObject.CreateInstance<DialogueGraphDefinition>();
            var bank = ScriptableObject.CreateInstance<AudioBankDefinition>();
            graph.EnsureAuthoringId(); bank.EnsureAuthoringId();
            graph.AddNode(new DialogueNodeEntry { kind = DialogueNodeKind.Line, text = "Hello" });
            AssetDatabase.CreateAsset(graph, folder + "/Graph.asset");
            AssetDatabase.CreateAsset(bank, folder + "/Bank.asset");
            try
            {
                using (StudioRuntime tools = StudioRuntime.Create(new StudioRuntimeOptions
                {
                    Paths = new StudioPaths(Directory.GetParent(Application.dataPath)!.FullName, Path.Combine(state, "media")),
                    TypeSource = () => new[] { typeof(DialogueGraphDefinition), typeof(AudioBankDefinition) },
                    ToolMethodSource = () => typeof(DialogueTools).GetMethods().Concat(typeof(AudioTools).GetMethods())
                        .Where(method => GameCore.Studio.Authoring.AuthoringMetadata.Operation(method) != null),
                    SearchFolders = new[] { folder }, LoadIndexCache = false,
                }))
                {
                    foreach (string id in new[] { "dialogue.generateVoice", "audio.generateVoice", "audio.generateSfx" })
                    {
                        bool dialogue = id.StartsWith("dialogue.", StringComparison.Ordinal);
                        JObject args = dialogue ? new JObject { ["node"] = 0 } : id.EndsWith("Sfx", StringComparison.Ordinal)
                            ? new JObject { ["clipId"] = "bell", ["description"] = "A bell" }
                            : new JObject { ["clipId"] = "voice", ["text"] = "Hello" };
                        var op = new Operation("media", id, tools.Resolver.BuildRef(dialogue ? (Object)graph : bank), args, null, Preconditions.None);
                        ChangeSet change = StudioRuntime.Single("media", IntentOrigin.Manual, op)
                            .WithRequirements(Requirements.FromOperations(new[] { tools.Registry.Find(id)!.Entry.RuntimeApply }));
                        ApplyReport report = tools.Engine.Apply(change);
                        Assert.That(report.State, Is.EqualTo(ChangeSetState.Applied), id + ": " + string.Join("; ", report.Diagnostics.Select(d => d.Message)));
                    }
                    Assert.That(gateway.Calls, Is.EqualTo(3));
                }
            }
            finally { Object.DestroyImmediate(session); AssetDatabase.DeleteAsset(folder); }
        }

        [Test]
        public void R2_41_UnregisteredConcreteTypesAreNotConstructed()
        {
            Assert.That(MediaGateways.Resolve(), Is.TypeOf<NotConfiguredMediaGateway>());
        }

        [Test]
        public void R2_41_AmbiguousRegistrationsFailClosed()
        {
            var a = ScriptableObject.CreateInstance<MediaSessionFixture>();
            var b = ScriptableObject.CreateInstance<MediaSessionFixture>();
            try
            {
                a.MediaGateway = new CountingMedia(); b.MediaGateway = new CountingMedia();
                Assert.That(MediaGenerationLookup.Resolve(new object[] { a, b }), Is.TypeOf<NotConfiguredMediaGateway>());
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }

        [Test]
        public void R2_34_WorldToolReferencesAreBindableArguments()
        {
            foreach (MethodInfo method in typeof(WorldTools).GetMethods().Where(m =>
                m.Name == "ConnectRegions" || m.Name == "AddPortalEnd" || m.Name == "SetRegionSpawnPoint"))
            {
                ToolEntry entry = GameCore.Studio.Authoring.AuthoringMetadata.BuildToolEntry(method);
                Assert.That(entry.TargetRequired, Is.True);
                foreach (ParameterInfo arg in method.GetParameters().Skip(1))
                    Assert.That(entry.Args.Any(spec => spec.Name == arg.Name), Is.True, arg.Name);
            }
        }

        [Test]
        public void R2_B_BindingReplacesServicesAndRejectsUnverifiedSmoke()
        {
            bool ready = false;
            int smokeCalls = 0;
            StudioAdmissionServices.BindAdmission(runtime, () => null, () => ready, _ => { smokeCalls++; return true; });
            AdmissionOptions options = StageAdmission.Of(runtime).Options;
            Assert.That(options.SessionReady!(), Is.False);
            ready = true;
            Assert.That(options.SessionReady!(), Is.True);
            Assert.That(options.Capture!.TryCapture("admit-test", out _), Is.False);
            StageVerdict verdict = StageVerdict.Parse(Encoding.UTF8.GetBytes("{\"schema\":\"gamecore.studio.stage-verdict/1\",\"changeSetId\":\"cs_test\",\"pass\":true}"), out string? problem)!;
            Assert.That(verdict, Is.Not.Null, problem);
            Assert.That(options.SmokeTest!(verdict), Is.False);
            Assert.That(StudioAdmissionServices.RunSmokeTest(runtime, verdict, (_, __, ___) => { smokeCalls++; return true; }), Is.False);
            Assert.That(smokeCalls, Is.Zero);
            StudioAdmissionServices.BindAdmission(runtime, () => null, () => false, _ => false);
            Assert.That(options.SessionReady!(), Is.False);
            Assert.That(runtime.Services.LiveTranslators.OfType<WorldLiveOpTranslator>().Count(), Is.EqualTo(1));
        }

        [Test]
        public void R2_06_RuntimeMetadataAndReloadRegistration()
        {
            WorldLiveOpTranslator first = WorldLiveOpTranslator.Register(runtime);
            Assert.That(WorldLiveOpTranslator.Register(runtime), Is.SameAs(first));
            foreach (string id in new[] { "world.travel", "dialogue.start", "entity.spawn", "entity.despawn", "npc.goTo" })
            {
                ToolEntry entry = runtime.Registry.Find(id)!.Entry;
                Assert.That(entry.RuntimeOnly, Is.True);
                Assert.That(entry.RuntimeApply, Is.EqualTo(RuntimeApply.Live));
                Assert.That(entry.Args.Any(arg => arg.Name == "expectedRevision" && arg.Required), Is.True);
            }
        }

        [Test]
        public void R2_06_CatalogMechanismIsNonUndoableAndNeverCallsAuthoredTool()
        {
            WorldLiveOpTranslator.Register(runtime).Bridge = new Bridge();
            var tool = new MechanismTool("plate.press", true);
            runtime.Registry.Register(tool);
            ApplyReport report = runtime.Engine.Apply(Change("plate.press", 7));
            Assert.That(report.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", report.Diagnostics));
            Assert.That(live.Expected, Is.EqualTo(7));
            Assert.That(tool.Calls, Is.Zero);
            Assert.That(report.Outcome("a")!.Undo, Is.Null);
            Assert.That(runtime.History.Undo(report.Entry.Id).Ok, Is.False);
        }

        [Test]
        public void R2_06_MissingBridgeAndStaleRevisionRefuseWithoutSubmit()
        {
            WorldLiveOpTranslator translator = WorldLiveOpTranslator.Register(runtime);
            runtime.Registry.Register(new MechanismTool("plate.press", true));
            Assert.That(runtime.Engine.Apply(Change("plate.press", 7)).State, Is.EqualTo(ChangeSetState.Failed));
            translator.Bridge = new Bridge();
            Assert.That(runtime.Engine.Apply(Change("plate.press", 6)).State, Is.EqualTo(ChangeSetState.Failed));
            Assert.That(live.Submits, Is.Zero);
        }

        [Test]
        public void R2_06_MixedAtomicBatchRefusesBeforeEffects()
        {
            WorldLiveOpTranslator.Register(runtime).Bridge = new Bridge();
            var authored = new MechanismTool("fixture.asset", false);
            runtime.Registry.Register(authored);
            runtime.Registry.Register(new MechanismTool("plate.press", true));
            var change = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent("mixed", IntentOrigin.Manual),
                new[] { Op("a", "plate.press", 7), Op("b", "fixture.asset", 7) }, policy: ApplyPolicy.AllOrNothing);
            Assert.That(runtime.Engine.Apply(change).State, Is.EqualTo(ChangeSetState.Failed));
            Assert.That(live.Submits, Is.Zero); Assert.That(authored.Calls, Is.Zero);
        }

        private static Operation Op(string id, string tool, int revision) => new Operation(id, tool, null,
            new JObject { ["expectedRevision"] = revision }, null, Preconditions.None);
        private static ChangeSet Change(string tool, int revision) => StudioRuntime.Single("runtime", IntentOrigin.Manual, Op("a", tool, revision));

        private sealed class MechanismTool : IStudioTool
        {
            public MechanismTool(string id, bool runtimeOnly) => Entry = new ToolEntry(id, ToolTier.Configure, RuntimeApply.Live,
                false, new[] { new ArgSpec("expectedRevision", "int", true) }, runtimeOnly: runtimeOnly);
            public ToolEntry Entry { get; }
            public bool Internal => false;
            public bool ReadOnly => false;
            public int Calls { get; private set; }
            public ToolStageResult Stage(EditContext context) => new ToolStageResult();
            public OperationResult Apply(EditContext context) { Calls++; return OperationResult.Applied(); }
        }
        private sealed class Bridge : IWorldLiveActionBridge
        {
            public CompositionEditPayload? Translate(EditContext context, ulong expectedRevision, out Diagnostic? problem)
            {
                problem = null;
                return new CompositionEditPayload(CompositionEditSubject.ScopeCreate, default, default, false,
                    null, null, null, null, default, default, default, default, null, 0, null, PropagationMode.Conservative);
            }
        }
        private sealed class LiveGateway : ILiveWorldGateway
        {
            public bool IsAvailable => true;
            public ulong CommittedRevision => 7;
            public ulong Expected { get; private set; }
            public int Submits { get; private set; }
            public LiveSubmitResult Submit(CompositionEditPayload payload, ulong expectedRevision)
            {
                Submits++; Expected = expectedRevision;
                return new LiveSubmitResult(LiveSubmitStatus.Executed, "w:test/i:test/s:1", expectedRevision, 8, null);
            }
        }
    }
}
