#nullable enable
// Hollowmere R2-G2 adapter test support (based on the P2.4 fixture) - a disposable Studio runtime (temporary state root, only the admission tools) with a
// temporary packages root outside the project (Unity never imports what an admission writes there) and fakes for the
// compiler, the catalog and the checkers, so every admission branch runs without a domain reload.
using System;
using GameCore.Studio.Authoring.Agent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hollowmere.R2_G.EditMode.Tests
{
    internal sealed class MemoryLog : IStudioLog
    {
        public List<string> Lines { get; } = new List<string>();

        public void Write(StudioLogLevel level, string category, string message, Diagnostic? diagnostic = null)
        {
            Lines.Add(level + " " + category + ": " + message);
        }
    }

    /// <summary>Compiles instantly; fails while <see cref="Fail"/> is set.</summary>
    internal sealed class FakeCompiler : IAdmissionCompiler
    {
        public bool Fail { get; set; }

        public List<string> Requests { get; } = new List<string>();

        public void Compile(string reason, Action<AdmissionCompileResult> done)
        {
            Requests.Add(reason);
            bool fail = Fail && reason.StartsWith("admit", StringComparison.Ordinal);
            done(fail
                ? new AdmissionCompileResult(false, false, "error CS0103: The name 'Broken' does not exist (simulated)", new[] { "error CS0103 (simulated)" })
                : new AdmissionCompileResult(true, false, "compiled (fake)"));
        }
    }

    /// <summary>A world with a fixed fingerprint; the mechanism catalog is "loaded" while its package directory exists.</summary>
    internal sealed class FakeCatalog : IAdmissionCatalog
    {
        private readonly AdapterTestBed _bed;

        public FakeCatalog(AdapterTestBed bed)
        {
            _bed = bed;
        }

        public string World { get; set; } = new string('a', 64);

        public string? WorldFingerprint(out string? problem)
        {
            problem = null;
            return World;
        }

        public string? MechanismFingerprint(string catalogType, out string? problem)
        {
            problem = null;
            if (catalogType == AdapterTestBed.CatalogType && Directory.Exists(Path.Combine(_bed.PackagesRoot, AdapterTestBed.Package)))
            {
                return AdapterTestBed.MechanismFingerprint;
            }

            problem = catalogType + " is not loaded";
            return null;
        }
    }

    internal sealed class FakeCapture : IAdmissionCapture
    {
        public List<string> Captured { get; } = new List<string>();

        public List<string> Restored { get; } = new List<string>();

        public bool TryCapture(string slot, out string? problem)
        {
            if (!Captured.Contains(slot)) Captured.Add(slot);
            problem = null;
            return true;
        }

        public bool TryRestore(string slot, out string? problem)
        {
            Restored.Add(slot);
            problem = null;
            return true;
        }
    }

    internal sealed class FakeChecker : IAdmissionChecker
    {
        public int Runs { get; private set; }

        public bool Check(string packageDirectory, string package, out string detail)
        {
            Runs++;
            detail = Directory.Exists(packageDirectory) ? "fake checkers clean" : "package missing";
            return Directory.Exists(packageDirectory);
        }
    }

    internal sealed class AdapterTestBed : IDisposable, IStageService
    {
        public const string Package = "com.example.p24.pressureplate";
        public const string CatalogType = "Example.P24.Generated.PlateCatalog";
        public static readonly string MechanismFingerprint = new string('b', 64);

        public AdapterTestBed()
        {
            string leaf = Guid.NewGuid().ToString("N").Substring(0, 10);
            Root = Path.Combine(Path.GetTempPath(), "gcstudio-r2-g2-" + leaf);
            StateRoot = Path.Combine(Root, "state");
            PackagesRoot = Path.Combine(Root, "packages");
            Directory.CreateDirectory(StateRoot);
            Directory.CreateDirectory(PackagesRoot);
            Log = new MemoryLog();
            Runtime = StudioRuntime.Create(new StudioRuntimeOptions
            {
                Paths = new StudioPaths(Directory.GetParent(Application.dataPath)!.FullName, StateRoot, "p24-test"),
                Log = Log,
                TypeSource = () => Array.Empty<Type>(),
                ToolMethodSource = AdmissionMethods,
                SearchFolders = new[] { "Assets/Hollowmere/Tests/P2_4" },
                LoadIndexCache = false,
            });
            Compiler = new FakeCompiler();
            Catalog = new FakeCatalog(this);
            Checker = new FakeChecker();
            Admission = StageAdmission.Configure(Runtime, new AdmissionOptions
            {
                PackagesRoot = PackagesRoot,
                Compiler = Compiler,
                Catalog = Catalog,
                Checker = Checker,
                PlayModeProbe = () => false,
                StageService = this,
                ProjectId = "test-project", SourceRevision = () => "test-source", CatalogRevision = () => "test-catalog",
                SmokeTest = _ => true,

            });
        }

        public string Root { get; }

        public string StateRoot { get; }

        public string PackagesRoot { get; }

        public MemoryLog Log { get; }

        public StudioRuntime Runtime { get; }

        public FakeCompiler Compiler { get; }

        public FakeCatalog Catalog { get; }

        public FakeChecker Checker { get; }

        public StageAdmission Admission { get; }

        public string PackageDirectory => Path.Combine(PackagesRoot, Package);

        public static IEnumerable<MethodInfo> AdmissionMethods()
        {
            foreach (MethodInfo method in typeof(MechanismAdmission).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (AuthoringMetadata.Operation(method) != null)
                {
                    yield return method;
                }
            }
        }

        /// <summary>The files of the staged test package.</summary>
        public static SortedDictionary<string, byte[]> PackageFiles(string marker = "1")
        {
            SortedDictionary<string, byte[]> files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            files["package.json"] = Utf8("{\n  \"name\": \"" + Package + "\",\n  \"version\": \"0.1.0\",\n  \"displayName\": \"P2.4 test plate\",\n  \"description\": \"A test package.\",\n  \"unity\": \"6000.0\"\n}\n");
            files["Runtime/Plate.cs"] = Utf8("#nullable enable\nnamespace Example.P24\n{\n    public static class Plate\n    {\n        public const int Marker = " + marker + ";\n    }\n}\n");
            files["Runtime/Example.P24.asmdef"] = Utf8("{\n    \"name\": \"Example.P24\"\n}\n");
            return files;
        }

        public static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

        /// <summary>A deterministic gzip-compressed ustar archive of <paramref name="files"/>.</summary>
        public static byte[] TarGz(IReadOnlyDictionary<string, byte[]> files)
        {
            using (MemoryStream tar = new MemoryStream())
            {
                foreach (KeyValuePair<string, byte[]> file in files)
                {
                    byte[] header = new byte[512];
                    Put(header, 0, file.Key, 100);
                    Put(header, 100, "0000644", 8);
                    Put(header, 108, "0000000", 8);
                    Put(header, 116, "0000000", 8);
                    Put(header, 124, Convert.ToString(file.Value.Length, 8).PadLeft(11, '0'), 12);
                    Put(header, 136, "00000000000", 12);
                    header[156] = (byte)'0';
                    Put(header, 257, "ustar", 6);
                    header[263] = (byte)'0';
                    header[264] = (byte)'0';
                    for (int i = 148; i < 156; i++)
                    {
                        header[i] = (byte)' ';
                    }

                    int sum = 0;
                    foreach (byte value in header)
                    {
                        sum += value;
                    }

                    Put(header, 148, Convert.ToString(sum, 8).PadLeft(6, '0'), 7);
                    header[155] = (byte)' ';
                    tar.Write(header, 0, header.Length);
                    tar.Write(file.Value, 0, file.Value.Length);
                    int pad = (512 - (file.Value.Length % 512)) % 512;
                    tar.Write(new byte[pad], 0, pad);
                }

                tar.Write(new byte[1024], 0, 1024);
                using (MemoryStream gz = new MemoryStream())
                {
                    using (GZipStream zip = new GZipStream(gz, CompressionMode.Compress, true))
                    {
                        byte[] raw = tar.ToArray();
                        zip.Write(raw, 0, raw.Length);
                    }

                    return gz.ToArray();
                }
            }
        }

        private static void Put(byte[] header, int offset, string text, int length)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            Buffer.BlockCopy(bytes, 0, header, offset, Math.Min(bytes.Length, length));
        }

        /// <summary>A worker candidate (mechanism.propose) carrying <paramref name="archive"/> and a proposal; retained.</summary>
        public ChangeSet Candidate(byte[] archive, out string packageSha, out string proposalSha)
        {
            byte[] proposal = Utf8("{\"schema\":\"gamecore.studio.proposal/1\",\"package\":\"" + Package + "\",\"catalog\":{\"type\":\"" + CatalogType + "\"}}");
            JObject descriptor = JObject.Parse(Encoding.UTF8.GetString(proposal));
            descriptor["smokeTest"] = new JObject { ["type"] = "Trusted.GameSmoke", ["method"] = "Begin", ["steps"] = 4 };
            proposal = Utf8(descriptor.ToString(Formatting.None));
            packageSha = ContentStamp.Sha256Hex(archive);
            proposalSha = ContentStamp.Sha256Hex(proposal);
            ArtifactRef packageRef = new ArtifactRef(packageSha, "application/gzip", archive.LongLength, "package.tgz", null, "package");
            ArtifactRef proposalRef = new ArtifactRef(proposalSha, "application/json", proposal.LongLength, "proposal.json", null, "proposal");
            Runtime.Artifacts.Put(archive, packageRef);
            Runtime.Artifacts.Put(proposal, proposalRef);
            JObject args = new JObject
            {
                ["description"] = "A pressure plate (test).",
                ["package"] = new JObject { ["artifact"] = "sha256:" + packageSha },
                ["proposal"] = new JObject { ["artifact"] = "sha256:" + proposalSha },
                ["stageInputs"] = new JArray("Assets/Hollowmere/World.json"),
            };
            Operation propose = new Operation("op1", BuiltInToolIds.MechanismPropose, null, args, null, null, RuntimeApply.Compile);
            return new ChangeSet(
                IdDerivation.NewChangeSetId(),
                ChangeSet.SchemaId,
                new Intent("Add a pressure plate", IntentOrigin.Agent),
                new[] { propose },
                artifacts: new[] { packageRef, proposalRef },
                requirements: new Requirements(RuntimeApply.Compile, false, true, false));
        }

        /// <summary>A verdict as the staging lane writes it (canonical JSON, keys sorted).</summary>
        public byte[] Verdict(string changeSetId, string packageSha, string proposalSha, IReadOnlyDictionary<string, byte[]> stagedFiles, bool pass = true)
        {
            JArray files = new JArray();
            foreach (KeyValuePair<string, byte[]> file in stagedFiles)
            {
                files.Add(new JObject { ["bytes"] = file.Value.Length, ["path"] = file.Key, ["sha256"] = ContentStamp.Sha256Hex(file.Value) });
            }

            JArray steps = new JArray();
            foreach (string id in new[] { "scan", "checkers", "dotnet", "unity-editmode", "playmode-smoke", "determinism", "budget" })
            {
                steps.Add(new JObject { ["durationMs"] = 10, ["id"] = id, ["status"] = !pass && id == "unity-editmode" ? "fail" : "pass" });
            }

            JObject verdict = new JObject
            {
                ["jobId"] = "test-job", ["projectId"] = "test-project", ["sourceRevision"] = "test-source",
                ["catalogRevision"] = "test-catalog", ["confinement"] = "docker", ["coldCache"] = false,
                ["artifacts"] = new JArray(
                    new JObject { ["role"] = "package", ["sha256"] = packageSha },
                    new JObject { ["role"] = "proposal", ["sha256"] = proposalSha }),
                ["budgetMs"] = 360000,
                ["catalogDelta"] = new JObject
                {
                    ["mechanisms"] = new JArray(new JObject { ["catalogType"] = CatalogType, ["fingerprint"] = MechanismFingerprint, ["package"] = Package }),
                    ["predicted"] = CatalogSet.Combine(Catalog.World, new[] { MechanismFingerprint }),
                    ["world"] = Catalog.World,
                },
                ["changeSetId"] = changeSetId,
                ["createdAt"] = 1,
                ["durationMs"] = 70,
                ["files"] = files,
                ["forbiddenHits"] = new JArray(),
                ["package"] = Package,
                ["pass"] = pass,
                ["runner"] = new JObject { ["version"] = "test" },
                ["schema"] = StageVerdict.SchemaId,
                ["slot"] = "cs-test",
                ["steps"] = steps,
            };
            return Utf8(verdict.ToString(Formatting.None));
        }

        public SignedVerdict? Issued { get; set; }
        public bool Verify { get; set; } = true;
        public Task<string> RequestStage(StageCandidateRequest request) => Task.FromResult("test-job");
        public Task<SignedVerdict> GetVerdict(string jobId) => Task.FromResult(Issued!);
        public Task<StageVerification> VerifyVerdict(string jobId, StageVerificationRequest request)
            => Task.FromResult(new StageVerification(Verify, jobId));

        public StageCandidateRequest Request(ChangeSet candidate) => Admission.BuildStageRequest(candidate, "games/hollowmere");
        public void Trust(ChangeSet candidate, byte[] verdict)
        {
            Issued = new SignedVerdict("test-job", "companion-test-signature", JObject.Parse(Encoding.UTF8.GetString(verdict)));
            Admission.FetchVerdict("test-job", Request(candidate)).GetAwaiter().GetResult();
        }
        public AdmissionResult Admit(ChangeSet candidate, byte[]? verdict = null, bool captureAndStop = false)
        {
            if (verdict != null)
            {
                try { Trust(candidate, verdict); }
                catch (InvalidOperationException error)
                {
                    Runtime.Journal.Write(candidate.WithState(ChangeSetState.Rejected));
                    string reason = error.Message.Split(':')[0];
                    Runtime.Journal.Write(StageAdmission.WithScenario(candidate.WithState(ChangeSetState.Rejected), StageAdmission.VerdictScenario, ScenarioStatus.Fail, reason));
                    return new AdmissionResult(candidate.Id, AdmissionOutcome.Refused, error.Message) { Reason = reason };
                }
            }
            return Admission.Admit(candidate, captureAndStop: captureAndStop);
        }

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
    }
}
