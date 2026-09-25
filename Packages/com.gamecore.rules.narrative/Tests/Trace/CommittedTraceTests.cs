#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.Contracts;
using NUnit.Framework;

namespace GameCore.Rules.Narrative.Tests
{
    /// <summary>
    /// The committed canonical narrative trace (P-008, P-060). The committed file must be exactly what this package
    /// regenerates offline: the rules section is a pure function of the declarations, the name audit has a fixed
    /// input, and the pipeline section is the declared expectation of one narrative run rather than a recording
    /// nobody can reproduce. A drift in a rule, a name, a stratum or a declared expectation therefore fails here,
    /// without a Unity build.
    /// </summary>
    [TestFixture]
    public sealed class CommittedTraceTests
    {
        /// <summary>Repository-relative path of the committed canonical document.</summary>
        private const string RelativeTracePath = "artifacts/gc-010/narrative-trace.json";

        private static string committedText = string.Empty;

        [OneTimeSetUp]
        public void ReadTheCommittedTrace()
        {
            string path = FindTracePath();
            committedText = File.ReadAllText(path);
            Assert.That(committedText, Is.Not.Empty, "the committed canonical trace is empty: " + path);
        }

        [Test]
        public void TheCommittedDocumentIsExactlyWhatThisPackageRegenerates()
        {
            string regenerated = NarrativeScenarioTrace.ExpectedDocument();

            Assert.That(
                committedText,
                Is.EqualTo(regenerated),
                "the committed canonical trace differs from a fresh generation, so a declaration moved without the "
                + "trace being regenerated");

            Assert.That(NarrativeScenarioTrace.ExpectedDocumentDigest(), Is.EqualTo(NarrativeDigest.OfText(regenerated)));
        }

        [Test]
        public void TheCommittedDocumentDeclaresItsOwnFormatAndRulesDigest()
        {
            Assert.That(committedText, Does.Contain("\"traceFormat\": \"gamecore.narrative-trace/1\","));
            Assert.That(committedText, Does.Contain("\"protocolVersion\": \"1.0\","));
            Assert.That(committedText, Does.Contain("\"task\": \"" + NarrativeTrace.Task + "\","));
            Assert.That(committedText, Does.Contain("\"rulesDigest\": \"" + NarrativeTrace.RulesDigest() + "\","));
            Assert.That(committedText, Does.Contain(
                "\"registeredNamesDigest\": \"" + NarrativeDigest.OfLines(NarrativeRegistrations.AllNames) + "\""));
        }

        [Test]
        public void TheCommittedPipelineEqualsTheDeclaredExpectation()
        {
            Assert.That(
                NarrativeTrace.TryReadPipelineEntries(committedText, out IReadOnlyList<NarrativeTraceEntry> entries),
                Is.True,
                "the committed document has no readable pipeline section");
            Assert.That(entries, Is.Not.Empty);

            bool compared = NarrativeScenarioTrace.TryCompare(entries, out IReadOnlyList<string> mismatches);
            Assert.That(mismatches, Is.Empty, string.Join(" | ", ToArray(mismatches)));
            Assert.That(compared, Is.True);
        }

        [Test]
        public void TryCompareReportsAWrongValueAndAnUndeclaredObservation()
        {
            var wrong = new List<NarrativeTraceEntry>();
            IReadOnlyList<NarrativeTraceEntry> declared = NarrativeScenarioTrace.ExpectedPipeline();
            for (int i = 0; i < declared.Count; i++)
            {
                wrong.Add(declared[i]);
            }

            IReadOnlyList<NarrativeTraceEntry> first = NarrativeScenarioTrace.ExpectedPipeline();
            wrong[0] = new NarrativeTraceEntry(first[0].Key, "<wrong>");
            wrong.Add(new NarrativeTraceEntry("narrative.undeclared", "1"));

            bool compared = NarrativeScenarioTrace.TryCompare(wrong, out IReadOnlyList<string> mismatches);
            Assert.That(compared, Is.False);
            Assert.That(mismatches.Count, Is.EqualTo(2));
            Assert.That(mismatches[0], Does.Contain(first[0].Key));

            Assert.That(NarrativeScenarioTrace.TryCompare(null, out IReadOnlyList<string> missing), Is.False);
            Assert.That(missing, Is.Not.Empty);

            Assert.That(NarrativeScenarioTrace.ExpectedValue("liveTargetCount"), Is.EqualTo("7"));
            Assert.That(NarrativeScenarioTrace.ExpectedValue("narrative.undeclared"), Is.EqualTo("<undeclared>"));
        }

