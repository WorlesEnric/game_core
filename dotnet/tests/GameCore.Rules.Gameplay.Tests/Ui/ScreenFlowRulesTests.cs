// GameCore.Rules.Gameplay.Tests - UI screen flow, intents, settings and slot naming (P1.5, catalog row 10).
#nullable enable
using System.Collections.Generic;
using GameCore.Rules.Gameplay.Ui;
using NUnit.Framework;

namespace GameCore.Rules.Gameplay.Tests.Ui
{
    public sealed class ScreenFlowRulesTests
    {
        private static UiState On(UiScreen screen, UiScreen returnTo = UiScreen.None, int message = 0) =>
            new UiState((int)screen, (int)returnTo, message);

        [TestCase(UiScreen.None, UiScreen.Menu)]
        [TestCase(UiScreen.None, UiScreen.Hud)]
        [TestCase(UiScreen.Menu, UiScreen.Settings)]
        [TestCase(UiScreen.Menu, UiScreen.Load)]
        [TestCase(UiScreen.Hud, UiScreen.Pause)]
        [TestCase(UiScreen.Hud, UiScreen.Journal)]
        [TestCase(UiScreen.Hud, UiScreen.Inventory)]
        [TestCase(UiScreen.Hud, UiScreen.Ending)]
        [TestCase(UiScreen.Pause, UiScreen.Settings)]
        [TestCase(UiScreen.Pause, UiScreen.Save)]
        [TestCase(UiScreen.Pause, UiScreen.Load)]
        [TestCase(UiScreen.Journal, UiScreen.Inventory)]
        [TestCase(UiScreen.Save, UiScreen.Load)]
        public void Open_AllowsTheTableTransitions(UiScreen from, UiScreen to)
        {
            UiTransition result = ScreenFlowRules.Open(On(from), (int)to);
            Assert.That(result.Accepted, Is.True, result.ToString());
            Assert.That(result.Next.ScreenValue, Is.EqualTo(to));
            Assert.That(result.HostAction, Is.EqualTo(UiAction.None));
        }

        [TestCase(UiScreen.Menu, UiScreen.Save)]
        [TestCase(UiScreen.Menu, UiScreen.Pause)]
        [TestCase(UiScreen.Hud, UiScreen.Save)]
        [TestCase(UiScreen.Hud, UiScreen.Menu)]
        [TestCase(UiScreen.Ending, UiScreen.Pause)]
        [TestCase(UiScreen.Settings, UiScreen.Save)]
        [TestCase(UiScreen.Pause, UiScreen.Journal)]
        public void Open_RefusesEverythingElse(UiScreen from, UiScreen to)
        {
            UiTransition result = ScreenFlowRules.Open(On(from), (int)to);
            Assert.That(result.Refusal, Is.EqualTo(UiRefusal.TransitionNotAllowed));
            Assert.That(result.Next.ScreenValue, Is.EqualTo(from), "a refusal keeps the state");
            StringAssert.Contains(from.ToString(), result.Detail, "the refusal names the inputs it read");
        }

        [Test]
        public void Open_RefusesUnknownAndUnchangedScreens()
        {
            Assert.That(ScreenFlowRules.Open(On(UiScreen.Hud), 42).Refusal, Is.EqualTo(UiRefusal.UnknownScreen));
            Assert.That(ScreenFlowRules.Open(On(UiScreen.Hud), -1).Refusal, Is.EqualTo(UiRefusal.UnknownScreen));
            Assert.That(ScreenFlowRules.Open(new UiState(77, 0, 0), (int)UiScreen.Hud).Refusal, Is.EqualTo(UiRefusal.UnknownScreen));
            Assert.That(ScreenFlowRules.Open(On(UiScreen.Pause), (int)UiScreen.Pause).Refusal, Is.EqualTo(UiRefusal.Unchanged));
        }

