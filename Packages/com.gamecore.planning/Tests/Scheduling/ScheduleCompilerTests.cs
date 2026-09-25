// GameCore.Planning tests - semantic schedule compilation (GC-009, TEST-012 and TEST-022 subsets).
//
// Every case is a pure, deterministic assertion over the compiler: no Unity world, no clock, no wall time and no
// second ECS facade. The measurable claims are the ones 08 makes for this area: schedule compilation either
// produces a stable valid dependency graph or a diagnostic witness before publication (TEST-012); ordering and
// hashing are canonical under shuffled declaration order (TEST-022, P-008); and no schedule requires a combat,
// physics or animation stage (P-001, P-059).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning.Scheduling;
using GameCore.Planning.Tests.Scheduling;
using NUnit.Framework;

namespace GameCore.Planning.Scheduling.Tests
{
    [TestFixture]
    public sealed class ScheduleCompilerTests
    {
        private static CompiledSchedule Compile(ScheduleDeclarations declarations)
        {
            ScheduleCompilation compilation = ScheduleCompiler.Compile(declarations);
            Assert.That(compilation.Succeeded, Is.True, compilation.Explain());
            Assert.That(compilation.Schedule, Is.Not.Null);
            Assert.That(compilation.Witnesses.Count, Is.EqualTo(0), "a successful compilation carries no witnesses");
            return compilation.Schedule!;
        }

        /// <summary>Compiles a declaration set that must reject, and returns the compilation for inspection.</summary>
        private static ScheduleCompilation Rejected(ScheduleDeclarations declarations)
        {
            ScheduleCompilation compilation = ScheduleCompiler.Compile(declarations);
            Assert.That(compilation.Succeeded, Is.False, "this declaration set must be rejected");
            Assert.That(compilation.Schedule, Is.Null, "a rejected compilation never exposes a partial schedule");
            Assert.That(compilation.Witnesses.Count, Is.GreaterThan(0), "a rejection carries edge witnesses");
            for (int i = 0; i < compilation.Witnesses.Count; i++)
            {
                Assert.That(compilation.Witnesses[i].Detail, Is.Not.Empty);
            }

            return compilation;
        }

        /// <summary>Rejects, asserts the primary diagnostic code, and asserts the expected witness kind is present.</summary>
        private static ScheduleCompilation Rejected(
            ScheduleDeclarations declarations,
            DiagnosticCode code,
            ScheduleWitnessKind kind)
        {
            ScheduleCompilation compilation = Rejected(declarations);
            Assert.That(compilation.Code, Is.EqualTo(code), compilation.Explain());
            Assert.That(compilation.Witnesses[0].Code, Is.EqualTo(code));
            Assert.That(compilation.Witnesses[0].Kind, Is.EqualTo(kind),
                "the canonically first witness is the one the code names" + Environment.NewLine + compilation.Explain());
            return compilation;
        }

        private static IReadOnlyList<StageId> Order(CompiledSchedule schedule)
        {
            var order = new List<StageId>(schedule.Stages.Count);
            for (int i = 0; i < schedule.Stages.Count; i++)
            {
                order.Add(schedule.Stages[i].Stage);
            }

            return order;
        }

        private static List<StageSpec> Reverse(List<StageSpec> stages)
        {
            var copy = new List<StageSpec>(stages);
            copy.Reverse();
            return copy;
        }

        // ------------------------------------------------------------------ ordering

        [Test]
        public void DeclaredEdgesAndBufferEdgesProduceOneStableTopologicalOrder()
        {
            CompiledSchedule schedule = Compile(ScheduleFixtures.CardAndNarrative());

            Assert.That(schedule.Stages.Count, Is.EqualTo(4));
            Assert.That(Order(schedule), Is.EqualTo(new[]
            {
                ScheduleFixtures.Stage(1UL),
                ScheduleFixtures.Stage(2UL),
                ScheduleFixtures.Stage(3UL),
                ScheduleFixtures.Stage(4UL),
            }), "the declared and buffer edges fix this order exactly");

            for (int i = 0; i < schedule.Stages.Count; i++)
            {
                Assert.That(schedule.Stages[i].StageIndex, Is.EqualTo(i),
                    "the fence index equals the position in canonical order");
            }

            for (int e = 0; e < schedule.Edges.Count; e++)
            {
                Assert.That(schedule.Edges[e].FromIndex, Is.LessThan(schedule.Edges[e].ToIndex),
                    "every stage edge points backward in the compiled order (P-040, P-041)");
            }

            Assert.That(schedule.PlaybackPoints.Count, Is.EqualTo(1));
            SchedulePlaybackPoint playback = schedule.PlaybackPoints[0];
            Assert.That(playback.Buffer, Is.EqualTo(ScheduleFixtures.Buffer(1UL)));
            Assert.That(playback.OwnerStage, Is.EqualTo(ScheduleFixtures.Stage(2UL)));
            Assert.That(playback.ConsumerStage, Is.EqualTo(ScheduleFixtures.Stage(3UL)));
            Assert.That(playback.OwnerStageIndex, Is.EqualTo(1));
            Assert.That(playback.ConsumerStageIndex, Is.EqualTo(2));
            Assert.That(playback.ProducerStageIndexes, Is.EqualTo(new[] { 1 }));
            Assert.That(playback.OrderKey, Is.EqualTo(ScheduleFixtures.Key(90UL)), "the declared order key is kept");
            Assert.That(playback.IsStageLocal, Is.False);
            Assert.That(schedule.BufferBindings.Count, Is.EqualTo(1));
        }

        [Test]
        public void ShuffledDeclarationOrderCompilesToTheSameOrderAndHash()
        {
            ScheduleDeclarations forward = ScheduleFixtures.CardAndNarrative();
            ScheduleDeclarations reversed = ScheduleFixtures.Reversed(forward);

            CompiledSchedule first = Compile(forward);
            CompiledSchedule second = Compile(reversed);

            Assert.That(Order(second), Is.EqualTo(Order(first)), "declaration order is not an ordering input (P-008)");
            Assert.That(second.Hash, Is.EqualTo(first.Hash), "the plan hash covers semantic inputs only");
            Assert.That(second.Describe(), Is.EqualTo(first.Describe()));

            Assert.That(second.Entries.Count, Is.EqualTo(first.Entries.Count));
            for (int i = 0; i < first.Entries.Count; i++)
            {
                Assert.That(second.Entries[i].SystemKey, Is.EqualTo(first.Entries[i].SystemKey));
                Assert.That(second.Entries[i].DispatchIndex, Is.EqualTo(first.Entries[i].DispatchIndex));
                Assert.That(second.Entries[i].StageIndex, Is.EqualTo(first.Entries[i].StageIndex));
            }

            Assert.That(second.PlaybackPoints[0].OrderKey, Is.EqualTo(first.PlaybackPoints[0].OrderKey));
            Assert.That(second.BufferBindings[0].Producers, Is.EqualTo(first.BufferBindings[0].Producers));
        }

