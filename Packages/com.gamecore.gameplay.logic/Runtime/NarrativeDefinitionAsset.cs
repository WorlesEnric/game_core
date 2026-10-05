// GameCore.Gameplay.Logic - NarrativeDefinitionAsset (its own file: Unity resolves a ScriptableObject script by file name).
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
    /// <summary>Shared identity plumbing of every narrative definition asset.</summary>
    public abstract class NarrativeDefinitionAsset : ScriptableObject, INarrativeDefinition
    {
        [SerializeField] private string authoringId = string.Empty;

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public virtual string DefinitionName => name;

        public string ContentStamp => contentStamp;

        public abstract string NarrativeKind { get; }

        /// <summary>
        /// Capability ids studio.core reads through AuthoringIdentity.GetCapabilities: every narrative definition provides
        /// <see cref="NarrativeKinds.Definition"/>, so a reference with that category (GameplayContentSet.definitions)
        /// accepts any of them through the generic create/assign/set tools.
        /// </summary>
        public virtual IEnumerable<string> Capabilities => DefinitionCapabilities;

        private static readonly IReadOnlyList<string> DefinitionCapabilities = Array.AsReadOnly(new[] { NarrativeKinds.Definition });

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        /// <summary>Sets an explicit authoring id (content authoring scripts that keep ids stable across re-creation).</summary>
        public void SetAuthoringId(string id)
        {
            if (!AuthoringIds.IsValid(id))
            {
                throw new ArgumentException("GP-ID-002: '" + id + "' is not a canonical authoring id");
            }

            authoringId = id;
        }

        public void SetContentStamp(string stamp) => contentStamp = stamp ?? string.Empty;

        protected virtual void Reset() => EnsureAuthoringId();

        protected virtual void OnValidate()
        {
            if (!AuthoringIds.IsValid(authoringId))
            {
                EnsureAuthoringId();
            }
        }
    }
}
