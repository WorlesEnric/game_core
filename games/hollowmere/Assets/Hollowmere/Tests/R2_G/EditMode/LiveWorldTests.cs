#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using GameCore.Gameplay.World.Editor;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Unity.App;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hollowmere.R2_G.EditMode.Tests
{
    public sealed class LiveWorldTests
    {
        [UnityTest]
        public IEnumerator R2_06_HollowmerePlayWorldRefusesUnavailableCommandBridgeAndMixedAtomicBatch()
        {
            yield return new EnterPlayMode();
            AsyncOperation load = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Hollowmere/Boot/Boot.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!load.isDone) yield return null;
            for (int i = 0; i < 300 && GameApplication.Current == null; i++) yield return null;
            Assert.That(GameApplication.Current, Is.Not.Null);
            string state = Path.Combine(Path.GetTempPath(), "r2-g-play-" + Guid.NewGuid().ToString("N"));
            using (StudioRuntime runtime = StudioRuntime.Create(new StudioRuntimeOptions
            {
                Paths = new StudioPaths(Directory.GetParent(Application.dataPath)!.FullName, state),
                TypeSource = () => Array.Empty<Type>(), ToolMethodSource = () => Array.Empty<MethodInfo>(), LoadIndexCache = false,
            }))
            {
                Assert.That(runtime.Engine.IsPlayMode, Is.True);
                Assert.That(runtime.Live.IsAvailable, Is.True);
                WorldLiveOpTranslator.Register(runtime);
                var action = new ProbeTool("plate.press", true);
                var asset = new ProbeTool("fixture.asset", false);
                runtime.Registry.Register(action); runtime.Registry.Register(asset);
                ulong revision = runtime.Live.CommittedRevision;
                var op = new Operation("a", "plate.press", null, new JObject { ["expectedRevision"] = revision }, null, Preconditions.None);
                Assert.That(runtime.Engine.Apply(StudioRuntime.Single("live", IntentOrigin.Manual, op)).State, Is.EqualTo(ChangeSetState.Failed));
                var mixed = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent("mixed", IntentOrigin.Manual),
                    new[] { op, new Operation("b", "fixture.asset", null, new JObject(), null, Preconditions.None) }, policy: ApplyPolicy.AllOrNothing);
                Assert.That(runtime.Engine.Apply(mixed).State, Is.EqualTo(ChangeSetState.Rejected));
                Assert.That(action.Calls + asset.Calls, Is.Zero);
                Assert.That(runtime.Live.CommittedRevision, Is.EqualTo(revision));
            }
            Directory.Delete(state, true);
            yield return new ExitPlayMode();
        }

        private sealed class ProbeTool : IStudioTool
        {
            public ProbeTool(string id, bool runtimeOnly) => Entry = new ToolEntry(id, ToolTier.Configure, RuntimeApply.Live,
                false, runtimeOnly ? new[] { new ArgSpec("expectedRevision", "int", true) } : Array.Empty<ArgSpec>(), runtimeOnly: runtimeOnly);
            public ToolEntry Entry { get; }
            public bool Internal => false;
            public bool ReadOnly => false;
            public int Calls { get; private set; }
            public ToolStageResult Stage(EditContext context) => new ToolStageResult();
            public OperationResult Apply(EditContext context) { Calls++; return OperationResult.Applied(); }
        }
    }
}
