// Hollowmere P3.1 EditMode (UnityTest) - the pressure plate sample admitted into the running Hollowmere (P2.4's open
// Play-Mode admission test; R2-G request 4).
//
//   PressurePlateAdmittedIntoTheRunningGame: Boot.unity plays; the player gains 7 old coins (2 -> 9, a state a fresh boot
//     does not have). A disposable Studio runtime (temporary state and packages roots) is bound to the running game with
//     the game's own R2-G binding (HollowmereStudioAdmission.Bind: SaveService capture, session readiness, the live smoke
//     registry). The candidate carries the real sample artifacts (samples/mechanisms/pressure-plate/candidate:
//     package.tgz, proposal.json with smokeTest Hollowmere.Mechanism.PressurePlate.PressurePlateSmoke.Begin, 120 steps)
//     and an authenticated test verdict bound to those exact bytes. Admit(captureAndStop) captures the running game
//     ("admit-<id>" in its SaveService) and asks Play Mode to stop; Play Mode stops; the admission installs the package
//     in the temporary packages root, compiles (fake), re-bakes and verifies the catalog set (fake catalog), and asks Play
//     Mode to start; Play Mode starts a fresh game (2 coins); the admission is re-configured from its durable record with
//     fresh trust (RefreshPendingVerdicts, as after a domain reload), the fresh game binds, the session becomes ready,
//     the capture is restored into the fresh game (9 coins again) and the live smoke runs in the active world
//     (HollowmereAdmittedSmoke: session ready, root running, equal round-trip slot hash) -> Admitted.
//
// Compile and catalog are fakes (as in P2.4/R2-B's admission tests): the real compile, domain reload and catalog proof
// belong to the staging lane's own tests. Domain reload on entering Play Mode is disabled for this test (restored
// afterwards) so the disposable runtime survives the Play/Edit/Play cycle. Logs "[P3.1] admission ..." lines.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using GameCore.Studio.Authoring;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Unity.App;
using Hollowmere.Authoring;
using Hollowmere.Boot;
using Hollowmere.Game;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace Hollowmere.P3_1.EditMode.Tests
{
    public sealed class P31AdmissionInPlayMode
    {
        private bool savedOptionsEnabled;
        private EnterPlayModeOptions savedOptions;
        private P31AdmissionBed? bed;

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator PressurePlateAdmittedIntoTheRunningGame()
        {
            savedOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            savedOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorSceneManager.OpenScene(HollowmerePaths.BootScene, OpenSceneMode.Single);

            // 1. The running game, with a state a fresh boot does not have.
            yield return new EnterPlayMode();
            HollowmereGame? game = null;
            yield return WaitForGame(60, g => game = g);
            GameBoot boot = game!.GetComponent<GameBoot>();
            int coins = game.Director!.ItemCount("OldCoin");
            Assert.That(boot.Modules!.Inventory.Commands!.Grant("OldCoin", 7).Admitted, Is.True);
            yield return Until(() => game.Director!.ItemCount("OldCoin") == coins + 7, "seven more old coins", 300);
            int playedCoins = coins + 7;

            // 2. A disposable Studio runtime bound to the running game through the game's own R2-G binding.
            bed = new P31AdmissionBed(GameApplication.Current!.CatalogHash.ToHex());
            HollowmereAdmittedSmoke smoke = HollowmereStudioAdmission.Bind(bed.Runtime, boot, boot.Saves!);
            AdmissionOptions options = bed.Admission.Options;
            Assert.That(options.Capture, Is.InstanceOf<SaveServiceAdmissionCapture>());
            Assert.That(options.SessionReady!(), Is.True, "the running game's session is ready");
            ChangeSet candidate = bed.Candidate();
            bed.Trust(candidate);

            // 3. Admit from Play Mode: capture the running game, then Play Mode must stop.
            AdmissionResult first = bed.Admission.Admit(candidate, captureAndStop: true);
            Debug.Log("[P3.1] admission from Play Mode: " + first.Outcome + " (" + first.Reason + ") " + first.Detail + "; capture " + first.CaptureSlot);
            Assert.That(first.Outcome, Is.EqualTo(AdmissionOutcome.Pending), first.Detail);
            Assert.That(bed.StopRequests, Is.EqualTo(1), "the admission asked Play Mode to stop");
            string captureSlot = first.CaptureSlot ?? (string)bed.Admission.ReadPending(candidate.Id)!["captureSlot"]!;
            Assert.That(boot.Saves!.Exists(captureSlot), Is.True, "the running game was captured to " + captureSlot);
            string saveDirectory = boot.Saves.Directory;

            // 4. Play Mode stops; the admission installs, compiles, re-bakes, verifies and asks Play Mode to start.
            yield return new ExitPlayMode();
            AdmissionResult edit = Resume(candidate.Id);
            Assert.That(edit.Outcome, Is.EqualTo(AdmissionOutcome.Pending), edit.Detail);
            Assert.That(bed.StartRequests, Is.GreaterThanOrEqualTo(1), "the admission asked Play Mode to start: " + edit.Detail);
            Assert.That(Directory.Exists(bed.PackageDirectory), Is.True, "the pressure plate package is installed");
            Assert.That(File.Exists(Path.Combine(bed.PackageDirectory, "package.json")), Is.True);
            Debug.Log("[P3.1] admission in Edit Mode: " + edit.Detail + "; installed " + bed.PackageDirectory);

            // 5. A fresh game; the admission resumes from its durable record with fresh trust and binds to the new game.
            yield return new EnterPlayMode();
            HollowmereGame? fresh = null;
            yield return WaitForGame(60, g => fresh = g);
            GameBoot freshBoot = fresh!.GetComponent<GameBoot>();
            Assert.That(fresh.Director!.ItemCount("OldCoin"), Is.EqualTo(coins), "a fresh game starts without the played coins");
            Assert.That(freshBoot.Saves!.Directory, Is.EqualTo(saveDirectory));
            bed.Reconfigure();
            smoke = HollowmereStudioAdmission.Bind(bed.Runtime, freshBoot, freshBoot.Saves!);
            AdmissionResult done = Resume(candidate.Id);
            Debug.Log("[P3.1] admission after the restart: " + done.Outcome + " (" + done.Reason + ") " + done.Detail + "; smoke " + smoke.LastReport);
            Assert.That(done.Outcome, Is.EqualTo(AdmissionOutcome.Admitted), done.Detail + " | smoke " + smoke.LastReport);
            Assert.That(smoke.Runs, Is.EqualTo(1), "the live smoke ran once");
            StringAssert.StartsWith("pass:", smoke.LastReport);
            yield return Until(() => fresh.Director!.ItemCount("OldCoin") == playedCoins, "the captured game restored into the fresh one", 300);
            SaveRoundTripReport roundTrip = freshBoot.Saves!.TestRoundTrip();
            Assert.That(roundTrip.Equal, Is.True, roundTrip.ToString());
            freshBoot.Saves.Delete(captureSlot);
            yield return new ExitPlayMode();
        }

        [TearDown]
        public void TearDown()
        {
            EditorSettings.enterPlayModeOptionsEnabled = savedOptionsEnabled;
            EditorSettings.enterPlayModeOptions = savedOptions;
            bed?.Dispose();
            bed = null;
        }

        private AdmissionResult Resume(string id)
        {
            AdmissionResult result = bed!.Admission.Resume(id);
            for (int i = 0; i < 8 && result.Outcome == AdmissionOutcome.Pending; i++)
            {
                AdmissionResult next = bed.Admission.Resume(id);
                if (next.Outcome == result.Outcome && next.Detail == result.Detail)
                {
                    return next;
                }

                result = next;
            }

            return result;
        }

        private static IEnumerator WaitForGame(double seconds, Action<HollowmereGame> found)
        {
            DateTime end = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                HollowmereGame? game = UnityEngine.Object.FindAnyObjectByType<HollowmereGame>();
                GameBoot? boot = game != null ? game.GetComponent<GameBoot>() : null;
                if (game != null && game.Saves != null && game.Director != null && boot != null && boot.Saves != null && boot.AdmissionReady(boot.Saves))
                {
                    found(game);
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("HollowmereGame did not boot a ready session within " + seconds + " s");
        }

        private static IEnumerator Until(Func<bool> condition, string what, int frames)
        {
            for (int i = 0; i < frames && !condition(); i++)
            {
                yield return null;
            }

            Assert.That(condition(), Is.True, "not reached within " + frames + " frames: " + what);
        }
    }

    /// <summary>
    /// A disposable Studio runtime with the admission tools only, a temporary packages root outside the project, fake
    /// compiler/catalog/checkers and an authenticated test stage service, carrying the pressure plate sample's artifacts.
    /// </summary>
    internal sealed class P31AdmissionBed : IDisposable, IStageService, IAdmissionCompiler, IAdmissionCatalog, IAdmissionChecker, IStudioLog
    {
        public const string Package = HollowmereAdmittedSmoke.PressurePlatePackage;
        public const string CatalogType = "Hollowmere.Mechanism.PressurePlate.Generated.PressurePlateCatalog";
        public static readonly string PlateFingerprint = new string('c', 64);

        private readonly string world;
        private byte[]? verdictBytes;
        private byte[]? archive;
        private string packageSha = string.Empty;
        private string proposalSha = string.Empty;

        public P31AdmissionBed(string worldFingerprint)
        {
            world = worldFingerprint;
            Root = Path.Combine(Path.GetTempPath(), "gcstudio-p31-" + Guid.NewGuid().ToString("N").Substring(0, 10));
            StateRoot = Path.Combine(Root, "state");
            PackagesRoot = Path.Combine(Root, "packages");
            Directory.CreateDirectory(StateRoot);
            Directory.CreateDirectory(PackagesRoot);
            Runtime = StudioRuntime.Create(new StudioRuntimeOptions
            {
                Paths = new StudioPaths(ProjectRoot, StateRoot, "p31-admission-test"),
                Log = this,
                TypeSource = () => Array.Empty<Type>(),
                ToolMethodSource = AdmissionMethods,
                SearchFolders = new[] { "Assets/Hollowmere/Tests/P3_1" },
                LoadIndexCache = false,
            });
            Options = new AdmissionOptions
            {
                PackagesRoot = PackagesRoot,
                Compiler = this,
                Catalog = this,
                Checker = this,
                StageService = this,
                PlayModeProbe = () => EditorApplication.isPlaying,
                StopPlayMode = () => StopRequests++,
                StartPlayMode = () => StartRequests++,
                ProjectId = "p31-project",
                SourceRevision = () => "p31-source",
                CatalogRevision = () => "p31-catalog",
            };
            Admission = StageAdmission.Configure(Runtime, Options);
        }

        public static string ProjectRoot => Directory.GetParent(Application.dataPath)!.FullName;

        public static string SampleRoot => Path.GetFullPath(Path.Combine(ProjectRoot, "..", "..", "samples", "mechanisms", "pressure-plate", "candidate", "artifacts"));

        public string Root { get; }

        public string StateRoot { get; }

        public string PackagesRoot { get; }

        public string PackageDirectory => Path.Combine(PackagesRoot, Package);

        public StudioRuntime Runtime { get; }

        public AdmissionOptions Options { get; }

        public StageAdmission Admission { get; private set; }

        public int StopRequests { get; private set; }

        public int StartRequests { get; private set; }

        public List<string> Lines { get; } = new List<string>();

        /// <summary>As after a domain reload: a new admission service over the same runtime, verdicts re-fetched and re-verified.</summary>
        public void Reconfigure()
        {
            Admission = StageAdmission.Configure(Runtime, Options);
            Admission.RefreshPendingVerdicts().GetAwaiter().GetResult();
        }

        /// <summary>A worker candidate (mechanism.propose) carrying the sample's package.tgz and proposal.json; retained.</summary>
        public ChangeSet Candidate()
        {
            archive = File.ReadAllBytes(Path.Combine(SampleRoot, "package.tgz"));
            byte[] proposal = File.ReadAllBytes(Path.Combine(SampleRoot, "proposal.json"));
            packageSha = ContentStamp.Sha256Hex(archive);
            proposalSha = ContentStamp.Sha256Hex(proposal);
            var packageRef = new ArtifactRef(packageSha, "application/gzip", archive.LongLength, "package.tgz", null, "package");
            var proposalRef = new ArtifactRef(proposalSha, "application/json", proposal.LongLength, "proposal.json", null, "proposal");
            Runtime.Artifacts.Put(archive, packageRef);
            Runtime.Artifacts.Put(proposal, proposalRef);
            var args = new JObject
            {
                ["description"] = "The pressure plate sample (P3.1 Play-Mode admission test).",
                ["package"] = new JObject { ["artifact"] = "sha256:" + packageSha },
                ["proposal"] = new JObject { ["artifact"] = "sha256:" + proposalSha },
                ["stageInputs"] = new JArray("Assets/Hollowmere/World.json"),
            };
            var propose = new Operation("op1", BuiltInToolIds.MechanismPropose, null, args, null, null, RuntimeApply.Compile);
            return new ChangeSet(
                IdDerivation.NewChangeSetId(),
                ChangeSet.SchemaId,
                new Intent("Admit the pressure plate into the running Hollowmere", IntentOrigin.Agent),
                new[] { propose },
                artifacts: new[] { packageRef, proposalRef },
                requirements: new Requirements(RuntimeApply.Compile, false, true, false));
        }

        /// <summary>Issues and trusts a passing verdict bound to the candidate's exact bytes (as the companion would).</summary>
        public void Trust(ChangeSet candidate)
        {
            PackageArchive? read = PackageArchive.Read(archive!, out string? problem);
            Assert.That(read, Is.Not.Null, problem);
            var files = new JArray();
            foreach (KeyValuePair<string, byte[]> file in read!.Files)
            {
                files.Add(new JObject { ["bytes"] = file.Value.Length, ["path"] = file.Key, ["sha256"] = ContentStamp.Sha256Hex(file.Value) });
            }

            var steps = new JArray();
            foreach (string id in new[] { "scan", "checkers", "dotnet", "unity-editmode", "playmode-smoke", "determinism", "budget" })
            {
                steps.Add(new JObject { ["durationMs"] = 10, ["id"] = id, ["status"] = "pass" });
            }

            var verdict = new JObject
            {
                ["jobId"] = "p31-job", ["projectId"] = "p31-project", ["sourceRevision"] = "p31-source",
                ["catalogRevision"] = "p31-catalog", ["confinement"] = "docker", ["coldCache"] = false,
                ["artifacts"] = new JArray(
                    new JObject { ["role"] = "package", ["sha256"] = packageSha },
                    new JObject { ["role"] = "proposal", ["sha256"] = proposalSha }),
                ["budgetMs"] = 360000,
                ["catalogDelta"] = new JObject
                {
                    ["mechanisms"] = new JArray(new JObject { ["catalogType"] = CatalogType, ["fingerprint"] = PlateFingerprint, ["package"] = Package }),
                    ["predicted"] = CatalogSet.Combine(world, new[] { PlateFingerprint }),
                    ["world"] = world,
                },
                ["changeSetId"] = candidate.Id,
                ["createdAt"] = 1,
                ["durationMs"] = 70,
                ["files"] = files,
                ["forbiddenHits"] = new JArray(),
                ["package"] = Package,
                ["pass"] = true,
                ["runner"] = new JObject { ["version"] = "p31-test" },
                ["schema"] = StageVerdict.SchemaId,
                ["slot"] = "cs-p31",
                ["steps"] = steps,
            };
            verdictBytes = new UTF8Encoding(false).GetBytes(verdict.ToString(Formatting.None));
            Admission.FetchVerdict("p31-job", Admission.BuildStageRequest(candidate, "games/hollowmere")).GetAwaiter().GetResult();
        }

        // ---------------------------------------------------------------- IStageService (authenticated test companion)

        public Task<string> RequestStage(StageCandidateRequest request) => Task.FromResult("p31-job");

        public Task<SignedVerdict> GetVerdict(string jobId) =>
            Task.FromResult(new SignedVerdict(jobId, "companion-test-signature", JObject.Parse(Encoding.UTF8.GetString(verdictBytes!))));

        public Task<StageVerification> VerifyVerdict(string jobId, StageVerificationRequest request) => Task.FromResult(new StageVerification(true, jobId));

        // ---------------------------------------------------------------- fakes: compile, catalog, checkers, log

        public void Compile(string reason, Action<AdmissionCompileResult> done) => done(new AdmissionCompileResult(true, false, "compiled (fake)"));

        public string? WorldFingerprint(out string? problem)
        {
            problem = null;
            return world;
        }

        public string? MechanismFingerprint(string catalogType, out string? problem)
        {
            problem = null;
            if (catalogType == CatalogType && Directory.Exists(PackageDirectory))
            {
                return PlateFingerprint;
            }

            problem = catalogType + " is not loaded";
            return null;
        }

        public bool Check(string packageDirectory, string package, out string detail)
        {
            detail = Directory.Exists(packageDirectory) ? "fake checkers clean" : "package missing";
            return Directory.Exists(packageDirectory);
        }

        public void Write(StudioLogLevel level, string category, string message, Diagnostic? diagnostic = null) => Lines.Add(level + " " + category + ": " + message);

        public void Dispose()
        {
            Runtime.Dispose();
            try
            {
                StageAdmission.DeleteDirectory(Root);
            }
            catch (IOException)
            {
            }
        }

        private static IEnumerable<MethodInfo> AdmissionMethods()
        {
            foreach (MethodInfo method in typeof(MechanismAdmission).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (AuthoringMetadata.Operation(method) != null)
                {
                    yield return method;
                }
            }
        }
    }
}
