#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Edit
{
    public sealed partial class StageAdmission
    {
        public AdmissionResult Admit(ChangeSet candidate, byte[]? verdictBytes = null, bool captureAndStop = false)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (ReadPending(candidate.Id) != null) return Resume(candidate.Id);
            if (HasOtherPending(candidate.Id)) return Refuse(candidate.Id, "admission_busy", "Another admission or removal is pending.");
            if (_runtime.Journal.Read(candidate.Id)?.EffectiveState == ChangeSetState.Applied)
                return Refuse(candidate.Id, "already_applied", "The admission is already applied.");
            bool redo = _runtime.Journal.Read(candidate.Id)?.EffectiveState == ChangeSetState.Undone;
            _runtime.Journal.Write(candidate.WithState(ChangeSetState.Candidate));
            if (verdictBytes != null)
            {
                RecordVerdict(verdictBytes);
                return Reject(candidate.Id, VerdictReasons.Untrusted, "Artifact verdict bytes cannot authorize admission. Fetch the job through the companion.");
            }
            StageVerdict? verdict = VerdictOf(candidate.Id);
            PackageArchive? archive;
            try
            {
                archive = CheckCandidate(candidate, verdict, out string? reason, out string message);
                if (reason != null) return Reject(candidate.Id, reason, message);
            }
            catch (Exception error) when (error is ArgumentException || error is IOException || error is JsonException || error is ArtifactStoreException)
            {
                return Reject(candidate.Id, "invalid_candidate", "Candidate artifacts or stage inputs are invalid.");
            }
            if (IsPlaying && !captureAndStop) return Reject(candidate.Id, "play_mode", "Stop Play Mode or choose capture & stop.");
            if (IsPlaying && Options.Capture == null) return Reject(candidate.Id, "capture_failed", "The game capture service is unavailable.");
            string package = archive!.PackageName!;
            string directory = PackageDirectory(package, Options.SharedPolicy);
            if (System.IO.Directory.Exists(directory) || File.Exists(directory)) return Reject(candidate.Id, "package_exists", "The destination package already exists.");
            StageDataPaths.ContainedFile(Path.GetDirectoryName(directory)!, Path.GetFileName(directory));
            // Entry.Verify opens authored scenes. In Play, retain capture/stop first and witness the
            // rollback catalog durably in Edit mode, before installing any candidate bytes.
            string? before = IsPlaying ? null : LiveHash(null, out _);
            if (!IsPlaying && before == null) return Reject(candidate.Id, "catalog_missing", "The current world catalog cannot be captured for verified rollback.");
            var pending = new JObject
            {
                ["schema"] = "gamecore.studio.admission/2", ["changeSetId"] = candidate.Id,
                ["phase"] = IsPlaying ? "capture" : "pending", ["action"] = "admit",
                ["package"] = package, ["directory"] = ProjectRelative(directory), ["shared"] = Options.SharedPolicy,
                ["before"] = before, ["verdict"] = verdict!.Digest,
                ["startedMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["candidate"] = JObject.Parse(StudioJson.Serialize(candidate)),
                ["catalogTypes"] = new JArray(System.Linq.Enumerable.Select(verdict.Mechanisms, m => m.Key)),
                ["packagesRoot"] = Options.PackagesRoot, ["restoreRemoved"] = redo,
            };
            if (IsPlaying) pending["captureSlot"] = "admit-" + candidate.Id.ToLowerInvariant();
            if (Options.SharedPolicy)
            {
                string manifest = Path.Combine(_runtime.Paths.ProjectRoot, "Packages", "manifest.json");
                pending["manifestBefore"] = Convert.ToBase64String(File.ReadAllBytes(manifest));
            }
            _runtime.Journal.Write(ToAdmission(candidate, verdict).WithState(ChangeSetState.Interrupted));
            Checkpoint(candidate.Id, pending, (string)pending["phase"]!, IsPlaying ? AdmissionFaultPoint.Capture : AdmissionFaultPoint.Pending);
            return Resume(candidate.Id);
        }

        private AdmissionResult Reject(string id, string reason, string message)
        {
            ChangeSet? entry = _runtime.Journal.Read(id);
            if (entry != null) _runtime.Journal.Write(entry.WithState(ChangeSetState.Rejected));
            return Refuse(id, reason, message);
        }

        private PackageArchive? CheckCandidate(ChangeSet candidate, StageVerdict? verdict, out string? reason, out string message, bool checkCurrentContext = true)
        {
            reason = VerdictReasons.Missing;
            message = "No authenticated companion verdict is available.";
            if (verdict == null) return null;
            if (checkCurrentContext && (Options.ProjectId != (string?)verdict.Document["projectId"]
                || Options.SourceRevision?.Invoke() != (string?)verdict.Document["sourceRevision"]
                || Options.CatalogRevision?.Invoke() != (string?)verdict.Document["catalogRevision"]))
            {
                reason = VerdictReasons.Mismatch;
                message = "The current project, source or catalog revision differs from the verified stage request.";
                return null;
            }
            Operation op = ProposalOperation(candidate);
            string? packageSha = MechanismAdmission.Digest(op.Args?["package"]);
            string? proposalSha = MechanismAdmission.Digest(op.Args?["proposal"]);
            if (packageSha == null || proposalSha == null) { reason = VerdictReasons.Mismatch; return null; }
            StageDataPaths.ValidateProposal(JObject.Parse(Encoding.UTF8.GetString(_runtime.Artifacts.Read(proposalSha))));
            foreach (JToken input in op.Args?["stageInputs"] as JArray ?? new JArray()) StageDataPaths.ValidateInput(input.Value<string>() ?? "");
            PackageArchive? archive = PackageArchive.Read(_runtime.Artifacts.Read(packageSha), out string? problem);
            if (archive == null) { reason = VerdictReasons.Mismatch; message = problem ?? "Invalid archive."; return null; }
            verdict.HostAllowed = Options.AllowHostConfinement;
            reason = VerdictCheck.Check(verdict, candidate.Id, packageSha, proposalSha, archive.Digests, out message);
            if (reason == null && (archive.PackageName != verdict.Package || Reserved(verdict.Package)
                || !System.Text.RegularExpressions.Regex.IsMatch(verdict.Package, @"^[a-z0-9]+(?:[.-][a-z0-9]+)+$")))
            { reason = VerdictReasons.Mismatch; message = "Reserved or mismatched package name."; }
            return archive;
        }

        private static bool Reserved(string package) => package.StartsWith("com.gamecore.", StringComparison.OrdinalIgnoreCase)
            || package.StartsWith("com.unity.", StringComparison.OrdinalIgnoreCase);

        private void Checkpoint(string id, JObject pending, string phase, AdmissionFaultPoint point)
        {
            if (phase != "smoke-pending") StopSmokePolling(id);
            pending["phase"] = phase;
            WritePending(id, pending);
            ChangeSet? entry = _runtime.Journal.Read(id);
            if (entry != null) _runtime.Journal.Write(WithScenario(entry.WithState(ChangeSetState.Interrupted),
                (string?)pending["action"] == "undo" ? UndoScenario : AdmissionScenario, ScenarioStatus.Pending, phase));
            Fault(point, id);
        }

        public IReadOnlyList<AdmissionResult> ResumePending()
        {
            var results = new List<AdmissionResult>();
            string legacyRoot = Path.Combine(_runtime.Paths.LibraryRoot, "stage");
            foreach (string root in new[] { StateRoot, legacyRoot })
            {
                if (!System.IO.Directory.Exists(root)) continue;
                var legacy = new List<string>(System.IO.Directory.GetFiles(root, "admit-after-play-cs_*.json"));
                if (root == legacyRoot) legacy.AddRange(System.IO.Directory.GetFiles(root, "pending-cs_*.json"));
                foreach (string file in legacy)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    string id = name.Substring(name.StartsWith("pending-", StringComparison.Ordinal) ? "pending-".Length : "admit-after-play-".Length);
                    if (ReadPending(id) == null) results.Add(new AdmissionResult(id, AdmissionOutcome.Refused,
                        "Legacy record lacks authenticated job provenance; restage and explicitly admit.") { Reason = "legacy_admission_incomplete" });
                }
            }
            if (!System.IO.Directory.Exists(StateRoot)) return results;
            foreach (string file in System.IO.Directory.GetFiles(StateRoot, "pending-cs_*.json"))
                results.Add(Resume(Path.GetFileNameWithoutExtension(file).Substring("pending-".Length)));
            return results;
        }

        public AdmissionResult Finalize(string changeSetId) => Resume(changeSetId);

        public AdmissionResult Resume(string id) => Resume(id, false);

        private AdmissionResult Resume(string id, bool smokeFrame)
        {
            JObject? p = ReadPending(id);
            if (p == null) return Refuse(id, "pending_missing", "No pending admission.");
            if ((string?)p["schema"] != "gamecore.studio.admission/2") return Waiting(id, "Legacy record needs explicit restaging.");
            // The original destination is pinned by the record; never recompute from new session defaults.
            string directory = FromProjectRelative((string)p["directory"]!);
            string package = (string)p["package"]!;
            if (Reserved(package) || Path.GetFileName(directory) != package) return Waiting(id, "Invalid owned package path.");
            Options.PackagesRoot = (string?)p["packagesRoot"];
            try
            {
                while (true)
                {
                    string phase = (string)p["phase"]!;
                    if (phase == "undo-pending" || phase == "rollback-pending")
                    {
                        if (IsPlaying)
                        {
                            (Options.StopPlayMode ?? (() => UnityEditor.EditorApplication.isPlaying = false))();
                            if (IsPlaying) return Waiting(id, "Stopping Play Mode before package removal.");
                        }
                        RemoveOwned(id, p, directory);
                        Checkpoint(id, p, "undo-compile", AdmissionFaultPoint.UndoCompile);
                        continue;
                    }
                    if (phase == "undo-compile") return CompilePending(id, p, true);
                    if (phase == "undo-reload") { Checkpoint(id, p, "undo-verify", AdmissionFaultPoint.UndoVerify); continue; }
                    if (phase == "undo-verify") return VerifyRemoval(id, p);
                    if (phase == "smoke-pending")
                    {
                        if (SmokeBudgetExhausted(p)) return FailSmoke(id, p, "smoke_budget_exhausted");
                        ScheduleSmokePolling(id);
                        if (!smokeFrame) return Waiting(id, "Awaiting the next admission smoke-test frame.");
                        // Charge the frame before game code, including frames waiting for trusted services.
                        p["smokeFrames"] = (int)p["smokeFrames"]! + 1;
                        WritePending(id, p);
                    }
                    StageVerdict? verdict = VerdictOf(id);
                    if (verdict == null) return SmokeWaiting(id, p, "Awaiting authenticated companion verdict refresh.");
                    if ((string?)p["verdict"] != verdict.Digest) return SmokeWaiting(id, p, "The job verdict changed; recovery is held for review.");
                    ChangeSet candidate = StudioJson.Deserialize<ChangeSet>(p["candidate"]!.ToString(Formatting.None));
                    PackageArchive? archive = CheckCandidate(candidate, verdict, out string? refusal, out string message, phase == "pending" || phase == "capture" || phase == "stop-play");
                    if (refusal != null) return SmokeWaiting(id, p, message);
                    switch (phase)
                    {
                        case "capture":
                            if (Options.Capture == null) return Waiting(id, "Awaiting game capture service registration.");
                            if (!Options.Capture.TryCapture((string)p["captureSlot"]!, out _)) return BeginRollback(id, p, "capture_failed");
                            Fault(AdmissionFaultPoint.AfterCapture, id);
                            Checkpoint(id, p, "stop-play", AdmissionFaultPoint.StopPlay);
                            continue;
                        case "stop-play":
                            (Options.StopPlayMode ?? (() => UnityEditor.EditorApplication.isPlaying = false))();
                            if (IsPlaying) return Waiting(id, "Waiting for Play Mode to stop.");
                            Checkpoint(id, p, "pending", AdmissionFaultPoint.Pending);
                            continue;
                        case "pending":
                            if (IsPlaying) return Waiting(id, "Stop Play Mode before installing the package.");
                            if (p["before"]?.Type != JTokenType.String)
                            {
                                string? baseline = LiveHash(null, out _);
                                if (baseline == null) return Waiting(id, "The current world catalog cannot be captured for verified rollback.");
                                p["before"] = baseline;
                                Checkpoint(id, p, "pending", AdmissionFaultPoint.Pending);
                            }
                            if (!System.IO.Directory.Exists(directory))
                            {
                                string removed = Path.Combine(StateRoot, "removed", id, "package");
                                if ((bool?)p["restoreRemoved"] == true && System.IO.Directory.Exists(removed))
                                {
                                    VerifyInstalled(removed, archive!);
                                    System.IO.Directory.Move(removed, directory);
                                    if (File.Exists(removed + ".meta")) File.Move(removed + ".meta", directory + ".meta");
                                }
                                else MechanismAdmission.Install(directory, archive!);
                            }
                            else VerifyInstalled(directory, archive!);
                            if ((bool?)p["shared"] == true && AddManifestEntry(package, directory) != null) return BeginRollback(id, p, "manifest_failed");
                            Fault(AdmissionFaultPoint.AfterApply, id);
                            Checkpoint(id, p, "compile", AdmissionFaultPoint.Compile);
                            continue;
                        case "compile": return CompilePending(id, p, false);
                        case "reload": Checkpoint(id, p, "rebake", AdmissionFaultPoint.Rebake); continue;
                        case "rebake":
                            Fault(AdmissionFaultPoint.BeforeChecks, id);
                            VerifyInstalled(directory, archive!);
                            if (!Checker.Check(directory, package, out _)) return BeginRollback(id, p, "checkers_failed");
                            Fault(AdmissionFaultPoint.BeforeCatalogCheck, id);
                            string? world = Catalog.WorldFingerprint(out _);
                            if (world == null) return Waiting(id, "The world re-bake is not ready.");
                            p["world"] = world;
                            Checkpoint(id, p, "verify", AdmissionFaultPoint.Verify);
                            continue;
                        case "verify":
                            if (verdict.Mechanisms.Count == 0 || verdict.Predicted == null || verdict.World != (string?)p["world"])
                                return BeginRollback(id, p, "catalog_mismatch");
                            var fingerprints = new List<string>();
                            foreach (var mechanism in verdict.Mechanisms)
                            {
                                string? fingerprint = Catalog.MechanismFingerprint(mechanism.Key, out _);
                                if (fingerprint != mechanism.Value) return BeginRollback(id, p, "catalog_mismatch");
                                fingerprints.Add(fingerprint!);
                            }
                            if (CatalogSet.Combine(verdict.World!, fingerprints) != verdict.Predicted) return BeginRollback(id, p, "catalog_mismatch");
                            p["live"] = verdict.Predicted;
                            Checkpoint(id, p, p["captureSlot"] == null ? "smoke" : "restore-play",
                                p["captureSlot"] == null ? AdmissionFaultPoint.Smoke : AdmissionFaultPoint.RestorePlay);
                            continue;
                        case "restore-play":
                            if (!IsPlaying)
                            {
                                (Options.StartPlayMode ?? (() => UnityEditor.EditorApplication.isPlaying = true))();
                                return Waiting(id, "Re-entering Play Mode for checkpoint restore.");
                            }
                            if (Options.SessionReady?.Invoke() != true) return Waiting(id, "Awaiting game session rebinding.");
                            Checkpoint(id, p, "restore-capture", AdmissionFaultPoint.RestoreCapture);
                            continue;
                        case "restore-capture":
                            if (Options.Capture == null) return Waiting(id, "Awaiting game capture service registration.");
                            if (!Options.Capture.TryRestore((string)p["captureSlot"]!, out _)) return BeginRollback(id, p, "restore_failed");
                            Checkpoint(id, p, "smoke", AdmissionFaultPoint.Smoke);
                            continue;
                        case "smoke":
                            if (Options.SmokeTest == null && Options.PollSmokeTest == null)
                                return Waiting(id, "Awaiting the game's live proposal smoke-test adapter.");
                            if (Options.SmokeTest?.Invoke(verdict) == false) return FailSmoke(id, p, "smoke_failed");
                            if (Options.PollSmokeTest != null)
                            {
                                p["smokeStartedMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                                p["smokeFrames"] = 0;
                                p["smokeFrameBudget"] = Options.SmokeTestFrameBudget;
                                double milliseconds = Options.SmokeTestTimeoutSeconds * 1000;
                                if (Options.SmokeTestFrameBudget <= 0 || double.IsNaN(milliseconds)
                                    || double.IsInfinity(milliseconds) || milliseconds <= 0 || milliseconds >= long.MaxValue)
                                    return FailSmoke(id, p, "smoke_budget_exhausted");
                                p["smokeTimeoutMs"] = (long)Math.Ceiling(milliseconds);
                                p["smokeStatus"] = AdmissionSmokeStatus.Pending.ToString();
                                Checkpoint(id, p, "smoke-pending", AdmissionFaultPoint.SmokePending);
                                continue;
                            }
                            Checkpoint(id, p, "applied", AdmissionFaultPoint.Applied);
                            continue;
                        case "smoke-pending":
                            if (Options.PollSmokeTest == null)
                                return SmokeWaiting(id, p, "Awaiting the game's smoke polling adapter after reload.");
                            AdmissionSmokeStatus status = Options.PollSmokeTest(verdict);
                            if (status != AdmissionSmokeStatus.Pending && status != AdmissionSmokeStatus.Passed)
                                return FailSmoke(id, p, "smoke_failed");
                            // A slow callback cannot pass after its wall-clock deadline.
                            if (SmokeTimeExhausted(p)) return FailSmoke(id, p, "smoke_budget_exhausted");
                            if (status == AdmissionSmokeStatus.Pending)
                                return SmokeWaiting(id, p, "The live proposal smoke test is Pending.");
                            p["smokeStatus"] = AdmissionSmokeStatus.Passed.ToString();
                            Checkpoint(id, p, "applied", AdmissionFaultPoint.Applied);
                            continue;
                        case "applied": return CompleteAdmission(id, p, verdict);
                        default: return Waiting(id, "Unknown durable admission phase; no side effects performed.");
                    }
                }
            }
            catch (AdmissionCrashException) { throw; }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                // Do not discard recovery information if an inverse or its verification fails.
                if ((string?)p["action"] != "admit") return Waiting(id, "Package removal or verification failed; recovery remains pending.");
                if ((string?)p["phase"] == "smoke-pending") return FailSmoke(id, p, "smoke_failed");
                return BeginRollback(id, p, "fault");
            }
        }

        private readonly HashSet<string> _compiling = new HashSet<string>(StringComparer.Ordinal);

        private AdmissionResult CompilePending(string id, JObject p, bool removing)
        {
            if (_compiling.Contains(id)) return Waiting(id, "Compilation is already running.");
            Fault(AdmissionFaultPoint.BeforeCompile, id);
            _compiling.Add(id);
            AdmissionResult? immediate = null;
            Compiler.Compile((removing ? "undo " : "admit ") + id, result =>
            {
                _compiling.Remove(id);
                if (!result.Succeeded)
                {
                    immediate = removing ? Waiting(id, "Removal compile failed; recovery remains pending.") : BeginRollback(id, p, "compile_failed");
                    return;
                }
                Checkpoint(id, p, removing ? "undo-reload" : "reload", AdmissionFaultPoint.Reload);
                immediate = result.ReloadPending ? Waiting(id, "Waiting for domain reload.") : Resume(id);
            });
            return immediate ?? Waiting(id, "Waiting for compilation.");
        }

        private static void VerifyInstalled(string directory, PackageArchive archive)
        {
            foreach (var file in archive.Digests)
            {
                string path = StageDataPaths.ContainedFile(directory, file.Key);
                if (!File.Exists(path) || ContentStamp.Sha256Hex(File.ReadAllBytes(path)) != file.Value)
                    throw new IOException("installed_package_changed");
            }
            RejectLinkedDirectories(directory);
            foreach (string path in System.IO.Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
                StageDataPaths.ContainedFile(directory, relative);
                if (!archive.Digests.ContainsKey(relative) && !relative.EndsWith(".meta", StringComparison.Ordinal))
                    throw new IOException("installed_package_changed");
            }
        }

        private static void RejectLinkedDirectories(string directory)
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("package_link_forbidden");
            foreach (string child in System.IO.Directory.GetDirectories(directory)) RejectLinkedDirectories(child);
        }

        private AdmissionResult CompleteAdmission(string id, JObject p, StageVerdict verdict)
        {
            JObject admitted = ReadState("admitted.json");
            admitted[(string)p["package"]!] = new JObject
            {
                ["changeSetId"] = id, ["directory"] = p["directory"]!.DeepClone(),
                ["catalogType"] = verdict.Mechanisms[0].Key, ["fingerprint"] = verdict.Mechanisms[0].Value,
                ["record"] = p.DeepClone(),
            };
            WriteState("admitted.json", admitted);
            ChangeSet entry = _runtime.Journal.Read(id)!;
            _runtime.Journal.Write(WithScenario(entry.WithState(ChangeSetState.Applied)
                .WithTimestamps(new Timestamps(entry.Timestamps?.Requested, entry.Timestamps?.Candidate, Journal.Now())),
                AdmissionScenario, ScenarioStatus.Pass, "verified; confinement=" + verdict.Confinement + "; coldCache=" + verdict.ColdCache));
            WriteState("completed-" + id + ".json", p);
            DeletePending(id);
            var result = new AdmissionResult(id, AdmissionOutcome.Admitted, "Admitted and verified.")
            {
                Package = (string?)p["package"], Directory = (string?)p["directory"], Before = (string?)p["before"],
                Live = (string?)p["live"], Predicted = verdict.Predicted, Verdict = verdict.Reference, Slot = verdict.Slot,
                CaptureSlot = (string?)p["captureSlot"], Confinement = verdict.Confinement, ColdCache = verdict.ColdCache, Milliseconds = Elapsed(id, p),
            };
            Finished?.Invoke(result);
            return result;
        }

        public AdmissionResult Undo(string changeSetId)
        {
            if (ReadPending(changeSetId) != null)
                return new AdmissionResult(changeSetId, AdmissionOutcome.Refused, "Undo is unavailable while admission or removal is pending.")
                {
                    Reason = "admission_pending",
                    Diagnostics = new[] { PendingDiagnostic("Wait for admission to finish or request rollback.") },
                };
            if (HasOtherPending(changeSetId)) return Refuse(changeSetId, "admission_busy", "Another admission or removal is pending.");
            JObject p = ReadState("completed-" + changeSetId + ".json");
            if (_runtime.Journal.Read(changeSetId)?.EffectiveState != ChangeSetState.Applied || !p.HasValues)
                return new AdmissionResult(changeSetId, AdmissionOutcome.UndoFailed, "Not an applied admission.");
            string directory = FromProjectRelative((string)p["directory"]!);
            string? problem = CheckRemovable(directory, (string)p["package"]!);
            if (problem != null) return new AdmissionResult(changeSetId, AdmissionOutcome.UndoFailed, problem);
            p["action"] = "undo";
            Checkpoint(changeSetId, p, "undo-pending", AdmissionFaultPoint.UndoPending);
            return Resume(changeSetId);
        }

        public AdmissionResult Redo(string changeSetId)
        {
            if (ReadPending(changeSetId) != null) return Resume(changeSetId);
            if (HasOtherPending(changeSetId)) return Refuse(changeSetId, "admission_busy", "Another admission or removal is pending.");
            JObject p = ReadState("completed-" + changeSetId + ".json");
            if (_runtime.Journal.Read(changeSetId)?.EffectiveState != ChangeSetState.Undone || !p.HasValues)
                return Refuse(changeSetId, "not_undone", "Not an undone admission.");
            // The exact retained candidate is rechecked against the authenticated job before installing again.
            ChangeSet candidate = StudioJson.Deserialize<ChangeSet>(p["candidate"]!.ToString(Formatting.None));
            return Admit(candidate);
        }

        public AdmissionResult RollbackPending(string id)
        {
            JObject? p = ReadPending(id);
            return p == null ? Refuse(id, "pending_missing", "No pending admission.") : BeginRollback(id, p, "creator_rollback");
        }

        private AdmissionResult BeginRollback(string id, JObject p, string reason)
        {
            if (p["before"]?.Type != JTokenType.String)
            {
                // No catalog checkpoint means installation has never been authorized. A failed capture or
                // creator cancellation needs no package removal or compile; retain the capture for the creator.
                if (System.IO.Directory.Exists(FromProjectRelative((string)p["directory"]!)))
                    return Waiting(id, "Unexpected package before catalog verification; recovery remains pending.");
                ChangeSet entry = _runtime.Journal.Read(id)!;
                _runtime.Journal.Write(WithScenario(entry.WithState(ChangeSetState.Failed), AdmissionScenario,
                    ScenarioStatus.Fail, "cancelled before installation: " + reason));
                DeletePending(id);
                var cancelled = new AdmissionResult(id, AdmissionOutcome.RolledBack, "No package was installed.")
                { Reason = reason, CaptureSlot = (string?)p["captureSlot"] };
                Finished?.Invoke(cancelled);
                return cancelled;
            }
            p["action"] = "rollback";
            p["reason"] = reason;
            Checkpoint(id, p, "rollback-pending", AdmissionFaultPoint.UndoPending);
            return Resume(id);
        }

        private void RemoveOwned(string id, JObject p, string directory)
        {
            // Ownership comes from the durable pre-install record and project journal, never a caller path.
            if (_runtime.Journal.Read(id) == null || (string?)p["changeSetId"] != id) throw new IOException("package_not_admitted");
            StageDataPaths.ContainedFile(Path.GetDirectoryName(directory)!, Path.GetFileName(directory));
            string removed = Path.Combine(StateRoot, "removed", id, "package");
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(removed)!);
            if (System.IO.Directory.Exists(directory))
            {
                ChangeSet candidate = StudioJson.Deserialize<ChangeSet>(p["candidate"]!.ToString(Formatting.None));
                string sha = MechanismAdmission.Digest(ProposalOperation(candidate).Args?["package"])!;
                PackageArchive archive = PackageArchive.Read(_runtime.Artifacts.Read(sha), out _)!;
                VerifyInstalled(directory, archive);
                if (System.IO.Directory.Exists(removed)) DeleteDirectory(removed);
                System.IO.Directory.Move(directory, removed);
            }
            if (File.Exists(directory + ".meta"))
            {
                string meta = removed + ".meta";
                if (File.Exists(meta)) File.Delete(meta);
                File.Move(directory + ".meta", meta);
            }
            if ((bool?)p["shared"] == true)
            {
                // Restore just this dependency, preserving unrelated concurrent manifest edits.
                JObject before = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String((string)p["manifestBefore"]!)));
                string manifestPath = Path.Combine(_runtime.Paths.ProjectRoot, "Packages", "manifest.json");
                JObject current = JObject.Parse(File.ReadAllText(manifestPath));
                string package = (string)p["package"]!;
                JObject dependencies = (JObject)current["dependencies"]!;
                JToken? old = before["dependencies"]?[package];
                if (old == null) dependencies.Remove(package); else dependencies[package] = old.DeepClone();
                WriteDurable(manifestPath, current.ToString(Formatting.Indented) + "\n");
            }
        }

        private AdmissionResult VerifyRemoval(string id, JObject p)
        {
            string directory = FromProjectRelative((string)p["directory"]!);
            if (System.IO.Directory.Exists(directory)) return Waiting(id, "Package removal is incomplete.");
            foreach (JToken catalogType in p["catalogTypes"] as JArray ?? new JArray())
                    if (Catalog.MechanismFingerprint(catalogType.Value<string>()!, out _) != null) return Waiting(id, "Removed catalog is still loaded.");
            JObject admitted = ReadState("admitted.json");
            admitted.Remove((string)p["package"]!);
            string? live = LiveHash(admitted, out _);
            if (live == null || live != (string?)p["before"]) return Waiting(id, "Removal catalog verification failed; recovery remains pending.");
            WriteState("admitted.json", admitted);
            bool undo = (string?)p["action"] == "undo";
            ChangeSet entry = _runtime.Journal.Read(id)!;
            _runtime.Journal.Write(WithScenario(entry.WithState(undo ? ChangeSetState.Undone : ChangeSetState.Failed),
                undo ? UndoScenario : AdmissionScenario, undo ? ScenarioStatus.Pass : ScenarioStatus.Fail,
                undo ? "verified removal" : "rolled back: " + (string?)p["reason"]));
            DeletePending(id);
            var result = new AdmissionResult(id, undo ? AdmissionOutcome.Undone : AdmissionOutcome.RolledBack, "Removal compiled and catalog verified.")
            { Reason = (string?)p["reason"], Package = (string?)p["package"], Before = (string?)p["before"], Live = live, CaptureSlot = (string?)p["captureSlot"] };
            Finished?.Invoke(result);
            return result;
        }

        private bool HasOtherPending(string id)
        {
            if (!System.IO.Directory.Exists(StateRoot)) return false;
            foreach (string path in System.IO.Directory.GetFiles(StateRoot, "pending-cs_*.json"))
                if (path != PendingPath(id)) return true;
            return false;
        }

        private static AdmissionResult Waiting(string id, string detail) => new AdmissionResult(id, AdmissionOutcome.Pending, detail);

        public bool RestoreCapture(string captureSlot, out string? problem)
        {
            problem = "The capture service is unavailable.";
            return Options.Capture != null && Options.Capture.TryRestore(captureSlot, out problem);
        }
    }
}
