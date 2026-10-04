// GameCore.Studio.Authoring - building and resolving AuthoringRefs (docs/studio/03-authoring-contracts.md s1, s2).
// The Editor implementation (GameCore.Studio.Edit.AuthoringRefResolver) uses GlobalObjectId, asset GUIDs and
// SerializedObject-based content stamps; runtime consumers such as the picking service depend only on this interface.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Model;

namespace GameCore.Studio.Authoring
{
    /// <summary>The outcome of resolving one ref: the live object (when found) and any stale findings.</summary>
    public sealed class ResolveResult
    {
        public ResolveResult(AuthoringRef target, UnityEngine.Object? resolved, IReadOnlyList<StaleEntry> stale, string? currentStamp)
        {
            Ref = target ?? throw new ArgumentNullException(nameof(target));
            Object = resolved;
            Stale = stale ?? throw new ArgumentNullException(nameof(stale));
            CurrentStamp = currentStamp;
        }

        public AuthoringRef Ref { get; }

        /// <summary>The resolved object; null when destroyed or unloaded, and for Location refs.</summary>
        public UnityEngine.Object? Object { get; }

        public IReadOnlyList<StaleEntry> Stale { get; }

        /// <summary>The object's current content stamp (null when unresolved).</summary>
        public string? CurrentStamp { get; }

        public bool IsLocation => Ref.Kind == AuthoringKind.Location;

        /// <summary>True when the object was found (or the ref is a Location).</summary>
        public bool Resolved => IsLocation || Object != null;

        /// <summary>True when the ref resolved and no blocking stale finding exists.</summary>
        public bool IsCurrent
        {
            get
            {
                if (!Resolved)
                {
                    return false;
                }

                for (int i = 0; i < Stale.Count; i++)
                {
                    if (Stale[i].Blocking)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public StaleReport Report => Stale.Count == 0 ? StaleReport.Empty : new StaleReport(Stale);
    }

    /// <summary>Builds refs from Unity objects and resolves refs back with the 03 s2 stale checks.</summary>
    public interface IAuthoringRefResolver
    {
        /// <summary>
        /// A ref to <paramref name="target"/> (a GameObject resolves to its authored component when it has one).
        /// Null for objects that cannot be addressed (destroyed, or Studio staging objects).
        /// </summary>
        AuthoringRef? BuildRef(UnityEngine.Object target, AuthorScope? scope = null, bool includeStamp = true);

        /// <summary>Resolves a ref, checking existence, path, stamp and residency.</summary>
        ResolveResult Resolve(AuthoringRef reference);

        /// <summary>The content stamp of an object (03 s1), or null when it cannot be computed.</summary>
        string? ComputeStamp(UnityEngine.Object target);
    }
}
