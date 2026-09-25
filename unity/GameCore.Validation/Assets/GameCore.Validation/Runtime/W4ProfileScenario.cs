// GameCore.Validation.ProbeHost — the Wave 4 provisional generic-execution profile gate (GC-012).
//
// The sentence this file implements, from `docs/game-core/09-implementation-guide.md` (Wave 4 — Dynamic composition
// and early player contract gate):
//
//   "Run both families in IL2CPP, then integrate indexed move/mode changes, service-closure lifecycle and slot
//    policies. [...] Provisional generic execution freeze requires GC-012."
//
// and GC-012's own concrete work: "Build the narrative and card slices into one IL2CPP player with generated
// inactive plugin entries. Mount/spawn/execute/observe each, compare with Editor/world fixture outputs, and audit
// all generic contracts for required genre-specific types."
//
// WHY THIS IS A SEPARATE SCENARIO FROM THE W3 GATE
//
// The W3 gate proves that one kernel carries two families. This gate proves four additional things that only a
// later revision can:
//
//   1. **Generated inactive plugin entries.** Each family now has its own generated registration whose direct
//      constructor reference roots that family's gameplay closure (`FamilyEntryRegistrations` in both generated
//      catalogs). `Assets/link.xml` no longer carries `preserve="all"` for the narrative gameplay/rules
//      assemblies, so a stripped player keeps them only because generation references them (04 section 8).
//   2. **Canonical comparison against the Editor/world fixture outputs.** Each family is run twice — once over the
//      committed generated catalog and once over the slice's hand-written generated-style fixture catalog — and the
//      two runs' canonical observations must agree. For narrative that is the declared canonical trace's own
//      comparison (`NarrativeScenarioTrace.TryCompare` over `NarrativeFacts.PipelineEntries()`), so the player's
//      numbers are checked against the *committed* `artifacts/gc-010/narrative-trace.json` declaration, not against
//      each other alone. For cards the comparison is the canonical facts digest of the two runs.
//   3. **The P-017/P-019 multi-supporter slot in a live world.** GC-011 recorded this as a kernel gap: an `Additive`
//      slot with more than one supporter had no binding-row representation, so the nested +3 festival on top of the
//      +2 festival was provable only by pure reducer and derivation tests. GC-012 corrected that (a binding row now
//      carries a support set), and `CardAdditiveScenario` proves it end-to-end in a real world over both catalogs.
//   4. **The generic-profile audit.** No kernel assembly may reference a gameplay/rules/validation/generated
//      assembly, no kernel assembly may be loaded twice, and every loaded gameplay/rules assembly must reference
//      the kernel. `KernelAssemblyAudit` reads that from the running process; the build-time `.asmdef` half runs in
//      the EditMode suite where a project tree exists.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Gameplay.Cards.Fixtures;
using GameCore.Gameplay.Narrative.Fixtures;
using GameCore.Rules.Narrative;
using GameCore.Unity.Runtime;
using GameCore.Validation.Generated;
using GameCore.Validation.GeneratedCards;
using GameCore.Validation.Slices;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>One named W4 profile observation: what was checked and the values it was checked against.</summary>
    public sealed class W4ProfileStep
    {
        /// <summary>Builds one observation.</summary>
        public W4ProfileStep(string name, bool passed, string detail)
        {
            Name = name;
            Passed = passed;
            Detail = detail ?? string.Empty;
        }

        /// <summary>Stable observation name.</summary>
        public string Name { get; }

        /// <summary>Whether the observation held.</summary>
        public bool Passed { get; }

        /// <summary>The observed values the verdict was computed from.</summary>
        public string Detail { get; }

        /// <summary>One-line form.</summary>
        public override string ToString() => Name + ": " + (Passed ? "Pass" : "Fail") + " (" + Detail + ")";
    }

    /// <summary>Facts the profile gate observed, so a probe archives values beside verdicts.</summary>
    public sealed class W4ProfileFacts
    {
        // ---------------------------------------------------------------- generated inactive entries
        /// <summary>Factory key of the narrative family's generated entry, as the committed catalog declares it.</summary>
        public string NarrativeEntryKey { get; set; } = string.Empty;

        /// <summary>Factory key of the card family's generated entry, as the committed catalog declares it.</summary>
        public string CardEntryKey { get; set; } = string.Empty;

        /// <summary>Systems the narrative entry rooted, read through the generated catalog's own resolution.</summary>
        public int NarrativeEntrySystems { get; set; }

        /// <summary>Systems the card entry rooted.</summary>
        public int CardEntrySystems { get; set; }

        /// <summary>Typed readers each entry rooted, so a stripped payload reader is observable.</summary>
        public int NarrativeEntryReaders { get; set; }
        public int CardEntryReaders { get; set; }

        /// <summary>Registered worlds at the moment the probe started; zero proves nothing mounted at startup.</summary>
        public int RegistryAtStart { get; set; }

        /// <summary>True when both family entries resolved from the built catalogs.</summary>
        public bool BothEntriesResolved { get; set; }

        // ---------------------------------------------------------------- narrative canonical comparison
        public int NarrativeGeneratedSteps { get; set; }
        public int NarrativeFixtureSteps { get; set; }
        public bool NarrativeGeneratedMatchesDeclaredTrace { get; set; }
        public bool NarrativeFixtureMatchesDeclaredTrace { get; set; }
        public bool NarrativeRunsAgreeCanonically { get; set; }
        public string NarrativeTraceMismatch { get; set; } = string.Empty;
        public string NarrativeDeclaredTraceDigest { get; set; } = string.Empty;

        // ---------------------------------------------------------------- card canonical comparison
        public int CardGeneratedSteps { get; set; }
        public int CardFixtureSteps { get; set; }
        public bool CardRunsAgreeCanonically { get; set; }
        public string CardGeneratedDigest { get; set; } = string.Empty;
        public string CardFixtureDigest { get; set; } = string.Empty;
        public int CardGeneratedFailures { get; set; }
        public int CardFixtureFailures { get; set; }

        // ---------------------------------------------------------------- the additive multi-supporter slot
        public bool AdditiveGeneratedPassed { get; set; }
        public bool AdditiveFixturePassed { get; set; }
        public int AdditiveComposedValue { get; set; }
        public int AdditiveSupporterCount { get; set; }
        public string AdditiveGeneratedDigest { get; set; } = string.Empty;
        public string AdditiveFixtureDigest { get; set; } = string.Empty;

        // ---------------------------------------------------------------- kernel separation
        public int KernelAssemblyCount { get; set; }
        public int KernelReferenceCount { get; set; }
        public int KernelForbiddenReferenceCount { get; set; }
        public int DuplicateKernelAssemblyCount { get; set; }
        public int KernelInspectionFailureCount { get; set; }
        public int GameplayFamilyAssemblyCount { get; set; }
        public int GameplayFamilyOnKernelCount { get; set; }
        public string KernelImageIdentity { get; set; } = string.Empty;
        public string KernelAssemblies { get; set; } = string.Empty;

        /// <summary>Registered worlds after every run tore its world down.</summary>
        public int RegistryAfterAll { get; set; }

        /// <summary>One-line digest of every fact this gate observed.</summary>
        public string Describe() =>
            "narrativeEntry=" + NarrativeEntryKey
            + "; cardEntry=" + CardEntryKey
            + "; bothEntriesResolved=" + BothEntriesResolved
            + "; narrativeEntrySystems=" + I(NarrativeEntrySystems)
            + "; cardEntrySystems=" + I(CardEntrySystems)
            + "; narrativeEntryReaders=" + I(NarrativeEntryReaders)
            + "; cardEntryReaders=" + I(CardEntryReaders)
            + "; registryAtStart=" + I(RegistryAtStart)
            + "; narrativeGeneratedSteps=" + I(NarrativeGeneratedSteps)
            + "; narrativeFixtureSteps=" + I(NarrativeFixtureSteps)
            + "; narrativeGeneratedMatchesDeclaredTrace=" + NarrativeGeneratedMatchesDeclaredTrace
            + "; narrativeFixtureMatchesDeclaredTrace=" + NarrativeFixtureMatchesDeclaredTrace
            + "; narrativeRunsAgree=" + NarrativeRunsAgreeCanonically
            + "; narrativeTraceMismatch=" + NarrativeTraceMismatch
            + "; narrativeDeclaredTraceDigest=" + NarrativeDeclaredTraceDigest
            + "; cardGeneratedSteps=" + I(CardGeneratedSteps)
            + "; cardFixtureSteps=" + I(CardFixtureSteps)
            + "; cardRunsAgree=" + CardRunsAgreeCanonically
            + "; cardGeneratedDigest=" + CardGeneratedDigest
            + "; cardFixtureDigest=" + CardFixtureDigest
            + "; cardGeneratedFailures=" + I(CardGeneratedFailures)
            + "; cardFixtureFailures=" + I(CardFixtureFailures)
            + "; additiveGeneratedPassed=" + AdditiveGeneratedPassed
            + "; additiveFixturePassed=" + AdditiveFixturePassed
            + "; additiveComposedValue=" + I(AdditiveComposedValue)
            + "; additiveSupporterCount=" + I(AdditiveSupporterCount)
            + "; additiveGeneratedDigest=" + AdditiveGeneratedDigest
            + "; additiveFixtureDigest=" + AdditiveFixtureDigest
            + "; kernelAssemblies=" + I(KernelAssemblyCount)
            + "; kernelReferences=" + I(KernelReferenceCount)
            + "; kernelForbiddenReferences=" + I(KernelForbiddenReferenceCount)
            + "; duplicateKernelAssemblies=" + I(DuplicateKernelAssemblyCount)
            + "; kernelInspectionFailures=" + I(KernelInspectionFailureCount)
            + "; gameplayFamilyAssemblies=" + I(GameplayFamilyAssemblyCount)
            + "; gameplayFamilyOnKernel=" + I(GameplayFamilyOnKernelCount)
            + "; registryAfterAll=" + I(RegistryAfterAll)
            + "; kernelImage=" + KernelImageIdentity.Replace("\n", " ")
            + "; kernelSet=" + KernelAssemblies;

        private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Full result of one profile run: the named observations plus the facts they were computed from.</summary>
    public sealed class W4ProfileScenarioResult
    {
        public W4ProfileScenarioResult(IReadOnlyList<W4ProfileStep> steps, W4ProfileFacts facts)
        {
            Steps = steps;
            Facts = facts;
        }

        public IReadOnlyList<W4ProfileStep> Steps { get; }

        public W4ProfileFacts Facts { get; }

        /// <summary>True when every observation passed and there was at least one.</summary>
        public bool AllPassed
        {
            get
            {
                for (int i = 0; i < Steps.Count; i++)
                {
                    if (!Steps[i].Passed)
                    {
                        return false;
                    }
                }

                return Steps.Count > 0;
            }
        }

        /// <summary>One-line digest naming every failed observation.</summary>
        public string Describe()
        {
            var failed = new List<string>();
            for (int i = 0; i < Steps.Count; i++)
            {
                if (!Steps[i].Passed)
                {
                    failed.Add(Steps[i].Name + " (" + Steps[i].Detail + ")");
                }
            }

            return failed.Count == 0
                ? Steps.Count.ToString(CultureInfo.InvariantCulture) + " W4 profile checks passed"
                : failed.Count.ToString(CultureInfo.InvariantCulture) + " W4 profile check(s) failed: "
                    + string.Join(" | ", failed.ToArray());
        }
    }

    /// <summary>Runs the Wave 4 provisional generic-execution profile gate over the real modules.</summary>
    public static class W4ProfileScenario
    {
        /// <summary>Player-visible kernel assemblies the loaded-set check requires (the rest are Editor-only).</summary>
        private const int RequiredKernelAssemblyCount = 6;

        /// <summary>Gameplay and rules assemblies the one-way edge check requires.</summary>
        private const int RequiredGameplayFamilyAssemblyCount = 6;

        /// <summary>
        /// Runs the four profile clauses in one process. Both families are mounted late (nothing exists at probe
        /// start), each runs over its committed generated catalog and over its own hand-written fixture catalog, and
        /// the additive multi-supporter proof runs over both catalogs as well.
        /// </summary>
        public static W4ProfileScenarioResult Run()
        {
            var steps = new List<W4ProfileStep>(10);
            var facts = new W4ProfileFacts();

            facts.RegistryAtStart = UnityWorldRegistry.Count;

            // ---- clause 1: the generated inactive family entries resolve by key from the built catalogs
            RunGeneratedEntryCheck(steps, facts);

            // ---- clause 2: both families, generated catalog vs fixture catalog, canonical comparison
            RunNarrativeComparison(steps, facts);
            RunCardComparison(steps, facts);

            // ---- clause 3: the P-017/P-019 multi-supporter slot, end-to-end in a live world
            RunAdditiveProof(steps, facts);

            facts.RegistryAfterAll = UnityWorldRegistry.Count;

            // ---- clause 4: the kernel separation, read after every family has been loaded
            RunKernelAudit(steps, facts);

            return new W4ProfileScenarioResult(steps, facts);
        }

        // ---------------------------------------------------------------- clause 1

        private static void RunGeneratedEntryCheck(List<W4ProfileStep> steps, W4ProfileFacts facts)
        {
            const string name = "w4-generated-inactive-family-entries";
            try
            {
                facts.NarrativeEntryKey = ProbeCatalog.NarrativeFamilyEntryKey.ToString();
                facts.CardEntryKey = CardCatalog.CardFamilyEntryKey.ToString();

                bool narrativeResolved = ProbeCatalog.TryGetFamilyEntry(
                    ProbeCatalog.NarrativeFamilyEntryKey,
                    out IFamilyPluginEntry? narrative);
                bool cardResolved = CardCatalog.TryGetFamilyEntry(
                    CardCatalog.CardFamilyEntryKey,
                    out IFamilyPluginEntry? card);

                facts.BothEntriesResolved = narrativeResolved && cardResolved && narrative != null && card != null;

                if (narrative != null)
                {
                    facts.NarrativeEntrySystems = narrative.DeclarationCount;
                    facts.NarrativeEntryReaders = narrative.SystemNames.Count;
                }

                if (card != null)
                {
                    facts.CardEntrySystems = card.DeclarationCount;
                    facts.CardEntryReaders = card.SystemNames.Count;
                }

                // The entries must be *inactive*: nothing was mounted before the probe ran. The narrative entry
                // roots six systems and the cards entry four, which is the family's own declared stage set.
                bool pass = facts.BothEntriesResolved
                    && narrative != null
                    && card != null
                    && string.Equals(narrative.Family, "narrative", StringComparison.Ordinal)
                    && string.Equals(card.Family, "cards", StringComparison.Ordinal)
                    && facts.NarrativeEntrySystems == 6
                    && facts.CardEntrySystems == 4
                    && facts.RegistryAtStart == 0;

                steps.Add(new W4ProfileStep(name, pass,
                    "narrativeEntry=" + facts.NarrativeEntryKey
                    + "; cardEntry=" + facts.CardEntryKey
                    + "; bothResolved=" + facts.BothEntriesResolved
                    + "; narrativeSystems=" + facts.NarrativeEntrySystems.ToString(CultureInfo.InvariantCulture)
                    + "; cardSystems=" + facts.CardEntrySystems.ToString(CultureInfo.InvariantCulture)
                    + "; registryAtStart=" + facts.RegistryAtStart.ToString(CultureInfo.InvariantCulture)));
            }
            catch (Exception exception)
            {
                steps.Add(new W4ProfileStep(name, false, DescribeException(exception)));
            }
        }

        // ---------------------------------------------------------------- clause 2a

        private static void RunNarrativeComparison(List<W4ProfileStep> steps, W4ProfileFacts facts)
        {
            const string name = "w4-narrative-runs-agree-with-the-declared-canonical-trace";
            try
            {
                NarrativeScenarioResult generated = NarrativeScenarioHost.RunGeneratedCatalog();
                NarrativeScenarioResult fixture = NarrativeScenarioHost.RunFixtureCatalog();

                facts.NarrativeGeneratedSteps = generated.Steps.Count;
                facts.NarrativeFixtureSteps = fixture.Steps.Count;
                facts.NarrativeDeclaredTraceDigest = NarrativeScenarioTrace.ExpectedDocumentDigest();

                facts.NarrativeGeneratedMatchesDeclaredTrace = NarrativeScenarioTrace.TryCompare(
                    generated.Facts.PipelineEntries(),
                    out IReadOnlyList<string> generatedMismatches);
                facts.NarrativeFixtureMatchesDeclaredTrace = NarrativeScenarioTrace.TryCompare(
                    fixture.Facts.PipelineEntries(),
                    out IReadOnlyList<string> fixtureMismatches);

                // Canonical comparison of the two runs: the declared keys are the same set, so the two runs agree
                // exactly when every key's text value is equal. A mismatch in either run is reported by key.
                IReadOnlyList<NarrativeTraceEntry> generatedEntries = generated.Facts.PipelineEntries();
                IReadOnlyList<NarrativeTraceEntry> fixtureEntries = fixture.Facts.PipelineEntries();
                var mismatches = new List<string>(generatedMismatches);
                mismatches.AddRange(fixtureMismatches);
                int differences = 0;
                if (generatedEntries.Count == fixtureEntries.Count)
                {
                    for (int i = 0; i < generatedEntries.Count; i++)
                    {
                        if (!string.Equals(
                                generatedEntries[i].Value,
                                fixtureEntries[i].Value,
                                StringComparison.Ordinal))
                        {
                            differences++;
                            if (mismatches.Count < 8)
                            {
                                mismatches.Add(
                                    generatedEntries[i].Key + ": generated=" + generatedEntries[i].Value
                                    + " fixture=" + fixtureEntries[i].Value);
                            }
                        }
                    }
                }
                else
                {
                    differences = -1;
                    mismatches.Add("the two runs report a different number of canonical pipeline keys");
                }

                facts.NarrativeRunsAgreeCanonically = differences == 0;
                facts.NarrativeTraceMismatch = mismatches.Count == 0 ? "<none>" : string.Join(" | ", mismatches.ToArray());

                bool pass = generated.AllPassed
                    && fixture.AllPassed
                    && facts.NarrativeGeneratedMatchesDeclaredTrace
                    && facts.NarrativeFixtureMatchesDeclaredTrace
                    && facts.NarrativeRunsAgreeCanonically;

                steps.Add(new W4ProfileStep(name, pass, generated.Describe() + "; " + fixture.Describe()));
            }
            catch (Exception exception)
            {
                steps.Add(new W4ProfileStep(name, false, DescribeException(exception)));
            }
        }

        // ---------------------------------------------------------------- clause 2b

        private static void RunCardComparison(List<W4ProfileStep> steps, W4ProfileFacts facts)
        {
            const string name = "w4-card-runs-agree-canonically-generated-vs-fixture";
            try
            {
                CardScenarioResult generated = CardsScenarioHost.RunGeneratedCatalog();
                CardScenarioResult fixture = CardsScenarioHost.RunFixtureCatalog();

                facts.CardGeneratedSteps = generated.Steps.Count;
                facts.CardFixtureSteps = fixture.Steps.Count;
                facts.CardGeneratedFailures = CountCardFailures(generated.Steps);
                facts.CardFixtureFailures = CountCardFailures(fixture.Steps);

                // The slice's canonical digest is its own facts digest: the card slice has no declared trace
                // document comparable to the narrative one (GC-011 recorded that asymmetry), so the comparison this
                // gate can make is that the run over the committed generated catalog and the run over the
                // hand-written generated-style fixture catalog observe the same values. A drift in either the
                // generated registration or the slice's own behaviour changes one digest and not the other.
                facts.CardGeneratedDigest = generated.Facts.Describe();
                facts.CardFixtureDigest = fixture.Facts.Describe();
                facts.CardRunsAgreeCanonically = string.Equals(
                    facts.CardGeneratedDigest,
                    facts.CardFixtureDigest,
                    StringComparison.Ordinal);

                bool pass = generated.AllPassed
                    && fixture.AllPassed
                    && facts.CardRunsAgreeCanonically
                    && facts.CardGeneratedSteps > 0
                    && facts.CardGeneratedSteps == facts.CardFixtureSteps;

                steps.Add(new W4ProfileStep(name, pass,
                    "generatedSteps=" + facts.CardGeneratedSteps.ToString(CultureInfo.InvariantCulture)
                    + "; fixtureSteps=" + facts.CardFixtureSteps.ToString(CultureInfo.InvariantCulture)
                    + "; digestEqual=" + facts.CardRunsAgreeCanonically
                    + "; generatedFailures=" + facts.CardGeneratedFailures.ToString(CultureInfo.InvariantCulture)
                    + "; fixtureFailures=" + facts.CardFixtureFailures.ToString(CultureInfo.InvariantCulture)));
            }
            catch (Exception exception)
            {
                steps.Add(new W4ProfileStep(name, false, DescribeException(exception)));
            }
        }

        // ---------------------------------------------------------------- clause 3

        private static void RunAdditiveProof(List<W4ProfileStep> steps, W4ProfileFacts facts)
        {
            const string name = "w4-additive-multi-supporter-slot-in-a-live-world";
            try
            {
                GameCore.Contracts.CatalogBuildResult build = CardCatalog.BuildVerifiedCatalog(
                    out GameCore.Contracts.ContentHash _);
                if (build.Catalog == null)
                {
                    throw new InvalidOperationException(
                        "the committed generated card catalog was rejected by the production catalog rules: "
                        + build.Describe());
                }

                CardAdditiveResult generated = CardAdditiveScenario.Run(
                    build.Catalog,
                    CardAdditiveScenario.Declarations(),
                    ProbeKeys.AbsentFixturePluginKey,
                    CardTableKeys.PluginType("cards.absent-plugin"),
                    ParseFingerprint(CardCatalog.CatalogFingerprint));
                CardAdditiveResult fixture = CardAdditiveScenario.RunFixtureCatalog();

                facts.AdditiveGeneratedPassed = generated.AllPassed;
                facts.AdditiveFixturePassed = fixture.AllPassed;
                facts.AdditiveComposedValue = generated.Facts.SeatABonusValue;
                facts.AdditiveSupporterCount = generated.Facts.SeatABonusSupporterCount;
                facts.AdditiveGeneratedDigest = generated.Facts.Describe();
                facts.AdditiveFixtureDigest = fixture.Facts.Describe();

                int expectedComposed = CardVocabulary.FestivalBonus + CardVocabulary.NestedFestivalBonus;
                bool digestAgrees = string.Equals(
                    facts.AdditiveGeneratedDigest,
                    facts.AdditiveFixtureDigest,
                    StringComparison.Ordinal);

                bool pass = generated.AllPassed
                    && fixture.AllPassed
                    && digestAgrees
                    && facts.AdditiveComposedValue == expectedComposed
                    && facts.AdditiveSupporterCount == 2;

                steps.Add(new W4ProfileStep(name, pass,
                    "generated=" + generated.Describe()
                    + "; fixture=" + fixture.Describe()
                    + "; composedValue=" + facts.AdditiveComposedValue.ToString(CultureInfo.InvariantCulture)
                    + "; expectedComposed=" + expectedComposed.ToString(CultureInfo.InvariantCulture)
                    + "; supporters=" + facts.AdditiveSupporterCount.ToString(CultureInfo.InvariantCulture)
                    + "; digestEqual=" + digestAgrees));
            }
            catch (Exception exception)
            {
                steps.Add(new W4ProfileStep(name, false, DescribeException(exception)));
            }
        }

        // ---------------------------------------------------------------- clause 4

        private static void RunKernelAudit(List<W4ProfileStep> steps, W4ProfileFacts facts)
        {
            try
            {
                LoadedAssemblyReport kernel = KernelAssemblyAudit.AuditLoadedAssemblies();
                facts.KernelAssemblies = string.Join(",", ToArray(kernel.KernelAssemblies));
                facts.KernelImageIdentity = kernel.Identity;
                facts.KernelAssemblyCount = kernel.KernelAssemblies.Count;
                facts.KernelReferenceCount = kernel.KernelReferenceCount;
                facts.KernelForbiddenReferenceCount = kernel.ForbiddenReferences.Count;
                facts.DuplicateKernelAssemblyCount = kernel.DuplicateKernelAssemblies.Count;
                facts.KernelInspectionFailureCount = kernel.InspectionFailures.Count;
                facts.GameplayFamilyAssemblyCount = kernel.GameplayFamilyAssemblies.Count;
                facts.GameplayFamilyOnKernelCount = kernel.GameplayFamilyOnKernel.Count;

                steps.Add(new W4ProfileStep(
                    "w4-generic-profile-no-genre-type-in-the-kernel",
                    kernel.Clean
                    && kernel.KernelAssemblies.Count >= RequiredKernelAssemblyCount
                    && kernel.GameplayFamilyAssemblies.Count >= RequiredGameplayFamilyAssemblyCount
                    && kernel.GameplayFamilyOnKernel.Count == kernel.GameplayFamilyAssemblies.Count,
                    kernel.Describe()));

                // The two families ran late and sequentially, so the registry must be back at its start value: if a
                // family leaked a world, "one host and one world per protocol world" (04 section 3) would be false.
                steps.Add(new W4ProfileStep(
                    "w4-both-families-mount-late-and-leave-no-world",
                    facts.RegistryAtStart == 0 && facts.RegistryAfterAll == facts.RegistryAtStart,
                    "registryAtStart=" + facts.RegistryAtStart.ToString(CultureInfo.InvariantCulture)
                    + "; registryAfterAll=" + facts.RegistryAfterAll.ToString(CultureInfo.InvariantCulture)
                    + "; kernelImage=" + kernel.Identity.Replace("\n", " ")));
            }
            catch (Exception exception)
            {
                steps.Add(new W4ProfileStep("w4-generic-profile-no-genre-type-in-the-kernel", false, DescribeException(exception)));
            }
        }

        // ---------------------------------------------------------------- helpers

        private static GameCore.Contracts.ContentHash ParseFingerprint(string literal)
        {
            if (!GameCore.Contracts.ContentHash.TryParseHex(literal, out GameCore.Contracts.ContentHash hash))
            {
                throw new InvalidOperationException(
                    "the generated card catalog's emitted fingerprint literal is not a canonical content hash.");
            }

            return hash;
        }

        private static int CountCardFailures(IReadOnlyList<CardStep> steps)
        {
            int failed = 0;
            for (int i = 0; i < steps.Count; i++)
            {
                if (!steps[i].Passed)
                {
                    failed++;
                }
            }

            return failed;
        }

        private static string[] ToArray(IReadOnlyList<string> values)
        {
            var array = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                array[i] = values[i];
            }

            return array;
        }

        private static string DescribeException(Exception exception) =>
            "unhandled " + exception.GetType().FullName + ": " + exception.Message;
    }
}
