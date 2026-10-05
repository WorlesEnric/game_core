#nullable enable
// Hollowmere P2.4 EditMode tests - StageAdmission (docs/studio/03 s8, W-MECH-01): staged code reaches the project only
// through a passing verdict for exactly its artifacts and an explicit Admit; failures roll back through the journal; an
// admitted package is undone cleanly and the catalog hash returns.
using System.Collections.Generic;
using System.IO;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using NUnit.Framework;

namespace Hollowmere.P2_4.EditMode.Tests
{
    public sealed class StageAdmissionTests
    {
        private AdmissionTestBed _bed = null!;

        [SetUp]
        public void SetUp()
        {
            _bed = new AdmissionTestBed();
        }

        [TearDown]
        public void TearDown()
        {
            _bed.Dispose();
        }

        private static ValidationScenario? Scenario(ChangeSet entry, string name)
        {
            foreach (ValidationScenario scenario in entry.Validation ?? new List<ValidationScenario>())
            {
                if (scenario.Scenario == name)
                {
                    return scenario;
                }
            }

            return null;
        }

        [Test]
        public void AdmissionIsRefusedWithoutAVerdict()
        {
            SortedDictionary<string, byte[]> files = AdmissionTestBed.PackageFiles();
            ChangeSet candidate = _bed.Candidate(AdmissionTestBed.TarGz(files), out _, out _);

            AdmissionResult result = _bed.Admission.Admit(candidate);

            Assert.That(result.Outcome, Is.EqualTo(AdmissionOutcome.Refused), result.Detail);
            Assert.That(result.Reason, Is.EqualTo(VerdictReasons.Missing), result.Detail);
            Assert.That(Directory.Exists(_bed.PackageDirectory), Is.False, "nothing is written without a verdict");
            Assert.That(_bed.Compiler.Requests, Is.Empty, "nothing is compiled without a verdict");
            ChangeSet entry = _bed.Runtime.Journal.Read(candidate.Id)!;
            Assert.That(entry.EffectiveState, Is.EqualTo(ChangeSetState.Rejected));
            Assert.That(Scenario(entry, StageAdmission.AdmissionScenario)!.Status, Is.EqualTo(ScenarioStatus.Fail));

            // A failing verdict is refused too.
            ChangeSet second = _bed.Candidate(AdmissionTestBed.TarGz(files), out string packageSha, out string proposalSha);
            AdmissionResult failed = _bed.Admission.Admit(second, _bed.Verdict(second.Id, packageSha, proposalSha, files, pass: false));
            Assert.That(failed.Outcome, Is.EqualTo(AdmissionOutcome.Refused));
            Assert.That(failed.Reason, Is.EqualTo(VerdictReasons.Failed), failed.Detail);
            Assert.That(Scenario(_bed.Runtime.Journal.Read(second.Id)!, StageAdmission.VerdictScenario)!.Status, Is.EqualTo(ScenarioStatus.Fail));
        }

        [Test]
        public void AdmissionSucceedsWithAMatchingVerdict()
        {
            SortedDictionary<string, byte[]> files = AdmissionTestBed.PackageFiles();
            ChangeSet candidate = _bed.Candidate(AdmissionTestBed.TarGz(files), out string packageSha, out string proposalSha);
            byte[] verdict = _bed.Verdict(candidate.Id, packageSha, proposalSha, files);

            AdmissionResult result = _bed.Admission.Admit(candidate, verdict);

            Assert.That(result.Outcome, Is.EqualTo(AdmissionOutcome.Admitted), result.Detail + " " + string.Join(" | ", _bed.Log.Lines));
            foreach (KeyValuePair<string, byte[]> file in files)
            {
                string path = Path.Combine(_bed.PackageDirectory, file.Key);
                Assert.That(File.Exists(path), Is.True, file.Key);
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(file.Value), file.Key);
            }

