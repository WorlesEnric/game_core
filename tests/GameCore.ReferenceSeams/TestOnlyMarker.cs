// Test-only marker for the W0 compiled reference seam (GC-002).
// The assembly is a fixture oracle that freezes the shared compile-time surface of
// docs/game-core/05-contracts-and-data-model.md so Wave 1 peers can compile independently.
// It is not production code, is not shipped with a plugin, and contains no ECS/world runtime.
#nullable enable
using System;
using System.Reflection;

[assembly: GameCore.TestOnlyReferenceSeam("GC-002", "Wave 0 shared-contract reference seam and API snapshot")]
[assembly: AssemblyMetadata("GameCore.TestOnly", "true")]
[assembly: AssemblyMetadata("GameCore.ReferenceSeamTask", "GC-002")]
[assembly: AssemblyMetadata("GameCore.ReplacedBy", "GC-003 production GameCore.Contracts")]

namespace GameCore
{
    /// <summary>
    /// Marks an assembly as a test-only reference seam. The attribute is applied to this assembly so the
    /// orchestrator and later tasks can assert that the oracle never becomes a shipped dependency.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public sealed class TestOnlyReferenceSeamAttribute : Attribute
    {
        public TestOnlyReferenceSeamAttribute(string taskId, string purpose)
        {
            TaskId = taskId;
            Purpose = purpose;
        }

        public string TaskId { get; }

        public string Purpose { get; }
    }
}