        [Test]
        public void IndependentStagesAreOrderedByCanonicalStageIdBytes()
        {
            StageId low = ScheduleFixtures.Stage(1UL);
            StageId middle = ScheduleFixtures.Stage(2UL);
            StageId high = ScheduleFixtures.Stage(3UL);

            var stages = new List<StageSpec>
            {
                ScheduleFixtures.StageSpec(high, new[] { ScheduleFixtures.System(ScheduleFixtures.Key(3UL)) }),
                ScheduleFixtures.StageSpec(low, new[] { ScheduleFixtures.System(ScheduleFixtures.Key(1UL)) }),
                ScheduleFixtures.StageSpec(middle, new[] { ScheduleFixtures.System(ScheduleFixtures.Key(2UL)) }),
            };

            CompiledSchedule schedule = Compile(new ScheduleDeclarations(stages, null));

            Assert.That(Order(schedule), Is.EqualTo(new[] { low, middle, high }));
            Assert.That(schedule.Hash, Is.EqualTo(Compile(new ScheduleDeclarations(Reverse(stages), null)).Hash));
        }

        [Test]
        public void IndependentSystemsInsideOneStageAreOrderedByCanonicalKeyBytes()
        {
            StageId stage = ScheduleFixtures.Stage(1UL);
            FactoryKey first = ScheduleFixtures.Key(1UL);
            FactoryKey second = ScheduleFixtures.Key(2UL);
            FactoryKey third = ScheduleFixtures.Key(3UL);

            var keys = new[] { third, first, second };
            var specs = new List<SystemSpec>();
            for (int i = 0; i < keys.Length; i++)
            {
                specs.Add(ScheduleFixtures.System(keys[i]));
            }

            CompiledSchedule schedule = Compile(new ScheduleDeclarations(
                new[] { ScheduleFixtures.StageSpec(stage, specs) },
                null));

            Assert.That(schedule.Entries.Count, Is.EqualTo(3));
            Assert.That(schedule.Entries[0].SystemKey, Is.EqualTo(first));
            Assert.That(schedule.Entries[1].SystemKey, Is.EqualTo(second));
            Assert.That(schedule.Entries[2].SystemKey, Is.EqualTo(third));
            Assert.That(schedule.Entries[0].PredecessorStages.Count, Is.EqualTo(0));
            Assert.That(schedule.Entries[1].PredecessorSystems.Count, Is.EqualTo(0),
                "a canonical tie-break is not a declared dependency");
        }

        // ------------------------------------------------------------------ declared edges

        [Test]
        public void OptionalStageEdgesDisappearWhileRequiredOnesRejectWhenAbsent()
        {
            StageId present = ScheduleFixtures.Stage(1UL);
            StageId absent = ScheduleFixtures.Stage(9UL);
            var systems = new[] { ScheduleFixtures.System(ScheduleFixtures.Key(1UL)) };

            StageSpec optional = ScheduleFixtures.StageSpec(present, systems, optionalBefore: new[] { absent });
            CompiledSchedule schedule = Compile(new ScheduleDeclarations(new[] { optional }, null));
            Assert.That(schedule.Stages.Count, Is.EqualTo(1));
            Assert.That(schedule.Edges.Count, Is.EqualTo(0), "an optional edge to an absent stage disappears (P-039)");

            StageSpec required = ScheduleFixtures.StageSpec(present, systems, requiredBefore: new[] { absent });
            ScheduleCompilation rejected = Rejected(
                new ScheduleDeclarations(new[] { required }, null),
                DiagnosticCode.MissingDependency,
                ScheduleWitnessKind.RequiredStageEdgeMissing);
            Assert.That(rejected.Witnesses[0].Stage, Is.EqualTo(present));
            Assert.That(rejected.Witnesses[0].RelatedStage, Is.EqualTo(absent));
        }

        [Test]
        public void RequiredInnerSystemEdgesMustNameASystemOfThatStage()
        {
            StageId stage = ScheduleFixtures.Stage(1UL);
            SystemSpec spec = ScheduleFixtures.System(
                ScheduleFixtures.Key(1UL),
                requiredBefore: new[] { ScheduleFixtures.Key(2UL) });

            ScheduleCompilation rejected = Rejected(
                new ScheduleDeclarations(new[] { ScheduleFixtures.StageSpec(stage, new[] { spec }) }, null),
                DiagnosticCode.MissingDependency,
                ScheduleWitnessKind.RequiredSystemEdgeMissing);

            Assert.That(rejected.Witnesses[0].Stage, Is.EqualTo(stage));
            Assert.That(rejected.Witnesses[0].System, Is.EqualTo(ScheduleFixtures.Key(1UL)));
            Assert.That(rejected.Witnesses[0].RelatedSystem, Is.EqualTo(ScheduleFixtures.Key(2UL)));
        }

        [Test]
        public void AnExplicitInnerEdgeOrdersTwoOverlappingSystems()
        {
            StageId stage = ScheduleFixtures.Stage(1UL);
            SchemaRef schema = ScheduleFixtures.Schema(1UL);
            FactoryKey writer = ScheduleFixtures.Key(1UL);
            FactoryKey reader = ScheduleFixtures.Key(2UL);

            var systems = new[]
            {
                ScheduleFixtures.System(
                    reader,
                    ScheduleFixtures.AccessSetOf(ScheduleFixtures.Access(schema, AccessMode.Read)),
                    requiredAfter: new[] { writer }),
                ScheduleFixtures.System(writer, ScheduleFixtures.AccessSetOf(
                    ScheduleFixtures.Access(schema, AccessMode.Write))),
            };

            CompiledSchedule schedule = Compile(
                new ScheduleDeclarations(new[] { ScheduleFixtures.StageSpec(stage, systems) }, null));

            Assert.That(schedule.Entries.Count, Is.EqualTo(2));
            Assert.That(schedule.Entries[0].SystemKey, Is.EqualTo(writer));
            Assert.That(schedule.Entries[1].SystemKey, Is.EqualTo(reader));
            Assert.That(schedule.Entries[1].PredecessorSystems, Is.EqualTo(new[] { writer }));
        }

        // ------------------------------------------------------------------ cycles

