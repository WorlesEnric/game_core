#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Cards;
using GameCore.Gameplay.Narrative.Fixtures;
using GameCore.Rules.Cards;
using GameCore.Rules.Narrative;
using GameCore.Unity.Runtime;

namespace GameCore.Validation.Slices
{
    /// <summary>
    /// A generated inactive family entry (04 section 8, TEST-001, TEST-020).
    ///
    /// The two catalogs this project builds (`Generated/ProbeCatalog.g.cs` for the narrative family and
    /// `GeneratedCards/CardCatalog.g.cs` for the cards family) each register one of these under its own generated
    /// factory key, with a direct constructor reference. The closure each constructor names is therefore reachable
    /// from generated code: nothing here depends on `link.xml` `preserve="all"`, and managed stripping has a real
    /// root to start from.
    ///
    /// "Inactive" means the entry is linked into the executable but mounts nothing. The entry holds no world, no
    /// installation and no live state; it is a reachability root plus the key the probe resolves. The family's own
    /// plugin factory (the narrative/wave fixture plugin key, the card table plugin key) is mounted later by the
    /// scenario through the catalog, which is what proves "linked but not started, mountable by key".
    /// </summary>
    public interface IFamilyPluginEntry
    {
        /// <summary>Stable diagnostic family name; never a runtime identity (P-004).</summary>
        string Family { get; }

        /// <summary>
        /// Number of plugin declarations this entry enumerates. Touching the family's own registration code is what
        /// makes the entry a root rather than a marker: the count comes from the family package.
        /// </summary>
        int DeclarationCount { get; }

        /// <summary>Diagnostic names of the systems the family's compiled schedule dispatches, in declared order.</summary>
        IReadOnlyList<string> SystemNames { get; }
    }

    /// <summary>
    /// The narrative family's generated inactive entry: it references the narrative gameplay package's real
    /// registration surface, so the linker keeps the six narrative systems, their payload readers and the chapter
    /// content in a stripped player. Nothing is mounted and no world is touched.
    /// </summary>
    public sealed class NarrativeFamilyPluginEntry : IFamilyPluginEntry
    {
        private readonly List<string> systemNames = new List<string>();

        public NarrativeFamilyPluginEntry()
        {
            // Every call below is a real reference into GameCore.Gameplay.Narrative.Fixtures and
            // GameCore.Rules.Narrative: stripping cannot remove the registration helpers, the typed readers, the six
            // ManagedSystemRegistration instantiations or the chapter content while this entry is alive, which is
            // exactly what a generated root is for.
            IReadOnlyList<SystemRegistration> systems = NarrativeRegistration.Systems();
            for (int i = 0; i < systems.Count; i++)
            {
                systemNames.Add(systems[i].DiagnosticName);
            }

            DeclarationCount = systems.Count;
            ReaderCount = NarrativeRegistration.Readers().Count;
            LaneCount = NarrativeRegistration.Messages().Buffers.Count;
            Chapters = NarrativeChapters.All.Count;
            RegisteredNames = NarrativeRegistrations.Count;
        }

        public string Family => "narrative";

        public int DeclarationCount { get; }

        /// <summary>Declared typed command readers of the narrative plane (04 section 8).</summary>
        public int ReaderCount { get; }

        /// <summary>Declared bounded lanes of the narrative plane (P-043).</summary>
        public int LaneCount { get; }

        /// <summary>Chapters the narrative rules package declares (GC-010).</summary>
        public int Chapters { get; }

        /// <summary>Content names the narrative genre-neutrality audit covers (P-001).</summary>
        public int RegisteredNames { get; }

        public IReadOnlyList<string> SystemNames => systemNames;

        public override string ToString()
            => "NarrativeFamilyPluginEntry(systems=" + DeclarationCount + ", chapters=" + Chapters + ")";
    }

    /// <summary>
    /// The cards family's generated inactive entry: it references the card gameplay and rules packages' real
    /// registration surface, so the linker keeps the four settlement systems, their readers, the registered reducer
    /// and the registered static predicate in a stripped player. Nothing is mounted.
    /// </summary>
    public sealed class CardFamilyPluginEntry : IFamilyPluginEntry
    {
        /// <summary>Stable name of the additive reducer registration the card catalog declares (P-019).</summary>
        public const string ReducerStableName = "cards.reducer.int32-sum";

        /// <summary>Stable name of the static eligibility predicate the card catalog declares (P-015).</summary>
        public const string PredicateStableName = "cards.predicate.always";

        private readonly List<string> systemNames = new List<string>();

        public CardFamilyPluginEntry()
        {
            IReadOnlyList<SystemRegistration> systems = CardTableRegistration.Systems();
            for (int i = 0; i < systems.Count; i++)
            {
                systemNames.Add(systems[i].DiagnosticName);
            }

            DeclarationCount = systems.Count;
            ReaderCount = CardTableRegistration.Readers().Count;
            LaneCount = CardTableRegistration.Messages().Buffers.Count;
            RouteCount = CardTableRegistration.Messages().Routes.Count;
            DispatchKinds = CardTableRegistration.DispatchKinds().Count;

            // The card rules package's own generated-style seams (the registered reducer and the registered static
            // predicate) are part of the derived-scoring closure the card world needs, so the entry constructs them
            // and their implementation types become reachable too (P-015, P-019). Each key is derived from its own
            // stable name by the documented rule (P-004) rather than read out of the generated card catalog, because
            // referencing that catalog's assembly back from here would be a cyclic asmdef edge.
            ReducerKey = ReducerStableName;
            PredicateKey = PredicateStableName;
            var reducerKey = new FactoryKey(StableNameKeyDerivation.Derive(ReducerStableName), 1U);
            var predicateKey = new FactoryKey(StableNameKeyDerivation.Derive(PredicateStableName), 1U);
            ReducerLabel = new CardSetBonusReducer(reducerKey).StableName;
            PredicateLabel = new CardAlwaysPredicate(predicateKey).StableName;
        }

        public string Family => "cards";

        public int DeclarationCount { get; }

        /// <summary>Declared typed command and batch readers of the card plane (04 section 8).</summary>
        public int ReaderCount { get; }

        /// <summary>Declared bounded lanes of the card plane (P-043).</summary>
        public int LaneCount { get; }

        /// <summary>Declared command routes of the card plane (P-042).</summary>
        public int RouteCount { get; }

        /// <summary>Compiled dispatch kinds the card schedule adaptation resolves (GC-009).</summary>
        public int DispatchKinds { get; }

        /// <summary>Stable name of the registered additive reducer, so the reducer type is a root (P-019).</summary>
        public string ReducerKey { get; }

        /// <summary>Stable name of the registered eligibility predicate, so the predicate type is a root (P-015).</summary>
        public string PredicateKey { get; }

        /// <summary>Stable name the rooted reducer instance reports.</summary>
        public string ReducerLabel { get; }

        /// <summary>Stable name the rooted predicate instance reports.</summary>
        public string PredicateLabel { get; }

        public IReadOnlyList<string> SystemNames => systemNames;

        public override string ToString()
            => "CardFamilyPluginEntry(systems=" + DeclarationCount + ", routes=" + RouteCount + ")";
    }
}
