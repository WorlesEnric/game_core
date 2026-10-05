// GameCore.Rules.Gameplay.Tests - volume curve and mix math, music states, ambience zones and the crossfade schedule
// (P1.5, catalog row 11).
#nullable enable
using System;
using GameCore.Rules.Gameplay.Audio;
using NUnit.Framework;

namespace GameCore.Rules.Gameplay.Tests.Audio
{
    public sealed class AudioRulesTests
    {
        private const int Explore = 1001;
        private const int Tense = 1002;
        private const int Ending = 1003;
        private const int Village = 11;
        private const int Marsh = 22;
        private const int Belfry = 33;

        [TestCase(0, 0f)]
        [TestCase(500, 0.25f)]
        [TestCase(1000, 1f)]
        [TestCase(-20, 0f)]
        [TestCase(2000, 1f)]
        public void Volume_LinearGainFollowsThePerceptualCurve(int permille, float expected)
        {
            Assert.That(VolumeRules.ToLinear(permille), Is.EqualTo(expected).Within(1e-6));
        }

        [Test]
        public void Volume_DecibelsAreZeroAtFull_FloorAtSilence_AndMonotonic()
        {
            Assert.That(VolumeRules.ToDecibels(1000), Is.EqualTo(0f).Within(1e-4));
            Assert.That(VolumeRules.ToDecibels(0), Is.EqualTo(VolumeRules.SilenceDecibels));
            Assert.That(VolumeRules.ToDecibels(500), Is.EqualTo(-12.0412f).Within(1e-3), "half the slider is a quarter of the gain");
            float previous = float.NegativeInfinity;
            for (int p = 0; p <= 1000; p += 10)
            {
                float db = VolumeRules.ToDecibels(p);
                Assert.That(db, Is.GreaterThanOrEqualTo(previous), "monotonic at " + p);
                Assert.That(db, Is.InRange(VolumeRules.SilenceDecibels, 0f));
                previous = db;
            }

            Assert.That(VolumeRules.LinearToDecibels(float.NaN), Is.EqualTo(VolumeRules.SilenceDecibels));
            Assert.That(VolumeRules.LinearToDecibels(4f), Is.EqualTo(0f), "gains above unity are clamped");
        }

        [Test]
        public void Volume_MasterScalesEveryChannel_AndFromLinearInvertsTheCurve()
        {
            Assert.That(VolumeRules.Effective(1000, 1000), Is.EqualTo(1f).Within(1e-6));
            Assert.That(VolumeRules.Effective(500, 1000), Is.EqualTo(0.25f).Within(1e-6));
            Assert.That(VolumeRules.Effective(500, 500), Is.EqualTo(0.0625f).Within(1e-6));
            Assert.That(VolumeRules.Effective(0, 1000), Is.EqualTo(0f));
            for (int p = 0; p <= 1000; p += 50)
            {
                Assert.That(VolumeRules.FromLinear(VolumeRules.ToLinear(p)), Is.EqualTo(p), "round trip at " + p);
            }

            Assert.That(VolumeRules.FromLinear(-1f), Is.EqualTo(0));
        }

        [TestCase(0, 800, 600, AudioRefusal.None)]
        [TestCase(3, 0, 600, AudioRefusal.None)]
        [TestCase(1, 600, 600, AudioRefusal.Unchanged)]
        [TestCase(4, 600, 0, AudioRefusal.UnknownChannel)]
        [TestCase(-1, 600, 0, AudioRefusal.UnknownChannel)]
        [TestCase(2, 1001, 0, AudioRefusal.OutOfRange)]
        [TestCase(2, -1, 0, AudioRefusal.OutOfRange)]
        public void Volume_SetVolumeIsValidated(int channel, int permille, int current, AudioRefusal expected)
        {
            Assert.That(VolumeRules.Check(channel, permille, current), Is.EqualTo(expected));
        }

        [Test]
        public void MusicState_AcceptsKnownStatesAndSilence_RefusesUnknownAndUnchanged()
        {
            int[] known = { Explore, Tense, Ending };
            Assert.That(MusicStateRules.Check(MusicStateRules.Silence, Explore, known), Is.EqualTo(AudioRefusal.None));
            Assert.That(MusicStateRules.Check(Explore, Tense, known), Is.EqualTo(AudioRefusal.None));
            Assert.That(MusicStateRules.Check(Tense, MusicStateRules.Silence, known), Is.EqualTo(AudioRefusal.None));
            Assert.That(MusicStateRules.Check(Tense, Tense, known), Is.EqualTo(AudioRefusal.Unchanged));
            Assert.That(MusicStateRules.Check(Tense, 4242, known), Is.EqualTo(AudioRefusal.UnknownState));
            Assert.That(MusicStateRules.Check(Tense, 4242, Array.Empty<int>()), Is.EqualTo(AudioRefusal.UnknownState));
        }

