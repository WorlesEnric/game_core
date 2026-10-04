// GameCore.Gameplay.World - GameplayWorldBehaviour (P1.1).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Unity.Adapters;
using GameCore.Unity.App;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;
using UnityEngine;

namespace GameCore.Gameplay.World
{
    /// <summary>Holds the running gameplay world in a scene (portal triggers and debug tools find it here).</summary>
    public sealed class GameplayWorldBehaviour : MonoBehaviour
    {
        public GameplayWorld? World { get; set; }
    }
}