        [Test]
        public void StageCycleIsRejectedWithItsPath()
        {
            StageId first = ScheduleFixtures.Stage(1UL);
            StageId second = ScheduleFixtures.Stage(2UL);
            StageId third = ScheduleFixtures.Stage(3UL);

            var stages = new List<StageSpec>
            {
                ScheduleFixtures.StageSpec(
                    first,
                    new[] { ScheduleFixtures.System(ScheduleFixtures.Key(1UL)) },
                    requiredAfter: new[] { second }),
                ScheduleFixtures.StageSpec(
                    second,
                    new[] { ScheduleFixtures.System(ScheduleFixtures.Key(2UL)) },
                    requiredAfter: new[] { third }),
                ScheduleFixtures.StageSpec(
                    third,
                    new[] { ScheduleFixtures.System(ScheduleFixtures.Key(3UL)) },
                    requiredAfter: new[] { first }),
            };

            ScheduleCompilation rejected = Rejected(
                new ScheduleDeclarations(stages, null),
                DiagnosticCode.Cycle,
                ScheduleWitnessKind.StageCycle);

            IReadOnlyList<StageId> path = rejected.CyclePath;
            Assert.That(path.Count, Is.EqualTo(4), rejected.Explain());
            Assert.That(path[0], Is.EqualTo(path[path.Count - 1]), "a cycle path starts and ends at the same stage");
            Assert.That(path, Does.Contain(first));
            Assert.That(path, Does.Contain(second));
            Assert.That(path, Does.Contain(third));

            // Every consecutive pair of the witness path is really a declared edge of the declaration set. A pair
            // (X, Y) is the edge X -> Y, and a declaration states it as "Y requires X before it", i.e. X appears in
            // Y.RequiredAfter; X.RequiredBefore is the same statement written the other way round.
            for (int i = 0; i < path.Count - 1; i++)
            {
                StageId from = path[i];
                StageId to = path[i + 1];
                bool declared = false;
                for (int s = 0; s < stages.Count; s++)
                {
                    if (!stages[s].StageId.Equals(to))
                    {
                        continue;
                    }

                    for (int a = 0; a < stages[s].RequiredAfter.Count; a++)
                    {
                        if (stages[s].RequiredAfter[a].Equals(from))
                        {
                            declared = true;
                        }
                    }
                }

                Assert.That(declared, Is.True,
                    "path edge " + i + " (" + from.ToString() + " -> " + to.ToString() + ") must be a declared edge");
            }
        }

        [Test]
        public void InnerSystemCycleIsRejectedWithItsPath()
        {
            StageId stage = ScheduleFixtures.Stage(1UL);
            FactoryKey left = ScheduleFixtures.Key(1UL);
            FactoryKey right = ScheduleFixtures.Key(2UL);

            var systems = new[]
            {
                ScheduleFixtures.System(left, requiredAfter: new[] { right }),
                ScheduleFixtures.System(right, requiredAfter: new[] { left }),
            };

            ScheduleCompilation rejected = Rejected(
                new ScheduleDeclarations(new[] { ScheduleFixtures.StageSpec(stage, systems) }, null),
                DiagnosticCode.Cycle,
                ScheduleWitnessKind.SystemCycle);

            Assert.That(rejected.Witnesses[0].Stage, Is.EqualTo(stage));
            IReadOnlyList<FactoryKey> path = rejected.Witnesses[0].SystemCyclePath;
            Assert.That(path.Count, Is.EqualTo(3), rejected.Explain());
            Assert.That(path[0], Is.EqualTo(path[path.Count - 1]));
            Assert.That(path, Does.Contain(left));
            Assert.That(path, Does.Contain(right));
        }

        [Test]
        public void AStageThatDependsOnItselfIsRejectedAsACycle()
        {
            StageId stage = ScheduleFixtures.Stage(1UL);
            StageSpec spec = ScheduleFixtures.StageSpec(
                stage,
                new[] { ScheduleFixtures.System(ScheduleFixtures.Key(1UL)) },
                requiredAfter: new[] { stage });

            Rejected(
                new ScheduleDeclarations(new[] { spec }, null),
                DiagnosticCode.Cycle,
                ScheduleWitnessKind.StageCycle);
        }

        // ------------------------------------------------------------------ access conflicts

        [Test]
        public void UnorderedWriterConflictRejectsWithBothDeclarationsAsWitness()
        {
            StageId first = ScheduleFixtures.Stage(1UL);
            StageId second = ScheduleFixtures.Stage(2UL);
            SchemaRef schema = ScheduleFixtures.Schema(1UL);
            FactoryKey leftKey = ScheduleFixtures.Key(1UL);
            FactoryKey rightKey = ScheduleFixtures.Key(2UL);

            var stages = new List<StageSpec>
            {
                ScheduleFixtures.StageSpec(first, new[]
                {
                    ScheduleFixtures.System(leftKey, ScheduleFixtures.AccessSetOf(
                        ScheduleFixtures.Access(schema, AccessMode.Write))),
                }),
                ScheduleFixtures.StageSpec(second, new[]
                {
                    ScheduleFixtures.System(rightKey, ScheduleFixtures.AccessSetOf(
                        ScheduleFixtures.Access(schema, AccessMode.Read))),
                }),
            };

            ScheduleCompilation rejected = Rejected(
                new ScheduleDeclarations(stages, null),
                DiagnosticCode.AmbiguousOrder,
                ScheduleWitnessKind.AmbiguousAccessOrder);

            ScheduleWitness witness = rejected.Witnesses[0];
            Assert.That(witness.Schema, Is.EqualTo(schema));
            Assert.That(witness.System, Is.EqualTo(leftKey));
            Assert.That(witness.RelatedSystem, Is.EqualTo(rightKey));
            Assert.That(witness.Mode, Is.EqualTo(AccessMode.Write));
            Assert.That(witness.RelatedMode, Is.EqualTo(AccessMode.Read));
            Assert.That(witness.Stage, Is.EqualTo(first));
            Assert.That(witness.RelatedStage, Is.EqualTo(second));
        }

        [Test]
        public void TwoWritersInsideOneStageWithoutAnEdgeReject()
        {
            StageId stage = ScheduleFixtures.Stage(1UL);
            SchemaRef schema = ScheduleFixtures.Schema(1UL);

            var systems = new[]
            {
                ScheduleFixtures.System(ScheduleFixtures.Key(1UL), ScheduleFixtures.AccessSetOf(
                    ScheduleFixtures.Access(schema, AccessMode.Write))),
                ScheduleFixtures.System(ScheduleFixtures.Key(2UL), ScheduleFixtures.AccessSetOf(
                    ScheduleFixtures.Access(schema, AccessMode.ReadWrite))),
            };

            ScheduleCompilation rejected = Rejected(
                new ScheduleDeclarations(new[] { ScheduleFixtures.StageSpec(stage, systems) }, null),
                DiagnosticCode.AmbiguousOrder,
                ScheduleWitnessKind.AmbiguousAccessOrder);

            Assert.That(rejected.Witnesses[0].Stage, Is.EqualTo(stage));
            Assert.That(rejected.Witnesses[0].RelatedStage, Is.EqualTo(stage),
                "two coalesced systems in one stage still need a semantic edge (04 section 4, P-040)");
        }

        [Test]
        public void ValidDisjointPartitionsMayOverlapWithoutAnEdge()
        {
            StageId first = ScheduleFixtures.Stage(1UL);
            StageId second = ScheduleFixtures.Stage(2UL);
            SchemaRef schema = ScheduleFixtures.Schema(1UL);

            var stages = new List<StageSpec>
            {
                ScheduleFixtures.StageSpec(first, new[]
                {
                    ScheduleFixtures.System(ScheduleFixtures.Key(1UL), ScheduleFixtures.AccessSetOf(
                        ScheduleFixtures.Partitioned(schema, AccessMode.Write, ScheduleFixtures.Partition(1UL)))),
                }),
                ScheduleFixtures.StageSpec(second, new[]
                {
                    ScheduleFixtures.System(ScheduleFixtures.Key(2UL), ScheduleFixtures.AccessSetOf(
                        ScheduleFixtures.Partitioned(schema, AccessMode.Write, ScheduleFixtures.Partition(2UL)))),
                }),
            };

            CompiledSchedule schedule = Compile(new ScheduleDeclarations(stages, null));
            Assert.That(schedule.Stages.Count, Is.EqualTo(2));
            Assert.That(schedule.Edges.Count, Is.EqualTo(0), "proven disjoint partitions need no semantic edge (P-040)");
        }

