// Hollowmere - the per-frame callback of an admitted mechanism's live smoke (R2-G2's P3.1 binding).
//
// The trusted Editor integration (Hollowmere.Authoring.Editor, HollowmereStudioAdmission) adds this component to the
// GameBoot object in Play Mode and subscribes its smoke registry to Frame: a registered smoke entry advances one step
// per normal game frame (LateUpdate, after the world's own pump), never through the admission's poll. Nothing adds the
// component in a player build.
#nullable enable
using System;
using UnityEngine;

namespace Hollowmere.Boot
{
    /// <summary>Raises <see cref="Frame"/> once per game frame for the Editor's admission smoke registry.</summary>
    [DisallowMultipleComponent]
    public sealed class AdmissionSmokeFrames : MonoBehaviour
    {
        /// <summary>One normal game frame has completed (LateUpdate).</summary>
        public event Action? Frame;

        /// <summary>Frames raised so far.</summary>
        public int Frames { get; private set; }

        private void LateUpdate()
        {
            Frames++;
            Frame?.Invoke();
        }
    }
}
