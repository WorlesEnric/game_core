// GameCore.Rules.Gameplay.Tests - P1.7a gameplay hardening, the pure halves:
//   * vertical motion as player slots (A6): a paid jump from the ground takes off, airborne steps lose gravity per step
//     down to the terminal speed, a grounded step rests, a jump in the air is refused; the take-off speed is sqrt(2gh);
//   * player.restoreStamina (P3.1 request): clamped to the maximum, refused when unchanged or not positive;
//   * request ids (A1/A6): command ids are positive, deterministic and step-dependent; obligation ids are negative and
//     never int.MinValue; the two ranges are disjoint;
//   * GameplayClock (A5): step x stepMs for a command-driven world, the domain clock for a fixed-step world, saturating;
//   * streaming reconciliation (A3): the matrix of committed residency x scene loaded x wanted, the failure latch and
//     the doubling backoff; the stable GP-WLD refusal codes are distinct;
//   * quest prerequisites and fail-closes-dependents (coordinator addition to A4).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Rules.Gameplay.Player;
using GameCore.Rules.Gameplay.Quest;
using GameCore.Rules.Gameplay.World;
using NUnit.Framework;

namespace GameCore.Rules.Gameplay.Tests.P1_7a
{
    public sealed class VerticalMotionTests
    {
        private static readonly PlayerTuning Tuning = PlayerTuning.Default;

        private static PlayerState Fresh() => PlayerRules.Spawned(0, 0, 0, 0, 77, Tuning);

        [Test]
        public void Defaults_GiveASixMetrePerSecondTakeOff_AndGravityPerStep()
        {
            Assert.That(Tuning.JumpSpeedMillimetresPerSecond, Is.EqualTo(6000));
            Assert.That(Tuning.GravityPerStep, Is.EqualTo(360), "18 m/s2 over a 20 ms step");
            Assert.That(PlayerRules.JumpSpeed(18000, 1000), Is.EqualTo(6000), "sqrt(2 x 18 m/s2 x 1 m) = 6 m/s");
            Assert.That(PlayerRules.JumpSpeed(9810, 500), Is.EqualTo(3132), "floor(sqrt(9810000)) = 3132");
            Assert.That(PlayerRules.JumpSpeed(0, 1000), Is.Zero);
            Assert.That(Tuning.WithVertical(18.0, 1.0).JumpSpeedMillimetresPerSecond, Is.EqualTo(6000));
            Assert.That(Tuning.WithVertical(9.81, 0.5).GravityMillimetresPerSecondSquared, Is.EqualTo(9810));
        }

        [Test]
        public void Jump_FromTheGround_TakesOff_AndLeavesTheGround()
        {
            PlayerMoveResult jumped = PlayerRules.Move(Fresh(), new PlayerMove(0, 120, 0, 0, false, true), Tuning);
            Assert.That(jumped.Jumped, Is.True);
            Assert.That(jumped.State.VerticalSpeed, Is.EqualTo(Tuning.JumpSpeedMillimetresPerSecond));
            Assert.That(jumped.State.Grounded, Is.False);
            Assert.That(jumped.State.Stamina, Is.EqualTo(1000 - Tuning.JumpCost));
        }

        [Test]
        public void Jump_InTheAir_IsRefused_AndPaysNothing()
        {
            PlayerState airborne = Fresh().WithVertical(2000, false);
            PlayerMoveResult refused = PlayerRules.Move(airborne, new PlayerMove(0, 40, 0, 0, false, true), Tuning);
            Assert.That(refused.JumpRefused, Is.True);
            Assert.That(refused.State.Stamina, Is.EqualTo(1000), "no jump cost in the air");
            Assert.That(refused.State.VerticalSpeed, Is.EqualTo(2000 - Tuning.GravityPerStep), "gravity still applies");
        }

        [Test]
        public void Airborne_LosesGravityPerStep_DownToTheTerminalSpeed_ThenLands()
        {
            VerticalMotion rising = PlayerRules.Vertical(6000, false, false, Tuning);
            Assert.That(rising.Speed, Is.EqualTo(6000 - 360));
            Assert.That(rising.Grounded, Is.False);

            VerticalMotion falling = PlayerRules.Vertical(-Tuning.TerminalSpeedMillimetresPerSecond + 100, false, false, Tuning);
            Assert.That(falling.Speed, Is.EqualTo(-Tuning.TerminalSpeedMillimetresPerSecond), "clamped to the terminal speed");

            VerticalMotion landed = PlayerRules.Vertical(-4000, true, false, Tuning);
            Assert.That(landed.Speed, Is.Zero);
            Assert.That(landed.Grounded, Is.True);

            VerticalMotion stillRising = PlayerRules.Vertical(3000, true, false, Tuning);
            Assert.That(stillRising.Grounded, Is.False, "a rising player is not grounded even when the resolver touched something");
        }