        [Test]
        public void TheDeclaredPipelineCoversEveryFactTheEditModeSuiteAsserts()
        {
            // The Unity EditMode suite and this declaration are the same claims; a key one side names and the other
            // does not would make the gate pass on a value nobody asserted.
            string[] asserted =
            {
                "liveTargetCount",
                "derivedTargetCountAfterChapterOne",
                "chapterOneInstalledRows",
                "migratedConversationSlotCount",
                "publishedBindingRowCountAfterChapterTwo",
                "spawnedBindingRowCount",
                "spawnedBindingValue",
                "publishedBindingRowCountAfterSpawn",
                "gateDecisionBeforeCommand",
                "gateDecisionAfterCommand",
                "questFactValueAfterCommand",
                "questFactVersionAfterCommand",
                "committedEventCountAfterCommand",
                "stepsAfterCommand",
                "stepsAfterDuplicate",
                "idleFrames",
                "idleStepsCommitted",
                "genreAuditChecked",
                "genreAuditNeutral",
                "forbiddenGenreNameCount",
            };

            for (int i = 0; i < asserted.Length; i++)
            {
                Assert.That(
                    NarrativeScenarioTrace.ExpectedValue(asserted[i]),
                    Is.Not.EqualTo("<undeclared>"),
                    "the canonical trace does not declare '" + asserted[i] + "'");
            }
        }

        [Test]
        public void TheFactSlotTagsAreCanonicalAndRoundTrip()
        {
            Assert.That(NarrativeFacts.TryGetFactSlotTag(NarrativeFacts.BridgePermitFactKey, out string bridge), Is.True);
            Assert.That(bridge, Is.EqualTo("bridge-permit"));
            Assert.That(NarrativeFacts.TryGetFactSlotTag(NarrativeFacts.HarborPermitFactKey, out string harbor), Is.True);
            Assert.That(harbor, Is.EqualTo("harbor-permit"));
            Assert.That(NarrativeFacts.TryGetFactSlotTag("chapter1.unknown", out string unknown), Is.False);
            Assert.That(unknown, Is.Empty);

            // A slot tag is a stable name fragment, so the derived slot identity is canonical and the key itself
            // never has to become one (P-004).
            Assert.That(bridge, Does.Not.Contain("."));
            Assert.That(NarrativeFacts.TryGetFactKeyByOrdinal(0, out string first), Is.True);
            Assert.That(first, Is.EqualTo(NarrativeFacts.BridgePermitFactKey));
            Assert.That(NarrativeFacts.TryGetFactKeyByOrdinal(NarrativeFacts.DeclaredFactKeys.Count, out string past), Is.False);
            Assert.That(past, Is.Empty);

            // The slots one fact declares are distinct from each other and from every other fact's slots.
            var slots = new List<string>();
            for (int i = 0; i < NarrativeFacts.DeclaredFactKeys.Count; i++)
            {
                NarrativeFacts.TryGetFactSlotTag(NarrativeFacts.DeclaredFactKeys[i], out string tag);
                slots.Add(NarrativeCompositionNames.FactValueSlotName(tag));
                slots.Add(NarrativeCompositionNames.FactVersionSlotName(tag));
                slots.Add(NarrativeCompositionNames.FactValueFieldName(tag));
                slots.Add(NarrativeCompositionNames.FactVersionFieldName(tag));
            }

            Assert.That(slots, Is.Unique);
            for (int i = 0; i < slots.Count; i++)
            {
                Assert.That(StableNameKeyDerivation.IsCanonicalStableName(slots[i]), Is.True, "'" + slots[i] + "' is not canonical");
            }
        }

        private static string FindTracePath()
        {
            // The Unity Editor runs tests from its installation but opens the project as its working directory.
            // Plain dotnet tests use the test assembly directory instead; walk both without an engine reference.
            string workingTree = FindTraceAbove(Directory.GetCurrentDirectory());
            if (workingTree.Length != 0)
            {
                return workingTree;
            }

            string assemblyTree = FindTraceAbove(AppContext.BaseDirectory);
            if (assemblyTree.Length != 0)
            {
                return assemblyTree;
            }

            throw new FileNotFoundException(
                "the committed canonical trace " + RelativeTracePath + " was not found above "
                + Directory.GetCurrentDirectory() + " or " + AppContext.BaseDirectory);
        }

        private static string FindTraceAbove(string startingDirectory)
        {
            DirectoryInfo? directory = new DirectoryInfo(startingDirectory);
            for (int depth = 0; directory != null && depth < 12; depth++)
            {
                string candidate = Path.Combine(directory.FullName, RelativeTracePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return string.Empty;
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
    }
}