        [Test]
        public void AnUnpartitionedWriterConflictsWithAPartitionedOneOnTheSameSchema()
        {
            StageId first = ScheduleFixtures.Stage(1UL);
            StageId second = ScheduleFixtures.Stage(2UL);
            SchemaRef schema = ScheduleFixtures.Schema(1UL);

            var stages = new List<StageSpec>
            {
                ScheduleFixtures.StageSpec(first, new[]
                {
                    ScheduleFixtures.System(ScheduleFixtures.Key(1UL), ScheduleFixtures.AccessSetOf(
                        ScheduleFixtures.Access(schema, AccessMode.Write))),
                }),
                ScheduleFixtures.StageSpec(second, new[]
                {
                    ScheduleFixtures.System(ScheduleFixtures.Key(2UL), ScheduleFixtures.AccessSetOf(
                        ScheduleFixtures.Partitioned(schema, AccessMode.Write, ScheduleFixtures.Partition(2UL)))),
                }),
            };

            Rejected(
                new ScheduleDeclarations(stages, null),
                DiagnosticCode.AmbiguousOrder,
                ScheduleWitnessKind.AmbiguousAccessOrder);
        }

        [Test]
        public void ReadOnlyOverlapBetweenUnorderedSystemsIsLegal()
        {
            StageId first = ScheduleFixtures.Stage(1UL);
            StageId second = ScheduleFixtures.Stage(2UL);
            SchemaRef schema = ScheduleFixtures.Schema(1UL);

            var stages = new List<StageSpec>
            {
                ScheduleFixtures.StageSpec(first, new[]
                {
                    ScheduleFixtures.System(ScheduleFixtures.Key(1UL), ScheduleFixtures.AccessSetOf(
                        ScheduleFixtures.Access(schema, AccessMode.Read))),
                }),
                ScheduleFixtures.StageSpec(second, new[]
                {
                    ScheduleFixtures.System(ScheduleFixtures.Key(2UL), ScheduleFixtures.AccessSetOf(
                        ScheduleFixtures.Access(schema, AccessMode.Read))),
                }),
            };

            CompiledSchedule schedule = Compile(new ScheduleDeclarations(stages, null));
            Assert.That(schedule.Edges.Count, Is.EqualTo(0), "two readers never need a semantic edge (P-040)");
        }

        [Test]
        public void AccessOnDifferentSchemasNeverConflicts()
        {
            StageId stage = ScheduleFixtures.Stage(1UL);

            var systems = new[]
            {
                ScheduleFixtures.System(ScheduleFixtures.Key(1UL), ScheduleFixtures.AccessSetOf(
                    ScheduleFixtures.Access(ScheduleFixtures.Schema(1UL), AccessMode.Write))),
                ScheduleFixtures.System(ScheduleFixtures.Key(2UL), ScheduleFixtures.AccessSetOf(
                    ScheduleFixtures.Access(ScheduleFixtures.Schema(2UL), AccessMode.Write))),
            };

            CompiledSchedule schedule = Compile(
                new ScheduleDeclarations(new[] { ScheduleFixtures.StageSpec(stage, systems) }, null));
            Assert.That(schedule.Entries.Count, Is.EqualTo(2));
        }

        // ------------------------------------------------------------------ coalescing

        [Test]
        public void TwoCompatibleStageDeclarationsCoalesceIntoOneStage()
        {
            StageId stage = ScheduleFixtures.Stage(1UL);
            SchemaRef first = ScheduleFixtures.Schema(1UL);
            SchemaRef second = ScheduleFixtures.Schema(2UL);

            var stages = new List<StageSpec>
            {
                ScheduleFixtures.StageSpec(
                    stage,
                    new[]
                    {
                        ScheduleFixtures.System(ScheduleFixtures.Key(1UL), ScheduleFixtures.AccessSetOf(
                            ScheduleFixtures.Access(first, AccessMode.Write))),
                    },
                    ScheduleFixtures.AccessSetOf(ScheduleFixtures.Access(first, AccessMode.Write))),
                ScheduleFixtures.StageSpec(
                    stage,
                    new[]
                    {
                        ScheduleFixtures.System(ScheduleFixtures.Key(2UL), ScheduleFixtures.AccessSetOf(
                            ScheduleFixtures.Access(second, AccessMode.Read))),
                    },
                    ScheduleFixtures.AccessSetOf(ScheduleFixtures.Access(second, AccessMode.Read))),
            };

            CompiledSchedule schedule = Compile(new ScheduleDeclarations(stages, null));

            Assert.That(schedule.Stages.Count, Is.EqualTo(1), "shared stage declarations coalesce (P-039)");
            Assert.That(schedule.Entries.Count, Is.EqualTo(2), "coalescing never merges distinct system keys");
            Assert.That(schedule.Stages[0].Access.Declarations.Count, Is.EqualTo(2),
                "the coalesced read/write summary is the union of both declarations");
        }

        [Test]
        public void AnIdenticalSystemDeclarationInTwoCoalescedStagesIsMergedOnce()
        {
            StageId stage = ScheduleFixtures.Stage(1UL);
            SystemSpec system = ScheduleFixtures.System(ScheduleFixtures.Key(1UL));

            CompiledSchedule schedule = Compile(new ScheduleDeclarations(new[]
            {
                ScheduleFixtures.StageSpec(stage, new[] { system }),
                ScheduleFixtures.StageSpec(stage, new[] { system }),
            }, null));

            Assert.That(schedule.Stages.Count, Is.EqualTo(1));
            Assert.That(schedule.Entries.Count, Is.EqualTo(1),
                "one system key keeps one position in one execution plan (04 section 4)");
        }

        [Test]
        public void CoalescedDeclarationsMustAgreeOnVersionOwnerAndAffinity()
        {
            StageId stage = ScheduleFixtures.Stage(1UL);
            var systems = new[] { ScheduleFixtures.System(ScheduleFixtures.Key(1UL)) };

            Rejected(
                new ScheduleDeclarations(new[]
                {
                    ScheduleFixtures.StageSpec(stage, systems, version: 1U),
                    ScheduleFixtures.StageSpec(stage, systems, version: 2U),
                }, null),
                DiagnosticCode.UnsupportedVersion,
                ScheduleWitnessKind.StageVersionMismatch);

            Rejected(
                new ScheduleDeclarations(new[]
                {
                    ScheduleFixtures.StageSpec(stage, systems),
                    ScheduleFixtures.StageSpec(stage, systems, ownerPackage: new Id128(0x4F54484552504B47UL, 1UL)),
                }, null),
                DiagnosticCode.OwnershipConflict,
                ScheduleWitnessKind.StageOwnerMismatch);

            Rejected(
                new ScheduleDeclarations(new[]
                {
                    ScheduleFixtures.StageSpec(stage, systems),
                    ScheduleFixtures.StageSpec(stage, systems, affinity: HostAffinity.BurstJob),
                }, null),
                DiagnosticCode.OwnershipConflict,
                ScheduleWitnessKind.StageAffinityMismatch);
        }

