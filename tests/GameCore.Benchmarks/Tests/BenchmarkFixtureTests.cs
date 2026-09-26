// GameCore.Benchmarks tests — the deterministic fixture generator at the shape 08 section 3 declares (GC-026).
//
// The fixture is the input of every measurement the benchmark reports, so a defect here is a defect in every number
// the gate publishes: a wrong target count changes the update-size denominators, a wrong scope tree makes the
// reparent subtree an unknown size, and a seed that changes the *shape* makes two recorded runs incomparable rather
// than differently loaded. This suite pins the declared shape on the generator's real output rather than on its
// constants, and pins the two seed rules separately:
//
//   * equal scale + equal seed  -> equal identities, equal descriptors, equal per-leaf distribution;
//   * different seed            -> the same declared shape, a different seeded-tag population.
//
// Sources in this folder run as plain-dotnet tests and as Unity EditMode tests.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Derivation;
using NUnit.Framework;

namespace GameCore.Benchmarks.Tests
{
    [TestFixture]
    public sealed class BenchmarkFixtureTests
    {
        /// <summary>A seed one step away from the recorded one: the smallest observable seed change.</summary>
        private const uint NeighbourSeed = BenchmarkWorkloads.DefaultSeed + 1U;

        [Test]
        public void TheGeneratorProducesTheDeclaredScopeAndTargetCounts()
        {
            BenchmarkFixture fixture = BenchmarkFixtureGenerator.Generate(BenchmarkScale.Default);

            Assert.That(fixture.Scale.Scopes, Is.EqualTo(BenchmarkWorkloads.DefaultScopes), "declared scale");
            Assert.That(fixture.Scale.Targets, Is.EqualTo(BenchmarkWorkloads.DefaultTargets), "declared scale");
            Assert.That(fixture.Scale.Seed, Is.EqualTo(BenchmarkWorkloads.DefaultSeed), "recorded seed");
            Assert.That(BenchmarkScale.Default.Scopes, Is.EqualTo(1000), "08's declared fixture is 1,000 scopes");
            Assert.That(BenchmarkScale.Default.Targets, Is.EqualTo(10000), "08's declared fixture is 10,000 targets");
            Assert.That(BenchmarkScale.Default.LiveScopes, Is.EqualTo(1000), "the live half is the same declared scale");
            Assert.That(BenchmarkScale.Default.LiveTargets, Is.EqualTo(10000), "the live half is the same declared scale");

            Assert.That(fixture.Scopes.Count, Is.EqualTo(1000), "every declared scope is generated");
            Assert.That(fixture.Targets.Count, Is.EqualTo(10000), "every declared target is generated");
            Assert.That(
                fixture.Scopes.Count,
                Is.EqualTo(1 + fixture.GroupScopes.Count + fixture.LeafScopes.Count),
                "the tree is the root plus every group and leaf the fixture reports");
            Assert.That(fixture.GroupScopes.Count, Is.EqualTo(BenchmarkFixture.GroupCount));
            Assert.That(
                fixture.LeafScopes.Count,
                Is.EqualTo(BenchmarkWorkloads.DefaultScopes - 1 - BenchmarkFixture.GroupCount));
            Assert.That(fixture.LeafScopes.Count, Is.EqualTo(989), "1,000 - root - ten groups");
            Assert.That(fixture.TargetsPerLeaf.Count, Is.EqualTo(fixture.LeafScopes.Count));
        }

        [Test]
        public void TheScopeTreeIsRootedAndEveryParentPrecedesItsChild()
        {
            BenchmarkFixture fixture = BenchmarkFixtureGenerator.Generate(BenchmarkScale.Default);
            Dictionary<Id128, int> index = ScopeIndex(fixture);

            Assert.That(fixture.Scopes[0].IsRoot, Is.True, "scope 0 is the world root (P-010)");
            Assert.That(fixture.Scopes[0].Parent.IsDefault, Is.True, "the root is the only parentless scope");
            Assert.That(fixture.RootScope.Equals(fixture.Scopes[0].Scope), Is.True, "RootScope names scope 0");

            for (int i = 1; i < fixture.Scopes.Count; i++)
            {
                DerivationScope scope = fixture.Scopes[i];
                Assert.That(scope.IsRoot, Is.False, "scope " + i.ToString() + " is not a second root");
                Assert.That(
                    index.ContainsKey(scope.Parent.Value),
                    Is.True,
                    "scope " + i.ToString() + " declares a parent that exists in the tree");
                Assert.That(
                    index[scope.Parent.Value],
                    Is.LessThan(i),
                    "scope " + i.ToString() + " has a parent generated before it, so the tree is acyclic (P-010)");
            }

            for (int g = 0; g < fixture.GroupScopes.Count; g++)
            {
                DerivationScope group = DeclaredScope(fixture, fixture.GroupScopes[g]);
                Assert.That(
                    group.Parent.Equals(fixture.RootScope),
                    Is.True,
                    "group " + g.ToString() + " hangs off the world root");
            }

            var groups = new HashSet<Id128>();
            for (int g = 0; g < fixture.GroupScopes.Count; g++)
            {
                groups.Add(fixture.GroupScopes[g].Value);
            }

            int reparentLeaves = 0;
            for (int leaf = 0; leaf < fixture.LeafScopes.Count; leaf++)
            {
                DerivationScope scope = DeclaredScope(fixture, fixture.LeafScopes[leaf]);
                Assert.That(
                    groups.Contains(scope.Parent.Value),
                    Is.True,
                    "leaf " + leaf.ToString() + " hangs off a declared group, not off the root");
                if (scope.Parent.Equals(fixture.ReparentScope))
                {
                    reparentLeaves++;
                }
            }

            Assert.That(
                reparentLeaves,
                Is.EqualTo(BenchmarkFixture.ReparentLeafCount),
                "the reparent branch is exactly the declared number of leaves");
            Assert.That(fixture.LeafScopes.Count, Is.GreaterThan(reparentLeaves), "the fixture has leaves outside that branch");
        }

