#nullable enable
// Hollowmere.Mechanism.PressurePlate - presentation of placed plates (W-MECH-01 sample).
//
// The binder reads the committed plate.pressed slot of every bound plate after the host pump (P-045) and sinks the
// plate's view a little while it is pressed. It never writes authoritative state. Under batchmode with no graphics
// device it reports itself inactive and touches no engine object (headless-safe, like the P1.1 binders).
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hollowmere.Mechanism.PressurePlate
{
    /// <summary>Maps plate.pressed onto each bound plate view's local height.</summary>
    public sealed class PressurePlateBinder : IPresentationBinder
    {
        /// <summary>How far a pressed plate's view sinks (m).</summary>
        public const float PressedDepth = 0.04f;

        private readonly List<Binding> bindings = new List<Binding>();

        public PressurePlateBinder()
        {
            IsActive = !(Application.isBatchMode && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null);
        }

        public string BinderName => "hollowmere.pressureplate.binder";

        public bool IsActive { get; }

        public int BindingCount => bindings.Count;

        /// <summary>Binds a plate view; ignored while inactive (headless), so nothing engine-side is retained.</summary>
        public void Bind(TargetId plate, Transform view)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            if (!IsActive)
            {
                return;
            }

            bindings.Add(new Binding(plate, view, view.localPosition));
        }

        public int Present(ICommittedSlotReader slots)
        {
            if (!IsActive || slots == null)
            {
                return 0;
            }

            int touched = 0;
            for (int i = 0; i < bindings.Count; i++)
            {
                Binding binding = bindings[i];
                if (binding.View == null)
                {
                    continue;
                }

                bool pressed = slots.TryRead(binding.Plate, PressurePlateDeclarations.Owner, PressurePlateDeclarations.PressedSlot, out int value)
                    && value == 1;
                Vector3 rest = binding.Rest;
                binding.View.localPosition = pressed ? new Vector3(rest.x, rest.y - PressedDepth, rest.z) : rest;
                touched++;
            }

            return touched;
        }

        private sealed class Binding
        {
            internal Binding(TargetId plate, Transform view, Vector3 rest)
            {
                Plate = plate;
                View = view;
                Rest = rest;
            }

            internal TargetId Plate { get; }

            internal Transform View { get; }

            internal Vector3 Rest { get; }
        }
    }
}