        [Test]
        public void SubScreens_ReturnToTheScreenTheyWereOpenedFrom()
        {
            UiTransition settings = ScreenFlowRules.Open(On(UiScreen.Pause), (int)UiScreen.Settings);
            Assert.That(settings.Next.ReturnTo, Is.EqualTo((int)UiScreen.Pause));
            UiTransition back = ScreenFlowRules.Close(settings.Next);
            Assert.That(back.Next.ScreenValue, Is.EqualTo(UiScreen.Pause));
            Assert.That(back.Next.ReturnTo, Is.EqualTo((int)UiScreen.None));

            UiTransition fromMenu = ScreenFlowRules.Open(On(UiScreen.Menu), (int)UiScreen.Settings);
            Assert.That(ScreenFlowRules.Close(fromMenu.Next).Next.ScreenValue, Is.EqualTo(UiScreen.Menu));

            // save -> load keeps the original return target, so closing load goes back to pause, not to save.
            UiTransition save = ScreenFlowRules.Open(On(UiScreen.Pause), (int)UiScreen.Save);
            UiTransition load = ScreenFlowRules.Open(save.Next, (int)UiScreen.Load);
            Assert.That(load.Next.ReturnTo, Is.EqualTo((int)UiScreen.Pause));
            Assert.That(ScreenFlowRules.Close(load.Next).Next.ScreenValue, Is.EqualTo(UiScreen.Pause));
        }

        [TestCase(UiScreen.Pause, UiScreen.Hud)]
        [TestCase(UiScreen.Journal, UiScreen.Hud)]
        [TestCase(UiScreen.Inventory, UiScreen.Hud)]
        public void Close_ReturnsOverlaysToTheHud(UiScreen from, UiScreen expected)
        {
            UiTransition result = ScreenFlowRules.Close(On(from));
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Next.ScreenValue, Is.EqualTo(expected));
        }

        [TestCase(UiScreen.Hud)]
        [TestCase(UiScreen.Menu)]
        [TestCase(UiScreen.Ending)]
        [TestCase(UiScreen.None)]
        public void Close_RefusesScreensWithNothingToClose(UiScreen screen)
        {
            Assert.That(ScreenFlowRules.Close(On(screen)).Refusal, Is.EqualTo(UiRefusal.NothingToClose));
        }

        [Test]
        public void Close_OfASubScreenWithoutAReturnTarget_FallsBackToTheMenu()
        {
            Assert.That(ScreenFlowRules.Close(On(UiScreen.Load, UiScreen.None)).Next.ScreenValue, Is.EqualTo(UiScreen.Menu));
            Assert.That(ScreenFlowRules.Close(On(UiScreen.Load, UiScreen.Save)).Next.ScreenValue, Is.EqualTo(UiScreen.Menu));
        }

        [Test]
        public void Commands_AreOfferedOnlyWhereTheirButtonIs()
        {
            Assert.That(ScreenFlowRules.Command(On(UiScreen.Menu), (int)UiAction.NewGame, 0).Accepted, Is.True);
            Assert.That(ScreenFlowRules.Command(On(UiScreen.Hud), (int)UiAction.NewGame, 0).Refusal, Is.EqualTo(UiRefusal.CommandNotOffered));
            Assert.That(ScreenFlowRules.Command(On(UiScreen.Pause), (int)UiAction.SaveSlot, 1).Refusal, Is.EqualTo(UiRefusal.CommandNotOffered));
            Assert.That(ScreenFlowRules.Command(On(UiScreen.Save), (int)UiAction.SaveSlot, 1).Accepted, Is.True);
            Assert.That(ScreenFlowRules.Command(On(UiScreen.Save), (int)UiAction.LoadSlot, 1).Refusal, Is.EqualTo(UiRefusal.CommandNotOffered));
            Assert.That(ScreenFlowRules.Command(On(UiScreen.Hud), (int)UiAction.Quit, 0).Refusal, Is.EqualTo(UiRefusal.CommandNotOffered));
            Assert.That(ScreenFlowRules.Command(On(UiScreen.Menu), 0, 0).Refusal, Is.EqualTo(UiRefusal.CommandNotOffered));
            Assert.That(ScreenFlowRules.Command(On(UiScreen.Menu), 99, 0).Refusal, Is.EqualTo(UiRefusal.CommandNotOffered));
        }

