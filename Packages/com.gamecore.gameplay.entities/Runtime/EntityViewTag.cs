// GameCore.Gameplay.Entities - EntityViewTag (P1.1).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameCore.Gameplay.Entities
{
    /// <summary>Tags a view object with the kernel target it presents.</summary>
    public sealed class EntityViewTag : MonoBehaviour
    {
        public TargetId Target { get; private set; }

        public string AuthoringId { get; private set; } = string.Empty;

        public void Bind(TargetId target, string authoringId)
        {
            Target = target;
            AuthoringId = authoringId;
        }
    }
}
