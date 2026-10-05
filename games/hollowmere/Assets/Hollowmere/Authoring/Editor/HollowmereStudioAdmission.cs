// Hollowmere - the trusted Editor binding of Studio admission to the running game (R2-G request 4; P2.4 open item 2).
//
// GameBoot.UseSaves raises GameBoot.SavesInstalled on every call (before its UI-rig early return). This Editor-only,
// game-owned integration answers each one with
//
//   StudioAdmissionServices.BindAdmission(runtime, () => service, () => boot.AdmissionReady(service),
//       verdict => StudioAdmissionServices.RunSmokeTest(runtime, verdict,
//           (type, method, steps) => smoke.RunAdmittedSmokeEntry(verdict, type, method, steps)))
//
// for StudioServices.Runtime, so a staged mechanism admitted from Play Mode captures the running game through its
// SaveService ("admit-<id>"), waits for the restarted game's session, restores the capture and runs the mechanism's
// live smoke against the active world. It also binds on entering Play Mode and after a domain reload while playing (a
// game already past UseSaves). The readiness lambda reads GameBoot.World/Narrative, so it follows every restored-world
// re-attach. GameBoot only raises an instance event: player builds reference no Editor assembly.
//
// The binding also sets R2-B's polled smoke (AdmissionOptions.PollSmokeTest): after the synchronous check, the
// admission stays Pending while the active root advances the proposal's smoke steps (sanctioned frames, pumped by the
// PlayerLoop as always - never by the smoke), then the live assertions run again -> Passed or Failed.
//
// HollowmereAdmittedSmoke is the live smoke registry. It runs only entries compiled into this assembly whose package is
// the verdict's admitted package, never a type or method named by candidate data, and never the sandbox Begin()
// harness (that would boot a second root and displace the restored game). An entry observes and asserts the active
// world without driving it, so a re-run after an admission crash recovery gives the same answer (the frame count
// restarts with the new binding).
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

        /// <summary>The R2-G binding of <paramref name="runtime"/>'s admission to <paramref name="boot"/>'s <paramref name="service"/>.</summary>
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

            var smoke = new HollowmereAdmittedSmoke(boot, service);
            StudioAdmissionServices.BindAdmission(
                runtime,
                () => service,
                () => boot != null && boot.AdmissionReady(service),
                verdict => StudioAdmissionServices.RunSmokeTest(runtime, verdict, (type, method, steps) => smoke.RunAdmittedSmokeEntry(verdict, type, method, steps)));
            AdmissionOptions options = StageAdmission.Of(runtime).Options;

            // R2-B charges one polling frame per Editor update against SmokeTestFrameBudget (default 120). An entry observes
            // its proposal's steps on the world's own pump, so the allowance must cover them with slack for the Editor
            // and player loops not stepping in lockstep.
            options.SmokeTestFrameBudget = Math.Max(options.SmokeTestFrameBudget, HollowmereAdmittedSmoke.FrameBudget);
            options.PollSmokeTest = verdict =>
            {
                if (!ReferenceEquals(StageAdmission.Of(runtime).VerdictOf(verdict.ChangeSetId), verdict))
                {
                    return AdmissionSmokeStatus.Failed;
                }

                if (boot == null || !boot.AdmissionReady(service))
                {
                    return AdmissionSmokeStatus.Pending;
                }

                // The proposal's entry, resolved by R2-G's verified lookup (the callback only records it).
                string? type = null;
                string? method = null;
                int steps = 0;
                bool resolved = StudioAdmissionServices.RunSmokeTest(runtime, verdict, (t, m, n) =>
                {
                    type = t;
                    method = m;
                    steps = n;
                    return true;
                });
                return resolved ? smoke.PollAdmittedSmokeEntry(verdict, type!, method!, steps) : AdmissionSmokeStatus.Failed;
            };
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

        /// <summary>The polling-frame allowance the binding asks R2-B for: twice <see cref="MaxSteps"/>.</summary>
        public const int FrameBudget = 2 * MaxSteps;

        private static readonly IReadOnlyList<Entry> Entries = Array.AsReadOnly(new[]
        {
            new Entry(PressurePlateType, "Begin", PressurePlatePackage),
        });

        private readonly GameBoot boot;
        private readonly SaveService service;
        private readonly Dictionary<string, Observation> observations = new Dictionary<string, Observation>(StringComparer.Ordinal);

        public HollowmereAdmittedSmoke(GameBoot boot, SaveService service)
        {
            this.boot = boot;
            this.service = service;
        }

        /// <summary>Runs (this binding) so far.</summary>
        public int Runs { get; private set; }

        /// <summary>The last run's outcome line ("pass: ..." or "fail: ...").</summary>
        public string LastReport { get; private set; } = string.Empty;

        /// <summary>
        /// R2-G's dispatcher: runs the trusted entry registered for <paramref name="type"/>.<paramref name="method"/> when
        /// it belongs to the verdict's admitted package, against the active world. True only after every assertion passed.
        /// </summary>
        public bool RunAdmittedSmokeEntry(StageVerdict verdict, string type, string method, int steps)
        {
            Runs++;
            string? failure = Check(verdict, type, method, steps, out string detail);
            if (failure == null)
            {
                GameApplicationRoot root = boot.World!.Root;
                observations[verdict.Digest] = new Observation(root, root.PumpCounter.SanctionedPumps);
            }

            LastReport = (failure == null ? "pass: " + detail : "fail: " + failure) + " [" + type + "." + method + ", steps " + steps.ToString(CultureInfo.InvariantCulture) + "]";
            if (failure == null)
            {
                Debug.Log("[Hollowmere] admitted smoke " + LastReport);
            }
            else
            {
                Debug.LogWarning("[Hollowmere] admitted smoke " + LastReport);
            }

            return failure == null;
        }

        /// <summary>
        /// R2-B's polled smoke for the same trusted entry: Pending until the active root advanced <paramref name="steps"/>
        /// sanctioned frames since the synchronous run (else the first poll; a replaced root restarts the count), then the
        /// live assertions again,
        /// round trip included -> Passed or Failed. Never pumps the world.
        /// </summary>
        public AdmissionSmokeStatus PollAdmittedSmokeEntry(StageVerdict verdict, string type, string method, int steps)
        {
            string? failure = Check(verdict, type, method, steps, out string detail, false);
            if (failure != null)
            {
                Finish("fail: " + failure, type, method, steps, false);
                return AdmissionSmokeStatus.Failed;
            }

            GameApplicationRoot root = boot.World!.Root;
            int pumps = root.PumpCounter.SanctionedPumps;
            if (!observations.TryGetValue(verdict.Digest, out Observation? seen) || !ReferenceEquals(seen.Root, root))
            {
                observations[verdict.Digest] = new Observation(root, pumps);
                return AdmissionSmokeStatus.Pending;
            }

            int advanced = pumps - seen.Pumps;
            if (advanced < steps)
            {
                return AdmissionSmokeStatus.Pending;
            }

            failure = LiveWorld(steps, true, out detail);
            observations.Remove(verdict.Digest);
            Finish(failure == null ? "pass: " + detail + "; observed " + advanced.ToString(CultureInfo.InvariantCulture) + " frame(s)" : "fail: " + failure,
                type, method, steps, failure == null);
            return failure == null ? AdmissionSmokeStatus.Passed : AdmissionSmokeStatus.Failed;
        }

        /// <summary>Polled smoke runs that reached a verdict (Passed or Failed).</summary>
        public int Polls { get; private set; }

        private void Finish(string report, string type, string method, int steps, bool passed)
        {
            Polls++;
            LastReport = "poll " + report + " [" + type + "." + method + ", steps " + steps.ToString(CultureInfo.InvariantCulture) + "]";
            if (passed)
            {
                Debug.Log("[Hollowmere] admitted smoke " + LastReport);
            }
            else
            {
                Debug.LogWarning("[Hollowmere] admitted smoke " + LastReport);
            }
        }

        private string? Check(StageVerdict verdict, string type, string method, int steps, out string detail, bool roundTrip = true)
        {
            detail = string.Empty;
            if (verdict == null)
            {
                return "no verdict";
            }

            Entry? entry = null;
            foreach (Entry candidate in Entries)
            {
                if (string.Equals(candidate.Type, type, StringComparison.Ordinal) && string.Equals(candidate.Method, method, StringComparison.Ordinal))
                {
                    entry = candidate;
                }
            }

            if (entry == null)
            {
                return "no trusted smoke entry is registered for " + type + "." + method;
            }

            if (!string.Equals(verdict.Package, entry.Package, StringComparison.Ordinal))
            {
                return "the entry belongs to " + entry.Package + ", the verdict admits " + verdict.Package;
            }

            if (!verdict.Pass)
            {
                return "the verdict did not pass";
            }

            if (steps <= 0 || steps > MaxSteps)
            {
                return "steps must be within 1.." + MaxSteps.ToString(CultureInfo.InvariantCulture);
            }

            return LiveWorld(steps, roundTrip, out detail);
        }

        /// <summary>
        /// The live assertions of an admitted mechanism in the active Hollowmere world: the session is ready (the restored
        /// root is the save service's and the application's), the root runs, and the restored world round-trips through its
        /// save codecs with an equal canonical slot hash (when <paramref name="roundTrip"/>). Synchronous and side-effect
        /// free; the frames themselves are observed by <see cref="PollAdmittedSmokeEntry"/>.
        /// </summary>
        private string? LiveWorld(int steps, bool roundTrip, out string detail)
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

            detail += ", round trip equal (slot hash " + report.SourceSlotHash.Substring(0, Math.Min(12, report.SourceSlotHash.Length)) + "), "
                + steps.ToString(CultureInfo.InvariantCulture) + " step(s) requested";
            return null;
        }

        private sealed class Observation
        {
            public Observation(GameApplicationRoot root, int pumps)
            {
                Root = root;
                Pumps = pumps;
            }

            public GameApplicationRoot Root { get; }

            public int Pumps { get; }
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
