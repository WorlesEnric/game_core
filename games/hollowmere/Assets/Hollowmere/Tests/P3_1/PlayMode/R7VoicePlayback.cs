#nullable enable
using System.Collections;
using GameCore.Gameplay.Audio;
using GameCore.Gameplay.Dialogue;
using Hollowmere.Boot;
using Hollowmere.Narrative;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hollowmere.P3_1.PlayMode.Tests
{
    public sealed partial class FullQuestHeadless
    {
        [UnityTest]
        [Explicit("Native audio acceptance: select this case in a graphics-enabled batch Editor.")]
        public IEnumerator R7C_WPLUG11_ActualMarenLinePlaysItsNativeVoiceClip()
        {
            yield return Boot();
            GameBoot boot = game.GetComponent<GameBoot>();
            Assert.That(game.Narrative!.Conversations.TryStart(HollowmereNarrative.MarenId, HollowmereNarrative.MarenGraphRef).Started, Is.True);
            yield return Until(() => Presented(boot).Active && Presented(boot).Kind == "line", "Maren's voiced line");
            AudioEngineHost host = Object.FindAnyObjectByType<AudioEngineHost>();
            Assert.That(host.HasSources, Is.True, "the native playback assertion requires graphics-enabled batch PlayMode");
            AudioSource voice = null!;
            foreach (AudioSource source in host.GetComponentsInChildren<AudioSource>())
                if (source.name == "Voice") voice = source;
            Assert.That(voice, Is.Not.Null);
            for (int i = 0; i < 120 && (voice.clip == null || !voice.isPlaying); i++) yield return null;
            Assert.That(voice.clip, Is.Not.Null, "a voice event without a playable native clip is not audible dialogue");
            Assert.That(voice.clip.name, Is.EqualTo("Maren_greet"));
            Assert.That(voice.isPlaying, Is.True);
            Assert.That(voice.volume, Is.GreaterThan(0));
            Assert.That(voice.clip.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            Debug.Log("[W-PLUG-11] real Maren dialogue plays " + voice.clip.name + " volume=" + voice.volume);
        }
    }
}
