// Hollowmere - the player command line the game reads at boot (P3.1).
//
//   -frameLog [path]        write a per-frame CSV (frame,time_s,dt_ms,region,marker); without a path (or when the next
//                           argument starts with '-') the log goes to <persistentDataPath>/frame-log.csv
//   -autoplay <file>        run an autoplay script (AutoplayScript) instead of reading player input; off unless given
//   -saveDir <dir>          the save directory (default: <persistentDataPath>/saves)
//   -quitAfterFrames <n>    quit with exit code 0 after n frames (smoke runs)
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Hollowmere.Game
{
    /// <summary>The parsed Hollowmere command line (immutable).</summary>
    public sealed class HollowmereCommandLine
    {
        public const string FrameLogFlag = "-frameLog";
        public const string AutoplayFlag = "-autoplay";
        public const string SaveDirFlag = "-saveDir";
        public const string QuitAfterFramesFlag = "-quitAfterFrames";
        public const string DefaultFrameLogName = "frame-log.csv";

        private HollowmereCommandLine(string? frameLogPath, string? autoplayPath, string? saveDirectory, int quitAfterFrames, IReadOnlyList<string> problems)
        {
            FrameLogPath = frameLogPath;
            AutoplayPath = autoplayPath;
            SaveDirectory = saveDirectory;
            QuitAfterFrames = quitAfterFrames;
            Problems = problems;
        }

        /// <summary>Where the frame log goes; null when -frameLog was not given.</summary>
        public string? FrameLogPath { get; }

        /// <summary>The autoplay script file; null when -autoplay was not given.</summary>
        public string? AutoplayPath { get; }

        public bool Autoplay => AutoplayPath != null;

        /// <summary>The save directory override; null for the default.</summary>
        public string? SaveDirectory { get; }

        /// <summary>Quit with 0 after this many frames; 0 = never.</summary>
        public int QuitAfterFrames { get; }

        /// <summary>Malformed arguments (a flag without its value, a bad number).</summary>
        public IReadOnlyList<string> Problems { get; }

        /// <summary>A command line without any flag.</summary>
        public static HollowmereCommandLine Empty() => new HollowmereCommandLine(null, null, null, 0, Array.Empty<string>());

        /// <summary>Parses <paramref name="args"/> (pass Environment.GetCommandLineArgs()); flags are case-sensitive.</summary>
        public static HollowmereCommandLine Parse(IReadOnlyList<string> args)
        {
            if (args == null)
            {
                return Empty();
            }

            string? frameLog = null;
            string? autoplay = null;
            string? saveDir = null;
            int quitAfter = 0;
            var problems = new List<string>();
            for (int i = 0; i < args.Count; i++)
            {
                string arg = args[i] ?? string.Empty;
                string? next = i + 1 < args.Count ? args[i + 1] : null;
                bool hasValue = next != null && next.Length > 0 && !next.StartsWith("-", StringComparison.Ordinal);
                switch (arg)
                {
                    case FrameLogFlag:
                        if (hasValue)
                        {
                            frameLog = next;
                            i++;
                        }
                        else
                        {
                            frameLog = Path.Combine(Application.persistentDataPath, DefaultFrameLogName);
                        }

                        break;
                    case AutoplayFlag:
                        if (hasValue)
                        {
                            autoplay = next;
                            i++;
                        }
                        else
                        {
                            problems.Add(AutoplayFlag + " needs a script path");
                        }

                        break;
                    case SaveDirFlag:
                        if (hasValue)
                        {
                            saveDir = next;
                            i++;
                        }
                        else
                        {
                            problems.Add(SaveDirFlag + " needs a directory");
                        }

                        break;
                    case QuitAfterFramesFlag:
                        if (next != null && int.TryParse(next, NumberStyles.Integer, CultureInfo.InvariantCulture, out int frames) && frames >= 0)
                        {
                            quitAfter = frames;
                            i++;
                        }
                        else
                        {
                            problems.Add(QuitAfterFramesFlag + " needs a non-negative frame count");
                        }

                        break;
                }
            }

            return new HollowmereCommandLine(frameLog, autoplay, saveDir, quitAfter, problems);
        }

        public override string ToString() =>
            "frameLog=" + (FrameLogPath ?? "-") + " autoplay=" + (AutoplayPath ?? "-") + " saveDir=" + (SaveDirectory ?? "-")
            + " quitAfterFrames=" + QuitAfterFrames.ToString(CultureInfo.InvariantCulture);
    }
}
