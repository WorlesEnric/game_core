#nullable enable
using NUnit.Framework;

namespace GameCore.Rules.Narrative.Tests
{
    /// <summary>
    /// Gate evaluation (07 s3.1/3.3, P-032, REF-N05): the decision is a pure function of the ledger's fact value
    /// and version, a gate whose evaluated version lags the ledger is observably stale, and the registered
    /// `RebindGate` function is the same evaluation run over a fenced copy.
    /// </summary>
    [TestFixture]
    public sealed class GateRuleTests
    {
        [TestCase(NarrativeFacts.False, NarrativeGateRules.Closed)]
        [TestCase(NarrativeFacts.True, NarrativeGateRules.Open)]
        [TestCase(2, NarrativeGateRules.Closed)]
        [TestCase(-1, NarrativeGateRules.Closed)]
        public void TheDecisionFollowsTheFactValue(int conditionFactValue, int expected)
        {
            Assert.That(NarrativeGateRules.Evaluate(conditionFactValue), Is.EqualTo(expected));
        }

        [Test]
        public void TheDeclaredDecisionValuesAreTheDocumentedOnes()
        {
            Assert.That(NarrativeGateRules.Closed, Is.EqualTo(0));
            Assert.That(NarrativeGateRules.Open, Is.EqualTo(1));
            Assert.That(NarrativeGateRules.IsDecision(NarrativeGateRules.Closed), Is.True);
            Assert.That(NarrativeGateRules.IsDecision(NarrativeGateRules.Open), Is.True);
            Assert.That(NarrativeGateRules.IsDecision(2), Is.False);
            Assert.That(NarrativeGateRules.IsDecision(-1), Is.False);
        }

