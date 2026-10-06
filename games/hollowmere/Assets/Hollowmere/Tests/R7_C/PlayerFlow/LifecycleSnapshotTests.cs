#nullable enable
using Hollowmere.Game;
using NUnit.Framework;
using UnityEngine;

namespace Hollowmere.R7_C.PlayerFlow.Tests
{
    public sealed class LifecycleSnapshotTests
    {
        // This defends the driver's restore assertion only; standalone video remains mandatory for W-GAME-05.
        [Test]
        public void RestoreAssertionRejectsFreshWorldOrMissingQuestItem()
        {
            var saved = new HollowmereLifecycleAudit.State
            {
                region = "marsh", position = new Vector3(200, 0, -3),
                quest = 1, stage = 2, lantern = 1, clapper = 1, rumour = 1, gate = 1, shrine = 1,
            };
            var restored = JsonUtility.FromJson<HollowmereLifecycleAudit.State>(JsonUtility.ToJson(saved));
            Assert.That(saved.Matches(restored), Is.True);
            Assert.That(saved.Matches(new HollowmereLifecycleAudit.State()), Is.False,
                "a successful load receipt alone must not pass with a reset world");
            restored.clapper = 0;
            Assert.That(saved.Matches(restored), Is.False,
                "the missing quest item must fail restore acceptance even when region, pose and quest agree");
        }
    }
}