        [Test]
        public void TwoConflictingDeclarationsOfOneSystemKeyReject()
        {
            StageId stage = ScheduleFixtures.Stage(1UL);
            FactoryKey key = ScheduleFixtures.Key(1UL);
            SchemaRef schema = ScheduleFixtures.Schema(1UL);

            var stages = new List<StageSpec>
            {
                ScheduleFixtures.StageSpec(stage, new[]
                {
                    ScheduleFixtures.System(key, ScheduleFixtures.AccessSetOf(
                        ScheduleFixtures.Access(schema, AccessMode.Read))),
                    ScheduleFixtures.System(key, ScheduleFixtures.AccessSetOf(
                        ScheduleFixtures.Access(schema, AccessMode.Write))),
                }),
            };

            ScheduleCompilation rejected = Rejected(
                new ScheduleDeclarations(stages, null),
                DiagnosticCode.OwnershipConflict,
                ScheduleWitnessKind.DuplicateSystemDeclaration);
            Assert.That(rejected.Witnesses[0].System, Is.EqualTo(key));
        }

        [Test]
        public void TwoConflictingDeclarationsOfOneSystemKeyRejectEvenWhenTheirAccessSetIsIdentical()
        {
            StageId stage = ScheduleFixtures.Stage(1UL);
            FactoryKey key = ScheduleFixtures.Key(1UL);
            FactoryKey other = ScheduleFixtures.Key(2UL);

            var stages = new List<StageSpec>
            {
                ScheduleFixtures.StageSpec(stage, new[]
                {
                    ScheduleFixtures.System(key, multiplicity: SystemMultiplicity.World),
                    ScheduleFixtures.System(key, multiplicity: SystemMultiplicity.PerPartition),
                }),
                ScheduleFixtures.StageSpec(stage, new[]
                {
                    ScheduleFixtures.System(other),
                }),
            };

            Rejected(
                new ScheduleDeclarations(stages, null),
                DiagnosticCode.OwnershipConflict,
                ScheduleWitnessKind.DuplicateSystemDeclaration);
        }

        // ------------------------------------------------------------------ buffers and playback

        [Test]
        public void ABufferWhoseActiveProducersHaveNoConsumerRejects()
        {
            StageId producer = ScheduleFixtures.Stage(1UL);
            StageId absentConsumer = ScheduleFixtures.Stage(9UL);
            FactoryKey producerSystem = ScheduleFixtures.Key(1UL);
            BufferId buffer = ScheduleFixtures.Buffer(1UL);

            var stages = new List<StageSpec>
            {
                ScheduleFixtures.StageSpec(
                    producer,
                    new[] { ScheduleFixtures.System(producerSystem) },
                    ports: new[] { new BufferPort(buffer, PortDirection.Producer, producer) }),
            };

            var buffers = new List<BufferSpec>
            {
                ScheduleFixtures.BufferSpec(buffer, new[] { producerSystem }, producer, absentConsumer),
            };

            ScheduleCompilation rejected = Rejected(
                new ScheduleDeclarations(stages, buffers),
                DiagnosticCode.MissingDependency,
                ScheduleWitnessKind.BufferConsumerMissing);
            Assert.That(rejected.Witnesses[0].Buffer, Is.EqualTo(buffer));
            Assert.That(rejected.Witnesses[0].RelatedStage, Is.EqualTo(absentConsumer));
        }

        [Test]
        public void AStagePortWithoutABufferContractRejects()
        {
            StageId stage = ScheduleFixtures.Stage(1UL);
            BufferId undeclared = ScheduleFixtures.Buffer(7UL);

            var stages = new List<StageSpec>
            {
                ScheduleFixtures.StageSpec(
                    stage,
                    new[] { ScheduleFixtures.System(ScheduleFixtures.Key(1UL)) },
                    ports: new[] { new BufferPort(undeclared, PortDirection.Producer, stage) }),
            };

            ScheduleCompilation rejected = Rejected(
                new ScheduleDeclarations(stages, null),
                DiagnosticCode.MissingDependency,
                ScheduleWitnessKind.BufferPortMissing);
            Assert.That(rejected.Witnesses[0].Buffer, Is.EqualTo(undeclared));
        }

        [Test]
        public void AProducerPortForABufferWhoseContractNamesNoSuchProducerRejects()
        {
            StageId producer = ScheduleFixtures.Stage(1UL);
            StageId consumer = ScheduleFixtures.Stage(2UL);
            FactoryKey producerSystem = ScheduleFixtures.Key(1UL);
            FactoryKey consumerSystem = ScheduleFixtures.Key(2UL);
            BufferId buffer = ScheduleFixtures.Buffer(1UL);

            var stages = new List<StageSpec>
            {
                ScheduleFixtures.StageSpec(
                    producer,
                    new[] { ScheduleFixtures.System(producerSystem) },
                    ports: new[] { new BufferPort(buffer, PortDirection.Producer, producer) }),
                ScheduleFixtures.StageSpec(
                    consumer,
                    new[] { ScheduleFixtures.System(consumerSystem) },
                    ports: new[] { new BufferPort(buffer, PortDirection.Consumer, consumer) }),
            };

            // The contract names a producer living in another stage, so this stage's producer port is dishonest.
            var buffers = new List<BufferSpec>
            {
                ScheduleFixtures.BufferSpec(buffer, new[] { consumerSystem }, producer, consumer),
            };

            ScheduleCompilation rejected = Rejected(new ScheduleDeclarations(stages, buffers));
            bool found = false;
            for (int i = 0; i < rejected.Witnesses.Count; i++)
            {
                if (rejected.Witnesses[i].Kind == ScheduleWitnessKind.BufferPortDirectionMismatch)
                {
                    found = true;
                    Assert.That(rejected.Witnesses[i].Stage, Is.EqualTo(producer));
                    Assert.That(rejected.Witnesses[i].Buffer, Is.EqualTo(buffer));
                }
            }

            Assert.That(found, Is.True, rejected.Explain());
        }

        [Test]
        public void TwoBufferContractsForOneBufferRejectAsDuplicate()
        {
            StageId producer = ScheduleFixtures.Stage(1UL);
            StageId consumer = ScheduleFixtures.Stage(2UL);
            FactoryKey producerSystem = ScheduleFixtures.Key(1UL);
            FactoryKey consumerSystem = ScheduleFixtures.Key(2UL);
            BufferId buffer = ScheduleFixtures.Buffer(1UL);

            var stages = new List<StageSpec>
            {
                ScheduleFixtures.StageSpec(
                    producer,
                    new[] { ScheduleFixtures.System(producerSystem) },
                    ports: new[] { new BufferPort(buffer, PortDirection.Producer, producer) }),
                ScheduleFixtures.StageSpec(
                    consumer,
                    new[] { ScheduleFixtures.System(consumerSystem) },
                    ports: new[] { new BufferPort(buffer, PortDirection.Consumer, consumer) }),
            };

            var buffers = new List<BufferSpec>
            {
                ScheduleFixtures.BufferSpec(buffer, new[] { producerSystem }, producer, consumer),
                ScheduleFixtures.BufferSpec(
                    buffer,
                    new[] { producerSystem },
                    producer,
                    consumer,
                    lifetime: BufferLifetime.Step),
            };

            ScheduleCompilation rejected = Rejected(
                new ScheduleDeclarations(stages, buffers),
                DiagnosticCode.OwnershipConflict,
                ScheduleWitnessKind.DuplicateBufferContract);
            Assert.That(rejected.Witnesses[0].Buffer, Is.EqualTo(buffer));
        }

