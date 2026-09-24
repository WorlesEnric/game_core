#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution;
using Unity.Entities;

namespace GameCore.Unity.Runtime
{
    /// <summary>
    /// Where one dispatched entry is invoked, resolved from its generated registration key. The generated registry
    /// is the only resolver: there is no reflection lookup and no assembly scan at runtime (04 s3, 04 s8).
    /// </summary>
    public readonly struct SystemDispatchTarget
    {
        public readonly FactoryKey Key;
        public readonly SystemDispatchKind Kind;

        /// <summary>Managed <c>SystemBase</c> or adapter group for <see cref="SystemDispatchKind.ManagedSystem"/>
        /// and <see cref="SystemDispatchKind.InfrastructureGroup"/>.</summary>
        public readonly ComponentSystemBase? ManagedSystem;

        /// <summary>Unmanaged system handle for <see cref="SystemDispatchKind.UnmanagedSystem"/>.</summary>
        public readonly SystemHandle UnmanagedSystem;

        public SystemDispatchTarget(FactoryKey key, SystemDispatchKind kind, ComponentSystemBase? managedSystem, SystemHandle unmanagedSystem)
        {
            Key = key;
            Kind = kind;
            ManagedSystem = managedSystem;
            UnmanagedSystem = unmanagedSystem;
        }

        public bool IsResolved
        {
            get
            {
                if (Kind == SystemDispatchKind.UnmanagedSystem)
                {
                    return !UnmanagedSystem.Equals(default(SystemHandle));
                }

                return ManagedSystem != null;
            }
        }

        public override string ToString() => Kind + ":" + Key.ToString();
    }

    /// <summary>Key-to-instance resolution for one world's dispatch tables (04 s4).</summary>
    public interface ISystemDispatchCatalog
    {
        bool TryResolve(FactoryKey systemKey, out SystemDispatchTarget target);

        int Count { get; }
    }

    /// <summary>
    /// Per-world system registry. One precompiled system type has one live scheduling instance per world in V1, so
    /// a duplicate key is a registration defect and rejects instead of silently shadowing (04 s4, P-039).
    /// </summary>
    public sealed class SystemDispatchCatalog : ISystemDispatchCatalog
    {
        private readonly Dictionary<FactoryKey, SystemDispatchTarget> targets =
            new Dictionary<FactoryKey, SystemDispatchTarget>();

        public int Count => targets.Count;

        public void RegisterManaged(FactoryKey key, ComponentSystemBase system)
            => Register(new SystemDispatchTarget(key, SystemDispatchKind.ManagedSystem, system, default(SystemHandle)));

        public void RegisterInfrastructureGroup(FactoryKey key, ComponentSystemGroup group)
            => Register(new SystemDispatchTarget(key, SystemDispatchKind.InfrastructureGroup, group, default(SystemHandle)));

        public void RegisterUnmanaged(FactoryKey key, SystemHandle system)
            => Register(new SystemDispatchTarget(key, SystemDispatchKind.UnmanagedSystem, null, system));

        private void Register(SystemDispatchTarget target)
        {
            if (target.ManagedSystem == null && target.Kind != SystemDispatchKind.UnmanagedSystem)
            {
                throw new ArgumentException("A managed registration needs a live system instance.", nameof(target));
            }

            if (targets.ContainsKey(target.Key))
            {
                throw new InvalidOperationException(
                    "Factory key " + target.Key.ToString() + " is already registered in this world; one precompiled "
                    + "system type has one scheduling instance per world in V1 (04 s4).");
            }

            targets.Add(target.Key, target);
        }

        public bool TryResolve(FactoryKey systemKey, out SystemDispatchTarget target)
            => targets.TryGetValue(systemKey, out target);

        public bool Contains(FactoryKey systemKey) => targets.ContainsKey(systemKey);
    }

    /// <summary>One declared stage: its namespaced identity, its fence slot and its incoming stage edges (P-039).</summary>
    public sealed class StageRegistration
    {
        public StageRegistration(StageId stage, string diagnosticName, int stageIndex, IReadOnlyList<int>? predecessorStages)
        {
            Stage = stage;
            DiagnosticName = diagnosticName ?? string.Empty;
            StageIndex = stageIndex;
            PredecessorStages = ContractCollections.Freeze(predecessorStages);
        }

        public StageId Stage { get; }