        [Test]
        public void Commands_DecideTheNextScreenAndTheHostAction()
        {
            UiTransition newGame = ScreenFlowRules.Command(On(UiScreen.Menu, UiScreen.None, 5), (int)UiAction.NewGame, 0);
            Assert.That(newGame.Next.ScreenValue, Is.EqualTo(UiScreen.Hud));
            Assert.That(newGame.Next.Message, Is.EqualTo(0), "a new game clears the message");
            Assert.That(newGame.HostAction, Is.EqualTo(UiAction.NewGame));

            UiTransition save = ScreenFlowRules.Command(On(UiScreen.Save, UiScreen.Pause), (int)UiAction.SaveSlot, 3);
            Assert.That(save.Next.ScreenValue, Is.EqualTo(UiScreen.Save), "the save screen stays open while the host saves");
            Assert.That(save.HostAction, Is.EqualTo(UiAction.SaveSlot));
            Assert.That(save.Argument, Is.EqualTo(3));

            Assert.That(ScreenFlowRules.Command(On(UiScreen.Pause), (int)UiAction.Resume, 0).Next.ScreenValue, Is.EqualTo(UiScreen.Hud));
            Assert.That(ScreenFlowRules.Command(On(UiScreen.Pause), (int)UiAction.QuitToMenu, 0).Next.ScreenValue, Is.EqualTo(UiScreen.Menu));
            Assert.That(ScreenFlowRules.Command(On(UiScreen.Ending), (int)UiAction.Restart, 0).Next.ScreenValue, Is.EqualTo(UiScreen.Hud));
            UiTransition quit = ScreenFlowRules.Command(On(UiScreen.Menu), (int)UiAction.Quit, 0);
            Assert.That(quit.Next.ScreenValue, Is.EqualTo(UiScreen.None));
            Assert.That(quit.HostAction, Is.EqualTo(UiAction.Quit));
        }

        [Test]
        public void Loaded_GoesToTheHudFromAnyScreen_AndCarriesTheMessage()
        {
            for (int screen = 0; screen < ScreenFlowRules.ScreenCount; screen++)
            {
                UiTransition result = ScreenFlowRules.Command(new UiState(screen, (int)UiScreen.Pause, 0), (int)UiAction.Loaded, 77);
                Assert.That(result.Accepted, Is.True, ((UiScreen)screen).ToString());
                Assert.That(result.Next.ScreenValue, Is.EqualTo(UiScreen.Hud));
                Assert.That(result.Next.Message, Is.EqualTo(77));
                Assert.That(result.Next.ReturnTo, Is.EqualTo((int)UiScreen.None));
            }
        }

        [Test]
        public void SlotCommands_RefuseSlotsOutsideTheManualRange()
        {
            Assert.That(ScreenFlowRules.Command(On(UiScreen.Save), (int)UiAction.SaveSlot, 0).Refusal, Is.EqualTo(UiRefusal.BadArgument));
            Assert.That(ScreenFlowRules.Command(On(UiScreen.Load), (int)UiAction.LoadSlot, SaveSlotNaming.MaxManualSlots + 1).Refusal, Is.EqualTo(UiRefusal.BadArgument));
            Assert.That(ScreenFlowRules.Command(On(UiScreen.Load), (int)UiAction.DeleteSlot, SaveSlotNaming.MaxManualSlots).Accepted, Is.True);
        }

        [Test]
        public void Messages_ShowAndClear_RefusingNoOps()
        {
            UiTransition shown = ScreenFlowRules.Command(On(UiScreen.Save), (int)UiAction.ShowMessage, 1234);
            Assert.That(shown.Next.Message, Is.EqualTo(1234));
            Assert.That(shown.Next.ScreenValue, Is.EqualTo(UiScreen.Save));
            Assert.That(ScreenFlowRules.Command(shown.Next, (int)UiAction.ShowMessage, 1234).Refusal, Is.EqualTo(UiRefusal.Unchanged));
            UiTransition cleared = ScreenFlowRules.Command(shown.Next, (int)UiAction.ClearMessage, 0);
            Assert.That(cleared.Next.Message, Is.EqualTo(0));
            Assert.That(ScreenFlowRules.Command(cleared.Next, (int)UiAction.ClearMessage, 0).Refusal, Is.EqualTo(UiRefusal.Unchanged));
        }

        [Test]
        public void PauseSemantics_OnlyTheHudLetsGameplayRun()
        {
            var running = new List<UiScreen>();
            for (int screen = 0; screen < ScreenFlowRules.ScreenCount; screen++)
            {
                if (!ScreenFlowRules.PausesGameplay((UiScreen)screen))
                {
                    running.Add((UiScreen)screen);
                }
            }

            Assert.That(running, Is.EqualTo(new[] { UiScreen.Hud }));
            Assert.That(ScreenFlowRules.IsModal(UiScreen.None), Is.False);
            Assert.That(ScreenFlowRules.IsModal(UiScreen.Hud), Is.False);
            Assert.That(ScreenFlowRules.IsModal(UiScreen.Settings), Is.True);
        }