        [Test]
        public void EveryTargetIsOwnedByALeafAndThePerLeafCountsDescribeThem()
        {
            BenchmarkFixture fixture = BenchmarkFixtureGenerator.Generate(BenchmarkScale.Default);
            var leaves = new HashSet<Id128>();
            for (int leaf = 0; leaf < fixture.LeafScopes.Count; leaf++)
            {
                leaves.Add(fixture.LeafScopes[leaf].Value);
            }

            var ownedByLeaf = new Dictionary<Id128, int>();
            for (int t = 0; t < fixture.Targets.Count; t++)
            {
                DerivationTarget target = fixture.Targets[t];
                Assert.That(
                    leaves.Contains(target.Scope.Value),
                    Is.True,
                    "target " + t.ToString() + " is owned by a generated leaf scope (P-010)");
                Assert.That(target.Scope.IsDefault, Is.False, "a live target has one real owner scope");
                Assert.That(target.Target.IsDefault, Is.False, "a generated target identity is never the zero identity");

                int count;
                ownedByLeaf.TryGetValue(target.Scope.Value, out count);
                ownedByLeaf[target.Scope.Value] = count + 1;
            }

            int distributed = 0;
            for (int leaf = 0; leaf < fixture.LeafScopes.Count; leaf++)
            {
                int declared = fixture.TargetsPerLeaf[leaf];
                int owned;
                ownedByLeaf.TryGetValue(fixture.LeafScopes[leaf].Value, out owned);
                Assert.That(
                    owned,
                    Is.EqualTo(declared),
                    "leaf " + leaf.ToString() + " declares " + declared.ToString()
                    + " targets and owns exactly that many");
                distributed += declared;
            }

            Assert.That(distributed, Is.EqualTo(fixture.Targets.Count), "the distribution covers every declared target");

            // Every target identity is distinct, or the snapshot indexes would silently collapse two targets.
            var identities = new HashSet<Id128>();
            for (int t = 0; t < fixture.Targets.Count; t++)
            {
                Assert.That(
                    identities.Add(fixture.Targets[t].Target.Value),
                    Is.True,
                    "target identity " + t.ToString() + " is unique");
            }
        }

        [Test]
        public void EveryTargetDeclaresExactlyOneOfTheThreeSchemaFamilies()
        {
            BenchmarkFixture fixture = BenchmarkFixtureGenerator.Generate(BenchmarkScale.Default);
            var perFamily = new int[BenchmarkFixture.SchemaFamilies];

            for (int t = 0; t < fixture.Targets.Count; t++)
            {
                DerivationTarget target = fixture.Targets[t];
                IReadOnlyList<SchemaRef> declared = target.Descriptor.SupportedSchemas;
                Assert.That(
                    declared.Count,
                    Is.EqualTo(1),
                    "target " + t.ToString() + " declares exactly one supported schema, so 'a family' is unambiguous");

                int family = FamilyOf(declared[0]);
                Assert.That(
                    family,
                    Is.GreaterThanOrEqualTo(0),
                    "target " + t.ToString() + " declares one of the three declared schema families");
                perFamily[family]++;
                Assert.That(
                    target.Descriptor.Recipe.Schema.Id.Equals(declared[0].Id),
                    Is.True,
                    "target " + t.ToString() + " derives its recipe from the schema it declares");
                Assert.That(
                    target.Descriptor.Recipe.Id.Equals(BenchmarkIds.Definition(BenchmarkNames.FamilyRecipe(family))),
                    Is.True,
                    "target " + t.ToString() + " derives its recipe identity from the same family");
            }

            int total = 0;
            for (int family = 0; family < perFamily.Length; family++)
            {
                Assert.That(
                    perFamily[family],
                    Is.GreaterThan(0),
                    "schema family " + family.ToString() + " appears in the fixture (08 asks for three)");
                total += perFamily[family];
            }

            Assert.That(total, Is.EqualTo(fixture.Targets.Count));
            Assert.That(fixture.SchemaFamilyCount, Is.EqualTo(BenchmarkFixture.SchemaFamilies));
            Assert.That(BenchmarkFixture.SchemaFamilies, Is.EqualTo(3), "08's fixture declares three schema families");
        }

