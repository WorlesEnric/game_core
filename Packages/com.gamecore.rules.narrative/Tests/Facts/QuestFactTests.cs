#nullable enable
using NUnit.Framework;

namespace GameCore.Rules.Narrative.Tests
{
    /// <summary>
    /// The durable quest facts as pure integer rules (07 s3.2, P-032, P-044): a fact is a declared slot whose value
    /// is one of {0, 1}, an out-of-domain request is refused before any write, and a request that would not change
    /// the value is refused so a duplicate mutation emits no second transition (REF-N02).
    /// </summary>
    [TestFixture]
    public sealed class QuestFactTests
    {
        [Test]
        public void TheDeclaredFactKeysAreOrderedAndEveryOneHasASlotOrdinal()
        {
            Assert.That(NarrativeFacts.DeclaredFactKeys.Count, Is.EqualTo(2));
            Assert.That(
                NarrativeFacts.DeclaredFactKeys,
                Is.EqualTo(new[] { NarrativeFacts.BridgePermitFactKey, NarrativeFacts.HarborPermitFactKey }));

            for (int i = 0; i < NarrativeFacts.DeclaredFactKeys.Count; i++)
            {
                string key = NarrativeFacts.DeclaredFactKeys[i];
                Assert.That(NarrativeFacts.TryGetFactOrdinal(key, out int ordinal), Is.True);
                Assert.That(ordinal, Is.EqualTo(i), "a fact's ordinal is its declaration position, not a hash.");
                Assert.That(NarrativeFacts.IsDeclared(key), Is.True);
            }

            Assert.That(NarrativeFacts.TryGetFactOrdinal("chapter3.unknown", out int undeclared), Is.False);
            Assert.That(undeclared, Is.EqualTo(-1), "a miss reports no slot rather than a guessed one (P-015).");
            Assert.That(NarrativeFacts.IsDeclared("chapter3.unknown"), Is.False);
            Assert.That(NarrativeFacts.TryGetFactOrdinal(string.Empty, out int none), Is.False);
            Assert.That(none, Is.EqualTo(-1));
        }

        [Test]
        public void TheDeclaredInitiationIsUnsetAtTheInitialVersion()
        {
            Assert.That(NarrativeFacts.False, Is.EqualTo(0));
            Assert.That(NarrativeFacts.True, Is.EqualTo(1));
            Assert.That(NarrativeFacts.InitialValue, Is.EqualTo(NarrativeFacts.False));
            Assert.That(NarrativeFacts.InitialVersion, Is.EqualTo(1));
            Assert.That(NarrativeFacts.BridgePermitFactKey, Is.EqualTo("chapter1.bridgePermit"));
            Assert.That(NarrativeFacts.HarborPermitFactKey, Is.EqualTo("chapter2.harborPermit"));
        }

        [Test]
        public void AnUnsetFactAcceptsThePermitTransitionAndReportsTheNewValue()
        {
            bool accepted = NarrativeFacts.TryTransition(
                NarrativeFacts.False,
                NarrativeFacts.True,
                out int next,
                out string refusalCode);

            Assert.That(accepted, Is.True);
            Assert.That(next, Is.EqualTo(NarrativeFacts.True));
            Assert.That(refusalCode, Is.EqualTo(NarrativeRefusals.None));

            bool withoutCode = NarrativeFacts.TryTransition(NarrativeFacts.False, NarrativeFacts.True, out int nextWithoutCode);
            Assert.That(withoutCode, Is.True);
            Assert.That(nextWithoutCode, Is.EqualTo(next));
        }

