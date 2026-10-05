// Hollowmere - the -frameLog recorder (P3.1, B-FRAME evidence).
//
// One CSV row per rendered frame, written from LateUpdate: frame,time_s,dt_ms,region,marker. dt_ms is
// Time.unscaledDeltaTime in milliseconds (three decimals); region is the focus region the game last reported
// (SetRegion); marker joins every label queued with Mark since the previous row with '|'. The game marks region
// transitions as "region:<from>-><to>", which record_playthrough.sh uses to separate transition hitches from steady
// frames. Rows are buffered and flushed every 120 frames, on quit and on destroy. Invariant culture, no quoting:
// commas in a region or marker become ';'.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Hollowmere.Game
{
    /// <summary>Writes one CSV row per rendered frame (added by the game when -frameLog is given).</summary>
    [DisallowMultipleComponent]
    public sealed class FrameLogRecorder : MonoBehaviour
    {
        public const int FlushEveryFrames = 120;
        public const string Header = "frame,time_s,dt_ms,region,marker";

        private readonly StringBuilder buffer = new StringBuilder(16 * 1024);
        private readonly List<string> markers = new List<string>();
        private string? path;
        private string region = string.Empty;
        private int pending;

        /// <summary>The file being written; null before Begin.</summary>
        public string? LogPath => path;

        /// <summary>Data rows written (or buffered) so far.</summary>
        public int Rows { get; private set; }

        public bool Recording => path != null;

        /// <summary>Starts the log at <paramref name="logPath"/> (truncates it) and writes the header lines.</summary>
        public void Begin(string logPath, string revision)
        {
            if (string.IsNullOrEmpty(logPath))
            {
                throw new ArgumentException("a frame log needs a path", nameof(logPath));
            }

            string? directory = Path.GetDirectoryName(Path.GetFullPath(logPath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            path = logPath;
            Rows = 0;
            markers.Clear();
            buffer.Clear();
            buffer.Append("# hollowmere frame log\n");
            buffer.Append("# revision ").Append(Clean(string.IsNullOrEmpty(revision) ? "unknown" : revision)).Append('\n');
            buffer.Append("# started ").Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)).Append('\n');
            buffer.Append("# screen ").Append(Screen.width.ToString(CultureInfo.InvariantCulture)).Append('x')
                .Append(Screen.height.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(Clean(SystemInfo.graphicsDeviceName)).Append(' ')
                .Append(SystemInfo.graphicsDeviceType.ToString()).Append('\n');
            buffer.Append(Header).Append('\n');
            File.WriteAllText(logPath, buffer.ToString(), new UTF8Encoding(false));
            buffer.Clear();
            pending = 0;
        }

        /// <summary>The region name written on the following rows.</summary>
        public void SetRegion(string regionName) => region = Clean(regionName ?? string.Empty);

        /// <summary>Queues a marker for the next row.</summary>
        public void Mark(string label)
        {
            if (!string.IsNullOrEmpty(label))
            {
                markers.Add(Clean(label).Replace('|', '/'));
            }
        }

        /// <summary>Writes the buffered rows to the file.</summary>
        public void Flush()
        {
            if (path == null || buffer.Length == 0)
            {
                return;
            }

            File.AppendAllText(path, buffer.ToString(), new UTF8Encoding(false));
            buffer.Clear();
            pending = 0;
        }

        private void LateUpdate()
        {
            if (path == null)
            {
                return;
            }

            buffer.Append(Time.frameCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Time.realtimeSinceStartupAsDouble.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                .Append((Time.unscaledDeltaTime * 1000.0).ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                .Append(region).Append(',');
            for (int i = 0; i < markers.Count; i++)
            {
                if (i > 0)
                {
                    buffer.Append('|');
                }

                buffer.Append(markers[i]);
            }

            buffer.Append('\n');
            markers.Clear();
            Rows++;
            if (++pending >= FlushEveryFrames)
            {
                Flush();
            }
        }

        private void OnApplicationQuit() => Flush();

        private void OnDestroy() => Flush();

        /// <summary>No commas, no newlines (the CSV has no quoting).</summary>
        private static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var cleaned = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                cleaned.Append(c == ',' ? ';' : c == '\n' || c == '\r' ? ' ' : c);
            }

            return cleaned.ToString();
        }
    }
}
