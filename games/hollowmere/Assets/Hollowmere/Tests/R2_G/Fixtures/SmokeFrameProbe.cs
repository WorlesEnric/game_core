#nullable enable
using UnityEngine;

namespace Hollowmere.R2_G.Fixtures
{
    public enum SmokeFrameStatus { Pending, Passed, Failed }

    /// <summary>Trusted test registration: smoke progresses only on the active world's Unity frames.</summary>
    public sealed class SmokeFrameProbe : MonoBehaviour
    {
        public object? Root { get; set; }
        public int Frames { get; private set; }
        public int Assertions { get; private set; }
        public int Begins { get; private set; }
        public int FailAt { get; set; }
        private int steps;
        private SmokeFrameStatus status = SmokeFrameStatus.Pending;

        public SmokeFrameStatus Poll(string type, string method, int requiredSteps)
        {
            if (type != "Trusted.GameSmoke" || method != "Begin" || requiredSteps != 4)
                return SmokeFrameStatus.Failed;
            if (Begins == 0) { Begins++; steps = requiredSteps; }
            return status;
        }

        private void LateUpdate()
        {
            if (Begins == 0 || status != SmokeFrameStatus.Pending) return;
            Frames++;
            if (!ReferenceEquals(Root, GameCore.Unity.App.GameApplication.Current) || Frames == FailAt)
            {
                status = SmokeFrameStatus.Failed;
                return;
            }
            Assertions++;
            if (Frames == steps) status = SmokeFrameStatus.Passed;
        }
    }
}
