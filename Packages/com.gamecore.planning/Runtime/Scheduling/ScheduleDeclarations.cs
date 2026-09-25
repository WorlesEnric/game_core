// GameCore.Planning - semantic schedule compilation (GC-009).
// Normative sources: docs/game-core/03-runtime-and-execution.md section 3, docs/game-core/04-unity-integration.md
// section 4, docs/game-core/00-core-protocols.md P-008 and P-039..P-043.
// Unity-free: BCL subset only, no UnityEngine/Unity.* reference (01 section 1).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Planning.Scheduling
{
    /// <summary>
    /// The immutable declaration set one schedule is compiled from: the active stage/system declarations plus the
    /// buffer contracts they reference (P-039 to P-043, 03 section 3). Both lists are catalog output; the compiler
    /// adds no stage of its own and requires no domain stage to exist (P-001, P-059).
    /// </summary>
    public sealed class ScheduleDeclarations
    {
        public ScheduleDeclarations(IReadOnlyList<StageSpec>? stages, IReadOnlyList<BufferSpec>? buffers)
        {
            Stages = ContractCollections.Freeze(stages);
            Buffers = ContractCollections.Freeze(buffers);
        }

        /// <summary>An empty declaration set compiles to an empty, valid schedule: the kernel mandates no stage.</summary>
        public static ScheduleDeclarations Empty { get; } = new ScheduleDeclarations(null, null);

        public IReadOnlyList<StageSpec> Stages { get; }

        public IReadOnlyList<BufferSpec> Buffers { get; }

        public override string ToString()
        {
            return "declarations(stages=" + Stages.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ", buffers=" + Buffers.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        }
    }
}
