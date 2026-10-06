#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Model;
using GameCore.Studio.UI;
using Hollowmere.P2_1.Evidence;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace P42c.Live
{
    public sealed class GuideTests
    {
        [UnityTest, Order(0)]
        public IEnumerator R2_38_CreatorGuide_TextSend()
        {
            return Send("text", "Move this well one metre to the east.", false);
        }

        [UnityTest, Order(1), Timeout(900000)]
        public IEnumerator R2_38_CreatorGuide_ImageSend()
        {
            return Send("image", "Generate exactly one small inventory icon of an old brass marsh lantern with a warm flame, hand-painted game UI style, centred, plain dark background. Maximum image cost USD 0.25. Import the image and bind it to this item's icon. Do not generate additional variants.", true);
        }

        private static IEnumerator Send(string kind, string text, bool image)
        {
            string output = Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE")!;
            EtosStudioSession.Start();
            EditorSceneManager.OpenScene("Assets/Hollowmere/Boot/Boot.unity");
            Assert.That(EditorApplication.ExecuteMenuItem("GameCore/Studio/Open Studio"), Is.True);
            EditorSceneManager.OpenScene("Assets/Hollowmere/Regions/ThornwickVillage.unity");
            var context = StudioUiSession.Context;
            DateTime end = DateTime.UtcNow.AddSeconds(60);
            while (!context.Gateway.Status.AgentReady && DateTime.UtcNow < end) yield return null;
            Assert.That(context.Gateway.Status.AgentReady, Is.True);
            UnityEngine.Object target = image
                ? AssetDatabase.LoadMainAssetAtPath("Assets/Hollowmere/Items/Lantern.asset")
                : Resources.FindObjectsOfTypeAll<GameObject>().First(g => g.name == "Village Well" && g.scene.IsValid());
            if (image && target == null)
            {
                string path = AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("Lantern t:ItemDefinition").First());
                target = AssetDatabase.LoadMainAssetAtPath(path);
            }
            Selection.activeObject = target;
            context.Selection.Set(new[] { context.Runtime.Resolver.BuildRef(target) }, SelectionOp.Replace);
            StudioViewportWindow viewport = StudioViewportWindow.Open();
            for (int i = 0; i < 15; i++) yield return null;
            PromptBar prompt = viewport.Prompt!;
            prompt.Text = text; prompt.Refresh();
            Assert.That(prompt.DisabledReason, Is.Null);
            UnityWindowCapture.CaptureStudio(Path.Combine(output, kind + "-send.png"), false);
            var send = prompt.SubmitAsync();
            while (!send.IsCompleted) yield return null;
            var handle = send.GetAwaiter().GetResult();
            Assert.That(handle, Is.Not.Null);
            File.WriteAllText(Path.Combine(output, kind + "-sent.json"), new JObject { ["prompt"] = text,
                ["requestId"] = handle!.RequestId, ["refusal"] = handle.Refusal == null ? "" : handle.Refusal.Code + ": " + handle.Refusal.Message }.ToString());
            Assert.That(handle.Refusal, Is.Null);
            end = DateTime.UtcNow.AddMinutes(12);
            CandidateEntry? entry = null;
            while (DateTime.UtcNow < end)
            {
                entry = context.Candidates.Find(handle.RequestId);
                RequestView? request = context.Gateway.Requests.FirstOrDefault(r => r.RequestId == handle.RequestId);
                if (entry != null || request?.State == "needs_clarification" || request?.State == "candidate_invalid" || request?.State == "failed") break;
                yield return null;
            }
            var view = context.Gateway.Requests.FirstOrDefault(r => r.RequestId == handle.RequestId);
            File.WriteAllText(Path.Combine(output, kind + "-outcome.json"), new JObject { ["requestId"] = handle.RequestId,
                ["state"] = view?.State ?? "unknown", ["candidate"] = entry == null ? new JObject() : StudioJson.ToToken(entry.ChangeSet),
                ["problems"] = entry == null ? new JArray() : new JArray(entry.Problems.Select(StudioJson.ToToken)) }.ToString());
            if (entry != null) StudioCandidatesWindow.Open(entry.Id);
            UnityWindowCapture.CaptureStudio(Path.Combine(output, kind + "-result.png"), false);
            Assert.That(entry, Is.Not.Null, "Send occurred; completion refused or requested clarification, see receipt");
            Assert.That(entry!.Problems.Any(), Is.False);
            context.Candidates.Reject(entry, "Guide walkthrough: reviewed candidate, explicit reject after Send.");
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator R2_38_CreatorGuide_ResumeImageSendWithoutAnotherRequest()
        {
            string output = Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE")!;
            string id = Environment.GetEnvironmentVariable("GAMECORE_P42C_GUIDE_REQUEST")!;
            EtosStudioSession.Start();
            EditorSceneManager.OpenScene("Assets/Hollowmere/Regions/ThornwickVillage.unity");
            EditorApplication.ExecuteMenuItem("GameCore/Studio/Open Studio");
            var context = StudioUiSession.Context;
            DateTime end = DateTime.UtcNow.AddSeconds(90);
            while ((!context.Gateway.Status.AgentReady || !context.Candidates.CanRequestStage) && DateTime.UtcNow < end) yield return null;
            Assert.That(context.Gateway.Status.AgentReady, Is.True);
            var receive = context.Candidates.Receive(id);
            while (!receive.IsCompleted) yield return null;
            CandidateEntry candidate = receive.GetAwaiter().GetResult();
            StudioCandidatesWindow.Open(candidate.Id);
            for (int i = 0; i < 20; i++) yield return null;
            bool previewOk = candidate.IsOpen && context.Candidates.Preview(candidate).Ok;
            File.WriteAllText(Path.Combine(output, "image-outcome.json"), new JObject { ["requestId"] = id,
                ["resubmitted"] = false, ["previewOk"] = previewOk, ["state"] = candidate.Stage.ToString(),
                ["candidate"] = candidate.ChangeSet == null ? new JObject() : StudioJson.ToToken(candidate.ChangeSet),
                ["problems"] = new JArray(candidate.Problems.Select(StudioJson.ToToken)) }.ToString());
            UnityWindowCapture.CaptureStudio(Path.Combine(output, "image-result.png"), false);
            Assert.That(previewOk, Is.True, "the existing candidate must pass the actual media importer policy");
            Assert.That(candidate.ChangeSet.Artifacts!.Any(a => a.MediaType.StartsWith("image/", StringComparison.Ordinal)), Is.True, "worker delivered an image artifact");
            Assert.That(candidate.ChangeSet.Operations.Any(o => o.Tool == "bind" || o.Tool == "assign" || (o.Tool == "set" && (string?)o.Args?["field"] == "icon")), Is.True, "candidate contains the requested icon assignment");
            context.Candidates.Reject(candidate, "Recovered guide image request reviewed; no second Send or generation.");
        }

        [UnityTest, Order(2)]
        public IEnumerator P42_OPS_01_Installed3dRefusesUnconfigured()
        {
            EtosStudioSession.Start();
            var context = StudioUiSession.Context;
            DateTime end = DateTime.UtcNow.AddSeconds(60);
            while (!context.Gateway.Status.AgentReady && DateTime.UtcNow < end) yield return null;
            var pending = context.Gateway.GenerateAsync(new OpRequest("3d", new JObject { ["prompt"] = "A simple stone" }, 0.25), default);
            while (!pending.IsCompleted) yield return null;
            OpResult result = pending.GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE")!, "3d-refusal.json"),
                new JObject { ["succeeded"] = result.Succeeded, ["code"] = result.Refusal?.Code, ["message"] = result.Refusal?.Message }.ToString());
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Refusal?.Code, Is.EqualTo("not_configured"));
        }
    }
}
