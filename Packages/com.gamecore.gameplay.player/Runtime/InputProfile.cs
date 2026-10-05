// GameCore.Gameplay.Player - PlayerDefinition and InputProfile (P1.3, catalog row 3).
//
// The player is an authored entity like any other (AuthoredEntity + EntityDefinition, so the bake, the streamer and
// Studio know it); what makes it the player is a PlayerDefinition whose `entity` is that entity's definition, and the
// world's focus entity being that placed entity. The PlayerDefinition holds the tuning the kernel rules use
// (converted once to integers: mm/s, ms, stamina units), the locomotion shape the presentation's CharacterController
// uses, the camera rig and the input profile.
#nullable enable
using System;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Rules.Gameplay.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GameCore.Gameplay.Player
{
    /// <summary>Which input actions drive the player, and the look sensitivity.</summary>
    [Authorable("player.inputProfile", DisplayName = "Input Profile", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Live,
        Doc = "The Input System actions asset and action names the player samples, and the look sensitivity.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/Input Profile", fileName = "InputProfile")]
    public sealed class InputProfile : ScriptableObject, IDefinitionAsset
    {
        public const string DefaultMap = "Player";

        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "asset.inputActions", Doc = "The actions asset (Move, Look, Jump, Interact, Pause, Journal, Inventory, Run).")]
        [SerializeField] private InputActionAsset? actions;

        [AuthorField(Doc = "Action map name.")]
        [SerializeField] private string map = DefaultMap;

        [AuthorField(Unit = "deg/unit", Min = 0.01, Max = 10, Doc = "Look sensitivity (degrees per input unit).")]
        [SerializeField] private float lookSensitivity = 0.15f;

        [AuthorField(Doc = "Invert vertical look.")]
        [SerializeField] private bool invertY;

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => contentStamp;

        public InputActionAsset? Actions => actions;

        public string Map => string.IsNullOrEmpty(map) ? DefaultMap : map;

        public float LookSensitivity => lookSensitivity;

        public bool InvertY => invertY;

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        public void Configure(InputActionAsset? asset, string mapName, float sensitivity)
        {
            actions = asset;
            map = mapName ?? DefaultMap;
            lookSensitivity = sensitivity;
        }

        private void Reset() => EnsureAuthoringId();

        private void OnValidate()
        {
            if (!AuthoringIds.IsValid(authoringId))
            {
                EnsureAuthoringId();
            }
        }
    }
}
