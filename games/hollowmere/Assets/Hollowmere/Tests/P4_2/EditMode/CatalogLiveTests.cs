#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hollowmere.P4_2
{
    public sealed class CatalogLiveTests
    {
        [SetUp]
        public void RequireLiveQualification()
        {
            if (Environment.GetEnvironmentVariable("GAMECORE_ETOS_LIVE") != "1")
                Assert.Ignore("P4.2 live acceptance requires GAMECORE_ETOS_LIVE=1; run the documented installed-node command.");
        }

        [UnityTest]
        [Explicit("Fetches the retained real worker candidate; no new task or paid operation")]
        public IEnumerator R2_05_R2_38_LiveCandidateCatalogChangeRefusesStaleContext()
        {
            string output = Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE") ?? throw new InvalidOperationException("evidence required");
            string id = Environment.GetEnvironmentVariable("GAMECORE_P42_REQUEST") ?? throw new InvalidOperationException("retained request required");
            Assert.That(EtosStudioSession.Start(), Is.True, EtosStudioSession.Problem?.ToString());
            var fetch = EtosStudioSession.Gateway!.Client.GetCandidateAsync(id);
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (!fetch.IsCompleted && DateTime.UtcNow < deadline) yield return null;
            Assert.That(fetch.IsCompleted && !fetch.IsFaulted, Is.True, fetch.Exception?.GetBaseException().Message);
            CandidateInfo info = fetch.Result;
            File.WriteAllText(Path.Combine(output, "real-candidate.json"), info.Raw.ToString());
            ChangeSet candidate = StudioJson.Deserialize<ChangeSet>(info.ChangeSet.ToString());
            string project = Directory.GetParent(Application.dataPath)!.FullName;
            using (StudioRuntime runtime = StudioRuntime.Create(new StudioRuntimeOptions { Paths = new StudioPaths(project, Path.Combine(output, "state")) }))
            {
                string before = runtime.Registry.Catalog.Revision ?? runtime.Registry.Catalog.ComputeRevision();
                runtime.Registry.Register(new CatalogProbe());
                string after = runtime.Registry.Catalog.Revision ?? runtime.Registry.Catalog.ComputeRevision();
                Assert.That(after, Is.Not.EqualTo(before), "the actual installed-tool catalog revision changed");
                var staged = runtime.Engine.Stage(candidate, new StageOptions { Mode = ValidationMode.Candidate, ToolCatalogRevision = info.ToolCatalogRevision });
                var report = new JObject { ["requestId"] = id, ["taskId"] = info.TaskId, ["candidateCatalog"] = info.ToolCatalogRevision,
                    ["before"] = before, ["after"] = after, ["ok"] = staged.Ok,
                    ["diagnostics"] = new JArray(staged.AllDiagnostics.Select(d => StudioJson.ToToken(d))) };
                File.WriteAllText(Path.Combine(output, "stale-context.json"), report.ToString());
                Assert.That(staged.Ok, Is.False);
                Assert.That(staged.AllDiagnostics.Any(d => d.Code == DiagnosticCodes.StaleContext), Is.True, report.ToString());
            }
        }

        private sealed class CatalogProbe : IStudioTool
        {
            public ToolEntry Entry { get; } = new ToolEntry("p42.catalogProbe", ToolTier.Configure, RuntimeApply.Rebuild, false, Array.Empty<ArgSpec>());
            public bool Internal => false;
            public bool ReadOnly => false;
            public ToolStageResult Stage(EditContext context) => new ToolStageResult();
            public OperationResult Apply(EditContext context) => OperationResult.Refused(DiagnosticCodes.Refused, "metadata-only qualification probe");
        }
    }
}