        [Test]
        public void OneTargetCarriesTheOneTagAndExactlyHundredCarryTheHundredTag()
        {
            BenchmarkFixture fixture = BenchmarkFixtureGenerator.Generate(BenchmarkScale.Default);
            Id128 tagAll = StableNameKeyDerivation.Derive(BenchmarkNames.TagAll);
            Id128 tagOne = StableNameKeyDerivation.Derive(BenchmarkNames.TagOne);
            Id128 tagHundred = StableNameKeyDerivation.Derive(BenchmarkNames.TagHundred);

            var oneTargets = new List<int>();
            var hundredTargets = new List<int>();
            for (int t = 0; t < fixture.Targets.Count; t++)
            {
                DerivationTarget target = fixture.Targets[t];
                Assert.That(
                    target.DeclaresTag(tagAll),
                    Is.True,
                    "every target carries the all-targets tag, so a whole-world update reaches it by construction");
                if (target.DeclaresTag(tagOne))
                {
                    oneTargets.Add(t);
                }

                if (target.DeclaresTag(tagHundred))
                {
                    hundredTargets.Add(t);
                }
            }

            Assert.That(oneTargets.Count, Is.EqualTo(1), "exactly one target carries the one-target tag");
            Assert.That(hundredTargets.Count, Is.EqualTo(100), "exactly 100 targets carry the hundred-target tag");
            Assert.That(BenchmarkFixture.HundredTargets, Is.EqualTo(BenchmarkWorkloads.HundredTargets));

            // The tagged targets are the first generated ones, which is what makes an update's affected count exact:
            // 1 target is index 0 and 100 targets are indices 0..99.
            Assert.That(
                fixture.Targets[oneTargets[0]].Target.Equals(BenchmarkIds.Target(0)),
                Is.True,
                "the one-target tag is on generated target 0");
            for (int i = 0; i < hundredTargets.Count; i++)
            {
                Assert.That(
                    fixture.Targets[hundredTargets[i]].Target.Equals(BenchmarkIds.Target(i)),
                    Is.True,
                    "the hundred-target tag is on generated targets 0..99, in order");
            }

            for (int t = 0; t < fixture.Targets.Count; t++)
            {
                if (!fixture.Targets[t].DeclaresTag(tagHundred))
                {
                    Assert.That(
                        fixture.Targets[t].Target.Equals(BenchmarkIds.Target(t)),
                        Is.True,
                        "the untagged target at position " + t.ToString() + " is generated target " + t.ToString());
                    Assert.That(t, Is.GreaterThanOrEqualTo(100), "no target before index 100 is untagged");
                }
            }
        }

        [Test]
        public void TheSharedCapabilityIsExcludedOnTheDeclaredSubtreeAndOnTheDeclaredSingleTarget()
        {
            BenchmarkFixture fixture = BenchmarkFixtureGenerator.Generate(BenchmarkScale.Default);
            Id128 shared = BenchmarkIds.CapabilityValue(BenchmarkNames.SharedCapability);

            DerivationScope firstLeaf = DeclaredScope(fixture, fixture.LeafScopes[0]);
            Assert.That(firstLeaf.Exclusions.Count, Is.EqualTo(1), "the first leaf declares exactly one exclusion");
            ExclusionRule subtree = firstLeaf.Exclusions[0];
            Assert.That(subtree.Kind, Is.EqualTo(ExclusionTargetKind.Capability));
            Assert.That(subtree.TargetId.Equals(shared), Is.True, "the subtree exclusion addresses the shared capability");
            Assert.That(subtree.AtScope.Equals(fixture.LeafScopes[0]), Is.True, "at the leaf that declares it");
            Assert.That(subtree.HasTarget, Is.False, "a subtree exclusion names no single target");
            Assert.That(subtree.AppliesToSubtree, Is.True, "and it reaches the leaf's descendants (P-016)");

            var excludedTargets = new List<DerivationTarget>();
            for (int t = 0; t < fixture.Targets.Count; t++)
            {
                if (fixture.Targets[t].Descriptor.Exclusions.Count > 0)
                {
                    excludedTargets.Add(fixture.Targets[t]);
                }
            }

            Assert.That(excludedTargets.Count, Is.EqualTo(1), "exactly one target declares a target-level exclusion");
            DerivationTarget excluded = excludedTargets[0];
            Assert.That(
                excluded.Target.Equals(BenchmarkIds.Target(1)),
                Is.True,
                "the declared single excluded target is generated target 1");
            Assert.That(excluded.Descriptor.Exclusions.Count, Is.EqualTo(1));
            ExclusionRule single = excluded.Descriptor.Exclusions[0];
            Assert.That(single.Kind, Is.EqualTo(ExclusionTargetKind.Capability));
            Assert.That(single.TargetId.Equals(shared), Is.True, "the single-target exclusion addresses the shared capability");
            Assert.That(single.AtTarget.Equals(excluded.Target), Is.True, "at that target");
            Assert.That(single.HasScope, Is.False, "a target-level exclusion names no scope");
            Assert.That(single.AppliesToSubtree, Is.False, "and it reaches only the target itself");

            // The fixture declares both P-016 shapes: one subtree exclusion and one named capability boundary.
            var isolated = new List<int>();
            for (int leaf = 0; leaf < fixture.LeafScopes.Count; leaf++)
            {
                IsolationSet isolation = DeclaredScope(fixture, fixture.LeafScopes[leaf]).CapabilityIsolation;
                if (isolation.AllContracts || isolation.Contracts.Count > 0)
                {
                    isolated.Add(leaf);
                }
            }

            Assert.That(isolated.Count, Is.EqualTo(1), "exactly one leaf declares a named capability boundary (P-016)");
            Assert.That(isolated[0], Is.EqualTo(BenchmarkFixture.ReparentLeafCount), "the boundary is on the first leaf outside the branch");
            IsolationSet boundary = DeclaredScope(fixture, fixture.LeafScopes[isolated[0]]).CapabilityIsolation;
            Assert.That(boundary.AllContracts, Is.False, "the boundary names one capability rather than blocking every contract");
            Assert.That(boundary.Contracts.Count, Is.EqualTo(1));
            Assert.That(boundary.Contracts[0].Equals(shared), Is.True, "and that capability is the shared one");
        }

