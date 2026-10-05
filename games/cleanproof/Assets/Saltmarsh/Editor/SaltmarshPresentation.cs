#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GameCore.Gameplay.Audio;
using GameCore.Gameplay.Audio.Editor;
using GameCore.Gameplay.Ui;
using GameCore.Gameplay.Ui.Editor;
using GameCore.Rules.Gameplay.Ui;
using Saltmarsh.UiAudio;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static Saltmarsh.Authoring.SaltmarshAuthoring;
namespace Saltmarsh.Authoring
{
    public static class SaltmarshPresentation
    {
        public const string PresentationRoot = Root + "/Presentation";
        public static void Author()
        {
            if (AssetDatabase.LoadAssetAtPath<SaltmarshUiAudioContent>(Root+"/Resources/Saltmarsh/UiAudio.asset") != null) return;
            Directory.CreateDirectory(PresentationRoot+"/Audio");
            var specs=new List<ProceduralClipSpec>();
            string[] regions={"Harbour","Dunes","Lighthouse"};
            for(int i=0;i<regions.Length;i++) specs.Add(new ProceduralClipSpec("ambience."+regions[i],regions[i]+".wav",ProceduralRecipe.Ambience,3f,101+i){ noise=.3f, noiseCutoffHz=300f+i*500, tones=new[]{80f+i*30},toneGain=.12f,peak=.35f });
            specs.Add(new ProceduralClipSpec("music.coast","Coast.wav",ProceduralRecipe.Ambience,4f,210){tones=new[]{220f,330f,440f},toneGain=.2f,noise=.05f,peak=.3f});
            ProceduralAudioGenerator.Generate(PresentationRoot+"/Audio","ProceduralPlaceholders.json",specs);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var bank=Asset<AudioBankDefinition>(PresentationRoot+"/Bank.asset");
            foreach(var spec in specs) AudioTools.AssignClip(bank,spec.id,Load<AudioClip>(PresentationRoot+"/Audio/"+spec.file),spec.id.StartsWith("music",StringComparison.Ordinal)?AudioGroup.Music:AudioGroup.Ambience,.7f,true,false);
            var set=Asset<AudioSetDefinition>(PresentationRoot+"/AudioSet.asset"); set.Configure(bank,Array.Empty<MusicStateDefinition>(),"music.coast",Array.Empty<AmbienceDefinition>());
            AudioTools.SetMusicState(set,"music.coast","music.coast",1500,"",true,PresentationRoot+"/Music.asset");
            foreach(string name in regions) AudioTools.SetAmbience(set,Region(name),"ambience."+name,.7f,1500,PresentationRoot+"/Ambience"+name+".asset");
            Dirty(bank); Dirty(set);
            Directory.CreateDirectory(PresentationRoot+"/Screens");
            File.WriteAllText(PresentationRoot+"/Saltmarsh.uss",".saltmarsh { background-color: rgba(12,35,48,0.9); color: rgb(235,232,211); padding: 18px; margin: 14px; max-width: 640px; } Label { font-size: 20px; margin-bottom: 8px; white-space: normal; } Button { height: 34px; margin: 4px; } ListView { height: 150px; }\n");
            File.WriteAllText(PresentationRoot+"/Saltmarsh.tss","@import url(\"unity-theme://default\");\n");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var panel=Asset<PanelSettings>(PresentationRoot+"/Panel.asset"); panel.scaleMode=PanelScaleMode.ScaleWithScreenSize; panel.referenceResolution=new Vector2Int(1280,720); panel.themeStyleSheet=Load<ThemeStyleSheet>(PresentationRoot+"/Saltmarsh.tss"); Dirty(panel);
            var theme=Asset<ThemeDefinition>(PresentationRoot+"/Theme.asset");
            var flow=Asset<ScreenFlowDefinition>(PresentationRoot+"/Flow.asset"); flow.Configure("Saltmarsh — Relight the Coast",UiScreen.Menu,null,2500);
            UiTools.SetTheme(flow,theme,panel,panel.themeStyleSheet,new[]{Load<StyleSheet>(PresentationRoot+"/Saltmarsh.uss")});
            Screen(flow, UiScreen.Hud, "Hud", @"<ui:UXML xmlns:ui=""UnityEngine.UIElements""><ui:VisualElement class=""saltmarsh""><ui:Label text=""Saltmarsh · Hud"" /><ui:Label name=""objective-line"" text=""objective line"" /><ui:ProgressBar name=""stamina-bar"" /><ui:Label name=""region-banner"" text=""region banner"" /><ui:Label name=""region-name"" text=""region name"" /><ui:Label name=""message"" text=""message"" /><ui:Label name=""prompt"" text=""prompt"" /><ui:Label name=""prompt-text"" text=""prompt text"" /></ui:VisualElement></ui:UXML>", 0);
            Screen(flow, UiScreen.Hud, "Dialogue", @"<ui:UXML xmlns:ui=""UnityEngine.UIElements""><ui:VisualElement class=""saltmarsh""><ui:Label text=""Saltmarsh · Dialogue"" /><ui:Label name=""dialogue-panel"" text=""dialogue panel"" /><ui:Label name=""dialogue-speaker"" text=""dialogue speaker"" /><ui:Label name=""dialogue-text"" text=""dialogue text"" /><ui:ListView name=""dialogue-choices"" /><ui:Button name=""dialogue-continue"" text=""dialogue continue"" /></ui:VisualElement></ui:UXML>", 20);
            Screen(flow, UiScreen.Menu, "Menu", @"<ui:UXML xmlns:ui=""UnityEngine.UIElements""><ui:VisualElement class=""saltmarsh""><ui:Label text=""Saltmarsh · Menu"" /><ui:Label name=""menu-title"" text=""menu title"" /><ui:Button name=""menu-continue"" text=""menu continue"" /><ui:Label name=""menu-message"" text=""menu message"" /><ui:Button name=""menu-new"" text=""menu new"" /><ui:Button name=""menu-load"" text=""menu load"" /><ui:Button name=""menu-settings"" text=""menu settings"" /><ui:Button name=""menu-quit"" text=""menu quit"" /></ui:VisualElement></ui:UXML>", 0);
            Screen(flow, UiScreen.Pause, "Pause", @"<ui:UXML xmlns:ui=""UnityEngine.UIElements""><ui:VisualElement class=""saltmarsh""><ui:Label text=""Saltmarsh · Pause"" /><ui:Button name=""pause-resume"" text=""pause resume"" /><ui:Button name=""pause-save"" text=""pause save"" /><ui:Button name=""pause-load"" text=""pause load"" /><ui:Button name=""pause-settings"" text=""pause settings"" /><ui:Button name=""pause-menu"" text=""pause menu"" /><ui:Button name=""pause-quit"" text=""pause quit"" /><ui:Label name=""pause-volume"" text=""pause volume"" /></ui:VisualElement></ui:UXML>", 0);
            Screen(flow, UiScreen.Settings, "Settings", @"<ui:UXML xmlns:ui=""UnityEngine.UIElements""><ui:VisualElement class=""saltmarsh""><ui:Label text=""Saltmarsh · Settings"" /><ui:SliderInt name=""settings-master"" low-value=""0"" high-value=""1000"" /><ui:SliderInt name=""settings-music"" low-value=""0"" high-value=""1000"" /><ui:SliderInt name=""settings-sfx"" low-value=""0"" high-value=""1000"" /><ui:SliderInt name=""settings-voice"" low-value=""0"" high-value=""1000"" /><ui:Slider name=""settings-sensitivity"" /><ui:DropdownField name=""settings-resolution"" /><ui:Toggle name=""settings-fullscreen"" /><ui:Button name=""settings-back"" text=""settings back"" /></ui:VisualElement></ui:UXML>", 0);
            Screen(flow, UiScreen.Save, "Save", @"<ui:UXML xmlns:ui=""UnityEngine.UIElements""><ui:VisualElement class=""saltmarsh""><ui:Label text=""Saltmarsh · Save"" /><ui:Label name=""saveload-title"" text=""saveload title"" /><ui:ListView name=""saveload-slots"" /><ui:Label name=""saveload-status"" text=""saveload status"" /><ui:Label name=""saveload-code"" text=""saveload code"" /><ui:Button name=""saveload-confirm"" text=""saveload confirm"" /><ui:Button name=""saveload-delete"" text=""saveload delete"" /><ui:Button name=""saveload-back"" text=""saveload back"" /></ui:VisualElement></ui:UXML>", 0);
            Screen(flow, UiScreen.Load, "Load", @"<ui:UXML xmlns:ui=""UnityEngine.UIElements""><ui:VisualElement class=""saltmarsh""><ui:Label text=""Saltmarsh · Load"" /><ui:Label name=""saveload-title"" text=""saveload title"" /><ui:ListView name=""saveload-slots"" /><ui:Label name=""saveload-status"" text=""saveload status"" /><ui:Label name=""saveload-code"" text=""saveload code"" /><ui:Button name=""saveload-confirm"" text=""saveload confirm"" /><ui:Button name=""saveload-delete"" text=""saveload delete"" /><ui:Button name=""saveload-back"" text=""saveload back"" /></ui:VisualElement></ui:UXML>", 0);
            Screen(flow, UiScreen.Journal, "Journal", @"<ui:UXML xmlns:ui=""UnityEngine.UIElements""><ui:VisualElement class=""saltmarsh""><ui:Label text=""Saltmarsh · Journal"" /><ui:ListView name=""journal-quests"" /><ui:ListView name=""journal-detail"" /><ui:Label name=""journal-empty"" text=""journal empty"" /><ui:Button name=""journal-inventory"" text=""journal inventory"" /><ui:Button name=""journal-close"" text=""journal close"" /></ui:VisualElement></ui:UXML>", 0);
            Screen(flow, UiScreen.Inventory, "Inventory", @"<ui:UXML xmlns:ui=""UnityEngine.UIElements""><ui:VisualElement class=""saltmarsh""><ui:Label text=""Saltmarsh · Inventory"" /><ui:ListView name=""inventory-grid"" /><ui:Label name=""inventory-selected"" text=""inventory selected"" /><ui:Label name=""inventory-currency"" text=""inventory currency"" /><ui:Button name=""inventory-use"" text=""inventory use"" /><ui:Button name=""inventory-drop"" text=""inventory drop"" /><ui:Button name=""inventory-journal"" text=""inventory journal"" /><ui:Button name=""inventory-close"" text=""inventory close"" /></ui:VisualElement></ui:UXML>", 0);
            Screen(flow, UiScreen.Ending, "Ending", @"<ui:UXML xmlns:ui=""UnityEngine.UIElements""><ui:VisualElement class=""saltmarsh""><ui:Label text=""Saltmarsh · Ending"" /><ui:Label name=""ending-title"" text=""ending title"" /><ui:Label name=""ending-body"" text=""ending body"" /><ui:Button name=""ending-restart"" text=""ending restart"" /><ui:Button name=""ending-menu"" text=""ending menu"" /></ui:VisualElement></ui:UXML>", 0);
            Dirty(flow);
            var content=Asset<SaltmarshUiAudioContent>(Root+"/Resources/Saltmarsh/UiAudio.asset"); content.Configure(flow,set); Dirty(content);
        }
        private static void Screen(ScreenFlowDefinition flow, UiScreen screen,string name,string xml,int layer)
        {
            string path=PresentationRoot+"/Screens/"+name+".uxml";
            File.WriteAllText(path,xml); AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var doc=UiTools.AddScreen(flow,screen,path,layer,PresentationRoot+"/"+name+".asset");
            BindScreen(doc,screen,path); Dirty(doc);
        }
        private static int BindScreen(UiDocumentDefinition d, UiScreen screen, string uxml)
        {
            int n = 0;
            if (uxml.EndsWith("Hud.uxml", StringComparison.Ordinal))
            {
                n += B(d, "objective-line", "vm:hud.ObjectiveText");
                n += B(d, "objective-line", "vm:hud.ObjectiveVisible", "visible");
                n += B(d, "stamina-bar", "vm:hud.Stamina", "value");
                n += B(d, "stamina-bar", "vm:hud.StaminaVisible", "visible");
                n += B(d, "region-banner", "vm:hud.RegionBannerVisible", "visible");
                n += B(d, "region-name", "event:region-entered");
                n += B(d, "message", "vm:screen.MessageText");
                n += B(d, "message", "vm:screen.MessageVisible", "visible");
                n += B(d, "prompt", "vm:prompt.Visible", "visible");
                n += B(d, "prompt-text", "vm:prompt.Text");
                n += B(d, "prompt-text", "vm:prompt.Enabled", "enabled");
                return n;
            }

            if (uxml.EndsWith("Dialogue.uxml", StringComparison.Ordinal))
            {
                n += B(d, "dialogue-panel", "vm:dialogue.Visible", "visible");
                n += B(d, "dialogue-speaker", "vm:dialogue.Speaker");
                n += B(d, "dialogue-text", "vm:dialogue.Text");
                n += B(d, "dialogue-choices", "vm:dialogue.Choices", "items", "command:choose");
                n += B(d, "dialogue-choices", "vm:dialogue.Selected", "selected");
                n += B(d, "dialogue-choices", "vm:dialogue.HasChoices", "visible");
                n += C(d, "dialogue-continue", "advance");
                return n;
            }

            switch (screen)
            {
                case UiScreen.Menu:
                    n += B(d, "menu-title", "vm:menu.Title");
                    n += B(d, "menu-continue", "vm:menu.ContinueAvailable", "enabled");
                    n += B(d, "menu-message", "vm:screen.MessageText");
                    n += C(d, "menu-new", "newgame");
                    n += C(d, "menu-continue", "continue");
                    n += C(d, "menu-load", "open.load");
                    n += C(d, "menu-settings", "open.settings");
                    n += C(d, "menu-quit", "quit");
                    break;
                case UiScreen.Pause:
                    n += C(d, "pause-resume", "resume");
                    n += C(d, "pause-save", "open.save");
                    n += C(d, "pause-load", "open.load");
                    n += C(d, "pause-settings", "open.settings");
                    n += C(d, "pause-menu", "quitToMenu");
                    n += C(d, "pause-quit", "quit");
                    n += B(d, "pause-volume", "slot:audio.owner/audio.volumeMaster@audio", "text", "percent:1000");
                    break;
                case UiScreen.Settings:
                    n += B(d, "settings-master", "vm:settings.MasterVolume", "value");
                    n += B(d, "settings-music", "vm:settings.MusicVolume", "value");
                    n += B(d, "settings-sfx", "vm:settings.SfxVolume", "value");
                    n += B(d, "settings-voice", "vm:settings.VoiceVolume", "value");
                    n += C(d, "settings-master", "volume.0", "changed");
                    n += C(d, "settings-music", "volume.1", "changed");
                    n += C(d, "settings-sfx", "volume.2", "changed");
                    n += C(d, "settings-voice", "volume.3", "changed");
                    n += B(d, "settings-master", "vm:settings.AudioAvailable", "enabled");
                    n += B(d, "settings-music", "vm:settings.AudioAvailable", "enabled");
                    n += B(d, "settings-sfx", "vm:settings.AudioAvailable", "enabled");
                    n += B(d, "settings-voice", "vm:settings.AudioAvailable", "enabled");
                    n += B(d, "settings-sensitivity", "vm:settings.Sensitivity", "value");
                    n += C(d, "settings-sensitivity", "sensitivity", "changed");
                    n += B(d, "settings-resolution", "vm:settings.Resolutions", "items");
                    n += B(d, "settings-resolution", "vm:settings.ResolutionIndex", "value");
                    n += C(d, "settings-resolution", "resolution", "changed");
                    n += B(d, "settings-fullscreen", "vm:settings.Fullscreen", "value");
                    n += C(d, "settings-fullscreen", "fullscreen", "changed");
                    n += C(d, "settings-back", "close");
                    break;
                case UiScreen.Save:
                case UiScreen.Load:
                    n += B(d, "saveload-title", "vm:saveload.Mode", "text", "{0} game");
                    n += B(d, "saveload-slots", "vm:saveload.SlotLabels", "items", "command:select");
                    n += B(d, "saveload-slots", "vm:saveload.Selected", "selected", "offset:-1");
                    n += B(d, "saveload-status", "vm:saveload.Status");
                    n += B(d, "saveload-code", "vm:saveload.RefusalCode");
                    n += B(d, "saveload-confirm", "vm:saveload.Available", "enabled");
                    n += B(d, "saveload-delete", "vm:saveload.Available", "enabled");
                    n += C(d, "saveload-confirm", "slot.selected");
                    n += C(d, "saveload-delete", "delete.selected");
                    n += C(d, "saveload-back", "close");
                    break;
                case UiScreen.Journal:
                    n += B(d, "journal-quests", "vm:journal.QuestLines", "items", "command:select");
                    n += B(d, "journal-quests", "vm:journal.Selected", "selected");
                    n += B(d, "journal-detail", "vm:journal.DetailLines", "items");
                    n += B(d, "journal-empty", "vm:journal.Empty", "visible");
                    n += C(d, "journal-inventory", "open.inventory");
                    n += C(d, "journal-close", "close");
                    break;
                case UiScreen.Inventory:
                    n += B(d, "inventory-grid", "vm:inventory.SlotLabels", "items", "command:item");
                    n += B(d, "inventory-grid", "vm:inventory.Selected", "selected");
                    n += B(d, "inventory-selected", "vm:inventory.SelectedName");
                    n += B(d, "inventory-currency", "vm:inventory.Currency", "text", "{0} coins");
                    n += B(d, "inventory-use", "vm:inventory.SelectedUsable", "enabled");
                    n += C(d, "inventory-use", "use");
                    n += C(d, "inventory-drop", "drop");
                    n += C(d, "inventory-journal", "open.journal");
                    n += C(d, "inventory-close", "close");
                    break;
                case UiScreen.Ending:
                    n += B(d, "ending-title", "vm:ending.Title");
                    n += B(d, "ending-body", "vm:ending.Body");
                    n += C(d, "ending-restart", "restart");
                    n += C(d, "ending-menu", "quitToMenu");
                    break;
            }

            return n;
        }

        private static int B(UiDocumentDefinition d, string element, string source, string property = "text", string format = "")
        {
            UiTools.Bind(d, element, source, property, format);
            return 1;
        }

        private static int C(UiDocumentDefinition d, string element, string command, string property = "clicked")
        {
            UiTools.SetCommand(d, element, command, property);
            return 1;
        }

    }
}