        [Test]
        public void ADuplicateMutationIsRefusedAsUnchangedAndLeavesTheValueAlone()
        {
            bool accepted = NarrativeFacts.TryTransition(
                NarrativeFacts.True,
                NarrativeFacts.True,
                out int next,
                out string refusalCode);

            Assert.That(accepted, Is.False);
            Assert.That(refusalCode, Is.EqualTo(NarrativeRefusals.FactUnchanged));
            Assert.That(next, Is.EqualTo(NarrativeFacts.True), "a refusal writes nothing (P-044).");

            Assert.That(
                NarrativeFacts.TryTransition(NarrativeFacts.False, NarrativeFacts.False, out int unchanged, out string clearing),
                Is.False);
            Assert.That(clearing, Is.EqualTo(NarrativeRefusals.FactUnchanged));
            Assert.That(unchanged, Is.EqualTo(NarrativeFacts.False));
        }

        [Test]
        public void AnOutOfDomainValueIsRefusedAndLeavesTheValueAlone()
        {
            int[] outOfDomain = { -1, 2, 99, int.MaxValue };
            for (int i = 0; i < outOfDomain.Length; i++)
            {
                bool accepted = NarrativeFacts.TryTransition(
                    NarrativeFacts.False,
                    outOfDomain[i],
                    out int next,
                    out string refusalCode);

                Assert.That(accepted, Is.False, "requested value " + outOfDomain[i] + " is not in the declared domain.");
                Assert.That(refusalCode, Is.EqualTo(NarrativeRefusals.FactValueOutOfDomain));
                Assert.That(next, Is.EqualTo(NarrativeFacts.False));
            }

            Assert.That(
                NarrativeFacts.TryTransition(7, NarrativeFacts.True, out int corrupt, out string corruptRefusal),
                Is.False);
            Assert.That(corruptRefusal, Is.EqualTo(NarrativeRefusals.FactValueOutOfDomain));
            Assert.That(corrupt, Is.EqualTo(7), "a corrupt current value is reported, never silently repaired.");
        }

        [Test]
        public void TheValueDomainIsExactlyTheTwoDeclaredFlagValues()
        {
            Assert.That(NarrativeFacts.IsValidValue(NarrativeFacts.False), Is.True);
            Assert.That(NarrativeFacts.IsValidValue(NarrativeFacts.True), Is.True);
            Assert.That(NarrativeFacts.IsValidValue(-1), Is.False);
            Assert.That(NarrativeFacts.IsValidValue(2), Is.False);

            Assert.That(NarrativeFacts.IsSameFact(NarrativeFacts.False, NarrativeFacts.False), Is.True);
            Assert.That(NarrativeFacts.IsSameFact(NarrativeFacts.False, NarrativeFacts.True), Is.False);
            Assert.That(NarrativeFacts.IsSameFact(NarrativeFacts.True, NarrativeFacts.True), Is.True);
        }

        [Test]
        public void AVersionAdvancesByExactlyOneTransition()
        {
            Assert.That(NarrativeFacts.NextVersion(NarrativeFacts.InitialVersion), Is.EqualTo(NarrativeFacts.InitialVersion + 1));
            Assert.That(NarrativeFacts.NextVersion(41), Is.EqualTo(42));
            Assert.That(NarrativeFacts.NextVersion(0), Is.EqualTo(1));
        }

        [Test]
        public void TheCanonicalTextFormNamesTheKeyTheFlagAndTheVersion()
        {
            Assert.That(
                NarrativeFacts.Describe(NarrativeFacts.BridgePermitFactKey, NarrativeFacts.True, 2),
                Is.EqualTo("chapter1.bridgePermit=true@v2"));
            Assert.That(
                NarrativeFacts.Describe(NarrativeFacts.HarborPermitFactKey, NarrativeFacts.False, NarrativeFacts.InitialVersion),
                Is.EqualTo("chapter2.harborPermit=false@v1"));
            Assert.That(
                NarrativeFacts.Describe(NarrativeFacts.BridgePermitFactKey, NarrativeFacts.True, 12),
                Is.EqualTo("chapter1.bridgePermit=true@v12"),
                "the version is formatted invariantly, never through the current culture.");
        }
    }
}
