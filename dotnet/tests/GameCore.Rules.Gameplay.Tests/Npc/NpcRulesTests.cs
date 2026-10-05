// GameCore.Rules.Gameplay.Tests - NPC rules (P1.3): state machine, patrol stepping, approach, conversation hold,
// residency stride and schedule phases.
#nullable enable
using System.Collections.Generic;
using GameCore.Rules.Gameplay.Npc;
using NUnit.Framework;

namespace GameCore.Rules.Gameplay.Tests.Npc
{
    public sealed class NpcRulesTests
    {
        private static readonly NpcProfile Profile = new NpcProfile(1000, 100, 400, 0);

        private static readonly IReadOnlyList<PatrolPoint> Square = new[]
        {
            new PatrolPoint(1000, 0),
            new PatrolPoint(1000, 1000),
            new PatrolPoint(0, 1000),
        };

        private static NpcSnapshot Patroller() => NpcRules.Placed(0, 0, 0, NpcStateCode.Patrol, Square, 0);

        [Test]
        public void Placed_PatrolAimsAtTheFirstPoint_AndWithoutARouteIdles()
        {
            NpcSnapshot npc = Patroller();
            Assert.That(npc.StateCode, Is.EqualTo(NpcStateCode.Patrol));
            Assert.That(npc.TargetX, Is.EqualTo(1000));
            Assert.That(npc.TargetZ, Is.EqualTo(0));
            NpcSnapshot lost = NpcRules.Placed(0, 0, 0, NpcStateCode.Patrol, new PatrolPoint[0], 0);
            Assert.That(lost.StateCode, Is.EqualTo(NpcStateCode.Idle));
        }

        [Test]
        public void Patrol_StepsAtSpeed_ArrivesExactly_WaitsAndAdvances()
        {
            NpcSnapshot npc = Patroller();
            NpcTransition step = NpcRules.Step(npc, Profile, Square, 500);
            Assert.That(step.State.PosX, Is.EqualTo(500));
            Assert.That(step.Arrived, Is.False);
            Assert.That(step.State.Yaw, Is.EqualTo(1571), "facing +X");

            step = NpcRules.Step(step.State, Profile, Square, 600);
            Assert.That(step.Arrived, Is.True);
            Assert.That(step.ArrivedIndex, Is.EqualTo(0));
            Assert.That(step.State.PosX, Is.EqualTo(1000));
            Assert.That(step.State.TimerMilliseconds, Is.EqualTo(100));

            step = NpcRules.Step(step.State, Profile, Square, 100);
            Assert.That(step.State.PatrolIndex, Is.EqualTo(1));
            Assert.That(step.State.TargetZ, Is.EqualTo(1000));
        }

