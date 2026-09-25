#nullable enable
using System;
using System.Globalization;
using NUnit.Framework;

namespace GameCore.Rules.Narrative.Tests
{
    /// <summary>
    /// The genre neutrality audit of the narrative slice (P-001, P-059, TEST-021): every name the slice registers is
    /// narrative or kernel vocabulary, an audit over nothing is refused rather than reported as neutral, and the
    /// registered-name digest is a canonical SHA-256.
    /// </summary>
    [TestFixture]
    public sealed class GenreNeutralityTests
    {
        [Test]
        public void TheRegisteredNameSetIsGenreNeutral()
        {
            GenreAuditReport report = NarrativeRegistrations.Audit();

            Assert.That(NarrativeRegistrations.Count, Is.GreaterThan(0));
            Assert.That(report.Neutral, Is.True);
            Assert.That(report.CheckedCount, Is.EqualTo(NarrativeRegistrations.Count));
            Assert.That(report.CheckedCount, Is.EqualTo(NarrativeRegistrations.AllNames.Count));
            Assert.That(report.ForbiddenNames, Is.Empty);
            Assert.That(
                NarrativeGenreAudit.Describe(report),
                Is.EqualTo(
                    "checked=" + NarrativeRegistrations.Count.ToString(CultureInfo.InvariantCulture)
                    + ", neutral=True, forbidden=0"));
        }

        [Test]
        public void EveryRegisteredNameIsNeutralOnItsOwnAndNoneIsRegisteredTwice()
        {
            for (int i = 0; i < NarrativeRegistrations.AllNames.Count; i++)
            {
                string name = NarrativeRegistrations.AllNames[i];
                Assert.That(name, Is.Not.Empty);
                Assert.That(NarrativeGenreAudit.IsNeutral(name), Is.True, "registered name '" + name + "' is genre vocabulary.");
            }

            Assert.That(NarrativeRegistrations.AllNames, Is.Unique);
        }

        [Test]
        public void TheAuditRefusesNullAndAnEmptyNameListRatherThanReportingNeutral()
        {
            Assert.Throws<ArgumentNullException>(() => NarrativeGenreAudit.Audit(null));
            Assert.Throws<ArgumentException>(() => NarrativeGenreAudit.Audit(new string[0]));
            Assert.Throws<ArgumentNullException>(() => NarrativeGenreAudit.Describe(null!));
            Assert.Throws<ArgumentNullException>(() => NarrativeGenreAudit.IsNeutral(null!));
        }

        [Test]
        public void GenreVocabularyIsRecognizedCaseInsensitivelyAndReportedInExaminationOrder()
        {
            Assert.That(NarrativeGenreAudit.IsNeutral("narrative.binding.dialogue"), Is.True);
            Assert.That(NarrativeGenreAudit.IsNeutral("gamecore.actor-state"), Is.False);
            Assert.That(NarrativeGenreAudit.IsNeutral("Combat"), Is.False);
            Assert.That(NarrativeGenreAudit.IsNeutral("NARRATIVE.VELOCITY"), Is.False);
            Assert.That(NarrativeGenreAudit.ForbiddenTokens, Is.Not.Empty);

            GenreAuditReport report = NarrativeGenreAudit.Audit(
                new[] { "narrative.binding.dialogue", "gamecore.actor-state", "Physics.Probe" });

            Assert.That(report.CheckedCount, Is.EqualTo(3));
            Assert.That(report.Neutral, Is.False);
            Assert.That(
                report.ForbiddenNames,
                Is.EqualTo(new[] { "gamecore.actor-state", "Physics.Probe" }),
                "only the offending names are reported, in the order they were examined.");
            Assert.That(NarrativeGenreAudit.Describe(report), Does.Contain("gamecore.actor-state"));
            Assert.That(NarrativeGenreAudit.Describe(report), Does.Contain("neutral=False"));
            Assert.That(NarrativeGenreAudit.Describe(report), Does.Contain("forbidden=2"));
        }

        [Test]
        public void TheDigestOfTheRegisteredNamesIsACanonicalSha256()
        {
            string digest = NarrativeDigest.OfLines(NarrativeRegistrations.AllNames);

            Assert.That(NarrativeTestSupport.IsLowercaseHex64(digest), Is.True, "the digest is 64 lowercase hex characters.");
            Assert.That(digest, Is.EqualTo(NarrativeDigest.OfLines(NarrativeRegistrations.AllNames)));
            Assert.That(
                digest,
                Is.Not.EqualTo(NarrativeDigest.OfLines(new[] { NarrativeRegistrations.AllNames[0] })),
                "the digest is over the whole name set, not a prefix of it.");
        }
    }
}
