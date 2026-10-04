// GameCore.Gameplay.Entities - AuthoredEntity (P1.1).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using UnityEngine;

namespace GameCore.Gameplay.Entities
{
    /// <summary>
    /// A placed entity in a region scene: the authoring proxy of one kernel target. At play time a running gameplay world
    /// deactivates the proxy and presents the target through its definition's prefab instead.
    /// </summary>
    [Authorable("entity.instance", DisplayName = "Authored Entity", Scope = AuthorScope.Instance | AuthorScope.Prefab,
        RuntimeApplicability = RuntimeApply.Rebuild, Doc = "A placed entity: one kernel target with a definition, a variant and overrides.")]
    [DisallowMultipleComponent]
    public sealed class AuthoredEntity : MonoBehaviour, IAuthoredObject
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "entity.definition", Doc = "The definition this entity instantiates.")]
        [SerializeField] private EntityDefinition? definition;

        [AuthorField(Min = 0, Doc = "Variant index: 0 is the definition itself, 1..n its variants.")]
        [SerializeField] private int variant;

        [AuthorField(Doc = "Per-instance overrides of the definition's overridable fields.")]
        [SerializeField] private OverrideSet overrides = new OverrideSet();

        public string AuthoringId => authoringId;

        public EntityDefinition? Definition => definition;

        public int Variant => variant;

        public OverrideSet Overrides => overrides;

        /// <summary>
        /// The region this entity belongs to: the one authored region of its scene (auto-detected, never serialized).
        /// Null when the scene has no region, which the validator reports.
        /// </summary>
        public IAuthoredRegion? Region
        {
            get
            {
                if (!gameObject.scene.IsValid())
                {
                    return null;
                }

                GameObject[] roots = gameObject.scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    MonoBehaviour[] behaviours = roots[i].GetComponentsInChildren<MonoBehaviour>(true);
                    for (int b = 0; b < behaviours.Length; b++)
                    {
                        if (behaviours[b] is IAuthoredRegion region)
                        {
                            return region;
                        }
                    }
                }

                return null;
            }
        }

        public string RegionId
        {
            get
            {
                IAuthoredRegion? region = Region;
                return region != null ? region.AuthoringId : string.Empty;
            }
        }

        /// <summary>The kernel target id of this placed entity (empty id until minted).</summary>
        public GameCore.Contracts.TargetId TargetId =>
            AuthoringIds.IsValid(authoringId) ? AuthoringIds.TargetIdFor(authoringId) : default(GameCore.Contracts.TargetId);

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        /// <summary>Replaces the authoring id with a fresh one (the entity.duplicate tool on the copy).</summary>
        public void RemintAuthoringId() => authoringId = AuthoringIds.Mint();

        /// <summary>Clears the id (a prefab asset never carries one).</summary>
        public void ClearAuthoringId() => authoringId = string.Empty;

        public void SetDefinition(EntityDefinition? value) => definition = value;

        public void SetVariant(int value) => variant = value;

        private void Reset() => MintIfPlaced();

        private void OnValidate() => MintIfPlaced();

        /// <summary>Mints the id on a placed instance; a prefab asset (no valid scene) keeps an empty id.</summary>
        private void MintIfPlaced()
        {
            if (!gameObject.scene.IsValid())
            {
                return;
            }

            if (AuthoringIds.IsValid(authoringId))
            {
                return;
            }

#if UNITY_EDITOR
            // Prefab mode edits the prefab asset through a preview scene: the asset itself keeps an empty id.
            if (UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject) != null)
            {
                return;
            }
#endif

            EnsureAuthoringId();
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this))
            {
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
            }
#endif
        }
    }
}
