#nullable enable
// GameCore.Studio.Edit - the admission tools of the staging lane (P2.4, docs/studio/03 s5/s8).
//
//   mechanism.admit   installs a staged mechanism package (Mechanism tier, Compile): its validator RequiresStageVerdict
//                     refuses the operation unless a passing stage verdict for exactly this change set's artifacts is
//                     retained; the package files are written from the retained archive (never from anywhere else);
//                     the asset-level inverse is mechanism.remove. Only StageAdmission builds admit operations, from
//                     a worker's mechanism.propose candidate (03 s5: "only the staging lane turns it into admit").
//   mechanism.remove  removes an admitted package directory (and its manifest entry under the shared policy); its
//                     inverse re-admits from the same retained artifacts, so redo/undo stay verified.
// Both write files on disk, outside AssetDatabase editing; recompiling and verifying the live catalog is
// StageAdmission's job (compile, domain reload, checkers, re-bake, catalog-set hash).
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Edit
{
    /// <summary>The <c>RequiresStageVerdict</c> validator (03 s8): a passing verdict with a sha256 match per code artifact.</summary>
    [AuthorValidator(MechanismAdmission.ValidatorId, Codes = new[] { DiagnosticCodes.StageFailed })]
    public sealed class RequiresStageVerdict : IOperationValidator
    {
        public IEnumerable<Diagnostic> Validate(EditContext context)
        {
            MechanismAdmission.Evaluation evaluation = MechanismAdmission.Evaluate(context);
            if (evaluation.Reason != null)
            {
                yield return Diagnostic.AtOperation(
                    DiagnosticCodes.StageFailed,
                    context.Operation.OpId,
                    evaluation.Message,
                    "Stage the change set (POST /v1/stage) and admit it with the passing verdict.",
                    new JObject { ["reason"] = evaluation.Reason, ["validator"] = MechanismAdmission.ValidatorId });
            }
        }
    }

    /// <summary>The admission tools and their shared verdict evaluation.</summary>
    public static class MechanismAdmission
    {
        public const string AdmitTool = "mechanism.admit";
        public const string RemoveTool = "mechanism.remove";
        public const string ValidatorId = "RequiresStageVerdict";
        public const string EmbeddedPolicy = "embedded";
        public const string SharedPolicy = "shared";

        /// <summary>What the verdict check found for one admit operation.</summary>
        public sealed class Evaluation
        {
            public string? Reason { get; internal set; }

            public string Message { get; internal set; } = string.Empty;

            public StageVerdict? Verdict { get; internal set; }

            public PackageArchive? Archive { get; internal set; }

            public string PackageSha { get; internal set; } = string.Empty;

            public string? ProposalSha { get; internal set; }

            public string? PackageName => Archive?.PackageName;
        }

        /// <summary>Evaluates an admit operation's verdict against its artifacts (validator and apply share this).</summary>
        public static Evaluation Evaluate(EditContext context)
        {
            Evaluation result = new Evaluation();
            string? package = Digest(context.Arg("package"));
            string? verdictDigest = Digest(context.Arg("verdict"));
            result.ProposalSha = Digest(context.Arg("proposal"));
            if (package == null)
            {
                result.Reason = VerdictReasons.Mismatch;
                result.Message = "mechanism.admit needs the package archive as { \"artifact\": \"sha256:...\" }.";
                return result;
            }

            result.PackageSha = package;
            if (verdictDigest == null || !context.Artifacts.Has(verdictDigest))
            {
                result.Reason = VerdictReasons.Missing;
                result.Message = "No stage verdict is retained for change set " + context.ChangeSet.Id + "; stage it first (POST /v1/stage).";
                return result;
            }

            StageVerdict? verdict;
            try
            {
                verdict = StageVerdict.Parse(context.Artifacts.Read(verdictDigest), out string? problem);
                if (verdict == null)
                {
                    result.Reason = VerdictReasons.Missing;
                    result.Message = "The retained verdict sha256:" + verdictDigest + " is not a stage verdict: " + problem + ".";
                    return result;
                }
            }
            catch (ArtifactStoreException error)
            {
                result.Reason = VerdictReasons.Missing;
                result.Message = error.Message;
                return result;
            }

            result.Verdict = verdict;
            PackageArchive? archive = null;
            if (context.Artifacts.Has(package))
            {
                try
                {
                    archive = PackageArchive.Read(context.Artifacts.Read(package), out string? problem);
                    if (archive == null)
                    {
                        result.Reason = VerdictReasons.Mismatch;
                        result.Message = "The package artifact is not a package archive: " + problem + ".";
                        return result;
                    }
                }
                catch (ArtifactStoreException error)
                {
                    result.Reason = VerdictReasons.Mismatch;
                    result.Message = error.Message;
                    return result;
                }
            }
            else
            {
                result.Reason = VerdictReasons.Mismatch;
                result.Message = "The package artifact sha256:" + package + " is not retained.";
                return result;
            }

            result.Archive = archive;
            // Journal replays (redo, undo of a removal) run as internal change sets linked to the admitted one.
            string owner = context.ChangeSet.Intent.Origin == IntentOrigin.Replay && context.ChangeSet.Links?.Parent != null
                ? context.ChangeSet.Links!.Parent!
                : context.ChangeSet.Id;
            string? reason = VerdictCheck.Check(verdict, owner, package, result.ProposalSha, archive.Digests, out string message);
            if (reason == null && !string.Equals(verdict.Package, archive.PackageName, StringComparison.Ordinal))
            {
                reason = VerdictReasons.Mismatch;
                message = "The verdict is for package " + verdict.Package + ", the archive holds " + archive.PackageName + ".";
            }

            result.Reason = reason;
            result.Message = message;
            return result;
        }

        /// <summary><c>mechanism.admit</c>: installs a package the staging lane passed.</summary>
        [AuthorOperation(AdmitTool,
            Tier = ToolTier.Mechanism,
            RuntimeApplicability = RuntimeApply.Compile,
            Validator = typeof(RequiresStageVerdict),
            Doc = "Install a staged mechanism package after a passing stage verdict (RequiresStageVerdict). Built by the staging lane from a mechanism.propose candidate; recompiles the project.")]
        public static OperationResult Admit(
            EditContext context,
            [AuthorArg(Type = ValueTypes.Artifact, Doc = "The package archive (.tgz) the verdict covers.")] JObject package,
            [AuthorArg(Type = ValueTypes.Artifact, Required = false, Doc = "The proposal (proposal.json) the verdict covers.")] JObject? proposal = null,
            [AuthorArg(Type = ValueTypes.Artifact, Required = false, Doc = "The stage verdict (gamecore.studio.stage-verdict/1); without it RequiresStageVerdict refuses the operation.")] JObject? verdict = null,
            [AuthorArg(Required = false, Doc = "embedded (games/<game>/Packages, default) or shared (repository Packages/ + manifest entry).")] string? policy = null)
        {
            Evaluation evaluation = Evaluate(context);
            if (evaluation.Reason != null || evaluation.Archive == null)
            {
                return OperationResult.Refused(DiagnosticCodes.StageFailed, evaluation.Message);
            }

            StageAdmission admission = StageAdmission.Of(context.Runtime);
            string name = evaluation.Archive.PackageName!;
            bool shared = string.Equals(policy, SharedPolicy, StringComparison.Ordinal);
            if (policy != null && !shared && !string.Equals(policy, EmbeddedPolicy, StringComparison.Ordinal))
            {
                return OperationResult.Refused(DiagnosticCodes.InvalidArgs, "policy is 'embedded' or 'shared', not '" + policy + "'.");
            }

            string directory = admission.PackageDirectory(name, shared);
            if (Directory.Exists(directory) || File.Exists(directory))
            {
                return OperationResult.Refused(DiagnosticCodes.Conflict, "The package directory " + directory + " already exists; remove the installed package first.");
            }

            bool manifestEntry = false;
            string? failure = null;
            context.OutsideAssetEditing(() =>
            {
                failure = WriteFiles(directory, evaluation.Archive);
                if (failure == null && shared)
                {
                    failure = admission.AddManifestEntry(name, directory);
                    manifestEntry = failure == null;
                }
            });
            if (failure != null)
            {
                TryDelete(directory);
                return OperationResult.Failed(DiagnosticCodes.Refused, "Admitting " + name + " failed: " + failure);
            }

            string relative = admission.ProjectRelative(directory);
            JObject args = new JObject
            {
                ["package"] = name,
                ["directory"] = relative,
                ["archive"] = new JObject { ["artifact"] = ContentStamp.Prefix + evaluation.PackageSha },
                ["verdict"] = new JObject { ["artifact"] = evaluation.Verdict!.Reference },
            };
            if (manifestEntry)
            {
                args["manifestEntry"] = true;
            }

            if (evaluation.ProposalSha != null)
            {
                args["proposal"] = new JObject { ["artifact"] = ContentStamp.Prefix + evaluation.ProposalSha };
            }

            Operation inverse = new Operation(context.Operation.OpId + ".remove", RemoveTool, null, args, null, Preconditions.None, RuntimeApply.Compile);
            context.Log.Write(Authoring.StudioLogLevel.Info, "stage", "admitted " + name + " (" + evaluation.Archive.Files.Count + " files) into " + relative + " with verdict " + evaluation.Verdict.Digest);
            return OperationResult.Applied(new JObject
                {
                    ["package"] = name,
                    ["directory"] = relative,
                    ["files"] = evaluation.Archive.Files.Count,
                    ["verdict"] = evaluation.Verdict.Reference,
                    ["slot"] = evaluation.Verdict.Slot,
                })
                .WithAssetLevelInverse(inverse)
                .WithReplay(new JObject { ["directory"] = relative });
        }

        /// <summary><c>mechanism.remove</c>: removes an admitted mechanism package.</summary>
        [AuthorOperation(RemoveTool,
            Tier = ToolTier.Mechanism,
            RuntimeApplicability = RuntimeApply.Compile,
            Doc = "Remove an admitted mechanism package (its directory, and its manifest entry under the shared policy). Recompiles the project.")]
        public static OperationResult Remove(
            EditContext context,
            [AuthorArg(Doc = "The package name.")] string package,
            [AuthorArg(Required = false, Doc = "The package directory, relative to the project (default: the embedded location).")] string? directory = null,
            [AuthorArg(Required = false, Doc = "The package was added to the project manifest (shared policy).")] bool manifestEntry = false,
            [AuthorArg(Type = ValueTypes.Artifact, Required = false, Doc = "The admitted archive (makes the removal undoable).")] JObject? archive = null,
            [AuthorArg(Type = ValueTypes.Artifact, Required = false, Doc = "The verdict that admitted it.")] JObject? verdict = null,
            [AuthorArg(Type = ValueTypes.Artifact, Required = false, Doc = "The admitted proposal.")] JObject? proposal = null)
        {
            StageAdmission admission = StageAdmission.Of(context.Runtime);
            string path = directory == null ? admission.PackageDirectory(package, manifestEntry) : admission.FromProjectRelative(directory);
            string? unsafeReason = admission.CheckRemovable(path, package);
            if (unsafeReason != null)
            {
                return OperationResult.Refused(DiagnosticCodes.Refused, unsafeReason);
            }

            string? failure = null;
            context.OutsideAssetEditing(() =>
            {
                try
                {
                    StageAdmission.DeleteDirectory(path);
                    string meta = path.TrimEnd('/', '\\') + ".meta";
                    if (File.Exists(meta))
                    {
                        File.Delete(meta);
                    }
                }
                catch (IOException error)
                {
                    failure = error.Message;
                }
                catch (UnauthorizedAccessException error)
                {
                    failure = error.Message;
                }

                if (failure == null && manifestEntry)
                {
                    failure = admission.RemoveManifestEntry(package);
                }
            });
            if (failure != null)
            {
                return OperationResult.Failed(DiagnosticCodes.Refused, "Removing " + package + " failed: " + failure);
            }

            OperationResult result = OperationResult.Applied(new JObject { ["package"] = package, ["directory"] = admission.ProjectRelative(path) });
            if (archive != null && verdict != null)
            {
                JObject args = new JObject { ["package"] = archive.DeepClone(), ["verdict"] = verdict.DeepClone() };
                if (proposal != null)
                {
                    args["proposal"] = proposal.DeepClone();
                }

                if (manifestEntry)
                {
                    args["policy"] = SharedPolicy;
                }

                result.WithAssetLevelInverse(new Operation(context.Operation.OpId + ".admit", AdmitTool, null, args, null, Preconditions.None, RuntimeApply.Compile));
            }

            context.Log.Write(Authoring.StudioLogLevel.Info, "stage", "removed " + package + " from " + admission.ProjectRelative(path));
            return result;
        }

        internal static string? Digest(JToken? value)
        {
            string? reference = value is JObject artifactObject ? (string?)artifactObject["artifact"] : (value?.Type == JTokenType.String ? value.Value<string>() : null);
            if (reference == null)
            {
                return null;
            }

            string text = reference.StartsWith(ContentStamp.Prefix, StringComparison.Ordinal) ? reference.Substring(ContentStamp.Prefix.Length) : reference;
            return ContentStamp.IsValidHex(text) ? text : null;
        }

        private static string? WriteFiles(string directory, PackageArchive archive)
        {
            string parent = Path.GetDirectoryName(directory.TrimEnd('/', '\\')) ?? directory;
            string staging = Path.Combine(parent, "." + Path.GetFileName(directory.TrimEnd('/', '\\')) + ".admitting");
            try
            {
                if (Directory.Exists(staging))
                {
                    StageAdmission.DeleteDirectory(staging);
                }

                string root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
                foreach (KeyValuePair<string, byte[]> file in archive.Files)
                {
                    string target = Path.GetFullPath(Path.Combine(staging, file.Key.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(root, StringComparison.Ordinal))
                    {
                        return "the archive entry " + file.Key + " leaves the package directory";
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.WriteAllBytes(target, file.Value);
                }

                Directory.CreateDirectory(parent);
                Directory.Move(staging, directory);
                return null;
            }
            catch (IOException error)
            {
                TryDelete(staging);
                return error.Message;
            }
            catch (UnauthorizedAccessException error)
            {
                TryDelete(staging);
                return error.Message;
            }
        }

        private static void TryDelete(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    StageAdmission.DeleteDirectory(directory);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
