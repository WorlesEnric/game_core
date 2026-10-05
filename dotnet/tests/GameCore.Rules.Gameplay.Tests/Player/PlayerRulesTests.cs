// GameCore.Rules.Gameplay.Tests - player rules (P1.3): integer plane math, moves, stamina, jump, focus ranking.
#nullable enable
using System.Collections.Generic;
using GameCore.Rules.Gameplay.Player;
using NUnit.Framework;

namespace GameCore.Rules.Gameplay.Tests.Player
{
    public sealed class PlanarMathTests
    {
        [Test]
        public void Isqrt_IsTheFloorOfTheSquareRoot()
        {
            Assert.That(PlanarMath.Isqrt(0), Is.EqualTo(0));
            Assert.That(PlanarMath.Isqrt(15), Is.EqualTo(3));
            Assert.That(PlanarMath.Isqrt(16), Is.EqualTo(4));
            Assert.That(PlanarMath.Isqrt(long.MaxValue), Is.EqualTo(3037000499L));
            Assert.That(PlanarMath.Distance(3000, 4000), Is.EqualTo(5000));
        }

        [TestCase(0, 1000, 0)]
        [TestCase(1000, 0, 1571)]
        [TestCase(0, -1000, 3142)]
        [TestCase(-1000, 0, 4712)]
        [TestCase(1000, 1000, 785)]
        [TestCase(-1000, -1000, 3927)]
        public void YawTowards_FollowsTheEngineConvention(int dx, int dz, int expected)
        {
            Assert.That(PlanarMath.YawTowards(dx, dz), Is.EqualTo(expected).Within(1));
        }

        [Test]
        public void YawTowards_IsWithinTwoMilliradiansOfTheExactAngleEverywhere()
        {
            for (int degrees = 0; degrees < 360; degrees += 7)
            {
                double radians = degrees * System.Math.PI / 180.0;
                int dx = (int)System.Math.Round(System.Math.Sin(radians) * 100000.0);
                int dz = (int)System.Math.Round(System.Math.Cos(radians) * 100000.0);
                int exact = PlanarMath.NormalizeYaw((int)System.Math.Round(radians * 1000.0));
                Assert.That(PlanarMath.AngleBetween(PlanarMath.YawTowards(dx, dz), exact), Is.LessThanOrEqualTo(2), "at " + degrees + " degrees");
            }
        }

        [Test]
        public void MoveToward_StepsExactlyAndArrivesOnTheTarget()
        {
            Assert.That(PlanarMath.MoveToward(0, 0, 3000, 4000, 1000, out int x, out int z), Is.False);
            Assert.That(x, Is.EqualTo(600));
            Assert.That(z, Is.EqualTo(800));
            Assert.That(PlanarMath.MoveToward(x, z, 3000, 4000, 10000, out x, out z), Is.True);
            Assert.That(x, Is.EqualTo(3000));
            Assert.That(z, Is.EqualTo(4000));
        }

        [Test]
        public void AngleBetween_TakesTheShortWayRound()
        {
            Assert.That(PlanarMath.AngleBetween(100, 6200), Is.EqualTo(183));
            Assert.That(PlanarMath.AngleBetween(0, 3142), Is.EqualTo(3141));
            Assert.That(PlanarMath.NormalizeYaw(-1), Is.EqualTo(6282));
        }
    }

    public sealed class PlayerRulesTests
    {
        private static readonly PlayerTuning Tuning = PlayerTuning.Default;

        private static PlayerState Fresh() => PlayerRules.Spawned(0, 0, -12000, 0, 77, Tuning);

        [Test]
        public void Spawned_HasFullStaminaAndNoFocus()
        {
            PlayerState state = Fresh();
            Assert.That(state.Stamina, Is.EqualTo(1000));
            Assert.That(state.Focus, Is.EqualTo(PlayerRules.NoFocus));
            Assert.That(state.RegionKey, Is.EqualTo(77));
        }

        [Test]
        public void Move_AppliesTheDisplacementAndFacing()
        {
            PlayerMoveResult result = PlayerRules.Move(Fresh(), new PlayerMove(30, 0, 40, 1571, false, false), Tuning);
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Clamped, Is.False);
            Assert.That(result.State.PosX, Is.EqualTo(30));
            Assert.That(result.State.PosZ, Is.EqualTo(-11960));
            Assert.That(result.State.Yaw, Is.EqualTo(1571));
        }

        [Test]
        public void Move_ClampsAWalkToWalkSpeedOverTheMoveWindow()
        {
            // walk 2.5 m/s over a 100 ms window = 250 mm.
            PlayerMoveResult result = PlayerRules.Move(Fresh(), new PlayerMove(3000, 0, 4000, 0, false, false), Tuning);
            Assert.That(result.Clamped, Is.True);
            Assert.That(result.State.PosX, Is.EqualTo(150));
            Assert.That(result.State.PosZ, Is.EqualTo(-12000 + 200));
        }

