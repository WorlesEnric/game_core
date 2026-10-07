#nullable enable
using System;
using System.Globalization;
using System.Text;
using Unity.Profiling;

namespace Hollowmere.Game
{
    /// <summary>Completed-frame engine markers. Release players may strip markers: -1 means unavailable,
    /// never zero cost. These overlap other scopes and are not an additive frame decomposition.</summary>
    public sealed class FrameEngineAttribution : IDisposable
    {
        private readonly ProfilerRecorder[] counters;
        public const string Header = "gc_ns,scene_loading_ns,shader_ns,audio_ns,navmesh_ns,snapshot_ns,present_wait_ns";

        public FrameEngineAttribution()
        {
            counters = new[]
            {
                ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Collect"),
                ProfilerRecorder.StartNew(ProfilerCategory.Loading, "Loading.LoadSceneOperation"),
                ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shader.CreateGPUProgram"),
                ProfilerRecorder.StartNew(ProfilerCategory.Audio, "AudioManager.Update"),
                ProfilerRecorder.StartNew(ProfilerCategory.Ai, "NavMesh.Update"),
                ProfilerRecorder.StartNew(ProfilerCategory.Memory, "MemoryProfiler.TakeSnapshot"),
                ProfilerRecorder.StartNew(ProfilerCategory.Render, "Gfx.WaitForPresentOnGfxThread")
            };
        }

        public string Owner()
        {
            // A timed marker over the threshold is evidence; mere activity/GC count is not.
            for (int i = 0; i < counters.Length; i++)
            {
                if (Value(i) <= 100000000) continue;
                switch (i)
                {
                    case 0: return "engine.gc";
                    case 1: return "engine.scene-loading";
                    case 2: return "engine.shader";
                    case 3: return "engine.audio";
                    case 4: return "engine.navmesh";
                    case 5: return "engine.memory-snapshot";
                    case 6: return "engine.present-wait";
                }
            }
            return "unattributed";
        }

        public void AppendTo(StringBuilder output)
        {
            for (int i = 0; i < counters.Length; i++)
                output.Append(',').Append(Value(i).ToString(CultureInfo.InvariantCulture));
        }

        private long Value(int index) => counters[index].Valid && counters[index].Count > 0 ? counters[index].LastValue : -1;

        public void Dispose()
        {
            for (int i = 0; i < counters.Length; i++) counters[i].Dispose();
        }
    }
}
