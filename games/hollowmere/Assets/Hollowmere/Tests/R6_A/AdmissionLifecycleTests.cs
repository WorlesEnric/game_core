#nullable enable
using System;
using System.IO;
using System.Text;
using GameCore.Studio.Edit;
using Hollowmere.P2_4.EditMode.Tests;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hollowmere.R6_A
{
    public sealed class AdmissionLifecycleTests
    {
        [TestCase("world")]
        [TestCase("predicted")]
        public void R6_A_01_P42dMissingDeltaRefusedBeforeInstall(string missing)
        {
            using (var bed = new AdmissionTestBed())
            {
                var files = AdmissionTestBed.PackageFiles();
                var candidate = bed.Candidate(AdmissionTestBed.TarGz(files), out string package, out string proposal);
                JObject verdict = JObject.Parse(Encoding.UTF8.GetString(bed.Verdict(candidate.Id, package, proposal, files)));
                ((JObject)verdict["catalogDelta"]!).Remove(missing);
                Assert.Throws<InvalidOperationException>(() =>
                    bed.Trust(candidate, Encoding.UTF8.GetBytes(verdict.ToString(Formatting.None))));
                Assert.That(Directory.Exists(bed.PackageDirectory), Is.False);
                Assert.That(bed.Compiler.Requests, Is.Empty);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void R6_A_02_ExpiredDurableCompileDoesNotReenterAndRetainsTimeout(bool legacy)
        {
            using (var bed = new AdmissionTestBed())
            {
                var files = AdmissionTestBed.PackageFiles();
                var candidate = bed.Candidate(AdmissionTestBed.TarGz(files), out string package, out string proposal);
                bed.Trust(candidate, bed.Verdict(candidate.Id, package, proposal, files));
                bed.Admission.Options.FaultHook = (point, id) =>
                {
                    if (point == AdmissionFaultPoint.Compile) throw new AdmissionCrashException();
                };
                Assert.Throws<AdmissionCrashException>(() => bed.Admission.Admit(candidate));
                JObject pending = bed.Admission.ReadPending(candidate.Id)!;
                pending["startedMs"] = DateTimeOffset.UtcNow.AddSeconds(-817).ToUnixTimeMilliseconds();
                if (!legacy)
                {
                    pending["compileAction"] = "admit";
                    pending["compileDomain"] = "lost-process";
                    pending["compileDeadlineMs"] = DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeMilliseconds();
                }
                File.WriteAllText(Path.Combine(bed.Admission.StateRoot, "pending-" + candidate.Id + ".json"), pending.ToString());
                bed.Admission.Options.Compiler = new UnityAdmissionCompiler();
                bed.Admission.Options.FaultHook = (point, id) =>
                {
                    // Stop before a real removal compile; the expired install was never reissued.
                    if (point == AdmissionFaultPoint.UndoCompile) throw new AdmissionCrashException();
                };
                Assert.Throws<AdmissionCrashException>(() => bed.Admission.Resume(candidate.Id));
                JObject timeout = JObject.Parse(File.ReadAllText(Path.Combine(bed.Admission.StateRoot, "compile-timeout-" + candidate.Id + ".json")));
                Assert.That((string?)timeout["compileOutcome"], Is.EqualTo("compile_timeout"));
                Assert.That((string?)bed.Admission.ReadPending(candidate.Id)!["reason"], Is.EqualTo("compile_timeout"));
                Assert.That(Directory.Exists(bed.PackageDirectory), Is.False);
                Assert.That(bed.Compiler.Requests, Is.Empty);
                pending = bed.Admission.ReadPending(candidate.Id)!;
                pending["phase"] = "compile-timeout";
                File.WriteAllText(Path.Combine(bed.Admission.StateRoot, "pending-" + candidate.Id + ".json"), pending.ToString());
                StageAdmission recovered = StageAdmission.Configure(bed.Runtime, bed.Admission.Options);
                Assert.That(recovered.VerdictOf(candidate.Id), Is.Null, "new domain has no retained trust");
                Assert.That(recovered.Resume(candidate.Id).Outcome, Is.EqualTo(AdmissionOutcome.UndoFailed));
                Assert.That(recovered.Resume(candidate.Id).Reason, Is.EqualTo("compile_timeout"));
            }
        }
    }
}