        [TestCase(UiScreen.Hud, UiIntentRules.Pause, UiIntentRules.Effect.Open, UiScreen.Pause)]
        [TestCase(UiScreen.Pause, UiIntentRules.Pause, UiIntentRules.Effect.Close, UiScreen.Pause)]
        [TestCase(UiScreen.Hud, UiIntentRules.Journal, UiIntentRules.Effect.Open, UiScreen.Journal)]
        [TestCase(UiScreen.Journal, UiIntentRules.Journal, UiIntentRules.Effect.Close, UiScreen.Journal)]
        [TestCase(UiScreen.Journal, UiIntentRules.Inventory, UiIntentRules.Effect.Open, UiScreen.Inventory)]
        [TestCase(UiScreen.Menu, UiIntentRules.Journal, UiIntentRules.Effect.Ignore, UiScreen.Menu)]
        [TestCase(UiScreen.Settings, UiIntentRules.Cancel, UiIntentRules.Effect.Close, UiScreen.Settings)]
        [TestCase(UiScreen.Hud, UiIntentRules.Cancel, UiIntentRules.Effect.Ignore, UiScreen.Hud)]
        [TestCase(UiScreen.Menu, UiIntentRules.Confirm, UiIntentRules.Effect.Activate, UiScreen.Menu)]
        [TestCase(UiScreen.Hud, UiIntentRules.Confirm, UiIntentRules.Effect.Ignore, UiScreen.Hud)]
        public void Intents_MapToOpenCloseOrActivate(UiScreen screen, int intent, UiIntentRules.Effect effect, UiScreen open)
        {
            Assert.That(UiIntentRules.Map(screen, intent, out UiScreen target), Is.EqualTo(effect));
            Assert.That(target, Is.EqualTo(open));
        }

        [Test]
        public void Settings_ClampAndStep()
        {
            Assert.That(SettingsRules.ClampSensitivity(0f), Is.EqualTo(SettingsRules.MinSensitivity).Within(1e-6));
            Assert.That(SettingsRules.ClampSensitivity(99f), Is.EqualTo(SettingsRules.MaxSensitivity).Within(1e-6));
            Assert.That(SettingsRules.ClampSensitivity(float.NaN), Is.EqualTo(SettingsRules.DefaultSensitivity));
            Assert.That(SettingsRules.ClampSensitivity(1.234f), Is.EqualTo(1.25f).Within(1e-5));
            Assert.That(SettingsRules.ClampVolume(-5), Is.EqualTo(0));
            Assert.That(SettingsRules.ClampVolume(1500), Is.EqualTo(1000));
            Assert.That(SettingsRules.StepVolume(980, 1), Is.EqualTo(1000));
            Assert.That(SettingsRules.StepVolume(30, -1), Is.EqualTo(0));
            Assert.That(SettingsRules.StepVolume(500, 1), Is.EqualTo(550));
        }

        [Test]
        public void Settings_ResolutionChoices_AreDistinctLargestFirst_AndIncludeTheCurrentSize()
        {
            var available = new[]
            {
                new ResolutionChoice(1280, 720), new ResolutionChoice(1920, 1080), new ResolutionChoice(1280, 720),
                new ResolutionChoice(640, 480), new ResolutionChoice(2560, 1440),
            };
            IReadOnlyList<ResolutionChoice> choices = SettingsRules.Choices(available, new ResolutionChoice(1600, 900), 800);
            Assert.That(choices, Is.EqualTo(new[]
            {
                new ResolutionChoice(2560, 1440), new ResolutionChoice(1920, 1080), new ResolutionChoice(1600, 900), new ResolutionChoice(1280, 720),
            }));
            Assert.That(SettingsRules.IndexOf(choices, new ResolutionChoice(1920, 1080)), Is.EqualTo(1));
            Assert.That(SettingsRules.IndexOf(choices, new ResolutionChoice(1366, 768)), Is.EqualTo(3), "closest by area");
            Assert.That(SettingsRules.IndexOf(new ResolutionChoice[0], new ResolutionChoice(1, 1)), Is.EqualTo(-1));
        }

        [TestCase("slot-1", true, 1)]
        [TestCase("slot-9", true, 9)]
        [TestCase("slot-10", false, 0)]
        [TestCase("slot-0", false, 0)]
        [TestCase("slot-01", false, 0)]
        [TestCase("quick", false, 0)]
        [TestCase("slot-", false, 0)]
        public void SaveSlotNames_RoundTrip(string name, bool valid, int slot)
        {
            Assert.That(SaveSlotNaming.TryParse(name, out int parsed), Is.EqualTo(valid));
            Assert.That(parsed, Is.EqualTo(slot));
            if (valid)
            {
                Assert.That(SaveSlotNaming.SlotName(parsed), Is.EqualTo(name));
            }
        }
    }
}
