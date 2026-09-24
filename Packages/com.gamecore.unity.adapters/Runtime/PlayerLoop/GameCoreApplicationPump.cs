#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Unity.Runtime;
using UnityEngine;

namespace GameCore.Unity.Adapters
{
    /// <summary>
    /// One host frame of the whole application: it enumerates the registered owned worlds, supplies each world its
    /// own clock source and pumps it once. A single application pump may drive several worlds, but every host has
    /// its own reentrancy guard and step counter, so no world can be pumped twice per frame (04 s3).
    /// </summary>
    public static class GameCoreApplicationPump
    {
        private static readonly List<UnityWorldHost> frameHosts = new List<UnityWorldHost>();

        /// <summary>
        /// Test and diagnostic switch. An automatic pump is the production path; tests disable it when they drive
        /// one world explicitly so frame-by-frame counts stay exact.
        /// </summary>
        public static bool IsEnabled { get; set; } = true;

        public static int FrameCount { get; private set; }

        /// <summary>Host frames routed across all worlds.</summary>
        public static int PumpedWorldFrameCount { get; private set; }

        public static int RefusedFrameCount { get; private set; }

        public static int ReentrantRefusalCount { get; private set; }

        /// <summary>Trampoline invocations refused because the node belongs to an earlier session (04 s9).</summary>
        public static int StaleTrampolineRefusalCount { get; private set; }

        public static ulong LastHostTicks { get; private set; }

        /// <summary>PlayerLoop entry point. A stale generation refuses to pump anything.</summary>
        public static void PumpTrampoline(int generation)
        {
            if (generation != GameCorePlayerLoopInstaller.Generation)
            {
                StaleTrampolineRefusalCount++;
                return;
            }

            PumpFrame();
        }

        /// <summary>Pumps every registered world once, in registry order.</summary>
        public static void PumpFrame()
        {
            if (!IsEnabled)
            {
                RefusedFrameCount++;
                return;
            }

            FrameCount++;
            frameHosts.Clear();
            frameHosts.AddRange(UnityWorldRegistry.Hosts);

            for (int i = 0; i < frameHosts.Count; i++)
            {
                UnityWorldHost host = frameHosts[i];
                ulong hostTicksNow = HostTicksNow(host);
                LastHostTicks = hostTicksNow;

                WorldPumpResult result = host.PumpFrame(hostTicksNow);
                if (result.Pumped)
                {
                    PumpedWorldFrameCount++;
                }
                else if (result.Reentrant)
                {
                    ReentrantRefusalCount++;
                }
            }

            frameHosts.Clear();
        }

        /// <summary>Pumps one world explicitly; used by tests and by the standalone probe.</summary>
        public static WorldPumpResult PumpWorld(UnityWorldHost host)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            ulong hostTicksNow = HostTicksNow(host);
            LastHostTicks = hostTicksNow;
            return host.PumpFrame(hostTicksNow);
        }

        /// <summary>The world's declared clock source: unscaled host time only when the world asked for it (P-036).</summary>
        public static ulong HostTicksNow(UnityWorldHost host)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            double seconds = host.Temporal.UsesUnscaledHostClock
                ? Time.realtimeSinceStartupAsDouble
                : Time.timeAsDouble;

            if (seconds < 0.0)
            {
                seconds = 0.0;
            }

            return (ulong)(seconds * host.HostTicksPerSecond);
        }

        /// <summary>Clears per-session pump state; counters are evidence for one Play Mode session (04 s9).</summary>
        public static void Reset()
        {
            frameHosts.Clear();
            FrameCount = 0;
            PumpedWorldFrameCount = 0;
            RefusedFrameCount = 0;
            ReentrantRefusalCount = 0;
            StaleTrampolineRefusalCount = 0;
            LastHostTicks = 0UL;
            IsEnabled = true;
        }
    }
}