        [Test]
        public void AirborneFlag_IsAKnownFlag_AndRoundTripsThroughTheMove()
        {
            var move = PlayerMove.FromFlags(0, 0, 0, 0, PlayerRules.AirborneFlag);
            Assert.That(move.Airborne, Is.True);
            Assert.That(move.Flags & PlayerRules.AirborneFlag, Is.EqualTo(PlayerRules.AirborneFlag));
            Assert.That(PlayerRules.KnownFlags & PlayerRules.AirborneFlag, Is.EqualTo(PlayerRules.AirborneFlag));
            PlayerMoveResult step = PlayerRules.Move(Fresh(), move, Tuning);
            Assert.That(step.State.Grounded, Is.False, "the resolver found nothing under the player");
            Assert.That(step.State.VerticalSpeed, Is.EqualTo(-Tuning.GravityPerStep));
        }

        [Test]
        public void SameInputs_GiveIdenticalStates()
        {
            PlayerState a = Fresh();
            PlayerState b = Fresh();
            var moves = new[]
            {
                new PlayerMove(50, 0, 0, 0, false, false),
                new PlayerMove(50, 100, 0, 0, false, true),
                PlayerMove.FromFlags(50, 80, 0, 0, PlayerRules.AirborneFlag),
                PlayerMove.FromFlags(50, -60, 0, 0, PlayerRules.AirborneFlag),
                new PlayerMove(50, 0, 0, 0, false, false),
            };
            for (int i = 0; i < moves.Length; i++)
            {
                a = PlayerRules.Move(a, moves[i], Tuning).State;
                b = PlayerRules.Move(b, moves[i], Tuning).State;
            }

            Assert.That(a.ToString(), Is.EqualTo(b.ToString()));
        }
    }

    public sealed class RestoreStaminaTests
    {
        private static readonly PlayerTuning Tuning = PlayerTuning.Default;

        [Test]
        public void Restore_AddsAndClampsToTheMaximum()
        {
            PlayerState tired = PlayerRules.Spawned(0, 0, 0, 0, 77, Tuning).WithStamina(300, 0);
            PlayerTransition some = PlayerRules.RestoreStamina(tired, 200, Tuning);
            Assert.That(some.Accepted, Is.True);
            Assert.That(some.State.Stamina, Is.EqualTo(500));

            PlayerTransition clamped = PlayerRules.RestoreStamina(tired, 5000, Tuning);
            Assert.That(clamped.Accepted, Is.True);
            Assert.That(clamped.State.Stamina, Is.EqualTo(Tuning.StaminaMax));
        }

        [Test]
        public void Restore_IsRefusedWhenUnchanged_OrNotPositive()
        {
            PlayerState full = PlayerRules.Spawned(0, 0, 0, 0, 77, Tuning);
            PlayerTransition unchanged = PlayerRules.RestoreStamina(full, 100, Tuning);
            Assert.That(unchanged.Accepted, Is.False);
            Assert.That(unchanged.Refusal, Is.EqualTo(PlayerRefusal.StaminaUnchanged));
            Assert.That(PlayerRefusals.Code(PlayerRefusal.StaminaUnchanged), Is.EqualTo("player.stamina-unchanged"));

            PlayerTransition zero = PlayerRules.RestoreStamina(full.WithStamina(10, 0), 0, Tuning);
            Assert.That(zero.Refusal, Is.EqualTo(PlayerRefusal.InvalidAmount));
        }
    }

    public sealed class RequestIdTests
    {
        private static readonly Id128 Issuer = GameplayIssuers.Narrative("hollowmere");

        [Test]
        public void CommandIds_ArePositive_Deterministic_AndStepDependent()
        {
            int a = GameplayRequestIds.OfCommand(Issuer, 100UL, 1UL);
            Assert.That(a, Is.GreaterThan(0));
            Assert.That(GameplayRequestIds.OfCommand(Issuer, 100UL, 1UL), Is.EqualTo(a), "same inputs, same id");
            Assert.That(GameplayRequestIds.OfCommand(Issuer, 101UL, 1UL), Is.Not.EqualTo(a), "a restored world resumes at a later step");
            Assert.That(GameplayRequestIds.OfCommand(Issuer, 100UL, 2UL), Is.Not.EqualTo(a));
            Assert.That(GameplayRequestIds.OfCommand(GameplayIssuers.Host("hollowmere"), 100UL, 1UL), Is.Not.EqualTo(a));
            Assert.That(GameplayRequestIds.IsCommand(a), Is.True);
            Assert.That(GameplayRequestIds.IsObligation(a), Is.False);
        }

