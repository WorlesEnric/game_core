// GameCore.Gameplay.Entities - InteractionTargetMarker (P1.1).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameCore.Gameplay.Entities
{
    /// <summary>The marker the interaction binder puts on interactable views.</summary>
    public sealed class InteractionTargetMarker : MonoBehaviour, IInteractionTarget
    {
        public TargetId Target { get; private set; }

        public string InteractionKind { get; private set; } = string.Empty;

        public void Bind(TargetId target, string kind)
        {
            Target = target;
            InteractionKind = kind;
        }
    }
}