        [Test]
        public void TheDeclaredProviderCountsAndInstallationsAgreeWithTheShape()
        {
            BenchmarkFixture fixture = BenchmarkFixtureGenerator.Generate(BenchmarkScale.Default);

            int uniqueStride = BenchmarkFixture.UniqueProviderStride;
            int expectedUnique = (fixture.LeafScopes.Count + uniqueStride - 1) / uniqueStride;
            Assert.That(fixture.SharedProviderCount, Is.EqualTo(fixture.GroupScopes.Count + 1), "the root plus every group");
            Assert.That(fixture.SharedProviderCount, Is.EqualTo(11));
            Assert.That(fixture.UniqueProviderCount, Is.EqualTo(expectedUnique), "one unique leaf provider per stride");
            Assert.That(fixture.UniqueProviderCount, Is.EqualTo(99));
            Assert.That(
                fixture.Installs.Count,
                Is.EqualTo(1 + fixture.GroupScopes.Count + expectedUnique),
                "the base installations are the root provider, the group providers and the unique leaf providers");
            Assert.That(fixture.Installs.Count, Is.EqualTo(110));

            var identities = new HashSet<Id128>();
            var scopesOfInstalls = new HashSet<Id128>();
            int sharedDeclarations = 0;
            int localDeclarations = 0;
            for (int i = 0; i < fixture.Installs.Count; i++)
            {
                DerivationInstall install = fixture.Installs[i];
                Assert.That(identities.Add(install.Instance.Value), Is.True, "installation " + i.ToString() + " has a unique identity");
                Assert.That(install.IsActive, Is.True, "a base installation is Active, because only an active one contributes (P-012)");
                Assert.That(install.Scope.IsDefault, Is.False, "an installation always names its owning scope (P-010)");
                Assert.That(install.Record.Instance.Equals(install.Instance), Is.True, "the record names the same installation");
                Assert.That(install.Scope.Equals(install.Record.Scope), Is.True, "the manifest is installed at the record's scope");
                Assert.That(install.Manifest.DerivationRules.Count, Is.EqualTo(1), "each base installation declares one rule");
                Assert.That(install.Record.Scope.IsDefault, Is.False, "an installation always names a real owning scope (P-010)");
                scopesOfInstalls.Add(install.Scope.Value);

                for (int c = 0; c < install.Manifest.CapabilityContracts.Count; c++)
                {
                    CapabilityContract contract = install.Manifest.CapabilityContracts[c];
                    if (contract.Capability.Capability.Equals(fixture.SharedCapability))
                    {
                        sharedDeclarations++;
                    }

                    if (contract.Capability.Capability.Equals(fixture.LocalCapability))
                    {
                        localDeclarations++;
                    }

                    Assert.That(
                        contract.Capability.Capability.Equals(fixture.UpdateCapability),
                        Is.False,
                        "no base installation declares the update contract; the update workload mounts the one that does");
                    Assert.That(contract.OutputSlots.Count, Is.EqualTo(1), "a declared contract carries its one slot schema");
                    Assert.That(contract.SlotPolicies.Count, Is.EqualTo(1), "and its one composition policy");
                    Assert.That(contract.SlotPolicies[0].Policy, Is.EqualTo(CompositionPolicy.Additive));
                }
            }

            Assert.That(sharedDeclarations, Is.EqualTo(1), "one capability identity has one declaration (P-017)");
            Assert.That(localDeclarations, Is.EqualTo(1), "and so does the unique provider's capability");
            Assert.That(scopesOfInstalls.Count, Is.EqualTo(1 + fixture.GroupScopes.Count + expectedUnique), "each installation sits at its own scope");

            Assert.That(fixture.Contracts.Count, Is.EqualTo(3), "the fixture declares three contracts (P-017, P-019)");
            Assert.That(fixture.Contracts[0].Capability.Capability.Equals(fixture.SharedCapability), Is.True);
            Assert.That(fixture.Contracts[1].Capability.Capability.Equals(fixture.LocalCapability), Is.True);
            Assert.That(fixture.Contracts[2].Capability.Capability.Equals(fixture.UpdateCapability), Is.True);
            Assert.That(fixture.SharedCapability.Equals(BenchmarkIds.Capability(BenchmarkNames.SharedCapability)), Is.True);
            Assert.That(fixture.LocalCapability.Equals(BenchmarkIds.Capability(BenchmarkNames.LocalCapability)), Is.True);
            Assert.That(fixture.UpdateSlot.Equals(BenchmarkIds.Slot(BenchmarkNames.UpdateSlot)), Is.True);
        }

