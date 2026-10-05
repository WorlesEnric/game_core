// Hollowmere - scripted player intents (P3.1): the autoplay runner and the PlayMode tests steer the player through the
// same input adapter the keyboard drives, so a scripted run exercises the real move/interact path (pause gating, focus,
// portals) rather than writing slots. While no script holds it, the source passes the wrapped (keyboard) source through.
#nullable enable
using GameCore.Gameplay.Player;
using UnityEngine;

namespace Hollowmere.Game
{
    /// <summary>An override of the player's intent source: world-space moves, facing nudges and one-frame interacts.</summary>
    public sealed class AutoplayIntentSource : IPlayerIntentSource
    {
        private Vector3 move;
        private bool run;
        private bool interact;
        private float? facing;

        public AutoplayIntentSource(IPlayerIntentSource inner)
        {
            Inner = inner;
        }

        /// <summary>The source the override wraps (keyboard / gamepad behind the pause gate).</summary>
        public IPlayerIntentSource Inner { get; }

        /// <summary>True while a script steers (inner input is ignored).</summary>
        public bool Scripted { get; set; }

        /// <summary>Camera or player heading in degrees used to turn a world direction into a stick vector.</summary>
        public System.Func<float>? Heading { get; set; }

        /// <summary>True while a modal screen pauses gameplay (scripted moves and interacts are dropped like keyboard ones).</summary>
        public System.Func<bool>? Paused { get; set; }

        public int Interacts { get; private set; }

        /// <summary>Moves along a world-space direction (zero stops).</summary>
        public void SetMove(Vector3 worldDirection, bool running)
        {
            Scripted = true;
            move = new Vector3(worldDirection.x, 0f, worldDirection.z);
            run = running;
        }

        /// <summary>Turns toward a heading (one short step along it on the next frame).</summary>
        public void SetFacing(float yawDegrees)
        {
            Scripted = true;
            facing = yawDegrees;
        }

        /// <summary>Presses interact on the next frame.</summary>
        public void PressInteract()
        {
            Scripted = true;
            interact = true;
        }

        public void Release()
        {
            Scripted = false;
            move = Vector3.zero;
            run = false;
            interact = false;
            facing = null;
        }

        public PlayerIntent Sample()
        {
            PlayerIntent intent = Inner.Sample();
            if (!Scripted)
            {
                return intent;
            }

            if (Paused != null && Paused())
            {
                intent.Move = Vector2.zero;
                intent.Look = Vector2.zero;
                intent.Run = false;
                intent.Jump = false;
                intent.Interact = false;
                return intent;
            }

            Vector3 direction = move;
            if (facing.HasValue)
            {
                float yaw = facing.Value * Mathf.Deg2Rad;
                direction = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * 0.05f;
                facing = null;
            }

            float heading = (Heading?.Invoke() ?? 0f) * Mathf.Deg2Rad;
            var forward = new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading));
            var right = new Vector3(Mathf.Cos(heading), 0f, -Mathf.Sin(heading));
            Vector3 planar = direction.sqrMagnitude > 1f ? direction.normalized : direction;
            intent.Move = new Vector2(Vector3.Dot(planar, right), Vector3.Dot(planar, forward));
            intent.Run = run;
            intent.Look = Vector2.zero;
            intent.Jump = false;
            intent.Interact = interact;
            if (interact)
            {
                Interacts++;
            }

            interact = false;
            return intent;
        }
    }
}