            string predicted = CatalogSet.Combine(_bed.Catalog.World, new[] { AdmissionTestBed.MechanismFingerprint });
            Assert.That(result.Live, Is.EqualTo(predicted));
            Assert.That(result.Predicted, Is.EqualTo(predicted));
            Assert.That(result.Before, Is.EqualTo(_bed.Catalog.World), "no mechanism before: the catalog-set hash is the world fingerprint");
            Assert.That(_bed.Compiler.Requests.Count, Is.EqualTo(1));
            Assert.That(_bed.Checker.Runs, Is.EqualTo(1), "the checkers re-run on the admitted package");
            ChangeSet entry = _bed.Runtime.Journal.Read(candidate.Id)!;
            Assert.That(entry.EffectiveState, Is.EqualTo(ChangeSetState.Applied));
            Assert.That(entry.Operations[0].Tool, Is.EqualTo(MechanismAdmission.AdmitTool));
            Assert.That(Scenario(entry, StageAdmission.VerdictScenario)!.Status, Is.EqualTo(ScenarioStatus.Pass));
            Assert.That(Scenario(entry, StageAdmission.VerdictScenario)!.Detail, Does.Contain("cs-test"), "the journal records the slot");
            Assert.That(Scenario(entry, StageAdmission.VerdictScenario)!.Detail, Does.Contain("sha256:" + ContentStamp.Sha256Hex(verdict)), "the journal records the verdict id");
            Assert.That(Scenario(entry, StageAdmission.AdmissionScenario)!.Status, Is.EqualTo(ScenarioStatus.Pass));
            Assert.That(_bed.Admission.ReadPending(candidate.Id), Is.Null);
        }

        [Test]
        public void AdmissionIsRefusedWithATamperedArtifact()
        {
            SortedDictionary<string, byte[]> staged = AdmissionTestBed.PackageFiles("1");
            SortedDictionary<string, byte[]> tampered = AdmissionTestBed.PackageFiles("2");
            ChangeSet candidate = _bed.Candidate(AdmissionTestBed.TarGz(tampered), out string packageSha, out string proposalSha);

            // The verdict names the delivered archive but its staged files differ from what the archive now holds.
            AdmissionResult fileMismatch = _bed.Admission.Admit(candidate, _bed.Verdict(candidate.Id, packageSha, proposalSha, staged));
            Assert.That(fileMismatch.Outcome, Is.EqualTo(AdmissionOutcome.Refused), fileMismatch.Detail);
            Assert.That(fileMismatch.Reason, Is.EqualTo(VerdictReasons.Mismatch), fileMismatch.Detail);
            Assert.That(fileMismatch.Detail, Does.Contain("Runtime/Plate.cs"));
            Assert.That(Directory.Exists(_bed.PackageDirectory), Is.False);

            // A verdict for another archive digest.
            ChangeSet other = _bed.Candidate(AdmissionTestBed.TarGz(tampered), out _, out string otherProposal);
            AdmissionResult digestMismatch = _bed.Admission.Admit(other, _bed.Verdict(other.Id, new string('c', 64), otherProposal, tampered));
            Assert.That(digestMismatch.Reason, Is.EqualTo(VerdictReasons.Mismatch), digestMismatch.Detail);

            // A verdict for another change set.
            ChangeSet third = _bed.Candidate(AdmissionTestBed.TarGz(staged), out string thirdPackage, out string thirdProposal);
            AdmissionResult wrongChangeSet = _bed.Admission.Admit(third, _bed.Verdict(IdDerivation.NewChangeSetId(), thirdPackage, thirdProposal, staged));
            Assert.That(wrongChangeSet.Outcome, Is.EqualTo(AdmissionOutcome.Refused));
            Assert.That(Directory.Exists(_bed.PackageDirectory), Is.False);
            Assert.That(_bed.Compiler.Requests, Is.Empty);
        }

        [Test]
        public void ACompileFailureRollsTheAdmissionBack()
        {
            SortedDictionary<string, byte[]> files = AdmissionTestBed.PackageFiles();
            ChangeSet candidate = _bed.Candidate(AdmissionTestBed.TarGz(files), out string packageSha, out string proposalSha);
            _bed.Compiler.Fail = true;

            AdmissionResult result = _bed.Admission.Admit(candidate, _bed.Verdict(candidate.Id, packageSha, proposalSha, files));

            Assert.That(result.Outcome, Is.EqualTo(AdmissionOutcome.RolledBack), result.Detail);
            Assert.That(result.Reason, Is.EqualTo("compile_failed"));
            Assert.That(Directory.Exists(_bed.PackageDirectory), Is.False, "RollbackInterrupted ran the mechanism.remove inverse");
            Assert.That(_bed.Compiler.Requests.Count, Is.EqualTo(2), "the rollback recompiles");
            ChangeSet entry = _bed.Runtime.Journal.Read(candidate.Id)!;
            Assert.That(entry.EffectiveState, Is.EqualTo(ChangeSetState.Failed));
            Assert.That(Scenario(entry, StageAdmission.AdmissionScenario)!.Status, Is.EqualTo(ScenarioStatus.Fail));
            Assert.That(Scenario(entry, StageAdmission.AdmissionScenario)!.Detail, Does.Contain("compile_failed"));
        }