        [Test]
        public void ABufferBindsItsProducersAndConsumerCanonically()
        {
            CompiledSchedule schedule = Compile(ScheduleFixtures.CardAndNarrative());

            Assert.That(schedule.BufferBindings.Count, Is.EqualTo(1));
            BufferBinding binding = schedule.BufferBindings[0];
            Assert.That(binding.Buffer, Is.EqualTo(ScheduleFixtures.Buffer(1UL)));
            Assert.That(binding.Producers, Is.EqualTo(new[] { ScheduleFixtures.Key(2UL) }));
            Assert.That(binding.ConsumerStage, Is.EqualTo(ScheduleFixtures.Stage(3UL)));
        }

        [Test]
        public void ABufferWithNoActiveProducerOrdersNothingAndCreatesNoPlaybackPoint()
        {
            StageId consumer = ScheduleFixtures.Stage(2UL);
            FactoryKey consumerSystem = ScheduleFixtures.Key(2UL);
            BufferId buffer = ScheduleFixtures.Buffer(1UL);

            var stages = new List<StageSpec>
            {
                ScheduleFixtures.StageSpec(
                    consumer,
                    new[] { ScheduleFixtures.System(consumerSystem) },
                    ports: new[] { new BufferPort(buffer, PortDirection.Consumer, consumer) }),
            };

            // The declared producer belongs to a plugin that is not mounted: the consumer reads an empty stream.
            var buffers = new List<BufferSpec>
            {
                ScheduleFixtures.BufferSpec(buffer, new[] { ScheduleFixtures.Key(9UL) }, consumer, consumer),
            };

            CompiledSchedule schedule = Compile(new ScheduleDeclarations(stages, buffers));
            Assert.That(schedule.Stages.Count, Is.EqualTo(1));
            Assert.That(schedule.Edges.Count, Is.EqualTo(0));
            Assert.That(schedule.PlaybackPoints.Count, Is.EqualTo(0),
                "nothing was produced, so there is nothing to play back (03 section 3)");
        }

        [Test]
        public void ABufferWhoseOwnerStageIsAbsentWhileItsProducerIsActiveRejects()
        {
            StageId producer = ScheduleFixtures.Stage(1UL);
            StageId consumer = ScheduleFixtures.Stage(2UL);
            StageId absentOwner = ScheduleFixtures.Stage(9UL);
            FactoryKey producerSystem = ScheduleFixtures.Key(1UL);
            BufferId buffer = ScheduleFixtures.Buffer(1UL);

            var stages = new List<StageSpec>
            {
                ScheduleFixtures.StageSpec(
                    producer,
                    new[] { ScheduleFixtures.System(producerSystem) },
                    ports: new[] { new BufferPort(buffer, PortDirection.Producer, producer) }),
                ScheduleFixtures.StageSpec(
                    consumer,
                    new[] { ScheduleFixtures.System(ScheduleFixtures.Key(2UL)) },
                    ports: new[] { new BufferPort(buffer, PortDirection.Consumer, consumer) }),
            };

            var buffers = new List<BufferSpec>
            {
                ScheduleFixtures.BufferSpec(buffer, new[] { producerSystem }, absentOwner, consumer),
            };

            ScheduleCompilation rejected = Rejected(
                new ScheduleDeclarations(stages, buffers),
                DiagnosticCode.MissingDependency,
                ScheduleWitnessKind.BufferOwnerMissing);
            Assert.That(rejected.Witnesses[0].RelatedStage, Is.EqualTo(absentOwner));
        }

        // ------------------------------------------------------------------ neutrality and shape

        [Test]
        public void AnEmptyDeclarationSetCompilesToAnEmptyValidSchedule()
        {
            CompiledSchedule schedule = Compile(ScheduleDeclarations.Empty);

            Assert.That(schedule.IsEmpty, Is.True);
            Assert.That(schedule.StageCount, Is.EqualTo(0));
            Assert.That(schedule.SystemCount, Is.EqualTo(0));
            Assert.That(schedule.PlaybackPoints.Count, Is.EqualTo(0));
            Assert.That(schedule.Plan.Nodes.Count, Is.EqualTo(0));
            Assert.That(schedule.Plan.PlanHash.IsEmpty, Is.False, "an empty schedule still has a canonical hash");
        }

        [Test]
        public void NoScheduleRequiresACombatPhysicsOrAnimationStage()
        {
            CompiledSchedule schedule = Compile(ScheduleFixtures.CardAndNarrative());

            string[] forbidden = { "combat", "physics", "animation", "turn", "actor", "vitality", "reward" };
            for (int i = 0; i < schedule.Stages.Count; i++)
            {
                string stage = schedule.Stages[i].Stage.ToString();
                for (int f = 0; f < forbidden.Length; f++)
                {
                    Assert.That(stage.ToLowerInvariant(), Does.Not.Contain(forbidden[f]),
                        "the kernel supplies no mandatory " + forbidden[f] + " stage (P-001, P-059)");
                }
            }

            Assert.That(schedule.Stages.Count, Is.EqualTo(4), "the schedule is exactly the declarations asked for");
        }

        [Test]
        public void EverySystemCarriesItsStageFenceIndexAndItsStagePredecessors()
        {
            CompiledSchedule schedule = Compile(ScheduleFixtures.CardAndNarrative());

            ScheduleStage settle = schedule.Stages[1];
            ScheduleStage evaluate = schedule.Stages[2];

            Assert.That(settle.Stage, Is.EqualTo(ScheduleFixtures.Stage(2UL)));
            Assert.That(settle.PredecessorStages, Is.EqualTo(new[] { 0 }));
            Assert.That(settle.Systems[0].PredecessorStages, Is.EqualTo(new[] { 0 }));
            Assert.That(settle.Systems[0].StageIndex, Is.EqualTo(1));
            Assert.That(settle.Systems[0].DispatchIndex, Is.EqualTo(1));
            Assert.That(evaluate.PredecessorStages, Is.EqualTo(new[] { 1 }));
            Assert.That(evaluate.Systems[0].Access.Declarations.Count, Is.EqualTo(2));

            Assert.That(schedule.TryGetStageIndex(ScheduleFixtures.Stage(3UL), out int index), Is.True);
            Assert.That(index, Is.EqualTo(2));
            Assert.That(schedule.EntriesOfStage(index).Count, Is.EqualTo(1));
            Assert.That(schedule.PlaybackPointsOfStage(1).Count, Is.EqualTo(1));
            Assert.That(schedule.PlaybackPointsOfStage(0).Count, Is.EqualTo(0));
            Assert.That(schedule.TryGetStageIndex(ScheduleFixtures.Stage(9UL), out _), Is.False);
        }

