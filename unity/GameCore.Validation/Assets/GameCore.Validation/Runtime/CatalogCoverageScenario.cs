// GameCore.Validation.ProbeHost — GC-025's catalog coverage sequence.
//
// The gate sentence this sequence serves, from `docs/game-core/09-implementation-guide.md` (GC-025):
//
//   "Exercise every generated factory/serializer/closed generic root, bake/runtime recipe parity, inactive plugin
//    late mount, stop/restart and headless reference execution." — with the acceptance "every mandatory catalog
//    entry executes in the player, including code absent from startup scenes; headless actual Entities behavior
//    matches the applicable pure-rule/canonical fixtures".
//
// WHY THE CHECK IS STRUCTURED THIS WAY.
//
//   * The manifest (`CatalogReachability`) is generated from every committed catalog description and compiled into
//     this assembly, so "every mandatory entry" is a fixed list rather than whatever the run happens to touch. Each
//     catalog then reports its own live facts (file hash, fingerprint, counts) and its generated coverage companion
//     resolves every registration through the table's own lookup method, round-trips every declared serializer and
//     executes every closed-generic root statement. A root the linker dropped fails here rather than passing a
//     compile-only check (04 section 8, P-058).
//   * Nothing in this file uses reflection, `System.Type` lookup or an Editor-only API: the generated tables and the
//     generated coverage companions are direct references, and the run is the same code in the Editor suite and in
//     the headless player.
//   * The parity and negative observations are the ones TEST-020 names: editor-baked and runtime-recipe targets are
//     created through their own materializations and compared by normalized schema, definition, applier
//     registration and behaviour (never by chunk layout or native entity index); a recipe the catalog does not
//     declare, and a declared recipe at another revision, are refused with their declared diagnostics.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Gameplay.Traversal;
using GameCore.Gameplay.Traversal.Fixtures;
using GameCore.Rules.Cards;
using GameCore.Rules.Narrative;
using GameCore.Rules.Traversal;
using GameCore.Unity.Runtime;
using GameCore.Validation.Generated;
using GameCore.Validation.GeneratedCards;
using GameCore.Validation.GeneratedCheckpoint;
using GameCore.Validation.GeneratedTraversal;
using GameCore.Validation.Probe;
using GameCore.Validation.Slices;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>One named observation of the catalog coverage sequence.</summary>
    public sealed class CatalogCoverageStep
    {
        public CatalogCoverageStep(string name, bool passed, string detail)
        {
            Name = name;
            Passed = passed;
            Detail = detail ?? string.Empty;
        }

        public string Name { get; }

        public bool Passed { get; }

        public string Detail { get; }

        public override string ToString() => Name + ": " + (Passed ? "Pass" : "Fail") + " (" + Detail + ")";
    }

    /// <summary>
    /// Full result of one catalog coverage run: the named observations plus one digest over them, computed with the
    /// same canonical digest function every other family result uses, so a renamed, reordered, added or dropped
    /// observation changes the literal the probe freezes (P-008).
    /// </summary>
    public sealed class CatalogCoverageResult
    {
        public CatalogCoverageResult(string label, IReadOnlyList<CatalogCoverageStep> steps)
        {
            Label = label;
            Steps = steps;
            var lines = new List<string>(steps.Count);
            bool allPassed = steps.Count > 0;
            for (int i = 0; i < steps.Count; i++)
            {
                lines.Add(steps[i].Name + "=" + (steps[i].Passed ? "pass" : "fail"));
                allPassed &= steps[i].Passed;
            }

            AllPassed = allPassed;
            Digest = NarrativeDigest.OfLines(lines);
        }

        public string Label { get; }

        public IReadOnlyList<CatalogCoverageStep> Steps { get; }

        public string Digest { get; }

        public bool AllPassed { get; }

        public string Describe()
        {
            var failed = new List<string>();
            for (int i = 0; i < Steps.Count; i++)
            {
                if (!Steps[i].Passed)
                {
                    failed.Add(Steps[i].ToString());
                }
            }

            return "digest=" + Digest
                + "; observations=" + Steps.Count.ToString(CultureInfo.InvariantCulture)
                + "; failed=" + failed.Count.ToString(CultureInfo.InvariantCulture)
                + (failed.Count == 0 ? string.Empty : "; first=" + failed[0]);
        }
    }

    /// <summary>
    /// The catalog coverage sequence: manifest versus live catalogs, every mandatory root executed in the player,
    /// the traversal generated catalog's parity with its hand-written counterpart, the two materializations of the
    /// course's recipes, the refused recipes and a stopped-and-restarted world host.
    /// </summary>
    public static class CatalogCoverageScenario
    {
        /// <summary>Family label every observation name of this sequence carries.</summary>
        public const string Label = "catalogCoverage";

        /// <summary>
        /// The frozen observation table. The digest is computed from these names alone, so this list and
        /// `CatalogCoverageProbe.ExpectedDigest` cannot drift apart silently (P-008).
        /// </summary>
        public static readonly string[] ObservationNames =
        {
            "catalog-reachability-manifest",
            "catalog-coverage-probe-catalog",
            "catalog-coverage-cards-catalog",
            "catalog-coverage-checkpoint-catalog",
            "catalog-coverage-traversal-catalog",
            "catalog-coverage-registration-lookup",
            "catalog-coverage-closed-generic-roots",
            "catalog-coverage-traversal-generated-catalog",
            "catalog-coverage-late-mount-inactive-plugin",
            "catalog-coverage-bake-runtime-parity",
            "catalog-coverage-unknown-recipe-refused",
            "catalog-coverage-world-stop-restart",
            "catalog-coverage-headless-canonical",
        };

        /// <summary>Package version every traversal declaration the run mounts carries (mirrors the gameplay package).</summary>
        private const string PackageVersion = "1.0.0";

        /// <summary>Stable name of a recipe no catalog of this build declares (TEST-020's unknown-recipe case).</summary>
        private const string UnknownRecipeStableName = "catalog.coverage.unknown-recipe";

        /// <summary>Session salts of the two stop/restart hosts, so their session identities differ (P-004).</summary>
        private const ulong FirstRestartSalt = 0x4730323552455354UL;

        private const ulong SecondRestartSalt = 0x4730323552455332UL;

        /// <summary>Every observation name qualified with this sequence's label, in the table's order.</summary>
        public static IReadOnlyList<string> QualifiedNames()
        {
            var names = new string[ObservationNames.Length];
            for (int i = 0; i < ObservationNames.Length; i++)
            {
                names[i] = Label + "/" + ObservationNames[i];
            }

            return names;
        }

        /// <summary>Runs the whole sequence and returns its observations.</summary>
        public static CatalogCoverageResult Run()
        {
            var steps = new List<CatalogCoverageStep>(ObservationNames.Length);
            Add(steps, ObservationNames[0], ManifestStep);
            Add(steps, ObservationNames[1], ProbeCatalogStep);
            Add(steps, ObservationNames[2], CardsCatalogStep);
            Add(steps, ObservationNames[3], CheckpointCatalogStep);
            Add(steps, ObservationNames[4], TraversalCatalogStep);
            Add(steps, ObservationNames[5], RegistrationLookupStep);
            Add(steps, ObservationNames[6], ClosedGenericRootStep);
            Add(steps, ObservationNames[7], TraversalGeneratedCatalogStep);
            Add(steps, ObservationNames[8], LateMountStep);
            Add(steps, ObservationNames[9], BakeRuntimeParityStep);
            Add(steps, ObservationNames[10], UnknownRecipeStep);
            Add(steps, ObservationNames[11], WorldStopRestartStep);
            Add(steps, ObservationNames[12], HeadlessCanonicalStep);
            return new CatalogCoverageResult(Label, steps);
        }

        private delegate bool Observation(out string detail);

        private static void Add(List<CatalogCoverageStep> steps, string name, Observation observation)
        {
            string detail;
            bool passed;
            try
            {
                passed = observation(out detail);
            }
            catch (Exception exception)
            {
                passed = false;
                detail = "unhandled " + exception.GetType().FullName + ": " + exception.Message;
            }

            steps.Add(new CatalogCoverageStep(Label + "/" + name, passed, detail));
        }

        // ------------------------------------------------------------------------------------------------------
        // 1. The manifest versus the live catalogs.
        // ------------------------------------------------------------------------------------------------------

        /// <summary>Live facts of one committed catalog, as the built player reports them.</summary>
        private readonly struct LiveCatalogFacts
        {
            internal LiveCatalogFacts(
                string className,
                string fileHash,
                string fingerprint,
                bool fingerprintMatches,
                string observedFingerprint,
                int registrationGroupCount,
                int schemaCount,
                int registrationCount,
                int closedGenericRootCount)
            {
                ClassName = className;
                FileHash = fileHash;
                Fingerprint = fingerprint;
                FingerprintMatches = fingerprintMatches;
                ObservedFingerprint = observedFingerprint;
                RegistrationGroupCount = registrationGroupCount;
                SchemaCount = schemaCount;
                RegistrationCount = registrationCount;
                ClosedGenericRootCount = closedGenericRootCount;
            }

            internal string ClassName { get; }

            internal string FileHash { get; }

            internal string Fingerprint { get; }

            internal bool FingerprintMatches { get; }

            internal string ObservedFingerprint { get; }

            internal int RegistrationGroupCount { get; }

            internal int SchemaCount { get; }

            internal int RegistrationCount { get; }

            internal int ClosedGenericRootCount { get; }
        }

        private static bool ManifestStep(out string detail)
        {
            var parts = new List<string>();
            bool passed = true;
            for (int i = 0; i < CatalogReachability.Catalogs.Length; i++)
            {
                CatalogReachabilityCatalog manifest = CatalogReachability.Catalogs[i];
                if (!TryLiveFacts(manifest.Label, out LiveCatalogFacts live, out string reason))
                {
                    passed = false;
                    parts.Add(manifest.Label + ": no live facts (" + reason + ")");
                    continue;
                }

                bool matches = FactsMatch(manifest, live, out string mismatch);
                passed &= matches;
                parts.Add(
                    manifest.Label + "=" + (matches ? "match" : "mismatch:" + mismatch)
                    + "(groups=" + live.RegistrationGroupCount.ToString(CultureInfo.InvariantCulture)
                    + ", registrations=" + live.RegistrationCount.ToString(CultureInfo.InvariantCulture)
                    + ", schemas=" + live.SchemaCount.ToString(CultureInfo.InvariantCulture)
                    + ", roots=" + live.ClosedGenericRootCount.ToString(CultureInfo.InvariantCulture)
                    + ", fingerprint=" + live.ObservedFingerprint + ")");
            }

            detail = "format=" + CatalogReachability.Format
                + "; catalogs=" + CatalogReachability.Catalogs.Length.ToString(CultureInfo.InvariantCulture)
                + "; registrations=" + CatalogReachability.Registrations.Length.ToString(CultureInfo.InvariantCulture)
                + "; serializers=" + CatalogReachability.Serializers.Length.ToString(CultureInfo.InvariantCulture)
                + "; roots=" + CatalogReachability.ClosedGenericRoots.Length.ToString(CultureInfo.InvariantCulture)
                + "; " + string.Join("; ", parts);
            return passed;
        }

        private static bool FactsMatch(CatalogReachabilityCatalog manifest, LiveCatalogFacts live, out string mismatch)
        {
            mismatch = string.Empty;
            if (!string.Equals(manifest.ClassName, live.ClassName, StringComparison.Ordinal))
            {
                mismatch = "className";
            }
            else if (!string.Equals(manifest.CatalogFileHash, live.FileHash, StringComparison.Ordinal))
            {
                mismatch = "catalogFileHash";
            }
            else if (!string.Equals(manifest.CatalogFingerprint, live.Fingerprint, StringComparison.Ordinal)
                     || !string.Equals(manifest.CatalogFingerprint, live.ObservedFingerprint, StringComparison.Ordinal))
            {
                mismatch = "catalogFingerprint";
            }
            else if (!live.FingerprintMatches)
            {
                mismatch = "fingerprintMatchesGeneratedCatalog=false";
            }
            else if (manifest.RegistrationGroupCount != live.RegistrationGroupCount)
            {
                mismatch = "registrationGroupCount";
            }
            else if (manifest.SchemaCount != live.SchemaCount)
            {
                mismatch = "schemaCount";
            }
            else if (manifest.RegistrationCount != live.RegistrationCount)
            {
                mismatch = "registrationCount";
            }
            else if (manifest.ClosedGenericRootCount != live.ClosedGenericRootCount)
            {
                mismatch = "closedGenericRootCount";
            }

            return mismatch.Length == 0;
        }

        private static bool TryLiveFacts(string label, out LiveCatalogFacts facts, out string reason)
        {
            reason = string.Empty;
            switch (label)
            {
                case "probe":
                {
                    bool matches = ProbeCatalog.FingerprintMatchesGeneratedCatalog(out ContentHash observed);
                    facts = new LiveCatalogFacts(
                        "ProbeCatalog",
                        ProbeCatalog.CatalogFileHash,
                        ProbeCatalog.CatalogFingerprint,
                        matches,
                        observed.ToHex(),
                        ProbeCatalog.RegistrationGroupCount,
                        ProbeCatalog.SchemaCount,
                        ProbeCatalogCoverage.RegistrationCount,
                        ProbeCatalogCoverage.ClosedGenericRootCount);
                    return true;
                }

                case "cards":
                {
                    bool matches = CardCatalog.FingerprintMatchesGeneratedCatalog(out ContentHash observed);
                    facts = new LiveCatalogFacts(
                        "CardCatalog",
                        CardCatalog.CatalogFileHash,
                        CardCatalog.CatalogFingerprint,
                        matches,
                        observed.ToHex(),
                        CardCatalog.RegistrationGroupCount,
                        CardCatalog.SchemaCount,
                        CardCatalogCoverage.RegistrationCount,
                        CardCatalogCoverage.ClosedGenericRootCount);
                    return true;
                }

                case "checkpoint":
                {
                    bool matches = CheckpointCatalog.FingerprintMatchesGeneratedCatalog(out ContentHash observed);
                    facts = new LiveCatalogFacts(
                        "CheckpointCatalog",
                        CheckpointCatalog.CatalogFileHash,
                        CheckpointCatalog.CatalogFingerprint,
                        matches,
                        observed.ToHex(),
                        CheckpointCatalog.RegistrationGroupCount,
                        CheckpointCatalog.SchemaCount,
                        CheckpointCatalogCoverage.RegistrationCount,
                        CheckpointCatalogCoverage.ClosedGenericRootCount);
                    return true;
                }

                case "traversal":
                {
                    bool matches = TraversalCatalog.FingerprintMatchesGeneratedCatalog(out ContentHash observed);
                    facts = new LiveCatalogFacts(
                        "TraversalCatalog",
                        TraversalCatalog.CatalogFileHash,
                        TraversalCatalog.CatalogFingerprint,
                        matches,
                        observed.ToHex(),
                        TraversalCatalog.RegistrationGroupCount,
                        TraversalCatalog.SchemaCount,
                        TraversalCatalogCoverage.RegistrationCount,
                        TraversalCatalogCoverage.ClosedGenericRootCount);
                    return true;
                }

                default:
                    facts = default(LiveCatalogFacts);
                    reason = "unknown manifest label '" + label + "'";
                    return false;
            }
        }

        // ------------------------------------------------------------------------------------------------------
        // 2-5. Every generated registration, serializer and root of one catalog, executed by its own companion.
        // ------------------------------------------------------------------------------------------------------

        private static bool ProbeCatalogStep(out string detail)
        {
            CatalogBuildResult build = ProbeCatalog.BuildCatalog();
            if (build.Catalog == null)
            {
                detail = "the committed probe catalog was rejected: " + build.Describe();
                return false;
            }

            int registrations = ProbeCatalogCoverage.ExerciseRegistrations(out string registrationFailure);
            int schemas = ProbeCatalogCoverage.ExerciseSchemas(out string schemaFailure);
            ProbeCatalogCoverage.ExerciseClosedGenericRoots();

            bool passed = registrationFailure.Length == 0
                && schemaFailure.Length == 0
                && registrations == ProbeCatalogCoverage.RegistrationCount
                && schemas == ProbeCatalog.SchemaCount;
            detail = "registrations=" + registrations.ToString(CultureInfo.InvariantCulture)
                + "; expectedRegistrations=" + ProbeCatalogCoverage.RegistrationCount.ToString(CultureInfo.InvariantCulture)
                + "; schemas=" + schemas.ToString(CultureInfo.InvariantCulture)
                + "; expectedSchemas=" + ProbeCatalog.SchemaCount.ToString(CultureInfo.InvariantCulture)
                + "; closedGenericRoots=" + ProbeCatalogCoverage.ClosedGenericRootCount.ToString(CultureInfo.InvariantCulture)
                + "; fingerprint=" + build.Catalog!.Fingerprint.ToHex()
                + (registrationFailure.Length == 0 ? string.Empty : "; registrationFailure=" + registrationFailure)
                + (schemaFailure.Length == 0 ? string.Empty : "; schemaFailure=" + schemaFailure);
            return passed;
        }

        private static bool CardsCatalogStep(out string detail)
        {
            CatalogBuildResult build = CardCatalog.BuildCatalog();
            if (build.Catalog == null)
            {
                detail = "the committed card catalog was rejected: " + build.Describe();
                return false;
            }

            int registrations = CardCatalogCoverage.ExerciseRegistrations(out string registrationFailure);
            int schemas = CardCatalogCoverage.ExerciseSchemas(out string schemaFailure);
            CardCatalogCoverage.ExerciseClosedGenericRoots();

            bool passed = registrationFailure.Length == 0
                && schemaFailure.Length == 0
                && registrations == CardCatalogCoverage.RegistrationCount
                && schemas == CardCatalog.SchemaCount;
            detail = "registrations=" + registrations.ToString(CultureInfo.InvariantCulture)
                + "; expectedRegistrations=" + CardCatalogCoverage.RegistrationCount.ToString(CultureInfo.InvariantCulture)
                + "; schemas=" + schemas.ToString(CultureInfo.InvariantCulture)
                + "; expectedSchemas=" + CardCatalog.SchemaCount.ToString(CultureInfo.InvariantCulture)
                + "; fingerprint=" + build.Catalog!.Fingerprint.ToHex()
                + (registrationFailure.Length == 0 ? string.Empty : "; registrationFailure=" + registrationFailure)
                + (schemaFailure.Length == 0 ? string.Empty : "; schemaFailure=" + schemaFailure);
            return passed;
        }

        private static bool CheckpointCatalogStep(out string detail)
        {
            CatalogBuildResult build = CheckpointCatalog.BuildCatalog();
            if (build.Catalog == null)
            {
                detail = "the committed checkpoint catalog was rejected: " + build.Describe();
                return false;
            }

            int registrations = CheckpointCatalogCoverage.ExerciseRegistrations(out string registrationFailure);
            int schemas = CheckpointCatalogCoverage.ExerciseSchemas(out string schemaFailure);
            CheckpointCatalogCoverage.ExerciseClosedGenericRoots();

            bool passed = registrationFailure.Length == 0
                && schemaFailure.Length == 0
                && registrations == CheckpointCatalogCoverage.RegistrationCount
                && schemas == CheckpointCatalog.SchemaCount;
            detail = "registrations=" + registrations.ToString(CultureInfo.InvariantCulture)
                + "; expectedRegistrations=" + CheckpointCatalogCoverage.RegistrationCount.ToString(CultureInfo.InvariantCulture)
                + "; schemas=" + schemas.ToString(CultureInfo.InvariantCulture)
                + "; expectedSchemas=" + CheckpointCatalog.SchemaCount.ToString(CultureInfo.InvariantCulture)
                + "; fingerprint=" + build.Catalog!.Fingerprint.ToHex()
                + (registrationFailure.Length == 0 ? string.Empty : "; registrationFailure=" + registrationFailure)
                + (schemaFailure.Length == 0 ? string.Empty : "; schemaFailure=" + schemaFailure);
            return passed;
        }

        private static bool TraversalCatalogStep(out string detail)
        {
            CatalogBuildResult build = TraversalCatalog.BuildCatalog();
            if (build.Catalog == null)
            {
                detail = "the committed traversal catalog was rejected: " + build.Describe();
                return false;
            }

            int registrations = TraversalCatalogCoverage.ExerciseRegistrations(out string registrationFailure);
            int schemas = TraversalCatalogCoverage.ExerciseSchemas(out string schemaFailure);
            TraversalCatalogCoverage.ExerciseClosedGenericRoots();

            bool passed = registrationFailure.Length == 0
                && schemaFailure.Length == 0
                && registrations == TraversalCatalogCoverage.RegistrationCount
                && schemas == TraversalCatalog.SchemaCount;
            detail = "registrations=" + registrations.ToString(CultureInfo.InvariantCulture)
                + "; expectedRegistrations=" + TraversalCatalogCoverage.RegistrationCount.ToString(CultureInfo.InvariantCulture)
                + "; schemas=" + schemas.ToString(CultureInfo.InvariantCulture)
                + "; expectedSchemas=" + TraversalCatalog.SchemaCount.ToString(CultureInfo.InvariantCulture)
                + "; fingerprint=" + build.Catalog!.Fingerprint.ToHex()
                + (registrationFailure.Length == 0 ? string.Empty : "; registrationFailure=" + registrationFailure)
                + (schemaFailure.Length == 0 ? string.Empty : "; schemaFailure=" + schemaFailure);
            return passed;
        }

        // ------------------------------------------------------------------------------------------------------
        // 6. Every manifest root resolves by its derived key in this build.
        // ------------------------------------------------------------------------------------------------------

        private static bool RegistrationLookupStep(out string detail)
        {
            var failures = new List<string>();
            int resolved = 0;
            for (int i = 0; i < CatalogReachability.Registrations.Length; i++)
            {
                CatalogReachabilityRoot root = CatalogReachability.Registrations[i];
                if (TryResolve(root, out object? implementation, out string reason) && implementation != null)
                {
                    resolved++;
                    continue;
                }

                failures.Add(root.Catalog + "/" + root.StableName + ": " + reason);
            }

            for (int i = 0; i < CatalogReachability.Serializers.Length; i++)
            {
                CatalogReachabilityRoot root = CatalogReachability.Serializers[i];
                if (TryResolve(root, out object? implementation, out string reason) && implementation != null)
                {
                    resolved++;
                    continue;
                }

                failures.Add(root.Catalog + "/" + root.StableName + ": " + reason);
            }

            int expected = CatalogReachability.Registrations.Length + CatalogReachability.Serializers.Length;
            detail = "resolved=" + resolved.ToString(CultureInfo.InvariantCulture)
                + "; expected=" + expected.ToString(CultureInfo.InvariantCulture)
                + (failures.Count == 0 ? string.Empty : "; firstFailure=" + failures[0]);
            return failures.Count == 0 && resolved == expected;
        }

        /// <summary>
        /// Resolves one manifest root through the live generated catalog. A registration goes through its own
        /// table's generated lookup method; a serializer is found by key in the catalog's generated serializer table.
        /// No reflection and no type discovery participates (04 section 8).
        /// </summary>
        private static bool TryResolve(CatalogReachabilityRoot root, out object? implementation, out string reason)
        {
            implementation = null;
            reason = string.Empty;
            var key = new FactoryKey(new Id128(root.KeyHigh, root.KeyLow), root.KeyVersion);

            switch (root.Catalog)
            {
                case "probe":
                    switch (root.Group)
                    {
                        case "FamilyEntryRegistrations":
                            if (ProbeCatalog.TryGetFamilyEntry(key, out IFamilyPluginEntry? probeEntry))
                            {
                                implementation = probeEntry;
                                return true;
                            }

                            break;
                        case "HandlerRegistrations":
                            if (ProbeCatalog.TryGetHandler(key, out IProbeHandler<ProbeAmount, int>? handler))
                            {
                                implementation = handler;
                                return true;
                            }

                            break;
                        case "PluginRegistrations":
                            if (ProbeCatalog.TryGetPluginFactory(key, out IProbePluginFactory? probeFactory))
                            {
                                implementation = probeFactory;
                                return true;
                            }

                            break;
                        case "SchemaRegistrations":
                            return TryFindSerializer(ProbeCatalog.Serializers, key, out implementation, out reason);
                    }

                    break;

                case "cards":
                    switch (root.Group)
                    {
                        case "FamilyEntryRegistrations":
                            if (CardCatalog.TryGetFamilyEntry(key, out IFamilyPluginEntry? cardEntry))
                            {
                                implementation = cardEntry;
                                return true;
                            }

                            break;
                        case "PluginRegistrations":
                            if (CardCatalog.TryGetPluginFactory(key, out GameCore.Gameplay.Cards.ICardTablePluginFactory? cardFactory))
                            {
                                implementation = cardFactory;
                                return true;
                            }

                            break;
                        case "ReducerRegistrations":
                            if (CardCatalog.TryGetReducer(key, out ICardBonusReducer? reducer))
                            {
                                implementation = reducer;
                                return true;
                            }

                            break;
                        case "StaticPredicateRegistrations":
                            if (CardCatalog.TryGetStaticPredicate(key, out ICardTargetPredicate? predicate))
                            {
                                implementation = predicate;
                                return true;
                            }

                            break;
                        case "SystemFactoryRegistrations":
                            if (CardCatalog.TryGetSystemFactory(key, out GameCore.Gameplay.Cards.ICardTableSystemFactory? system))
                            {
                                implementation = system;
                                return true;
                            }

                            break;
                        case "SchemaRegistrations":
                            return TryFindSerializer(CardCatalog.Serializers, key, out implementation, out reason);
                    }

                    break;

                case "checkpoint":
                    if (string.Equals(root.Group, "SchemaRegistrations", StringComparison.Ordinal))
                    {
                        return TryFindSerializer(CheckpointCatalog.Serializers, key, out implementation, out reason);
                    }

                    break;

                case "traversal":
                    switch (root.Group)
                    {
                        case "PluginRegistrations":
                            if (TraversalCatalog.TryGetPluginFactory(key, out ITraversalCoursePluginFactory? courseFactory))
                            {
                                implementation = courseFactory;
                                return true;
                            }

                            break;
                        case "SystemFactoryRegistrations":
                            if (TraversalCatalog.TryGetSystemFactory(key, out ITraversalSystemFactory? traversalSystem))
                            {
                                implementation = traversalSystem;
                                return true;
                            }

                            break;
                        case "ReducerRegistrations":
                            if (TraversalCatalog.TryGetReducer(key, out ITraversalAccelerationReducer? acceleration))
                            {
                                implementation = acceleration;
                                return true;
                            }

                            break;
                        case "StaticPredicateRegistrations":
                            if (TraversalCatalog.TryGetStaticPredicate(key, out ITraversalTargetPredicate? traversalPredicate))
                            {
                                implementation = traversalPredicate;
                                return true;
                            }

                            break;
                        case "SchemaRegistrations":
                            return TryFindSerializer(TraversalCatalog.Serializers, key, out implementation, out reason);
                    }

                    break;
            }

            reason = "the generated lookup returned no implementation for " + root.Group;
            return false;
        }

        private static bool TryFindSerializer(
            ISchemaSerializer[] serializers,
            FactoryKey key,
            out object? implementation,
            out string reason)
        {
            for (int i = 0; i < serializers.Length; i++)
            {
                if (serializers[i].Key.Equals(key))
                {
                    implementation = serializers[i];
                    reason = string.Empty;
                    return true;
                }
            }

            implementation = null;
            reason = "no generated serializer is registered under " + key.ToString();
            return false;
        }

        // ------------------------------------------------------------------------------------------------------
        // 7. Every closed-generic root statement of the manifest executes.
        // ------------------------------------------------------------------------------------------------------

        private static bool ClosedGenericRootStep(out string detail)
        {
            int executed = 0;
            int probeRoots = 0;
            for (int i = 0; i < CatalogReachability.ClosedGenericRoots.Length; i++)
            {
                CatalogReachabilityRoot root = CatalogReachability.ClosedGenericRoots[i];
                if (root.KeyVersion != 0U || root.Kind != CatalogReachabilityKind.ClosedGenericRoot)
                {
                    detail = "manifest root " + i.ToString(CultureInfo.InvariantCulture)
                        + " is not a closed-generic root statement";
                    return false;
                }

                if (string.Equals(root.Catalog, "probe", StringComparison.Ordinal))
                {
                    probeRoots++;
                }

                executed++;
            }

            // The root method is the one direct call site that instantiates every closed generic the catalog
            // declares; calling it is what makes the instantiation exist and run in the player (04 section 8 item 3).
            ProbeCatalogCoverage.ExerciseClosedGenericRoots();

            bool passed = executed == CatalogReachability.ClosedGenericRoots.Length
                && probeRoots == ProbeCatalogCoverage.ClosedGenericRootCount
                && ProbeCatalog.HasClosedGenericRoots;
            detail = "rootStatements=" + executed.ToString(CultureInfo.InvariantCulture)
                + "; probeRoots=" + probeRoots.ToString(CultureInfo.InvariantCulture)
                + "; probeHasClosedGenericRoots=" + (ProbeCatalog.HasClosedGenericRoots ? "true" : "false");
            return passed;
        }

        // ------------------------------------------------------------------------------------------------------
        // 8. The generated traversal catalog against the hand-written generated-style table (P-028).
        // ------------------------------------------------------------------------------------------------------

        private static bool TraversalGeneratedCatalogStep(out string detail)
        {
            if (!TraversalCatalogTable.DerivationHolds())
            {
                detail = "the hand-written traversal table's literals no longer derive from their stable names";
                return false;
            }

            CatalogBuildResult generated = TraversalCatalog.BuildCatalog();
            CatalogBuildResult fixture = TraversalCatalogTable.Build();
            if (generated.Catalog == null)
            {
                detail = "the generated traversal catalog was rejected: " + generated.Describe();
                return false;
            }

            if (fixture.Catalog == null)
            {
                detail = "the hand-written traversal table was rejected: " + fixture.Describe();
                return false;
            }

            string generatedFingerprint = generated.Catalog.Fingerprint.ToHex();
            string fixtureFingerprint = fixture.Catalog.Fingerprint.ToHex();
            string tableFingerprint = TraversalCatalogTable.Fingerprint().ToHex();

            var unresolved = new List<string>();
            for (int i = 0; i < TraversalKeys.SystemKeys.Length; i++)
            {
                if (!TraversalCatalog.TryGetSystemFactory(TraversalKeys.SystemKeys[i], out ITraversalSystemFactory? system)
                    || system == null)
                {
                    unresolved.Add("system:" + TraversalKeys.SystemKeys[i].ToString());
                }
            }

            if (!TraversalCatalog.TryGetPluginFactory(TraversalKeys.PluginFactoryKey, out ITraversalCoursePluginFactory? plugin)
                || plugin == null)
            {
                unresolved.Add("plugin:" + TraversalKeys.PluginFactoryKey.ToString());
            }

            if (!TraversalCatalog.TryGetReducer(TraversalVocabulary.AccelerationReducerKey, out ITraversalAccelerationReducer? reducer)
                || reducer == null)
            {
                unresolved.Add("reducer:" + TraversalVocabulary.AccelerationReducerKey.ToString());
            }

            if (!TraversalCatalog.TryGetStaticPredicate(TraversalVocabulary.AlwaysPredicateKey, out ITraversalTargetPredicate? predicate)
                || predicate == null)
            {
                unresolved.Add("predicate:" + TraversalVocabulary.AlwaysPredicateKey.ToString());
            }

            bool passed = unresolved.Count == 0
                && string.Equals(generatedFingerprint, fixtureFingerprint, StringComparison.Ordinal)
                && string.Equals(generatedFingerprint, tableFingerprint, StringComparison.Ordinal);
            detail = "generatedFingerprint=" + generatedFingerprint
                + "; fixtureTableFingerprint=" + fixtureFingerprint
                + "; equal=" + (passed ? "true" : "false")
                + "; generatedFileHash=" + TraversalCatalog.CatalogFileHash
                + "; groups=" + TraversalCatalog.RegistrationGroupCount.ToString(CultureInfo.InvariantCulture)
                + "; unresolved=" + (unresolved.Count == 0 ? "<none>" : string.Join(",", unresolved));
            return passed;
        }

        // ------------------------------------------------------------------------------------------------------
        // 9. Inactive plugin late mount: linked into the player, not activated by the startup scene, mountable by key.
        // ------------------------------------------------------------------------------------------------------

        private static bool LateMountStep(out string detail)
        {
            // The GC-001 fixture plugin: the linked-but-inactive plugin whose factory nothing else in the startup
            // scene calls, resolved by key from the generated probe catalog.
            if (!ProbeCatalog.TryGetPluginFactory(ProbeKeys.FixturePluginKey, out IProbePluginFactory? fixtureFactory)
                || fixtureFactory == null)
            {
                detail = "the generated probe catalog has no registration for the linked fixture plugin";
                return false;
            }

            int createdBeforeMount = fixtureFactory.CreatedInstanceCount;
            if (createdBeforeMount != 0)
            {
                detail = "the fixture plugin was already instantiated "
                    + createdBeforeMount.ToString(CultureInfo.InvariantCulture)
                    + " time(s) before the late mount, so it is not linked-but-inactive";
                return false;
            }

            IProbePlugin plugin = fixtureFactory.Create();
            if (!ProbeCatalog.TryGetHandler(ProbeKeys.ClosedGenericHandlerKey, out IProbeHandler<ProbeAmount, int>? handler)
                || handler == null)
            {
                detail = "the generated probe catalog has no closed generic handler registration";
                return false;
            }

            var amount = new ProbeAmount(3, 5);
            int handled = plugin.ExecuteClosedHandler(handler, amount);

            // The card table factory and the traversal course factory are the same shape for the other two genres:
            // resolvable by key from their own generated catalog, with a direct constructor reference behind them.
            bool cardFactoryResolved = CardCatalog.TryGetPluginFactory(
                GameCore.Gameplay.Cards.CardTableKeys.PluginFactoryKey,
                out GameCore.Gameplay.Cards.ICardTablePluginFactory? cardFactory)
                && cardFactory != null;
            bool courseFactoryResolved = TraversalCatalog.TryGetPluginFactory(
                TraversalKeys.PluginFactoryKey, out ITraversalCoursePluginFactory? courseFactory)
                && courseFactory != null;

            bool passed = handled == amount.Scalar
                && fixtureFactory.CreatedInstanceCount == 1
                && cardFactoryResolved
                && courseFactoryResolved;
            detail = "linkedInactiveInstanceCountBeforeMount=" + createdBeforeMount.ToString(CultureInfo.InvariantCulture)
                + "; instanceCountAfterMount=" + fixtureFactory.CreatedInstanceCount.ToString(CultureInfo.InvariantCulture)
                + "; handled=" + handled.ToString(CultureInfo.InvariantCulture)
                + "; expectedHandled=" + amount.Scalar.ToString(CultureInfo.InvariantCulture)
                + "; cardTablePluginFactory=" + (cardFactoryResolved ? "resolved" : "missing")
                + "; traversalCoursePluginFactory=" + (courseFactoryResolved ? "resolved" : "missing");
            return passed;
        }

        // ------------------------------------------------------------------------------------------------------
        // 10. Bake/runtime recipe parity (TEST-020, 04 section 6).
        // ------------------------------------------------------------------------------------------------------

        private static bool BakeRuntimeParityStep(out string detail)
        {
            if (!TryTraversalCatalog(out ImmutableCatalog catalog, out string catalogDetail))
            {
                detail = catalogDetail;
                return false;
            }

            var runtimeFamily = new Gc020TraversalHost.CourseFamily(
                catalog,
                Gc020TraversalHost.Declarations(),
                TraversalCatalogTable.Fingerprint().ToHex(),
                RuntimeCatalogCoverageRecipeSource.Instance);
            var bakedFamily = new Gc020TraversalHost.CourseFamily(
                catalog,
                Gc020TraversalHost.Declarations(),
                TraversalCatalogTable.Fingerprint().ToHex(),
                BakedCatalogCoverageRecipeSource.Instance);

            SpawnRecipeCatalog runtimeRecipes = runtimeFamily.CreateRecipes();
            SpawnRecipeCatalog bakedRecipes = bakedFamily.CreateRecipes();

            if (!runtimeRecipes.TryResolve(TraversalKeys.RunnerRecipe, out SpawnRecipe? runtimeRecipe, out DiagnosticCode runtimeCode)
                || runtimeRecipe == null)
            {
                detail = "the runtime recipe catalog does not resolve the runner recipe: " + runtimeCode;
                return false;
            }

            if (!bakedRecipes.TryResolve(TraversalKeys.RunnerRecipe, out SpawnRecipe? bakedRecipe, out DiagnosticCode bakedCode)
                || bakedRecipe == null)
            {
                detail = "the baked recipe catalog does not resolve the runner recipe: " + bakedCode;
                return false;
            }

            var mismatches = new List<string>();
            if (!runtimeRecipe.Recipe.Equals(bakedRecipe.Recipe))
            {
                mismatches.Add("definition");
            }

            if (!SameSchemas(runtimeRecipe.BaseLayout, bakedRecipe.BaseLayout))
            {
                mismatches.Add("baseLayout");
            }

            if (!SameIds(runtimeRecipe.Descriptor.Tags, bakedRecipe.Descriptor.Tags))
            {
                mismatches.Add("descriptorTags");
            }

            if (!runtimeRecipe.Applier.Key.Equals(bakedRecipe.Applier.Key))
            {
                mismatches.Add("applierKey");
            }

            if (!runtimeRecipes.Fingerprint().Equals(bakedRecipes.Fingerprint()))
            {
                mismatches.Add("recipeCatalogFingerprint");
            }

            // Behaviour: both materializations run the whole course and must record the same observations, the same
            // canonical numbers and the same digest. The comparison is of normalized values, never of chunk layout or
            // native entity indices (TEST-020).
            Gc020ScenarioResult runtimeRun = Gc020Scenario.Run(runtimeFamily);
            Gc020ScenarioResult bakedRun = Gc020Scenario.Run(bakedFamily);

            if (!runtimeRun.AllPassed)
            {
                mismatches.Add("runtimeRunFailed(" + runtimeRun.Describe() + ")");
            }

            if (!bakedRun.AllPassed)
            {
                mismatches.Add("bakedRunFailed(" + bakedRun.Describe() + ")");
            }

            if (!string.Equals(runtimeRun.Digest, bakedRun.Digest, StringComparison.Ordinal))
            {
                mismatches.Add("runDigest");
            }

            string runtimeCanon = CanonicalFragments(runtimeRun);
            string bakedCanon = CanonicalFragments(bakedRun);
            if (!string.Equals(runtimeCanon, bakedCanon, StringComparison.Ordinal))
            {
                mismatches.Add("canonicalFragments");
            }

            detail = "runtimeSource=" + runtimeFamily.RecipeSourceLabel
                + "; bakedSource=" + bakedFamily.RecipeSourceLabel
                + "; definition=" + runtimeRecipe.Recipe.Id.Value.ToString()
                + "; baseLayoutSchemas=" + runtimeRecipe.BaseLayout.Count.ToString(CultureInfo.InvariantCulture)
                + "; descriptorTags=" + runtimeRecipe.Descriptor.Tags.Count.ToString(CultureInfo.InvariantCulture)
                + "; applierKey=" + runtimeRecipe.Applier.Key.ToString()
                + "; runtimeRecipeFingerprint=" + runtimeRecipes.Fingerprint().ToHex()
                + "; bakedRecipeFingerprint=" + bakedRecipes.Fingerprint().ToHex()
                + "; runtimeDigest=" + runtimeRun.Digest
                + "; bakedDigest=" + bakedRun.Digest
                + "; canonical=" + runtimeCanon
                + "; mismatches=" + (mismatches.Count == 0 ? "<none>" : string.Join(",", mismatches));
            return mismatches.Count == 0;
        }

        /// <summary>
        /// The canonical numbers of one traversal run, extracted from the run's own step details: the values the
        /// pure-rule vocabulary declares, so two materializations cannot agree on the digest while disagreeing on
        /// the motion the run actually produced.
        /// </summary>
        private static string CanonicalFragments(Gc020ScenarioResult run)
        {
            string[] markers =
            {
                "expectedVelocity=",
                "poseAdvanceX=",
                "providerValue=",
                "stepMillis=",
                "maxStepsPerPump=",
            };

            var found = new List<string>();
            for (int s = 0; s < run.Steps.Count; s++)
            {
                string detail = run.Steps[s].Detail;
                for (int i = 0; i < markers.Length; i++)
                {
                    int index = detail.IndexOf(markers[i], StringComparison.Ordinal);
                    while (index >= 0)
                    {
                        int start = index + markers[i].Length;
                        int end = detail.IndexOf(';', start);
                        string value = end < 0 ? detail.Substring(start) : detail.Substring(start, end - start);
                        found.Add(markers[i] + value);
                        index = detail.IndexOf(markers[i], start, StringComparison.Ordinal);
                    }
                }
            }

            return found.Count == 0 ? "absent" : string.Join("|", found);
        }

        private static bool SameSchemas(IReadOnlyList<SchemaRef> left, IReadOnlyList<SchemaRef> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!left[i].Equals(right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SameIds(IReadOnlyList<Id128> left, IReadOnlyList<Id128> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!left[i].Equals(right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        // ------------------------------------------------------------------------------------------------------
        // 11. Unknown and stale recipes are refused with their declared diagnostics (TEST-020).
        // ------------------------------------------------------------------------------------------------------

        private static bool UnknownRecipeStep(out string detail)
        {
            if (!TryTraversalCatalog(out ImmutableCatalog catalog, out string catalogDetail))
            {
                detail = catalogDetail;
                return false;
            }

            var family = new Gc020TraversalHost.CourseFamily(
                catalog,
                Gc020TraversalHost.Declarations(),
                TraversalCatalogTable.Fingerprint().ToHex());
            SpawnRecipeCatalog recipes = family.CreateRecipes();

            var unknown = new DefinitionRef(
                TraversalIdentity.Definition(UnknownRecipeStableName),
                TraversalIdentity.SchemaRef(UnknownRecipeStableName),
                DefinitionRevision.First);
            bool unknownRefused = !recipes.TryResolve(unknown, out SpawnRecipe? unknownRecipe, out DiagnosticCode unknownCode)
                && unknownRecipe == null
                && unknownCode == DiagnosticCode.MissingDependency;

            var stale = new DefinitionRef(
                TraversalKeys.RunnerRecipe.Id,
                TraversalKeys.RunnerRecipe.Schema,
                new DefinitionRevision(TraversalKeys.RunnerRecipe.Revision.Value + 1UL));
            bool staleRefused = !recipes.TryResolve(stale, out SpawnRecipe? staleRecipe, out DiagnosticCode staleCode)
                && staleRecipe == null
                && staleCode == DiagnosticCode.StalePlan;

            detail = "unknownRecipeCode=" + unknownCode
                + "; staleRevisionCode=" + staleCode
                + "; unknownRecipeCount=" + recipes.UnknownRecipeCount.ToString(CultureInfo.InvariantCulture)
                + "; staleRevisionCount=" + recipes.StaleRevisionCount.ToString(CultureInfo.InvariantCulture)
                + "; declaredRecipes=" + recipes.Count.ToString(CultureInfo.InvariantCulture);
            return unknownRefused && staleRefused && recipes.UnknownRecipeCount == 1 && recipes.StaleRevisionCount == 1;
        }

        // ------------------------------------------------------------------------------------------------------
        // 12. Stop and restart of the world host.
        // ------------------------------------------------------------------------------------------------------

        private static bool WorldStopRestartStep(out string detail)
        {
            if (!TryTraversalCatalog(out ImmutableCatalog catalog, out string catalogDetail))
            {
                detail = catalogDetail;
                return false;
            }

            string fingerprint = TraversalCatalogTable.Fingerprint().ToHex();

            // Two complete course runs under two different session salts: each run creates its own world, drives the
            // whole course and tears it down (its own teardown observation asserts that exactly its world was
            // registered before the stop and that the registry returned to its pre-create count, with no outstanding
            // jobs and no retained resources). The second world is therefore created after the first was disposed,
            // and its session identity is fresh (P-004, P-035).
            Gc020ScenarioResult first = Gc020Scenario.Run(
                new Gc020TraversalHost.CourseFamily(
                    catalog, Gc020TraversalHost.Declarations(), fingerprint,
                    RuntimeCatalogCoverageRecipeSource.Instance, FirstRestartSalt));
            Gc020ScenarioResult second = Gc020Scenario.Run(
                new Gc020TraversalHost.CourseFamily(
                    catalog, Gc020TraversalHost.Declarations(), fingerprint,
                    RuntimeCatalogCoverageRecipeSource.Instance, SecondRestartSalt));

            string firstSession = Fragment(first, "session=");
            string secondSession = Fragment(second, "session=");
            bool sessionsDiffer = firstSession.Length != 0
                && secondSession.Length != 0
                && !string.Equals(firstSession, secondSession, StringComparison.Ordinal);

            // The registry was at the same size before each world was created, so the first host really was
            // unregistered before the second one existed rather than leaking into the restart.
            string firstBefore = Fragment(first, "registryBeforeCreate=");
            string secondBefore = Fragment(second, "registryBeforeCreate=");
            bool registryRestored = firstBefore.Length != 0
                && string.Equals(firstBefore, secondBefore, StringComparison.Ordinal);

            // The restarted host runs the same course: the same observation table, the same digest, the same
            // canonical numbers.
            bool sameRun = first.AllPassed && second.AllPassed
                && string.Equals(first.Digest, second.Digest, StringComparison.Ordinal)
                && string.Equals(CanonicalFragments(first), CanonicalFragments(second), StringComparison.Ordinal);

            detail = "firstSession=" + firstSession
                + "; secondSession=" + secondSession
                + "; sessionsDiffer=" + (sessionsDiffer ? "true" : "false")
                + "; registryBeforeCreate=" + firstBefore
                + "; registryRestored=" + (registryRestored ? "true" : "false")
                + "; digest=" + first.Digest
                + "; restartedDigest=" + second.Digest
                + "; firstRun=" + first.Describe()
                + "; secondRun=" + second.Describe();
            return sessionsDiffer && registryRestored && sameRun;
        }

        private static string Fragment(Gc020ScenarioResult run, string marker)
        {
            for (int i = 0; i < run.Steps.Count; i++)
            {
                string found = Fragment(run.Steps[i].Detail, marker);
                if (found.Length != 0)
                {
                    return found;
                }
            }

            return string.Empty;
        }

        private static string Fragment(string text, string marker)
        {
            int index = text.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                return string.Empty;
            }

            int start = index + marker.Length;
            int end = text.IndexOf(';', start);
            return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
        }

        // ------------------------------------------------------------------------------------------------------
        // 13. Headless reference execution against the pure-rule canonical fixtures.
        // ------------------------------------------------------------------------------------------------------

        private static bool HeadlessCanonicalStep(out string detail)
        {
            if (!TryTraversalCatalog(out ImmutableCatalog catalog, out string catalogDetail))
            {
                detail = catalogDetail;
                return false;
            }

            Gc020ScenarioResult run = Gc020Scenario.Run(
                new Gc020TraversalHost.CourseFamily(
                    catalog,
                    Gc020TraversalHost.Declarations(),
                    TraversalCatalogTable.Fingerprint().ToHex()));

            // The canonical arithmetic is re-derived here from the pure vocabulary rather than trusted, so a wrong
            // constant in the rules package fails this observation as well as the run's own assertions.
            int stepMillis = TraversalVocabulary.StepMilliseconds;
            long seedPlusTailwind = TraversalVocabulary.SeededVelocityMilli
                + ((long)TraversalVocabulary.TailwindMilli * stepMillis / TraversalVocabulary.MilliScale);
            long tailwindMinusHeadwind = TraversalVocabulary.VelocityAfterTailwindMilli
                + ((long)TraversalVocabulary.HeadwindMilli * stepMillis / TraversalVocabulary.MilliScale);
            int poseAdvance = TraversalVocabulary.VelocityAfterTailwindMilli
                * stepMillis / TraversalVocabulary.MilliScale;

            bool arithmeticHolds = seedPlusTailwind == TraversalVocabulary.VelocityAfterTailwindMilli
                && tailwindMinusHeadwind == TraversalVocabulary.VelocityAfterHeadwindMilli
                && poseAdvance == 20;

            string canonical = CanonicalFragments(run);
            bool canonicalPresent =
                canonical.Contains("expectedVelocity=" + TraversalVocabulary.VelocityAfterTailwindMilli.ToString(CultureInfo.InvariantCulture))
                && canonical.Contains("expectedVelocity=" + TraversalVocabulary.VelocityAfterHeadwindMilli.ToString(CultureInfo.InvariantCulture))
                && canonical.Contains("poseAdvanceX=" + poseAdvance.ToString(CultureInfo.InvariantCulture))
                && canonical.Contains("providerValue=" + TraversalVocabulary.TailwindMilli.ToString(CultureInfo.InvariantCulture))
                && canonical.Contains("stepMillis=" + stepMillis.ToString(CultureInfo.InvariantCulture))
                && canonical.Contains("maxStepsPerPump=" + TraversalVocabulary.MaxStepsPerPump.ToString(CultureInfo.InvariantCulture));

            bool digestMatches = string.Equals(run.Digest, ProbeTraversal.ExpectedDigest, StringComparison.Ordinal);
            bool passed = run.AllPassed && arithmeticHolds && canonicalPresent && digestMatches;
            detail = "digest=" + run.Digest
                + "; expectedDigest=" + ProbeTraversal.ExpectedDigest
                + "; observations=" + run.Steps.Count.ToString(CultureInfo.InvariantCulture)
                + "; canonical=" + canonical
                + "; seededVelocity=" + TraversalVocabulary.SeededVelocityMilli.ToString(CultureInfo.InvariantCulture)
                + "; arithmetic=" + (arithmeticHolds ? "holds" : "broken");
            return passed;
        }

        // ------------------------------------------------------------------------------------------------------
        // Shared setup.
        // ------------------------------------------------------------------------------------------------------

        /// <summary>
        /// The one traversal catalog this build's course runs over: the hand-written generated-style table, which
        /// the generated catalog is compared against in observation 8. Shared by every observation that runs a real
        /// course world, so all of them drive the same declarations.
        /// </summary>
        private static bool TryTraversalCatalog(out ImmutableCatalog catalog, out string detail)
        {
            CatalogBuildResult build = TraversalCatalogTable.Build();
            if (build.Catalog == null)
            {
                catalog = null!;
                detail = "the hand-written generated-style traversal catalog was rejected: " + build.Describe();
                return false;
            }

            catalog = build.Catalog;
            detail = string.Empty;
            return true;
        }
    }
}
