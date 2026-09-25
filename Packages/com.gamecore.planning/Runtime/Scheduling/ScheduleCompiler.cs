// GameCore.Planning - semantic schedule compilation (GC-009).
//
// Compiles the active catalog stage/system declarations and their buffer contracts into one stable topological
// order (P-040): required/optional stage edges, each stage's inner system DAG, producer-before-consumer buffer
// edges and deferred structural playback points. Overlapping read/write access needs a directed path in the
// expanded DAG or proven disjoint partitions; an unordered conflict is AmbiguousOrder and a cycle is Cycle, and
// each rejection carries edge witnesses naming the conflicting declarations or the cycle path (P-008, P-039..P-043,
// 03 section 3).
//
// The compiler owns no stage list of its own: an empty declaration set compiles to an empty valid schedule, so no
// schedule requires a combat, physics or animation stage (P-001, P-059). Registration and declaration order are
// never input to precedence: every ready-set tie-break uses canonical stage-id and system-key bytes (P-008).
//
// Unity-free: BCL subset only, no UnityEngine/Unity.* reference (01 section 1).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Planning.Scheduling
{
    /// <summary>
    /// Pure compiler from stage/buffer declarations to a <see cref="CompiledSchedule"/>. Deterministic: the same
    /// declarations in any order compile to the same order and the same plan hash (P-008, TEST-022).
    /// </summary>
    public static class ScheduleCompiler
    {
        /// <summary>Upper bound on retained witnesses; a truncated result says so instead of dropping them silently.</summary>
        public const int MaxWitnesses = 64;

        public static ScheduleCompilation Compile(ScheduleDeclarations declarations)
        {
            if (declarations == null)
            {
                throw new ArgumentNullException(nameof(declarations));
            }

            return new Compilation(declarations).Run();
        }

        private sealed class Compilation
        {
            private readonly ScheduleDeclarations declarations;
            private readonly List<ScheduleWitness> witnesses = new List<ScheduleWitness>();
            private readonly List<StageNode> stages = new List<StageNode>();
            private readonly Dictionary<Id128, StageNode> byStageId = new Dictionary<Id128, StageNode>();
            private readonly Dictionary<FactoryKey, StageNode> bySystemKey = new Dictionary<FactoryKey, StageNode>();
            private readonly List<PendingPlayback> pendingPlayback = new List<PendingPlayback>();
            private readonly List<SystemNode> flatOrder = new List<SystemNode>();

            private bool truncated;

            internal Compilation(ScheduleDeclarations declarations)
            {
                this.declarations = declarations;
            }

            internal ScheduleCompilation Run()
            {
                BuildStageNodes();
                if (witnesses.Count > 0)
                {
                    return Reject("Stage declarations did not coalesce into a consistent contract.");
                }

                ResolveDeclaredStageEdges();
                RebuildStageAdjacency();
                if (witnesses.Count > 0)
                {
                    return Reject("A required stage edge names a stage outside the active declaration set.");
                }

                ResolveInnerSystemGraph();
                RebuildSystemAdjacency();
                if (witnesses.Count > 0)
                {
                    return Reject("A required inner system edge names a system outside its stage.");
                }

                ResolveBuffers();
                RebuildStageAdjacency();
                if (witnesses.Count > 0)
                {
                    return Reject("A declared buffer contract does not match the active stage/system declarations.");
                }

                DetectStageCycles();
                if (witnesses.Count > 0)
                {
                    return Reject("The stage dependency graph contains a cycle.");
                }

                DetectSystemCycles();
                if (witnesses.Count > 0)
                {
                    return Reject("A stage's inner system graph contains a cycle.");
                }

                OrderStages();
                if (witnesses.Count > 0)
                {
                    return Reject("The stage graph could not be ordered.");
                }

                OrderSystems();
                BuildFlatOrder();
                ValidateAccessOrder();
                if (witnesses.Count > 0)
                {
                    return Reject("Overlapping read/write access has no directed path and no proven disjoint partitions.");
                }

                ValidatePlaybackOrder();
                if (witnesses.Count > 0)
                {
                    return Reject("A deferred playback point has no defined position relative to its producers.");
                }

                return ScheduleCompilation.Compiled(BuildSchedule());
            }

            // ------------------------------------------------------------- phase 0: coalesce declarations

            private void BuildStageNodes()
            {
                var groups = new Dictionary<Id128, List<StageSpec>>();
                var ids = new List<Id128>();

                for (int i = 0; i < declarations.Stages.Count; i++)
                {
                    StageSpec spec = declarations.Stages[i];
                    Id128 key = spec.StageId.Value;
                    if (!groups.TryGetValue(key, out List<StageSpec>? group))
                    {
                        group = new List<StageSpec>();
                        groups.Add(key, group);
                        ids.Add(key);
                    }

                    group.Add(spec);
                }

                ids.Sort();

                for (int i = 0; i < ids.Count; i++)
                {
                    List<StageSpec> group = groups[ids[i]];
                    StageSpec first = group[0];
                    var node = new StageNode(first.StageId, first.StageVersion, first.OwnerPackageId, first.Affinity, stages.Count);

                    for (int g = 1; g < group.Count; g++)
                    {
                        StageSpec other = group[g];
                        if (other.StageVersion != first.StageVersion)
                        {
                            Add(new ScheduleWitness(
                                ScheduleWitnessKind.StageVersionMismatch,
                                DiagnosticCode.UnsupportedVersion,
                                "Two declarations of this stage declare contract versions "
                                + first.StageVersion.ToString(CultureInfo.InvariantCulture) + " and "
                                + other.StageVersion.ToString(CultureInfo.InvariantCulture)
                                + "; shared stage declarations coalesce only when their contract version matches (P-039).",
                                stage: first.StageId));
                        }

                        if (!other.OwnerPackageId.Equals(first.OwnerPackageId))
                        {
                            Add(new ScheduleWitness(
                                ScheduleWitnessKind.StageOwnerMismatch,
                                DiagnosticCode.OwnershipConflict,
                                "Two declarations of this stage name different owning packages (P-001, P-039).",
                                stage: first.StageId));
                        }

                        if (other.Affinity != first.Affinity)
                        {
                            Add(new ScheduleWitness(
                                ScheduleWitnessKind.StageAffinityMismatch,
                                DiagnosticCode.OwnershipConflict,
                                "Two declarations of this stage declare different host affinities "
                                + first.Affinity.ToString() + " and " + other.Affinity.ToString() + " (P-039).",
                                stage: first.StageId));
                        }
                    }

                    for (int g = 0; g < group.Count; g++)
                    {
                        MergeStageDeclaration(node, group[g]);
                    }

                    stages.Add(node);
                    byStageId.Add(node.Id.Value, node);
                }
            }

            private void MergeStageDeclaration(StageNode node, StageSpec spec)
            {
                for (int i = 0; i < spec.ReadWriteSet.Declarations.Count; i++)
                {
                    node.Access.Add(spec.ReadWriteSet.Declarations[i]);
                }

                AddDistinctKey(node.FactoryKeys, spec.FactoryKeys);
                AddDistinctId(node.ActivationMemberships, spec.ActivationMemberships);
                AddDistinctStage(node.RequiredBefore, spec.RequiredBefore);
                AddDistinctStage(node.RequiredAfter, spec.RequiredAfter);
                AddDistinctStage(node.OptionalBefore, spec.OptionalBefore);
                AddDistinctStage(node.OptionalAfter, spec.OptionalAfter);

                for (int i = 0; i < spec.Systems.Count; i++)
                {
                    MergeSystemDeclaration(node, spec.Systems[i]);
                }

                for (int i = 0; i < spec.BufferPorts.Count; i++)
                {
                    BufferPort port = spec.BufferPorts[i];
                    bool known = false;
                    for (int p = 0; p < node.Ports.Count; p++)
                    {
                        BufferPort existing = node.Ports[p];
                        if (existing.Buffer.Equals(port.Buffer)
                            && existing.Direction == port.Direction
                            && existing.Stage.Equals(port.Stage))
                        {
                            known = true;
                            break;
                        }
                    }

                    if (!known)
                    {
                        node.Ports.Add(port);
                    }
                }
            }

            private void MergeSystemDeclaration(StageNode node, SystemSpec spec)
            {
                if (node.SystemsByKey.TryGetValue(spec.SystemKey, out SystemNode? existing))
                {
                    if (!IdenticalDeclaration(existing.Declaration, spec))
                    {
                        // One precompiled system type cannot occupy two incompatible positions in one execution plan
                        // (04 section 4). Identical duplicate declarations coalesce silently.
                        Add(new ScheduleWitness(
                            ScheduleWitnessKind.DuplicateSystemDeclaration,
                            DiagnosticCode.OwnershipConflict,
                            "This system key is declared twice in one stage with a different multiplicity, access set "
                            + "or inner edge set (P-039, 04 section 4).",
                            stage: node.Id,
                            system: spec.SystemKey));
                    }

                    return;
                }

                var created = new SystemNode(spec.SystemKey, spec, node, node.Systems.Count);

                // The node's own access list is stored canonical (deduplicated and sorted by schema/mode/partition), so
                // conflict reporting cannot emit the same witness twice nor depend on the declaration order inside a
                // single access set (P-008, P-040).
                created.Access.AddRange(CanonicalAccess(spec.Access));

                node.Systems.Add(created);
                node.SystemsByKey.Add(created.Key, created);
                if (!bySystemKey.ContainsKey(created.Key))
                {
                    bySystemKey.Add(created.Key, node);
                }
            }

            private static bool IdenticalDeclaration(SystemSpec left, SystemSpec right)
            {
                return left.Multiplicity == right.Multiplicity
                    && SameDeclarations(CanonicalAccess(left.Access), CanonicalAccess(right.Access))
                    && SameKeys(left.RequiredBeforeSystems, right.RequiredBeforeSystems)
                    && SameKeys(left.RequiredAfterSystems, right.RequiredAfterSystems)
                    && SameKeys(left.OptionalBeforeSystems, right.OptionalBeforeSystems)
                    && SameKeys(left.OptionalAfterSystems, right.OptionalAfterSystems);
            }

            // ------------------------------------------------------------- phase 1: edges and inner graphs

            private void ResolveDeclaredStageEdges()
            {
                for (int i = 0; i < stages.Count; i++)
                {
                    StageNode node = stages[i];
                    ResolveStageEdges(node, node.RequiredBefore, true, true);
                    ResolveStageEdges(node, node.RequiredAfter, true, false);
                    ResolveStageEdges(node, node.OptionalBefore, false, true);
                    ResolveStageEdges(node, node.OptionalAfter, false, false);
                }
            }

            private void ResolveStageEdges(StageNode node, List<StageId> targets, bool required, bool before)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    StageId target = targets[i];
                    if (target.Equals(node.Id))
                    {
                        AddStageCycle(
                            new List<StageId> { node.Id, node.Id },
                            "A stage cannot depend on itself (P-040).");
                        continue;
                    }

                    if (!byStageId.TryGetValue(target.Value, out StageNode? other))
                    {
                        if (!required)
                        {
                            // Optional edges disappear when their endpoint is absent (P-039).
                            continue;
                        }

                        Add(new ScheduleWitness(
                            ScheduleWitnessKind.RequiredStageEdgeMissing,
                            DiagnosticCode.MissingDependency,
                            "A required stage edge names a stage that is not part of the active declaration set; a "
                            + "required dependency on an absent stage rejects assembly (P-039).",
                            stage: node.Id,
                            relatedStage: target));
                        continue;
                    }

                    if (before)
                    {
                        AddStageEdge(node, other, required, declared: true);
                    }
                    else
                    {
                        AddStageEdge(other, node, required, declared: true);
                    }
                }
            }

            private void ResolveInnerSystemGraph()
            {
                for (int i = 0; i < stages.Count; i++)
                {
                    StageNode node = stages[i];
                    for (int s = 0; s < node.Systems.Count; s++)
                    {
                        SystemNode system = node.Systems[s];
                        ResolveInnerEdges(node, system, system.Declaration.RequiredBeforeSystems, true, true);
                        ResolveInnerEdges(node, system, system.Declaration.RequiredAfterSystems, true, false);
                        ResolveInnerEdges(node, system, system.Declaration.OptionalBeforeSystems, false, true);
                        ResolveInnerEdges(node, system, system.Declaration.OptionalAfterSystems, false, false);
                    }
                }
            }

            private void ResolveInnerEdges(
                StageNode node,
                SystemNode system,
                IReadOnlyList<FactoryKey> targets,
                bool required,
                bool before)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    FactoryKey target = targets[i];
                    if (target.Equals(system.Key))
                    {
                        AddSystemCycle(
                            node,
                            new List<FactoryKey> { system.Key, system.Key },
                            "A system cannot depend on itself (P-039).");
                        continue;
                    }

                    if (!node.SystemsByKey.TryGetValue(target, out SystemNode? other))
                    {
                        if (!required)
                        {
                            continue;
                        }

                        Add(new ScheduleWitness(
                            ScheduleWitnessKind.RequiredSystemEdgeMissing,
                            DiagnosticCode.MissingDependency,
                            "A required inner system edge names a system that this stage does not declare (P-039).",
                            stage: node.Id,
                            system: system.Key,
                            relatedSystem: target));
                        continue;
                    }

                    if (before)
                    {
                        AddInnerEdge(system, other, required);
                    }
                    else
                    {
                        AddInnerEdge(other, system, required);
                    }
                }
            }

            // ------------------------------------------------------------- phase 2: buffers and playback

            private void ResolveBuffers()
            {
                var specsById = new Dictionary<Id128, BufferSpec>();
                var ordered = new List<BufferId>();

                for (int i = 0; i < declarations.Buffers.Count; i++)
                {
                    BufferSpec spec = declarations.Buffers[i];
                    if (specsById.ContainsKey(spec.BufferId.Value))
                    {
                        Add(new ScheduleWitness(
                            ScheduleWitnessKind.DuplicateBufferContract,
                            DiagnosticCode.OwnershipConflict,
                            "Two buffer contracts declare the same buffer id; a buffer has exactly one contract with "
                            + "one consuming owner (P-043).",
                            buffer: spec.BufferId));
                        continue;
                    }

                    specsById.Add(spec.BufferId.Value, spec);
                    ordered.Add(spec.BufferId);
                }

                ordered.Sort();
                ValidateDeclaredPorts(specsById);

                for (int i = 0; i < ordered.Count; i++)
                {
                    ResolveBufferContract(specsById[ordered[i].Value]);
                }
            }

            private void ValidateDeclaredPorts(Dictionary<Id128, BufferSpec> specsById)
            {
                for (int i = 0; i < stages.Count; i++)
                {
                    StageNode node = stages[i];
                    for (int p = 0; p < node.Ports.Count; p++)
                    {
                        BufferPort port = node.Ports[p];
                        if (!specsById.TryGetValue(port.Buffer.Value, out BufferSpec? spec))
                        {
                            Add(new ScheduleWitness(
                                ScheduleWitnessKind.BufferPortMissing,
                                DiagnosticCode.MissingDependency,
                                "This stage declares a buffer port whose buffer has no contract; a port without a "
                                + "declared schema, lifetime, capacity and drain policy is incomplete (P-043).",
                                stage: node.Id,
                                buffer: port.Buffer,
                                portDirection: port.Direction));
                            continue;
                        }

                        if (!port.Stage.Equals(node.Id))
                        {
                            Add(new ScheduleWitness(
                                ScheduleWitnessKind.BufferPortDirectionMismatch,
                                DiagnosticCode.OwnershipConflict,
                                "This port is declared on a stage other than the one that carries it; a buffer port "
                                + "belongs to its own stage's declaration (P-039, P-043).",
                                stage: node.Id,
                                relatedStage: port.Stage,
                                buffer: port.Buffer,
                                portDirection: port.Direction));
                            continue;
                        }

                        if (port.Direction == PortDirection.Consumer)
                        {
                            if (!spec.ConsumerStage.Equals(node.Id))
                            {
                                Add(new ScheduleWitness(
                                    ScheduleWitnessKind.BufferPortDirectionMismatch,
                                    DiagnosticCode.OwnershipConflict,
                                    "This stage declares a consumer port for a buffer whose contract names another "
                                    + "consuming stage; a buffer has exactly one consuming owner (P-043).",
                                    stage: node.Id,
                                    relatedStage: spec.ConsumerStage,
                                    buffer: port.Buffer,
                                    portDirection: port.Direction));
                            }

                            continue;
                        }

                        if (!StageProduces(spec, node))
                        {
                            Add(new ScheduleWitness(
                                ScheduleWitnessKind.BufferPortDirectionMismatch,
                                DiagnosticCode.OwnershipConflict,
                                "This stage declares a producer port for a buffer whose contract lists none of this "
                                + "stage's systems as producers (P-043).",
                                stage: node.Id,
                                buffer: port.Buffer,
                                portDirection: port.Direction));
                        }
                    }
                }
            }

            private static bool StageProduces(BufferSpec spec, StageNode node)
            {
                for (int i = 0; i < spec.Producers.Count; i++)
                {
                    if (node.SystemsByKey.ContainsKey(spec.Producers[i]))
                    {
                        return true;
                    }
                }

                return false;
            }

            private void ResolveBufferContract(BufferSpec spec)
            {
                var producerStages = new List<StageNode>();
                var producerKeys = new List<FactoryKey>();
                for (int i = 0; i < spec.Producers.Count; i++)
                {
                    FactoryKey producer = spec.Producers[i];
                    if (!bySystemKey.TryGetValue(producer, out StageNode? producerStage))
                    {
                        // A declared producer whose system is not active produces nothing in this schedule; a
                        // consumer may still read a declared empty stream, which 03 section 3 allows explicitly.
                        continue;
                    }

                    producerKeys.Add(producer);
                    if (!ContainsStage(producerStages, producerStage))
                    {
                        producerStages.Add(producerStage);
                    }
                }

                producerKeys.Sort(FactoryKeyComparer.Instance);
                producerStages.Sort(CompareStages);

                if (!byStageId.TryGetValue(spec.ConsumerStage.Value, out StageNode? consumer))
                {
                    if (producerStages.Count > 0)
                    {
                        // Reliable produced data with no consuming stage would be dropped at commit (P-043).
                        Add(new ScheduleWitness(
                            ScheduleWitnessKind.BufferConsumerMissing,
                            DiagnosticCode.MissingDependency,
                            "A buffer with active producer systems declares a consuming stage that is not in the "
                            + "active declaration set; reliable produced data must have a consumer (P-043).",
                            relatedStage: spec.ConsumerStage,
                            buffer: spec.BufferId));
                    }

                    return;
                }

                if (!byStageId.TryGetValue(spec.OwnerStage.Value, out StageNode? owner))
                {
                    Add(new ScheduleWitness(
                        ScheduleWitnessKind.BufferOwnerMissing,
                        DiagnosticCode.MissingDependency,
                        "A buffer whose consumer is active declares an owning stage that is not in the active "
                        + "declaration set; the buffer's lifetime owner must be scheduled (P-043).",
                        relatedStage: spec.OwnerStage,
                        buffer: spec.BufferId));
                    return;
                }

                for (int i = 0; i < producerStages.Count; i++)
                {
                    StageNode producerStage = producerStages[i];
                    if (!producerStage.Id.Equals(consumer.Id))
                    {
                        AddStageEdge(producerStage, consumer, true, declared: false);
                    }
                }

                if (!owner.Id.Equals(consumer.Id))
                {
                    AddStageEdge(owner, consumer, true, declared: false);
                }

                if (producerStages.Count > 0)
                {
                    pendingPlayback.Add(new PendingPlayback(spec, owner, consumer, producerStages, producerKeys));
                }
            }

            // ------------------------------------------------------------- phase 3: cycles

            private void DetectStageCycles()
            {
                int count = stages.Count;
                var color = new int[count];
                var stack = new List<StageNode>();
                var onStack = new Dictionary<Id128, int>();
                var reported = new HashSet<string>();

                for (int i = 0; i < count; i++)
                {
                    if (color[i] == 0)
                    {
                        VisitStage(i, color, stack, onStack, reported);
                    }
                }
            }

            private void VisitStage(
                int index,
                int[] color,
                List<StageNode> stack,
                Dictionary<Id128, int> onStack,
                HashSet<string> reported)
            {
                color[index] = 1;
                StageNode node = stages[index];
                onStack[node.Id.Value] = stack.Count;
                stack.Add(node);

                List<StageNode> successors = node.Successors;
                for (int i = 0; i < successors.Count; i++)
                {
                    StageNode next = successors[i];
                    if (color[next.CanonicalIndex] == 1)
                    {
                        int start = onStack[next.Id.Value];
                        var path = new List<StageId>();
                        for (int p = start; p < stack.Count; p++)
                        {
                            path.Add(stack[p].Id);
                        }

                        path.Add(next.Id);
                        string key = CycleKey(path);
                        if (reported.Add(key))
                        {
                            AddStageCycle(
                                path,
                                "The stage dependency graph contains a cycle; the path names the declarations that "
                                + "close it (P-040).");
                        }

                        continue;
                    }

                    if (color[next.CanonicalIndex] == 0)
                    {
                        VisitStage(next.CanonicalIndex, color, stack, onStack, reported);
                    }
                }

                stack.RemoveAt(stack.Count - 1);
                onStack.Remove(node.Id.Value);
                color[index] = 2;
            }

            private void DetectSystemCycles()
            {
                for (int s = 0; s < stages.Count; s++)
                {
                    StageNode stage = stages[s];
                    int count = stage.Systems.Count;
                    var color = new int[count];
                    var stack = new List<SystemNode>();
                    var onStack = new Dictionary<FactoryKey, int>();
                    var reported = new HashSet<string>();

                    // Roots are visited in canonical system-key order: the reported back edges (and therefore the
                    // witness list and its text) must not depend on the order the systems were declared in (P-008).
                    var roots = new List<SystemNode>(stage.Systems);
                    roots.Sort(CompareSystemsByKey);
                    for (int i = 0; i < roots.Count; i++)
                    {
                        if (color[roots[i].DeclarationIndex] == 0)
                        {
                            VisitSystem(stage, roots[i].DeclarationIndex, color, stack, onStack, reported);
                        }
                    }
                }
            }

            private void VisitSystem(
                StageNode stage,
                int index,
                int[] color,
                List<SystemNode> stack,
                Dictionary<FactoryKey, int> onStack,
                HashSet<string> reported)
            {
                color[index] = 1;
                SystemNode node = stage.Systems[index];
                onStack[node.Key] = stack.Count;
                stack.Add(node);

                List<SystemNode> successors = node.Successors;
                for (int i = 0; i < successors.Count; i++)
                {
                    SystemNode next = successors[i];
                    if (color[next.DeclarationIndex] == 1)
                    {
                        int start = onStack[next.Key];
                        var path = new List<FactoryKey>();
                        for (int p = start; p < stack.Count; p++)
                        {
                            path.Add(stack[p].Key);
                        }

                        path.Add(next.Key);
                        string key = SystemCycleKey(path);
                        if (reported.Add(key))
                        {
                            AddSystemCycle(
                                stage,
                                path,
                                "This stage's inner system graph contains a cycle; the path names the system "
                                + "declarations that close it (P-039, P-040).");
                        }

                        continue;
                    }

                    if (color[next.DeclarationIndex] == 0)
                    {
                        VisitSystem(stage, next.DeclarationIndex, color, stack, onStack, reported);
                    }
                }

                stack.RemoveAt(stack.Count - 1);
                onStack.Remove(node.Key);
                color[index] = 2;
            }

            // ------------------------------------------------------------- phase 4: canonical order

            private void OrderStages()
            {
                var ready = new List<StageNode>();
                for (int i = 0; i < stages.Count; i++)
                {
                    StageNode node = stages[i];
                    node.RemainingInDegree = node.Incoming.Count;
                    if (node.RemainingInDegree == 0)
                    {
                        ready.Add(node);
                    }
                }

                int next = 0;
                while (ready.Count > 0)
                {
                    StageNode pick = ready[0];
                    for (int i = 1; i < ready.Count; i++)
                    {
                        if (ready[i].Id.CompareTo(pick.Id) < 0)
                        {
                            pick = ready[i];
                        }
                    }

                    ready.Remove(pick);
                    pick.StageIndex = next;
                    next++;

                    List<StageNode> successors = pick.Successors;
                    for (int i = 0; i < successors.Count; i++)
                    {
                        StageNode successor = successors[i];
                        successor.RemainingInDegree--;
                        if (successor.RemainingInDegree == 0)
                        {
                            ready.Add(successor);
                        }
                    }
                }

                if (next != stages.Count)
                {
                    Add(new ScheduleWitness(
                        ScheduleWitnessKind.StageCycle,
                        DiagnosticCode.Cycle,
                        "The stage graph could not be ordered: "
                        + (stages.Count - next).ToString(CultureInfo.InvariantCulture)
                        + " stages remain unresolvable (P-040)."));
                }
            }

            private void OrderSystems()
            {
                for (int s = 0; s < stages.Count; s++)
                {
                    StageNode stage = stages[s];
                    var ready = new List<SystemNode>();
                    for (int i = 0; i < stage.Systems.Count; i++)
                    {
                        SystemNode system = stage.Systems[i];
                        system.RemainingInDegree = system.Incoming.Count;
                        if (system.RemainingInDegree == 0)
                        {
                            ready.Add(system);
                        }
                    }

                    int next = 0;
                    while (ready.Count > 0)
                    {
                        SystemNode pick = ready[0];
                        for (int i = 1; i < ready.Count; i++)
                        {
                            if (FactoryKeyComparer.Instance.Compare(ready[i].Key, pick.Key) < 0)
                            {
                                pick = ready[i];
                            }
                        }

                        ready.Remove(pick);
                        pick.OrderInStage = next;
                        next++;

                        List<SystemNode> successors = pick.Successors;
                        for (int i = 0; i < successors.Count; i++)
                        {
                            SystemNode successor = successors[i];
                            successor.RemainingInDegree--;
                            if (successor.RemainingInDegree == 0)
                            {
                                ready.Add(successor);
                            }
                        }
                    }

                    if (next != stage.Systems.Count)
                    {
                        Add(new ScheduleWitness(
                            ScheduleWitnessKind.SystemCycle,
                            DiagnosticCode.Cycle,
                            "This stage's systems could not be ordered: "
                            + (stage.Systems.Count - next).ToString(CultureInfo.InvariantCulture)
                            + " systems remain unresolvable (P-040).",
                            stage: stage.Id));
                    }
                }
            }

            private void BuildFlatOrder()
            {
                flatOrder.Clear();
                for (int index = 0; index < stages.Count; index++)
                {
                    var ordered = new List<SystemNode>(StageAt(index).Systems);
                    ordered.Sort(CompareSystemsInStage);
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        flatOrder.Add(ordered[i]);
                    }
                }
            }

            // ------------------------------------------------------------- phase 5: access validation

            private void ValidateAccessOrder()
            {
                int stageCount = stages.Count;
                if (stageCount == 0)
                {
                    return;
                }

                var stageReach = new bool[stageCount, stageCount];
                for (int i = 0; i < stageCount; i++)
                {
                    StageNode stage = StageAt(i);
                    List<StageNode> successors = stage.Successors;
                    for (int s = 0; s < successors.Count; s++)
                    {
                        stageReach[stage.StageIndex, successors[s].StageIndex] = true;
                    }
                }

                for (int k = 0; k < stageCount; k++)
                {
                    for (int i = 0; i < stageCount; i++)
                    {
                        if (!stageReach[i, k])
                        {
                            continue;
                        }

                        for (int j = 0; j < stageCount; j++)
                        {
                            if (stageReach[k, j])
                            {
                                stageReach[i, j] = true;
                            }
                        }
                    }
                }

                for (int index = 0; index < stageCount; index++)
                {
                    StageNode stage = StageAt(index);
                    int count = stage.Systems.Count;
                    if (count < 2)
                    {
                        continue;
                    }

                    var innerReach = new bool[count, count];
                    for (int i = 0; i < count; i++)
                    {
                        SystemNode system = stage.Systems[i];
                        List<SystemNode> successors = system.Successors;
                        for (int s = 0; s < successors.Count; s++)
                        {
                            innerReach[system.OrderInStage, successors[s].OrderInStage] = true;
                        }
                    }

                    for (int k = 0; k < count; k++)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            if (!innerReach[i, k])
                            {
                                continue;
                            }

                            for (int j = 0; j < count; j++)
                            {
                                if (innerReach[k, j])
                                {
                                    innerReach[i, j] = true;
                                }
                            }
                        }
                    }

                    stage.InnerReach = innerReach;
                }

                for (int i = 0; i < flatOrder.Count; i++)
                {
                    for (int j = i + 1; j < flatOrder.Count; j++)
                    {
                        SystemNode left = flatOrder[i];
                        SystemNode right = flatOrder[j];
                        if (IsOrdered(left, right, stageReach))
                        {
                            continue;
                        }

                        if (witnesses.Count >= MaxWitnesses)
                        {
                            truncated = true;
                            return;
                        }

                        ReportAccessConflicts(left, right);
                    }
                }
            }

            private static bool IsOrdered(SystemNode left, SystemNode right, bool[,] stageReach)
            {
                if (left.StageIndex == right.StageIndex)
                {
                    bool[,]? inner = left.Stage.InnerReach;
                    return inner != null && inner[left.OrderInStage, right.OrderInStage];
                }

                return stageReach[left.StageIndex, right.StageIndex];
            }

            private void ReportAccessConflicts(SystemNode left, SystemNode right)
            {
                for (int a = 0; a < left.Access.Count; a++)
                {
                    AccessDeclaration first = left.Access[a];
                    for (int b = 0; b < right.Access.Count; b++)
                    {
                        AccessDeclaration second = right.Access[b];
                        if (!first.Schema.Equals(second.Schema))
                        {
                            continue;
                        }

                        if (first.Mode == AccessMode.Read && second.Mode == AccessMode.Read)
                        {
                            continue;
                        }

                        if (first.IsPartitioned && second.IsPartitioned
                            && !first.PartitionId.Equals(second.PartitionId))
                        {
                            // Validated disjoint partitions may overlap without a semantic edge (P-034, P-040).
                            continue;
                        }

                        Add(new ScheduleWitness(
                            ScheduleWitnessKind.AmbiguousAccessOrder,
                            DiagnosticCode.AmbiguousOrder,
                            "These two systems overlap on one schema with no directed path between them and no proven "
                            + "disjoint partitions (" + first.Mode.ToString() + " vs " + second.Mode.ToString()
                            + "); the scheduler must not invent a gameplay order (P-040).",
                            stage: left.Stage.Id,
                            relatedStage: right.Stage.Id,
                            system: left.Key,
                            relatedSystem: right.Key,
                            schema: first.Schema,
                            mode: first.Mode,
                            relatedMode: second.Mode,
                            partition: first.PartitionId,
                            relatedPartition: second.PartitionId));

                        if (witnesses.Count >= MaxWitnesses)
                        {
                            truncated = true;
                            return;
                        }
                    }
                }
            }

            private void ValidatePlaybackOrder()
            {
                for (int i = 0; i < pendingPlayback.Count; i++)
                {
                    PendingPlayback point = pendingPlayback[i];
                    for (int p = 0; p < point.ProducerStages.Count; p++)
                    {
                        StageNode producer = point.ProducerStages[p];
                        if (producer.StageIndex == point.OwnerStageIndex)
                        {
                            continue;
                        }

                        if (!HasPath(producer, point.Owner))
                        {
                            Add(new ScheduleWitness(
                                ScheduleWitnessKind.PlaybackOrderUndefined,
                                DiagnosticCode.AmbiguousOrder,
                                "This buffer's producing stage is not ordered before its owning stage, so its "
                                + "deferred playback point has no defined position (P-040, P-041).",
                                stage: point.Owner.Id,
                                relatedStage: producer.Id,
                                buffer: point.Spec.BufferId));
                        }
                    }
                }
            }

            private bool HasPath(StageNode from, StageNode to)
            {
                var visited = new bool[stages.Count];
                var queue = new List<StageNode> { from };
                visited[from.StageIndex] = true;
                int head = 0;
                while (head < queue.Count)
                {
                    StageNode current = queue[head];
                    head++;
                    List<StageNode> successors = current.Successors;
                    for (int i = 0; i < successors.Count; i++)
                    {
                        StageNode next = successors[i];
                        if (next.StageIndex == to.StageIndex)
                        {
                            return true;
                        }

                        if (!visited[next.StageIndex])
                        {
                            visited[next.StageIndex] = true;
                            queue.Add(next);
                        }
                    }
                }

                return false;
            }

            // ------------------------------------------------------------- phase 6: output

            private CompiledSchedule BuildSchedule()
            {
                var entriesByStage = new List<ScheduleEntry>[stages.Count];
                var playbackByStage = new List<SchedulePlaybackPoint>[stages.Count];
                var allEntries = new List<ScheduleEntry>();

                for (int index = 0; index < stages.Count; index++)
                {
                    StageNode stage = StageAt(index);
                    var ordered = new List<SystemNode>(stage.Systems);
                    ordered.Sort(CompareSystemsInStage);

                    var entries = new List<ScheduleEntry>(ordered.Count);
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        var entry = new ScheduleEntry(
                            allEntries.Count,
                            stage.StageIndex,
                            stage.Id,
                            ordered[i].Key,
                            ordered[i].Declaration.Multiplicity,
                            new AccessSet(CanonicalAccess(ordered[i].Access)),
                            PredecessorSystemKeys(ordered[i]),
                            PredecessorStageIndexes(stage));
                        entries.Add(entry);
                        allEntries.Add(entry);
                    }

                    entriesByStage[index] = entries;
                }

                var orderedPlayback = new List<PendingPlayback>(pendingPlayback);
                orderedPlayback.Sort(ComparePlayback);
                var playbackPoints = new List<SchedulePlaybackPoint>(orderedPlayback.Count);
                for (int i = 0; i < orderedPlayback.Count; i++)
                {
                    SchedulePlaybackPoint point = orderedPlayback[i].Build(i);
                    playbackPoints.Add(point);
                    int owner = point.OwnerStageIndex;
                    if (playbackByStage[owner] == null)
                    {
                        playbackByStage[owner] = new List<SchedulePlaybackPoint>();
                    }

                    playbackByStage[owner]!.Add(point);
                }

                var bindings = new List<BufferBinding>(pendingPlayback.Count);
                for (int i = 0; i < pendingPlayback.Count; i++)
                {
                    PendingPlayback point = pendingPlayback[i];
                    bindings.Add(new BufferBinding(point.Spec.BufferId, point.ProducerKeys, point.Spec.ConsumerStage));
                }

                bindings.Sort(BufferBindingComparer.Instance);

                var edges = new List<ScheduleStageEdge>();
                for (int i = 0; i < stages.Count; i++)
                {
                    StageNode stage = stages[i];
                    for (int e = 0; e < stage.Outgoing.Count; e++)
                    {
                        StageEdgeRef edge = stage.Outgoing[e];
                        edges.Add(new ScheduleStageEdge(
                            edge.From.StageIndex,
                            edge.To.StageIndex,
                            edge.From.Id,
                            edge.To.Id,
                            edge.Kind,
                            edge.Required));
                    }
                }

                edges.Sort(CompareEdges);

                var scheduleStages = new List<ScheduleStage>(stages.Count);
                for (int index = 0; index < stages.Count; index++)
                {
                    StageNode stage = StageAt(index);
                    List<SchedulePlaybackPoint>? stagePlayback = playbackByStage[index];
                    scheduleStages.Add(new ScheduleStage(
                        stage.StageIndex,
                        stage.Id,
                        stage.Version,
                        stage.OwnerPackage,
                        stage.Affinity,
                        new AccessSet(CanonicalAccess(stage.Access)),
                        SortedKeys(stage.FactoryKeys),
                        SortedIds(stage.ActivationMemberships),
                        PredecessorStageIndexes(stage),
                        entriesByStage[index],
                        stagePlayback == null ? null : new List<SchedulePlaybackPoint>(stagePlayback)));
                }

                ContentHash hash = ScheduleHash.Compute(scheduleStages, edges, playbackPoints, bindings);
                ExecutionPlan plan = BuildExecutionPlan(scheduleStages, edges, hash);
                return new CompiledSchedule(scheduleStages, allEntries, edges, playbackPoints, bindings, plan);
            }

            private ExecutionPlan BuildExecutionPlan(
                List<ScheduleStage> scheduleStages,
                List<ScheduleStageEdge> edges,
                ContentHash hash)
            {
                var nodes = new List<PlanNode>();
                for (int s = 0; s < scheduleStages.Count; s++)
                {
                    ScheduleStage stage = scheduleStages[s];
                    nodes.Add(PlanNode.ForStage(stage.Stage));
                    for (int i = 0; i < stage.Systems.Count; i++)
                    {
                        nodes.Add(PlanNode.ForSystem(stage.Stage, stage.Systems[i].SystemKey));
                    }
                }

                var planEdges = new List<PlanEdge>();
                for (int i = 0; i < edges.Count; i++)
                {
                    ScheduleStageEdge edge = edges[i];
                    planEdges.Add(new PlanEdge(PlanNode.ForStage(edge.From), PlanNode.ForStage(edge.To), edge.Required));
                }

                for (int i = 0; i < flatOrder.Count; i++)
                {
                    SystemNode system = flatOrder[i];
                    List<SystemNode> successors = system.Successors;
                    for (int e = 0; e < successors.Count; e++)
                    {
                        planEdges.Add(new PlanEdge(
                            PlanNode.ForSystem(system.Stage.Id, system.Key),
                            PlanNode.ForSystem(successors[e].Stage.Id, successors[e].Key),
                            true));
                    }
                }

                planEdges.Sort(ComparePlanEdges);
                return new ExecutionPlan(nodes, planEdges, hash);
            }

            // ------------------------------------------------------------- graph and list helpers

            private StageNode StageAt(int stageIndex)
            {
                for (int i = 0; i < stages.Count; i++)
                {
                    if (stages[i].StageIndex == stageIndex)
                    {
                        return stages[i];
                    }
                }

                throw new InvalidOperationException(
                    "Stage index " + stageIndex.ToString(CultureInfo.InvariantCulture) + " was never assigned.");
            }

            private void AddStageEdge(StageNode from, StageNode to, bool required, bool declared)
            {
                for (int i = 0; i < from.Outgoing.Count; i++)
                {
                    StageEdgeRef existing = from.Outgoing[i];
                    if (!existing.To.Id.Equals(to.Id))
                    {
                        continue;
                    }

                    from.Outgoing[i] = new StageEdgeRef(
                        from,
                        to,
                        existing.Required || required,
                        existing.Kind == ScheduleEdgeKind.Declared || declared);
                    return;
                }

                from.Outgoing.Add(new StageEdgeRef(from, to, required, declared));
            }

            private static void AddInnerEdge(SystemNode from, SystemNode to, bool required)
            {
                for (int i = 0; i < from.Outgoing.Count; i++)
                {
                    SystemEdgeRef existing = from.Outgoing[i];
                    if (!existing.To.Key.Equals(to.Key))
                    {
                        continue;
                    }

                    from.Outgoing[i] = new SystemEdgeRef(from, to, existing.Required || required);
                    return;
                }

                from.Outgoing.Add(new SystemEdgeRef(from, to, required));
            }

            private void RebuildStageAdjacency()
            {
                for (int i = 0; i < stages.Count; i++)
                {
                    stages[i].Incoming.Clear();
                }

                for (int i = 0; i < stages.Count; i++)
                {
                    StageNode node = stages[i];
                    node.ClearSuccessors();
                    for (int e = 0; e < node.Outgoing.Count; e++)
                    {
                        StageNode target = node.Outgoing[e].To;
                        node.AddSuccessor(target);
                        target.AddIncoming(node);
                    }
                }

                for (int i = 0; i < stages.Count; i++)
                {
                    stages[i].SortAdjacency();
                }
            }

            private void RebuildSystemAdjacency()
            {
                for (int i = 0; i < stages.Count; i++)
                {
                    StageNode stage = stages[i];
                    for (int s = 0; s < stage.Systems.Count; s++)
                    {
                        stage.Systems[s].Incoming.Clear();
                    }
                }

                for (int i = 0; i < stages.Count; i++)
                {
                    StageNode stage = stages[i];
                    for (int s = 0; s < stage.Systems.Count; s++)
                    {
                        SystemNode node = stage.Systems[s];
                        node.ClearSuccessors();
                        for (int e = 0; e < node.Outgoing.Count; e++)
                        {
                            SystemNode target = node.Outgoing[e].To;
                            node.AddSuccessor(target);
                            target.AddIncoming(node);
                        }
                    }
                }

                for (int i = 0; i < stages.Count; i++)
                {
                    StageNode stage = stages[i];
                    for (int s = 0; s < stage.Systems.Count; s++)
                    {
                        stage.Systems[s].SortAdjacency();
                    }
                }
            }

            private static bool ContainsStage(List<StageNode> list, StageNode node)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Id.Equals(node.Id))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static int CompareSystemsInStage(SystemNode left, SystemNode right)
                => left.OrderInStage.CompareTo(right.OrderInStage);

            private static int CompareSystemsByKey(SystemNode left, SystemNode right)
                => FactoryKeyComparer.Instance.Compare(left.Key, right.Key);

            private static int CompareEdges(ScheduleStageEdge left, ScheduleStageEdge right)
            {
                int order = left.FromIndex.CompareTo(right.FromIndex);
                if (order != 0)
                {
                    return order;
                }

                order = left.ToIndex.CompareTo(right.ToIndex);
                if (order != 0)
                {
                    return order;
                }

                order = ((int)left.Kind).CompareTo((int)right.Kind);
                return order != 0 ? order : left.Required.CompareTo(right.Required);
            }

            private static int ComparePlanEdges(PlanEdge left, PlanEdge right)
            {
                int order = ((int)left.From.Kind).CompareTo((int)right.From.Kind);
                if (order != 0)
                {
                    return order;
                }

                order = left.From.Stage.CompareTo(right.From.Stage);
                if (order != 0)
                {
                    return order;
                }

                order = FactoryKeyComparer.Instance.Compare(left.From.System, right.From.System);
                if (order != 0)
                {
                    return order;
                }

                order = ((int)left.To.Kind).CompareTo((int)right.To.Kind);
                if (order != 0)
                {
                    return order;
                }

                order = left.To.Stage.CompareTo(right.To.Stage);
                if (order != 0)
                {
                    return order;
                }

                order = FactoryKeyComparer.Instance.Compare(left.To.System, right.To.System);
                return order != 0 ? order : left.Required.CompareTo(right.Required);
            }

            private static int ComparePlayback(PendingPlayback left, PendingPlayback right)
            {
                int order = left.OwnerStageIndex.CompareTo(right.OwnerStageIndex);
                return order != 0 ? order : left.Spec.BufferId.CompareTo(right.Spec.BufferId);
            }

            private static List<int> PredecessorStageIndexes(StageNode stage)
            {
                var indexes = new List<int>();
                for (int i = 0; i < stage.Incoming.Count; i++)
                {
                    indexes.Add(stage.Incoming[i].StageIndex);
                }

                indexes.Sort();
                return indexes;
            }

            private static List<FactoryKey> PredecessorSystemKeys(SystemNode system)
            {
                var keys = new List<FactoryKey>();
                for (int i = 0; i < system.Incoming.Count; i++)
                {
                    keys.Add(system.Incoming[i].Key);
                }

                keys.Sort(FactoryKeyComparer.Instance);
                return keys;
            }

            private static List<FactoryKey> SortedKeys(List<FactoryKey> keys)
            {
                var copy = new List<FactoryKey>(keys);
                copy.Sort(FactoryKeyComparer.Instance);
                return copy;
            }

            private static List<Id128> SortedIds(List<Id128> ids)
            {
                var copy = new List<Id128>(ids);
                copy.Sort();
                return copy;
            }

            private static List<AccessDeclaration> CanonicalAccess(AccessSet source)
            {
                var copy = new List<AccessDeclaration>(source.Declarations.Count);
                for (int i = 0; i < source.Declarations.Count; i++)
                {
                    copy.Add(source.Declarations[i]);
                }

                return CanonicalAccess(copy);
            }

            private static List<AccessDeclaration> CanonicalAccess(List<AccessDeclaration> source)
            {
                var copy = new List<AccessDeclaration>();
                for (int i = 0; i < source.Count; i++)
                {
                    bool known = false;
                    for (int c = 0; c < copy.Count; c++)
                    {
                        if (copy[c].Equals(source[i]))
                        {
                            known = true;
                            break;
                        }
                    }

                    if (!known)
                    {
                        copy.Add(source[i]);
                    }
                }

                copy.Sort(AccessDeclarationComparer.Instance);
                return copy;
            }

            private static void AddDistinctKey(List<FactoryKey> target, IReadOnlyList<FactoryKey> source)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    if (!target.Contains(source[i]))
                    {
                        target.Add(source[i]);
                    }
                }
            }

            private static void AddDistinctId(List<Id128> target, IReadOnlyList<Id128> source)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    if (!target.Contains(source[i]))
                    {
                        target.Add(source[i]);
                    }
                }
            }

            private static void AddDistinctStage(List<StageId> target, IReadOnlyList<StageId> source)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    if (!target.Contains(source[i]))
                    {
                        target.Add(source[i]);
                    }
                }
            }

            private static bool SameDeclarations(List<AccessDeclaration> left, List<AccessDeclaration> right)
            {
                if (left.Count != right.Count)
                {
                    return false;
                }

                for (int i = 0; i < left.Count; i++)
                {
                    if (!left[i].Equals(right[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            private static bool SameKeys(IReadOnlyList<FactoryKey> left, IReadOnlyList<FactoryKey> right)
            {
                if (left.Count != right.Count)
                {
                    return false;
                }

                var leftCopy = new List<FactoryKey>(left);
                var rightCopy = new List<FactoryKey>(right);
                leftCopy.Sort(FactoryKeyComparer.Instance);
                rightCopy.Sort(FactoryKeyComparer.Instance);
                for (int i = 0; i < leftCopy.Count; i++)
                {
                    if (!leftCopy[i].Equals(rightCopy[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            private static string CycleKey(List<StageId> path)
            {
                int length = path.Count - 1;
                if (length <= 0)
                {
                    return string.Empty;
                }

                int best = 0;
                for (int i = 1; i < length; i++)
                {
                    if (path[i].CompareTo(path[best]) < 0)
                    {
                        best = i;
                    }
                }

                var text = new StringBuilder();
                for (int i = 0; i < length; i++)
                {
                    text.Append(path[(best + i) % length].ToString()).Append('|');
                }

                return text.ToString();
            }

            private static string SystemCycleKey(List<FactoryKey> path)
            {
                int length = path.Count - 1;
                if (length <= 0)
                {
                    return string.Empty;
                }

                int best = 0;
                for (int i = 1; i < length; i++)
                {
                    if (FactoryKeyComparer.Instance.Compare(path[i], path[best]) < 0)
                    {
                        best = i;
                    }
                }

                var text = new StringBuilder();
                for (int i = 0; i < length; i++)
                {
                    text.Append(path[(best + i) % length].ToString()).Append('|');
                }

                return text.ToString();
            }

            private void Add(ScheduleWitness witness)
            {
                if (witnesses.Count >= MaxWitnesses)
                {
                    truncated = true;
                    return;
                }

                witnesses.Add(witness);
            }

            private void AddStageCycle(List<StageId> path, string detail)
            {
                Add(new ScheduleWitness(
                    ScheduleWitnessKind.StageCycle,
                    DiagnosticCode.Cycle,
                    detail,
                    stage: path[0],
                    cyclePath: path));
            }

            private void AddSystemCycle(StageNode stage, List<FactoryKey> path, string detail)
            {
                Add(new ScheduleWitness(
                    ScheduleWitnessKind.SystemCycle,
                    DiagnosticCode.Cycle,
                    detail,
                    stage: stage.Id,
                    system: path[0],
                    systemCyclePath: path));
            }

            private ScheduleCompilation Reject(string detail)
            {
                witnesses.Sort(ScheduleWitnessComparer.Instance);

                IReadOnlyList<StageId> cyclePath = Array.Empty<StageId>();
                for (int i = 0; i < witnesses.Count; i++)
                {
                    if (witnesses[i].Kind == ScheduleWitnessKind.StageCycle && witnesses[i].CyclePath.Count > 0)
                    {
                        cyclePath = witnesses[i].CyclePath;
                        break;
                    }
                }

                DiagnosticCode code = witnesses.Count > 0 ? witnesses[0].Code : DiagnosticCode.AmbiguousOrder;
                return ScheduleCompilation.Rejected(code, witnesses, detail, cyclePath, truncated);
            }
        }

        // -------------------------------------------------------------------- internal node model

        private sealed class StageNode
        {
            internal StageNode(StageId id, uint version, Id128 ownerPackage, HostAffinity affinity, int canonicalIndex)
            {
                Id = id;
                Version = version;
                OwnerPackage = ownerPackage;
                Affinity = affinity;
                CanonicalIndex = canonicalIndex;
            }

            internal StageId Id { get; }

            internal uint Version { get; }

            internal Id128 OwnerPackage { get; }

            internal HostAffinity Affinity { get; }

            /// <summary>Position in the canonical stage-id order; cycle detection only, never execution order.</summary>
            internal int CanonicalIndex { get; }

            internal List<AccessDeclaration> Access { get; } = new List<AccessDeclaration>();

            internal List<FactoryKey> FactoryKeys { get; } = new List<FactoryKey>();

            internal List<Id128> ActivationMemberships { get; } = new List<Id128>();

            internal List<StageId> RequiredBefore { get; } = new List<StageId>();

            internal List<StageId> RequiredAfter { get; } = new List<StageId>();

            internal List<StageId> OptionalBefore { get; } = new List<StageId>();

            internal List<StageId> OptionalAfter { get; } = new List<StageId>();

            internal List<SystemNode> Systems { get; } = new List<SystemNode>();

            internal Dictionary<FactoryKey, SystemNode> SystemsByKey { get; } = new Dictionary<FactoryKey, SystemNode>();

            internal List<BufferPort> Ports { get; } = new List<BufferPort>();

            internal List<StageEdgeRef> Outgoing { get; } = new List<StageEdgeRef>();

            internal List<StageNode> Incoming { get; } = new List<StageNode>();

            internal List<StageNode> Successors { get; } = new List<StageNode>();

            internal int StageIndex { get; set; } = -1;

            internal int RemainingInDegree { get; set; }

            internal bool[,]? InnerReach { get; set; }

            internal void ClearSuccessors() => Successors.Clear();

            internal void AddSuccessor(StageNode node)
            {
                if (!Contains(Successors, node))
                {
                    Successors.Add(node);
                }
            }

            internal void AddIncoming(StageNode node)
            {
                if (!Contains(Incoming, node))
                {
                    Incoming.Add(node);
                }
            }

            internal void SortAdjacency()
            {
                Successors.Sort(Compare);
                Incoming.Sort(Compare);
            }

            private static bool Contains(List<StageNode> list, StageNode node)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Id.Equals(node.Id))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static int Compare(StageNode left, StageNode right) => left.Id.CompareTo(right.Id);
        }

        private sealed class SystemNode
        {
            internal SystemNode(FactoryKey key, SystemSpec declaration, StageNode stage, int declarationIndex)
            {
                Key = key;
                Declaration = declaration;
                Stage = stage;
                DeclarationIndex = declarationIndex;
            }

            internal FactoryKey Key { get; }

            internal SystemSpec Declaration { get; }

            internal StageNode Stage { get; }

            /// <summary>Index in declaration order inside the stage; cycle detection only.</summary>
            internal int DeclarationIndex { get; }

            internal List<AccessDeclaration> Access { get; } = new List<AccessDeclaration>();

            internal List<SystemEdgeRef> Outgoing { get; } = new List<SystemEdgeRef>();

            internal List<SystemNode> Incoming { get; } = new List<SystemNode>();

            internal List<SystemNode> Successors { get; } = new List<SystemNode>();

            internal int OrderInStage { get; set; }

            internal int RemainingInDegree { get; set; }

            internal void ClearSuccessors() => Successors.Clear();

            internal void AddSuccessor(SystemNode node)
            {
                if (!Contains(Successors, node))
                {
                    Successors.Add(node);
                }
            }

            internal void AddIncoming(SystemNode node)
            {
                if (!Contains(Incoming, node))
                {
                    Incoming.Add(node);
                }
            }

            internal void SortAdjacency()
            {
                Successors.Sort(Compare);
                Incoming.Sort(Compare);
            }

            private static bool Contains(List<SystemNode> list, SystemNode node)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Key.Equals(node.Key))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static int Compare(SystemNode left, SystemNode right)
                => FactoryKeyComparer.Instance.Compare(left.Key, right.Key);
        }

        private readonly struct StageEdgeRef
        {
            internal StageEdgeRef(StageNode from, StageNode to, bool required, bool declared)
            {
                From = from;
                To = to;
                Required = required;
                Kind = declared ? ScheduleEdgeKind.Declared : ScheduleEdgeKind.Buffer;
            }

            internal StageNode From { get; }

            internal StageNode To { get; }

            internal bool Required { get; }

            internal ScheduleEdgeKind Kind { get; }
        }

        private readonly struct SystemEdgeRef
        {
            internal SystemEdgeRef(SystemNode from, SystemNode to, bool required)
            {
                From = from;
                To = to;
                Required = required;
            }

            internal SystemNode From { get; }

            internal SystemNode To { get; }

            internal bool Required { get; }
        }

        private sealed class PendingPlayback
        {
            internal PendingPlayback(
                BufferSpec spec,
                StageNode owner,
                StageNode consumer,
                List<StageNode> producerStages,
                List<FactoryKey> producerKeys)
            {
                Spec = spec;
                Owner = owner;
                Consumer = consumer;
                ProducerStages = producerStages;
                ProducerKeys = producerKeys;
            }

            internal BufferSpec Spec { get; }

            internal StageNode Owner { get; }

            internal StageNode Consumer { get; }

            internal List<StageNode> ProducerStages { get; }

            internal List<FactoryKey> ProducerKeys { get; }

            internal int OwnerStageIndex => Owner.StageIndex;

            internal SchedulePlaybackPoint Build(int playbackIndex)
            {
                var producerIndexes = new List<int>();
                for (int i = 0; i < ProducerStages.Count; i++)
                {
                    int index = ProducerStages[i].StageIndex;
                    if (!producerIndexes.Contains(index))
                    {
                        producerIndexes.Add(index);
                    }
                }

                producerIndexes.Sort();

                var consumerSystems = new List<FactoryKey>();
                for (int i = 0; i < Consumer.Systems.Count; i++)
                {
                    consumerSystems.Add(Consumer.Systems[i].Key);
                }

                consumerSystems.Sort(FactoryKeyComparer.Instance);

                return new SchedulePlaybackPoint(
                    playbackIndex,
                    Spec.BufferId,
                    Spec.Schema,
                    Spec.OrderKey,
                    Spec.Lifetime,
                    Spec.Overflow,
                    Spec.Cancellation,
                    Spec.Capacity,
                    Owner.Id,
                    Owner.StageIndex,
                    Consumer.Id,
                    Consumer.StageIndex,
                    producerIndexes,
                    ProducerKeys,
                    consumerSystems);
            }
        }
    }
}