        [Test]
        public void TheContractShapedPlanCarriesStageNodesSystemNodesAndTheSemanticHash()
        {
            CompiledSchedule schedule = Compile(ScheduleFixtures.CardAndNarrative());

            int stageNodes = 0;
            int systemNodes = 0;
            for (int i = 0; i < schedule.Plan.Nodes.Count; i++)
            {
                if (schedule.Plan.Nodes[i].Kind == PlanNodeKind.Stage)
                {
                    stageNodes++;
                }
                else
                {
                    systemNodes++;
                }
            }

            Assert.That(stageNodes, Is.EqualTo(4));
            Assert.That(systemNodes, Is.EqualTo(4));
            Assert.That(schedule.Plan.PlanHash, Is.EqualTo(schedule.Hash));
            Assert.That(schedule.HasEdge(fromStage: 0, toStage: 1), Is.True);
            Assert.That(schedule.HasEdge(fromStage: 1, toStage: 2), Is.True);
            Assert.That(schedule.HasEdge(fromStage: 3, toStage: 0), Is.False);
        }

        [Test]
        public void ChangingOnlyAnAccessSetChangesThePlanHash()
        {
            StageId stage = ScheduleFixtures.Stage(1UL);
            FactoryKey key = ScheduleFixtures.Key(1UL);
            SchemaRef schema = ScheduleFixtures.Schema(1UL);

            CompiledSchedule reader = Compile(new ScheduleDeclarations(new[]
            {
                ScheduleFixtures.StageSpec(stage, new[]
                {
                    ScheduleFixtures.System(key, ScheduleFixtures.AccessSetOf(
                        ScheduleFixtures.Access(schema, AccessMode.Read))),
                }),
            }, null));

            CompiledSchedule writer = Compile(new ScheduleDeclarations(new[]
            {
                ScheduleFixtures.StageSpec(stage, new[]
                {
                    ScheduleFixtures.System(key, ScheduleFixtures.AccessSetOf(
                        ScheduleFixtures.Access(schema, AccessMode.Write))),
                }),
            }, null));

            Assert.That(writer.Hash, Is.Not.EqualTo(reader.Hash),
                "access sets are semantic inputs of the plan hash (P-040)");
        }

        [Test]
        public void ChangingOnlyABufferLifetimeOrCapacityChangesThePlanHash()
        {
            StageId producer = ScheduleFixtures.Stage(1UL);
            StageId consumer = ScheduleFixtures.Stage(2UL);
            FactoryKey producerSystem = ScheduleFixtures.Key(1UL);
            BufferId buffer = ScheduleFixtures.Buffer(1UL);

            var stages = new List<StageSpec>
            {
                ScheduleFixtures.StageSpec(
                    producer,
                    new[] { ScheduleFixtures.System(producerSystem) },
                    ports: new[] { new BufferPort(buffer, PortDirection.Producer, producer) }),
                ScheduleFixtures.StageSpec(
                    consumer,
                    new[] { ScheduleFixtures.System(ScheduleFixtures.Key(2UL)) },
                    ports: new[] { new BufferPort(buffer, PortDirection.Consumer, consumer) }),
            };

            CompiledSchedule stageLifetime = Compile(new ScheduleDeclarations(stages, new[]
            {
                ScheduleFixtures.BufferSpec(buffer, new[] { producerSystem }, producer, consumer),
            }));

            CompiledSchedule stepLifetime = Compile(new ScheduleDeclarations(stages, new[]
            {
                ScheduleFixtures.BufferSpec(
                    buffer,
                    new[] { producerSystem },
                    producer,
                    consumer,
                    lifetime: BufferLifetime.Step),
            }));

            CompiledSchedule smaller = Compile(new ScheduleDeclarations(stages, new[]
            {
                ScheduleFixtures.BufferSpec(buffer, new[] { producerSystem }, producer, consumer, capacity: 4),
            }));

            Assert.That(stepLifetime.Hash, Is.Not.EqualTo(stageLifetime.Hash), "buffer lifetime is semantic (P-043)");
            Assert.That(smaller.Hash, Is.Not.EqualTo(stageLifetime.Hash), "buffer capacity is semantic (P-043)");
            Assert.That(stepLifetime.PlaybackPoints[0].Lifetime, Is.EqualTo(BufferLifetime.Step));
            Assert.That(stepLifetime.PlaybackPoints[0].Capacity, Is.EqualTo(16));
            Assert.That(smaller.PlaybackPoints[0].Capacity, Is.EqualTo(4));
        }

        [Test]
        public void ChangingOnlyAStageVersionOrAffinityChangesThePlanHash()
        {
            StageId stage = ScheduleFixtures.Stage(1UL);
            var systems = new[] { ScheduleFixtures.System(ScheduleFixtures.Key(1UL)) };

            CompiledSchedule versionOne = Compile(
                new ScheduleDeclarations(new[] { ScheduleFixtures.StageSpec(stage, systems, version: 1U) }, null));
            CompiledSchedule versionTwo = Compile(
                new ScheduleDeclarations(new[] { ScheduleFixtures.StageSpec(stage, systems, version: 2U) }, null));
            CompiledSchedule job = Compile(
                new ScheduleDeclarations(
                    new[] { ScheduleFixtures.StageSpec(stage, systems, affinity: HostAffinity.BurstJob) },
                    null));

            Assert.That(versionTwo.Hash, Is.Not.EqualTo(versionOne.Hash));
            Assert.That(job.Hash, Is.Not.EqualTo(versionOne.Hash));
        }

        [Test]
        public void RejectionsAreDeterministicUnderShuffledDeclarationOrder()
        {
            StageId first = ScheduleFixtures.Stage(1UL);
            StageId second = ScheduleFixtures.Stage(2UL);
            SchemaRef schema = ScheduleFixtures.Schema(1UL);

            var stages = new List<StageSpec>
            {
                ScheduleFixtures.StageSpec(first, new[]
                {
                    ScheduleFixtures.System(ScheduleFixtures.Key(1UL), ScheduleFixtures.AccessSetOf(
                        ScheduleFixtures.Access(schema, AccessMode.Write))),
                }),
                ScheduleFixtures.StageSpec(second, new[]
                {
                    ScheduleFixtures.System(ScheduleFixtures.Key(2UL), ScheduleFixtures.AccessSetOf(
                        ScheduleFixtures.Access(schema, AccessMode.Write))),
                }),
            };

            ScheduleCompilation forward = Rejected(new ScheduleDeclarations(stages, null));
            ScheduleCompilation reversed = Rejected(new ScheduleDeclarations(Reverse(stages), null));

            Assert.That(reversed.Explain(), Is.EqualTo(forward.Explain()),
                "the same declarations in another order produce the same rejection text (P-008, TEST-022)");
        }

