#nullable enable
using GameCore.Gameplay.World;
using Hollowmere.Boot;
using Hollowmere.Game;
using NUnit.Framework;

namespace Hollowmere.P3_1.EditMode.Tests
{
    public sealed class P31bFramePolicyTests
    {
        [Test]
        public void P31b_BOOT_FirstRegionWaitsForMenuFramesWithoutFakingResidency()
        {
            int frame = 0;
            var scenes = new ImmediateSceneLoader();
            var loader = new DeferredRegionLoader(scenes, () => frame, 3);
            ISceneOperation load = loader.Load("village");
            Assert.That(load.IsDone, Is.False);
            Assert.That(scenes.Loads, Is.Zero);
            Assert.That(loader.IsLoaded("village"), Is.False);
            frame = 3;
            Assert.That(load.IsDone, Is.True);
            Assert.That(loader.IsLoaded("village"), Is.True);
            Assert.That(load.IsDone, Is.True);
            Assert.That(scenes.Loads, Is.EqualTo(1));
            Assert.That(loader.Unload("village").IsDone, Is.True);
            Assert.That(loader.IsLoaded("village"), Is.False);
        }

        [Test]
        public void P31b_BELFRY_PreloadIsLimitedToFerryPreparation()
        {
            const string marsh = "7f21b99a-8e74-412f-a1da-5f7d60843080";
            Assert.That(HollowmereGame.ShouldPreloadBelfry(marsh, 0), Is.False);
            Assert.That(HollowmereGame.ShouldPreloadBelfry(marsh, 1), Is.True);
            Assert.That(HollowmereGame.ShouldPreloadBelfry("1c5a1ae9-bec2-4201-be5f-3ff8bf8e1d18", 1), Is.False);
        }
    }
}
