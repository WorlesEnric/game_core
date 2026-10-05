// Hollowmere P1.5 EditMode - UI and audio content, binding maps, screen flow, audio kernel, tools, preview.
#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GameCore.Contracts;
using GameCore.Gameplay.Audio;
using GameCore.Gameplay.Audio.Editor;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Ui;
using GameCore.Gameplay.Ui.Editor;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Audio;
using GameCore.Rules.Gameplay.Ui;
using Hollowmere.UiAudio;
using Hollowmere.UiAudioAuthoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Hollowmere.P1_5.EditMode.Tests
{
    [TestFixture]
    public sealed class UiAudioContentTests
    {
        [Test]
        public void AuthoringIsCleanAndIdempotent()
        {
            UiAudioAuthoringReport first = HollowmereUiAudioAuthoring.Author();
            Assert.That(first.Ok, Is.True, first.ToString());
            Assert.That(first.Clips, Is.EqualTo(HollowmereUiAudioAuthoring.AudioSpecs().Count));
            Assert.That(first.Documents, Is.EqualTo(10));
            string flowGuid = AssetDatabase.AssetPathToGUID(HollowmereUiAudioAuthoring.FlowPath);
            string bankGuid = AssetDatabase.AssetPathToGUID(HollowmereUiAudioAuthoring.BankPath);
            UiAudioAuthoringReport second = HollowmereUiAudioAuthoring.Author();
            Assert.That(second.Ok, Is.True, second.ToString());
            Assert.That(AssetDatabase.AssetPathToGUID(HollowmereUiAudioAuthoring.FlowPath), Is.EqualTo(flowGuid));
            Assert.That(AssetDatabase.AssetPathToGUID(HollowmereUiAudioAuthoring.BankPath), Is.EqualTo(bankGuid));
            TestContext.Out.WriteLine("P1.5-AUTHOR " + second);
        }

        [Test]
        public void EveryContentAssetIsBoundToItsScript()
        {
            HollowmereUiAudioContent content = UiAudioHarness.Content();
            var assets = new List<ScriptableObject> { content, content.Flow!, content.Flow!.Theme!, content.Audio!, content.Audio!.Bank! };
            for (int i = 0; i < content.Flow.Documents.Count; i++)
            {
                assets.Add(content.Flow.Documents[i]);
            }

            for (int i = 0; i < content.Audio.MusicStates.Count; i++)
            {
                assets.Add(content.Audio.MusicStates[i]);
            }

            for (int i = 0; i < content.Audio.Ambiences.Count; i++)
            {
                assets.Add(content.Audio.Ambiences[i]);
            }

            foreach (ScriptableObject asset in assets)
            {
                Assert.That(asset, Is.Not.Null);
                MonoScript? script = MonoScript.FromScriptableObject(asset);
                Assert.That(script, Is.Not.Null, asset.name + " has a script");
                Assert.That(script!.GetClass(), Is.EqualTo(asset.GetType()), asset.name);
                string path = AssetDatabase.GetAssetPath(asset);
                Assert.That(File.ReadAllText(path), Does.Not.Contain("m_Script: {fileID: 0}"), path + " references its script");
            }

            Assert.That(assets.Count, Is.EqualTo(21));
        }

        [Test]
        public void BindingMapResolvesEveryElementOfEveryDocument()
        {
            HollowmereUiAudioContent content = UiAudioHarness.Content();
            ScreenFlowDefinition flow = content.Flow!;
            var screens = new HashSet<UiScreen>();
            int bindings = 0;
            for (int i = 0; i < flow.Documents.Count; i++)
            {
                UiDocumentDefinition document = flow.Documents[i];
                Assert.That(document.Uxml, Is.Not.Null, document.name);
                VisualElement tree = document.Uxml!.Instantiate();
                UiBindingReport report = BindingHost.Check(tree, document.Bindings, new UiViewModels());
                Assert.That(report.Ok, Is.True, document.name + ": " + string.Join("; ", report.Problems));
                Assert.That(UiValidator.Validate(document), Is.Empty, document.name);
                Assert.That(document.Bindings.Count, Is.GreaterThan(0), document.name + " binds something");

                // Every element a binding names exists exactly once.
                for (int b = 0; b < document.Bindings.Count; b++)
                {
                    Assert.That(tree.Query(document.Bindings[b].Element).ToList().Count, Is.EqualTo(1), document.name + "/" + document.Bindings[b].Element);
                }

                // Every button of the document does something.
                tree.Query<Button>().ForEach(button =>
                {
                    bool bound = false;
                    for (int b = 0; b < document.Bindings.Count; b++)
                    {
                        bound |= document.Bindings[b].Element == button.name && document.Bindings[b].Property == "clicked";
                    }

                    Assert.That(bound, Is.True, document.name + ": button " + button.name + " has no command");
                });
                screens.Add(document.Screen);
                bindings += document.Bindings.Count;
            }

            foreach (UiScreen screen in new[] { UiScreen.Hud, UiScreen.Menu, UiScreen.Pause, UiScreen.Settings, UiScreen.Save, UiScreen.Load, UiScreen.Journal, UiScreen.Inventory, UiScreen.Ending })
            {
                Assert.That(screens.Contains(screen), Is.True, "a document for " + screen);
            }

            Assert.That(UiValidator.Validate(flow), Is.Empty);
            TestContext.Out.WriteLine("P1.5-BINDINGS documents=" + flow.Documents.Count + " bindings=" + bindings);
        }

        [Test]
        public void ValidatorAndToolsRefuseUnknownElementsWithStableCodes()
        {
            HollowmereUiAudioContent content = UiAudioHarness.Content();
            UiDocumentDefinition hud = content.Flow!.DocumentsOf(UiScreen.Hud)[0];
            int before = hud.Bindings.Count;
            var refused = Assert.Throws<System.ArgumentException>(() => UiTools.Bind(hud, "no-such-element", "vm:hud.RegionName"));
            Assert.That(refused!.Message, Does.StartWith(PresentationDiagnosticCodes.UiUnknownElement));
            refused = Assert.Throws<System.ArgumentException>(() => UiTools.Bind(hud, "region-name", "vm:hud.NoSuchProperty"));
            Assert.That(refused!.Message, Does.StartWith(PresentationDiagnosticCodes.UiBadSource));
            Assert.That(hud.Bindings.Count, Is.EqualTo(before), "a refused tool changes nothing");

            var broken = ScriptableObject.CreateInstance<UiDocumentDefinition>();
            broken.Configure(UiScreen.Hud, 50, hud.Uxml);
            broken.Bind("missing-element", "text", "vm:hud.RegionName", string.Empty);
            IReadOnlyList<GameplayDiagnostic> diagnostics = UiValidator.Validate(broken);
            Assert.That(diagnostics.Count, Is.EqualTo(1));
            Assert.That(diagnostics[0].Code, Is.EqualTo(PresentationDiagnosticCodes.UiUnknownElement));
            Object.DestroyImmediate(broken);
        }

        [Test]
        public void ProceduralAudioIsDeterministicLoopsSeamlesslyAndMatchesItsManifest()
        {
            UiAudioHarness.Content();
            List<ProceduralClipSpec> specs = HollowmereUiAudioAuthoring.AudioSpecs();
            string manifestPath = HollowmereUiAudioAuthoring.GeneratedFolder + "/" + HollowmereUiAudioAuthoring.AudioManifestName;
            Assert.That(File.Exists(manifestPath), Is.True);
            ProceduralAudioManifest manifest = JsonUtility.FromJson<ProceduralAudioManifest>(File.ReadAllText(manifestPath));
            Assert.That(manifest.outputs.Length, Is.EqualTo(specs.Count));
            for (int i = 0; i < specs.Count; i++)
            {
                short[] a = ProceduralAudioGenerator.Render(specs[i]);
                short[] b = ProceduralAudioGenerator.Render(specs[i]);
                Assert.That(a, Is.EqualTo(b), specs[i].id + " renders deterministically");
                byte[] wav = ProceduralAudioGenerator.Wav(a, manifest.sampleRate);
                string file = HollowmereUiAudioAuthoring.GeneratedFolder + "/" + specs[i].file;
                Assert.That(File.ReadAllBytes(file), Is.EqualTo(wav), file + " is the committed render");
                Assert.That(manifest.outputs[i].sha256, Is.EqualTo(ProceduralAudioGenerator.Sha256(wav)));
                if (specs[i].loop)
                {
                    int jump = System.Math.Abs(a[0] - a[a.Length - 1]);
                    int typical = 0;
                    for (int s = 1; s < 200; s++)
                    {
                        typical = System.Math.Max(typical, System.Math.Abs(a[s] - a[s - 1]));
                    }

                    Assert.That(jump, Is.LessThanOrEqualTo(System.Math.Max(64, typical * 2)), specs[i].id + " loops without a click");
                }

                AudioClip? clip = AssetDatabase.LoadAssetAtPath<AudioClip>(file);
                Assert.That(clip, Is.Not.Null, file + " imports as an AudioClip");
            }
        }

        [Test]
        public void AudioSetHasThreeAmbiencesThreeMusicStatesAndAMixerWithEveryParameter()
        {
            AudioSetDefinition set = UiAudioHarness.Content().Audio!;
            Assert.That(set.Ambiences.Count, Is.EqualTo(3));
            Assert.That(set.MusicStates.Count, Is.EqualTo(3));
            Assert.That(set.StartMusicState, Is.EqualTo(HollowmereUiAudioAuthoring.MusicExplore));
            Assert.That(set.FindState(HollowmereUiAudioAuthoring.MusicTense)!.StingerId, Is.EqualTo(HollowmereUiAudioAuthoring.Bell));
            Assert.That(set.Bank!.Mixer, Is.Not.Null, "the bank plays through the Hollowmere mixer");
            Assert.That(AudioValidator.ValidateMixer(set.Bank.Mixer!, "mixer"), Is.Empty);
            Assert.That(AudioValidator.Validate(set), Is.Empty);
            Assert.That(set.Bank.Mixer!.FindMatchingGroups("Master").Length, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void GenerateToolsAnswerNotConfiguredAndChangeNothing()
        {
            AudioBankDefinition bank = UiAudioHarness.Content().Audio!.Bank!;
            int before = bank.Entries.Count;
            Assert.That(MediaGateways.Resolve(), Is.InstanceOf<NullMediaGenerationGateway>(), "no gateway is configured before P2.2");
            MediaGenerationResult voice = AudioTools.GenerateVoice(bank, "voice.test.line", "The bell has not rung in years.", string.Empty, "warden", "Assets/Hollowmere/Audio/Generated/Voice");
            MediaGenerationResult sfx = AudioTools.GenerateSfx(bank, "sfx.test.creak", "a wet wooden door creaking", 800, true, "Assets/Hollowmere/Audio/Generated/Sfx");
            Assert.That(voice.Status, Is.EqualTo(MediaGenerationStatus.NotConfigured));
            Assert.That(voice.Code, Is.EqualTo(PresentationDiagnosticCodes.MediaNotConfigured));
            Assert.That(sfx.Status, Is.EqualTo(MediaGenerationStatus.NotConfigured));
            Assert.That(bank.Entries.Count, Is.EqualTo(before));
            Assert.That(Directory.Exists("Assets/Hollowmere/Audio/Generated/Voice"), Is.False, "nothing was written");
        }

        [UnityTest]
        public IEnumerator PreviewScreenCapturesARenderTextureOrSkipsHeadless()
        {
            ScreenFlowDefinition flow = UiAudioHarness.Content().Flow!;
            string output = Path.Combine(Path.GetTempPath(), "gamecore-p1_5-preview.png");
            using (UiPreviewSession session = UiPreviewSession.Begin(flow, UiScreen.Pause, "menu.Title=Hollowmere", 640, 360))
            {
                if (!session.Available)
                {
                    Assert.That(session.Result.Reason, Does.StartWith(PresentationDiagnosticCodes.UiPreviewUnavailable));
                    Assert.Ignore("ui.previewScreen skipped: " + session.Result.Reason);
                }

                Assert.That(session.Result.VisibleLayers, Is.GreaterThanOrEqualTo(2), "the pause screen over the HUD");
                for (int i = 0; i < 4; i++)
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                    yield return null;
                }

                UiPreviewResult result = session.Capture(output);
                Assert.That(File.Exists(result.Path), Is.True);
                TestContext.Out.WriteLine("P1.5-PREVIEW " + result);
            }
        }
    }

    [TestFixture]
    public sealed class UiAudioWorldTests
    {
        private bool pumpWasEnabled;

        [SetUp]
        public void SetUp() => pumpWasEnabled = GameCore.Unity.Adapters.GameCoreApplicationPump.IsEnabled;

        [TearDown]
        public void TearDown() => GameCore.Unity.Adapters.GameCoreApplicationPump.IsEnabled = pumpWasEnabled;

        [Test]
        public void ScreenFlowRunsThroughCommittedSlots()
        {
            using (UiAudioHarness h = UiAudioHarness.Boot(UiScreen.Menu))
            {
                Assert.That(h.Ui.Screen, Is.EqualTo(UiScreen.Menu), "a fresh world starts on the menu");
                Assert.That(h.Ui.GameplayPaused, Is.True);
                Assert.That(h.World.Presentation.Has<IUiIntentSink>(), Is.True);
                Assert.That(h.World.Presentation.Has<IVolumeSettingsSink>(), Is.True);
                h.Expect("newgame", UiScreen.Hud);
                Assert.That(h.Ui.GameplayPaused, Is.False);

                h.Ui.Raise(UiIntent.Pause);
                h.PumpUntil(() => h.Ui.Screen == UiScreen.Pause, "pause intent");
                h.Expect("open.settings", UiScreen.Settings);
                h.Expect("close", UiScreen.Pause);
                h.Expect("resume", UiScreen.Hud);

                h.Ui.Raise(UiIntent.Journal);
                h.PumpUntil(() => h.Ui.Screen == UiScreen.Journal, "journal intent");
                h.Expect("open.inventory", UiScreen.Inventory);
                h.Ui.Raise(UiIntent.Inventory);
                h.PumpUntil(() => h.Ui.Screen == UiScreen.Hud, "inventory intent closes the inventory");

                // Refusals: closing the HUD and opening the save screen from the HUD both leave the committed screen as it was.
                int refused = h.Ui.Extension.Module!.Refused;
                Assert.That(h.Ui.Dispatcher.Dispatch("close").Accepted, Is.True, "admitted; the rule refuses it in the step");
                Assert.That(h.Ui.Dispatcher.Dispatch("open.save").Accepted, Is.True);
                h.PumpUntil(() => h.Ui.Extension.Module!.Refused == refused + 2, "two refusals traced");
                Assert.That(h.Ui.Screen, Is.EqualTo(UiScreen.Hud));
                IReadOnlyList<UiRefusalTrace> trace = h.Ui.Extension.Module!.Trace;
                Assert.That(trace[trace.Count - 2].Refusal, Is.EqualTo(UiRefusal.NothingToClose));
                Assert.That(trace[trace.Count - 1].Refusal, Is.EqualTo(UiRefusal.TransitionNotAllowed));

                h.Ui.Raise(UiIntent.Pause);
                h.PumpUntil(() => h.Ui.Screen == UiScreen.Pause, "pause again");
                h.Expect("quitToMenu", UiScreen.Menu);
                TestContext.Out.WriteLine("P1.5-FLOW changes=" + h.Ui.ScreenChanges + " accepted=" + h.Ui.Extension.Module!.Accepted
                    + " refused=" + h.Ui.Extension.Module!.Refused + " hostActions=" + h.Ui.HostActionsRun);
            }
        }

        [Test]
        public void SaveWithoutAServiceShowsTheUnavailableCode()
        {
            using (UiAudioHarness h = UiAudioHarness.Boot(UiScreen.Hud))
            {
                h.Ui.Raise(UiIntent.Pause);
                h.PumpUntil(() => h.Ui.Screen == UiScreen.Pause, "pause");
                h.Expect("open.save", UiScreen.Save);
                Assert.That(h.Ui.Models.SaveLoad.Mode, Is.EqualTo("Save"));
                Assert.That(h.Ui.Models.SaveLoad.SlotLabels.Length, Is.EqualTo(5));
                Assert.That(h.Ui.Dispatcher.Dispatch("select.1").Accepted, Is.True);
                Assert.That(h.Ui.Models.SaveLoad.Selected, Is.EqualTo(2));
                Assert.That(h.Ui.Dispatcher.Dispatch("slot.selected").Accepted, Is.True);
                h.PumpUntil(() => h.Ui.Models.SaveLoad.RefusalCode.Length > 0, "the save host action ran");
                Assert.That(h.Ui.Models.SaveLoad.RefusalCode, Is.EqualTo(PresentationDiagnosticCodes.UiSaveUnavailable));
                h.PumpUntil(() => h.Ui.Models.Screen.MessageVisible, "the outcome is committed as ui.message");
                Assert.That(h.Ui.Models.Screen.MessageText, Does.Contain(PresentationDiagnosticCodes.UiSaveUnavailable));
                Assert.That(h.Ui.Screen, Is.EqualTo(UiScreen.Save));
            }
        }

        [Test]
        public void DialogueViewReceivesTheViewModelAndNavigates()
        {
            using (UiAudioHarness h = UiAudioHarness.Boot(UiScreen.Hud))
            {
                var input = new RecordingDialogueInput();
                h.World.Presentation.Register<IDialogueInput>(input);
                IDialogueView view = h.World.Presentation.Get<IDialogueView>()!;
                view.Show(new DialogueViewModel
                {
                    SpeakerId = "warden",
                    SpeakerName = "The Warden",
                    Text = "Which way did the bell-ringer go?",
                    Choices = new[] { "To the marsh.", "To the belfry.", "I did not see." },
                    ChoiceDisabled = new[] { 0, 1, 0 },
                });
                DialoguePanelViewModel panel = h.Ui.Models.Dialogue;
                Assert.That(panel.Received, Is.EqualTo(1));
                Assert.That(panel.Visible, Is.True);
                Assert.That(panel.Speaker, Is.EqualTo("The Warden"));
                Assert.That(panel.Choices.Length, Is.EqualTo(3));
                Assert.That(panel.Selected, Is.EqualTo(0));
                h.Ui.Navigate(0, -1);
                Assert.That(panel.Selected, Is.EqualTo(2), "down skips the disabled choice");
                h.Ui.Confirm();
                Assert.That(input.Chosen, Is.EqualTo(new List<int> { 2 }));
                view.Hide();
                Assert.That(panel.Visible, Is.False);
            }
        }

        [Test]
        public void AudioKernelFollowsCommandsAndRefusesUnknownStates()
        {
            using (UiAudioHarness h = UiAudioHarness.Boot(UiScreen.Hud))
            {
                AudioRuntime audio = h.Audio;
                AudioCommandIssuer issuer = audio.Commands!;
                int explore = PresentationSlots.KeyOf(HollowmereUiAudioAuthoring.MusicExplore);
                int tense = PresentationSlots.KeyOf(HollowmereUiAudioAuthoring.MusicTense);
                Assert.That(issuer.Read(PresentationSlots.MusicState, -1), Is.EqualTo(explore), "the start state is seeded");
                Assert.That(audio.Music.CurrentState, Is.EqualTo(explore));
                RegionRecord village = h.Region("Thornwick Village");
                Assert.That(issuer.Read(PresentationSlots.AmbienceZone, -1), Is.EqualTo(village.Key), "the start region's ambience is seeded");

                Assert.That(issuer.SetMusicState(HollowmereUiAudioAuthoring.MusicTense, HollowmereUiAudioAuthoring.Bell).Admitted, Is.True);
                h.PumpUntil(() => audio.MusicState == tense, "music -> tense");
                Assert.That(audio.Music.Stingers, Is.EqualTo(1));
                Assert.That(audio.Music.LastStinger, Is.EqualTo(PresentationSlots.KeyOf(HollowmereUiAudioAuthoring.Bell)));

                int refused = audio.Extension.Module!.Refused;
                issuer.SetMusicState("music.unknown");
                issuer.SetMusicState(HollowmereUiAudioAuthoring.MusicTense);
                issuer.SetVolume(7, 500);
                issuer.SetVolume(0, 1001);
                h.PumpUntil(() => audio.Extension.Module!.Refused == refused + 4, "four refusals");
                Assert.That(audio.MusicState, Is.EqualTo(tense));

                int mirrored = -1;
                audio.VolumeCommitted += (channel, permille) => mirrored = channel == 1 ? permille : mirrored;
                int previous = issuer.Read(PresentationSlots.VolumeMusic, 800);
                int next = previous == 450 ? 500 : 450;
                h.Ui.Dispatcher.Dispatch("volume.1", next);
                h.PumpUntil(() => issuer.Read(PresentationSlots.VolumeMusic, -1) == next, "music volume");
                Assert.That(mirrored, Is.EqualTo(next));
                h.PumpUntil(() => h.Ui.Models.Settings.MusicVolume == next, "the settings model follows the committed volume");
                Assert.That(audio.Mixer.Decibels[1], Is.EqualTo(VolumeRules.ToDecibels(next)).Within(0.001f));
                h.Ui.Dispatcher.Dispatch("volume.1", previous);
                h.PumpUntil(() => issuer.Read(PresentationSlots.VolumeMusic, -1) == previous, "music volume restored");

                issuer.PlaySfx("sfx.footstep", 1000, 0, 2000);
                issuer.PlaySfx("sfx.not-in-bank");
                issuer.PlayVoice("voice.warden.greeting", "warden");
                h.PumpUntil(() => audio.Voice.Started == 1, "voice event");
                Assert.That(audio.Sfx.Played, Is.EqualTo(1));
                Assert.That(audio.Sfx.Missing, Is.EqualTo(1));
                h.World.Presentation.Get<IVoiceLinePlayer>()!.Stop();
                h.PumpUntil(() => audio.Voice.Stopped == 1, "voice stopped");
                h.World.Presentation.Get<IFootstepSink>()!.OnFootstep(new FootstepEvent("player", 0, 0, 0, 0, false));
                Assert.That(audio.Sfx.Played, Is.EqualTo(2), "footsteps resolve through the sfx. prefix");
                IFeedbackSink cues = h.World.Presentation.Get<IFeedbackSink>()!;
                cues.OnFeedback(new FeedbackCue(string.Empty, "ui.click", 0, 0, 0, 0));
                Assert.That(audio.Sfx.Played, Is.EqualTo(3), "interaction cues resolve through the sfx. prefix");
                Assert.That(audio.Sfx.LastId, Is.EqualTo("sfx.ui.click"));
                cues.OnFeedback(new FeedbackCue("gate", "refused:GP-INT-003", 0, 0, 0, 0));
                Assert.That(audio.Sfx.Missing, Is.EqualTo(2), "Hollowmere's bank has no refused clip");
                TestContext.Out.WriteLine("P1.5-AUDIO accepted=" + audio.Extension.Module!.Accepted + " refused=" + audio.Extension.Module!.Refused);
            }
        }

        [Test]
        public void AmbienceZoneFollowsRegionEntered()
        {
            using (UiAudioHarness h = UiAudioHarness.Boot(UiScreen.Hud))
            {
                RegionRecord marsh = h.Region("Blackmere Marsh");
                RegionRecord belfry = h.Region("Drowned Belfry");
                TargetId traveller = h.World.Focus;
                Assert.That(h.World.Commands.Travel(traveller, marsh.AuthoringId).Admitted, Is.True);
                h.PumpUntil(() => h.Audio.AmbienceZone == marsh.Key, "ambience -> marsh");
                Assert.That(h.Audio.Ambience.CurrentZone, Is.EqualTo(marsh.Key));
                Assert.That(h.Audio.Ambience.CurrentRegionId, Is.EqualTo(marsh.AuthoringId));
                Assert.That(h.Ui.Models.Hud.RegionName, Is.EqualTo(marsh.Name), "the HUD banner names the region");
                Assert.That(h.World.Commands.Travel(traveller, belfry.AuthoringId).Admitted, Is.True);
                h.PumpUntil(() => h.Audio.AmbienceZone == belfry.Key, "ambience -> belfry");
                Assert.That(h.Audio.ZoneRequests, Is.EqualTo(2));
                Assert.That(h.Audio.AmbienceChanges, Is.GreaterThanOrEqualTo(3));
            }
        }

        private sealed class RecordingDialogueInput : IDialogueInput
        {
            public List<int> Chosen { get; } = new List<int>();

            public int Advanced { get; private set; }

            public void Choose(int index) => Chosen.Add(index);

            public void Advance() => Advanced++;
        }
    }
}
