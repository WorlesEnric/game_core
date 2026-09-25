#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;

namespace GameCore.Rules.Narrative.Tests
{
    /// <summary>
    /// The canonical narrative trace (P-008, P-060): the rules section is a pure function of the package's
    /// declarations and regenerates byte for byte, the registered-name digest labels the audited name set, and the
    /// pipeline section round-trips through the reader that a committed trace is compared with.
    /// </summary>
    [TestFixture]
    public sealed class TraceDocumentTests
    {
        private static readonly NarrativeTraceEntry[] Pipeline =
        {
            new NarrativeTraceEntry("pipeline.publicationRevision", "7"),
            new NarrativeTraceEntry("pipeline.layoutDigest", NarrativeDigest.OfText("derived-layout")),
            new NarrativeTraceEntry("pipeline.quoteAndBackslash", "a\"b\\c"),
        };

        [Test]
        public void TheTraceConstantsNameTheDeclaredFormatTaskAndFile()
        {
            Assert.That(NarrativeTrace.TraceFormat, Is.EqualTo("gamecore.narrative-trace/1"));
            Assert.That(NarrativeTrace.ProtocolVersion, Is.EqualTo("1.0"));
            Assert.That(NarrativeTrace.Task, Is.EqualTo("GC-010"));
            Assert.That(NarrativeTrace.FileName, Is.EqualTo("narrative-trace.json"));
        }

        [Test]
        public void TheRulesSectionIsNonEmptyAndEveryLineIsAKeyValueRow()
        {
            IReadOnlyList<string> lines = NarrativeTrace.RulesSectionLines();

            Assert.That(lines.Count, Is.GreaterThan(0));
            for (int i = 0; i < lines.Count; i++)
            {
                Assert.That(lines[i], Is.Not.Empty);
                Assert.That(lines[i].IndexOf('='), Is.GreaterThan(0), "line " + i + " is not a key=value row: " + lines[i]);
                Assert.That(lines[i], Does.Not.Contain("\n"));
            }
        }

        [Test]
        public void TheRulesDigestIsSha256ShapedAndStableAcrossCalls()
        {
            string first = NarrativeTrace.RulesDigest();
            string second = NarrativeTrace.RulesDigest();

            Assert.That(NarrativeTestSupport.IsLowercaseHex64(first), Is.True);
            Assert.That(first, Is.EqualTo(second), "the rules section is a pure function of the declarations (P-008).");
            Assert.That(first, Is.EqualTo(NarrativeDigest.OfLines(NarrativeTrace.RulesSectionLines())));
        }

        [Test]
        public void TheWrittenDocumentCarriesTheRulesSectionTheAuditAndTheRegisteredNameDigest()
        {
            string text = NarrativeTrace.Write(NarrativeRegistrations.AllNames, null);

            Assert.That(
                text.StartsWith("{\n  \"traceFormat\": \"gamecore.narrative-trace/1\",", StringComparison.Ordinal),
                Is.True,
                "the writer emits the declared format header first.");
            Assert.That(text, Does.Contain("\"protocolVersion\": \"" + NarrativeTrace.ProtocolVersion + "\""));
            Assert.That(text, Does.Contain("\"task\": \"" + NarrativeTrace.Task + "\""));
            Assert.That(text, Does.Contain("\"rulesDigest\": \"" + NarrativeTrace.RulesDigest() + "\""));
            Assert.That(
                text,
                Does.Contain("\"registeredNamesDigest\": \"" + NarrativeDigest.OfLines(NarrativeRegistrations.AllNames) + "\""));
            Assert.That(text, Does.Contain("\"checked\": " + NarrativeRegistrations.Count.ToString(CultureInfo.InvariantCulture)));
            Assert.That(text, Does.Contain("\"neutral\": true"));
            Assert.That(text, Does.Contain("\"forbiddenCount\": 0"));

            IReadOnlyList<string> rules = NarrativeTrace.RulesSectionLines();
            for (int i = 0; i < rules.Count; i++)
            {
                Assert.That(text, Does.Contain("\"" + rules[i] + "\""), "the written document misses rules line " + i);
            }

            for (int i = 0; i < NarrativeRegistrations.AllNames.Count; i++)
            {
                Assert.That(text, Does.Contain("\"" + NarrativeRegistrations.AllNames[i] + "\""));
            }

            Assert.That(
                NarrativeTrace.Write(NarrativeRegistrations.AllNames, null),
                Is.EqualTo(text),
                "the same input produces the same bytes.");
        }

        [Test]
        public void ThePipelineSectionRoundTripsThroughTheReaderIncludingEscapedValues()
        {
            string text = NarrativeTrace.Write(NarrativeRegistrations.AllNames, Pipeline);

            Assert.That(NarrativeTrace.TryReadPipelineEntries(text, out IReadOnlyList<NarrativeTraceEntry> entries), Is.True);
            Assert.That(entries.Count, Is.EqualTo(Pipeline.Length));
            for (int i = 0; i < Pipeline.Length; i++)
            {
                Assert.That(entries[i].Key, Is.EqualTo(Pipeline[i].Key));
                Assert.That(entries[i].Value, Is.EqualTo(Pipeline[i].Value));
            }

            Assert.That(entries[Pipeline.Length - 1].Value, Is.EqualTo("a\"b\\c"));
            Assert.That(entries[Pipeline.Length - 1].ToString(), Is.EqualTo("pipeline.quoteAndBackslash=a\"b\\c"));
        }

        [Test]
        public void TheWrittenDocumentRebuildsByteForByteFromThePipelineEntriesItCarries()
        {
            string committed = NarrativeTrace.Write(NarrativeRegistrations.AllNames, Pipeline);

            Assert.That(NarrativeTrace.TryReadPipelineEntries(committed, out IReadOnlyList<NarrativeTraceEntry> entries), Is.True);
            Assert.That(
                NarrativeTrace.Write(NarrativeRegistrations.AllNames, entries),
                Is.EqualTo(committed),
                "the reader recovers exactly what makes the document regenerable (P-008).");
        }

        [Test]
        public void TheReaderRefusesADocumentWithoutAPipelineSectionInsteadOfReturningAPartialTrace()
        {
            Assert.That(
                NarrativeTrace.TryReadPipelineEntries(
                    "{\n  \"rules\": [\n  ]\n}\n",
                    out IReadOnlyList<NarrativeTraceEntry> withoutSection),
                Is.False);
            Assert.That(withoutSection, Is.Empty);

            Assert.That(
                NarrativeTrace.TryReadPipelineEntries(string.Empty, out IReadOnlyList<NarrativeTraceEntry> empty),
                Is.False);
            Assert.That(empty, Is.Empty);

            Assert.That(
                NarrativeTrace.TryReadPipelineEntries(
                    "{\n  \"pipeline\": {\n    not a row\n  }\n}\n",
                    out IReadOnlyList<NarrativeTraceEntry> malformed),
                Is.False,
                "a shape the reader does not understand is a refusal, never a partial read.");
            Assert.That(malformed, Is.Empty);

            Assert.Throws<ArgumentNullException>(
                () => NarrativeTrace.TryReadPipelineEntries(null!, out IReadOnlyList<NarrativeTraceEntry> _));
        }

        [Test]
        public void WritingWithoutRegisteredNamesIsRefusedBecauseAnAuditOverNothingIsNotEvidence()
        {
            Assert.Throws<ArgumentException>(() => NarrativeTrace.Write(null, null));
            Assert.Throws<ArgumentException>(() => NarrativeTrace.Write(new string[0], Pipeline));
        }
    }
}
