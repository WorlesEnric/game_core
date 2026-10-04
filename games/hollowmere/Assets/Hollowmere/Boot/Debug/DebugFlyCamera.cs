// Hollowmere - debug fly camera (P1.1). REPLACED BY P1.3: throwaway debug control; P1.3 deletes Boot/Debug.
//   right mouse drag looks around, WASD moves, Q/E down/up, Shift is fast, F re-centres on the focus traveller's view.
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
}
