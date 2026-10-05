// GameCore.Studio.UI.Tests - the context panel: tools filtered to the selection's type, a Direct tool runs as one
// journaled single-operation change set through the engine, and an Agent-tier tool only prefills the prompt.
#nullable enable
using System.Collections.Generic;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Fixtures;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GameCore.Studio.UI.Tests
{
    public sealed class ContextPanelTests
    {
        private UiTestBed _bed = null!;

        [SetUp]
        public void SetUp() => _bed = new UiTestBed();

        [TearDown]
        public void TearDown() => _bed.Dispose();

        [Test]
        public void Tools_AreFilteredAndDirectToolsJournalOneOperation()
        {
            FixtureNpcDefinition npc = _bed.CreateNpc("Ferryman", "Ahoy");
            _bed.Runtime.Index.Rebuild();
            AuthoringRef target = _bed.Ref(npc);

            IReadOnlyList<IStudioTool> tools = ContextTools.For(_bed.Runtime, target);
            IStudioTool? setGreeting = null;
            foreach (IStudioTool tool in tools)
            {
                Assert.That(tool.Entry.TargetType == null || tool.Entry.TargetType == "fixture.npc" || !tool.Entry.TargetRequired, Is.True, tool.Entry.Id + " does not apply to fixture.npc");
                if (tool.Entry.Id == "fixture.setGreeting")
                {
                    setGreeting = tool;
                }
            }

            Assert.That(setGreeting, Is.Not.Null, "the fixture tool targeting fixture.npc is offered");
            Assert.That(ContextTools.KindOf(setGreeting!), Is.EqualTo(ToolButtonKind.Direct));

            _bed.Context.Selection.Set(new[] { target });
            ContextPanelView panel = new ContextPanelView(_bed.Context);
            panel.Rebuild();
            Assert.That(panel.ShownTools, Has.Some.Matches<IStudioTool>(tool => tool.Entry.Id == "fixture.setGreeting"));

            ApplyReport report = panel.RunDirect(setGreeting!, target, new JObject { ["greeting"] = "Ahoy there" }, "Ferryman");
            Assert.That(report.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", report.Diagnostics));
            Assert.That(npc.greeting, Is.EqualTo("Ahoy there"));
            ChangeSet entry = _bed.Runtime.Journal.Read(report.Entry.Id)!;
            Assert.That(entry.Operations.Count, Is.EqualTo(1));
            Assert.That(entry.Operations[0].Tool, Is.EqualTo("fixture.setGreeting"));
            Assert.That(entry.Intent.Origin, Is.EqualTo(IntentOrigin.Manual));
        }

        [Test]
        public void AgentTierTools_PrefillThePromptInsteadOfRunning()
        {
            string? prefilled = null;
            _bed.Context.PromptPrefillRequested += text => prefilled = text;
            _bed.Context.RequestPrompt("Generate a portrait for the ferryman");
            Assert.That(prefilled, Is.EqualTo("Generate a portrait for the ferryman"));
            Assert.That(_bed.Gateway.Submitted, Is.Empty, "prefill never submits");
        }
    }
}