        [Test]
        public void TheReparentBranchHoldsExactlyTheDeclaredHundredTargets()
        {
            BenchmarkFixture fixture = BenchmarkFixtureGenerator.Generate(BenchmarkScale.Default);

            Assert.That(fixture.ReparentScope.Equals(fixture.GroupScopes[0]), Is.True, "the reparent branch is the first group");
            Assert.That(fixture.SecondProviderScope.Equals(fixture.GroupScopes[1]), Is.True, "and it moves under the second");
            Assert.That(
                fixture.ReparentScopeTargets,
                Is.EqualTo(BenchmarkFixture.ReparentLeafCount * BenchmarkFixture.ReparentTargetsPerLeaf),
                "the branch is its declared leaves times the declared targets per leaf");
            Assert.That(fixture.ReparentScopeTargets, Is.EqualTo(100), "08 asks for a 100-target subtree");

            var branchScopes = new HashSet<Id128>();
            branchScopes.Add(fixture.ReparentScope.Value);
            for (int leaf = 0; leaf < fixture.LeafScopes.Count; leaf++)
            {
                if (DeclaredScope(fixture, fixture.LeafScopes[leaf]).Parent.Equals(fixture.ReparentScope))
                {
                    branchScopes.Add(fixture.LeafScopes[leaf].Value);
                }
            }

            int inBranch = 0;
            for (int t = 0; t < fixture.Targets.Count; t++)
            {
                if (branchScopes.Contains(fixture.Targets[t].Scope.Value))
                {
                    inBranch++;
                }
            }

            Assert.That(inBranch, Is.EqualTo(fixture.ReparentScopeTargets), "the declared subtree target count is the real one");
            Assert.That(inBranch, Is.LessThan(fixture.Targets.Count), "the branch is a real subtree, not the world");
        }

        [Test]
        public void TargetCountForSizeClampsToTheGeneratedWorld()
        {
            BenchmarkFixture fixture = BenchmarkFixtureGenerator.Generate(BenchmarkScale.Default);

            Assert.That(fixture.TargetCountForSize(1), Is.EqualTo(1));
            Assert.That(fixture.TargetCountForSize(100), Is.EqualTo(100));
            Assert.That(fixture.TargetCountForSize(10000), Is.EqualTo(10000));
            Assert.That(fixture.TargetCountForSize(20000), Is.EqualTo(fixture.Targets.Count), "a size beyond the world is the whole world");
            Assert.That(fixture.TargetCountForSize(0), Is.EqualTo(0), "a zero size affects no target");
            Assert.That(fixture.TargetCountForSize(-5), Is.EqualTo(-5), "the helper clamps only at the world's upper bound");
            Assert.That(BenchmarkFixtureVariants.DeclaredUpdateSize(fixture, 100), Is.EqualTo(100));
            Assert.That(BenchmarkFixtureVariants.DescribeUpdate(100, 0), Is.EqualTo("updateSize=100;repetition=0"));
        }

