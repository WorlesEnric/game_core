// GameCore.Gameplay.Logic - GameplayContentManifest (its own file: Unity resolves a ScriptableObject script by file name).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Logic;
using UnityEngine;

namespace GameCore.Gameplay.Logic
{
    /// <summary>
    /// The bake output the runtime boots narrative content from: every definition of a content set with its kind, key
    /// and content stamp, the fact table (one slot each), and a content hash over all of it. Written by the logic bake
    /// extension next to the content set; Entry.Verify recomputes it.
    /// </summary>
    public sealed class GameplayContentManifest : ScriptableObject
    {
        public const string Format = "gamecore.gameplay-content/1";

        [SerializeField] private string formatId = string.Empty;
        [SerializeField] private string worldId = string.Empty;
        [SerializeField] private string contentHash = string.Empty;
        [SerializeField] private List<ContentEntry> entries = new List<ContentEntry>();
        [SerializeField] private List<FactEntry> facts = new List<FactEntry>();

        public string FormatId => formatId;

        public string WorldId => worldId;

        public string ContentHash => contentHash;

        public IReadOnlyList<ContentEntry> Entries => entries;

        public IReadOnlyList<FactEntry> Facts => facts;

        public void Assign(string world, string hash, IEnumerable<ContentEntry> definitionEntries, IEnumerable<FactEntry> factEntries)
        {
            formatId = Format;
            worldId = world ?? string.Empty;
            contentHash = hash ?? string.Empty;
            entries = new List<ContentEntry>(definitionEntries);
            facts = new List<FactEntry>(factEntries);
        }

        /// <summary>The definitions of one kind, in manifest (authoring id) order.</summary>
        public List<ContentEntry> OfKind(string kind)
        {
            var list = new List<ContentEntry>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].kind, kind, StringComparison.Ordinal))
                {
                    list.Add(entries[i]);
                }
            }

            return list;
        }

        public ContentEntry? FindByAuthoringId(string authoringId)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].authoringId, authoringId, StringComparison.Ordinal))
                {
                    return entries[i];
                }
            }

            return null;
        }

        public ContentEntry? FindByName(string kind, string definitionName)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].kind, kind, StringComparison.Ordinal) && string.Equals(entries[i].name, definitionName, StringComparison.Ordinal))
                {
                    return entries[i];
                }
            }

            return null;
        }
    }
}
