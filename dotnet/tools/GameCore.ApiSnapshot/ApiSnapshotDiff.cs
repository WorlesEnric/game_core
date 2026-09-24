// Snapshot comparison helpers for the W0 reference seam (GC-002). Tooling only.
#nullable enable
using System;
using System.Text;

namespace GameCore.ApiSnapshot
{
    /// <summary>Outcome of comparing a committed snapshot with a freshly generated listing.</summary>
    public sealed class ApiSnapshotComparison
    {
        public ApiSnapshotComparison(bool pending, bool equal, string diff, int expectedLines, int actualLines)
        {
            Pending = pending;
            Equal = equal;
            Diff = diff;
            ExpectedLines = expectedLines;
            ActualLines = actualLines;
        }

        /// <summary>True when the committed file still carries the pending-generation placeholder.</summary>
        public bool Pending { get; }

        public bool Equal { get; }

        /// <summary>Line-oriented diff summary; empty when the listings are equal.</summary>
        public string Diff { get; }

        public int ExpectedLines { get; }

        public int ActualLines { get; }
    }

    /// <summary>Normalization and line-oriented diff for API snapshots.</summary>
    public static class ApiSnapshotDiff
    {
        /// <summary>Normalizes line endings and strips a trailing blank line so comparisons are host-independent.</summary>
        public static string Normalize(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return normalized.TrimEnd('\n');
        }

        public static bool IsPending(string committedText) =>
            Normalize(committedText).TrimStart().StartsWith(ApiSnapshotGenerator.PendingHeader, StringComparison.Ordinal);

        public static ApiSnapshotComparison Compare(string committedText, string generatedText)
        {
            if (committedText == null)
            {
                throw new ArgumentNullException(nameof(committedText));
            }

            if (generatedText == null)
            {
                throw new ArgumentNullException(nameof(generatedText));
            }

            string expected = Normalize(committedText);
            string actual = Normalize(generatedText);
            string[] expectedLines = expected.Length == 0 ? Array.Empty<string>() : expected.Split('\n');
            string[] actualLines = actual.Length == 0 ? Array.Empty<string>() : actual.Split('\n');

            if (IsPending(committedText))
            {
                return new ApiSnapshotComparison(true, false, Describe(expectedLines, actualLines), expectedLines.Length, actualLines.Length);
            }

            bool equal = string.Equals(expected, actual, StringComparison.Ordinal);
            return new ApiSnapshotComparison(
                false,
                equal,
                equal ? string.Empty : Describe(expectedLines, actualLines),
                expectedLines.Length,
                actualLines.Length);
        }

        /// <summary>Line-oriented diff: the first differing hunks with line numbers, call-site readable in a test log.</summary>
        public static string Describe(string[] expected, string[] actual)
        {
            if (expected == null)
            {
                throw new ArgumentNullException(nameof(expected));
            }

            if (actual == null)
            {
                throw new ArgumentNullException(nameof(actual));
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("expected ").Append(expected.Length).Append(" lines, actual ").Append(actual.Length).Append(" lines\n");
            int max = Math.Max(expected.Length, actual.Length);
            int hunks = 0;
            int index = 0;
            while (index < max)
            {
                string left = index < expected.Length ? expected[index] : "<missing>";
                string right = index < actual.Length ? actual[index] : "<missing>";
                if (string.Equals(left, right, StringComparison.Ordinal))
                {
                    index++;
                    continue;
                }

                if (hunks >= 20)
                {
                    builder.Append("... diff truncated at 20 hunks\n");
                    break;
                }

                hunks++;
                builder.Append("@@ line ").Append(index + 1).Append(" @@\n");
                builder.Append("-  ").Append(left).Append('\n');
                builder.Append("+  ").Append(right).Append('\n');
                index++;
            }

            if (hunks == 0)
            {
                builder.Append("no differing lines found\n");
            }

            return builder.ToString();
        }
    }
}