        [Test]
        public void EqualScalesAndSeedsGenerateEqualFixtures()
        {
            BenchmarkFixture first = BenchmarkFixtureGenerator.Generate(BenchmarkScale.Default);
            BenchmarkFixture second = BenchmarkFixtureGenerator.Generate(BenchmarkScale.Default);

            Assert.That(second.Describe(), Is.EqualTo(first.Describe()), "one seed produces one recorded shape");
            Assert.That(second.Scopes.Count, Is.EqualTo(first.Scopes.Count));
            Assert.That(second.Installs.Count, Is.EqualTo(first.Installs.Count));
            Assert.That(second.UniqueProviderCount, Is.EqualTo(first.UniqueProviderCount));
            Assert.That(second.SeededTagTargetCount, Is.EqualTo(first.SeededTagTargetCount));

            for (int i = 0; i < first.Scopes.Count; i++)
            {
                Assert.That(second.Scopes[i].Scope.Equals(first.Scopes[i].Scope), Is.True, "scope identity " + i.ToString());
                Assert.That(second.Scopes[i].Parent.Equals(first.Scopes[i].Parent), Is.True, "scope parent " + i.ToString());
                Assert.That(
                    second.Scopes[i].Exclusions.Count,
                    Is.EqualTo(first.Scopes[i].Exclusions.Count),
                    "scope exclusions " + i.ToString());
                Assert.That(
                    second.Scopes[i].CapabilityIsolation.Contracts.Count,
                    Is.EqualTo(first.Scopes[i].CapabilityIsolation.Contracts.Count),
                    "scope boundary " + i.ToString());
            }

            for (int i = 0; i < first.Installs.Count; i++)
            {
                Assert.That(second.Installs[i].Instance.Equals(first.Installs[i].Instance), Is.True, "installation identity " + i.ToString());
                Assert.That(second.Installs[i].Scope.Equals(first.Installs[i].Scope), Is.True, "installation scope " + i.ToString());
                Assert.That(
                    second.Installs[i].Manifest.DerivationRules[0].RuleId.Equals(first.Installs[i].Manifest.DerivationRules[0].RuleId),
                    Is.True,
                    "rule identity " + i.ToString());
            }

            for (int t = 0; t < first.Targets.Count; t++)
            {
                Assert.That(second.Targets[t].Target.Equals(first.Targets[t].Target), Is.True, "target identity " + t.ToString());
                Assert.That(second.Targets[t].Scope.Equals(first.Targets[t].Scope), Is.True, "target owner " + t.ToString());
                Assert.That(second.Targets[t].Descriptor.Recipe.Equals(first.Targets[t].Descriptor.Recipe), Is.True, "target recipe " + t.ToString());
                Assert.That(second.Targets[t].Descriptor.Tags.Count, Is.EqualTo(first.Targets[t].Descriptor.Tags.Count), "target tags " + t.ToString());
                for (int tag = 0; tag < first.Targets[t].Descriptor.Tags.Count; tag++)
                {
                    Assert.That(
                        second.Targets[t].Descriptor.Tags[tag].Equals(first.Targets[t].Descriptor.Tags[tag]),
                        Is.True,
                        "target " + t.ToString() + " tag " + tag.ToString());
                }
            }

            for (int leaf = 0; leaf < first.TargetsPerLeaf.Count; leaf++)
            {
                Assert.That(second.TargetsPerLeaf[leaf], Is.EqualTo(first.TargetsPerLeaf[leaf]), "per-leaf count " + leaf.ToString());
            }
        }

        [Test]
        public void ADifferentSeedKeepsTheShapeAndMovesOnlyTheSeededTagPopulation()
        {
            BenchmarkFixture recorded = BenchmarkFixtureGenerator.Generate(BenchmarkScale.Default);
            BenchmarkFixture neighbour = BenchmarkFixtureGenerator.Generate(
                new BenchmarkScale(
                    BenchmarkScale.Default.Scopes,
                    BenchmarkScale.Default.Targets,
                    BenchmarkScale.Default.LiveScopes,
                    BenchmarkScale.Default.LiveTargets,
                    NeighbourSeed));

            Assert.That(neighbour.Scopes.Count, Is.EqualTo(recorded.Scopes.Count), "the seed never changes the declared shape");
            Assert.That(neighbour.Targets.Count, Is.EqualTo(recorded.Targets.Count));
            Assert.That(neighbour.GroupScopes.Count, Is.EqualTo(recorded.GroupScopes.Count));
            Assert.That(neighbour.LeafScopes.Count, Is.EqualTo(recorded.LeafScopes.Count));
            Assert.That(neighbour.UniqueProviderCount, Is.EqualTo(recorded.UniqueProviderCount));
            Assert.That(neighbour.Installs.Count, Is.EqualTo(recorded.Installs.Count));
            Assert.That(neighbour.ReparentScopeTargets, Is.EqualTo(recorded.ReparentScopeTargets));
            Assert.That(neighbour.Scale.Seed, Is.EqualTo(NeighbourSeed));

            for (int leaf = 0; leaf < recorded.TargetsPerLeaf.Count; leaf++)
            {
                Assert.That(
                    neighbour.TargetsPerLeaf[leaf],
                    Is.EqualTo(recorded.TargetsPerLeaf[leaf]),
                    "a different seed keeps the per-leaf distribution at leaf " + leaf.ToString());
            }

            Id128 seededTag = StableNameKeyDerivation.Derive(BenchmarkNames.TagSeeded);
            var recordedSeeded = new HashSet<Id128>();
            for (int t = 0; t < recorded.Targets.Count; t++)
            {
                if (recorded.Targets[t].DeclaresTag(seededTag))
                {
                    recordedSeeded.Add(recorded.Targets[t].Target.Value);
                }
            }

            var neighbourSeeded = new HashSet<Id128>();
            for (int t = 0; t < neighbour.Targets.Count; t++)
            {
                if (neighbour.Targets[t].DeclaresTag(seededTag))
                {
                    neighbourSeeded.Add(neighbour.Targets[t].Target.Value);
                }
            }

            Assert.That(recordedSeeded.Count, Is.EqualTo(recorded.SeededTagTargetCount), "the recorded count is the real one");
            Assert.That(neighbourSeeded.Count, Is.EqualTo(neighbour.SeededTagTargetCount), "the neighbour count is the real one");
            Assert.That(
                recordedSeeded.Count,
                Is.GreaterThan(0),
                "the recorded seed actually marks targets, so a workload can report which ones");
            Assert.That(
                recordedSeeded.Count,
                Is.LessThan(recorded.Targets.Count),
                "and it does not mark every target, so the seeded tag stays a distinguishing load");
            Assert.That(
                neighbour.SeededTagTargetCount,
                Is.Not.EqualTo(recorded.SeededTagTargetCount),
                "a different seed moves the seeded marker population (" + recorded.SeededTagTargetCount.ToString()
                + " -> " + neighbour.SeededTagTargetCount.ToString() + ")");

            bool differs = recordedSeeded.Count != neighbourSeeded.Count;
            foreach (Id128 identity in neighbourSeeded)
            {
                if (!recordedSeeded.Contains(identity))
                {
                    differs = true;
                    break;
                }
            }

            Assert.That(differs, Is.True, "the seeded target *set* is seed-dependent");
            Assert.That(recorded.SeededTagTargetCount, Is.EqualTo(481), "the recorded seed's seeded population is stable");
        }

