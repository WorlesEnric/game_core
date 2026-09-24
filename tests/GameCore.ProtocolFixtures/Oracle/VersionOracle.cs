// Independent pure oracle (GC-002). Protocol-version interpretation from P-055: majors are incompatible,
// a minor extension is accepted inside the declared min/max minor window, and an unknown required feature
// rejects before mount. There is no silent fallback of any kind.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.ProtocolFixtures.Oracle
{
    /// <summary>Interpretation of one manifest against a host protocol version (P-055).</summary>
    public enum VersionVerdict
    {
        Supported = 0,
        UnsupportedMajor = 1,
        MinorBelowMinimum = 2,
        MinorAboveMaximum = 3,
        UnknownRequiredFeature = 4,
        MalformedRange = 5,
    }

    /// <summary>Version decision with the stable diagnostic code it maps to.</summary>
    public readonly struct VersionDecision
    {
        public VersionDecision(VersionVerdict verdict, string code, string detail)
        {
            Verdict = verdict;
            Code = code;
            Detail = detail;
        }

        public VersionVerdict Verdict { get; }

        /// <summary>Stable diagnostic code text; empty when supported.</summary>
        public string Code { get; }

        public string Detail { get; }

        public bool Supported => Verdict == VersionVerdict.Supported;

        /// <summary>
        /// The decision can never be reconstructed as a supported one: no fallback to Conservative, an old
        /// phase table or another backend happens on mismatch (P-055).
        /// </summary>
        public bool IsRejectedWithoutFallback => !Supported && Code.Length != 0;
    }

    /// <summary>The host's own protocol version and known feature set.</summary>
    public readonly struct HostProtocol
    {
        public HostProtocol(int major, int minor, IReadOnlyList<Id128>? knownFeatureIds)
        {
            Major = major;
            Minor = minor;
            KnownFeatureIds = knownFeatureIds ?? Array.Empty<Id128>();
        }

        public int Major { get; }

        public int Minor { get; }

        public IReadOnlyList<Id128> KnownFeatureIds { get; }
    }

    /// <summary>The manifest's declared support window and required features.</summary>
    public readonly struct ManifestProtocol
    {
        public ManifestProtocol(int major, int minMinor, int maxMinor, IReadOnlyList<Id128>? requiredFeatureIds)
        {
            Major = major;
            MinMinor = minMinor;
            MaxMinor = maxMinor;
            RequiredFeatureIds = requiredFeatureIds ?? Array.Empty<Id128>();
        }

        public int Major { get; }

        public int MinMinor { get; }

        public int MaxMinor { get; }

        public IReadOnlyList<Id128> RequiredFeatureIds { get; }
    }

    /// <summary>Version-window interpretation and required-feature gate (P-055).</summary>
    public static class VersionOracle
    {
        public const string UnsupportedVersionCode = "UnsupportedVersion";

        public static VersionDecision Evaluate(HostProtocol host, ManifestProtocol manifest)
        {
            if (manifest.MinMinor > manifest.MaxMinor)
            {
                return new VersionDecision(
                    VersionVerdict.MalformedRange,
                    UnsupportedVersionCode,
                    "manifest minor range is inverted: " + manifest.MinMinor + ".." + manifest.MaxMinor);
            }

            if (manifest.Major != host.Major)
            {
                return new VersionDecision(
                    VersionVerdict.UnsupportedMajor,
                    UnsupportedVersionCode,
                    "host major " + host.Major + " does not accept manifest major " + manifest.Major);
            }

            if (host.Minor < manifest.MinMinor)
            {
                return new VersionDecision(
                    VersionVerdict.MinorBelowMinimum,
                    UnsupportedVersionCode,
                    "host minor " + host.Minor + " is below manifest minimum " + manifest.MinMinor);
            }

            if (host.Minor > manifest.MaxMinor)
            {
                return new VersionDecision(
                    VersionVerdict.MinorAboveMaximum,
                    UnsupportedVersionCode,
                    "host minor " + host.Minor + " is above manifest maximum " + manifest.MaxMinor);
            }

            for (int i = 0; i < manifest.RequiredFeatureIds.Count; i++)
            {
                if (!Contains(host.KnownFeatureIds, manifest.RequiredFeatureIds[i]))
                {
                    return new VersionDecision(
                        VersionVerdict.UnknownRequiredFeature,
                        UnsupportedVersionCode,
                        "unknown required feature " + manifest.RequiredFeatureIds[i]);
                }
            }

            return new VersionDecision(VersionVerdict.Supported, string.Empty, "supported");
        }

        private static bool Contains(IReadOnlyList<Id128> values, Id128 candidate)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].Equals(candidate))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
