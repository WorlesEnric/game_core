#nullable enable
using System.Collections;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.World;
using NUnit.Framework;
using Saltmarsh.Boot;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace Saltmarsh.Tests
{
    public sealed class SaltmarshQuestHeadless
    {
        private GameBoot? boot;
        [UnitySetUp]
        public IEnumerator BootRealGame()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Boot/Boot.unity",LoadSceneMode.Single);
            yield return null;
            boot=Object.FindFirstObjectByType<GameBoot>();
            Assert.That(boot,Is.Not.Null);
            yield return SaltmarshScenario.Until(()=>boot!.Narrative!=null,"GameBoot: "+boot!.Failure);
            Assert.That(boot!.Failure,Is.Empty);
            Assert.That(boot.PresentationSeams,Is.GreaterThanOrEqualTo(8));
        }
        [UnityTest,Timeout(600000)]
        public IEnumerator W_CLEAN_01_SalvageBranchSaveRestoreAndConsequence()
        { yield return SaltmarshScenario.Play(boot!,0); }
        [UnityTest,Timeout(600000)]
        public IEnumerator W_CLEAN_01_SpareBranchSaveRestoreAndConsequence()
        { yield return SaltmarshScenario.Play(boot!,1); }
        [UnityTest,Timeout(600000)]
        public IEnumerator W_CLEAN_01_ThreeRegionLoopPreservesNpcIdentity()
        {
            var target=AuthoringIds.TargetIdFor(SaltmarshScenario.Id("Ada"));
            Assert.That(boot!.World!.Entities.TryGet(target,out var before),Is.True);
            foreach(string region in new[]{"Dunes","Lighthouse","Harbour","Dunes","Lighthouse","Harbour"})
                yield return SaltmarshScenario.Travel(boot,region);
            Assert.That(boot.World.Entities.TryGet(target,out var after),Is.True);
            Assert.That(after,Is.SameAs(before));
            Assert.That(boot.World.Streamer.ResidencyOf(SaltmarshScenario.Id("Harbour")),Is.EqualTo(RegionResidency.Resident));
            Assert.That(boot.World.Streamer.ResidencyOf(SaltmarshScenario.Id("Lighthouse")),Is.EqualTo(RegionResidency.Unloaded));
            Assert.That(boot.World.Root.PumpCounter.Violations,Is.Zero);
        }
        [UnityTearDown]
        public IEnumerator Shutdown()
        {
            if(boot!=null) Object.Destroy(boot.gameObject);
            yield return null;
            var scene=SceneManager.CreateScene("Saltmarsh test cleanup");
            SceneManager.SetActiveScene(scene);
        }
    }
}