        [Test]
        public void MusicState_StingerPlaysOnlyWhenAskedAndNotIntoSilence()
        {
            Assert.That(MusicStateRules.PlaysStinger(Ending, 77), Is.True);
            Assert.That(MusicStateRules.PlaysStinger(Ending, 0), Is.False);
            Assert.That(MusicStateRules.PlaysStinger(MusicStateRules.Silence, 77), Is.False);
        }

        [Test]
        public void Ambience_ZoneChangesFollowRegionsWithAnAmbience()
        {
            int[] zones = { Village, Marsh, Belfry };
            Assert.That(AmbienceRules.Check(Village, Marsh, zones), Is.EqualTo(AudioRefusal.None));
            Assert.That(AmbienceRules.Check(Marsh, Marsh, zones), Is.EqualTo(AudioRefusal.Unchanged));
            Assert.That(AmbienceRules.Check(Marsh, 99, zones), Is.EqualTo(AudioRefusal.UnknownZone));
            Assert.That(AmbienceRules.Check(Marsh, AmbienceRules.NoZone, zones), Is.EqualTo(AudioRefusal.None));
            Assert.That(AmbienceRules.ZoneForRegion(Village, Belfry, zones), Is.EqualTo(Belfry));
            Assert.That(AmbienceRules.ZoneForRegion(Village, 99, zones), Is.EqualTo(Village), "a region without ambience keeps the current zone");
        }

        [Test]
        public void Crossfade_IsEqualPower_AndEndsOnTheIncomingLoop()
        {
            var fade = new CrossfadeSchedule(Village, Marsh, 1000, 2000);
            CrossfadeGains start = fade.GainsAt(1000);
            Assert.That(start.Outgoing, Is.EqualTo(1f).Within(1e-6));
            Assert.That(start.Incoming, Is.EqualTo(0f).Within(1e-6));
            CrossfadeGains middle = fade.GainsAt(2000);
            Assert.That(middle.Outgoing, Is.EqualTo(middle.Incoming).Within(1e-5));
            for (long t = 1000; t <= 3000; t += 100)
            {
                CrossfadeGains g = fade.GainsAt(t);
                Assert.That(g.Outgoing * g.Outgoing + g.Incoming * g.Incoming, Is.EqualTo(1f).Within(1e-5), "power at " + t);
            }

            CrossfadeGains end = fade.GainsAt(3000);
            Assert.That(end.Complete, Is.True);
            Assert.That(end.Outgoing, Is.EqualTo(0f));
            Assert.That(end.Incoming, Is.EqualTo(1f));
            Assert.That(fade.GainsAt(500).Outgoing, Is.EqualTo(1f).Within(1e-6), "before the start nothing moved");
            Assert.That(fade.Progress(2500), Is.EqualTo(0.75f).Within(1e-6));
        }

        [Test]
        public void Crossfade_ZeroDurationIsACut_AndSilenceEndsHaveNoGain()
        {
            CrossfadeGains cut = new CrossfadeSchedule(Village, Marsh, 10, 0).GainsAt(10);
            Assert.That(cut.Complete, Is.True);
            Assert.That(cut.Incoming, Is.EqualTo(1f));
            CrossfadeGains fadeIn = new CrossfadeSchedule(0, Marsh, 0, 1000).GainsAt(500);
            Assert.That(fadeIn.Outgoing, Is.EqualTo(0f));
            CrossfadeGains fadeOut = new CrossfadeSchedule(Marsh, 0, 0, 1000).GainsAt(1000);
            Assert.That(fadeOut.Incoming, Is.EqualTo(0f));
            Assert.That(fadeOut.Outgoing, Is.EqualTo(0f));
            Assert.That(new CrossfadeSchedule(1, 2, 0, -5).DurationMs, Is.EqualTo(0));
        }

        [Test]
        public void Crossfade_RetargetStartsFromTheLouderLoop()
        {
            var fade = new CrossfadeSchedule(Village, Marsh, 0, 1000);
            CrossfadeSchedule early = fade.Retarget(Belfry, 200, 1000);
            Assert.That(early.FromKey, Is.EqualTo(Village), "at 20 % the outgoing village loop is still louder");
            CrossfadeSchedule late = fade.Retarget(Belfry, 800, 1000);
            Assert.That(late.FromKey, Is.EqualTo(Marsh));
            Assert.That(late.StartMs, Is.EqualTo(800));
            Assert.That(late.ToKey, Is.EqualTo(Belfry));
        }
    }
}
