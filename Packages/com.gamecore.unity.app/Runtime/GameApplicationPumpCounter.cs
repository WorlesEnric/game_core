// GameCore.Unity.App — SADR-010 (studio): exactly one pump path per host, counted and asserted.
//
// 02-architecture s7: "GameApplicationRoot installs exactly one pump; ... An assertion counts pumps per frame in Editor
// builds." The application pump (`GameCoreApplicationPump`) calls a world's adapter frame immediately before and after
// it pumps the host, so a counter installed as that world's adapter frame sees every pump the one sanctioned path
// performs. Two violations are detectable from there without touching the host:
//
//   * a second pump in the same host frame (the application pump ran twice, or a second loop node exists): the frame
//     clock reports the same frame for two input collections;
//   * a pump that bypassed the application pump (a scene script or a test calling `host.PumpFrame` directly): the
//     host's own `PumpCount` moved while no sanctioned pump was in progress.
//
// Violations are always counted. When assertions are enabled (Editor and development builds by default) each one is
// also logged as an error, which fails a Unity test that did not expect it; a release player only counts.
#nullable enable
using System;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Unity.Adapters;
using GameCore.Unity.Runtime;

namespace GameCore.Unity.App
{
    /// <summary>
    /// The pump counter of one host (SADR-010). It is the world's registered adapter frame and forwards both calls to
    /// the game's own frame, when there is one, so installing it changes nothing a game observes.
    /// </summary>
    public sealed class GameApplicationPumpCounter : IAdapterFrame
    {
        private readonly UnityWorldHost host;
        private Func<long> frameClock;
        private IAdapterFrame? inner;
        private long lastFrame = long.MinValue;
        private int lastObservedHostPumps;
        private bool insideSanctionedPump;

        /// <param name="host">The host whose pumps are counted.</param>
        /// <param name="inner">The game's adapter frame, forwarded unchanged; null when the game has none.</param>
        /// <param name="frameClock">The host frame number; <c>UnityEngine.Time.frameCount</c> by default.</param>
        /// <param name="assertionsEnabled">Log violations as errors; Editor and development builds by default.</param>
        public GameApplicationPumpCounter(
            UnityWorldHost host,
            IAdapterFrame? inner = null,
            Func<long>? frameClock = null,
            bool? assertionsEnabled = null)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            if (inner != null && !inner.World.Session.Equals(host.World.Session))
            {
                throw new ArgumentException("The game's adapter frame belongs to another world (P-004).", nameof(inner));
            }

            this.inner = inner;
            this.frameClock = frameClock ?? DefaultFrameClock;
            AssertionsEnabled = assertionsEnabled ?? (UnityEngine.Application.isEditor || UnityEngine.Debug.isDebugBuild);
            lastObservedHostPumps = host.PumpCount;
        }

        public WorldId World => host.World;

        /// <summary>The game's adapter frame this counter forwards to.</summary>
        public IAdapterFrame? Inner => inner;

        /// <summary>When true, every violation is also logged as an error.</summary>
        public bool AssertionsEnabled { get; set; }

        /// <summary>Host pumps the sanctioned application pump performed for this world.</summary>
        public int SanctionedPumps { get; private set; }

        /// <summary>Host frames in which the sanctioned pump ran more than once for this world.</summary>
        public int DuplicateFramePumps { get; private set; }

        /// <summary>Host pumps that happened outside the sanctioned application pump.</summary>
        public int BypassPumps { get; private set; }

        /// <summary>Every violation of the one-pump rule, of either kind.</summary>
        public int Violations => DuplicateFramePumps + BypassPumps;

        /// <summary>The host frame of the most recent sanctioned pump.</summary>
        public long LastFrame => lastFrame;

        /// <summary>Description of the most recent violation, empty before one.</summary>
        public string LastViolation { get; private set; } = string.Empty;

        /// <summary>Replaces the host frame clock (tests drive frames explicitly; EditMode has no advancing frame).</summary>
        public void UseFrameClock(Func<long> clock)
        {
            frameClock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>Replaces the forwarded game frame (the root installs it after its own composition).</summary>
        public void SetInner(IAdapterFrame? frame)
        {
            if (frame != null && !frame.World.Session.Equals(host.World.Session))
            {
                throw new ArgumentException("The game's adapter frame belongs to another world (P-004).", nameof(frame));
            }

            inner = frame;
        }

        /// <summary>
        /// Re-reads the host's pump count and charges any pump since the last observation that did not come from the
        /// sanctioned path. The root calls it from its own entry points (submit, lifecycle) as well.
        /// </summary>
        public void ObserveHost()
        {
            int pumps = host.PumpCount;
            if (pumps > lastObservedHostPumps && !insideSanctionedPump)
            {
                int bypassed = pumps - lastObservedHostPumps;
                BypassPumps += bypassed;
                Violate("world " + host.DiagnosticName + " was pumped " + bypassed.ToString(CultureInfo.InvariantCulture)
                    + " time(s) outside the application pump; GameApplicationRoot installs exactly one pump path "
                    + "(SADR-010, 04 s3)");
            }

            lastObservedHostPumps = pumps;
        }

        public AdapterFrameReport CollectInput()
        {
            ObserveHost();
            long frame = frameClock();
            if (frame == lastFrame)
            {
                DuplicateFramePumps++;
                Violate("world " + host.DiagnosticName + " was pumped twice in host frame "
                    + frame.ToString(CultureInfo.InvariantCulture) + " (SADR-010, 04 s3)");
            }

            lastFrame = frame;
            insideSanctionedPump = true;
            return inner != null ? inner.CollectInput() : AdapterFrameReport.Completed("pump-counter", 0, 0, "counted");
        }

        public AdapterFrameReport Present()
        {
            if (insideSanctionedPump)
            {
                int pumps = host.PumpCount;
                int performed = pumps - lastObservedHostPumps;
                if (performed > 1)
                {
                    DuplicateFramePumps += performed - 1;
                    Violate("world " + host.DiagnosticName + " was pumped " + performed.ToString(CultureInfo.InvariantCulture)
                        + " times inside one sanctioned pump (SADR-010)");
                }

                SanctionedPumps += performed > 0 ? 1 : 0;
                lastObservedHostPumps = pumps;
                insideSanctionedPump = false;
            }

            return inner != null ? inner.Present() : AdapterFrameReport.Completed("pump-counter", 0, 0, "counted");
        }

        public override string ToString() =>
            "pumpCounter{sanctioned=" + SanctionedPumps.ToString(CultureInfo.InvariantCulture)
            + ";duplicate=" + DuplicateFramePumps.ToString(CultureInfo.InvariantCulture)
            + ";bypass=" + BypassPumps.ToString(CultureInfo.InvariantCulture) + "}";

        private void Violate(string detail)
        {
            LastViolation = detail;
            if (AssertionsEnabled)
            {
                UnityEngine.Debug.LogError("[GameCore] one-pump assertion: " + detail);
            }
        }

        private static long DefaultFrameClock() => UnityEngine.Time.frameCount;
    }
}
