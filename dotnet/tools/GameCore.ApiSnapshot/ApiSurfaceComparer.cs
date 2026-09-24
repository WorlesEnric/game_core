// API compatibility comparison for the GC-003 production contract assembly.
//
// Tooling only. The generator's output format is unchanged (see ApiSnapshotGenerator); this comparer answers a
// different question than the W0 freeze test: "is the production assembly still a strict superset of the frozen
// W0 reference seam surface?" A removed type, a removed member, a changed member signature or a changed type
// header all fail; additions are reported but accepted, because a superset is what the Wave 1 gate allows.
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace GameCore.ApiSnapshot
{
    /// <summary>Outcome of comparing a frozen reference listing with a production listing.</summary>
    public sealed class ApiCompatibilityResult
    {
        internal ApiCompatibilityResult(
            bool compatible,
            IReadOnlyList<string> removedLines,
            IReadOnlyList<string> addedLines,
            int frozenTypes,
            int productionTypes)
        {
            Compatible = compatible;
            RemovedLines = removedLines;
            AddedLines = addedLines;
            FrozenTypes = frozenTypes;
            ProductionTypes = productionTypes;
        }

        /// <summary>True when no frozen line is missing or changed, and no type header differs.</summary>
        public bool Compatible { get; }

        /// <summary>Frozen lines absent from the production listing, prefixed by their type header.</summary>
        public IReadOnlyList<string> RemovedLines { get; }

        /// <summary>Production lines absent from the frozen listing, prefixed by their type header.</summary>
        public IReadOnlyList<string> AddedLines { get; }

        public int FrozenTypes { get; }

        public int ProductionTypes { get; }

        /// <summary>Human-readable summary; used directly as the assertion message in the test.</summary>
        public string Describe()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("frozen types: ").Append(FrozenTypes).Append(", production types: ").Append(ProductionTypes).Append('\n');
            builder.Append("removed or changed lines: ").Append(RemovedLines.Count).Append('\n');
            for (int i = 0; i < RemovedLines.Count; i++)
            {
                builder.Append("-  ").Append(RemovedLines[i]).Append('\n');
            }

            builder.Append("added lines: ").Append(AddedLines.Count).Append('\n');
            for (int i = 0; i < AddedLines.Count; i++)
            {
                builder.Append("+  ").Append(AddedLines[i]).Append('\n');
            }

            return builder.ToString();
        }
    }

    /// <summary>Strict-superset comparison of two API listings produced by <see cref="ApiSnapshotGenerator"/>.</summary>
    public static class ApiSurfaceComparer
    {
        /// <summary>
        /// Compares a frozen listing with a production listing. Provenance comment lines are ignored (the
        /// assembly name is deliberately not part of the surface), and every type header must appear verbatim in
        /// the production listing.
        /// </summary>
        public static ApiCompatibilityResult Compare(string frozenText, string productionText)
        {
            if (frozenText == null)
            {
                throw new ArgumentNullException(nameof(frozenText));
            }

            if (productionText == null)
            {
                throw new ArgumentNullException(nameof(productionText));
            }

            List<TypeSurface> frozen = Parse(frozenText);
            List<TypeSurface> production = Parse(productionText);
            Dictionary<string, TypeSurface> productionByHeader = new Dictionary<string, TypeSurface>(StringComparer.Ordinal);
            for (int i = 0; i < production.Count; i++)
            {
                productionByHeader[production[i].Header] = production[i];
            }

            List<string> removed = new List<string>();
            List<string> added = new List<string>();
            HashSet<string> frozenHeaders = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < frozen.Count; i++)
            {
                TypeSurface expected = frozen[i];
                frozenHeaders.Add(expected.Header);
                if (!productionByHeader.TryGetValue(expected.Header, out TypeSurface? actual))
                {
                    removed.Add("type header: " + expected.Header);
                    continue;
                }

                for (int m = 0; m < expected.Members.Count; m++)
                {
                    if (!actual.MemberSet.Contains(expected.Members[m]))
                    {
                        removed.Add(expected.Header + " :: " + expected.Members[m]);
                    }
                }
            }

            for (int i = 0; i < production.Count; i++)
            {
                TypeSurface actual = production[i];
                if (!frozenHeaders.Contains(actual.Header))
                {
                    added.Add("type header: " + actual.Header);
                }

                TypeSurface? expected = null;
                for (int f = 0; f < frozen.Count && expected == null; f++)
                {
                    if (string.Equals(frozen[f].Header, actual.Header, StringComparison.Ordinal))
                    {
                        expected = frozen[f];
                    }
                }

                for (int m = 0; m < actual.Members.Count; m++)
                {
                    if (expected == null || !expected.MemberSet.Contains(actual.Members[m]))
                    {
                        added.Add(actual.Header + " :: " + actual.Members[m]);
                    }
                }
            }

            return new ApiCompatibilityResult(removed.Count == 0, removed, added, frozen.Count, production.Count);
        }

        private static List<TypeSurface> Parse(string listing)
        {
            List<TypeSurface> types = new List<TypeSurface>();
            TypeSurface? current = null;
            string[] lines = ApiSnapshotDiff.Normalize(listing).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0)
                {
                    continue;
                }

                if (line[0] == '#')
                {
                    continue;
                }

                if (line.StartsWith("type ", StringComparison.Ordinal))
                {
                    current = new TypeSurface(line);
                    types.Add(current);
                    continue;
                }

                current?.Add(line.TrimStart());
            }

            return types;
        }

        private sealed class TypeSurface
        {
            internal TypeSurface(string header)
            {
                Header = header;
            }

            internal string Header { get; }

            internal void Add(string member)
            {
                Members.Add(member);
                MemberSet.Add(member);
            }

            internal List<string> Members { get; } = new List<string>();

            internal HashSet<string> MemberSet { get; } = new HashSet<string>(StringComparer.Ordinal);
        }
    }
}
