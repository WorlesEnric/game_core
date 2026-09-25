// GameCore.Composition tests — the scope tree a world definition declares (P-010, P-006).
//
// P-010 says scopes form one rooted acyclic tree per world and that a live target has exactly one owner scope. A
// world definition therefore declares the scopes its content lives in, and a lane joined to that world opens its
// composition with the declared subtree. That matters for P-006, which has ONE publication series: building a tree
// through scope-edit publications after the join would advance the composition revision and epoch once per scope
// while no assembly can be published for a scope-only edit, so the two counters would never rejoin.
//
// The cases below pin the seam down on both sides: a lane seeded with a declared tree exposes exactly those scopes
// with their parentage and depths, and a malformed declared tree is refused while the lane is still unexposed. A
// seed without a declared tree keeps the root-only behaviour every earlier test relies on.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Composition;
using NUnit.Framework;

namespace GameCore.Composition.Tests
{
    [TestFixture]
    public sealed class DeclaredScopeTreeTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x73636F70UL, 7UL));
        private static readonly ScopeId Root = new ScopeId(new Id128(0x726F6F74UL, 7UL));
        private static readonly ScopeId Story = new ScopeId(new Id128(0x73746F72UL, 7UL));
        private static readonly ScopeId Village = new ScopeId(new Id128(0x76696C6CUL, 7UL));
        private static readonly ScopeId Museum = new ScopeId(new Id128(0x6D757365UL, 7UL));
        private static readonly ScopeId Harbor = new ScopeId(new Id128(0x68617262UL, 7UL));

        /// <summary>One declared scope: a child of the root, or a grandchild of a named parent.</summary>
        private static ScopeRecord Scope(ScopeId scope, ScopeId parent, int depth, bool isolateAllCapabilities)
        {
            return new ScopeRecord(
                scope,
                parent,
                depth,
                new IsolationSet(false, null),
                new IsolationSet(isolateAllCapabilities, null),
                null,
                null);
        }

        private static IReadOnlyList<ScopeRecord> DeclaredTree() => new List<ScopeRecord>
        {
            Scope(Story, Root, 1, false),
            Scope(Village, Story, 2, false),
            Scope(Museum, Story, 2, true),
            Scope(Harbor, Root, 1, false),
        };

        private static CompositionHost OpenLane(CompositionLaneSeed seed)
        {
            return new CompositionHost(
                World,
                Root,
                new CompositionHostSettings(ControlLaneCapacitySettings.Default, OperationExpirySettings.Default),
                new TestManifestSource(),
                null,
                PropagationMode.Automatic,
                seed);
        }

        [Test]
        public void ADeclaredTreeOpensTheCommittedCompositionWithEveryDeclaredScope()
        {
            CompositionHost lane = OpenLane(CompositionLaneSeed.InitialAssembly.WithScopes(DeclaredTree()));

            Assert.That(lane.Committed.Scopes.Count, Is.EqualTo(5), "the root plus four declared scopes");
            Assert.That(lane.Committed.Scopes.Root, Is.EqualTo(Root));
            Assert.That(lane.Committed.Scopes.Depth(Root), Is.Zero);
            Assert.That(lane.Committed.Scopes.Depth(Story), Is.EqualTo(1));
            Assert.That(lane.Committed.Scopes.Depth(Village), Is.EqualTo(2));
            Assert.That(lane.Committed.Scopes.Depth(Museum), Is.EqualTo(2));
            Assert.That(lane.Committed.Scopes.Depth(Harbor), Is.EqualTo(1));
            Assert.That(lane.Committed.Scopes.Depth(new ScopeId(new Id128(0x61626364UL, 7UL))), Is.EqualTo(-1));

            Assert.That(lane.Committed.Scopes.TryGet(Village, out ScopeRecord? village), Is.True);
            Assert.That(village, Is.Not.Null);
            Assert.That(village!.Parent, Is.EqualTo(Story), "a declared scope keeps its declared parent (P-010)");
            Assert.That(
                lane.Committed.Scopes.ChildrenOf(Story),
                Is.EquivalentTo(new[] { Museum, Village }),
                "children are enumerated in canonical identity order, not declaration order");

            Assert.That(lane.Committed.Scopes.TryGet(Museum, out ScopeRecord? museum), Is.True);
            Assert.That(
                museum!.CapabilityIsolation.AllContracts,
                Is.True,
                "a declared `*` capability boundary survives the seeded composition (P-016)");

            // P-006: the declared tree is part of the initial composition, so it publishes nothing.
            Assert.That(lane.Committed.Revision, Is.EqualTo(CompositionRevision.First));
            Assert.That(lane.Committed.Epoch, Is.EqualTo(AssemblyEpoch.First));
            Assert.That(lane.PublicationCount, Is.Zero, "opening a declared tree is not a publication");
            Assert.That(lane.Seed.DeclaresScopes, Is.True);
            Assert.That(lane.Seed.IsConsistent, Is.True);
        }

        [Test]
        public void ASeedWithoutADeclaredTreeStillOpensARootOnlyWorld()
        {
            foreach (CompositionLaneSeed seed in new[]
            {
                CompositionLaneSeed.InitialAssembly,
                CompositionLaneSeed.Unpublished,
                CompositionLaneSeed.FromPublishedAssembly(new CompositionRevision(7UL), new AssemblyEpoch(7UL)),
            })
            {
                CompositionHost lane = OpenLane(seed);

                Assert.That(lane.Committed.Scopes.Count, Is.EqualTo(1), seed.ToString());
                Assert.That(lane.Committed.Scopes.Root, Is.EqualTo(Root), seed.ToString());
                Assert.That(lane.Seed.DeclaresScopes, Is.False, seed.ToString());
                Assert.That(lane.Seed.Revision, Is.EqualTo(seed.Revision), seed.ToString());
                Assert.That(lane.Seed.Epoch, Is.EqualTo(seed.Epoch), seed.ToString());
            }
        }

        [Test]
        public void WithScopesKeepsTheSeedsPublicationAndConsistency()
        {
            CompositionLaneSeed bare = CompositionLaneSeed.FromPublishedAssembly(
                new CompositionRevision(4UL),
                new AssemblyEpoch(4UL));
            CompositionLaneSeed withTree = bare.WithScopes(DeclaredTree());

            Assert.That(withTree.Revision, Is.EqualTo(bare.Revision));
            Assert.That(withTree.Epoch, Is.EqualTo(bare.Epoch));
            Assert.That(withTree.IsJoined, Is.True);
            Assert.That(withTree.IsConsistent, Is.True);
            Assert.That(withTree.DeclaresScopes, Is.True);
            Assert.That(bare.DeclaresScopes, Is.False, "the original seed is unchanged");
            Assert.That(withTree.InitialScopes, Is.Not.Null);
            Assert.That(withTree.InitialScopes!.Count, Is.EqualTo(4));
            Assert.That(withTree.ToString(), Does.Contain("declaredScopes=4"));

            // An empty declaration is the root-only case, and a null one is the same as no declaration.
            Assert.That(bare.WithScopes(new List<ScopeRecord>()).DeclaresScopes, Is.False);
            Assert.That(bare.WithScopes(null).DeclaresScopes, Is.False);
        }

        [Test]
        public void AMalformedDeclaredTreeIsRefusedBeforeTheLaneIsExposed()
        {
            // A duplicate identity appears once in one world (P-004).
            Assert.Throws<ArgumentException>(() => OpenLane(
                CompositionLaneSeed.InitialAssembly.WithScopes(new List<ScopeRecord>
                {
                    Scope(Story, Root, 1, false),
                    Scope(Story, Root, 1, false),
                })));

            // A scope's parent must exist in the same world, so a declared tree cannot name an unknown ancestor.
            Assert.Throws<ArgumentException>(() => OpenLane(
                CompositionLaneSeed.InitialAssembly.WithScopes(new List<ScopeRecord>
                {
                    Scope(Village, Story, 2, false),
                })));

            // Depth must be exactly one greater than the parent's; a declared tree that disagrees would make every
            // depth-based scope query answer wrongly (P-010).
            Assert.Throws<ArgumentException>(() => OpenLane(
                CompositionLaneSeed.InitialAssembly.WithScopes(new List<ScopeRecord>
                {
                    Scope(Story, Root, 1, false),
                    Scope(Village, Story, 1, false),
                })));

            // A second root is not a tree, and a null record is not a scope.
            Assert.Throws<ArgumentException>(() => OpenLane(
                CompositionLaneSeed.InitialAssembly.WithScopes(new List<ScopeRecord>
                {
                    new ScopeRecord(Story, default(ScopeId), 0, new IsolationSet(false, null), new IsolationSet(false, null), null, null),
                })));

            var withNullRecord = new List<ScopeRecord>();
            withNullRecord.Add(null!);
            Assert.Throws<ArgumentException>(() => OpenLane(
                CompositionLaneSeed.InitialAssembly.WithScopes(withNullRecord)));
        }

        [Test]
        public void ADeclaredTreesRegistrationOrderDoesNotMatter()
        {
            // A world definition lists its scopes in whatever order reads best; the registry adds them shallowest
            // first and enumerates them canonically, so the declaration order never reaches the composition (P-008).
            var reversed = new List<ScopeRecord>
            {
                Scope(Harbor, Root, 1, false),
                Scope(Museum, Story, 2, true),
                Scope(Village, Story, 2, false),
                Scope(Story, Root, 1, false),
            };

            CompositionHost fromDeclared = OpenLane(CompositionLaneSeed.InitialAssembly.WithScopes(DeclaredTree()));
            CompositionHost fromReversed = OpenLane(CompositionLaneSeed.InitialAssembly.WithScopes(reversed));

            Assert.That(fromReversed.Committed.Scopes.Count, Is.EqualTo(fromDeclared.Committed.Scopes.Count));
            Assert.That(fromReversed.Committed.Scopes.Scopes, Is.EqualTo(fromDeclared.Committed.Scopes.Scopes));
            Assert.That(
                fromReversed.Committed.Scopes.ChildrenOf(Story),
                Is.EqualTo(fromDeclared.Committed.Scopes.ChildrenOf(Story)));
        }

        [Test]
        public void ADeclaredTreeIsNotTheSameAsASeedWithoutOne()
        {
            CompositionHost withTree = OpenLane(CompositionLaneSeed.InitialAssembly.WithScopes(DeclaredTree()));
            CompositionHost withoutTree = OpenLane(CompositionLaneSeed.InitialAssembly);

            // The declared scopes are real members of the committed composition, so a target may name one as its
            // owner scope without a further composition operation (P-010).
            Assert.That(withTree.Committed.Scopes.TryGet(Harbor, out ScopeRecord? _), Is.True);
            Assert.That(withoutTree.Committed.Scopes.TryGet(Harbor, out ScopeRecord? _), Is.False);
            Assert.That(withTree.FindScope(Harbor), Is.Not.Null);
            Assert.That(withoutTree.FindScope(Harbor), Is.Null);
            Assert.That(
                withTree.FindScope(Story)!.Depth,
                Is.EqualTo(1),
                "the scope snapshot reports the declared depth");
        }
    }
}