        [Test]
        public void CommandIds_DoNotCollideAcrossThousandsOfRequests()
        {
            var seen = new HashSet<int>();
            for (ulong step = 1; step <= 50; step++)
            {
                for (ulong serial = 1; serial <= 40; serial++)
                {
                    Assert.That(seen.Add(GameplayRequestIds.OfCommand(Issuer, step, serial)), Is.True, "step " + step + " serial " + serial);
                }
            }
        }

        [Test]
        public void ObligationIds_AreNegative_NeverMinValue_AndDistinct()
        {
            var seen = new HashSet<int>();
            for (int i = 1; i <= 200; i++)
            {
                Id128 outbox = GameplayIds.Id("narrative.obligation.hollowmere." + i + ".1");
                int id = GameplayRequestIds.OfObligation(outbox);
                Assert.That(id, Is.LessThan(0));
                Assert.That(id, Is.Not.EqualTo(int.MinValue));
                Assert.That(GameplayRequestIds.IsObligation(id), Is.True);
                Assert.That(GameplayRequestIds.IsCommand(id), Is.False);
                Assert.That(seen.Add(id), Is.True, "200 grants get 200 distinct request ids");
            }

            Assert.That(GameplayRequestIds.IsObligation(int.MinValue), Is.False, "int.MinValue is the placeholder, never an id");
            Assert.That(GameplayRequestIds.IsObligation(0), Is.False);
        }

        [Test]
        public void ClaimWithoutATap_TreatsAnObligationAsApplied_AndLeavesOtherIdsAlone()
        {
            Assert.That(GameplayObligations.Claim(null, -42), Is.EqualTo(ObligationClaim.AlreadyApplied));
            Assert.That(GameplayObligations.Claim(null, 42), Is.EqualTo(ObligationClaim.NotObligation));
            Assert.That(GameplayObligations.Claim(null, 0), Is.EqualTo(ObligationClaim.NotObligation));
        }
    }

    public sealed class GameplayClockTests
    {
        [Test]
        public void CommandDriven_IsStepTimesStepLength()
        {
            Assert.That(GameplayClock.NowMs(TemporalModel.CommandDriven, 0UL, 20, 99.0), Is.Zero, "the frozen domain clock is ignored");
            Assert.That(GameplayClock.NowMs(TemporalModel.CommandDriven, 150UL, 20, 0.0), Is.EqualTo(3000L));
            Assert.That(GameplayClock.NowMs(TemporalModel.CommandDriven, 150UL, 0, 0.0), Is.EqualTo(150L * GameplayClock.DefaultStepMilliseconds));
            Assert.That(GameplayClock.StepTimeMs(10UL, 33), Is.EqualTo(330L));
        }

        [Test]
        public void FixedStep_ReadsTheDomainClock()
        {
            Assert.That(GameplayClock.NowMs(TemporalModel.FixedStep, 999UL, 20, 1.5), Is.EqualTo(1500L));
            Assert.That(GameplayClock.NowMs(TemporalModel.FixedStep, 999UL, 20, -3.0), Is.Zero);
        }

        [Test]
        public void Saturates_AndClampsToInt32()
        {
            Assert.That(GameplayClock.StepTimeMs(ulong.MaxValue, 20), Is.EqualTo(long.MaxValue));
            Assert.That(GameplayClock.NowMs32(TemporalModel.CommandDriven, ulong.MaxValue, 20, 0.0), Is.EqualTo(int.MaxValue));
            Assert.That(GameplayClock.Clamp(-5L), Is.Zero);
            Assert.That(GameplayClock.StepsFor(41L, 20), Is.EqualTo(3L));
            Assert.That(GameplayClock.StepsFor(0L, 20), Is.Zero);
        }
    }

    public sealed class StreamingReconcileTests
    {
        [TestCase(Residency.Resident, false, true, ReconcileAction.LoadScene)]
        [TestCase(Residency.Resident, false, false, ReconcileAction.LoadScene)]
        [TestCase(Residency.Resident, true, true, ReconcileAction.None)]
        [TestCase(Residency.Unloaded, true, false, ReconcileAction.UnloadScene)]
        [TestCase(Residency.Unloaded, true, true, ReconcileAction.None)]
        [TestCase(Residency.Unloaded, false, false, ReconcileAction.None)]
        [TestCase(Residency.Loading, false, true, ReconcileAction.None)]
        [TestCase(Residency.Unloading, true, false, ReconcileAction.None)]
        public void Reconcile_Matrix(int committed, bool sceneLoaded, bool wanted, ReconcileAction expected)
        {
            Assert.That(StreamingRules.Reconcile(committed, sceneLoaded, wanted), Is.EqualTo(expected));
        }