        /// <summary>Namespaced diagnostic name for logs; never used for ordering (P-004, P-008).</summary>
        public string DiagnosticName { get; }

        public int StageIndex { get; }

        public IReadOnlyList<int> PredecessorStages { get; }

        public override string ToString() => DiagnosticName + "/" + Stage.ToString();
    }

    /// <summary>
    /// Generated-style system registration: a stable factory key plus a direct typed construction of the concrete
    /// system. A build-time generator emits these; the fixture writes them by hand, and no reflection is involved
    /// (04 s8).
    /// </summary>
    public abstract class SystemRegistration
    {
        protected SystemRegistration(FactoryKey key, StageId stage, SystemDispatchKind kind, string diagnosticName)
        {
            Key = key;
            Stage = stage;
            Kind = kind;
            DiagnosticName = diagnosticName ?? string.Empty;
        }

        public FactoryKey Key { get; }

        public StageId Stage { get; }

        public SystemDispatchKind Kind { get; }

        public string DiagnosticName { get; }

        /// <summary>Creates the world-owned instance and registers it under <see cref="Key"/>.</summary>
        public abstract bool TryCreate(World world, SystemDispatchCatalog catalog, out string failure);

        public override string ToString() => Kind + ":" + DiagnosticName;
    }

    /// <summary>Registers a managed <see cref="SystemBase"/> through its concrete type (no reflection).</summary>
    public sealed class ManagedSystemRegistration<TSystem> : SystemRegistration
        where TSystem : SystemBase, new()
    {
        public ManagedSystemRegistration(FactoryKey key, StageId stage, string diagnosticName)
            : base(key, stage, SystemDispatchKind.ManagedSystem, diagnosticName)
        {
        }

        public override bool TryCreate(World world, SystemDispatchCatalog catalog, out string failure)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            TSystem system = world.CreateSystemManaged<TSystem>();
            catalog.RegisterManaged(Key, system);
            failure = string.Empty;
            return true;
        }
    }

    /// <summary>Registers an unmanaged <see cref="ISystem"/> through its concrete type (no reflection).</summary>
    public sealed class UnmanagedSystemRegistration<TSystem> : SystemRegistration
        where TSystem : unmanaged, ISystem
    {
        public UnmanagedSystemRegistration(FactoryKey key, StageId stage, string diagnosticName)
            : base(key, stage, SystemDispatchKind.UnmanagedSystem, diagnosticName)
        {
        }

        public override bool TryCreate(World world, SystemDispatchCatalog catalog, out string failure)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            SystemHandle handle = world.CreateSystem<TSystem>();
            catalog.RegisterUnmanaged(Key, handle);
            failure = string.Empty;
            return true;
        }
    }

    /// <summary>
    /// Registers an adapter infrastructure group as a dispatchable entry. Infrastructure groups are not protocol
    /// stages: they are the host's ingress/step/output boundaries (04 s3).
    /// </summary>
    public sealed class InfrastructureGroupRegistration<TSystem> : SystemRegistration
        where TSystem : ComponentSystemGroup, new()
    {
        public InfrastructureGroupRegistration(FactoryKey key, StageId stage, string diagnosticName)
            : base(key, stage, SystemDispatchKind.InfrastructureGroup, diagnosticName)
        {
        }

        public override bool TryCreate(World world, SystemDispatchCatalog catalog, out string failure)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            TSystem group = world.CreateSystemManaged<TSystem>();
            catalog.RegisterInfrastructureGroup(Key, group);
            failure = string.Empty;
            return true;
        }
    }

    /// <summary>
    /// The application composition root of one protocol world: its generated-style system registrations, its three
    /// explicit dispatch tables and an optional world-state seeder. The bootstrap allowlist is exactly this list;
    /// loaded assemblies are never scanned for systems (04 s3).
    /// </summary>
    public sealed class UnityWorldRegistration
    {
        public UnityWorldRegistration(
            string worldName,
            IReadOnlyList<StageRegistration>? stages,
            IReadOnlyList<SystemRegistration>? systems,
            GuardedDispatchPlan ingressPlan,
            GuardedDispatchPlan stepPlan,
            GuardedDispatchPlan outputPlan,
            Action<World>? seedWorldState)
        {
            WorldName = string.IsNullOrEmpty(worldName) ? "GameCoreWorld" : worldName;
            Stages = ContractCollections.Freeze(stages);
            Systems = ContractCollections.Freeze(systems);
            IngressPlan = ingressPlan ?? throw new ArgumentNullException(nameof(ingressPlan));
            StepPlan = stepPlan ?? throw new ArgumentNullException(nameof(stepPlan));
            OutputPlan = outputPlan ?? throw new ArgumentNullException(nameof(outputPlan));
            SeedWorldState = seedWorldState;
        }

        public string WorldName { get; }

        public IReadOnlyList<StageRegistration> Stages { get; }

        public IReadOnlyList<SystemRegistration> Systems { get; }

        public GuardedDispatchPlan IngressPlan { get; }

        public GuardedDispatchPlan StepPlan { get; }

        public GuardedDispatchPlan OutputPlan { get; }

        /// <summary>Optional typed seeding of world-scoped entities; never discovered by reflection (04 s6).</summary>
        public Action<World>? SeedWorldState { get; }

        /// <summary>
        /// Registration validation: every plan is well formed, every plan entry resolves to exactly one
        /// registration with the same kind and stage, and every declared stage index is consistent (P-028, P-039).
        /// </summary>
        public bool TryValidate(out DiagnosticCode code, out string detail)
        {
            if (!ValidatePlan(IngressPlan, "ingress", out code, out detail) ||
                !ValidatePlan(StepPlan, "step", out code, out detail) ||
                !ValidatePlan(OutputPlan, "output", out code, out detail))
            {
                return false;
            }

            for (int i = 0; i < Systems.Count; i++)
            {
                SystemRegistration registration = Systems[i];
                int matches = 0;
                for (int s = 0; s < Stages.Count; s++)
                {
                    if (Stages[s].Stage.Equals(registration.Stage))
                    {
                        matches++;
                    }
                }

                if (matches != 1)
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "System " + registration.DiagnosticName
                        + " declares stage " + registration.Stage.ToString()
                        + " which has " + matches + " declared stage registrations; exactly one is required (P-039).";
                    return false;
                }
            }

            for (int s = 0; s < Stages.Count; s++)
            {
                if (Stages[s].StageIndex != s)
                {
                    code = DiagnosticCode.AmbiguousOrder;
                    detail = "Stage registrations must be declared in fence-index order; index " + s
                        + " holds stage index " + Stages[s].StageIndex + " (P-040).";
                    return false;
                }

                for (int p = 0; p < Stages[s].PredecessorStages.Count; p++)
                {
                    int predecessor = Stages[s].PredecessorStages[p];
                    if (predecessor < 0 || predecessor >= s)
                    {
                        code = DiagnosticCode.Cycle;
                        detail = "Stage " + Stages[s].DiagnosticName
                            + " declares predecessor index " + predecessor
                            + " which is not strictly before it; a backward-only stage edge is required (P-040).";
                        return false;
                    }
                }
            }

            code = DiagnosticCode.None;
            detail = string.Empty;
            return true;
        }

        private bool ValidatePlan(GuardedDispatchPlan plan, string planName, out DiagnosticCode code, out string detail)
        {
            if (!plan.TryValidate(out code, out detail))
            {
                detail = planName + " dispatch table: " + detail;
                return false;
            }

            for (int i = 0; i < plan.Entries.Count; i++)
            {
                GuardedDispatchEntry entry = plan.Entries[i];
                int matches = 0;
                for (int s = 0; s < Systems.Count; s++)
                {
                    SystemRegistration registration = Systems[s];
                    if (registration.Key.Equals(entry.SystemKey))
                    {
                        if (!registration.Stage.Equals(entry.Stage) || registration.Kind != entry.Kind)
                        {
                            code = DiagnosticCode.MissingDependency;
                            detail = planName + " dispatch table: registration " + registration.DiagnosticName
                                + " does not match the declared stage or kind of its dispatch entry (P-039).";
                            return false;
                        }

                        matches++;
                    }
                }

                if (matches != 1)
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = planName + " dispatch table: entry " + entry.DispatchIndex + " names system key "
                        + entry.SystemKey.ToString() + " with " + matches
                        + " generated registrations; exactly one is required (04 s8).";
                    return false;
                }
            }

            code = DiagnosticCode.None;
            detail = string.Empty;
            return true;
        }
    }
}
