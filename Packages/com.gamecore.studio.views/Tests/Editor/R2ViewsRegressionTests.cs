#nullable enable
using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.TestTools;
using System.IO;
using System.Reflection;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using NUnit.Framework;
using UnityEngine;

namespace GameCore.Studio.Views.Tests
{
    public sealed class R2ViewsRegressionTests
    {
        public enum ReceiptKind { Accepted, Committed, Refused }
        public sealed class Result
        {
            public ReceiptKind Kind { get; set; }
            public string Diagnostic => "test refusal witness";
            public override string ToString() => "unrelated representation";
        }
        public sealed class Receipt
        {
            public bool Admitted => Result.Kind != ReceiptKind.Refused;
            public Result Result { get; } = new Result();
        }
        public sealed class Commands
        {
            public Receipt Receipt { get; } = new Receipt();
            public Receipt Travel(int focus, string region, string portal) => Receipt;
            public Receipt Travel(string focus, int region, string portal) => throw new InvalidOperationException("wrong overload");
        }
        public sealed class WrongCommands { public Receipt Travel(string focus, string region, string portal) => new Receipt(); }
        public sealed class ThrowCommands { public Receipt Travel(int focus, string region, string portal) => throw new ArgumentException("bad argument"); }
        public sealed class GameplayWorld
        {
            public object Commands { get; set; } = new Commands();
            public int Focus => 7;
        }
        public sealed class Conversation
        {
            public bool Started { get; set; }
            public string Detail => "graph refused";
        }
        public sealed class Starter
        {
            public Conversation Result { get; } = new Conversation();
            public Conversation TryStart(string speaker, string graph) => Result;
            public Conversation TryStart(int speaker, int graph) => throw new InvalidOperationException("wrong overload");
        }
        public sealed class NarrativeWorld
        {
            public GameplayWorld World { get; } = new GameplayWorld();
            public Starter Conversations { get; } = new Starter();
        }

        [Test]
        public void R2_30_StandaloneMinimumPersistsAfterReopenAndDockingUsesPanelMinimum()
        {
            for (int i = 0; i < 2; i++)
            {
                RelationshipsWindow window = ScriptableObject.CreateInstance<RelationshipsWindow>();
                try { Assert.That(window.minSize, Is.EqualTo(new Vector2(1280f, 720f))); }
                finally { UnityEngine.Object.DestroyImmediate(window); }
            }
            Assert.That(StudioViewWindow.MinimumFor(true), Is.EqualTo(new Vector2(640f, 360f)));
        }

        [TestCase(ReceiptKind.Accepted, GameplayCommandStatus.Submitted)]
        [TestCase(ReceiptKind.Committed, GameplayCommandStatus.Submitted)]
        [TestCase(ReceiptKind.Refused, GameplayCommandStatus.Refused)]
        public void R2_32_TravelUsesTypedReceiptAndExactOverload(ReceiptKind kind, GameplayCommandStatus expected)
        {
            GameplayWorld world = new GameplayWorld();
            ((Commands)world.Commands).Receipt.Result.Kind = kind;
            ReflectionGameplayBridge bridge = new ReflectionGameplayBridge(() => world, () => true);
            GameplayCommandResult result = bridge.Travel("region");
            Assert.That(result.Status, Is.EqualTo(expected));
            Assert.That(result.Detail, Does.Contain(kind.ToString()));
            if (kind == ReceiptKind.Refused) Assert.That(result.Detail, Does.Contain("witness"));
        }