        [Test]
        public void ADeclarationSetWithSeveralConflictsReportsTheCanonicallyFirstCode()
        {
            StageId first = ScheduleFixtures.Stage(1UL);
            StageId second = ScheduleFixtures.Stage(2UL);
            SchemaRef schema = ScheduleFixtures.Schema(1UL);
            var systems = new[] { ScheduleFixtures.System(ScheduleFixtures.Key(1UL)) };

            // A cycle (Cycle = 7) and an unordered access conflict (AmbiguousOrder = 6) in one declaration set.
            var stages = new List<StageSpec>
            {
                ScheduleFixtures.StageSpec(
                    first,
                    new[]
                    {
                        ScheduleFixtures.System(ScheduleFixtures.Key(2UL), ScheduleFixtures.AccessSetOf(
                            ScheduleFixtures.Access(schema, AccessMode.Write))),
                    },
                    requiredAfter: new[] { second }),
                ScheduleFixtures.StageSpec(
                    second,
                    systems,
                    requiredAfter: new[] { first }),
            };

            ScheduleCompilation rejected = Rejected(new ScheduleDeclarations(stages, null));
            Assert.That(rejected.Code, Is.EqualTo(DiagnosticCode.Cycle),
                "the cycle phase rejects before access validation" + Environment.NewLine + rejected.Explain());
            Assert.That(rejected.Witnesses.Count, Is.GreaterThan(0));
            Assert.That(rejected.CyclePath.Count, Is.EqualTo(3));
            Assert.That(rejected.WitnessesTruncated, Is.False);
        }

        [Test]
        public void InnerSystemCycleWitnessesAreStableUnderAShuffledSystemDeclarationOrder()
        {
            StageId stage = ScheduleFixtures.Stage(1UL);
            FactoryKey first = ScheduleFixtures.Key(1UL);
            FactoryKey second = ScheduleFixtures.Key(2UL);
            FactoryKey third = ScheduleFixtures.Key(3UL);

            // A -> B -> C -> A, with B additionally required before C: two cycles close at different places, so the
            // order the roots are visited in decides which back edges are reported.
            SystemSpec a = ScheduleFixtures.System(first, requiredBefore: new[] { second });
            SystemSpec b = ScheduleFixtures.System(second, requiredBefore: new[] { third });
            SystemSpec c = ScheduleFixtures.System(third, requiredBefore: new[] { first, second });

            ScheduleCompilation declaredAbc = Rejected(new ScheduleDeclarations(new[]
            {
                ScheduleFixtures.StageSpec(stage, new[] { a, b, c }),
            }, null));

            ScheduleCompilation declaredCab = Rejected(new ScheduleDeclarations(new[]
            {
                ScheduleFixtures.StageSpec(stage, new[] { c, a, b }),
            }, null));

            ScheduleCompilation declaredBca = Rejected(new ScheduleDeclarations(new[]
            {
                ScheduleFixtures.StageSpec(stage, new[] { b, c, a }),
            }, null));

            Assert.That(declaredAbc.Code, Is.EqualTo(DiagnosticCode.Cycle));
            Assert.That(declaredCab.Explain(), Is.EqualTo(declaredAbc.Explain()),
                "cycle witnesses must not depend on the order the systems were declared in (P-008, TEST-022)");
            Assert.That(declaredBca.Explain(), Is.EqualTo(declaredAbc.Explain()));
            Assert.That(declaredCab.Witnesses.Count, Is.EqualTo(declaredAbc.Witnesses.Count));
        }

        [Test]
        public void AccessConflictWitnessesAreStableUnderAShuffledAccessDeclarationOrder()
        {
            StageId first = ScheduleFixtures.Stage(1UL);
            StageId second = ScheduleFixtures.Stage(2UL);
            SchemaRef one = ScheduleFixtures.Schema(1UL);
            SchemaRef two = ScheduleFixtures.Schema(2UL);

            // Two unordered systems that each touch two schemas: two distinct conflicts of the same system pair.
            AccessDeclaration writeOne = ScheduleFixtures.Access(one, AccessMode.Write);
            AccessDeclaration writeTwo = ScheduleFixtures.Access(two, AccessMode.Write);
            AccessDeclaration readOne = ScheduleFixtures.Access(one, AccessMode.Read);
            AccessDeclaration readTwo = ScheduleFixtures.Access(two, AccessMode.Read);

            ScheduleCompilation declaredForward = Rejected(new ScheduleDeclarations(new[]
            {
                ScheduleFixtures.StageSpec(first, new[]
                {
                    ScheduleFixtures.System(ScheduleFixtures.Key(1UL), ScheduleFixtures.AccessSetOf(writeOne, writeTwo)),
                }),
                ScheduleFixtures.StageSpec(second, new[]
                {
                    ScheduleFixtures.System(ScheduleFixtures.Key(2UL), ScheduleFixtures.AccessSetOf(readOne, readTwo)),
                }),
            }, null));

            ScheduleCompilation declaredReversed = Rejected(new ScheduleDeclarations(new[]
            {
                ScheduleFixtures.StageSpec(first, new[]
                {
                    ScheduleFixtures.System(ScheduleFixtures.Key(1UL), ScheduleFixtures.AccessSetOf(writeTwo, writeOne)),
                }),
                ScheduleFixtures.StageSpec(second, new[]
                {
                    ScheduleFixtures.System(ScheduleFixtures.Key(2UL), ScheduleFixtures.AccessSetOf(readTwo, readOne)),
                }),
            }, null));

            Assert.That(declaredForward.Witnesses.Count, Is.EqualTo(2));
            Assert.That(declaredReversed.Witnesses.Count, Is.EqualTo(2));
            Assert.That(declaredReversed.Explain(), Is.EqualTo(declaredForward.Explain()),
                "access witnesses are ordered by schema and mode, not by declaration order (P-008, TEST-022)");
            Assert.That(declaredForward.Witnesses[0].Schema, Is.EqualTo(one),
                "the canonically first schema decides the first witness");
        }

        [Test]
        public void ARepeatedAccessDeclarationDoesNotDuplicateItsWitness()
        {
            StageId first = ScheduleFixtures.Stage(1UL);
            StageId second = ScheduleFixtures.Stage(2UL);
            SchemaRef schema = ScheduleFixtures.Schema(1UL);
            AccessDeclaration write = ScheduleFixtures.Access(schema, AccessMode.Write);

            ScheduleCompilation rejected = Rejected(new ScheduleDeclarations(new[]
            {
                ScheduleFixtures.StageSpec(first, new[]
                {
                    ScheduleFixtures.System(ScheduleFixtures.Key(1UL), ScheduleFixtures.AccessSetOf(write, write)),
                }),
                ScheduleFixtures.StageSpec(second, new[]
                {
                    ScheduleFixtures.System(ScheduleFixtures.Key(2UL), ScheduleFixtures.AccessSetOf(
                        ScheduleFixtures.Access(schema, AccessMode.Read))),
                }),
            }, null));

            Assert.That(rejected.Code, Is.EqualTo(DiagnosticCode.AmbiguousOrder));
            Assert.That(rejected.Witnesses.Count, Is.EqualTo(1),
                "a repeated declaration is one conflict, not two identical witnesses (P-008)");
        }
    }
}
