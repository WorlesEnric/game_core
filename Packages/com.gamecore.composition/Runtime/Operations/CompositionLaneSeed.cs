// GameCore.Composition — the seed that joins a control lane to a world's published assembly (GC-008).
//
// P-006 defines ONE publication series: `CompositionRevision` and `AssemblyEpoch` both increment at the same
// publication. 05 s2 fixes where that series starts: "A fresh session uses zero as the pre-publication
// revision/epoch/step; initial assembly publishes epoch/revision 1 while step stays 0."
//
// A lane that is joined to a world which has already published that initial assembly therefore does not start at
// 0/0 — it starts at the counters the world has published, and every publication it admits moves both counters by
// one. `CompositionLaneSeed` is that start value, passed explicitly when the lane is constructed, so the two
// counters stay one series instead of two that a later subsystem has to reconcile:
//
//   * standalone use (the GC-004 tests, a lane with no world) leaves the seed unset and keeps the pre-publication
//     0/0 behaviour unchanged;
//   * a lane joined to a world is seeded from that world's published assembly, and the publication path asserts
//     that what the lane published is what the world published (P-030).
//
// The seed carries no behaviour, no lease and no world reference: it is two counters, named so a caller cannot
// pass them in the wrong order.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Composition
{
    /// <summary>
    /// The published revision/epoch pair a lane starts from (P-006, 05 s2). Both counters always agree, because one
    /// publication increments them together; a seed whose two values differ is rejected rather than accepted as two
    /// series.
    /// </summary>
    public readonly struct CompositionLaneSeed
    {
        /// <summary>A lane with no published assembly: the pre-publication zero counters of a fresh session (05 s2).</summary>
        public static readonly CompositionLaneSeed Unpublished =
            new CompositionLaneSeed(CompositionRevision.Zero, AssemblyEpoch.Zero);

        /// <summary>
        /// The world's initial assembly: revision and epoch 1 with step 0 (05 s2). A lane joined to a world that has
        /// only published its initial assembly starts here.
        /// </summary>
        public static readonly CompositionLaneSeed InitialAssembly =
            new CompositionLaneSeed(CompositionRevision.First, AssemblyEpoch.First);

        public CompositionLaneSeed(CompositionRevision revision, AssemblyEpoch epoch)
            : this(revision, epoch, null)
        {
        }

        /// <summary>
        /// The same seed with the world definition's declared scope subtree (P-010). A world definition declares the
        /// scopes its content lives in, so a lane joined to that world starts from the declared tree rather than from
        /// a root alone: the tree is part of the initial composition, which is what keeps one publication series
        /// (P-006) — building a tree through scope-edit publications after the join would advance the composition
        /// counter without a matching assembly publication, and no assembly can be published for a scope-only edit.
        /// </summary>
        public CompositionLaneSeed(
            CompositionRevision revision,
            AssemblyEpoch epoch,
            IReadOnlyList<ScopeRecord>? initialScopes)
        {
            Revision = revision;
            Epoch = epoch;
            InitialScopes = initialScopes;
        }

        /// <summary>Committed composition revision the lane starts from.</summary>
        public CompositionRevision Revision { get; }

        /// <summary>Assembly epoch the lane starts from; always the same publication as <see cref="Revision"/>.</summary>
        public AssemblyEpoch Epoch { get; }

        /// <summary>True for a lane that is not joined to a published assembly (the pre-publication 0/0 case).</summary>
        public bool IsUnpublished => Revision.Equals(CompositionRevision.Zero) && Epoch.Equals(AssemblyEpoch.Zero);

        /// <summary>True when the lane is joined to a published assembly and must stay on that series (P-006).</summary>
        public bool IsJoined => !IsUnpublished;

        /// <summary>
        /// The world definition's declared scope subtree, or null/empty when the definition declares no scope beyond
        /// the world root. The records are validated when the lane opens its composition: a duplicate identity, a
        /// missing parent and a wrong depth are refused there (P-010).
        /// </summary>
        public IReadOnlyList<ScopeRecord>? InitialScopes { get; }

        /// <summary>True when the seed declares at least one scope beyond the world root.</summary>
        public bool DeclaresScopes => InitialScopes != null && InitialScopes.Count != 0;

        /// <summary>This seed's publication with the world definition's declared scope tree attached.</summary>
        public CompositionLaneSeed WithScopes(IReadOnlyList<ScopeRecord>? initialScopes) =>
            new CompositionLaneSeed(Revision, Epoch, initialScopes);

        /// <summary>
        /// True when the two counters describe the same publication. P-006 increments revision and epoch together,
        /// so a seed whose values differ would silently reintroduce two series and is rejected at construction.
        /// </summary>
        public bool IsConsistent => Revision.Value == Epoch.Value;

        /// <summary>Seeds a lane from the counters a world has already published (05 s2).</summary>
        public static CompositionLaneSeed FromPublishedAssembly(CompositionRevision revision, AssemblyEpoch epoch) =>
            new CompositionLaneSeed(revision, epoch);

        public override string ToString() =>
            "laneSeed(" + (IsUnpublished ? "unpublished" : "joined")
            + " revision=" + Revision.Value.ToString(CultureInfo.InvariantCulture)
            + ", epoch=" + Epoch.Value.ToString(CultureInfo.InvariantCulture)
            + (IsConsistent ? string.Empty : ", INCONSISTENT")
            + ", declaredScopes=" + (InitialScopes != null ? InitialScopes.Count : 0).ToString(CultureInfo.InvariantCulture)
            + ")";
    }
}
