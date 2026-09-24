#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Unity.Runtime;
using Unity.Entities;

namespace GameCore.Unity.Fixtures
{
    /// <summary>Which fixture world shape a registration builds (P-036: the temporal model is fixed at creation).</summary>
    public enum FixtureWorldShape
    {
        /// <summary>Command-driven: one logical step per admitted command or wake.</summary>
        CommandDriven = 0,

        /// <summary>Fixed-step: an unmanaged stage advanced by the fixed-step accumulator.</summary>
        FixedStep = 1,
    }

    /// <summary>
    /// Hand-written application composition root of the fixture world: a generated-style system registration table
    /// plus the three explicit dispatch tables (ingress, step, output). No reflection, no assembly scanning and no
    /// stage compiler: this is the tiny closed registration the W1 integration gate needs (04 s3, 04 s8).
    /// </summary>
    public static class FixtureRegistration
    {
        /// <summary>Shared stage index space of the three dispatch tables.</summary>
        public const int StageCount = 7;

        /// <summary>Definition identity of a command-driven fixture world.</summary>
        public static readonly WorldDefinitionId CommandWorldDefinition =
            new WorldDefinitionId(new Id128(FixtureKeys.Namespace, 0x3001UL));

        /// <summary>Definition identity of a fixed-step fixture world.</summary>
        public static readonly WorldDefinitionId FixedStepWorldDefinition =
            new WorldDefinitionId(new Id128(FixtureKeys.Namespace, 0x3002UL));

        /// <summary>100 ns host ticks per second, the rate the fixed-step configuration declares (P-036).</summary>
        public const ulong HostTicksPerSecond = 10_000_000UL;

        /// <summary>Fixed-step configuration of the fixture: 10 ms steps, at most four catch-up steps per pump.</summary>
        public static FixedStepSettings FixedStepConfiguration()
            => new FixedStepSettings(100_000UL, HostTicksPerSecond, 4U, usesUnscaledHostClock: true);

        /// <summary>Creation request of a command-driven fixture world (O-01, P-036).</summary>
        public static WorldCreateRequest CommandDrivenRequest(WorldId world, OperationId operation, ContentHash catalogHash)
            => new WorldCreateRequest(
                world,
                CommandWorldDefinition,
                TemporalModel.CommandDriven,
                PropagationMode.Automatic,
                catalogHash,
                operation,
                null);

        /// <summary>Creation request of a fixed-step fixture world (O-01, P-036).</summary>
        public static WorldCreateRequest FixedStepRequest(WorldId world, OperationId operation, ContentHash catalogHash)
            => new WorldCreateRequest(
                world,
                FixedStepWorldDefinition,
                TemporalModel.FixedStep,
                PropagationMode.Automatic,
                catalogHash,
                operation,
                FixedStepConfiguration());

        public const int IngressStageIndex = 0;
        public const int OutputStageIndex = 1;
        public const int AcceptStageIndex = 2;
        public const int SettleStageIndex = 3;
        public const int FaultStageIndex = 4;
        public const int ProjectStageIndex = 5;
        public const int TickStageIndex = 6;

        /// <summary>World name; the host appends the session id so every world name is an inspectable incarnation.</summary>
        public const string WorldName = "GameCoreFixtureWorld";

        /// <summary>Builds the fixture registration for one world shape.</summary>
        public static UnityWorldRegistration Create(FixtureWorldShape shape, bool includeFaultStage)
        {
            return new UnityWorldRegistration(
                WorldName,
                Stages(),
                Systems(),
                IngressPlan(),
                StepPlan(shape, includeFaultStage, declareCounterBuffer: shape == FixtureWorldShape.CommandDriven),
                OutputPlan(),
                world => FixtureWorldState.Seed(world));
        }

        /// <summary>
        /// Registration whose declared buffer has no consumer stage in the plan. Commit-time drain validation must
        /// reject it instead of publishing a step that produced reliable data nobody consumed (P-043, O-16).
        /// </summary>
        public static UnityWorldRegistration CreateDrainFaultWorld()
        {
            var entries = new List<GuardedDispatchEntry>
            {
                Entry(AcceptStageIndex, FixtureKeys.AcceptSystem, 0, null),
                Entry(SettleStageIndex, FixtureKeys.SettleSystem, 1, new[] { AcceptStageIndex }),
            };

            var binding = new BufferBinding(
                FixtureKeys.CounterBuffer,
                new[] { FixtureKeys.SettleSystem },
                FixtureKeys.ProjectStage);

            var plan = new GuardedDispatchPlan(entries, new[] { binding }, StageCount);

            return new UnityWorldRegistration(
                WorldName,
                Stages(),
                Systems(),
                IngressPlan(),
                plan,
                OutputPlan(),
                world => FixtureWorldState.Seed(world));
        }

        private static IReadOnlyList<StageRegistration> Stages()
        {
            return new List<StageRegistration>
            {
                new StageRegistration(FixtureKeys.IngressStage, "fixture.stage.ingress", IngressStageIndex, null),
                new StageRegistration(FixtureKeys.OutputStage, "fixture.stage.output", OutputStageIndex, null),
                new StageRegistration(FixtureKeys.AcceptStage, "fixture.stage.accept", AcceptStageIndex, null),
                new StageRegistration(FixtureKeys.SettleStage, "fixture.stage.settle", SettleStageIndex, new[] { AcceptStageIndex }),
                new StageRegistration(FixtureKeys.FaultStage, "fixture.stage.fault", FaultStageIndex, new[] { SettleStageIndex }),
                new StageRegistration(FixtureKeys.ProjectStage, "fixture.stage.project", ProjectStageIndex, new[] { FaultStageIndex }),
                new StageRegistration(FixtureKeys.TickStage, "fixture.stage.tick", TickStageIndex, null),
            };
        }

