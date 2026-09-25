// GameCore.Validation.ProbeHost — the W4 integration-gate family contract.
//
// The gate sentence this contract serves, from `docs/game-core/09-implementation-guide.md` (Wave 4):
//
//   "Run both families in IL2CPP, then integrate indexed move/mode changes, service-closure lifecycle and slot
//    policies. Demonstrate both mode directions, a subtree move preserving state, suspend/resume and provider
//    loss/unload. Provisional generic execution freeze requires GC-012; dynamic composition is still awaiting later
//    stress/fault completion."
//
// One runner, two family adapters. `W4GateScenario` owns the scripted sequence; a family owns only what its genre
// declares. Everything GC-013's own sequence already needed (catalog, scope tree, live targets, the provider to
// derive from, the branch to move, the two mode edits, the neutral scope creations) comes from `IGc013Family`, which
// the W4 gate's adapters implement unchanged: the W4 gate is an *integration* of GC-013's indexed move/mode changes
// with GC-014's service-closure lifecycle and GC-015's slot policies, not a fourth parallel implementation of any of
// them.
//
// This file therefore adds exactly the two halves `IGc013Family` does not have:
//
//   * the lifecycle half (P-011, P-012, P-046, P-047, P-048): the family provider the sequence suspends and resumes,
//     the required-service pair whose loss makes a consumer wait and whose return resumes it, the compatible
//     provider whose return does the resuming, the fourth installation whose unload is observed through
//     reverse-order disposal, and the mount/suspend/resume/unmount payload builders the runner submits through
//     `LifecycleController`. Every one of these is a real `PluginManifest` installed by the real lane, so the
//     required-dependency edge under test is a real `ServiceDependency` with `Required = true` resolved by the real
//     `ServiceResolver`;
//   * the state-policy half (P-020, P-025, P-029, P-032, P-033, P-034): the provider whose declared slots carry the
//     four last-support/reset policies, the manifests themselves (the reset permission is the manifest field GC-012
//     added, never a test-declared override), the initialization registry a declared reset reads its value from, the
//     values to seed, and the registered migrations the revision declares.
//
// Every member is data or a payload: the runner owns the ordering, the publications and the observations, so both
// genres are driven through exactly the same sequence (P-001).
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning;
using GameCore.Planning.StatePolicies;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// Which slot policy one <see cref="W4GateSlotCase"/> exercises: the four outcomes the W4 exit gate names
    /// (`Preserve`, `PreserveDormant`, `RemoveDerived`, `TransferTo`) plus the explicit manifest-supported reset
    /// P-032 requires a declaration field for.
    /// </summary>
    public enum W4GateSlotPolicy
    {
        /// <summary>An explicit `Preserve` request: the compatibility default, and the value must not move (P-032).</summary>
        Preserve = 0,

        /// <summary>The last support leaves and the declaration says `PreserveDormant` (P-032).</summary>
        PreserveDormant = 1,

        /// <summary>The last support leaves and the declaration says `RemoveDerived` (P-032, P-033).</summary>
        RemoveDerived = 2,

        /// <summary>The last support leaves and the declaration says `TransferTo` (P-032, P-025).</summary>
        TransferTo = 3,

        /// <summary>An explicit reset against a slot whose manifest declares reset support and a reason (P-032).</summary>
        Reset = 4,
    }

    /// <summary>
    /// One declared slot policy case: the live slot it acts on, the value it was seeded with, the request the runner
    /// submits for it and - for a transfer - the named available owner and destination target P-032 requires.
    /// </summary>
    public readonly struct W4GateSlotCase
    {
        public W4GateSlotCase(
            W4GateSlotPolicy policy,
            StateSlotKey slot,
            uint schemaVersion,
            int value,
            OwnerId destinationOwner,
            TargetId destinationTarget,
            string resetReason)
        {
            Policy = policy;
            Slot = slot;
            SchemaVersion = schemaVersion;
            Value = value;
            DestinationOwner = destinationOwner;
            DestinationTarget = destinationTarget;
            ResetReason = resetReason ?? string.Empty;
        }

        public W4GateSlotPolicy Policy { get; }

        /// <summary>The live slot key `(target, owner, slot)` the case is about (P-032).</summary>
        public StateSlotKey Slot { get; }

        /// <summary>Schema version the case's slot is seeded at and declared with.</summary>
        public uint SchemaVersion { get; }

        /// <summary>Non-default value the case's slot is seeded with; a policy that loses it fails.</summary>
        public int Value { get; }

        /// <summary>Declared destination owner of a `TransferTo` (P-032); default for the other policies.</summary>
        public OwnerId DestinationOwner { get; }

        /// <summary>Destination target of a `TransferTo`; default keeps the state on its own target.</summary>
        public TargetId DestinationTarget { get; }

        /// <summary>Reason the reset case's policy request carries; empty for the other policies (P-032).</summary>
        public string ResetReason { get; }

        /// <summary>Stable name of the case, used in the step details: `preserve`, `preserve-dormant`, etc.</summary>
        public string Name
        {
            get
            {
                switch (Policy)
                {
                    case W4GateSlotPolicy.Preserve:
                        return "preserve";
                    case W4GateSlotPolicy.PreserveDormant:
                        return "preserve-dormant";
                    case W4GateSlotPolicy.RemoveDerived:
                        return "remove-derived";
                    case W4GateSlotPolicy.TransferTo:
                        return "transfer-to";
                    default:
                        return "reset";
                }
            }
        }

        /// <summary>The destination key of a transfer: the destination target, or the case's own target by default.</summary>
        public StateSlotKey Destination =>
            new StateSlotKey(
                DestinationTarget.Value.IsDefault ? Slot.Target : DestinationTarget,
                DestinationOwner.Value.IsDefault ? Slot.Owner : DestinationOwner,
                Slot.Slot);

        /// <summary>The policy request this case submits (P-032).</summary>
        public StatePolicyRequest ToRequest()
        {
            switch (Policy)
            {
                case W4GateSlotPolicy.Preserve:
                    return StatePolicyRequest.Preserve(Slot);
                case W4GateSlotPolicy.PreserveDormant:
                    return StatePolicyRequest.PreserveDormant(Slot);
                case W4GateSlotPolicy.RemoveDerived:
                    return StatePolicyRequest.RemoveDerived(Slot);
                case W4GateSlotPolicy.TransferTo:
                    return StatePolicyRequest.LastSupportTransfer(Slot, DestinationOwner, DestinationTarget);
                default:
                    return StatePolicyRequest.Reset(Slot, ResetReason);
            }
        }

        public override string ToString() => Name + ":" + Slot.ToString();
    }

    /// <summary>
    /// One genre's declared facts for the W4 integration gate: everything <see cref="IGc013Family"/> declares, plus
    /// the lifecycle and state-policy surface the W4 sequence integrates with it.
    /// </summary>
    public interface IW4GateFamily : IGc013Family
    {
        // ------------------------------------------------------------------ the lifecycle half (GC-014)

        /// <summary>
        /// The family's own provider installation the sequence suspends and resumes: the installation whose
        /// activation carries the genre's real behavior (07 section 3.1's chapter one / 2.1's table runtime), so
        /// "suspend retracts behavior and resume restores it" is a statement about real attributed rows (P-046).
        /// </summary>
        PluginInstanceId LifecycleProviderInstall { get; }

        /// <summary>Scope the lifecycle provider is mounted at.</summary>
        ScopeId LifecycleProviderScope { get; }

        /// <summary>The manifest the lifecycle provider is mounted from: the family's own declaration.</summary>
        PluginManifest LifecycleProviderManifest { get; }

        /// <summary>Installation of the consumer of the required service (P-011).</summary>
        PluginInstanceId RequiredConsumerInstall { get; }

        /// <summary>Scope the required service's consumer is mounted at.</summary>
        ScopeId RequiredConsumerScope { get; }

        /// <summary>The consumer's manifest: its own contribution plus a required `ServiceDependency` (P-011).</summary>
        PluginManifest RequiredConsumerManifest { get; }

        /// <summary>Installation of the required service's provider, whose removal makes the consumer wait (P-012).</summary>
        PluginInstanceId RequiredProviderInstall { get; }

        /// <summary>Scope the required service's provider is mounted at.</summary>
        ScopeId RequiredProviderScope { get; }

        /// <summary>The provider's manifest: it exports the required contract (P-011).</summary>
        PluginManifest RequiredProviderManifest { get; }

        /// <summary>A compatible provider of the same contract: its return resumes the waiting consumer (P-012).</summary>
        PluginInstanceId RequiredProviderReplacementInstall { get; }

        /// <summary>The compatible provider's manifest: its own identities, the same exported contract.</summary>
        PluginManifest RequiredProviderReplacementManifest { get; }

        /// <summary>The contract the consumer requires and both providers export (P-011).</summary>
        ContractRef RequiredService { get; }

        /// <summary>
        /// Installation the unload step tears down through the P-048 order (O-07). It is mounted by the sequence
        /// itself so its staged leases are the ones the teardown retires in reverse acquisition order (P-047, P-048).
        /// </summary>
        PluginInstanceId UnloadInstall { get; }

        /// <summary>Scope the unload installation is mounted at.</summary>
        ScopeId UnloadScope { get; }

        /// <summary>
        /// One neutral composition edit per slot case, in the same order as <see cref="SlotCases"/>: a scope creation
        /// under the world root that no live target lives in, so the publication it drives changes no target assembly
        /// and the policy pass's dispositions are that publication's only effective change (P-006, GC-015). A gate
        /// needs one per case because a scope is created once per composition and each policy pass rides its own
        /// publication.
        /// </summary>
        IReadOnlyList<CompositionEditPayload> NeutralEdits { get; }

        /// <summary>The unload installation's manifest; it declares no stage, buffer or state slot.</summary>
        PluginManifest UnloadManifest { get; }

        /// <summary>O-03: mount one installation at one scope.</summary>
        CompositionEditPayload MountInstall(PluginManifest manifest, PluginInstanceId instance, ScopeId scope);

        /// <summary>O-06: suspend one installation, retaining its definition and configuration (P-046).</summary>
        CompositionEditPayload SuspendInstall(PluginInstanceId instance);

        /// <summary>O-04: resume an explicitly suspended installation.</summary>
        CompositionEditPayload ResumeInstall(PluginInstanceId instance);

        /// <summary>O-07: unmount one installation; the publication carries its removal (P-048).</summary>
        CompositionEditPayload UnmountInstall(PluginInstanceId instance);

        // ------------------------------------------------------------------ the state-policy half (GC-015)

        /// <summary>Installation of the provider that declares this gate's slot policies.</summary>
        PluginInstanceId StatePolicyInstall { get; }

        /// <summary>Scope the state-policy provider is mounted at: an ancestor of every policy target's scope.</summary>
        ScopeId StatePolicyScope { get; }

        /// <summary>
        /// The state-policy provider's manifest, with its declared state slots (P-032, P-033). It is also part of
        /// `Declarations`, so its slots belong to the compiled ownership surface of this revision.
        /// </summary>
        PluginManifest StatePolicyManifest { get; }

        /// <summary>O-03: mount the state-policy provider, whose slots the policy pass acts on.</summary>
        CompositionEditPayload MountStatePolicyHost();

        /// <summary>The declared last-support and reset cases, in the order the sequence reports them (P-032).</summary>
        IReadOnlyList<W4GateSlotCase> SlotCases { get; }

        /// <summary>
        /// The live targets the policy pass runs over: the targets whose declared slots this revision stores. Every
        /// live slot row of these targets must be declared by this revision, because the executor decides on every
        /// live slot it is shown (P-032).
        /// </summary>
        IReadOnlyList<TargetId> PolicyTargets { get; }

        /// <summary>
        /// The slot migrations this revision registers (P-029, P-054): the same registry the family publishes
        /// through, so a migration identity is declared once.
        /// </summary>
        MigrationRegistry PolicyMigrations { get; }

        /// <summary>
        /// Registered initialization values a declared reset reads from (P-032): a reset writes a declared value,
        /// never an implicit zero.
        /// </summary>
        IInitializationPolicyRegistry InitialValues { get; }

        /// <summary>
        /// Manifests the lane's manifest source must resolve beside the catalog's own declarations: the lifecycle
        /// pair, the compatible provider, the unload installation and the state-policy provider. Kept apart from
        /// `Declarations` because a declaration also drives the compiled ownership and schedule surface, and none of
        /// these declares a stage, buffer or resource.
        /// </summary>
        IReadOnlyList<PluginManifest> ExtraManifests { get; }

        /// <summary>
        /// Seeds one case's slot with its non-default value at its declared schema version, so the policy acts on
        /// state the world really owns rather than on a value the assertion invented (P-032).
        /// </summary>
        bool SeedSlotCase(W4GateSlotCase slotCase, LiveTargetSeeder seeder);
    }
}
