// GameCore.W4Profile.Tests — the Wave 4 provisional generic-execution profile gate, EditMode half (GC-012).
//
// The scenario under test (`GameCore.Validation.ProbeHost.W4ProfileScenario`) is the same one the player probe mode
// `-probeW4Profile` runs, so these cases are the Editor half of the same evidence. Two checks can only exist here:
//
//   * the build-time `.asmdef` audit, because a player cannot see a project tree;
//   * the pre-mount baseline, because an Editor test can create and dispose worlds deterministically around it.
//
// Everything else asserts on the facts the scenario observed, so a regression is reported by value rather than
// only by a boolean.
#nullable enable
using System.Collections.Generic;
using System.IO;
using GameCore.Gameplay.Cards.Fixtures;
using GameCore.Rules.Cards;
using GameCore.Unity.Runtime;
using GameCore.Validation.ProbeHost;
using GameCore.Validation.Slices;
using NUnit.Framework;

namespace GameCore.W4Profile.Tests
{
    [TestFixture]
    public sealed class W4ProfileIntegrationTests
    {
        private static W4ProfileScenarioResult result = null!;

        [OneTimeSetUp]
        public void RunTheProfileGateOnce()
        {
            UnityWorldRegistry.ResetAll();
            result = W4ProfileScenario.Run();
        }

        [OneTimeTearDown]
        public void TearDownTheOwnedWorlds() => UnityWorldRegistry.ResetAll();

        [Test]
        public void EveryProfileCheckPasses()
        {
            Assert.That(result.AllPassed, Is.True, result.Describe());
            Assert.That(result.Steps, Is.Not.Empty);
        }

        [Test]
        public void BothGeneratedFamilyEntriesResolveAndAreInactive()
        {
            Assert.That(result.Facts.BothEntriesResolved, Is.True);
            Assert.That(result.Facts.RegistryAtStart, Is.EqualTo(0), "nothing may be mounted before the probe runs");
            Assert.That(result.Facts.RegistryAfterAll, Is.EqualTo(result.Facts.RegistryAtStart));
            Assert.That(result.Facts.NarrativeEntrySystems, Is.EqualTo(6));
            Assert.That(result.Facts.CardEntrySystems, Is.EqualTo(4));
        }

        [Test]
        public void BothFamiliesRunLateInOneProcessAndLeaveNoWorld()
        {
            Assert.That(result.Facts.RegistryAtStart, Is.EqualTo(0));
            Assert.That(result.Facts.RegistryAfterAll, Is.EqualTo(0));
            Assert.That(result.Facts.NarrativeGeneratedSteps, Is.GreaterThan(0));
            Assert.That(result.Facts.CardGeneratedSteps, Is.GreaterThan(0));
            Assert.That(Step("w4-both-families-mount-late-and-leave-no-world").Passed, Is.True);
        }

        [Test]
        public void TheNarrativeRunsMatchTheDeclaredCanonicalTraceAndEachOther()
        {
            Assert.That(result.Facts.NarrativeGeneratedMatchesDeclaredTrace, Is.True, result.Facts.NarrativeTraceMismatch);
            Assert.That(result.Facts.NarrativeFixtureMatchesDeclaredTrace, Is.True);
            Assert.That(result.Facts.NarrativeRunsAgreeCanonically, Is.True, result.Facts.NarrativeTraceMismatch);
            Assert.That(result.Facts.NarrativeDeclaredTraceDigest, Is.Not.Empty);
        }

        [Test]
        public void TheCardRunsAgreeCanonically()
        {
            Assert.That(result.Facts.CardRunsAgreeCanonically, Is.True);
            Assert.That(result.Facts.CardGeneratedFailures, Is.EqualTo(0));
            Assert.That(result.Facts.CardFixtureFailures, Is.EqualTo(0));
            Assert.That(result.Facts.CardGeneratedSteps, Is.EqualTo(result.Facts.CardFixtureSteps));
        }

        [Test]
        public void TheAdditiveSlotCarriesItsComposedValueAndEverySupporter()
        {
            Assert.That(result.Facts.AdditiveGeneratedPassed, Is.True);
            Assert.That(result.Facts.AdditiveFixturePassed, Is.True);
            Assert.That(
                result.Facts.AdditiveComposedValue,
                Is.EqualTo(CardVocabulary.FestivalBonus + CardVocabulary.NestedFestivalBonus),
                "P-019: the Additive slot's value is the reducer's fold over every contribution, not one candidate's");
            Assert.That(result.Facts.AdditiveSupporterCount, Is.EqualTo(2), "P-017: two contributions share the slot");
            Assert.That(
                result.Facts.AdditiveGeneratedDigest,
                Is.EqualTo(result.Facts.AdditiveFixtureDigest),
                "the two catalogs must compose the same slot identically");
        }