        [Test]
        public void Patrol_LoopsBackToTheFirstPoint()
        {
            NpcSnapshot npc = Patroller();
            var arrivals = new List<int>();
            for (int i = 0; i < 200; i++)
            {
                NpcTransition step = NpcRules.Step(npc, Profile, Square, 100);
                if (step.Arrived)
                {
                    arrivals.Add(step.ArrivedIndex);
                }

                npc = step.State;
            }

            Assert.That(arrivals.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(arrivals.GetRange(0, 4), Is.EqualTo(new[] { 0, 1, 2, 0 }));
        }

        [Test]
        public void SetBehaviour_SwitchesIdleAndPatrol_RefusesUnknownAndRouteless()
        {
            NpcSnapshot idle = NpcRules.Placed(0, 0, 0, NpcStateCode.Idle, Square, 0);
            NpcTransition patrol = NpcRules.SetBehaviour(idle, (int)NpcStateCode.Patrol, Square);
            Assert.That(patrol.Accepted, Is.True);
            Assert.That(patrol.StateChanged, Is.True);
            Assert.That(patrol.State.StateCode, Is.EqualTo(NpcStateCode.Patrol));
            Assert.That(NpcRules.SetBehaviour(idle, (int)NpcStateCode.Converse, Square).Refusal, Is.EqualTo(NpcRefusal.UnknownBehaviour));
            Assert.That(NpcRules.SetBehaviour(idle, (int)NpcStateCode.Patrol, new PatrolPoint[0]).Refusal, Is.EqualTo(NpcRefusal.NoPatrolRoute));
            Assert.That(NpcRules.SetBehaviour(idle, (int)NpcStateCode.Idle, Square).Refusal, Is.EqualTo(NpcRefusal.Unchanged));
        }

        [Test]
        public void GoTo_ApproachesThenArrives_AndResumesTheStandingBehaviour()
        {
            NpcSnapshot idle = NpcRules.Placed(0, 0, 0, NpcStateCode.Idle, Square, 0);
            NpcTransition go = NpcRules.GoTo(idle, 0, 300);
            Assert.That(go.State.StateCode, Is.EqualTo(NpcStateCode.Approach));
            NpcTransition step = NpcRules.Step(go.State, Profile, Square, 200);
            Assert.That(step.Arrived, Is.False);
            step = NpcRules.Step(step.State, Profile, Square, 200);
            Assert.That(step.Arrived, Is.True);
            Assert.That(step.ArrivedIndex, Is.EqualTo(-1));
            Assert.That(step.State.StateCode, Is.EqualTo(NpcStateCode.Idle));
            Assert.That(step.State.PosZ, Is.EqualTo(300));
        }

        [Test]
        public void Converse_HoldsThenResumesPatrol_AndRefusesGoTo()
        {
            NpcSnapshot npc = NpcRules.Step(Patroller(), Profile, Square, 300).State;
            NpcTransition talk = NpcRules.Converse(npc, 400);
            Assert.That(talk.StateChanged, Is.True);
            Assert.That(NpcRules.GoTo(talk.State, 5, 5).Refusal, Is.EqualTo(NpcRefusal.Conversing));
            NpcTransition held = NpcRules.Step(talk.State, Profile, Square, 300);
            Assert.That(held.State.StateCode, Is.EqualTo(NpcStateCode.Converse));
            Assert.That(held.State.PosX, Is.EqualTo(300), "a conversing NPC does not move");
            NpcTransition resumed = NpcRules.Step(held.State, Profile, Square, 200);
            Assert.That(resumed.StateChanged, Is.True);
            Assert.That(resumed.State.StateCode, Is.EqualTo(NpcStateCode.Patrol));
            Assert.That(resumed.State.TargetX, Is.EqualTo(1000));
        }

        [Test]
        public void SetBehaviour_DuringAConversation_ChangesWhatItResumesTo()
        {
            NpcSnapshot talking = NpcRules.Converse(Patroller(), 400).State;
            NpcTransition changed = NpcRules.SetBehaviour(talking, (int)NpcStateCode.Idle, Square);
            Assert.That(changed.State.StateCode, Is.EqualTo(NpcStateCode.Converse));
            NpcTransition ended = NpcRules.EndConverse(changed.State, Square);
            Assert.That(ended.State.StateCode, Is.EqualTo(NpcStateCode.Idle));
        }

        [Test]
        public void Face_TurnsTowardAPoint_WithoutMoving()
        {
            NpcSnapshot idle = NpcRules.Placed(0, 0, 0, NpcStateCode.Idle, Square, 0);
            NpcTransition face = NpcRules.Face(idle, -1000, 0);
            Assert.That(face.State.Yaw, Is.EqualTo(4712));
            Assert.That(face.State.PosX, Is.EqualTo(0));
            Assert.That(NpcRules.Face(idle, 0, 0).Refusal, Is.EqualTo(NpcRefusal.Unchanged));
        }

        [Test]
        public void SetMood_IsBounded()
        {
            NpcSnapshot idle = NpcRules.Placed(0, 0, 0, NpcStateCode.Idle, Square, 0);
            Assert.That(NpcRules.SetMood(idle, 40).State.Mood, Is.EqualTo(40));
            Assert.That(NpcRules.SetMood(idle, 101).Refusal, Is.EqualTo(NpcRefusal.MoodOutOfRange));
            Assert.That(NpcRules.SetMood(idle, 0).Refusal, Is.EqualTo(NpcRefusal.Unchanged));
            Assert.That(NpcRefusals.Code(NpcRefusal.Conversing), Is.EqualTo("npc.conversing"));
        }

        [Test]
        public void UnloadedStride_KeepsTheAverageSpeed()
        {
            NpcSnapshot resident = Patroller();
            NpcSnapshot unloaded = Patroller();
            for (ulong step = 1; step <= 40; step++)
            {
                resident = NpcRules.Step(resident, Profile, Square, NpcRules.ElapsedOf(20, true, 8)).State;
                if (NpcRules.ShouldUpdate(step, false, 8))
                {
                    unloaded = NpcRules.Step(unloaded, Profile, Square, NpcRules.ElapsedOf(20, false, 8)).State;
                }
            }

            Assert.That(unloaded.PosX, Is.EqualTo(resident.PosX), "40 steps of 20 ms = 5 updates of 160 ms");
            Assert.That(NpcRules.ShouldUpdate(3, true, 8), Is.True);
            Assert.That(NpcRules.ShouldUpdate(3, false, 8), Is.False);
        }

        [Test]
        public void Schedule_ResolvesPhasesByTimeOfDay_WrappingOverMidnight()
        {
            var phases = new[]
            {
                new SchedulePhase(6000, (int)NpcStateCode.Patrol, false, 0, 0),
                new SchedulePhase(18000, (int)NpcStateCode.Idle, true, 50, 60),
            };
            Assert.That(ScheduleRules.IsWellFormed(24000, phases), Is.True);
            Assert.That(ScheduleRules.PhaseAt(7000, 24000, phases), Is.EqualTo(0));
            Assert.That(ScheduleRules.PhaseAt(19000, 24000, phases), Is.EqualTo(1));
            Assert.That(ScheduleRules.PhaseAt(2000, 24000, phases), Is.EqualTo(1), "before the first start: last phase");
            Assert.That(ScheduleRules.PhaseAt(24000 + 7000, 24000, phases), Is.EqualTo(0));
            Assert.That(ScheduleRules.WorldTime(100, 20, 6000), Is.EqualTo(8000));
            Assert.That(ScheduleRules.PhaseAt(5, 1000, new SchedulePhase[0]), Is.EqualTo(-1));
            Assert.That(ScheduleRules.IsWellFormed(24000, new[] { phases[1], phases[0] }), Is.False);
        }

        [Test]
        public void EnterPhase_SetsTheBehaviour_AndWalksToThePhaseLocation()
        {
            var night = new SchedulePhase(18000, (int)NpcStateCode.Idle, true, 50, 60);
            NpcTransition entered = NpcRules.EnterPhase(Patroller(), 1, night, Square);
            Assert.That(entered.State.SchedulePhase, Is.EqualTo(1));
            Assert.That(entered.State.Behaviour, Is.EqualTo((int)NpcStateCode.Idle));
            Assert.That(entered.State.StateCode, Is.EqualTo(NpcStateCode.Approach));
            Assert.That(entered.State.TargetX, Is.EqualTo(50));
            Assert.That(NpcRules.EnterPhase(entered.State, 1, night, Square).Refusal, Is.EqualTo(NpcRefusal.Unchanged));

            var day = new SchedulePhase(6000, (int)NpcStateCode.Patrol, false, 0, 0);
            NpcTransition morning = NpcRules.EnterPhase(entered.State, 0, day, Square);
            Assert.That(morning.State.StateCode, Is.EqualTo(NpcStateCode.Patrol));
        }

        [Test]
        public void SameSteps_GiveTheSameState()
        {
            NpcSnapshot a = Patroller();
            NpcSnapshot b = Patroller();
            for (int i = 0; i < 500; i++)
            {
                a = NpcRules.Step(a, Profile, Square, 20 + (i % 3)).State;
            }

            for (int i = 0; i < 500; i++)
            {
                b = NpcRules.Step(b, Profile, Square, 20 + (i % 3)).State;
            }

            Assert.That(b.ToString(), Is.EqualTo(a.ToString()));
        }
    }
}