        [Test]
        public void R2_32_MismatchedAndThrowingMembersRefuseWithDiagnostic()
        {
            GameplayWorld world = new GameplayWorld { Commands = new WrongCommands() };
            ReflectionGameplayBridge bridge = new ReflectionGameplayBridge(() => world, () => true);
            GameplayCommandResult result = bridge.Travel("region");
            Assert.That(result.Status, Is.EqualTo(GameplayCommandStatus.Refused));
            Assert.That(result.Detail, Does.Contain("BridgeContractMismatch"));
            world.Commands = new ThrowCommands();
            result = bridge.Travel("region");
            Assert.That(result.Status, Is.EqualTo(GameplayCommandStatus.Refused));
            Assert.That(result.Detail, Does.Contain("BridgeInvocationFailed").And.Contain("bad argument"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void R2_32_DialogueUsesStartedAndRebindsRestoredWorld(bool started)
        {
            NarrativeWorld world = new NarrativeWorld();
            ReflectionGameplayBridge bridge = new ReflectionGameplayBridge(() => world, () => true);
            Assert.That(bridge.StartDialogue("graph", "speaker").Status, Is.EqualTo(GameplayCommandStatus.Refused));
            world = new NarrativeWorld();
            world.Conversations.Result.Started = started;
            Assert.That(bridge.StartDialogue("graph", "speaker").Ok, Is.EqualTo(started));
            world = null!;
            Assert.That(bridge.Describe, Does.Not.Contain("registered"));
        }

        [Test]
        public void R2_35_CheckerResultRetainsRulesOutsideLocalGraph()
        {
            PackageMetadataCheck result = PackageMetadataCheck.Parse("{\"problems\":[\"unknown assembly\",\"engine pin mismatch\",\"lock map mismatch\"]}", 1);
            Assert.That(result.Assessed, Is.True);
            Assert.That(result.Passed, Is.False);
            Assert.That(result.Problems.Count, Is.EqualTo(3));
            Assert.That(PackageMetadataCheck.Parse("{\"problems\":[]}", 2).Assessed, Is.False);
            Assert.That(PackageMetadataCheck.Parse("{}", 0).Assessed, Is.False);
            Assert.That(PackageMetadataCheck.Parse("{\"problems\":[]}", 0).Passed, Is.True);
        }

        [UnityTest]
        public IEnumerator R2_35_ActualCheckerRunsAndReturnsItsJsonVerdict()
        {
            var task = PackageMetadataCheck.RunAsync(Directory.GetParent(Application.dataPath)!.FullName);
            while (!task.IsCompleted) yield return null;
            Assert.That(task.Result.Assessed, Is.True, task.Result.Detail);
            Assert.That(task.Result.Passed, Is.True, string.Join("; ", task.Result.Problems));
        }

        [Test]
        public void R2_36_ContextDoesNotInstallContributorOrRebuildIndex()
        {
            string state = Path.Combine(Path.GetTempPath(), "r2e-" + Guid.NewGuid().ToString("N"));
            try
            {
                using StudioRuntime runtime = StudioRuntime.Create(new StudioRuntimeOptions
                {
                    Paths = new StudioPaths(Directory.GetParent(Application.dataPath)!.FullName, state, "r2e"),
                    TypeSource = () => Array.Empty<Type>(), ToolMethodSource = () => Array.Empty<MethodInfo>(),
                    SearchFolders = Array.Empty<string>(), LoadIndexCache = false, Log = new MemoryStudioLog(),
                });
                runtime.Index.Rebuild();
                long revision = runtime.Index.Revision;
                int count = runtime.Index.Contributors.Count;
                using StudioViewContext context = new StudioViewContext(runtime, new ListSelectionBridge(), new ReflectionGameplayBridge(null, () => false), false);
                Assert.That(runtime.Index.Revision, Is.EqualTo(revision));
                Assert.That(runtime.Index.Contributors.Count, Is.EqualTo(count));
                Assert.That(context.Contributor, Is.SameAs(runtime.References));
            }
            finally { if (Directory.Exists(state)) Directory.Delete(state, true); }
        }

        [Test]
        public void R2_36_NodeRefsLabelClassifiedEdgesWithoutViewsContributor()
        {
            SyntheticIndex index = new SyntheticIndex();
            AuthoringRef target = index.Node("target", "inventory.item");
            AuthoringRef source = index.Node("source", "quest.quest", refs: new List<IndexRef> { new IndexRef("rewards[0].item", target) });
            index.Edge(source, target, EdgeKind.Triggers);
            Assert.That(IndexGraph.Build(index.Build()).Links[0].Label, Is.EqualTo("rewards[0].item"));
        }

        [Test]
        public void SelectionSoftBindingPropagatesFocusChangesAndDetaches()
        {
            ListSelectionBridge selection = new ListSelectionBridge();
            AuthoringRef? focused = null;
            using StudioSelectionAdapter adapter = new StudioSelectionAdapter(() => selection.Current,
                selection.Select, target => focused = target,
                changed => selection.SelectionChanged += changed, changed => selection.SelectionChanged -= changed);
            int notifications = 0;
            adapter.SelectionChanged += () => notifications++;
            AuthoringRef targetRef = SyntheticIndex.Ref("target");
            adapter.Focus(targetRef);
            Assert.That(adapter.Current[0], Is.SameAs(targetRef));
            Assert.That(focused, Is.SameAs(targetRef));
            Assert.That(notifications, Is.EqualTo(1));
            adapter.Dispose();
            selection.Select(Array.Empty<AuthoringRef>());
            Assert.That(notifications, Is.EqualTo(1));
        }
    }
}
