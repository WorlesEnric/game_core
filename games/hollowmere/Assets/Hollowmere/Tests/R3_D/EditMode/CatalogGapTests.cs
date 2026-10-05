#nullable enable
using System;
using System.IO;
using System.Linq;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Dialogue;
using GameCore.Gameplay.Dialogue.Editor;
using GameCore.Gameplay.Entities.Editor;
using GameCore.Gameplay.Ui;
using GameCore.Rules.Gameplay.Dialogue;
using NUnit.Framework;
using UnityEngine;

namespace Hollowmere.R3_D
{
    public sealed class CatalogGapTests
    {
        private static string Evidence(string relative) => File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../../artifacts/studio/workflows/P3.2/runs", relative)));

        [Test]
        public void D9_RetainedRobeRequestHasTextureBindingTool()
        {
            Assert.That(Evidence("robe2-20261005T070105Z/robe/candidate.json"), Does.Contain("Give her a green robe"));
            Assert.That(typeof(EntityTools).GetMethods().Any(m => m.GetCustomAttributes(typeof(AuthorOperationAttribute), false)
                .Cast<AuthorOperationAttribute>().Any(a => a.ToolId == "entity.setMaterialTexture")), Is.True);
        }

        [Test]
        public void D10a_RetainedShrineFactHasTypedConditionTool()
        {
            Assert.That(Evidence("narrative-20261005T072920Z/odd-line/outcome.json"), Does.Contain("shrine_lit"));
            Assert.That(typeof(DialogueTools).GetMethod("SetFactCondition"), Is.Not.Null);
        }

        [Test]
        public void D10b_RetainedHudRequestHasStageTitleSource()
        {
            Assert.That(Evidence("narrative-20261005T072920Z/hud/outcome.json"), Does.Contain("current stage title"));
            Assert.That(new HudViewModel().PropertyNames, Does.Contain("QuestStageTitle"));
        }

        [Test]
        public void D7_PairedProviderIsFoundByPublicVoiceTool()
        {
            Assert.That(Evidence("robe2-20261005T070105Z/voice-line/dialogue-generateVoice.json"), Does.Contain("no media generation gateway is configured"));
            var provider = ScriptableObject.CreateInstance<FakeMediaProvider>();
            var graph = ScriptableObject.CreateInstance<DialogueGraphDefinition>();
            try
            {
                graph.Configure("Maren", string.Empty, 0);
                graph.AddNode(new DialogueNodeEntry { kind = DialogueNodeKind.Branch });
                DialogueTools.AddLine(graph, "The shrine is lit.");
                var gateway = new FakeMediaGateway();
                provider.Gateway = gateway;
                Assert.Throws<ArgumentException>(() => DialogueTools.GenerateVoice(graph, 0, "fake-voice"));
                var result = DialogueTools.GenerateVoice(graph, 1, "fake-voice");
                Assert.That(result.RequestId, Is.EqualTo("fake-request"));
                Assert.That(gateway.Requests, Is.EqualTo(1));
                Assert.That(gateway.Last!.Node, Is.EqualTo(1));
                Assert.That(gateway.Last.Speaker, Is.EqualTo("Maren"));
                Assert.That(gateway.Last.Voice, Is.EqualTo("fake-voice"));
                provider.Gateway = null;
                Assert.That(DialogueTools.GenerateVoice(graph, 1).Status, Is.EqualTo(MediaGenerationStatus.NotConfigured));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(provider);
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }
    }

    public sealed class FakeMediaProvider : ScriptableObject, IMediaGenerationGatewayProvider
    {
        public IMediaGenerationGateway? Gateway { get; set; }
        public IMediaGenerationGateway? MediaGateway => Gateway;
    }

    public sealed class FakeMediaGateway : IMediaGenerationGateway
    {
        public int Requests { get; private set; }
        public VoiceGenerationRequest? Last { get; private set; }
        public MediaGenerationResult RequestVoiceLine(VoiceGenerationRequest request)
        {
            Requests++;
            Last = request;
            return new MediaGenerationResult(MediaGenerationStatus.Requested, "fake-request", "fake companion");
        }
    }
}