        [Test]
        public void TheGeneratedEntryRootsTheFamilyRegistrationSurfaceItself()
        {
            Assert.That(ProbeCatalog.TryGetFamilyEntry(ProbeCatalog.NarrativeFamilyEntryKey, out IFamilyPluginEntry? entry), Is.True);
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry!.DeclarationCount, Is.EqualTo(6));
            Assert.That(entry.SystemNames, Is.Not.Empty);

            Assert.That(CardCatalog.TryGetFamilyEntry(CardCatalog.CardFamilyEntryKey, out IFamilyPluginEntry? card), Is.True);
            Assert.That(card, Is.Not.Null);
            Assert.That(card!.DeclarationCount, Is.EqualTo(4));
        }

        [Test]
        public void NoLoadedKernelAssemblyReferencesAGameplayOrRulesAssembly()
        {
            LoadedAssemblyReport kernel = KernelAssemblyAudit.AuditLoadedAssemblies();

            Assert.That(kernel.ForbiddenReferences, Is.Empty, string.Join(" | ", kernel.ForbiddenReferences));
            Assert.That(kernel.DuplicateKernelAssemblies, Is.Empty, string.Join(" | ", kernel.DuplicateKernelAssemblies));
            Assert.That(kernel.InspectionFailures, Is.Empty, "an unreadable reference set proves nothing");
            Assert.That(kernel.GameplayFamilyOnKernel.Count, Is.EqualTo(kernel.GameplayFamilyAssemblies.Count));
            Assert.That(kernel.KernelAssemblies.Count, Is.GreaterThanOrEqualTo(6));
            Assert.That(kernel.Identity, Is.Not.Empty);
        }

        [Test]
        public void NoKernelAsmdefReferencesAGameplayAssembly()
        {
            // The build-time half: a player cannot see a project tree, so this is the discovery-complete direction.
            string assemblyDirectory =
                Path.GetDirectoryName(typeof(W4ProfileIntegrationTests).Assembly.Location) ?? string.Empty;
            string? root = KernelAssemblyAudit.TryFindRepositoryRoot(Directory.GetCurrentDirectory())
                ?? KernelAssemblyAudit.TryFindRepositoryRoot(assemblyDirectory);

            Assert.That(root, Is.Not.Null,
                "no repository checkout was found above the working directory (" + Directory.GetCurrentDirectory()
                + ") or the test assembly directory (" + assemblyDirectory + ").");

            AsmdefReferenceReport report = KernelAssemblyAudit.AuditAsmdefReferences(root!);

            Assert.That(report.MissingPackages, Is.Empty,
                "an expected package directory is absent: " + report.Describe());
            Assert.That(report.Violations, Is.Empty,
                "a kernel asmdef references a gameplay assembly: " + report.Describe());
            Assert.That(report.KernelAsmdefs, Is.Not.Empty, report.Describe());
            Assert.That(report.GameplayAsmdefs, Is.Not.Empty, report.Describe());
            Assert.That(report.GameplayAsmdefsOnKernel.Count, Is.EqualTo(report.GameplayAsmdefs.Count),
                "every gameplay/rules asmdef must reference the kernel: " + report.Describe());
        }

        [Test]
        public void TheAdditiveFixtureMountsTheNestedProviderOnItsOwnScope()
        {
            // The fixture's own declaration set: the four card declarations plus the nested festival provider, whose
            // rule identity differs from the ancestor's, so the two supporters are distinct contributions (P-017).
            IReadOnlyList<GameCore.Contracts.CatalogPluginDeclaration> declarations = CardAdditiveScenario.Declarations();
            Assert.That(declarations.Count, Is.EqualTo(CardTableFixture.Declarations().Count + 1));
            Assert.That(
                CardAdditiveScenario.NestedFestivalDeclaration().Manifest.PluginType,
                Is.EqualTo(CardAdditiveScenario.NestedFestivalType));
        }

        private static W4ProfileStep Step(string name)
        {
            for (int i = 0; i < result.Steps.Count; i++)
            {
                if (result.Steps[i].Name == name)
                {
                    return result.Steps[i];
                }
            }

            Assert.Fail("the scenario reported no step named " + name);
            return null!;
        }
    }
}
