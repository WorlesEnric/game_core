#nullable enable
using System;
using NUnit.Framework;

namespace GameCore.Rules.Narrative.Tests
{
    /// <summary>
    /// The one canonical digest function of the package (P-008, P-028, P-060): SHA-256 over explicitly ordered,
    /// newline-separated canonical text, lowercase hex. The known answers below are the SHA-256 of the UTF-8 input,
    /// so a change of encoding, of order or of hex case fails here instead of in a trace comparison.
    /// </summary>
    [TestFixture]
    public sealed class DigestTests
    {
        private const string Sha256OfGamecore = "0f8cb6a55a349cf4234b61fb174c28888994b59c421bd57ef11c7fcf37b9e279";
        private const string Sha256OfEmpty = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        [Test]
        public void TheKnownAnswersAreTheSha256OfTheUtf8Input()
        {
            Assert.That(NarrativeDigest.OfText("gamecore"), Is.EqualTo(Sha256OfGamecore));
            Assert.That(NarrativeDigest.OfText(string.Empty), Is.EqualTo(Sha256OfEmpty));
        }

        [Test]
        public void TheLineDigestIsOrderSensitiveAndJoinsWithASingleLineFeed()
        {
            Assert.That(
                NarrativeDigest.OfLines(new[] { "a", "b" }),
                Is.Not.EqualTo(NarrativeDigest.OfLines(new[] { "b", "a" })),
                "declaration order is content, so a permutation changes the digest (P-008).");
            Assert.That(NarrativeDigest.OfLines(new[] { "a", "b" }), Is.EqualTo(NarrativeDigest.OfText("a\nb")));
            Assert.That(NarrativeDigest.OfLines(new[] { "ab" }), Is.EqualTo(NarrativeDigest.OfText("ab")));
            Assert.That(NarrativeDigest.OfLines(new[] { "ab" }), Is.Not.EqualTo(NarrativeDigest.OfLines(new[] { "a", "b" })));
            Assert.That(NarrativeDigest.OfLines(new[] { string.Empty, "a" }), Is.EqualTo(NarrativeDigest.OfText("\na")));
        }

        [Test]
        public void NoRecordedLineIsTheDigestOfNothing()
        {
            Assert.That(NarrativeDigest.OfLines(null), Is.EqualTo(NarrativeDigest.OfText(string.Empty)));
            Assert.That(NarrativeDigest.OfLines(new string[0]), Is.EqualTo(NarrativeDigest.OfText(string.Empty)));
            Assert.That(NarrativeDigest.OfLines(null), Is.EqualTo(Sha256OfEmpty));
            Assert.That(NarrativeDigest.OfLines(new[] { "a" }), Is.Not.EqualTo(NarrativeDigest.OfLines(null)));
        }

        [Test]
        public void TheDigestIsDeterministicCanonicalHexAndRefusesNoText()
        {
            string first = NarrativeDigest.OfText(NarrativeCompositionNames.ChapterOne);

            Assert.That(NarrativeTestSupport.IsLowercaseHex64(first), Is.True);
            Assert.That(first, Is.EqualTo(NarrativeDigest.OfText(NarrativeCompositionNames.ChapterOne)));
            Assert.That(first, Is.Not.EqualTo(NarrativeDigest.OfText(NarrativeCompositionNames.ChapterTwo)));
            Assert.That(first, Is.EqualTo(NarrativeDigest.OfText("chapter-one")), "the digest names the declared text.");
            Assert.Throws<ArgumentNullException>(() => NarrativeDigest.OfText(null!));
        }
    }
}
