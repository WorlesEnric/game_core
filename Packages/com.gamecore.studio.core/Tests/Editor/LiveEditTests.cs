// GameCore.Studio.Edit.Tests - Play-mode live edits through the world bridge with an expected revision (SADR-011):
// Executed records the GameCore OperationId in outcome and links; a StalePlan refusal maps to Conflict
// (docs/studio/03-authoring-contracts.md s7). Play mode is simulated with the engine's probe and a fake world.
#nullable enable
using GameCore.Studio.Fixtures;
using GameCore.Studio.Model;
using NUnit.Framework;
using UnityEngine;

namespace GameCore.Studio.Edit.Tests
{
    public sealed class LiveEditTests
    {
        private StudioTestBed _bed = null!;
        private FixtureLiveTranslator _translator = null!;

        [SetUp]
        public void SetUp()
        {
            _bed = new StudioTestBed(true, new EngineOptions { PlayModeProbe = () => true });
            _translator = new FixtureLiveTranslator();
            _bed.Runtime.Services.RegisterLiveTranslator(_translator);
        }

        [TearDown]
        public void TearDown() => _bed.Dispose();

        [Test]
        public void Executed_RecordsTheOperationIdAndAdvancesTheExpectedRevision()
        {
            FixtureAuthoredEntity first = _bed.SpawnEntity("First", Vector3.zero);
            FixtureAuthoredEntity second = _bed.SpawnEntity("Second", Vector3.right);
            _bed.Live.CommittedRevision = 7;
            ChangeSet changeSet = StudioTestBed.NewChangeSet(
                "live",
                ApplyPolicy.BestEffort,
                StudioTestBed.Set("op1", _bed.Ref(first), "level", 4),
                StudioTestBed.Set("op2", _bed.Ref(second), "level", 5));

            ApplyReport report = _bed.Runtime.Engine.Apply(changeSet);

            Assert.That(report.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", report.Diagnostics));
            Assert.That(_translator.Translations, Is.EqualTo(2));
            Assert.That(_bed.Live.ExpectedRevisions, Is.EqualTo(new ulong[] { 7, 8 }), "each edit is submitted against the revision the previous one produced");
            Assert.That(report.Outcome("op1")!.GameCoreOps, Has.Count.EqualTo(1));
            StringAssert.StartsWith("w:", report.Outcome("op1")!.GameCoreOps![0]);
            Assert.That(report.Entry.Links?.GameCoreOps, Has.Count.EqualTo(2));
            Assert.That(first.level, Is.EqualTo(1), "D4: runtime actions leave authored data unchanged");
            Assert.That(second.level, Is.EqualTo(1));
        }

        [Test]
        public void StalePlan_IsConflict()
        {
            FixtureAuthoredEntity entity = _bed.SpawnEntity("Ferryman", Vector3.zero);
            _bed.Live.RefuseNextAsStale = true;
            ChangeSet changeSet = StudioTestBed.NewChangeSet("stale live", null, StudioTestBed.Set("op1", _bed.Ref(entity), "level", 9));

            ApplyReport report = _bed.Runtime.Engine.Apply(changeSet);

            Assert.That(report.State, Is.EqualTo(ChangeSetState.Failed));
            Assert.That(report.Outcome("op1")!.Status, Is.EqualTo(OutcomeStatus.Refused));
            Assert.That(report.Outcome("op1")!.Code, Is.EqualTo(DiagnosticCodes.Conflict));
            Assert.That(report.Diagnostics, Has.Some.Matches<Diagnostic>(d => d.Code == DiagnosticCodes.Conflict && (string?)d.Data?["expected"] == "revision:7"));
            Assert.That(entity.level, Is.EqualTo(1), "a refused world edit changes no authored data");
        }
    }
}