        [Test]
        public void AnAcceptedEvaluationReportsTheDecisionAndTheFactVersionItRead()
        {
            Assert.That(
                NarrativeGateRules.TryEvaluate(
                    NarrativeFacts.True,
                    NarrativeFacts.InitialVersion + 1,
                    out int openDecision,
                    out int openVersion,
                    out string openRefusal),
                Is.True);
            Assert.That(openDecision, Is.EqualTo(NarrativeGateRules.Open));
            Assert.That(openVersion, Is.EqualTo(NarrativeFacts.InitialVersion + 1));
            Assert.That(openRefusal, Is.EqualTo(NarrativeRefusals.None));

            Assert.That(
                NarrativeGateRules.TryEvaluate(
                    NarrativeFacts.False,
                    NarrativeFacts.InitialVersion,
                    out int closedDecision,
                    out int closedVersion,
                    out string closedRefusal),
                Is.True);
            Assert.That(closedDecision, Is.EqualTo(NarrativeGateRules.Closed));
            Assert.That(closedVersion, Is.EqualTo(NarrativeFacts.InitialVersion));
            Assert.That(closedRefusal, Is.EqualTo(NarrativeRefusals.None));

            Assert.That(
                NarrativeGateRules.TryEvaluate(NarrativeFacts.True, 5, out int gateWithoutCode, out int versionWithoutCode),
                Is.True);
            Assert.That(gateWithoutCode, Is.EqualTo(openDecision));
            Assert.That(versionWithoutCode, Is.EqualTo(5));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void AVersionBelowTheInitialVersionIsRefusedAndReportsNoVersion(int factVersion)
        {
            bool accepted = NarrativeGateRules.TryEvaluate(
                NarrativeFacts.True,
                factVersion,
                out int decision,
                out int evaluatedFactVersion,
                out string refusalCode);

            Assert.That(accepted, Is.False);
            Assert.That(refusalCode, Is.EqualTo(NarrativeRefusals.FactVersionBelowInitial));
            Assert.That(decision, Is.EqualTo(NarrativeGateRules.Closed), "a refused evaluation opens nothing.");
            Assert.That(evaluatedFactVersion, Is.EqualTo(0));
        }

        [TestCase(-1)]
        [TestCase(2)]
        [TestCase(int.MaxValue)]
        public void AnOutOfDomainFactValueIsRefusedAndOpensNothing(int conditionFactValue)
        {
            bool accepted = NarrativeGateRules.TryEvaluate(
                conditionFactValue,
                NarrativeFacts.InitialVersion,
                out int decision,
                out int evaluatedFactVersion,
                out string refusalCode);

            Assert.That(accepted, Is.False);
            Assert.That(refusalCode, Is.EqualTo(NarrativeRefusals.FactValueOutOfDomain));
            Assert.That(decision, Is.EqualTo(NarrativeGateRules.Closed));
            Assert.That(evaluatedFactVersion, Is.EqualTo(0));
        }

        [Test]
        public void RebindOnAClosedGateOpensItAtTheFactsVersion()
        {
            bool accepted = NarrativeGateRules.TryRebind(
                NarrativeGateRules.Closed,
                NarrativeFacts.True,
                NarrativeFacts.InitialVersion + 1,
                out int decision,
                out int evaluatedFactVersion,
                out string refusalCode);

            Assert.That(accepted, Is.True);
            Assert.That(decision, Is.EqualTo(NarrativeGateRules.Open));
            Assert.That(evaluatedFactVersion, Is.EqualTo(NarrativeFacts.InitialVersion + 1));
            Assert.That(refusalCode, Is.EqualTo(NarrativeRefusals.None));
        }

        [Test]
        public void RebindClosesAnOpenGateWhenTheFactNoLongerHolds()
        {
            bool accepted = NarrativeGateRules.TryRebind(
                NarrativeGateRules.Open,
                NarrativeFacts.False,
                NarrativeFacts.InitialVersion,
                out int decision,
                out int evaluatedFactVersion,
                out string refusalCode);

            Assert.That(accepted, Is.True);
            Assert.That(decision, Is.EqualTo(NarrativeGateRules.Closed));
            Assert.That(evaluatedFactVersion, Is.EqualTo(NarrativeFacts.InitialVersion));
            Assert.That(refusalCode, Is.EqualTo(NarrativeRefusals.None));
        }

        [TestCase(2)]
        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        public void RebindRefusesAnOutOfDomainCurrentDecisionAndKeepsIt(int currentDecision)
        {
            bool accepted = NarrativeGateRules.TryRebind(
                currentDecision,
                NarrativeFacts.True,
                NarrativeFacts.InitialVersion,
                out int decision,
                out int evaluatedFactVersion,
                out string refusalCode);

            Assert.That(accepted, Is.False);
            Assert.That(refusalCode, Is.EqualTo(NarrativeRefusals.GateDecisionOutOfDomain));
            Assert.That(decision, Is.EqualTo(currentDecision), "a refused rebind reports the fenced value it was given.");
            Assert.That(evaluatedFactVersion, Is.EqualTo(0));
        }

        [Test]
        public void RebindRunsTheSameEvaluationAsTheLiveGateOwner()
        {
            int[] factValues = { NarrativeFacts.False, NarrativeFacts.True };
            for (int i = 0; i < factValues.Length; i++)
            {
                for (int version = NarrativeFacts.InitialVersion; version < NarrativeFacts.InitialVersion + 3; version++)
                {
                    bool evaluationAccepted = NarrativeGateRules.TryEvaluate(
                        factValues[i],
                        version,
                        out int evaluated,
                        out int evaluatedVersion,
                        out string evaluationRefusal);
                    bool rebindAccepted = NarrativeGateRules.TryRebind(
                        NarrativeGateRules.Closed,
                        factValues[i],
                        version,
                        out int rebound,
                        out int reboundVersion,
                        out string rebindRefusal);

                    Assert.That(rebindAccepted, Is.EqualTo(evaluationAccepted));
                    Assert.That(rebound, Is.EqualTo(evaluated));
                    Assert.That(reboundVersion, Is.EqualTo(evaluatedVersion));
                    Assert.That(rebindRefusal, Is.EqualTo(evaluationRefusal));
                }
            }
        }

        [TestCase(NarrativeGateRules.Open, 3, "open@v3")]
        [TestCase(NarrativeGateRules.Closed, 1, "closed@v1")]
        [TestCase(2, 0, "closed@v0")]
        public void TheCanonicalTextFormNamesTheDecisionAndItsVersion(int decision, int evaluatedFactVersion, string expected)
        {
            Assert.That(NarrativeGateRules.Describe(decision, evaluatedFactVersion), Is.EqualTo(expected));
        }
    }
}
