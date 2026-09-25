// GameCore.Rules.Narrative — the narrative slice's declared canonical trace (P-008, P-060).
//
// The committed `artifacts/gc-010/narrative-trace.json` is the document this type regenerates. Two properties make
// it worth committing:
//
//   * the rules section is a pure function of the rules package's declarations, so any process — a plain dotnet
//     test, the Editor, a player — reproduces it byte for byte, and a test can compare the committed file against a
//     fresh generation;
//   * the pipeline section names the exact observations the Unity-world fixture must produce. They are declared
//     here, not recorded after the fact, so the run either matches the declaration or the gate fails. The run's own
//     observed values are written beside it by the probe (`artifacts/gc-010/toolchain/`), never over the committed
//     canonical document.
//
// Nothing here is a measurement: every value is a declared expectation of the reference composition, and the
// evidence for it is the scenario's own assertion (GC-010's EditMode suite and `-probeNarrative`).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Rules.Narrative
{
    /// <summary>The declared observations one narrative run must produce, and the document that records them.</summary>
    public static class NarrativeScenarioTrace
    {
        /// <summary>Number of frames the idle window pumps: 600 frames of 16.667 ms is the ten idle seconds.</summary>
        public const int IdleFrames = 600;

        /// <summary>Host ticks one idle frame advances, at the world's default 10,000,000 ticks per second.</summary>
        public const ulong IdleTicksPerFrame = 166_667UL;

        /// <summary>
        /// The pipeline entries of the committed canonical trace, in the order the scenario reports them. Each value
        /// is a number or a stable word, so a run's own recording can be compared with it as text.
        /// </summary>
        public static IReadOnlyList<NarrativeTraceEntry> ExpectedPipeline()
        {
            return new List<NarrativeTraceEntry>
            {
                Entry("liveTargetCount", "7"),
                Entry("ineligibleTargetCount", "2"),
                Entry("derivedTargetCountAfterChapterOne", "3"),
                Entry("chapterOneInstalledRows", "4"),
                Entry("chapterOneRetractedRows", "0"),
                Entry("migratedConversationSlotCount", "2"),
                Entry("chapterTwoInstalledRows", "2"),
                Entry("publishedBindingRowCountAfterChapterTwo", "6"),
                Entry("compiledStageCount", "6"),
                Entry("compiledSystemCount", "6"),
                Entry("forwardDerivationHadNoTargetChange", "true"),
                Entry("spawnedBindingRowCount", "2"),
                Entry("spawnedBindingValue", "1"),
                Entry("publishedBindingRowCountAfterSpawn", "8"),
                Entry("recipeApplyCount", "1"),
                Entry("admittedChoiceCount", "1"),
                Entry("forwardedChoiceCount", "1"),
                Entry("refusedChoiceCount", "0"),
                Entry("committedFactCount", "1"),
                Entry("duplicateMutationCount", "0"),
                Entry("gateDecisionCount", "1"),
                Entry("encounterHookCount", "1"),
                Entry("gateDecisionBeforeCommand", "0"),
                Entry("gateDecisionAfterCommand", "1"),
                Entry("questFactValueAfterCommand", "1"),
                Entry("questFactVersionAfterCommand", "2"),
                Entry("committedEventCountAfterCommand", "2"),
                Entry("committedEventSchemaAfterCommand", "ba1f284d15dd28bc2bd2e01281932572|4a396c0a9abe7f1de442889d30077ac0"),
                Entry("stepsAfterCommand", "1"),
                Entry("stepsAfterDuplicate", "1"),
                Entry("duplicatePumpSteps", "0"),
                Entry("laneEpochAfterChapterOne", "2"),
                Entry("laneEpochAfterChapterTwo", "3"),
                Entry("worldEpochAfterSpawn", "4"),
                Entry("trailStepsAfterCommand", "1"),
                Entry("trailProjectedStepsAfterCommand", "1"),
                Entry("trailCommittedFactsAfterCommand", "1"),
                Entry("trailGateDecisionsAfterCommand", "1"),
                Entry("trailEncounterHooksAfterCommand", "1"),
                Entry("idleFrames", IdleFrames.ToString(CultureInfo.InvariantCulture)),
                Entry("idleStepsCommitted", "0"),
                Entry("idleDispatchRuns", "0"),
                Entry("genreAuditChecked", NarrativeRegistrations.Count.ToString(CultureInfo.InvariantCulture)),
                Entry("genreAuditNeutral", "true"),
                Entry("forbiddenGenreNameCount", "0"),
                Entry("registeredNamesDigest", NarrativeDigest.OfLines(NarrativeRegistrations.AllNames)),
            };
        }

        /// <summary>An expected entry's value, or a marker when the key is not declared.</summary>
        public static string ExpectedValue(string key)
        {
            IReadOnlyList<NarrativeTraceEntry> entries = ExpectedPipeline();
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].Key, key, StringComparison.Ordinal))
                {
                    return entries[i].Value;
                }
            }

            return "<undeclared>";
        }

        /// <summary>The whole canonical document: the rules section, the name audit and the declared observations.</summary>
        public static string ExpectedDocument()
        {
            return NarrativeTrace.Write(NarrativeRegistrations.AllNames, ExpectedPipeline());
        }

        /// <summary>The digest of the whole canonical document, so a run can report one value for it.</summary>
        public static string ExpectedDocumentDigest() => NarrativeDigest.OfText(ExpectedDocument());

        /// <summary>
        /// Compares one run's recorded pipeline against the declared expectations. Every declared key must be present
        /// with the declared value; an extra key a run records is reported as a mismatch, because a canonical
        /// document with an undeclared observation is not reproducible from the declaration.
        /// </summary>
        public static bool TryCompare(
            IReadOnlyList<NarrativeTraceEntry>? observed,
            out IReadOnlyList<string> mismatches)
        {
            var found = new List<string>();
            IReadOnlyList<NarrativeTraceEntry> expected = ExpectedPipeline();

            if (observed == null)
            {
                found.Add("no pipeline entries were recorded");
                mismatches = found;
                return false;
            }

            for (int i = 0; i < expected.Count; i++)
            {
                string actual = "<missing>";
                for (int j = 0; j < observed.Count; j++)
                {
                    if (string.Equals(observed[j].Key, expected[i].Key, StringComparison.Ordinal))
                    {
                        actual = observed[j].Value;
                        break;
                    }
                }

                if (!string.Equals(actual, expected[i].Value, StringComparison.Ordinal))
                {
                    found.Add(expected[i].Key + ": expected '" + expected[i].Value + "', observed '" + actual + "'");
                }
            }

            for (int i = 0; i < observed.Count; i++)
            {
                bool declared = false;
                for (int j = 0; j < expected.Count; j++)
                {
                    if (string.Equals(observed[i].Key, expected[j].Key, StringComparison.Ordinal))
                    {
                        declared = true;
                        break;
                    }
                }

                if (!declared)
                {
                    found.Add(observed[i].Key + ": observed but not declared");
                }
            }

            mismatches = found;
            return found.Count == 0;
        }

        private static NarrativeTraceEntry Entry(string key, string value) => new NarrativeTraceEntry(key, value);
    }
}
