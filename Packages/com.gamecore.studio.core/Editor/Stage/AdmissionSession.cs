#nullable enable
using System;
using System.Runtime.CompilerServices;
using UnityEditor;

namespace GameCore.Studio.Edit
{
    internal sealed class AdmissionSession : ScriptableSingleton<AdmissionSession>
    {
        [NonSerialized] internal bool ResumeScheduled;
        [NonSerialized] internal bool Resuming;
        [NonSerialized] internal readonly ConditionalWeakTable<StudioRuntime, StageAdmission> Instances = new ConditionalWeakTable<StudioRuntime, StageAdmission>();
    }

    /// <summary>Test-only simulated process loss: leave the durable checkpoint exactly as a crash would.</summary>
    public sealed class AdmissionCrashException : Exception
    {
        public AdmissionCrashException() : base("simulated admission process loss") { }
    }
}
