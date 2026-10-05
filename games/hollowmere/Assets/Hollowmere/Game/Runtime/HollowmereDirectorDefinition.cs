// Hollowmere - the director's data: how committed narrative state turns into presentation (P3.1).
//
// The director is presentation-side game code: it reads committed slots after the step and never writes state except
// through ordinary typed commands (the fact-request bridge below). Every Hollowmere-specific choice it makes - which
// quest ends the game, the ending texts, the music state of a consequence, the ambience layer of a lit shrine, the
// portraits of the speakers, which world items hide their entity once taken - is data on this asset, authored through
// Studio (create/set), so the game code holds no content.
//
// Fact requests are the interim bridge for effects the narrative action kinds cannot express yet (P1.7a/b add
// ActionKind.Buy and ActionKind.RestoreStamina): content raises a counter fact (AddFact +1) and the director turns each
// committed increment into one typed command (inventory buy, a deterministic loot roll, a stamina restore). It is
// replaced by the declared actions when they land.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Inventory;
using UnityEngine;

namespace Hollowmere.Game
{
    /// <summary>What a fact request turns into.</summary>
    public enum FactRequestKind
    {
        /// <summary>InventoryCommands.Buy(vendor, item, count).</summary>
        Buy = 0,

        /// <summary>A deterministic roll of a loot table, granted with derived request ids.</summary>
        Loot = 1,

        /// <summary>player.restoreStamina{amount} (waits for the P1.7a route; logged as unavailable until then).</summary>
        RestoreStamina = 2,
    }

    /// <summary>One ending: shown when the quest completes on <see cref="branch"/> (-1 = the quest failed).</summary>
    [Serializable]
    public sealed class EndingEntry
    {
        [AuthorField(Doc = "Quest branch that ends the game this way (1..n), or -1 for the failure ending.")]
        public int branch;

        [AuthorField(Doc = "Ending title.")]
        public string title = string.Empty;

        [AuthorField(Doc = "Ending text.")]
        public string body = string.Empty;

        [AuthorField(Doc = "Music state id to switch to (empty = keep).")]
        public string musicState = string.Empty;
    }

    /// <summary>A music state chosen while a fact holds a value (later entries win).</summary>
    [Serializable]
    public sealed class MusicReaction
    {
        [AuthorField(Doc = "Fact name.")]
        public string fact = string.Empty;

        [AuthorField(Doc = "Value the fact must reach (>=).")]
        public int value = 1;

        [AuthorField(Doc = "Music state id (audio.musicState).")]
        public string musicState = string.Empty;
    }

    /// <summary>A looping ambience layer that plays in a region while a fact holds (the lit shrine's chimes).</summary>
    [Serializable]
    public sealed class AmbienceLayer
    {
        [AuthorField(Doc = "Fact name.")]
        public string fact = string.Empty;

        [AuthorField(Doc = "Value the fact must reach (>=).")]
        public int value = 1;

        [AuthorField(Type = "authoringId", Doc = "Region authoring id where the layer plays (empty = everywhere).")]
        public string regionId = string.Empty;

        [AuthorField(Doc = "Bank clip id of the loop.")]
        public string clipId = string.Empty;

        [AuthorField(Min = 0, Max = 1, Doc = "Layer volume.")]
        public float volume = 0.6f;
    }

    /// <summary>Interim bridge: each committed increment of a counter fact becomes one typed command.</summary>
    [Serializable]
    public sealed class FactRequest
    {
        [AuthorField(Doc = "Counter fact raised by the content (AddFact +1).")]
        public string fact = string.Empty;

        [AuthorField(Doc = "What one increment does.")]
        public FactRequestKind kind;

        [AuthorField(Doc = "Vendor definition name (Buy).")]
        public string vendor = string.Empty;

        [AuthorField(Doc = "Item definition name (Buy).")]
        public string item = string.Empty;

        [AuthorField(Min = 1, Doc = "Count (Buy) or amount (RestoreStamina).")]
        public int count = 1;

        [AuthorRef(Category = "inventory.lootTable", Required = false, Doc = "Loot table (Loot).")]
        public LootTableDefinition? lootTable;
    }

    /// <summary>A speaker's portrait for the dialogue panel.</summary>
    [Serializable]
    public sealed class PortraitEntry
    {
        [AuthorField(Doc = "Speaker name as the dialogue shows it.")]
        public string speaker = string.Empty;

        [AuthorRef(Category = "texture.texture2d", Doc = "Portrait texture.")]
        public Texture2D? portrait;
    }

    /// <summary>The Hollowmere director: endings, music, ambience layers, fact requests, portraits, world item presence.</summary>
    [Authorable("hollowmere.director", DisplayName = "Hollowmere Director", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "How committed narrative state is presented in Hollowmere: endings, music, ambience layers, interim fact requests, portraits and world-item presence.")]
    [CreateAssetMenu(menuName = "Hollowmere/Director", fileName = "HollowmereDirector")]
    public sealed class HollowmereDirectorDefinition : ScriptableObject, IAuthoredObject
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorField(Doc = "The quest whose completion or failure ends the game (definition name).")]
        [SerializeField] private string quest = "DrownedBell";

        [AuthorField(Doc = "Endings by quest branch (-1 = failure).")]
        [SerializeField] private List<EndingEntry> endings = new List<EndingEntry>();

        [AuthorField(Doc = "Music states chosen by facts (later entries win).")]
        [SerializeField] private List<MusicReaction> music = new List<MusicReaction>();

        [AuthorField(Doc = "Consequence ambience layers.")]
        [SerializeField] private List<AmbienceLayer> layers = new List<AmbienceLayer>();

        [AuthorField(Doc = "Interim fact-request bridge (replaced by ActionKind.Buy / RestoreStamina).")]
        [SerializeField] private List<FactRequest> requests = new List<FactRequest>();

        [AuthorField(Doc = "Speaker portraits.")]
        [SerializeField] private List<PortraitEntry> portraits = new List<PortraitEntry>();

        [AuthorRef(Category = "inventory.worldItem", Doc = "World items whose placed entity hides once the item is taken.")]
        [SerializeField] private List<WorldItemDefinition> worldItems = new List<WorldItemDefinition>();

        public string AuthoringId => authoringId;

        public string Quest => quest;

        public IReadOnlyList<EndingEntry> Endings => endings;

        public IReadOnlyList<MusicReaction> Music => music;

        public IReadOnlyList<AmbienceLayer> Layers => layers;

        public IReadOnlyList<FactRequest> Requests => requests;

        public IReadOnlyList<PortraitEntry> Portraits => portraits;

        public IReadOnlyList<WorldItemDefinition> WorldItems => worldItems;

        /// <summary>The ending for a quest outcome (branch 1..n, or -1 for failure), or null.</summary>
        public EndingEntry? EndingFor(int branch)
        {
            for (int i = 0; i < endings.Count; i++)
            {
                if (endings[i] != null && endings[i].branch == branch)
                {
                    return endings[i];
                }
            }

            return null;
        }

        /// <summary>The portrait of a speaker, or null.</summary>
        public Texture2D? PortraitOf(string speaker)
        {
            for (int i = 0; i < portraits.Count; i++)
            {
                if (portraits[i] != null && string.Equals(portraits[i].speaker, speaker, StringComparison.Ordinal))
                {
                    return portraits[i].portrait;
                }
            }

            return null;
        }

        private void Reset() => EnsureId();

        private void OnValidate()
        {
            if (!AuthoringIds.IsValid(authoringId))
            {
                EnsureId();
            }
        }

        private void EnsureId()
        {
            authoringId = AuthoringIdField.Ensure(authoringId);
        }
    }
}