        [Test]
        public void FailedLoads_BackOffByDoubling_AndLatchAfterThree()
        {
            Assert.That(StreamingRules.BackoffFrames(0), Is.Zero);
            Assert.That(StreamingRules.BackoffFrames(1), Is.EqualTo(StreamingRules.BaseBackoffFrames));
            Assert.That(StreamingRules.BackoffFrames(2), Is.EqualTo(StreamingRules.BaseBackoffFrames * 2));
            Assert.That(StreamingRules.BackoffFrames(20), Is.EqualTo(StreamingRules.BaseBackoffFrames << 8), "bounded");
            Assert.That(StreamingRules.IsLatched(StreamingRules.MaxLoadFailures - 1), Is.False);
            Assert.That(StreamingRules.IsLatched(StreamingRules.MaxLoadFailures), Is.True);
        }

        [Test]
        public void WorldRefusalCodes_AreStableAndDistinct()
        {
            var codes = new HashSet<string>(WorldRefusalCodes.All);
            Assert.That(codes.Count, Is.EqualTo(WorldRefusalCodes.All.Count));
            foreach (string code in WorldRefusalCodes.All)
            {
                StringAssert.StartsWith("GP-WLD-", code);
            }

            Assert.That(codes.Contains(WorldRefusalCodes.OfTravel(TravelRefusal.SameRegion)), Is.False, "travel validation codes are their own");
        }
    }

    public sealed class QuestDependencyTests
    {
        private static QuestModel Quest(int key, int[]? prerequisites = null, int[]? dependents = null) =>
            new QuestModel(
                key,
                "q" + key,
                new[] { new StageModel(0, "s0", string.Empty, Array.Empty<int>(), -1) },
                Array.Empty<ObjectiveModel>(),
                Array.Empty<RewardModel>(),
                null,
                null,
                prerequisites,
                dependents);

        [Test]
        public void Start_IsRefusedUntilEveryPrerequisiteIsCompleted()
        {
            QuestModel ferry = Quest(3, new[] { 1, 2 });
            var statuses = new Dictionary<int, int> { { 1, QuestRules.Completed }, { 2, QuestRules.Active } };
            var events = new List<QuestEvent>();
            var state = new QuestState(0);
            Assert.That(QuestRules.Start(ferry, state, k => statuses.TryGetValue(k, out int s) ? s : QuestRules.Inactive, events),
                Is.EqualTo(QuestRefusal.PrerequisitesUnmet));
            Assert.That(state.Status, Is.EqualTo(QuestRules.Inactive));
            Assert.That(events, Is.Empty);
            Assert.That(QuestRules.PrerequisitesMet(ferry, k => statuses[k], out int unmet), Is.False);
            Assert.That(unmet, Is.EqualTo(2));

            statuses[2] = QuestRules.Completed;
            Assert.That(QuestRules.Start(ferry, state, k => statuses[k], events), Is.EqualTo(QuestRefusal.None));
            Assert.That(state.Status, Is.Not.EqualTo(QuestRules.Inactive), "started (a stage without objectives completes at once)");
            Assert.That(events[0].Kind, Is.EqualTo(QuestEventKind.Started));
        }

        [Test]
        public void Start_WithoutPrerequisites_BehavesAsBefore()
        {
            var state = new QuestState(0);
            Assert.That(QuestRules.Start(Quest(1), state, k => QuestRules.Inactive, new List<QuestEvent>()), Is.EqualTo(QuestRefusal.None));
            Assert.That(QuestRules.Start(Quest(1), state, k => QuestRules.Inactive, new List<QuestEvent>()), Is.EqualTo(QuestRefusal.AlreadyStarted));
        }

        [Test]
        public void Fail_ClosesDeclaredDependents_AndQuestsThatNeedIt()
        {
            QuestModel bell = Quest(1, null, new[] { 7 });
            var quests = new List<QuestModel> { bell, Quest(2, new[] { 1 }), Quest(3), Quest(7), Quest(9, new[] { 3, 1 }) };
            Assert.That(QuestRules.DependentsOf(bell, quests), Is.EqualTo(new[] { 2, 7, 9 }));

            var active = new QuestState(0) { Status = QuestRules.Active, Stage = 0 };
            var events = new List<QuestEvent>();
            Assert.That(QuestRules.CloseDependent(active, events), Is.True);
            Assert.That(active.Status, Is.EqualTo(QuestRules.Failed));
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0].Kind, Is.EqualTo(QuestEventKind.Failed));

            var done = new QuestState(0) { Status = QuestRules.Completed };
            Assert.That(QuestRules.CloseDependent(done, events), Is.False, "a completed dependent stays completed");
        }
    }
}
