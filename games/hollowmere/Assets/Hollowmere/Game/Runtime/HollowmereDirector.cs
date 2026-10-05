// Hollowmere - the director (P3.1): turns committed narrative state into Hollowmere's presentation, every frame after
// the pump (HollowmereGame.LateUpdate), from the authored HollowmereDirectorDefinition:
//   * endings: when the director's quest completes (or fails) it fills the ending screen with the ending of the
//     committed quest branch (-1 for failure), switches the music state and opens the Ending screen through the
//     ordinary ui.open command, a short beat after the committed outcome so the last line and the bell are heard;
//   * consequences: music states chosen by facts (the bell rung -> the rung theme), looping ambience layers that play
//     in a region while a fact holds (the lit shrine's chimes in the marsh), region atmosphere (RegionAtmosphere of the
//     region scene applied to the Boot sun and post-processing volume on every region change);
//   * the interim fact-request bridge (inventory buy, loot rolls, stamina restore - replaced by P1.7a/b's declared
//     ActionKind.Buy / RestoreStamina);
//   * portraits next to the dialogue text (the UI package shows none), world-item presence (a taken pickup's view
//     hides) and the HUD stamina bar's visibility;
//   * the residency repair operations of a restore (InterimRestoreReattach) are polled here.
// It never writes slots directly: every state change is an ordinary typed command.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Gameplay.Audio;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Inventory;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Ui;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Inventory;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Rules.Gameplay.Quest;
using GameCore.Rules.Gameplay.Ui;
using GameCore.Unity.App;
using Hollowmere.Narrative;
using Hollowmere.UiAudio;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Hollowmere.Game
{
    /// <summary>Hollowmere's presentation director (see the file header).</summary>
    public sealed class HollowmereDirector : IDisposable
    {
        public const string PortraitElement = "hm-portrait";
        public const float EndingDelaySeconds = 3f;

        private readonly HollowmereGame game;
        private readonly HollowmereDirectorDefinition? definition;
        private readonly Dictionary<string, int> factKeys = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> itemKeys = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> idsByName = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> requestSeen = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<ISceneOperation> sceneOperations = new List<ISceneOperation>();
        private readonly WorldItemPresence presence = new WorldItemPresence();
        private NarrativeWorld? narrative;
        private HollowmereNarrativeModules? modules;
        private int questKey;
        private string musicRequested = string.Empty;
        private string regionId = string.Empty;
        private float outcomeSeenAt = -1f;
        private bool endingShown;
        private AudioSource? layerSource;
        private string layerClip = string.Empty;
        private VisualElement? portrait;
        private string portraitSpeaker = string.Empty;

        public HollowmereDirector(HollowmereGame game, HollowmereDirectorDefinition? definition)
        {
            this.game = game ?? throw new ArgumentNullException(nameof(game));
            this.definition = definition;
        }

        public HollowmereDirectorDefinition? Definition => definition;

        /// <summary>The quest outcome the ending screen shows (0 none, 1..n branch, -1 failed).</summary>
        public int Outcome { get; private set; }

        public bool EndingShown => endingShown;

        public string EndingTitle { get; private set; } = string.Empty;

        public string MusicState => musicRequested;

        public string AmbienceLayer => layerClip;

        public int Buys { get; private set; }

        public int LootRolls { get; private set; }

        public int LootGranted { get; private set; }

        /// <summary>Stamina restores requested before player.restoreStamina exists (logged; P1.7a adds the route).</summary>
        public int StaminaRestoresUnavailable { get; private set; }

        public int AtmosphereApplied { get; private set; }

        public string LastProblem { get; private set; } = string.Empty;

        /// <summary>Binds the director to a (new or restored) narrative world.</summary>
        public void Attach(NarrativeWorld world, HollowmereNarrativeModules narrativeModules)
        {
            narrative = world ?? throw new ArgumentNullException(nameof(world));
            modules = narrativeModules ?? throw new ArgumentNullException(nameof(narrativeModules));
            factKeys.Clear();
            itemKeys.Clear();
            idsByName.Clear();
            NarrativeModelSet models = world.Runtime.Models;
            foreach (FactModel fact in models.Facts)
            {
                factKeys[fact.Name] = fact.Key;
            }

            foreach (ItemModel item in models.Items)
            {
                itemKeys[item.Name] = item.Key;
            }

            IReadOnlyList<ContentEntry> entries = world.Runtime.Content.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (!string.IsNullOrEmpty(entries[i].name) && !idsByName.ContainsKey(entries[i].name))
                {
                    idsByName.Add(entries[i].name, entries[i].authoringId);
                }
            }

            questKey = 0;
            if (definition != null)
            {
                foreach (QuestModel quest in models.Quests)
                {
                    if (string.Equals(quest.Name, definition.Quest, StringComparison.Ordinal))
                    {
                        questKey = quest.Key;
                    }
                }
            }

            // Requests count increments from now on: a restored counter is not re-executed.
            requestSeen.Clear();
            if (definition != null)
            {
                for (int i = 0; i < definition.Requests.Count; i++)
                {
                    FactRequest request = definition.Requests[i];
                    if (request != null && !requestSeen.ContainsKey(request.fact))
                    {
                        requestSeen.Add(request.fact, Fact(request.fact));
                    }
                }
            }

            if (narrativeModules.Inventory.WorldItems != null)
            {
                narrativeModules.Inventory.WorldItems.Presence = presence;
            }

            int status = QuestStatus();
            endingShown = status == QuestIds.Completed || status == QuestIds.Failed ? endingShown : false;
            outcomeSeenAt = -1f;
            musicRequested = string.Empty;
            regionId = string.Empty;
        }

        /// <summary>Adds scene operations to poll (restore residency repair).</summary>
        public void Poll(IReadOnlyList<ISceneOperation> operations)
        {
            for (int i = 0; i < operations.Count; i++)
            {
                sceneOperations.Add(operations[i]);
            }
        }

        // ------------------------------------------------------------------ queries

        /// <summary>A fact's committed value by name (0 when unknown).</summary>
        public int Fact(string name)
        {
            NarrativeWorld? world = narrative;
            return world != null && factKeys.TryGetValue(name ?? string.Empty, out int key) ? world.Runtime.State.Fact(key) : 0;
        }

        /// <summary>How many of an item (by definition name) the player holds.</summary>
        public int ItemCount(string name)
        {
            NarrativeWorld? world = narrative;
            return world != null && itemKeys.TryGetValue(name ?? string.Empty, out int key) ? world.Runtime.State.ItemCount(0, key, world.Runtime.ActorKey) : 0;
        }

        /// <summary>The director quest's committed status (QuestIds.Inactive..Failed).</summary>
        public int QuestStatus() => narrative != null && questKey != 0 ? narrative.Runtime.State.Quest(questKey, QuestField.Status) : 0;

        public int QuestStage() => narrative != null && questKey != 0 ? narrative.Runtime.State.Quest(questKey, QuestField.Stage) : 0;

        public int QuestBranch() => narrative != null && questKey != 0 ? narrative.Runtime.State.Quest(questKey, QuestField.Branch) : 0;

        /// <summary>The authoring id of a content definition by name (or the name itself when unknown).</summary>
        public string IdOf(string name) => idsByName.TryGetValue(name ?? string.Empty, out string? id) ? id : name ?? string.Empty;

        // ------------------------------------------------------------------ frame

        public void Tick()
        {
            PollSceneOperations();
            NarrativeWorld? world = narrative;
            if (world == null || definition == null || world.Root.State != GameApplicationState.Running)
            {
                return;
            }

            HollowmereUiAudio? rig = game.Rig;
            Requests(world);
            Endings(rig);
            Music(rig);
            RegionChange();
            Layers(rig);
            Portraits(rig);
            Presence(world);
            if (rig != null && rig.Ui.Screen == UiScreen.Hud)
            {
                rig.Ui.Models.Hud.StaminaVisible = true;
            }
        }

        private void PollSceneOperations()
        {
            for (int i = sceneOperations.Count - 1; i >= 0; i--)
            {
                if (sceneOperations[i].IsDone)
                {
                    sceneOperations.RemoveAt(i);
                }
            }
        }

        private void Requests(NarrativeWorld world)
        {
            HollowmereDirectorDefinition def = definition!;
            for (int i = 0; i < def.Requests.Count; i++)
            {
                FactRequest request = def.Requests[i];
                if (request == null || string.IsNullOrEmpty(request.fact))
                {
                    continue;
                }

                int now = Fact(request.fact);
                int seen = requestSeen.TryGetValue(request.fact, out int s) ? s : 0;
                if (now <= seen)
                {
                    requestSeen[request.fact] = now;
                    continue;
                }

                for (int n = seen + 1; n <= now; n++)
                {
                    Execute(world, request, n);
                }

                requestSeen[request.fact] = now;
            }
        }

        private void Execute(NarrativeWorld world, FactRequest request, int serial)
        {
            InventoryCommands? commands = modules?.Inventory.Commands;
            switch (request.kind)
            {
                case FactRequestKind.Buy:
                    if (commands != null && commands.Buy(IdOf(request.vendor), IdOf(request.item), Math.Max(1, request.count)).Admitted)
                    {
                        Buys++;
                    }
                    else
                    {
                        Problem("buy " + request.item + " from " + request.vendor + " was not admitted");
                    }

                    break;
                case FactRequestKind.Loot:
                    Roll(world, request, serial, commands);
                    break;
                case FactRequestKind.RestoreStamina:
                    StaminaRestoresUnavailable++;
                    Debug.Log("[Hollowmere] " + request.fact + ": stamina restore of " + request.count.ToString(CultureInfo.InvariantCulture)
                        + " requested; player.restoreStamina arrives with P1.7a (interim: not applied)");
                    break;
            }
        }

        /// <summary>A deterministic roll: the same counter value of the same fact always drops the same items.</summary>
        private void Roll(NarrativeWorld world, FactRequest request, int serial, InventoryCommands? commands)
        {
            LootTableDefinition? table = request.lootTable;
            if (table == null || commands == null || table.Entries.Count == 0)
            {
                Problem("loot request " + request.fact + " has no table");
                return;
            }

            LootRolls++;
            uint state = (uint)NarrativeKeys.NameKey("hollowmere.loot." + world.Runtime.Index.WorldId + "." + request.fact + "." + serial.ToString(CultureInfo.InvariantCulture)) | 1u;
            int totalWeight = 0;
            for (int i = 0; i < table.Entries.Count; i++)
            {
                totalWeight += Math.Max(1, table.Entries[i].weight);
            }

            for (int roll = 0; roll < Math.Max(1, table.Rolls); roll++)
            {
                state = Next(state);
                int pick = (int)(state % (uint)totalWeight);
                LootEntryDefinition? chosen = null;
                for (int i = 0; i < table.Entries.Count; i++)
                {
                    pick -= Math.Max(1, table.Entries[i].weight);
                    if (pick < 0)
                    {
                        chosen = table.Entries[i];
                        break;
                    }
                }

                if (chosen == null || chosen.item == null)
                {
                    continue;
                }

                state = Next(state);
                int span = Math.Max(0, chosen.max - chosen.min);
                int count = Math.Max(1, chosen.min + (int)(state % (uint)(span + 1)));
                if (commands.Grant(chosen.item.AuthoringId, count).Admitted)
                {
                    LootGranted += count;
                }
            }
        }

        private static uint Next(uint x)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return x;
        }

        private void Endings(HollowmereUiAudio? rig)
        {
            int status = QuestStatus();
            if (status != QuestIds.Completed && status != QuestIds.Failed)
            {
                Outcome = 0;
                outcomeSeenAt = -1f;
                endingShown = false;
                return;
            }

            Outcome = status == QuestIds.Failed ? -1 : QuestBranch();
            if (endingShown)
            {
                return;
            }

            if (outcomeSeenAt < 0f)
            {
                outcomeSeenAt = Time.realtimeSinceStartup;
                EndingEntry? early = definition!.EndingFor(Outcome);
                if (early != null && early.musicState.Length > 0)
                {
                    RequestMusic(rig, early.musicState);
                }
            }

            bool headless = rig == null || rig.Root == null;
            if (!headless && Time.realtimeSinceStartup - outcomeSeenAt < EndingDelaySeconds)
            {
                return;
            }

            EndingEntry? ending = definition!.EndingFor(Outcome);
            EndingTitle = ending != null ? ending.title : (Outcome < 0 ? "The marsh keeps what it takes" : "The End");
            endingShown = true;
            if (rig == null)
            {
                return;
            }

            rig.Ui.SetEnding(EndingTitle, ending != null ? ending.body : string.Empty);
            UiCommandIssuer? issuer = rig.Ui.Commands;
            if (issuer == null || !issuer.Open(UiScreen.Ending).Admitted)
            {
                Problem("the ending screen did not open");
            }

            game.Session?.Mark("ending:" + Outcome.ToString(CultureInfo.InvariantCulture));
        }

        private void Music(HollowmereUiAudio? rig)
        {
            if (Outcome != 0)
            {
                return;
            }

            string wanted = string.Empty;
            IReadOnlyList<MusicReaction> reactions = definition!.Music;
            for (int i = 0; i < reactions.Count; i++)
            {
                if (reactions[i] != null && reactions[i].musicState.Length > 0 && Fact(reactions[i].fact) >= reactions[i].value)
                {
                    wanted = reactions[i].musicState;
                }
            }

            if (wanted.Length > 0)
            {
                RequestMusic(rig, wanted);
            }
        }

        private void RequestMusic(HollowmereUiAudio? rig, string state)
        {
            if (string.Equals(state, musicRequested, StringComparison.Ordinal))
            {
                return;
            }

            AudioCommandIssuer? audio = rig != null ? rig.Audio.Commands : null;
            if (audio != null && audio.SetMusicState(state).Admitted)
            {
                musicRequested = state;
            }
        }

        private void RegionChange()
        {
            string current = game.CurrentRegionId();
            if (current.Length == 0 || string.Equals(current, regionId, StringComparison.Ordinal))
            {
                return;
            }

            regionId = current;
            ManifestRegion? region = game.CurrentRegion();
            if (region == null || BinderEnvironment.IsHeadless)
            {
                return;
            }

            Scene scene = SceneManager.GetSceneByPath(region.scenePath);
            RegionAtmosphere? atmosphere = scene.IsValid() && scene.isLoaded ? RegionAtmosphere.Find(scene) : null;
            if (atmosphere == null)
            {
                return;
            }

            Light? sun = RenderSettings.sun;
            if (sun == null)
            {
                Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i].type == LightType.Directional && lights[i].gameObject.scene == game.gameObject.scene)
                    {
                        sun = lights[i];
                        break;
                    }
                }
            }

            Volume? volume = null;
            Volume[] volumes = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None);
            for (int i = 0; i < volumes.Length; i++)
            {
                if (volumes[i].isGlobal && volumes[i].gameObject.scene == game.gameObject.scene)
                {
                    volume = volumes[i];
                    break;
                }
            }

            atmosphere.Apply(sun, volume);
            AtmosphereApplied++;
        }

        private void Layers(HollowmereUiAudio? rig)
        {
            string wanted = string.Empty;
            float volume = 0f;
            IReadOnlyList<AmbienceLayer> layers = definition!.Layers;
            for (int i = 0; i < layers.Count; i++)
            {
                AmbienceLayer layer = layers[i];
                if (layer == null || layer.clipId.Length == 0 || Fact(layer.fact) < layer.value)
                {
                    continue;
                }

                if (layer.regionId.Length > 0 && !string.Equals(layer.regionId, regionId, StringComparison.Ordinal))
                {
                    continue;
                }

                wanted = layer.clipId;
                volume = layer.volume;
            }

            layerClip = wanted;
            if (rig == null || rig.Engine == null || BinderEnvironment.IsHeadless)
            {
                return;
            }

            if (layerSource == null)
            {
                var host = new GameObject("Hollowmere Ambience Layer");
                host.transform.SetParent(game.transform, false);
                layerSource = host.AddComponent<AudioSource>();
                layerSource.loop = true;
                layerSource.playOnAwake = false;
                layerSource.spatialBlend = 0f;
                layerSource.volume = 0f;
            }

            if (wanted.Length > 0 && (layerSource.clip == null || !string.Equals(layerSource.clip.name, ClipName(rig, wanted), StringComparison.Ordinal)))
            {
                AudioBankDefinition? bank = rig.Audio.Set != null ? rig.Audio.Set.Bank : null;
                if (bank != null && bank.TryGet(wanted, out AudioBankEntry? entry) && entry != null && entry.Clip != null)
                {
                    layerSource.clip = entry.Clip;
                    layerSource.Play();
                }
            }

            float target = wanted.Length > 0 ? volume : 0f;
            layerSource.volume = Mathf.MoveTowards(layerSource.volume, target, Time.unscaledDeltaTime * 0.4f);
            if (layerSource.volume <= 0f && wanted.Length == 0 && layerSource.isPlaying)
            {
                layerSource.Stop();
                layerSource.clip = null;
            }
        }

        private static string ClipName(HollowmereUiAudio rig, string id)
        {
            AudioBankDefinition? bank = rig.Audio.Set != null ? rig.Audio.Set.Bank : null;
            return bank != null && bank.TryGet(id, out AudioBankEntry? entry) && entry != null && entry.Clip != null ? entry.Clip.name : id;
        }

        private void Portraits(HollowmereUiAudio? rig)
        {
            if (rig == null || rig.Root == null || rig.Root.Document == null)
            {
                return;
            }

            if (portrait == null || portrait.panel == null)
            {
                VisualElement? panel = rig.Root.Document.rootVisualElement?.Q<VisualElement>("dialogue-panel");
                if (panel == null)
                {
                    return;
                }

                portrait = panel.Q<VisualElement>(PortraitElement);
                if (portrait == null)
                {
                    portrait = new VisualElement { name = PortraitElement, pickingMode = PickingMode.Ignore };
                    portrait.style.width = 112;
                    portrait.style.height = 112;
                    portrait.style.position = Position.Absolute;
                    portrait.style.left = -128;
                    portrait.style.top = 8;
                    portrait.style.borderTopLeftRadius = 8;
                    portrait.style.borderTopRightRadius = 8;
                    portrait.style.borderBottomLeftRadius = 8;
                    portrait.style.borderBottomRightRadius = 8;
                    panel.Add(portrait);
                }

                portraitSpeaker = string.Empty;
            }

            string speaker = rig.Ui.Models.Dialogue.Visible ? rig.Ui.Models.Dialogue.Speaker : string.Empty;
            if (string.Equals(speaker, portraitSpeaker, StringComparison.Ordinal))
            {
                return;
            }

            portraitSpeaker = speaker;
            Texture2D? texture = speaker.Length > 0 ? definition!.PortraitOf(speaker) : null;
            portrait.style.display = texture != null ? DisplayStyle.Flex : DisplayStyle.None;
            portrait.style.backgroundImage = texture != null ? new StyleBackground(texture) : new StyleBackground(StyleKeyword.None);
        }

        private void Presence(NarrativeWorld world)
        {
            PrefabViewBinder? views = world.World.Views;
            if (views == null || BinderEnvironment.IsHeadless)
            {
                return;
            }

            presence.Apply(views);
        }

        private void Problem(string text)
        {
            LastProblem = text;
            Debug.LogWarning("[Hollowmere] director: " + text);
        }

        public void Dispose()
        {
            if (layerSource != null)
            {
                UnityEngine.Object.Destroy(layerSource.gameObject);
                layerSource = null;
            }

            narrative = null;
            modules = null;
        }

        /// <summary>Hides the view of a taken world item (the WorldItemBinder reports item.taken changes).</summary>
        private sealed class WorldItemPresence : IWorldItemPresence
        {
            private readonly Dictionary<string, bool> present = new Dictionary<string, bool>(StringComparer.Ordinal);

            public void SetPresent(string worldItemAuthoringId, string entityAuthoringId, bool isPresent)
            {
                if (!string.IsNullOrEmpty(entityAuthoringId))
                {
                    present[entityAuthoringId] = isPresent;
                }
            }

            public void Apply(PrefabViewBinder views)
            {
                foreach (KeyValuePair<string, bool> item in present)
                {
                    if (!AuthoringIds.IsValid(item.Key) || !views.TryGetView(AuthoringIds.TargetIdFor(item.Key), out GameObject? view) || view == null)
                    {
                        continue;
                    }

                    Renderer[] renderers = view.GetComponentsInChildren<Renderer>(true);
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        if (renderers[i].enabled != item.Value)
                        {
                            renderers[i].enabled = item.Value;
                        }
                    }

                    Collider[] colliders = view.GetComponentsInChildren<Collider>(true);
                    for (int i = 0; i < colliders.Length; i++)
                    {
                        colliders[i].enabled = item.Value;
                    }
                }
            }
        }
    }
}
