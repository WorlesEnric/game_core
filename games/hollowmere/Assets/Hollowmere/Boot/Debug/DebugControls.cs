// Hollowmere - debug fly camera and keyboard travel (P1.1).
//
// REPLACED BY P1.3: these are throwaway debug controls for walking the three-region loop before the player controller
// exists. P1.3 deletes this folder and drives travel through the player controller and portal triggers.
//
//   DebugFlyCamera   right mouse drag looks around, WASD moves, Q/E down/up, Shift is fast; F re-centres on the
//                    focus traveller's view.
//   DebugTravelKeys  1/2/3 send world.travel for the focus traveller to the 1st/2nd/3rd region of the world (in the
//                    manifest's canonical order); P toggles neighbour preloading.
#nullable enable
using GameCore.Contracts;
using GameCore.Gameplay.World;
using Hollowmere.Boot;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hollowmere.Boot.Debug
{
    /// <summary>A free-fly debug camera (replaced by P1.3).</summary>
    public sealed class DebugFlyCamera : MonoBehaviour
    {
        [SerializeField] private float speed = 12f;
        [SerializeField] private float lookDegreesPerPixel = 0.15f;
        [SerializeField] private GameBoot? boot;

        private float yaw;
        private float pitch;

        public void Configure(GameBoot gameBoot) => boot = gameBoot;

        private void OnEnable()
        {
            Vector3 angles = transform.eulerAngles;
            yaw = angles.y;
            pitch = angles.x > 180f ? angles.x - 360f : angles.x;
        }

        private void Update()
        {
            Keyboard? keyboard = Keyboard.current;
            Mouse? mouse = Mouse.current;
            if (keyboard == null)
            {
                return;
            }

            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                yaw += delta.x * lookDegreesPerPixel;
                pitch = Mathf.Clamp(pitch - delta.y * lookDegreesPerPixel, -89f, 89f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }

            Vector3 move = Vector3.zero;
            if (keyboard.wKey.isPressed)
            {
                move += transform.forward;
            }

            if (keyboard.sKey.isPressed)
            {
                move -= transform.forward;
            }

            if (keyboard.dKey.isPressed)
            {
                move += transform.right;
            }

            if (keyboard.aKey.isPressed)
            {
                move -= transform.right;
            }

            if (keyboard.eKey.isPressed)
            {
                move += Vector3.up;
            }

            if (keyboard.qKey.isPressed)
            {
                move -= Vector3.up;
            }

            float factor = keyboard.leftShiftKey.isPressed ? 4f : 1f;
            transform.position += move * (speed * factor * Time.unscaledDeltaTime);

            if (keyboard.fKey.wasPressedThisFrame)
            {
                FocusTraveller();
            }
        }

        private void FocusTraveller()
        {
            GameplayWorld? world = boot != null ? boot.World : null;
            if (world == null || world.Views == null || world.Focus.IsDefault)
            {
                return;
            }

            if (world.Views.TryGetView(world.Focus, out GameObject? view) && view != null)
            {
                transform.position = view.transform.position + new Vector3(0f, 6f, -10f);
                transform.LookAt(view.transform.position);
                OnEnable();
            }
        }
    }

    /// <summary>Keyboard travel for the focus traveller (replaced by P1.3).</summary>
    public sealed class DebugTravelKeys : MonoBehaviour
    {
        [SerializeField] private GameBoot? boot;

        public int Requests { get; private set; }

        public void Configure(GameBoot gameBoot) => boot = gameBoot;

        private void Update()
        {
            Keyboard? keyboard = Keyboard.current;
            GameplayWorld? world = boot != null ? boot.World : null;
            if (keyboard == null || world == null || world.Focus.IsDefault)
            {
                return;
            }

            if (keyboard.pKey.wasPressedThisFrame)
            {
                world.Streamer.PreloadNeighbours = !world.Streamer.PreloadNeighbours;
            }

            int index = keyboard.digit1Key.wasPressedThisFrame ? 0
                : keyboard.digit2Key.wasPressedThisFrame ? 1
                : keyboard.digit3Key.wasPressedThisFrame ? 2
                : -1;
            if (index < 0 || index >= world.Worlds.Regions.Count)
            {
                return;
            }

            Requests++;
            CommandAdmissionReceipt receipt = world.Commands.Travel(world.Focus, world.Worlds.Regions[index].AuthoringId);
            UnityEngine.Debug.Log("[Hollowmere/debug] travel to " + world.Worlds.Regions[index].Name + ": admitted=" + receipt.Admitted);
        }
    }
}
