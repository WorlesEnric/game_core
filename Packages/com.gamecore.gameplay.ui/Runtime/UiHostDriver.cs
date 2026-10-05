// GameCore.Gameplay.Ui - UiHostDriver: runs the UI runtime's host actions between frames (P1.5).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Rules.Gameplay.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Gameplay.Ui
{
    /// <summary>Drives the runtime's host actions between frames.</summary>
    public sealed class UiHostDriver : MonoBehaviour
    {
        public UiRuntime? Runtime { get; set; }

        private void LateUpdate()
        {
            Runtime?.RunHostActions();
        }
    }
}