        [Test]
        public void AFaultAfterTheCompileRollsTheAdmissionBack()
        {
            SortedDictionary<string, byte[]> files = AdmissionTestBed.PackageFiles();
            ChangeSet candidate = _bed.Candidate(AdmissionTestBed.TarGz(files), out string packageSha, out string proposalSha);
            _bed.Admission.Options.FaultHook = (point, id) =>
            {
                if (point == AdmissionFaultPoint.BeforeCatalogCheck)
                {
                    throw new IOException("simulated editor fault before the catalog check");
                }
            };

            AdmissionResult result = _bed.Admission.Admit(candidate, _bed.Verdict(candidate.Id, packageSha, proposalSha, files));

            Assert.That(result.Outcome, Is.EqualTo(AdmissionOutcome.RolledBack), result.Detail);
            Assert.That(result.Reason, Is.EqualTo("fault"));
            Assert.That(Directory.Exists(_bed.PackageDirectory), Is.False);
            Assert.That(_bed.Runtime.Journal.Read(candidate.Id)!.EffectiveState, Is.EqualTo(ChangeSetState.Failed));
        }

        [Test]
        public void AMismatchedLiveCatalogRollsTheAdmissionBack()
        {
            SortedDictionary<string, byte[]> files = AdmissionTestBed.PackageFiles();
            ChangeSet candidate = _bed.Candidate(AdmissionTestBed.TarGz(files), out string packageSha, out string proposalSha);
            byte[] verdict = _bed.Verdict(candidate.Id, packageSha, proposalSha, files);
            _bed.Catalog.World = new string('d', 64);

            AdmissionResult result = _bed.Admission.Admit(candidate, verdict);

            Assert.That(result.Outcome, Is.EqualTo(AdmissionOutcome.RolledBack), result.Detail);
            Assert.That(result.Reason, Is.EqualTo("catalog_mismatch"));
            Assert.That(Directory.Exists(_bed.PackageDirectory), Is.False);
        }

        [Test]
        public void UndoAfterAdmitRemovesThePackageAndRestoresTheCatalogHash()
        {
            SortedDictionary<string, byte[]> files = AdmissionTestBed.PackageFiles();
            ChangeSet candidate = _bed.Candidate(AdmissionTestBed.TarGz(files), out string packageSha, out string proposalSha);
            AdmissionResult admitted = _bed.Admission.Admit(candidate, _bed.Verdict(candidate.Id, packageSha, proposalSha, files));
            Assert.That(admitted.Outcome, Is.EqualTo(AdmissionOutcome.Admitted), admitted.Detail);
            Assert.That(_bed.Admission.LiveHash(null, out _), Is.EqualTo(admitted.Predicted));

            AdmissionResult undone = _bed.Admission.Undo(candidate.Id);

            Assert.That(undone.Outcome, Is.EqualTo(AdmissionOutcome.Undone), undone.Detail);
            Assert.That(Directory.Exists(_bed.PackageDirectory), Is.False, "the undo removed the package");
            Assert.That(undone.Live, Is.EqualTo(admitted.Before), "the catalog hash returned to its value before the admission");
            ChangeSet entry = _bed.Runtime.Journal.Read(candidate.Id)!;
            Assert.That(entry.EffectiveState, Is.EqualTo(ChangeSetState.Undone));
            Assert.That(Scenario(entry, StageAdmission.UndoScenario)!.Status, Is.EqualTo(ScenarioStatus.Pass));

            // Redo re-admits from the same retained, verified artifacts.
            HistoryResult redo = _bed.Runtime.History.Redo(candidate.Id);
            Assert.That(redo.Ok, Is.True, redo.Diagnostics.Count > 0 ? redo.Diagnostics[0].Message : string.Empty);
            Assert.That(File.Exists(Path.Combine(_bed.PackageDirectory, "package.json")), Is.True);
        }
    }
}
