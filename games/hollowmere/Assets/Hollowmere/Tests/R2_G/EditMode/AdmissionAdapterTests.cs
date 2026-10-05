#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using GameCore.Gameplay.World.Editor;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Unity.App;
using NUnit.Framework;
using Hollowmere.R2_G.Fixtures;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hollowmere.R2_G.EditMode.Tests
{
    public sealed class AdmissionAdapterTests
    {
        private AdapterTestBed? bed;
        private SmokeFrameProbe? probe;
        private ChangeSet candidate = null!;
        private bool ready;
        private StageAdmission Admission => StageAdmission.Of(bed!.Runtime);
        private StageVerdict Verdict => Admission.VerdictOf(candidate.Id)!;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return new EnterPlayMode();
            AsyncOperation load = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Hollowmere/Boot/Boot.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!load.isDone) yield return null;
            for (int frame = 0; frame < 300 && GameApplication.Current == null; frame++) yield return null;
            Assert.That(GameApplication.Current, Is.Not.Null);
            ready = true;
            probe = new GameObject("R2-G2 frame smoke").AddComponent<SmokeFrameProbe>();
            probe.Root = GameApplication.Current;
            bed = new AdapterTestBed();
            var files = AdapterTestBed.PackageFiles();
            candidate = bed.Candidate(AdapterTestBed.TarGz(files), out string package, out string proposal);
            bed.Trust(candidate, bed.Verdict(candidate.Id, package, proposal, files));
            // Installation/compiler/catalog are doubles. Smoke runs on the actual active Hollowmere world.
            Admission.Options.SmokeTestFrameBudget = 100;
        }

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            bed?.Dispose();
            bed = null;
            if (probe != null) UnityEngine.Object.DestroyImmediate(probe.gameObject);
            probe = null;
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
        }

        // Resolve the additive API so the exact regression fixture also compiles against the pre-fix adapter.
        private void Bind(Func<StageVerdict, AdmissionSmokeStatus> poll)
        {
            MethodInfo? bind = typeof(StudioAdmissionServices).GetMethod("BindAdmission", new[]
            {
                typeof(StudioRuntime), typeof(Func<SaveService>), typeof(Func<bool>),
                typeof(Func<StageVerdict, AdmissionSmokeStatus>),
            });
            Assert.That(bind, Is.Not.Null, "R2-14: a tri-state gameplay binding is required");
            bind!.Invoke(null, new object[] { bed!.Runtime, new Func<SaveService?>(() => null),
                new Func<bool>(() => ready && GameApplication.Current == probe!.Root), poll });
        }

        private void Recover()
        {
            MethodInfo? recover = typeof(StudioAdmissionServices).GetMethod("RecoverPendingSmoke");
            Assert.That(recover, Is.Not.Null, "R2-14: durable pending smoke must re-register after reload");
            recover!.Invoke(null, new object[] { bed!.Runtime });
        }

        private AdmissionSmokeStatus PollEntry(StageVerdict verdict)
        {
            MethodInfo? run = typeof(StudioAdmissionServices).GetMethod("RunSmokeTest", new[]
            {
                typeof(StudioRuntime), typeof(StageVerdict), typeof(Func<string, string, int, AdmissionSmokeStatus>),
            });
            Assert.That(run, Is.Not.Null, "R2-14: descriptor dispatch must preserve Pending");
            return (AdmissionSmokeStatus)run!.Invoke(null, new object[] { bed!.Runtime, verdict,
                new Func<string, string, int, AdmissionSmokeStatus>((type, method, steps) =>
                    (AdmissionSmokeStatus)probe!.Poll(type, method, steps)) })!;
        }

        private void Begin()
        {
            Assert.That(Admission.Admit(candidate).Outcome, Is.EqualTo(AdmissionOutcome.Pending));
            Assert.That((string?)Admission.ReadPending(candidate.Id)?["phase"], Is.EqualTo("smoke-pending"));
            Assert.That(bed!.Runtime.Journal.Read(candidate.Id)!.EffectiveState, Is.EqualTo(ChangeSetState.Interrupted));
        }

        private IEnumerator Finish(ChangeSetState expected)
        {
            for (int frame = 0; frame < 200 && Admission.ReadPending(candidate.Id) != null; frame++) yield return null;
            Assert.That(Admission.ReadPending(candidate.Id), Is.Null);
            Assert.That(bed!.Runtime.Journal.Read(candidate.Id)!.EffectiveState, Is.EqualTo(expected));
            Assert.That(Directory.Exists(bed.PackageDirectory), Is.EqualTo(expected == ChangeSetState.Applied));
        }

        [UnityTest]
        public IEnumerator R2_14_AdapterPendingPassesOnlyAfterNWorldFrames()
        {
            Bind(PollEntry);
            Begin();
            for (int call = 0; call < 8; call++)
                Assert.That(Admission.Options.PollSmokeTest!(Verdict), Is.EqualTo(AdmissionSmokeStatus.Pending));
            Assert.That(probe!.Frames, Is.Zero, "polls cannot pump the world or run smoke steps synchronously");
            IEnumerator finish = Finish(ChangeSetState.Applied);
            while (finish.MoveNext()) yield return finish.Current;
            Assert.That(probe.Frames, Is.EqualTo(4));
            Assert.That(probe.Assertions, Is.EqualTo(4));
            Assert.That(probe.Begins, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator R2_14_AdapterPendingFailureRollsBack()
        {
            probe!.FailAt = 2;
            Bind(PollEntry);
            Begin();
            IEnumerator finish = Finish(ChangeSetState.Failed);
            while (finish.MoveNext()) yield return finish.Current;
            Assert.That(probe.Frames, Is.EqualTo(2));
            Assert.That(bed!.Compiler.Requests.Count, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator R2_14_AdapterSessionStopsBeingReadyFailsClosed()
        {
            Bind(PollEntry);
            Begin();
            Assert.That(Admission.Options.PollSmokeTest!(Verdict), Is.EqualTo(AdmissionSmokeStatus.Pending));
            ready = false;
            Assert.That(Admission.Options.PollSmokeTest!(Verdict), Is.EqualTo(AdmissionSmokeStatus.Failed));
            IEnumerator finish = Finish(ChangeSetState.Failed);
            while (finish.MoveNext()) yield return finish.Current;
        }

        [UnityTest]
        public IEnumerator R2_14_AdapterReloadDuringPendingFailsClosedDespitePassingBool()
        {
            Bind(PollEntry);
            Begin();
            AdmissionOptions options = Admission.Options;
            options.PollSmokeTest = null;
            int boolCalls = 0;
            options.SmokeTest = _ => { boolCalls++; return true; };
            StageAdmission.Configure(bed!.Runtime, options);
            string durable = Admission.ReadPending(candidate.Id)!.ToString();
            Recover();
            Recover();
            Assert.That(Admission.ReadPending(candidate.Id)!.ToString(), Is.EqualTo(durable));
            Assert.That(options.PollSmokeTest, Is.Not.Null);
            Admission.RefreshPendingVerdicts().GetAwaiter().GetResult();
            Assert.That(options.PollSmokeTest!(Verdict), Is.EqualTo(AdmissionSmokeStatus.Failed));
            IEnumerator finish = Finish(ChangeSetState.Failed);
            while (finish.MoveNext()) yield return finish.Current;
            Assert.That(boolCalls, Is.Zero, "a missing poll adapter must never fall back to passing bool");
        }

        [UnityTest]
        public IEnumerator R2_14_AdapterReloadRebindRetainsBudgetAndRequiresFreshTrust()
        {
            Bind(verdict => { PollEntry(verdict); return AdmissionSmokeStatus.Pending; });
            Begin();
            // Observe at least one real polling frame before reconstructing the admission service.
            for (int frame = 0; frame < 100 && (int)Admission.ReadPending(candidate.Id)!["smokeFrames"]! == 0; frame++)
                yield return null;
            var pending = Admission.ReadPending(candidate.Id)!;
            Assert.That((int)pending["smokeFrames"]!, Is.GreaterThan(0));
            StageVerdict oldVerdict = Verdict;
            AdmissionOptions options = Admission.Options;
            options.PollSmokeTest = null;
            StageAdmission.Configure(bed!.Runtime, options);
            Bind(PollEntry);
            Bind(PollEntry);
            Assert.That(Admission.ReadPending(candidate.Id)!.ToString(), Is.EqualTo(pending.ToString()));
            Assert.That(options.PollSmokeTest!(oldVerdict), Is.EqualTo(AdmissionSmokeStatus.Failed));
            Admission.RefreshPendingVerdicts().GetAwaiter().GetResult();
            IEnumerator finish = Finish(ChangeSetState.Applied);
            while (finish.MoveNext()) yield return finish.Current;
            Assert.That(probe!.Begins, Is.EqualTo(1));
            Assert.That(probe.Assertions, Is.EqualTo(4));
        }

        [UnityTest]
        public IEnumerator R2_14_AdapterExceptionInvalidStatusAndReadinessLossFailClosed()
        {
            Bind(_ => throw new InvalidOperationException("assertion failed"));
            Assert.That(Admission.Options.PollSmokeTest!(Verdict), Is.EqualTo(AdmissionSmokeStatus.Failed));
            Bind(_ => (AdmissionSmokeStatus)99);
            Assert.That(Admission.Options.PollSmokeTest!(Verdict), Is.EqualTo(AdmissionSmokeStatus.Failed));
            Bind(_ => { ready = false; return AdmissionSmokeStatus.Passed; });
            Assert.That(Admission.Options.PollSmokeTest!(Verdict), Is.EqualTo(AdmissionSmokeStatus.Failed));
            yield break;
        }

        [UnityTest]
        public IEnumerator R2_14_AdapterImmediateBoolRemainsSynchronous()
        {
            int calls = 0;
            StudioAdmissionServices.BindAdmission(bed!.Runtime, () => null, () => ready,
                verdict => { calls++; return StudioAdmissionServices.RunSmokeTest(bed.Runtime, verdict, (_, __, ___) => true); });
            Assert.That(Admission.Options.PollSmokeTest, Is.Null);
            Assert.That(Admission.Admit(candidate).Outcome, Is.EqualTo(AdmissionOutcome.Admitted));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(probe!.Frames, Is.Zero);
            yield break;
        }
    }
}
