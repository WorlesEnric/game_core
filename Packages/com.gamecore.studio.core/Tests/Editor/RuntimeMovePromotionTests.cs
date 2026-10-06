#nullable enable
using GameCore.Studio.Fixtures;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GameCore.Studio.Edit.Tests
{
    public sealed class RuntimeMovePromotionTests
    {
        private StudioTestBed _bed = null!;
        private bool _playing;
        private Gateway _gateway = null!;
        private FixtureAuthoredEntity _entity = null!;
        private readonly Vector3 _destination = new Vector3(3, 2, 1);

        [SetUp]
        public void SetUp()
        {
            _playing = false;
            _bed = new StudioTestBed(true, new EngineOptions { PlayModeProbe = () => _playing });
            _entity = _bed.SpawnEntity("Promotable", Vector3.zero);
            _bed.SaveScene();
            _bed.Runtime.Engine.RuntimeMoves.CaptureAuthoredState();
            _gateway = new Gateway();
            _bed.Runtime.Engine.RuntimeMoves.Gateway = _gateway;
            _playing = true;
        }
        [TearDown] public void TearDown() => _bed.Dispose();

        private ApplyReport Move() => _bed.Runtime.Engine.Apply(StudioRuntime.Single("runtime experiment", IntentOrigin.Manual,
            new Operation("move", RuntimeMovePromotion.ToolId, _bed.Ref(_entity).WithScope(AuthorScope.Instance),
                new JObject { ["position"] = new JArray(3, 2, 1), ["yaw"] = 0 })));

        [Test]
        public void W_EDIT_06_PromotionSurvivesRuntimeReconstructionAndHasIndependentInverse()
        {
            ApplyReport runtime = Move();
            Assert.That(runtime.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", runtime.Diagnostics));
            Assert.That(_entity.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(_bed.Runtime.History.Undo(runtime.Entry.Id).Ok, Is.False);
            Assert.That(_bed.Runtime.Engine.RuntimeMoves.TryApplyToAuthored(runtime.Entry.Id, out ChangeSet? promoted, out Diagnostic? problem), Is.True, problem?.ToString());
            Assert.That(_bed.Runtime.Engine.RuntimeMoves.TryApplyToAuthored(runtime.Entry.Id, out ChangeSet? duplicate, out _), Is.True);
            Assert.That(duplicate!.Id, Is.EqualTo(promoted!.Id));
            Assert.That(promoted.Id, Is.Not.EqualTo(runtime.Entry.Id));
            Assert.That(_bed.Runtime.Journal.Read(promoted.Id)!.EffectiveState, Is.EqualTo(ChangeSetState.Candidate));
            _playing = false;
            StudioRuntime reloaded = _bed.Reload();
            var results = reloaded.Engine.RuntimeMoves.ApplyPending();
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", results[0].Diagnostics));
            Assert.That(_entity.transform.position, Is.EqualTo(_destination));
            Assert.That(results[0].Outcome("move")!.Undo, Is.Not.Null);
            Assert.That(reloaded.Engine.RuntimeMoves.ApplyPending(), Is.Empty);
            Assert.That(reloaded.History.Undo(promoted.Id).Ok, Is.True);
            Assert.That(_entity.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(reloaded.History.Undo(runtime.Entry.Id).Ok, Is.False);
        }

        [TestCase(false, DiagnosticCodes.Conflict)]
        [TestCase(true, DiagnosticCodes.StaleTarget)]
        public void W_EDIT_06_ChangedOrDeletedAuthoredTargetRefusesWithoutOverwriting(bool delete, string code)
        {
            ApplyReport runtime = Move();
            Assert.That(_bed.Runtime.Engine.RuntimeMoves.TryApplyToAuthored(runtime.Entry.Id, out _, out _), Is.True);
            _playing = false;
            if (delete) Object.DestroyImmediate(_entity.gameObject);
            else _entity.transform.position = Vector3.one;
            _bed.SaveScene();
            var results = _bed.Runtime.Engine.RuntimeMoves.ApplyPending();
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].State, Is.Not.EqualTo(ChangeSetState.Applied));
            Assert.That(results[0].Diagnostics, Has.Some.Matches<Diagnostic>(d => d.Code == code));
            if (!delete) Assert.That(_entity.transform.position, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void W_EDIT_06_UncommittedOrUnmappedMoveAndOtherActionsCannotPromote()
        {
            ApplyReport runtime = Move();
            _gateway.Committed = false;
            Assert.That(_bed.Runtime.Engine.RuntimeMoves.TryApplyToAuthored(runtime.Entry.Id, out _, out _), Is.False);
            _gateway.Committed = true;
            _playing = false;
            _entity.transform.position = Vector3.up;
            EditorSceneManager.MarkSceneDirty(_entity.gameObject.scene);
            _bed.Runtime.Engine.RuntimeMoves.CaptureAuthoredState();
            _playing = true;
            Assert.That(_bed.Runtime.Engine.RuntimeMoves.TryApplyToAuthored(runtime.Entry.Id, out _, out _), Is.False);
            _playing = false;
            ApplyReport authored = _bed.Runtime.Engine.Apply(StudioRuntime.Single("ordinary", IntentOrigin.Manual,
                StudioTestBed.Set("set", _bed.Ref(_entity), "level", 7)));
            Assert.That(authored.State, Is.EqualTo(ChangeSetState.Applied));
            Assert.That(_bed.Runtime.Engine.RuntimeMoves.TryApplyToAuthored(authored.Entry.Id, out _, out _), Is.False);
        }

        [Test]
        public void W_EDIT_06_RuntimeMoveCannotJoinAtomicAuthoredBatch()
        {
            var move = new Operation("move", RuntimeMovePromotion.ToolId, _bed.Ref(_entity).WithScope(AuthorScope.Instance),
                new JObject { ["position"] = new JArray(3, 2, 1), ["yaw"] = 0 });
            var change = StudioTestBed.NewChangeSet("mixed", ApplyPolicy.AllOrNothing, move,
                StudioTestBed.Set("set", _bed.Ref(_entity), "level", 7));
            Assert.That(_bed.Runtime.Engine.Apply(change).State, Is.EqualTo(ChangeSetState.Rejected));
            Assert.That(_gateway.Calls, Is.Zero);
            Assert.That(_entity.level, Is.EqualTo(1));
        }

        private sealed class Gateway : IRuntimeMoveGateway
        {
            public bool Committed = true;
            public int Calls;
            public OperationResult Move(AuthoringRef target, Vector3 position, float yaw)
            {
                Calls++;
                return OperationResult.Applied().WithGameCoreOp("w:test/i:move/s:" + Calls);
            }
            public bool IsAt(AuthoringRef target, Vector3 position, float yaw, string operationId) => Committed;
        }
    }
}
