// GameCore.Unity.Adapters — committed-output presentation (GC-019).
//
// Normative sources: 00 P-002 ("`EngineAdapter` admits observations and presents committed output"), P-034 (one
// authority per state domain; a DI service or presentation mirror exposes read snapshots and commands, not an
// independently writable copy), P-045 (observers see immutable images at `(epoch, step)` publication only; they
// cannot use inspection to obtain writable references) and 04 s3 ("update presentation from the last published
// snapshot"), 04 s7 ("GameObjects read committed ECS snapshots; a visual interpolation cache is disposable.
// Transform parentage and scope membership are independent").
//
// The whole presentation path is a *reader*: a source projects the committed assembly into presentation values, and
// the registry applies them to views. There is no writable ECS reference anywhere in this file, so "rendering
// interpolation and animation presentation cannot deduct resources or advance authoritative steps" (TEST-019) is a
// property of the shape of the code rather than a promise. Composition parentage is read from the committed snapshot
// and Transform parentage from the binder, and the two are reported side by side so a test can show they differ.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Unity.Adapters.Views
{
    /// <summary>One target of the committed assembly, projected into presentation values (P-045).</summary>
    public sealed class PresentationTarget
    {
        public PresentationTarget(
            TargetId target,
            ScopeId compositionParent,
            SnapshotToken token,
            IReadOnlyList<PresentationField>? fields)
        {
            Target = target;
            CompositionParent = compositionParent;
            Token = token;
            Fields = ContractCollections.Freeze(fields);
        }

        public TargetId Target { get; }

        /// <summary>Committed scope parent at this token, from composition (P-010). Never a Transform parent.</summary>
        public ScopeId CompositionParent { get; }

        public SnapshotToken Token { get; }

        public IReadOnlyList<PresentationField> Fields { get; }

        public override string ToString() =>
            Target.ToString() + "@" + CompositionParent.ToString()
            + " fields=" + Fields.Count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The one read side presentation is allowed: a projection of the last published committed image. An
    /// implementation must never expose a writable component or a live entity, and it must be callable on an idle
    /// world (P-036: an idle world still presents).
    /// </summary>
    public interface IPresentationSource
    {
        /// <summary>World this source presents; a source never presents another world (P-004).</summary>
        WorldId World { get; }

        /// <summary>False when no assembly has been published yet, or the world is gone; presenting then does nothing.</summary>
        bool IsAvailable { get; }

        /// <summary>The current committed token; default when <see cref="IsAvailable"/> is false.</summary>
        SnapshotToken Current { get; }

        /// <summary>Targets of the committed assembly at the current token, in canonical order (P-008).</summary>
        IReadOnlyList<TargetId> CommittedTargets();

        /// <summary>Projects one committed target; false when the target is not in the committed assembly.</summary>
        bool TryRead(TargetId target, out PresentationTarget? projection);
    }

    /// <summary>Why one presentation pass did what it did (P-058: an unavailable adapter is explicit, not silent).</summary>
    public enum PresentationOutcome
    {
        /// <summary>The source had no committed image; nothing was read and nothing was applied.</summary>
        NoCommittedImage = 0,

        /// <summary>At least one committed target was presented.</summary>
        Presented = 1,

        /// <summary>The source is available but the registry holds no view, so there was nothing to present into.</summary>
        NoViews = 2,
    }

    /// <summary>What one presentation pass did. Counts are observations of adapter behaviour, never authority.</summary>
    public sealed class PresentationReport
    {
        public PresentationReport(
            PresentationOutcome outcome,
            SnapshotToken token,
            int applied,
            int staleRefused,
            int orphanedDestroyed,
            int unreadable,
            int viewsAfter,
            string detail)
        {
            Outcome = outcome;
            Token = token;
            Applied = applied;
            StaleRefused = staleRefused;
            OrphanedDestroyed = orphanedDestroyed;
            Unreadable = unreadable;
            ViewsAfter = viewsAfter;
            Detail = detail ?? string.Empty;
        }

        public PresentationOutcome Outcome { get; }

        /// <summary>Committed token this pass read; default when the source had no image.</summary>
        public SnapshotToken Token { get; }

        public int Applied { get; }

        /// <summary>Applies refused because the view had already seen the same or a newer image (P-045).</summary>
        public int StaleRefused { get; }

        /// <summary>Views destroyed because their target had left the committed assembly (P-024).</summary>
        public int OrphanedDestroyed { get; }

        /// <summary>Live views whose target the source could not project; counted, never guessed.</summary>
        public int Unreadable { get; }

        public int ViewsAfter { get; }

        public string Detail { get; }

        public bool Presented => Outcome == PresentationOutcome.Presented;

        public override string ToString() =>
            Outcome.ToString() + "(" + Detail + ": applied=" + Applied.ToString(CultureInfo.InvariantCulture)
            + ", stale=" + StaleRefused.ToString(CultureInfo.InvariantCulture)
            + ", orphaned=" + OrphanedDestroyed.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// Presents the committed output of one world into the views of its registry. It runs after the world's own
    /// pump committed whatever that frame was going to commit, reads only committed images, and never creates a
    /// step: an idle world is presented from its last committed token, repeatedly, unchanged (P-036, 04 s3).
    /// </summary>
    public sealed class CommittedOutputPresenter
    {
        private readonly ViewRegistry registry;
        private readonly IPresentationSource source;
        private readonly IViewBinder? binder;
        private SnapshotToken lastPresented;

        public CommittedOutputPresenter(ViewRegistry registry, IPresentationSource source, IViewBinder? binder)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.binder = binder;
            if (!source.World.Equals(registry.World))
            {
                throw new ArgumentException(
                    "A presenter binds one world's committed output to that world's views (P-004).",
                    nameof(source));
            }
        }

        public ViewRegistry Registry => registry;

        public IPresentationSource Source => source;

        public int PresentCallCount { get; private set; }

        /// <summary>Distinct committed tokens this presenter has read; it never advances one itself (P-036).</summary>
        public int ObservedTokenCount { get; private set; }

        public int AppliedCount { get; private set; }

        public int StaleRefusalCount { get; private set; }

        public int OrphanDestroyCount { get; private set; }

        public SnapshotToken LastPresented => lastPresented;

        /// <summary>
        /// One presentation pass. Views whose target left the committed assembly are destroyed; live views whose
        /// target is still committed are applied from the current image. Nothing here can advance a step or mutate
        /// gameplay, because nothing here holds a write path (P-034, P-045).
        /// </summary>
        public PresentationReport Present()
        {
            PresentCallCount++;
            if (!source.IsAvailable)
            {
                return new PresentationReport(
                    PresentationOutcome.NoCommittedImage,
                    default(SnapshotToken),
                    0,
                    0,
                    0,
                    0,
                    registry.LiveViewCount,
                    "the world has published no committed image yet, so presentation had nothing to read");
            }

            SnapshotToken token = source.Current;
            if (!token.Equals(lastPresented))
            {
                ObservedTokenCount++;
                lastPresented = token;
            }

            IReadOnlyList<ViewKey> live = registry.LiveKeys();
            if (live.Count == 0)
            {
                return new PresentationReport(
                    PresentationOutcome.NoViews,
                    token,
                    0,
                    0,
                    0,
                    0,
                    registry.LiveViewCount,
                    "no view is registered; a headless world presents nothing and keeps running (TEST-018)");
            }

            // Target membership is read once per pass, so "is this target still committed" is one answer, not a
            // per-view guess (P-024).
            IReadOnlyList<TargetId> committed = source.CommittedTargets();
            int applied = 0;
            int stale = 0;
            int orphaned = 0;
            int unreadable = 0;
            var examined = new List<TargetId>();

            for (int i = 0; i < live.Count; i++)
            {
                ViewKey key = live[i];
                if (!Contains(examined, key.Target))
                {
                    examined.Add(key.Target);
                }
            }

            for (int t = 0; t < examined.Count; t++)
            {
                TargetId target = examined[t];
                if (!Contains(committed, target))
                {
                    // The target was despawned: its views are presentation garbage and go away. Gameplay already
                    // committed its own outcome, and this call cannot change it (P-024).
                    orphaned += registry.DestroyViewsOf(target, binder);
                    continue;
                }

                if (!source.TryRead(target, out PresentationTarget? projection) || projection == null)
                {
                    unreadable += CountViews(registry.ViewsOf(target));
                    continue;
                }

                IReadOnlyList<ViewRecord> views = registry.ViewsOf(target);
                var keys = new List<ViewKey>(views.Count);
                for (int v = 0; v < views.Count; v++)
                {
                    keys.Add(views[v].Key);
                }

                for (int v = 0; v < keys.Count; v++)
                {
                    var data = new PresentationApplyData(
                        keys[v],
                        token,
                        projection.CompositionParent,
                        projection.Fields);
                    if (registry.TryApply(keys[v], data, binder))
                    {
                        applied++;
                    }
                    else
                    {
                        stale++;
                    }
                }
            }

            AppliedCount += applied;
            StaleRefusalCount += stale;
            OrphanDestroyCount += orphaned;

            return new PresentationReport(
                PresentationOutcome.Presented,
                token,
                applied,
                stale,
                orphaned,
                unreadable,
                registry.LiveViewCount,
                "presented from the committed image at " + token.ToString());
        }

        /// <summary>
        /// Explicit adapter command: create the view of one committed target. A target the committed assembly does
        /// not carry is refused, so a view can never present something that was never committed (P-024, 04 s7).
        /// </summary>
        public ViewCreateOutcome CreateView(TargetId target, uint slot, out ViewRecord? record)
        {
            record = null;
            if (!source.IsAvailable)
            {
                return ViewCreateOutcome.Refused;
            }

            SnapshotToken token = source.Current;
            bool committed = Contains(source.CommittedTargets(), target);
            return registry.Create(new ViewKey(target, slot), token, committed, binder, out record);
        }

        /// <summary>
        /// Explicit adapter command: set the visual parent of one view. This is presentation only: the committed
        /// composition parent is read from the source and left alone, which is why reparenting a Transform cannot
        /// move composition (P-010).
        /// </summary>
        public bool SetVisualParent(ViewKey child, ViewKey parent)
        {
            ScopeId compositionParent = default(ScopeId);
            if (source.TryRead(child.Target, out PresentationTarget? projection) && projection != null)
            {
                compositionParent = projection.CompositionParent;
            }

            return registry.TrySetVisualParent(child, parent, compositionParent, binder);
        }

        /// <summary>Tears every view down; used by teardown and by the headless/re-attach fixtures (TEST-015).</summary>
        public int DestroyAllViews() => registry.DestroyAll(binder);

        private static int CountViews(IReadOnlyList<ViewRecord> views)
        {
            int live = 0;
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].IsLive)
                {
                    live++;
                }
            }

            return live;
        }

        private static bool Contains(IReadOnlyList<TargetId> targets, TargetId candidate)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].Equals(candidate))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