        [Test]
        public void GenerateRejectsAScaleBelowTheDeclaredMinimumAndAcceptsItExactly()
        {
            Assert.That(
                BenchmarkFixtureGenerator.MinimumScopes,
                Is.EqualTo(1 + BenchmarkFixture.GroupCount + BenchmarkFixture.ReparentLeafCount),
                "the minimum is the root, the groups and the reparent leaves");
            Assert.That(BenchmarkFixtureGenerator.MinimumScopes, Is.EqualTo(21));

            var tooSmall = new BenchmarkScale(
                BenchmarkFixtureGenerator.MinimumScopes - 1,
                BenchmarkFixtureGenerator.MinimumScopes - 1,
                1,
                1,
                BenchmarkWorkloads.DefaultSeed);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => BenchmarkFixtureGenerator.Generate(tooSmall),
                "a scale with no room for the declared branches is refused, never silently reshaped");

            var exact = new BenchmarkScale(
                BenchmarkFixtureGenerator.MinimumScopes,
                BenchmarkFixtureGenerator.MinimumScopes,
                BenchmarkFixtureGenerator.MinimumScopes,
                BenchmarkFixtureGenerator.MinimumScopes,
                1U);
            BenchmarkFixture smallest = BenchmarkFixtureGenerator.Generate(exact);
            Assert.That(smallest.Scopes.Count, Is.EqualTo(BenchmarkFixtureGenerator.MinimumScopes));
            Assert.That(smallest.Targets.Count, Is.EqualTo(BenchmarkFixtureGenerator.MinimumScopes));
            Assert.That(smallest.GroupScopes.Count, Is.EqualTo(BenchmarkFixture.GroupCount));
            Assert.That(smallest.LeafScopes.Count, Is.EqualTo(BenchmarkFixture.ReparentLeafCount));
            // The reparent branch is capped by the declared target count, not by the branch's own capacity
            // (minimum of the declared targets and leaves x targets-per-leaf), so at this scale the whole world is
            // the branch. The declared 100 only holds where the scale supplies 100 targets.
            Assert.That(
                smallest.ReparentScopeTargets,
                Is.EqualTo(Math.Min(smallest.Targets.Count, BenchmarkFixture.ReparentLeafCount * BenchmarkFixture.ReparentTargetsPerLeaf)),
                "the branch holds its declared capacity or the declared targets, whichever is smaller");
            Assert.That(smallest.ReparentScopeTargets, Is.EqualTo(smallest.Targets.Count));
            Assert.That(
                smallest.ReparentScopeTargets,
                Is.LessThan(BenchmarkFixture.ReparentLeafCount * BenchmarkFixture.ReparentTargetsPerLeaf),
                "at the minimum scale the branch cannot reach its declared hundred");
        }

