#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using GameCore.Studio.Authoring;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Etos.Testing;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Hollowmere.R6_E.Tests
{
    public sealed class AdmissionRebindingTests
    {
        [UnityTest]
        public IEnumerator R6_E_PendingReloadRefreshStartsAuthenticatedSessionBeforeFirstPump()
        {
            string root = Path.Combine(Path.GetTempPath(), "gc-r6e-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
            File.WriteAllText(Path.Combine(root, "ProjectSettings/ProjectSettings.asset"), "productGUID: " + new string('b', 32));
            string? previousKey = Environment.GetEnvironmentVariable(EtosCredentials.KeyFileVariable);
            FieldInfo runtimeField = typeof(StudioServices).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object? previousRuntime = runtimeField.GetValue(StudioServices.instance);
            MethodInfo reload = typeof(EtosStudioSession).GetMethod("OnBeforeReload", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var fake = new FakeCompanion();
            fake.Start();
            string pairing = Path.Combine(root, "fixture-pairing.json");
            File.WriteAllText(pairing, new JObject { ["key"] = FakeCompanion.AppKey, ["url"] = fake.NodeUrl }.ToString());
            var paths = new StudioPaths(root, Path.Combine(root, "state"));
            using var runtime = StudioRuntime.Create(new StudioRuntimeOptions
            {
                Paths = paths, Log = new MemoryStudioLog(), LoadIndexCache = false,
                SearchFolders = Array.Empty<string>(), TypeSource = () => Array.Empty<Type>()
            });
            try
            {
                Environment.SetEnvironmentVariable(EtosCredentials.KeyFileVariable, pairing);
                runtimeField.SetValue(StudioServices.instance, runtime);
                string project = EtosProjectContext.LoadProjectId(root);
                string id = IdDerivation.NewChangeSetId();
                string package = new string('a', 64), proposal = new string('b', 64), catalog = new string('c', 64);
                var expected = new StageCandidateRequest(id, project, root, "source", catalog, package, proposal, Array.Empty<string>());
                using var client = new CompanionClient(new EtosClientOptions { NodeUrl = fake.NodeUrl, ProjectId = project },
                    new EtosCredentials(FakeCompanion.AppKey, fake.NodeUrl, "fixture"));
                Task<StageJobInfo> submit = client.StageAsync(id, project, "source", catalog);
                yield return Await(submit);
                string job = submit.Result.JobId;
                JArray steps = new JArray();
                foreach (string step in new[] { "scan", "checkers", "dotnet", "unity-editmode", "playmode-smoke", "determinism", "budget" })
                    steps.Add(new JObject { ["id"] = step, ["status"] = "pass", ["durationMs"] = 1 });
                fake.CompleteStage(job, "done", new JObject
                {
                    ["schema"] = StageVerdict.SchemaId, ["jobId"] = job, ["signature"] = "fixture-signature",
                    ["projectId"] = project, ["sourceRevision"] = "source", ["catalogRevision"] = catalog,
                    ["changeSetId"] = id, ["confinement"] = "docker", ["pass"] = true, ["coldCache"] = false,
                    ["package"] = "com.example.rebinding", ["packageDigest"] = package, ["proposalDigest"] = proposal,
                    ["artifacts"] = new JArray(new JObject { ["role"] = "package", ["sha256"] = package },
                        new JObject { ["role"] = "proposal", ["sha256"] = proposal }),
                    ["steps"] = steps, ["files"] = new JArray(), ["forbiddenHits"] = new JArray(),
                    ["catalogDelta"] = new JObject { ["world"] = package,
                        ["predicted"] = CatalogSet.Combine(package, new[] { proposal }),
                        ["mechanisms"] = new JArray(new JObject { ["catalogType"] = "Example.Rebinding", ["fingerprint"] = proposal, ["package"] = "com.example.rebinding" }) },
                    ["durationMs"] = 7, ["budgetMs"] = 360000, ["createdAt"] = 1, ["slot"] = "fixture"
                });
                StageAdmission admission = StageAdmission.Of(runtime);
                Directory.CreateDirectory(admission.StateRoot);
                File.WriteAllText(Path.Combine(admission.StateRoot, "pending-" + id + ".json"),
                    new JObject { ["schema"] = "gamecore.studio.admission/2", ["changeSetId"] = id, ["phase"] = "compile" }.ToString());
                File.WriteAllText(Path.Combine(admission.StateRoot, "verdicts.json"), new JObject
                {
                    [id] = new JObject { ["jobId"] = job, ["expected"] = JObject.FromObject(expected) }
                }.ToString());
                reload.Invoke(EtosStudioSession.instance, null);
                Assert.That(admission.VerdictOf(id), Is.Null, "disk state is not authenticated trust");
                Assert.That(admission.Options.StageService, Is.Null);
                Type resumer = typeof(StageAdmission).Assembly.GetType("GameCore.Studio.Edit.AdmissionResumer")!;
                MethodInfo? refresh = resumer.GetMethod("RefreshPendingVerdicts", BindingFlags.Static | BindingFlags.NonPublic);
                // The pre-fix resumer called admission.RefreshPendingVerdicts directly.
                Task<int> recovery = refresh == null ? admission.RefreshPendingVerdicts()
                    : (Task<int>)refresh.Invoke(null, new object[] { admission })!;
                yield return Await(recovery);
                Assert.That(recovery.Result, Is.EqualTo(1));
                Assert.That(admission.VerdictOf(id), Is.Not.Null, "the persisted job must be fetched and verified after reload");
                Assert.That(admission.Options.StageService, Is.TypeOf<CompanionStageService>());
                Assert.That(EtosStudioSession.Gateway!.Runtime, Is.SameAs(runtime));

                // A fresh admission instance must not lose authentication while the gateway survives.
                StageAdmission replacement = StageAdmission.Configure(runtime, new AdmissionOptions());
                Task<int> rebound = (Task<int>)refresh!.Invoke(null, new object[] { replacement })!;
                yield return Await(rebound);
                Assert.That(rebound.Result, Is.EqualTo(1));
                Assert.That(replacement.VerdictOf(id), Is.Not.Null);
            }
            finally
            {
                reload.Invoke(EtosStudioSession.instance, null);
                runtimeField.SetValue(StudioServices.instance, previousRuntime);
                Environment.SetEnvironmentVariable(EtosCredentials.KeyFileVariable, previousKey);
                Directory.Delete(root, true);
            }
        }

        private static IEnumerator Await(Task task)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "authenticated recovery did not finish");
            task.GetAwaiter().GetResult();
        }
    }
}
