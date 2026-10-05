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
// HollowmereAdmittedSmoke is the live smoke registry. It runs only entries compiled into this assembly whose package is
// the verdict's admitted package, never a type or method named by candidate data, and never the sandbox Begin()
// harness (that would boot a second root and displace the restored game). An entry registers once per verdict digest
// and active root, advances one step per normal game frame from the AdmissionSmokeFrames component's Frame event (never
// from the poll, never by pumping), asserts the live world on every step and once more with a save round trip after
// the last one: Pending until then, Passed only then, Failed on an assertion failure, a lost root or a missing entry.
// The assertions only observe the world, so a registration that restarts after a reload is safe.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Gameplay.World.Editor;
using GameCore.Studio.Edit;
using GameCore.Unity.App;
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
            frames.Frame += smoke.Step;

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

    /// <summary>The live smoke registry of admitted mechanisms (trusted entries compiled into the game's Editor assembly).</summary>
    public sealed class HollowmereAdmittedSmoke
    {
        /// <summary>The pressure plate sample's smoke type (samples/mechanisms/pressure-plate, proposal.json smokeTest.type).</summary>
        public const string PressurePlateType = "Hollowmere.Mechanism.PressurePlate.PressurePlateSmoke";

        /// <summary>The pressure plate sample's package.</summary>
        public const string PressurePlatePackage = "com.hollowmere.mechanism.pressureplate";

        /// <summary>The largest proposal step count a registered entry accepts (the sample's is 120).</summary>
        public const int MaxSteps = 120;

        /// <summary>The poll allowance the binding asks R2-B for before admission begins: twice <see cref="MaxSteps"/>.</summary>
        public const int FrameBudget = 2 * MaxSteps;

        private static readonly IReadOnlyList<Entry> Entries = Array.AsReadOnly(new[]
        {
            new Entry(PressurePlateType, "Begin", PressurePlatePackage),
        });

        private readonly GameBoot boot;
        private readonly SaveService service;
        private readonly Dictionary<string, Registration> registrations = new Dictionary<string, Registration>(StringComparer.Ordinal);

        public HollowmereAdmittedSmoke(GameBoot boot, SaveService service)
        {
            this.boot = boot;
            this.service = service;
        }

        /// <summary>Polls answered (any status).</summary>
        public int Polls { get; private set; }

        /// <summary>Registrations made (one per verdict digest and active root).</summary>
        public int Registrations { get; private set; }

        /// <summary>Steps advanced from game frames, over all registrations.</summary>
        public int Steps { get; private set; }

        /// <summary>The statuses the polls returned, in order of change ("Pending", "Passed", ...).</summary>
        public List<string> Transitions { get; } = new List<string>();

        /// <summary>The last outcome line ("pass: ..." or "fail: ..."; empty while Pending).</summary>
        public string LastReport { get; private set; } = string.Empty;

        /// <summary>
        /// R2-G2's entry: registers the trusted entry for <paramref name="type"/>.<paramref name="method"/> once per verdict
        /// digest and active root, then answers its status: Pending until <paramref name="steps"/> game frames and their
        /// assertions completed, Passed only then, Failed on a failure, a lost root or no trusted registration.
        /// </summary>
        public AdmissionSmokeStatus RunAdmittedSmokeEntry(StageVerdict verdict, string type, string method, int steps)
        {
            Polls++;
            AdmissionSmokeStatus status = Poll(verdict, type, method, steps);
            if (Transitions.Count == 0 || Transitions[Transitions.Count - 1] != status.ToString())
            {
                Transitions.Add(status.ToString());
            }

            return status;
        }

        /// <summary>The per-frame callback (AdmissionSmokeFrames.Frame): advances every Pending registration one step.</summary>
        public void Step()
        {
            foreach (Registration registration in registrations.Values)
            {
                if (registration.Status != AdmissionSmokeStatus.Pending)
                {
                    continue;
                }

                string? failure = LiveWorld(registration.Root, false, out _);
                if (failure != null)
                {
                    Finish(registration, "fail: step " + (registration.Frames + 1).ToString(CultureInfo.InvariantCulture) + ": " + failure, false);
                    continue;
                }

                registration.Frames++;
                Steps++;
                if (registration.Frames < registration.Steps)
                {
                    continue;
                }

                failure = LiveWorld(registration.Root, true, out string detail);
                Finish(registration, failure == null
                    ? "pass: " + detail + "; " + registration.Frames.ToString(CultureInfo.InvariantCulture) + " game frame(s) stepped and asserted"
                    : "fail: after " + registration.Frames.ToString(CultureInfo.InvariantCulture) + " frame(s): " + failure, failure == null);
            }
        }

        private AdmissionSmokeStatus Poll(StageVerdict verdict, string type, string method, int steps)
        {
            if (verdict == null)
            {
                return AdmissionSmokeStatus.Failed;
            }

            Entry? entry = null;
            foreach (Entry candidate in Entries)
            {
                if (string.Equals(candidate.Type, type, StringComparison.Ordinal) && string.Equals(candidate.Method, method, StringComparison.Ordinal))
                {
                    entry = candidate;
                }
            }

            string? refusal = entry == null ? "no trusted smoke entry is registered for " + type + "." + method
                : !string.Equals(verdict.Package, entry.Package, StringComparison.Ordinal) ? "the entry belongs to " + entry.Package + ", the verdict admits " + verdict.Package
                : !verdict.Pass ? "the verdict did not pass"
                : steps <= 0 || steps > MaxSteps ? "steps must be within 1.." + MaxSteps.ToString(CultureInfo.InvariantCulture)
                : boot == null || boot.World == null ? "the game that was bound is gone"
                : null;
            if (refusal != null)
            {
                LastReport = "fail: " + refusal;
                Debug.LogWarning("[Hollowmere] admitted smoke " + LastReport);
                return AdmissionSmokeStatus.Failed;
            }

            GameApplicationRoot root = boot!.World!.Root;
            if (registrations.TryGetValue(verdict.Digest, out Registration? registration))
            {
                if (!ReferenceEquals(registration.Root, root))
                {
                    Finish(registration, "fail: the active root was replaced during the smoke", false);
                }

                return registration.Status;
            }

            registration = new Registration(root, steps, type + "." + method);
            registrations[verdict.Digest] = registration;
            Registrations++;
            string? failure = LiveWorld(root, true, out _);
            if (failure != null)
            {
                Finish(registration, "fail: at registration: " + failure, false);
            }

            return registration.Status;
        }

        private void Finish(Registration registration, string report, bool passed)
        {
            if (registration.Status != AdmissionSmokeStatus.Pending)
            {
                return;
            }

            registration.Status = passed ? AdmissionSmokeStatus.Passed : AdmissionSmokeStatus.Failed;
            LastReport = report + " [" + registration.Name + ", steps " + registration.Steps.ToString(CultureInfo.InvariantCulture) + "]";
            if (passed)
            {
                Debug.Log("[Hollowmere] admitted smoke " + LastReport);
            }
            else
            {
                Debug.LogWarning("[Hollowmere] admitted smoke " + LastReport);
            }
        }

        /// <summary>
        /// The live assertions of an admitted mechanism in the active Hollowmere world: the session is ready (the restored
        /// root is the save service's and the application's), the registered root is still the active one and runs, and -
        /// when <paramref name="roundTrip"/> - the world round-trips through its save codecs with an equal canonical slot
        /// hash. Observes only.
        /// </summary>
        private string? LiveWorld(GameApplicationRoot registered, bool roundTrip, out string detail)
        {
            detail = string.Empty;
            if (boot == null)
            {
                return "the game that was bound is gone";
            }

            if (!boot.AdmissionReady(service))
            {
                return "the game session is not ready (world, narrative, active root)";
            }

            GameApplicationRoot root = boot.World!.Root;
            if (!ReferenceEquals(root, registered))
            {
                return "the active root was replaced";
            }

            if (root.State != GameApplicationState.Running && root.State != GameApplicationState.Paused)
            {
                return "the active root is " + root.State;
            }

            detail = "active root " + root.State + ", catalog " + root.CatalogHash.ToHex().Substring(0, 12);
            if (!roundTrip)
            {
                return null;
            }

            SaveRoundTripReport report = service.TestRoundTrip();
            if (report.Refusal != null)
            {
                return "the active world's round trip was refused: " + report;
            }

            if (!report.Equal)
            {
                return "the active world's round trip differs: " + report.Detail;
            }

            detail += ", round trip equal (slot hash " + report.SourceSlotHash.Substring(0, Math.Min(12, report.SourceSlotHash.Length)) + ")";
            return null;
        }

        private sealed class Registration
        {
            public Registration(GameApplicationRoot root, int steps, string name)
            {
                Root = root;
                Steps = steps;
                Name = name;
            }

            public GameApplicationRoot Root { get; }

            public int Steps { get; }

            public string Name { get; }

            public int Frames { get; set; }

            public AdmissionSmokeStatus Status { get; set; } = AdmissionSmokeStatus.Pending;
        }

        private sealed class Entry
        {
            public Entry(string type, string method, string package)
            {
                Type = type;
                Method = method;
                Package = package;
            }

            public string Type { get; }

            public string Method { get; }

            public string Package { get; }
        }
    }
}
