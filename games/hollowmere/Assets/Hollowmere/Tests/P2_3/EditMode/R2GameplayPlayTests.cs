#nullable enable
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GameCore.Studio.Views.Hollowmere.Tests
{
    public sealed class R2GameplayPlayTests
    {
        private const string Boot = "Assets/Hollowmere/Boot/Boot.unity";

        [UnityTest]
        public IEnumerator R2_32_RealPlayDialogueTravelAndDestroyedOwner()
        {
            EditorSceneManager.OpenScene(Boot, OpenSceneMode.Single);
            yield return new EnterPlayMode();
            ReflectionGameplayBridge bridge = new ReflectionGameplayBridge();
            for (int i = 0; i < 300 && !bridge.HasNarrative; i++) yield return null;
            Assert.That(bridge.HasNarrative, Is.True, bridge.Describe);
            GameplayCommandResult refused = bridge.StartDialogue("missing-r2-graph", string.Empty);
            Assert.That(refused.Status, Is.EqualTo(GameplayCommandStatus.Refused), refused.Detail);
            GameplayCommandResult dialogue = bridge.StartDialogue("dialogue.maren", string.Empty);
            Assert.That(dialogue.Status, Is.EqualTo(GameplayCommandStatus.Submitted), dialogue.Detail);
            GameplayCommandResult travel = bridge.Travel("7f21b99a-8e74-412f-a1da-5f7d60843080");
            Assert.That(travel.Status, Is.EqualTo(GameplayCommandStatus.Submitted), travel.Detail);
            // A fresh boot replaces the MonoBehaviour owner and its command issuer.
            EditorSceneManager.LoadSceneInPlayMode(Boot, new LoadSceneParameters(LoadSceneMode.Single));
            yield return new WaitForSecondsRealtime(1.1f);
            for (int i = 0; i < 300 && !bridge.HasNarrative; i++) yield return null;
            Assert.That(bridge.HasNarrative, Is.True, bridge.Describe);
            Assert.That(bridge.StartDialogue("dialogue.maren", string.Empty).Ok, Is.True, bridge.Describe);
            yield return new ExitPlayMode();
            Assert.That(new ReflectionGameplayBridge().Travel("region").Status, Is.EqualTo(GameplayCommandStatus.NotAvailable));
        }

        [UnityTearDown]
        public IEnumerator ClosePlayAndScene()
        {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }
}