        [Test]
        public void TheScaleRefusesNonPositiveCounts()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BenchmarkScale(0, 1, 1, 1, 0U));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BenchmarkScale(1, 0, 1, 1, 0U));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BenchmarkScale(1, 1, 0, 1, 0U));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BenchmarkScale(1, 1, 1, 0, 0U));

            var scale = new BenchmarkScale(2, 3, 4, 5, 6U);
            Assert.That(scale.Scopes, Is.EqualTo(2));
            Assert.That(scale.Targets, Is.EqualTo(3));
            Assert.That(scale.LiveScopes, Is.EqualTo(4));
            Assert.That(scale.LiveTargets, Is.EqualTo(5));
            Assert.That(scale.Seed, Is.EqualTo(6U));
            Assert.That(scale.ToString().Contains("scopes=2", StringComparison.Ordinal), Is.True);
            Assert.That(scale.ToString().Contains("seed=6", StringComparison.Ordinal), Is.True);
        }

        [Test]
        public void TheFixtureIdsAreDerivedFromRoleAndIndexRatherThanFromText()
        {
            Assert.That(BenchmarkIds.Id(BenchmarkIds.Role.Scope, 0).Equals(BenchmarkIds.Id(BenchmarkIds.Role.Scope, 0)), Is.True);
            Assert.That(BenchmarkIds.Id(BenchmarkIds.Role.Scope, 0).Equals(BenchmarkIds.Id(BenchmarkIds.Role.Target, 0)), Is.False, "roles never collide");
            Assert.That(BenchmarkIds.Id(BenchmarkIds.Role.Target, 1).Equals(BenchmarkIds.Id(BenchmarkIds.Role.Target, 2)), Is.False);
            Assert.That(BenchmarkIds.Id(BenchmarkIds.Role.Target, 7).Equals(BenchmarkIds.Target(7).Value), Is.True);
            Assert.That(BenchmarkIds.Scope(BenchmarkIds.Role.Scope, 3).Value.Equals(BenchmarkIds.Id(BenchmarkIds.Role.Scope, 3)), Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkIds.Id(BenchmarkIds.Role.Target, -1));
            Assert.That(BenchmarkIds.Rule(1, 2).Equals(BenchmarkIds.Rule(1, 2)), Is.True);
            Assert.That(BenchmarkIds.Rule(1, 2).Equals(BenchmarkIds.Rule(2, 2)), Is.False, "two rule kinds never share an identity");
            Assert.That(BenchmarkIds.Instance(4).Equals(BenchmarkIds.Instance(4)), Is.True);
            Assert.That(BenchmarkIds.Installation(BenchmarkIds.Instance(4)).Value.Equals(BenchmarkIds.Instance(4).Value), Is.True);
            Assert.That(BenchmarkIds.Seeded(0U, 0), Is.EqualTo(0U), "index 0 under seed 0 is the zero mix");
            Assert.That(BenchmarkIds.Seeded(1U, 0), Is.EqualTo(1U), "index 0 under seed 1 is the seed itself");
            Assert.That(BenchmarkIds.Seeded(1U, 1).Equals(BenchmarkIds.Seeded(1U, 1)), Is.True, "the mix is pure");
            Assert.That(BenchmarkIds.Capability(BenchmarkNames.SharedCapability).Equals(BenchmarkIds.Capability(BenchmarkNames.SharedCapability)), Is.True);
            Assert.That(BenchmarkIds.Key(BenchmarkNames.Reducer).Equals(BenchmarkIds.Key(BenchmarkNames.Reducer)), Is.True);
            Assert.That(BenchmarkIds.Key(BenchmarkNames.Reducer, 2U).Equals(BenchmarkIds.Key(BenchmarkNames.Reducer, 1U)), Is.False, "the version is part of the key");
            Assert.That(BenchmarkIds.CapabilityRef(BenchmarkNames.UpdateCapability, 3U).Version, Is.EqualTo(3U));
            Assert.That(BenchmarkIds.SchemaRef(BenchmarkNames.UpdateSchema, 2U).Version, Is.EqualTo(2U));
            Assert.That(BenchmarkNames.FamilySchema(1), Is.EqualTo("bench.schema.family-1"));
            Assert.That(BenchmarkNames.FamilyRecipe(2), Is.EqualTo("bench.recipe.family-2"));
        }

        private static Dictionary<Id128, int> ScopeIndex(BenchmarkFixture fixture)
        {
            var index = new Dictionary<Id128, int>(fixture.Scopes.Count);
            for (int i = 0; i < fixture.Scopes.Count; i++)
            {
                index[fixture.Scopes[i].Scope.Value] = i;
            }

            return index;
        }

        private static DerivationScope DeclaredScope(BenchmarkFixture fixture, ScopeId scope)
        {
            for (int i = 0; i < fixture.Scopes.Count; i++)
            {
                if (fixture.Scopes[i].Scope.Equals(scope))
                {
                    return fixture.Scopes[i];
                }
            }

            throw new InvalidOperationException("scope " + scope.ToString() + " is not part of the fixture (P-010).");
        }

        private static int FamilyOf(SchemaRef schema)
        {
            for (int family = 0; family < BenchmarkFixture.SchemaFamilies; family++)
            {
                if (schema.Id.Equals(BenchmarkIds.Schema(BenchmarkNames.FamilySchema(family))))
                {
                    return family;
                }
            }

            return -1;
        }
    }
}
