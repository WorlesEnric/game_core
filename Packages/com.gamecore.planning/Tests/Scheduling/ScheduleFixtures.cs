// GameCore.Planning tests - scheduling fixtures (GC-009).
//
// Frozen declaration fixtures for the schedule compiler. They are intentionally domain-neutral: a couple of
// card-like and narrative-like stage names, no combat/physics/animation stage and no dependency on another Wave-2
// task's ownership or stage descriptors (the W2 gate integrates the real ones).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning.Scheduling;

namespace GameCore.Planning.Tests.Scheduling
{
    /// <summary>Hand-built stage/buffer declarations for the schedule-compiler tests.</summary>
    internal static class ScheduleFixtures
    {
        internal const ulong Namespace = 0x4743505343484544UL;

        internal static readonly Id128 OwnerPackage = new Id128(0x4743504C414E504BUL, 1UL);

        internal static StageId Stage(ulong ordinal) => new StageId(new Id128(Namespace, 0x1000UL + ordinal));

        internal static FactoryKey Key(ulong ordinal) => new FactoryKey(new Id128(Namespace, 0x2000UL + ordinal), 1U);

        internal static SchemaRef Schema(ulong ordinal) => new SchemaRef(new SchemaId(new Id128(Namespace, 0x3000UL + ordinal)), 1U);

        internal static Id128 Partition(ulong ordinal) => new Id128(Namespace, 0x4000UL + ordinal);

        internal static BufferId Buffer(ulong ordinal) => new BufferId(new Id128(Namespace, 0x5000UL + ordinal));

        internal static AccessDeclaration Access(SchemaRef schema, AccessMode mode)
            => new AccessDeclaration(schema, mode, default(Id128));

        internal static AccessDeclaration Partitioned(SchemaRef schema, AccessMode mode, Id128 partition)
            => new AccessDeclaration(schema, mode, partition);

        internal static AccessSet AccessSetOf(params AccessDeclaration[] declarations)
            => new AccessSet(declarations);

        internal static SystemSpec System(
            FactoryKey key,
            AccessSet? access = null,
            SystemMultiplicity multiplicity = SystemMultiplicity.World,
            IReadOnlyList<FactoryKey>? requiredBefore = null,
            IReadOnlyList<FactoryKey>? requiredAfter = null,
            IReadOnlyList<FactoryKey>? optionalBefore = null,
            IReadOnlyList<FactoryKey>? optionalAfter = null)
            => new SystemSpec(
                key,
                multiplicity,
                access ?? new AccessSet(null),
                requiredBefore,
                requiredAfter,
                optionalBefore,
                optionalAfter);

        internal static StageSpec StageSpec(
            StageId stage,
            IReadOnlyList<SystemSpec>? systems,
            AccessSet? access = null,
            IReadOnlyList<StageId>? requiredBefore = null,
            IReadOnlyList<StageId>? requiredAfter = null,
            IReadOnlyList<StageId>? optionalBefore = null,
            IReadOnlyList<StageId>? optionalAfter = null,
            IReadOnlyList<BufferPort>? ports = null,
            uint version = 1U,
            HostAffinity affinity = HostAffinity.ManagedMain,
            Id128 ownerPackage = default(Id128))
            => new StageSpec(
                stage,
                version,
                ownerPackage.IsDefault ? OwnerPackage : ownerPackage,
                affinity,
                null,
                null,
                access ?? new AccessSet(null),
                requiredBefore,
                requiredAfter,
                optionalBefore,
                optionalAfter,
                systems,
                ports);

        internal static BufferSpec BufferSpec(
            BufferId buffer,
            IReadOnlyList<FactoryKey> producers,
            StageId ownerStage,
            StageId consumerStage,
            BufferLifetime lifetime = BufferLifetime.Stage,
            int capacity = 16,
            FactoryKey orderKey = default(FactoryKey),
            SchemaRef schema = default(SchemaRef))
            => new BufferSpec(
                buffer,
                schema.Id.IsDefault ? Schema(9UL) : schema,
                producers,
                ownerStage,
                consumerStage,
                orderKey,
                lifetime,
                capacity,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);

        /// <summary>
        /// The frozen Wave-2 shaped fixture: an ingress stage, a card-like accept/settle pair with a producer to
        /// consumer buffer edge, a narrative-like evaluate stage reading the settle result, and an unrelated
        /// project stage. No combat, physics or animation stage exists (P-001, P-059).
        /// </summary>
        internal static ScheduleDeclarations CardAndNarrative()
        {
            StageId accept = Stage(1UL);
            StageId settle = Stage(2UL);
            StageId evaluate = Stage(3UL);
            StageId project = Stage(4UL);

            FactoryKey acceptSystem = Key(1UL);
            FactoryKey settleSystem = Key(2UL);
            FactoryKey evaluateSystem = Key(3UL);
            FactoryKey projectSystem = Key(4UL);

            SchemaRef selection = Schema(1UL);
            SchemaRef receipt = Schema(2UL);
            SchemaRef projection = Schema(3UL);
            BufferId receiptBuffer = Buffer(1UL);

            var stages = new List<StageSpec>
            {
                StageSpec(
                    accept,
                    new[]
                    {
                        System(
                            acceptSystem,
                            AccessSetOf(Access(selection, AccessMode.ReadWrite))),
                    }),
                StageSpec(
                    settle,
                    new[]
                    {
                        System(
                            settleSystem,
                            AccessSetOf(Access(receipt, AccessMode.Write))),
                    },
                    requiredAfter: new[] { accept },
                    ports: new[] { new BufferPort(receiptBuffer, PortDirection.Producer, settle) }),
                StageSpec(
                    evaluate,
                    new[]
                    {
                        System(
                            evaluateSystem,
                            AccessSetOf(Access(receipt, AccessMode.Read), Access(selection, AccessMode.Read))),
                    },
                    requiredAfter: new[] { settle },
                    ports: new[] { new BufferPort(receiptBuffer, PortDirection.Consumer, evaluate) }),
                StageSpec(
                    project,
                    new[]
                    {
                        System(
                            projectSystem,
                            AccessSetOf(Access(projection, AccessMode.Write))),
                    }),
            };

            var buffers = new List<BufferSpec>
            {
                BufferSpec(receiptBuffer, new[] { settleSystem }, settle, evaluate, orderKey: Key(90UL)),
            };

            return new ScheduleDeclarations(stages, buffers);
        }

        /// <summary>Reverses the declaration order of the fixture without changing any declaration's content.</summary>
        internal static ScheduleDeclarations Reversed(ScheduleDeclarations source)
        {
            var stages = new List<StageSpec>(source.Stages);
            stages.Reverse();
            var buffers = new List<BufferSpec>(source.Buffers);
            buffers.Reverse();
            return new ScheduleDeclarations(stages, buffers);
        }
    }
}
