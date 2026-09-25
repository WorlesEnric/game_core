// GameCore.Unity.Adapters — the engine-observation DTO of one committed assembly image (GC-019).
//
// Normative sources: 00 P-034 (an engine-owned domain's ECS data is stamped observation; a mirror is never an
// independently writable authoritative copy), P-045 (observers see immutable images at `(epoch, step)` publication
// only; they cannot use inspection to obtain writable ECS references), P-024 (a target first becomes query-visible
// with its complete effective assembly) and 04 s7 ("GameObjects read committed ECS snapshots; a visual interpolation
// cache is disposable").
//
// This is the DTO the presentation adapter reads: a plain, immutable projection of the *published* assembly — the
// targets, their committed scope parents and the effective values of their binding rows. It is built from a
// `PublishedWorldView` (or, in pure tests, from the same values by hand), so presenting never reads live ECS and
// never holds a writable reference. A gameplay-specific quantity a projection wants to show beyond the derived rows
// is supplied through the optional `ICommittedValueReader` seam, which is a *reader*: it returns values, never
// components.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Unity.Adapters.Views
{
    /// <summary>Stable target-to-scope and target-to-recipe lookup: the committed membership, not a live query.</summary>
    public interface ITargetScopeIndex
    {
        /// <summary>Committed owner scope of one live target; false when the target is not live (P-010).</summary>
        bool TryGetScope(TargetId target, out ScopeId scope);

        /// <summary>Recipe of one live target; false when the target is not live (P-024).</summary>
        bool TryGetRecipe(TargetId target, out DefinitionRef recipe);
    }

    /// <summary>A target-to-scope index built from an explicit table; used by pure fixtures and by tests.</summary>
    public sealed class TargetScopeTable : ITargetScopeIndex
    {
        private readonly Dictionary<Id128, ScopeId> scopes = new Dictionary<Id128, ScopeId>();
        private readonly Dictionary<Id128, DefinitionRef> recipes = new Dictionary<Id128, DefinitionRef>();

        public TargetScopeTable()
        {
        }

        public TargetScopeTable(IReadOnlyList<CommittedTargetEntry>? entries)
        {
            if (entries == null)
            {
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                scopes[entries[i].Target.Value] = entries[i].CompositionParent;
                recipes[entries[i].Target.Value] = entries[i].Recipe;
            }
        }

        public int Count => scopes.Count;

        public TargetScopeTable With(TargetId target, ScopeId scope, DefinitionRef recipe)
        {
            scopes[target.Value] = scope;
            recipes[target.Value] = recipe;
            return this;
        }

        public bool TryGetScope(TargetId target, out ScopeId scope) => scopes.TryGetValue(target.Value, out scope);

        public bool TryGetRecipe(TargetId target, out DefinitionRef recipe) => recipes.TryGetValue(target.Value, out recipe);
    }

    /// <summary>
    /// One committed target of one image: its identity, its committed composition parent, its recipe and the
    /// effective values of its binding rows. Every field is a value read at one publication, so the whole record is
    /// an observation rather than a live handle (P-045).
    /// </summary>
    public sealed class CommittedTargetEntry
    {
        public CommittedTargetEntry(
            TargetId target,
            ScopeId compositionParent,
            DefinitionRef recipe,
            IReadOnlyList<PresentationField>? fields)
        {
            Target = target;
            CompositionParent = compositionParent;
            Recipe = recipe;
            Fields = ContractCollections.Freeze(fields);
        }

        public TargetId Target { get; }

        /// <summary>Committed composition parent (the target's owner scope), never a Transform parent (P-010).</summary>
        public ScopeId CompositionParent { get; }

        public DefinitionRef Recipe { get; }

        /// <summary>Effective values of the target's committed binding rows, in canonical row order (P-017).</summary>
        public IReadOnlyList<PresentationField> Fields { get; }

        public override string ToString() =>
            Target.ToString() + "@" + CompositionParent.ToString()
            + " fields=" + Fields.Count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// One immutable committed assembly image. It is what a presentation adapter is allowed to read, and it exists so
    /// that "the adapter presents committed output" is a statement about the type it holds (P-045).
    /// </summary>
    public sealed class CommittedAssemblyImage
    {
        private readonly Dictionary<Id128, CommittedTargetEntry> byTarget = new Dictionary<Id128, CommittedTargetEntry>();
        private readonly List<CommittedTargetEntry> entries = new List<CommittedTargetEntry>();

        /// <summary>An image with no committed target; the state of a world that has published nothing yet.</summary>
        public static readonly CommittedAssemblyImage Empty = new CommittedAssemblyImage(
            default(SnapshotToken),
            null);

        public CommittedAssemblyImage(SnapshotToken token, IReadOnlyList<CommittedTargetEntry>? targets)
        {
            Token = token;
            if (targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                CommittedTargetEntry entry = targets[i];
                if (entry == null || entry.Target.IsDefault || byTarget.ContainsKey(entry.Target.Value))
                {
                    continue;
                }

                entries.Add(entry);
                byTarget.Add(entry.Target.Value, entry);
            }

            entries.Sort(CompareEntries);
        }

        public SnapshotToken Token { get; }

        public int TargetCount => entries.Count;

        public bool IsEmpty => entries.Count == 0;

        /// <summary>Committed targets in canonical order (P-008).</summary>
        public IReadOnlyList<TargetId> Targets()
        {
            var targets = new List<TargetId>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                targets.Add(entries[i].Target);
            }

            return targets;
        }

        public IReadOnlyList<CommittedTargetEntry> Entries => entries;

        public bool TryGet(TargetId target, out CommittedTargetEntry? entry) =>
            byTarget.TryGetValue(target.Value, out entry);

        /// <summary>Number of committed binding rows across every target; the image's own size measure.</summary>
        public int FieldCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    count += entries[i].Fields.Count;
                }

                return count;
            }
        }

        public override string ToString() =>
            "committedImage(" + Token.ToString() + ", targets="
            + entries.Count.ToString(CultureInfo.InvariantCulture) + ")";

        private static int CompareEntries(CommittedTargetEntry left, CommittedTargetEntry right) =>
            left.Target.CompareTo(right.Target);
    }

    /// <summary>
    /// Optional reader of gameplay-state values the caller wants presented in addition to the derived rows. It is a
    /// reader port: an implementation returns frozen values from the committed image and never a component, an entity
    /// or a writable mirror (P-034, P-045).
    /// </summary>
    public interface ICommittedValueReader
    {
        /// <summary>Extra presentation fields for one committed target; an empty list is the ordinary answer.</summary>
        IReadOnlyList<PresentationField> Read(TargetId target, SnapshotToken token);
    }

    /// <summary>
    /// The presentation source of one world, built from immutable committed images. The Unity adapter refreshes it
    /// once per presentation pass from the world's published assembly; a pure fixture refreshes it by hand. Reading
    /// it twice between refreshes gives the identical answer, which is what "presentation cannot advance a step"
    /// means operationally (P-036, P-045).
    /// </summary>
    public sealed class CommittedImageSource : IPresentationSource
    {
        private readonly ICommittedValueReader? values;
        private CommittedAssemblyImage image = CommittedAssemblyImage.Empty;
        private bool available;

        public CommittedImageSource(WorldId world, ICommittedValueReader? values = null)
        {
            if (world.Session.IsDefault)
            {
                throw new ArgumentException("A presentation source must name a live world session (P-004).", nameof(world));
            }

            World = world;
            this.values = values;
        }

        public WorldId World { get; }

        public bool IsAvailable => available;

        public SnapshotToken Current => image.Token;

        /// <summary>Images this source has been given; it never produces one itself (P-036).</summary>
        public int RefreshCount { get; private set; }

        /// <summary>How many committed targets the current image carries; evidence for the presentation steps.</summary>
        public int CommittedTargetCount => image.TargetCount;

        /// <summary>
        /// Installs one committed image. An image of another world incarnation is refused: a source presents exactly
        /// the world it was created for (P-004).
        /// </summary>
        public bool Refresh(CommittedAssemblyImage? next)
        {
            if (next == null)
            {
                return false;
            }

            if (!next.Token.World.Session.Equals(World.Session) && !next.Token.World.Session.IsDefault)
            {
                return false;
            }

            image = next;
            available = next.Token.World.Session.Equals(World.Session);
            RefreshCount++;
            return available;
        }

        /// <summary>Marks the world's image unavailable (faulted, stopped); presenting then does nothing (P-031).</summary>
        public void MarkUnavailable()
        {
            available = false;
        }

        public IReadOnlyList<TargetId> CommittedTargets() => image.Targets();

        public bool TryRead(TargetId target, out PresentationTarget? projection)
        {
            projection = null;
            if (!available)
            {
                return false;
            }

            if (!image.TryGet(target, out CommittedTargetEntry? entry) || entry == null)
            {
                return false;
            }

            IReadOnlyList<PresentationField> extra = values == null
                ? Array.Empty<PresentationField>()
                : values.Read(target, image.Token);

            var fields = new List<PresentationField>(entry.Fields.Count + extra.Count);
            for (int i = 0; i < entry.Fields.Count; i++)
            {
                fields.Add(entry.Fields[i]);
            }

            for (int i = 0; i < extra.Count; i++)
            {
                fields.Add(extra[i]);
            }

            projection = new PresentationTarget(target, entry.CompositionParent, image.Token, fields);
            return true;
        }

        /// <summary>Committed composition parent of one target at the current image; default when not committed.</summary>
        public bool TryGetCompositionParent(TargetId target, out ScopeId scope)
        {
            scope = default(ScopeId);
            if (!image.TryGet(target, out CommittedTargetEntry? entry) || entry == null)
            {
                return false;
            }

            scope = entry.CompositionParent;
            return true;
        }
    }
}
