#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;

namespace Hollowmere
{
    /// <summary>Exports native player profiler samples without a graphical Editor or deep profiling.</summary>
    public static class ProfileReport
    {
        public static void Run()
        {
            try { Export(); EditorApplication.Exit(0); }
            catch (Exception error)
            {
                string[] args = Environment.GetCommandLineArgs();
                string root = args[Array.IndexOf(args, "-profileDirectory") + 1];
                File.WriteAllText(Path.Combine(root, "export-error.txt"), error.ToString());
                EditorApplication.Exit(1);
            }
        }

        public static void Export()
        {
            string[] args = Environment.GetCommandLineArgs();
            string root = args[Array.IndexOf(args, "-profileDirectory") + 1];
            // Unity 6000.0 exposes only this internal history setter; use its documented 2000-frame maximum.
            typeof(ProfilerDriver).GetMethod("SetMaxFrameHistoryLength", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { 2000 });
            foreach (string path in Directory.GetFiles(root, "capture.raw", SearchOption.AllDirectories))
            {
                ProfilerDriver.ClearAllFrames();
                if (!ProfilerDriver.LoadProfile(path, false)) throw new InvalidOperationException("Cannot load " + path);
                using var output = new StreamWriter(path + ".csv");
                output.WriteLine("frame,thread,marker,total_ms");
                using var iterator = new ProfilerFrameDataIterator();
                for (int frame = ProfilerDriver.firstFrameIndex; frame <= ProfilerDriver.lastFrameIndex; frame++)
                {
                    for (int thread = 0; thread < iterator.GetThreadCount(frame); thread++)
                    {
                        using var view = ProfilerDriver.GetRawFrameDataView(frame, thread);
                        if (!view.valid) break;
                        if (view.threadName != "Main Thread" && view.threadName != "Render Thread") continue;
                        var totals = new Dictionary<string, double>(StringComparer.Ordinal);
                        for (int sample = 0; sample < view.sampleCount; sample++)
                        {
                            string name = view.GetSampleName(sample) ?? "(unnamed)";
                            totals.TryGetValue(name, out double total);
                            totals[name] = total + view.GetSampleTimeMs(sample);
                        }
                        foreach (var pair in totals)
                        {
                            if (pair.Value < 0.01) continue;
                            output.WriteLine(frame.ToString(CultureInfo.InvariantCulture) + "," + view.threadName + ",\""
                                + pair.Key.Replace("\"", "\"\"") + "\"," + pair.Value.ToString("F6", CultureInfo.InvariantCulture));
                        }
                    }
                }
            }
        }
    }
}
