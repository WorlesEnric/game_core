#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace Hollowmere.Game
{
    /// <summary>Opt-in player diagnostics. Keeps samples in memory so profiling does not introduce periodic disk stalls.</summary>
    public sealed class FrameProfile : MonoBehaviour
    {
        private readonly FrameTiming[] timing = new FrameTiming[1];
        private readonly StringBuilder samples = new StringBuilder(1024 * 1024);
        private ProfilerRecorder main;
        private ProfilerRecorder render;
        private ProfilerRecorder wait;
        private ProfilerRecorder gc;
        private string? path;

        public static void Configure(GameObject host)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "-frameProfile") host.AddComponent<FrameProfile>().Begin(args[i + 1]);
            }
            Debug.Log("[Hollowmere] frame pacing vsync=" + QualitySettings.vSyncCount + " target=" + Application.targetFrameRate);
        }

        private void Begin(string output)
        {
            path = output;
            samples.Append("frame,time_s,delta_ms,cpu_ms,main_ms,present_wait_ms,render_ms,gpu_ms,main_rec_ns,render_rec_ns,wait_rec_ns,gc_bytes\n");
            main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread");
            render = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread");
            wait = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Gfx.WaitForPresentOnGfxThread");
            gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        }

        private void LateUpdate()
        {
            if (path == null) return;
            FrameTimingManager.CaptureFrameTimings();
            uint count = FrameTimingManager.GetLatestTimings(1, timing);
            FrameTiming t = count > 0 ? timing[0] : default;
            samples.AppendFormat(CultureInfo.InvariantCulture, "{0},{1:F4},{2:F3},{3:F3},{4:F3},{5:F3},{6:F3},{7:F3},{8},{9},{10},{11}\n",
                Time.frameCount, Time.realtimeSinceStartupAsDouble, Time.unscaledDeltaTime * 1000,
                t.cpuFrameTime, t.cpuMainThreadFrameTime, t.cpuMainThreadPresentWaitTime, t.cpuRenderThreadFrameTime, t.gpuFrameTime,
                main.LastValue, render.LastValue, wait.LastValue, gc.LastValue);
        }

        private void OnDestroy()
        {
            if (path != null) File.WriteAllText(path, samples.ToString());
            main.Dispose(); render.Dispose(); wait.Dispose(); gc.Dispose();
        }
        private void OnApplicationQuit()
        {
            if (path != null) File.WriteAllText(path, samples.ToString());
            path = null;
        }
    }
}