        [Test]
        public void Move_RunningAllowsRunSpeed_AndDrainsStamina()
        {
            PlayerMoveResult result = PlayerRules.Move(Fresh(), new PlayerMove(0, 0, 500, 0, true, false), Tuning);
            Assert.That(result.Clamped, Is.False, "run 5.5 m/s over 100 ms = 550 mm");
            Assert.That(result.Ran, Is.True);
            Assert.That(result.State.Stamina, Is.EqualTo(1000 - Tuning.DrainPerStep));
            Assert.That(result.State.RegenDelayMilliseconds, Is.EqualTo(Tuning.StaminaRegenDelayMilliseconds));
        }

        [Test]
        public void Move_WithoutStamina_RunningFallsBackToWalkSpeed()
        {
            PlayerState tired = Fresh().WithStamina(0, 0);
            PlayerMoveResult result = PlayerRules.Move(tired, new PlayerMove(0, 0, 500, 0, true, false), Tuning);
            Assert.That(result.Ran, Is.False);
            Assert.That(result.Clamped, Is.True);
            Assert.That(result.State.PosZ, Is.EqualTo(-12000 + 250));
        }

        [Test]
        public void Stamina_RegeneratesOnlyAfterTheDelay_AndNeverExceedsTheMaximum()
        {
            PlayerState state = Fresh().WithStamina(500, 40);
            PlayerMoveResult first = PlayerRules.Move(state, new PlayerMove(0, 0, 0, 0, false, false), Tuning);
            Assert.That(first.State.Stamina, Is.EqualTo(500), "the delay counts down first");
            Assert.That(first.State.RegenDelayMilliseconds, Is.EqualTo(20));
            PlayerState now = first.State;
            for (int i = 0; i < 2; i++)
            {
                now = PlayerRules.Move(now, new PlayerMove(0, 0, 0, 0, false, false), Tuning).State;
            }

            Assert.That(now.Stamina, Is.EqualTo(500 + Tuning.RegenPerStep));
            for (int i = 0; i < 1000; i++)
            {
                now = PlayerRules.Move(now, new PlayerMove(0, 0, 0, 0, false, false), Tuning).State;
            }

            Assert.That(now.Stamina, Is.EqualTo(Tuning.StaminaMax));
        }

        [Test]
        public void Jump_CostsStamina_AndIsRefusedWithoutIt()
        {
            PlayerMoveResult jumped = PlayerRules.Move(Fresh(), new PlayerMove(0, 300, 0, 0, false, true), Tuning);
            Assert.That(jumped.Jumped, Is.True);
            Assert.That(jumped.State.Stamina, Is.EqualTo(1000 - Tuning.JumpCost));
            Assert.That(jumped.State.PosY, Is.EqualTo(300));

            PlayerMoveResult refused = PlayerRules.Move(Fresh().WithStamina(10, 0), new PlayerMove(0, 300, 0, 0, false, true), Tuning);
            Assert.That(refused.JumpRefused, Is.True);
            Assert.That(refused.State.PosY, Is.EqualTo(0), "upward motion of a refused jump is dropped");
        }

        [Test]
        public void Move_ClampsVerticalMotion_AndRefusesUnknownFlags()
        {
            PlayerMoveResult falling = PlayerRules.Move(Fresh(), new PlayerMove(0, -5000, 0, 0, false, false), Tuning);
            Assert.That(falling.State.PosY, Is.EqualTo(-Tuning.MaxVerticalMillimetresPerStep));
            PlayerMoveResult forged = PlayerRules.Move(Fresh(), PlayerMove.FromFlags(10, 0, 0, 0, 8), Tuning);
            Assert.That(forged.Refusal, Is.EqualTo(PlayerRefusal.UnknownFlags));
            Assert.That(forged.State.PosX, Is.EqualTo(0));
        }

        [Test]
        public void SameMoves_FromTheSameState_GiveTheSameState()
        {
            var moves = new List<PlayerMove>();
            for (int i = 0; i < 200; i++)
            {
                moves.Add(new PlayerMove((i * 37) % 300 - 150, (i % 7) - 3, (i * 53) % 400 - 100, i * 31, i % 3 == 0, i % 50 == 0));
            }

            PlayerState a = Fresh();
            PlayerState b = Fresh();
            foreach (PlayerMove move in moves)
            {
                a = PlayerRules.Move(a, move, Tuning).State;
            }

            foreach (PlayerMove move in moves)
            {
                b = PlayerRules.Move(b, move, Tuning).State;
            }

            Assert.That(b.ToString(), Is.EqualTo(a.ToString()));
        }

        [Test]
        public void Adopt_ReplacesPoseAndRegion_KeepsStamina_ClearsFocus()
        {
            PlayerState focused = Fresh().WithStamina(321, 0).WithFocus(99);
            PlayerState adopted = PlayerRules.Adopt(focused, 5, 1, 2, 3, -10);
            Assert.That(adopted.PosX, Is.EqualTo(1));
            Assert.That(adopted.RegionKey, Is.EqualTo(5));
            Assert.That(adopted.Yaw, Is.EqualTo(6273));
            Assert.That(adopted.Stamina, Is.EqualTo(321));
            Assert.That(adopted.Focus, Is.EqualTo(PlayerRules.NoFocus));
        }

