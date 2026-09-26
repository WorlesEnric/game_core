// GameCore.Validation.ProbeHost — the three conformance entry points, one partial part per genre host.
//
// WHY THIS FILE EXISTS SEPARATELY. Each genre's `IConformanceFamily` implementation lives in its own file as one more
// partial part of that genre's host class; the entry point that BUILDS the family object and runs the table through
// `ConformanceScenario` is the same three lines for all three, so it lives here rather than being repeated. Being a
// partial part of the same class means it can use the host's own declarations and its own fixture catalog, exactly
// as the host's earlier gate entry points (`RunGeneratedCatalog`, `RunFixtureCatalog`, `RunW6Gate`) do.
//
// The FIXTURE catalog is used on purpose. The conformance fixture's claim is about the transition tables, not about
// the compiler's output: driving them over the hand-written generated-style catalog (which the genre packages ship
// and which an EditMode test can rebuild from source) keeps a conformance failure attributable to a transition rather
// than to a catalog regeneration, and the family's own `CatalogFingerprint` is recorded in every step so the run
// still names the catalog it used (P-028, P-060).
#nullable enable
using System;
using GameCore.Contracts;
using GameCore.Gameplay.Cards.Fixtures;
using GameCore.Gameplay.Narrative.Fixtures;
using GameCore.Gameplay.Traversal.Fixtures;
using GameCore.ReferenceConformance;

namespace GameCore.Validation.ProbeHost
{
    public static partial class Gc013CardsHost
    {
        /// <summary>
        /// Runs the whole card-market before/after table (07 s2.4 and the section's own prose rows) in real card
        /// worlds, one fresh world per stage, and returns its normalized trace and the oracle's verdict (GC-024).
        /// </summary>
        public static ConformanceTableResult RunConformanceCards()
        {
            CatalogBuildResult build = CardCatalogTable.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the hand-written card catalog was rejected by the production catalog rules, so the card"
                    + " conformance table cannot be executed: " + build.Describe());
            }

            var family = new CardFamily(
                build.Catalog,
                Declarations(),
                CardCatalogTable.Fingerprint().ToHex());
            return ConformanceScenario.Run(family, "cards");
        }
    }

    public static partial class Gc013NarrativeHost
    {
        /// <summary>
        /// Runs the whole chapter-quest before/after table (07 s3.3 and the section's own prose rows) in real
        /// narrative worlds, one fresh world per stage, and returns its normalized trace and the oracle's verdict.
        /// </summary>
        public static ConformanceTableResult RunConformanceNarrative()
        {
            CatalogBuildResult build = NarrativeScenarioCatalog.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the hand-written narrative catalog was rejected by the production catalog rules, so the"
                    + " narrative conformance table cannot be executed: " + build.Describe());
            }

            var family = new NarrativeFamily(
                build.Catalog,
                Declarations(),
                NarrativeScenarioCatalog.Fingerprint().ToHex());
            return ConformanceScenario.Run(family, "narrative");
        }
    }

    public static partial class Gc020TraversalHost
    {
        /// <summary>
        /// Runs the whole traversal before/after table (07 s4.3 and the section's own prose rows) in real
        /// fixed-step course worlds, one fresh world per stage, and returns its normalized trace and the oracle's
        /// verdict. The course's own declared step is what one `commit-command` integrates.
        /// </summary>
        public static ConformanceTableResult RunConformanceTraversal()
        {
            CatalogBuildResult build = TraversalCatalogTable.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the hand-written traversal catalog was rejected by the production catalog rules, so the"
                    + " traversal conformance table cannot be executed: " + build.Describe());
            }

            var family = new CourseFamily(
                build.Catalog,
                Declarations(),
                TraversalCatalogTable.Fingerprint().ToHex());
            return ConformanceScenario.Run(family, "traversal");
        }
    }
}
