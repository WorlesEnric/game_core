// Hollowmere - the trusted Editor binding of Studio admission to the running game (R2-G request 4 as superseded by
// R2-G2's "P3.1 exact binding"; P2.4 open item 2).
//
// GameBoot.UseSaves raises GameBoot.SavesInstalled on every call (before its UI-rig early return). This Editor-only,
// game-owned integration answers each one with R2-G2's binding
//
//   StudioAdmissionServices.BindAdmission(runtime, () => service, () => boot.AdmissionReady(service),
//       verdict => StudioAdmissionServices.RunSmokeTest(runtime, verdict,
//           (type, method, steps) => smoke.RunAdmittedSmokeEntry(verdict, type, method, steps)))
//
// (the tri-state poll overload), so a staged mechanism admitted from Play Mode captures the running game through its
// SaveService ("admit-<id>"), waits for the restarted game's session, restores the capture and polls the mechanism's
// live smoke in the active world. It also binds on entering Play Mode and after a domain reload while playing (a game
// already past UseSaves); a rebind re-registers from the verified retained proposal (R2-G2 RecoverPendingSmoke). The
// readiness lambda reads GameBoot.World/Narrative, so it follows every restored-world re-attach. GameBoot only raises an
// instance event, so player builds reference no Editor assembly. Before binding, the admission's smoke poll budget is
// raised to HollowmereAdmittedSmoke.FrameBudget (R2-B persists the budget when the smoke starts).
//
// HollowmereExtensionRegistry owns the reviewed package/type identities and normal world composition. The live smoke
// selects only those declarations: candidate smoke strings never become reflected callback names and sandbox Begin
// never runs here. Polling registers/observes; only AdmissionSmokeFrames advances the smoke, after the normal pump.
// A restored pre-admission checkpoint is witnessed before adding the new extension's targets/mounts through the
// existing composition boundary. The lever then proves committed off/on/off transitions on distinct game frames.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Gameplay.World;
using GameCore.Gameplay.World.Editor;
using GameCore.Studio.Edit;
using GameCore.Unity.App;
using GameCore.Unity.Runtime.Integration;
using Hollowmere.Boot;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hollowmere.Authoring
{
    /// <summary>Binds Studio admission (capture, session readiness, live smoke) to every running Hollowmere game.</summary>
    [InitializeOnLoad]
    public static class HollowmereStudioAdmission
    {
        static HollowmereStudioAdmission()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            if (EditorApplication.isPlaying)
            {
                EditorApplication.delayCall += () => AttachAll();
            }
        }

        /// <summary>
        /// Subscribes to every loaded GameBoot and binds Studio admission to the ones already past UseSaves (idempotent).
        /// Returns how many were bound now.
        /// </summary>
        public static int AttachAll()
        {
            int bound = 0;
            foreach (GameBoot boot in Object.FindObjectsByType<GameBoot>(FindObjectsSortMode.None))
            {
                boot.SavesInstalled -= OnSavesInstalled;
                boot.SavesInstalled += OnSavesInstalled;
                if (boot.Saves != null)
                {
                    Bind(StudioServices.Runtime, boot, boot.Saves);
                    bound++;
                }
            }

            return bound;
        }

        /// <summary>R2-G2's binding of <paramref name="runtime"/>'s admission to <paramref name="boot"/>'s <paramref name="service"/>.</summary>
        public static HollowmereAdmittedSmoke Bind(StudioRuntime runtime, GameBoot boot, SaveService service)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            if (boot == null)
            {
                throw new ArgumentNullException(nameof(boot));
            }

            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            AdmissionSmokeFrames frames = boot.GetComponent<AdmissionSmokeFrames>();
            if (frames == null)
            {
                frames = boot.gameObject.AddComponent<AdmissionSmokeFrames>();
            }

            var smoke = new HollowmereAdmittedSmoke(boot, service);
            frames.Bind(smoke.Step);

            // R2-B charges one poll per Editor update against SmokeTestFrameBudget (default 120) and persists the budget
            // when the smoke starts: an entry's steps run on game frames, so the allowance covers them with slack.
            AdmissionOptions options = StageAdmission.Of(runtime).Options;
            options.SmokeTestFrameBudget = Math.Max(options.SmokeTestFrameBudget, HollowmereAdmittedSmoke.FrameBudget);
            StudioAdmissionServices.BindAdmission(
                runtime,
                () => service,
                () => boot != null && boot.AdmissionReady(service),
                verdict => StudioAdmissionServices.RunSmokeTest(runtime, verdict, (type, method, steps) => smoke.RunAdmittedSmokeEntry(verdict, type, method, steps)));
            return smoke;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                AttachAll();
            }
        }

        private static void OnSavesInstalled(GameBoot boot, SaveService service) => Bind(StudioServices.Runtime, boot, service);
    }

    /// <summary>Game-owned live smoke drivers selected by the reload-stable trusted extension registry.</summary>
    public sealed class HollowmereAdmittedSmoke
    {
        public const string PressurePlateType = HollowmereExtensionRegistry.PressurePlateType;
        public const string PressurePlatePackage = HollowmereExtensionRegistry.PressurePlatePackage;
        public const string LeverType = HollowmereExtensionRegistry.LeverType;
        public const string LeverPackage = HollowmereExtensionRegistry.LeverPackage;
        public const int MaxSteps = 120;
        public const int FrameBudget = 2 * MaxSteps;

        private readonly GameBoot boot;
        private readonly SaveService service;
        private readonly HollowmereExtensionRegistry registry = new HollowmereExtensionRegistry();
        private readonly Dictionary<string, Registration> registrations = new Dictionary<string, Registration>(StringComparer.Ordinal);
        private readonly List<int> mechanismStates = new List<int>();
        private int lastFrame = -1;

        public HollowmereAdmittedSmoke(GameBoot boot, SaveService service)
        {
            this.boot = boot;
            this.service = service;
            MechanismStates = mechanismStates.AsReadOnly();
        }

        public int Polls { get; private set; }
        public int Registrations { get; private set; }
        public int Steps { get; private set; }
        public List<string> Transitions { get; } = new List<string>();
        public string LastReport { get; private set; } = string.Empty;
        public IReadOnlyList<HollowmereExtensionRegistry.Entry> Entries => registry.Entries;

        /// <summary>Committed lever observations of the most recent registration: off, on, off (never enqueue echoes).</summary>
        public IReadOnlyList<int> MechanismStates { get; }

        /// <summary>Equal round-trip hash at registration, before adding any absent extension target or mount.</summary>
        public string RestoredCheckpointSlotHash { get; private set; } = string.Empty;
        public string InitialSlotHash { get; private set; } = string.Empty;
        public string FinalSlotHash { get; private set; } = string.Empty;
        public bool CheckpointRoundTripsEqual { get; private set; }

        /// <summary>Registers once per verified digest/root; polling never advances the world or invokes sandbox Begin.</summary>
        public AdmissionSmokeStatus RunAdmittedSmokeEntry(StageVerdict verdict, string type, string method, int steps)
        {
            Polls++;
            AdmissionSmokeStatus status;
            try
            {
                status = Poll(verdict, type, method, steps);
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                status = Refuse(error.Message);
            }
            if (Transitions.Count == 0 || Transitions[Transitions.Count - 1] != status.ToString())
                Transitions.Add(status.ToString());
            return status;
        }

        /// <summary>One observation/command phase after each normal pump; repeated calls in a frame do not advance it.</summary>
        public void Step()
        {
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            foreach (Registration registration in registrations.Values)
            {
                if (registration.Status != AdmissionSmokeStatus.Pending) continue;
                try
                {
                    string? failure = LiveWorld(registration.Root, false, out _, out _);
                    if (failure == null)
                    {
                        registration.Frames++;
                        Steps++;
                        if (registration.Lever != null) failure = AdvanceLever(registration);
                    }
                    if (failure != null)
                    {
                        Finish(registration, "fail: frame " + registration.Frames.ToString(CultureInfo.InvariantCulture) + ": " + failure, false);
                        continue;
                    }
                    if (registration.Frames < registration.Steps) continue;
                    if (registration.Lever != null && registration.LeverPhase != 4)
                    {
                        Finish(registration, "fail: the lever did not complete committed off/on/off transitions", false);
                        continue;
                    }
                    failure = LiveWorld(registration.Root, true, out string detail, out string hash);
                    FinalSlotHash = hash;
                    CheckpointRoundTripsEqual = failure == null;
                    Finish(registration, failure == null
                        ? "pass: " + detail + "; " + registration.Frames.ToString(CultureInfo.InvariantCulture)
                            + " game frame(s) stepped and asserted" + (registration.Lever != null ? "; lever off/on/off committed" : string.Empty)
                        : "fail: final round trip: " + failure, failure == null);
                }
                catch (Exception error) when (!(error is OutOfMemoryException))
                {
                    Finish(registration, "fail: " + error.Message, false);
                }
            }
        }

        private AdmissionSmokeStatus Poll(StageVerdict verdict, string type, string method, int steps)
        {
            if (verdict == null) return Refuse("there is no verified verdict");
            HollowmereExtensionRegistry.Entry? entry = registry.Find(type, method);
            string? refusal = entry == null ? "no trusted smoke entry is registered for " + type + "." + method
                : !string.Equals(verdict.Package, entry.Package, StringComparison.Ordinal) ? "the entry belongs to " + entry.Package + ", the verdict admits " + verdict.Package
                : !verdict.Pass ? "the verdict did not pass"
                : steps < entry.MinimumSteps || steps > MaxSteps ? "steps must be within " + entry.MinimumSteps + ".." + MaxSteps
                : boot == null || boot.World == null ? "the game that was bound is gone"
                : null;
            if (refusal != null) return Refuse(refusal);

            GameApplicationRoot root = boot!.World!.Root;
            refusal = LiveWorld(root, false, out _, out _);
            if (refusal != null) return Refuse(refusal);
            if (entry!.ExtensionType != null && !string.Equals(root.CatalogHash.ToHex(), verdict.Predicted, StringComparison.Ordinal))
                return Refuse("the running catalog does not match the signed admitted catalog set");
            if (registrations.TryGetValue(verdict.Digest, out Registration? existing))
            {
                if (!ReferenceEquals(existing.Root, root))
                    return Refuse("the active root was replaced during the smoke");
                if (!ReferenceEquals(existing.Entry, entry) || existing.Steps != steps)
                    return Refuse("the registered smoke descriptor changed");
                return existing.Status;
            }

            // Resolve the reviewed adapter and witness the checkpoint before creating a runnable registration.
            // A missing/foreign/broken extension must never leave a Pending observer-only entry behind.
            HollowmereExtensionRegistry.LeverAccess? lever = entry!.ExtensionType == null ? null : entry.BindLever(boot.World);
            refusal = LiveWorld(root, true, out _, out string hash);
            if (refusal != null) return Refuse("at registration: " + refusal);
            var registration = new Registration(root, steps, entry) { Lever = lever };
            registrations.Add(verdict.Digest, registration);
            Registrations++;
            mechanismStates.Clear();
            InitialSlotHash = FinalSlotHash = string.Empty;
            RestoredCheckpointSlotHash = hash;
            CheckpointRoundTripsEqual = false;
            return registration.Status;
        }

        private string? AdvanceLever(Registration registration)
        {
            HollowmereExtensionRegistry.LeverAccess lever = registration.Lever!;
            GameApplicationRoot root = registration.Root;
            if (root.State != GameApplicationState.Running) return "the lever needs the running game's normal pump";
            if (registration.LeverPhase == 0)
            {
                string? failure = InstallRestoredExtension(root, lever);
                if (failure != null) return failure;
                registration.LeverPhase = 1;
                // A rebind may observe a previously committed on-state. Normalize it on a real frame before the proof.
                if (lever.State == 1)
                {
                    if (!lever.Toggle()) return "the lever normalization command was refused";
                    registration.CommandStep = root.Host.CurrentStep.Value;
                    registration.Normalizing = true;
                }
                return null;
            }

            int state = lever.State;
            if (state != 0 && state != 1) return "the lever has no committed 0/1 state in the active root";
            if (registration.LeverPhase == 1)
            {
                if (registration.Normalizing && root.Host.CurrentStep.Value <= registration.CommandStep) return null;
                if (state != 0) return "the lever did not reach its initial off state";
                string? failure = LiveWorld(root, true, out _, out string hash);
                if (failure != null) return failure;
                InitialSlotHash = hash;
                mechanismStates.Add(state);
                if (!lever.Toggle()) return "the off-to-on command was refused";
                if (lever.State != 0) return "Toggle changed state outside the committed game step";
                registration.CommandStep = root.Host.CurrentStep.Value;
                registration.LeverPhase = 2;
                return null;
            }
            if (registration.LeverPhase == 2 || registration.LeverPhase == 3)
            {
                if (root.Host.CurrentStep.Value <= registration.CommandStep) return null;
                int expected = registration.LeverPhase == 2 ? 1 : 0;
                if (state != expected) return "the lever committed " + state + " instead of " + expected;
                mechanismStates.Add(state);
                if (registration.LeverPhase == 2)
                {
                    if (!lever.Toggle()) return "the on-to-off command was refused";
                    if (lever.State != 1) return "Toggle changed state outside the committed game step";
                    registration.CommandStep = root.Host.CurrentStep.Value;
                }
                registration.LeverPhase++;
                return null;
            }
            return state == 0 ? null : "the lever left its final off state";
        }

        private string? InstallRestoredExtension(GameApplicationRoot root, HollowmereExtensionRegistry.LeverAccess lever)
        {
            // Restore deliberately omits boot steps. Reapply only the reviewed extension's additive targets/mounts,
            // after the old checkpoint was witnessed. Never seed an existing target or overwrite a restored slot.
            IGameplayWorldExtension extension = lever.Extension;
            bool created = false;
            if (!(extension is IGameplayWorldTargets targets)) return "the trusted lever has no declared session target";
            foreach (GameplayExtensionTarget target in targets.Targets(boot.World!.Manifest))
            {
                if (root.Targets.Contains(target.Target)) continue;
                if (!root.Seeder.TrySeed(target.Target, root.Definition.RootScope, target.Recipe, out _, out DiagnosticCode code, out string detail))
                    return "seeding the admitted extension was refused: " + code + ": " + detail;
                created = true;
            }
            foreach (GameplayPluginMount mount in extension.Plugins)
            {
                if (root.Lane.Committed.TryGetInstall(mount.Instance, out var installed))
                {
                    if (installed == null || !installed.Scope.Equals(root.Definition.RootScope)
                        || !installed.Record.PluginType.Equals(mount.Declaration.Manifest.PluginTypeId)
                        || installed.State != InstallationState.Active)
                        return "the admitted extension's instance is not an active matching world-scope mount";
                    continue;
                }
                WorldAdmissionReport report = root.Submit(WorldBuilder.Mount(mount.Declaration, mount.Instance, root.Definition.RootScope));
                if (report.Outcome != BridgeOutcome.Executed)
                    return "mounting the admitted extension was refused: " + report.Outcome + ": " + report.RefusalDetail;
                if (!root.Lane.Committed.TryGetInstall(mount.Instance, out installed) || installed == null
                    || !installed.Scope.Equals(root.Definition.RootScope)
                    || !installed.Record.PluginType.Equals(mount.Declaration.Manifest.PluginTypeId)
                    || installed.State != InstallationState.Active)
                    return "the admitted extension was not published as an active matching world-scope mount";
            }
            if (created && !lever.InitializeNewTarget())
                return "the new admitted lever target could not initialize its state";
            return null;
        }

        private AdmissionSmokeStatus Refuse(string reason)
        {
            LastReport = "fail: " + reason;
            Debug.LogWarning("[Hollowmere] admitted smoke " + LastReport);
            return AdmissionSmokeStatus.Failed;
        }

        private void Finish(Registration registration, string report, bool passed)
        {
            registration.Status = passed ? AdmissionSmokeStatus.Passed : AdmissionSmokeStatus.Failed;
            LastReport = report + " [" + registration.Entry.SmokeType + "." + registration.Entry.SmokeMethod
                + ", steps " + registration.Steps.ToString(CultureInfo.InvariantCulture) + "]";
            if (passed) Debug.Log("[Hollowmere] admitted smoke " + LastReport);
            else Debug.LogWarning("[Hollowmere] admitted smoke " + LastReport);
        }

        private string? LiveWorld(GameApplicationRoot registered, bool roundTrip, out string detail, out string hash)
        {
            detail = hash = string.Empty;
            if (boot == null) return "the game that was bound is gone";
            if (!boot.AdmissionReady(service)) return "the game session is not ready (world, narrative, active root)";
            GameApplicationRoot root = boot.World!.Root;
            if (!ReferenceEquals(root, registered)) return "the active root was replaced";
            if (root.State != GameApplicationState.Running && root.State != GameApplicationState.Paused)
                return "the active root is " + root.State;
            detail = "active root " + root.State + ", catalog " + root.CatalogHash.ToHex().Substring(0, 12);
            if (!roundTrip) return null;
            SaveRoundTripReport report = service.TestRoundTrip();
            if (report.Refusal != null) return "the active world's round trip was refused: " + report;
            if (!report.Equal) return "the active world's round trip differs: " + report.Detail;
            if (!boot.AdmissionReady(service) || !ReferenceEquals(root, boot.World.Root))
                return "the round trip displaced the running application root";
            hash = report.SourceSlotHash;
            detail += ", round trip equal (slot hash " + hash.Substring(0, Math.Min(12, hash.Length)) + ")";
            return null;
        }

        private sealed class Registration
        {
            internal Registration(GameApplicationRoot root, int steps, HollowmereExtensionRegistry.Entry entry)
            {
                Root = root;
                Steps = steps;
                Entry = entry;
            }

            internal GameApplicationRoot Root { get; }
            internal int Steps { get; }
            internal HollowmereExtensionRegistry.Entry Entry { get; }
            internal HollowmereExtensionRegistry.LeverAccess? Lever { get; set; }
            internal int Frames { get; set; }
            internal int LeverPhase { get; set; }
            internal ulong CommandStep { get; set; }
            internal bool Normalizing { get; set; }
            internal AdmissionSmokeStatus Status { get; set; } = AdmissionSmokeStatus.Pending;
        }
    }
}