        [Test]
        public void SetFocus_AcceptsKeysAndNoFocus_RefusesInvalidAndUnchanged()
        {
            PlayerTransition set = PlayerRules.SetFocus(Fresh(), 42);
            Assert.That(set.Accepted, Is.True);
            Assert.That(set.State.Focus, Is.EqualTo(42));
            Assert.That(PlayerRules.SetFocus(set.State, 42).Refusal, Is.EqualTo(PlayerRefusal.FocusUnchanged));
            Assert.That(PlayerRules.SetFocus(set.State, 0).Refusal, Is.EqualTo(PlayerRefusal.InvalidFocus));
            Assert.That(PlayerRules.SetFocus(set.State, PlayerRules.NoFocus).State.Focus, Is.EqualTo(PlayerRules.NoFocus));
            Assert.That(PlayerRefusals.Code(PlayerRefusal.NoFocus), Is.EqualTo("player.no-focus"));
        }

        [Test]
        public void Interact_NeedsAFocus()
        {
            Assert.That(PlayerRules.Interact(Fresh()).Refusal, Is.EqualTo(PlayerRefusal.NoFocus));
            Assert.That(PlayerRules.Interact(Fresh().WithFocus(7)).Accepted, Is.True);
        }

        [Test]
        public void StaminaMilli_ScalesToThousandths()
        {
            Assert.That(PlayerRules.StaminaMilli(Fresh().WithStamina(250, 0), Tuning), Is.EqualTo(250));
            var big = new PlayerTuning(2500, 5500, 20, 100, 400, 4000, 200, 150, 800, 150);
            Assert.That(PlayerRules.StaminaMilli(Fresh().WithStamina(1000, 0), big), Is.EqualTo(250));
        }
    }

    public sealed class FocusRulesTests
    {
        private static readonly FocusTuning Tuning = FocusTuning.Default;

        [Test]
        public void Rank_PicksTheNearestCandidateInTheViewCone()
        {
            var candidates = new List<FocusCandidate>
            {
                new FocusCandidate(10, 0, 2000, 0),
                new FocusCandidate(11, 0, 1200, 0),
                new FocusCandidate(12, 0, -1000, 0),
            };
            Assert.That(FocusRules.Rank(candidates, 0, Tuning, PlayerRules.NoFocus), Is.EqualTo(11));
        }

        [Test]
        public void Rank_IgnoresCandidatesOutOfRangeOrBehind_ButNotVeryCloseOnes()
        {
            Assert.That(FocusRules.Rank(new[] { new FocusCandidate(10, 0, 3000, 0) }, 0, Tuning, PlayerRules.NoFocus), Is.EqualTo(PlayerRules.NoFocus));
            Assert.That(FocusRules.Rank(new[] { new FocusCandidate(10, 0, -1500, 0) }, 0, Tuning, PlayerRules.NoFocus), Is.EqualTo(PlayerRules.NoFocus));
            Assert.That(FocusRules.Rank(new[] { new FocusCandidate(10, 0, -500, 0) }, 0, Tuning, PlayerRules.NoFocus), Is.EqualTo(10));
        }

        [Test]
        public void Rank_HigherPriorityWins_ThenLowerKeyOnTies()
        {
            var tied = new List<FocusCandidate> { new FocusCandidate(30, 0, 1500, 0), new FocusCandidate(20, 0, 1500, 0) };
            Assert.That(FocusRules.Rank(tied, 0, Tuning, PlayerRules.NoFocus), Is.EqualTo(20));
            var priority = new List<FocusCandidate> { new FocusCandidate(30, 0, 1000, 0), new FocusCandidate(40, 0, 2000, 5) };
            Assert.That(FocusRules.Rank(priority, 0, Tuning, PlayerRules.NoFocus), Is.EqualTo(40));
        }

        [Test]
        public void Rank_KeepsTheCurrentFocusUnlessARivalIsClearlyBetter()
        {
            var close = new List<FocusCandidate> { new FocusCandidate(1, 0, 1500, 0), new FocusCandidate(2, 0, 1400, 0) };
            Assert.That(FocusRules.Rank(close, 0, Tuning, 1), Is.EqualTo(1), "a slightly better rival does not steal focus");
            var clear = new List<FocusCandidate> { new FocusCandidate(1, 0, 2000, 0), new FocusCandidate(2, 0, 900, 0) };
            Assert.That(FocusRules.Rank(clear, 0, Tuning, 1), Is.EqualTo(2));
        }

        [Test]
        public void Score_GrowsWithDistanceAndAngle()
        {
            int ahead = FocusRules.Score(new FocusCandidate(1, 0, 2000, 0), 0, Tuning);
            int aside = FocusRules.Score(new FocusCandidate(1, 1000, 1732, 0), 0, Tuning);
            Assert.That(ahead, Is.GreaterThan(0));
            Assert.That(aside, Is.GreaterThan(ahead));
            Assert.That(FocusRules.Score(new FocusCandidate(0, 0, 100, 0), 0, Tuning), Is.EqualTo(-1), "keys are positive");
        }
    }
}