        private static IReadOnlyList<SystemRegistration> Systems()
        {
            return new List<SystemRegistration>
            {
                new ManagedSystemRegistration<FixtureIngressSystem>(FixtureKeys.IngressSystem, FixtureKeys.IngressStage, "FixtureIngressSystem"),
                new ManagedSystemRegistration<FixtureOutputSystem>(FixtureKeys.OutputSystem, FixtureKeys.OutputStage, "FixtureOutputSystem"),
                new ManagedSystemRegistration<FixtureAcceptSystem>(FixtureKeys.AcceptSystem, FixtureKeys.AcceptStage, "FixtureAcceptSystem"),
                new ManagedSystemRegistration<FixtureSettleSystem>(FixtureKeys.SettleSystem, FixtureKeys.SettleStage, "FixtureSettleSystem"),
                new ManagedSystemRegistration<FixtureFaultSystem>(FixtureKeys.FaultSystem, FixtureKeys.FaultStage, "FixtureFaultSystem"),
                new ManagedSystemRegistration<FixtureProjectSystem>(FixtureKeys.ProjectSystem, FixtureKeys.ProjectStage, "FixtureProjectSystem"),
                new UnmanagedSystemRegistration<FixtureTickSystem>(FixtureKeys.TickSystem, FixtureKeys.TickStage, "FixtureTickSystem"),
            };
        }

        private static GuardedDispatchPlan IngressPlan()
        {
            var entries = new List<GuardedDispatchEntry>
            {
                Entry(IngressStageIndex, FixtureKeys.IngressSystem, 0, null),
            };

            return new GuardedDispatchPlan(entries, null, StageCount);
        }

        private static GuardedDispatchPlan OutputPlan()
        {
            var entries = new List<GuardedDispatchEntry>
            {
                Entry(OutputStageIndex, FixtureKeys.OutputSystem, 0, null),
            };

            return new GuardedDispatchPlan(entries, null, StageCount);
        }

        private static GuardedDispatchPlan StepPlan(FixtureWorldShape shape, bool includeFaultStage, bool declareCounterBuffer)
        {
            var entries = new List<GuardedDispatchEntry>();

            if (shape == FixtureWorldShape.FixedStep)
            {
                entries.Add(Entry(TickStageIndex, FixtureKeys.TickSystem, 0, null));
                return new GuardedDispatchPlan(entries, null, StageCount);
            }

            entries.Add(Entry(AcceptStageIndex, FixtureKeys.AcceptSystem, 0, null));
            entries.Add(Entry(SettleStageIndex, FixtureKeys.SettleSystem, 1, new[] { AcceptStageIndex }));

            int projectIndex = 2;
            int projectPredecessor = SettleStageIndex;
            if (includeFaultStage)
            {
                entries.Add(Entry(FaultStageIndex, FixtureKeys.FaultSystem, 2, new[] { SettleStageIndex }));
                projectIndex = 3;
                projectPredecessor = FaultStageIndex;
            }

            entries.Add(Entry(ProjectStageIndex, FixtureKeys.ProjectSystem, projectIndex, new[] { projectPredecessor }));

            var bindings = new List<BufferBinding>();
            if (declareCounterBuffer)
            {
                bindings.Add(new BufferBinding(
                    FixtureKeys.CounterBuffer,
                    new[] { FixtureKeys.SettleSystem },
                    FixtureKeys.ProjectStage));
            }

            return new GuardedDispatchPlan(entries, bindings, StageCount);
        }

        private static GuardedDispatchEntry Entry(
            int stageIndex,
            FactoryKey systemKey,
            int dispatchIndex,
            IReadOnlyList<int>? predecessorStages)
        {
            if (stageIndex < 0 || stageIndex >= StageCount)
            {
                throw new ArgumentOutOfRangeException(nameof(stageIndex));
            }

            StageId stage = StageOf(stageIndex);
            SystemDispatchKind kind = stageIndex == TickStageIndex
                ? SystemDispatchKind.UnmanagedSystem
                : SystemDispatchKind.ManagedSystem;

            return new GuardedDispatchEntry(stage, systemKey, kind, dispatchIndex, stageIndex, predecessorStages);
        }

        private static StageId StageOf(int stageIndex)
        {
            switch (stageIndex)
            {
                case IngressStageIndex:
                    return FixtureKeys.IngressStage;
                case OutputStageIndex:
                    return FixtureKeys.OutputStage;
                case AcceptStageIndex:
                    return FixtureKeys.AcceptStage;
                case SettleStageIndex:
                    return FixtureKeys.SettleStage;
                case FaultStageIndex:
                    return FixtureKeys.FaultStage;
                case ProjectStageIndex:
                    return FixtureKeys.ProjectStage;
                case TickStageIndex:
                    return FixtureKeys.TickStage;
                default:
                    throw new ArgumentOutOfRangeException(nameof(stageIndex));
            }
        }
    }
}
