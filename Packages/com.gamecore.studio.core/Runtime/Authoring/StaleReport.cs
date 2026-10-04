// GameCore.Studio.Authoring - stale checks of authoring refs and selections (docs/studio/03-authoring-contracts.md
// s1, s2, s7). A StaleReport lists every ref that no longer matches the project; each entry carries a registered
// diagnostic (03 s9) so inspectors, the engine and agents report the same code.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Model;

namespace GameCore.Studio.Authoring
{
    /// <summary>Why a ref is stale.</summary>
    public enum StaleReason
    {
        /// <summary>The object no longer exists (deleted, or never resolvable).</summary>
        Destroyed,
        /// <summary>The object exists but its path (name, parent, asset location) changed. Not blocking.</summary>
        Moved,
        /// <summary>The object's content stamp differs from the ref's stamp.</summary>
        StampChanged,
        /// <summary>The asset's content changed (an Asset ref whose stamp differs).</summary>
        AssetChanged,
        /// <summary>The region (or scene) that holds the object is not loaded.</summary>
        RegionUnloaded,
    }

    /// <summary>One stale ref.</summary>
    public sealed class StaleEntry
    {
        public StaleEntry(AuthoringRef target, StaleReason reason, Diagnostic diagnostic, string? actualStamp = null, string? actualPath = null)
        {
            Ref = target ?? throw new ArgumentNullException(nameof(target));
            Reason = reason;
            Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
            ActualStamp = actualStamp;
            ActualPath = actualPath;
        }

        public AuthoringRef Ref { get; }

        public StaleReason Reason { get; }

        /// <summary>A diagnostic with a registered code (StaleTarget for every reason).</summary>
        public Diagnostic Diagnostic { get; }

        /// <summary>The current stamp when the object still exists.</summary>
        public string? ActualStamp { get; }

        /// <summary>The current path when the object moved.</summary>
        public string? ActualPath { get; }

        /// <summary>False only for <see cref="StaleReason.Moved"/>: a move does not invalidate an edit.</summary>
        public bool Blocking => Reason != StaleReason.Moved;

        /// <summary>Builds an entry with the standard message for <paramref name="reason"/>.</summary>
        public static StaleEntry Create(AuthoringRef target, StaleReason reason, string? actualStamp = null, string? actualPath = null)
        {
            string message;
            string hint;
            switch (reason)
            {
                case StaleReason.Destroyed:
                    message = "The target no longer exists.";
                    hint = "It was deleted or cannot be found; re-select or re-plan against the current index.";
                    break;
                case StaleReason.Moved:
                    message = "The target moved from '" + (target.Path ?? "?") + "' to '" + (actualPath ?? "?") + "'.";
                    hint = "The edit still applies; the ref's path is refreshed.";
                    break;
                case StaleReason.StampChanged:
                    message = "The target changed since it was selected (expected " + (target.Stamp ?? "?") + ", actual " + (actualStamp ?? "?") + ").";
                    hint = "Rebase: re-plan the operation against the current stamp, or skip it.";
                    break;
                case StaleReason.AssetChanged:
                    message = "The asset changed since it was selected (expected " + (target.Stamp ?? "?") + ", actual " + (actualStamp ?? "?") + ").";
                    hint = "Rebase against the current asset, or skip the operation.";
                    break;
                default:
                    message = "The region that holds the target is not loaded.";
                    hint = "Load the region (or open its scene) and validate the selection again.";
                    break;
            }

            return new StaleEntry(target, reason, Diagnostic.AtRef(DiagnosticCodes.StaleTarget, target, message, hint), actualStamp, actualPath);
        }
    }

    /// <summary>The stale refs found by one validation; empty means everything still matches.</summary>
    public sealed class StaleReport
    {
        public static readonly StaleReport Empty = new StaleReport(Array.Empty<StaleEntry>());

        public StaleReport(IReadOnlyList<StaleEntry> entries)
        {
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        }

        public IReadOnlyList<StaleEntry> Entries { get; }

        /// <summary>True when any entry blocks an edit (anything but a move).</summary>
        public bool IsStale
        {
            get
            {
                for (int i = 0; i < Entries.Count; i++)
                {
                    if (Entries[i].Blocking)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public IReadOnlyList<Diagnostic> Diagnostics
        {
            get
            {
                List<Diagnostic> diagnostics = new List<Diagnostic>(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    diagnostics.Add(Entries[i].Diagnostic);
                }

                return diagnostics;
            }
        }

        /// <summary>The first entry with <paramref name="reason"/>, or null.</summary>
        public StaleEntry? Find(StaleReason reason)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Reason == reason)
                {
                    return Entries[i];
                }
            }

            return null;
        }

        public static StaleReport Combine(IEnumerable<StaleReport> reports)
        {
            List<StaleEntry> entries = new List<StaleEntry>();
            foreach (StaleReport report in reports)
            {
                entries.AddRange(report.Entries);
            }

            return entries.Count == 0 ? Empty : new StaleReport(entries);
        }
    }
}
