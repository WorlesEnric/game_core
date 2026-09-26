// GameCore.Validation.ProbeHost — the GC-024 narrative conformance host.
//
// `Gc013NarrativeHost.NarrativeFamily` already declares the whole narrative slice as a family — its catalog
// declarations, its chapter scope tree, its live villagers, the chapter providers it mounts and every composition
// edit the earlier gates submit — through `IGc013Family`, `IW4GateFamily`, `IW5GateFamily`, `IW6Family`,
// `IGc018Family` and `IGc019Family`. This file adds the *GC-024* half as one more partial part of the same class:
//
//   * `Apply(operation, operand)` executes one operation key of a `ConformanceScript` against a live world with this
//     package's OWN payload builders (`NarrativeMounts.Mount`, `NarrativeLifecyclePayloads.*`,
//     `Gc013NarrativeHost.ScopeReparent`/`ModeSet`, `NarrativePayloadCodec.EncodeChoice`), so the fixture never names
//     a genre payload and the runner contains no genre switch (P-002, P-042);
//   * `TryReadField(field)` reads one canonical field of `ConformanceFields.Narrative()` out of the live world, so
//     07 s3.3's before/after table is observed in a real narrative world rather than merely executed.
//
// WHAT A READ IS, AND WHAT A MISS IS
//
//   * a derived field — `<subject>.dialogue-binding`, `<subject>.gate-binding`, `<subject>.encounter-binding` — is
//     the effective value of the target's ACTIVE row for that capability and output slot 0, read from the published
//     assembly in canonical target order. No active row is the canonical token `none`, which is a value and not a
//     miss: 07:172's "the crowd prop remains unchanged" and 07:175's isolated museum target are assertions about an
//     absent contribution (P-015, P-019). The row's diagnostic names the installation that supports it by its
//     stable catalog name (`chapter-narrative-one`/`chapter-narrative-two`), never by a hex identity;
//   * an owned field — the world ledger's bridge-permit value and version, Mara's conversation node and status, gate
//     east's decision, encounter oak's status — is an authoritative slot of one owner on one target, read through
//     the package's own `NarrativeState` storage access (P-032, P-034). A target this world does not hold, or a slot
//     its owner has not written, is a reported MISS with its reason: the runner records `none` and the oracle fails
//     the row, which is what keeps an unobserved assertion from passing for the wrong reason (P-026, P-060);
//   * `world.mode` is the committed composition's propagation mode (P-013).
//
// OPERATIONS THIS GENRE DOES NOT DECLARE are reported as `Unsupported` with their reason instead of being faked:
// the nested same-capability provider (`mount-nested-provider`), and the four reward-bridge keys, whose provider and
// durable outbox belong to the combined world of 07 s5 and not to the narrative slice (P-045).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Gameplay.Narrative;
using GameCore.Gameplay.Narrative.Fixtures;
using GameCore.ReferenceConformance;
using GameCore.Rules.Narrative;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using Unity.Entities;

namespace GameCore.Validation.ProbeHost
{
    public static partial class Gc013NarrativeHost
    {
        /// <summary>
        /// The narrative half of `IConformanceFamily`: one more partial part of the same family, so the 07 s3.3 table
        /// runs over the same chapter world the earlier gates run (P-001).
        /// </summary>
        public sealed partial class NarrativeFamily : IConformanceFamily
        {
            /// <summary>The label every observation of this family's run is qualified with.</summary>
            public string ConformanceLabel => Label;

            /// <summary>
            /// Empty, and deliberately so: every operation 07 s3.3's script names is expressible with the narrative
            /// slice's own declared providers. The chapter tree needs no second same-capability provider
            /// (`mount-nested-provider` is unsupported here), the exclusive pair of step D is already declared
            /// (`Gc013NarrativeHost.Declarations`), and the O-05 reconfiguration is a real reconfiguration of the
            /// chapter-one installation rather than a replacement declaration (P-009's "an empty category is
            /// explicit").
            /// </summary>
            public IReadOnlyList<CatalogPluginDeclaration> ConformanceDeclarations { get; } =
                Array.Empty<CatalogPluginDeclaration>();

            /// <summary>
            /// Executes one operation key of a `ConformanceScript` against one live narrative world. Every payload is
            /// the narrative package's own: a mount is `NarrativeMounts.Mount` over the declaration the family's own
            /// scenario mounts, a lifecycle edit is `NarrativeLifecyclePayloads`, the move and the mode switch are the
            /// `Gc013NarrativeHost` builders the GC-013 sequence publishes, and a choice is
            /// `NarrativePayloadCodec.EncodeChoice` on the family's own route, target and schema (P-002, P-042).
            /// </summary>
            public ConformanceOperationResult Apply(string operation, int operand, ConformanceWorld world)
            {
                if (world == null)
                {
                    return Unsupported("no world");
                }

                switch (operation)
                {
                    case ConformanceOperations.MountProvider:
                        return world.PublishEdit(MountProvider(), "mount-chapter-one");

                    case ConformanceOperations.MountSecondProvider:
                        return world.PublishEdit(MountSecondProvider(), "mount-chapter-two");

                    case ConformanceOperations.MountNestedProvider:
                        return Unsupported(
                            "the narrative chapter declares no second provider of one of its capabilities under a"
                            + " target's own scope: every chapter binding is a single Replace contribution at the"
                            + " chapter's own scope (P-019)");

                    case ConformanceOperations.MountConflictProvider:
                        return world.PublishEdit(MountConflictProvider(), "mount-conflict-one");

                    case ConformanceOperations.MountConflictSecondProvider:
                        return world.PublishEdit(MountConflictSecondProvider(), "mount-conflict-two");

                    case ConformanceOperations.UnmountProvider:
                        return world.PublishEdit(
                            NarrativeLifecyclePayloads.Unmount(NarrativeKeys.ChapterOneInstall), "unmount-chapter-one");

                    case ConformanceOperations.UnmountSecondProvider:
                        return world.PublishEdit(
                            NarrativeLifecyclePayloads.Unmount(NarrativeKeys.ChapterTwoInstall), "unmount-chapter-two");

                    case ConformanceOperations.ReconfigureProvider:
                        return Reconfigure(world, operand);

                    case ConformanceOperations.ReparentMovedScope:
                        return world.PublishEdit(ScopeReparent(), "reparent-village");

                    case ConformanceOperations.ModeAutomatic:
                        return world.PublishEdit(ModeSet(PropagationMode.Automatic), "mode-automatic");

                    case ConformanceOperations.ModeConservative:
                        return world.PublishEdit(ModeSet(PropagationMode.Conservative), "mode-conservative");

                    case ConformanceOperations.SuspendProvider:
                        return world.PublishEdit(
                            NarrativeLifecyclePayloads.Suspend(NarrativeKeys.ChapterOneInstall), "suspend-chapter-one");

                    case ConformanceOperations.ResumeProvider:
                        return world.PublishEdit(
                            NarrativeLifecyclePayloads.Resume(NarrativeKeys.ChapterOneInstall), "resume-chapter-one");

                    case ConformanceOperations.SpawnFutureTarget:
                        return SpawnFuture(world, operand);

                    case ConformanceOperations.SeedOptedInTarget:
                        return SeedOptedIn(world);

                    case ConformanceOperations.ApplyExclusion:
                        return ExcludeConversation(world);

                    case ConformanceOperations.CommitCommand:
                        return CommitChoices(world, operand);

                    case ConformanceOperations.CommitCommandRejected:
                        return CommitRejected(world);

                    case ConformanceOperations.MountRewardBridge:
                        return Unsupported(
                            "the narrative family declares no reward bridge: 07 s5's NarrativeCardRewards provider is"
                            + " the combined world's own declaration (P-045)");

                    case ConformanceOperations.UnmountRewardBridge:
                        return Unsupported(
                            "the narrative family declares no reward bridge to unmount: the durable reward seam is"
                            + " the combined world's (07 s5, P-045)");

                    case ConformanceOperations.SettleReward:
                        return Unsupported(
                            "the narrative family declares no durable outbox: the admitted reward is settled at the"
                            + " combined world's destination (07 s5, P-045)");

                    case ConformanceOperations.RedeliverReward:
                        return Unsupported(
                            "the narrative family declares no durable outbox to redeliver from: the idempotency key"
                            + " seam belongs to the combined world (07 s5, P-045)");

                    default:
                        return Unsupported("the narrative family declares no '" + operation + "' operation");
                }
            }

            /// <summary>
            /// Reads one canonical field of `ConformanceFields.Narrative()` out of the live narrative world: derived
            /// binding rows from the published assembly (P-030), owned state from the package's own ECS storage
            /// (P-034), and the world mode from the committed composition (P-013). A key this genre does not own is a
            /// reported miss, so a field no parser answers cannot silently read as an absent value.
            /// </summary>
            public bool TryReadField(string field, ConformanceWorld world, out string value, out string detail)
            {
                value = ConformanceValue.None;
                detail = string.Empty;
                if (world == null)
                {
                    detail = "no world";
                    return false;
                }

                if (field == null)
                {
                    detail = "no field key";
                    return false;
                }

                if (string.Equals(field, ConformanceFields.WorldMode, StringComparison.Ordinal))
                {
                    if (world.Lane == null)
                    {
                        detail = "the world has no composition lane";
                        return false;
                    }

                    value = world.Lane!.Committed.Mode == PropagationMode.Conservative ? "conservative" : "automatic";
                    detail = "the committed propagation mode (P-013)";
                    return true;
                }

                for (int i = 0; i < NarrativeFields.Count; i++)
                {
                    NarrativeField known = NarrativeFields[i];
                    if (!string.Equals(field, known.Key, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    return known.Kind == NarrativeFieldKind.DerivedBinding
                        ? TryReadDerivedBinding(world, known, out value, out detail)
                        : TryReadOwnedState(world, known, out value, out detail);
                }

                detail = "the narrative family owns no field '" + field + "'";
                return false;
            }

            // ------------------------------------------------------------------ derived binding reads

            /// <summary>
            /// The effective value of one target's active binding row for one capability and output slot 0. No such
            /// row is the canonical token `none` — an absent contribution, which is what an ineligible or isolated
            /// target must read in both phases (P-015, P-019) — and the row's own diagnostic names the supporting
            /// installation by its stable catalog name (P-017).
            /// </summary>
            private static bool TryReadDerivedBinding(
                ConformanceWorld world,
                NarrativeField known,
                out string value,
                out string detail)
            {
                value = ConformanceValue.None;
                if (world.Publisher == null)
                {
                    detail = known.Key + ": the world has no assembly publisher";
                    return false;
                }

                IReadOnlyList<CapabilityBinding> rows = world.Publisher!.ReadBindingRows(known.Target);
                for (int i = 0; i < rows.Count; i++)
                {
                    CapabilityBinding row = rows[i];
                    if (!row.IsActive
                        || row.OutputSlot != 0U
                        || !row.Capability.Equals(known.Capability))
                    {
                        continue;
                    }

                    value = row.Value.ToString(CultureInfo.InvariantCulture);
                    detail = known.Key + "=" + value + " via " + ProviderLabel(row.Provider) + " (P-017)";
                    return true;
                }

                detail = known.Key + " has no active derived row in the published assembly (P-019)";
                return true;
            }

            /// <summary>
            /// The stable catalog name of the installation that supports a binding row, compared by identity against
            /// this slice's own declared installations, never against a name prefix or a hex literal (P-004). A row
            /// some other provider supports reports its own identity, so an unexpected supporter is visible.
            /// </summary>
            private static string ProviderLabel(ProviderInstallationId installation)
            {
                if (installation.Value.Equals(NarrativeKeys.ChapterOneInstall.Value))
                {
                    return NarrativeCompositionNames.ChapterOneInstall;
                }

                if (installation.Value.Equals(NarrativeKeys.ChapterTwoInstall.Value))
                {
                    return NarrativeCompositionNames.ChapterTwoInstall;
                }

                return installation.Value.ToString();
            }

            // ------------------------------------------------------------------ owned state reads

            /// <summary>
            /// One authoritative slot of one owner on one target, read through the package's own storage access. The
            /// entity is resolved from the live target index, so a target this world does not hold is a miss with its
            /// identity — never a value read out of a second registry (P-005, P-034). The world-root ledger is a
            /// declared target of this genre (07 s3.1); a world that has not seeded it, and a target whose owner has
            /// not written the slot, both report the live state that is missing instead of a default.
            /// </summary>
            private static bool TryReadOwnedState(
                ConformanceWorld world,
                NarrativeField known,
                out string value,
                out string detail)
            {
                value = ConformanceValue.None;
                if (world.Host == null || world.Seeder == null)
                {
                    detail = known.Key + ": the world or its target seeder is missing";
                    return false;
                }

                if (!world.Seeder!.TryGetEntity(known.Target, out Entity entity))
                {
                    detail = known.Key + ": " + known.Target.ToString()
                        + " is not a live target of this world (P-005)";
                    return false;
                }

                if (!NarrativeState.TryRead(
                        world.Host!.EntityWorld.EntityManager,
                        entity,
                        known.Owner,
                        known.Slot,
                        out int read,
                        out uint schemaVersion))
                {
                    detail = known.Key + ": the owner has written no such slot on "
                        + known.Target.ToString() + " (P-032)";
                    return false;
                }

                value = read.ToString(CultureInfo.InvariantCulture);
                detail = known.Key + "=" + value + " at schema version "
                    + schemaVersion.ToString(CultureInfo.InvariantCulture) + " (P-034)";
                return true;
            }

            // ------------------------------------------------------------------ the field vocabulary

            /// <summary>The canonical field keys of `ConformanceFields.Narrative()` and the live state each reads.</summary>
            private static readonly IReadOnlyList<NarrativeField> NarrativeFields = new List<NarrativeField>
            {
                DerivedField(
                    ConformanceFields.DialogueBinding(NarrativeCompositionNames.Mara),
                    NarrativeKeys.Mara,
                    NarrativeKeys.DialogueBinding),
                DerivedField(
                    ConformanceFields.DialogueBinding(NarrativeCompositionNames.Display),
                    NarrativeKeys.Display,
                    NarrativeKeys.DialogueBinding),
                DerivedField(
                    ConformanceFields.DialogueBinding(NarrativeCompositionNames.CrowdProp),
                    NarrativeKeys.CrowdProp,
                    NarrativeKeys.DialogueBinding),
                DerivedField(
                    ConformanceFields.DialogueBinding(NarrativeCompositionNames.FutureVillager),
                    NarrativeKeys.FutureVillager,
                    NarrativeKeys.DialogueBinding),
                DerivedField(
                    ConformanceFields.DialogueBinding(NarrativeCompositionNames.Sailor),
                    NarrativeKeys.Sailor,
                    NarrativeKeys.DialogueBinding),
                DerivedField(
                    ConformanceFields.GateBinding(NarrativeCompositionNames.GateEast),
                    NarrativeKeys.GateEast,
                    NarrativeKeys.GateBinding),
                DerivedField(
                    ConformanceFields.EncounterBinding(NarrativeCompositionNames.EncounterOak),
                    NarrativeKeys.EncounterOak,
                    NarrativeKeys.EncounterBinding),
                OwnedField(
                    ConformanceFields.BridgePermit,
                    NarrativeKeys.QuestLedger,
                    NarrativeKeys.QuestOwner,
                    NarrativeKeys.BridgePermitValueSlot),
                OwnedField(
                    ConformanceFields.BridgePermitVersion,
                    NarrativeKeys.QuestLedger,
                    NarrativeKeys.QuestOwner,
                    NarrativeKeys.BridgePermitVersionSlot),
                OwnedField(
                    ConformanceFields.MaraConversationStatus,
                    NarrativeKeys.Mara,
                    NarrativeKeys.DialogueOwner,
                    NarrativeKeys.ConversationStatusSlot),
                OwnedField(
                    ConformanceFields.MaraConversationNode,
                    NarrativeKeys.Mara,
                    NarrativeKeys.DialogueOwner,
                    NarrativeKeys.ConversationNodeSlot),
                OwnedField(
                    ConformanceFields.GateEastDecision,
                    NarrativeKeys.GateEast,
                    NarrativeKeys.GateOwner,
                    NarrativeKeys.GateDecisionSlot),
                OwnedField(
                    ConformanceFields.EncounterOakStatus,
                    NarrativeKeys.EncounterOak,
                    NarrativeKeys.EncounterOwner,
                    NarrativeKeys.EncounterStatusSlot),
            };

            /// <summary>One canonical field key and the live state the family answers it from.</summary>
            private readonly struct NarrativeField
            {
                public NarrativeField(
                    string key,
                    NarrativeFieldKind kind,
                    TargetId target,
                    CapabilityId capability,
                    OwnerId owner,
                    SlotId slot)
                {
                    Key = key;
                    Kind = kind;
                    Target = target;
                    Capability = capability;
                    Owner = owner;
                    Slot = slot;
                }

                /// <summary>The canonical field key of `ConformanceFields`.</summary>
                public string Key { get; }

                /// <summary>Which half of the world this key is read from.</summary>
                public NarrativeFieldKind Kind { get; }

                /// <summary>The live target the field is about.</summary>
                public TargetId Target { get; }

                /// <summary>The derived capability, when the field is a derived binding row.</summary>
                public CapabilityId Capability { get; }

                /// <summary>The owner of the slot, when the field is authoritative state.</summary>
                public OwnerId Owner { get; }

                /// <summary>The owned slot, when the field is authoritative state.</summary>
                public SlotId Slot { get; }
            }

            /// <summary>Which half of a narrative world one field key is answered from.</summary>
            private enum NarrativeFieldKind
            {
                /// <summary>An active derived binding row's effective value, from the published assembly (P-030).</summary>
                DerivedBinding = 0,

                /// <summary>A slot an authoritative owner wrote, from the live target's own storage (P-034).</summary>
                OwnedState = 1,
            }

            private static NarrativeField DerivedField(string key, TargetId target, CapabilityId capability) =>
                new NarrativeField(
                    key,
                    NarrativeFieldKind.DerivedBinding,
                    target,
                    capability,
                    default(OwnerId),
                    default(SlotId));

            private static NarrativeField OwnedField(string key, TargetId target, OwnerId owner, SlotId slot) =>
                new NarrativeField(
                    key,
                    NarrativeFieldKind.OwnedState,
                    target,
                    default(CapabilityId),
                    owner,
                    slot);

            // ------------------------------------------------------------------ operations

            /// <summary>
            /// O-05: reconfigures this family's first installation — the chapter-one provider. The declared
            /// configuration hash is the canonical hash of exactly the layers the applier recomposes (schema defaults
            /// over the installation's inherited effective configuration over this empty local patch) and the
            /// revision advances by one, so the edit is the P-020 shape rather than a second interpretation of it.
            ///
            /// A chapter's contribution VALUE is not configuration: every chapter rule carries the chapter's binding
            /// ordinal as its declared payload (P-009), so an operand naming any other value has no declaration in
            /// this run to resolve and is reported as unsupported rather than silently applied.
            /// </summary>
            private ConformanceOperationResult Reconfigure(ConformanceWorld world, int operand)
            {
                if (operand != ProviderValue)
                {
                    return Unsupported(
                        "the chapter provider's contribution is the declared binding ordinal "
                        + ProviderValue.ToString(CultureInfo.InvariantCulture)
                        + ", which is a manifest payload and not configuration, so this run declares no O-05"
                        + " reconfiguration to " + operand.ToString(CultureInfo.InvariantCulture)
                        + " (P-009, P-020)");
                }

                if (world.Lane == null)
                {
                    return Unsupported("the world has no composition lane");
                }

                if (!world.Lane!.Committed.TryGetInstall(
                        NarrativeKeys.ChapterOneInstall, out InstallEntry? entry) || entry == null)
                {
                    return Unsupported("the chapter-one installation is not part of this committed composition (P-046)");
                }

                ConfigComposeResult composed = ConfigComposer.Compose(new[]
                {
                    new ConfigLayer(
                        ConfigLayerOrigin.SchemaDefaults,
                        entry.Manifest.ConfigSchema.Id.Value,
                        declarations[0].SchemaDefaults),
                    new ConfigLayer(
                        ConfigLayerOrigin.InheritedContribution,
                        NarrativeKeys.ChapterOneInstall.Value,
                        entry.Config),
                    new ConfigLayer(
                        ConfigLayerOrigin.LocalPatch,
                        NarrativeKeys.ChapterOneInstall.Value,
                        ConfigDocument.Empty),
                });

                return world.PublishEdit(
                    NarrativeLifecyclePayloads.Reconfigure(
                        entry.Manifest,
                        NarrativeKeys.ChapterOneInstall,
                        new DefinitionRevision(entry.Record.ConfigRevision.Value + 1UL),
                        ConfigDocumentCodec.HashOf(composed.Value),
                        ConfigDocument.Empty),
                    "reconfigure-chapter-one");
            }

            /// <summary>
            /// P-024's spawn of this genre's declared future target. The publication the spawn rides on must carry no
            /// derivable target change (a scope no live target lives in, `SpareScopeEdits[1]`), and the spawn itself
            /// publishes the villager fully assembled — its base layout and its derived Chapter One dialogue binding
            /// in one image — before it is registered in the live target index. The spawned villager is then mapped
            /// into the genre's own stage runtime, exactly as the narrative scenario maps it after its spawn, so a
            /// later choice addressed to it resolves its entity (07 s3.2, P-005).
            /// </summary>
            private ConformanceOperationResult SpawnFuture(ConformanceWorld world, int operand)
            {
                if (operand != 0)
                {
                    return Unsupported(
                        "the narrative family declares exactly one future target ("
                        + NarrativeCompositionNames.FutureVillager + "); operand "
                        + operand.ToString(CultureInfo.InvariantCulture) + " names no declared spawn (P-009)");
                }

                if (world.Pipeline == null || world.Targets == null || world.Seeder == null)
                {
                    return Unsupported("the world or its pipeline is missing");
                }

                PrepareSpawn();
                ConformanceOperationResult neutral = world.PublishEdit(SpareScopeEdits[1], "spawn-neutral-publication");
                if (!neutral.Published)
                {
                    return neutral;
                }

                DerivedAssemblyReport spawn = world.Pipeline!.PublishSpawn(
                    world.NextOperation(), FutureTarget, FutureRecipe, FutureScope);
                if (spawn.Outcome != DerivedAssemblyOutcome.Published)
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Refused,
                        "the spawn of " + FutureTarget.ToString() + " was refused: " + spawn.Describe());
                }

                if (!world.Targets!.TryRegister(
                        FutureTarget, FutureScope, FutureRecipe, out DiagnosticCode code, out string detail))
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Refused,
                        "the spawned target could not be registered: " + code + ": " + detail);
                }

                if (!world.Seeder!.TryGetEntity(FutureTarget, out Entity entity))
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Refused,
                        "the spawned target has no live entity (P-005)");
                }

                world.Runtime?.Adapters?.Narrative?.MapTarget(FutureTarget, entity);

                return new ConformanceOperationResult(
                    ConformanceOperationOutcome.Published,
                    "spawned " + FutureTarget.ToString() + " from " + FutureRecipe.ToString() + " under "
                    + FutureScope.ToString() + " (P-024)");
            }

            /// <summary>
            /// Seeds and publishes the complete-opt-in villager exactly as the family's own sequence does: the
            /// explicit opt-in is a descriptor property and therefore immutable assembly input, so it cannot be
            /// published as a later edit (P-013, P-015). Both halves must succeed for the operation to have happened.
            /// </summary>
            private ConformanceOperationResult SeedOptedIn(ConformanceWorld world)
            {
                if (world.Host == null || world.Targets == null || world.Seeder == null)
                {
                    return Unsupported("the world or its target index is missing");
                }

                if (!SeedOptedInTarget(new Gc013WorldContext(world.Host!, world.Targets!, world.Seeder!)))
                {
                    return Unsupported("seeding the complete-opt-in villager was refused");
                }

                return world.PublishEdit(SpareScopeEdits[0], "seed-opted-in-publication");
            }

            /// <summary>
            /// P-016's exclusion on the conversation capability of the moved villager: a scope-isolation edit on the
            /// village scope naming the capability and that one target, with the scope's existing isolation sets read
            /// back from the committed composition so the edit changes only what it says it changes. Other targets'
            /// bindings — gate east's condition — stay available, and the isolated museum target stays isolated.
            /// </summary>
            private static ConformanceOperationResult ExcludeConversation(ConformanceWorld world)
            {
                if (world.Lane == null)
                {
                    return Unsupported("the world has no composition lane");
                }

                ScopeId scope = NarrativeKeys.VillageScope;
                if (!world.Lane!.Committed.Scopes.TryGet(scope, out ScopeRecord? record) || record == null)
                {
                    return Unsupported("the village scope is not part of this committed composition");
                }

                var payload = new CompositionEditPayload(
                    CompositionEditSubject.ScopeIsolation,
                    scope,
                    record.Parent,
                    false,
                    record.ServiceIsolation,
                    record.CapabilityIsolation,
                    new List<ExclusionRule>
                    {
                        new ExclusionRule(
                            ExclusionTargetKind.Capability,
                            NarrativeKeys.DialogueBinding.Value,
                            scope,
                            NarrativeKeys.Mara,
                            false),
                    },
                    null,
                    default(PluginTypeId),
                    default(PluginInstanceId),
                    DefinitionRevision.Zero,
                    ContentHash.Empty,
                    null,
                    0,
                    null,
                    PropagationMode.Automatic);

                return world.PublishEdit(payload, "exclude-conversation-on-mara");
            }

            /// <summary>
            /// Submits this family's own declared choice `operand` times, pumping one command-driven step after each,
            /// so every accepted choice commits exactly one logical step and the fact, the conversation state and the
            /// gate decision move in the same step (07 s3.2's sequence, P-036, P-042). The choice names the
            /// conversation node the family itself seeded, so `NarrativeDialogueRules.Validate` accepts it at the
            /// node the live conversation really sits on.
            /// </summary>
            private ConformanceOperationResult CommitChoices(ConformanceWorld world, int operand)
            {
                if (world.Host == null)
                {
                    return Unsupported("the world has no host");
                }

                // A choice the world admits but the dialogue RULES refuse writes nothing (P-042), and the genre's own
                // stage runtime counts exactly that, so a copy the rules turned down is reported as refused instead
                // of being claimed as applied.
                NarrativeModule? module = world.Runtime?.Adapters?.Narrative;
                int refusedBefore = module != null ? module.RefusedChoices : 0;
                int copies = operand <= 0 ? 1 : operand;
                ConformanceOperationResult last = Unsupported("no choice was submitted");
                for (int i = 0; i < copies; i++)
                {
                    CommandEnvelope envelope = ChoiceEnvelope(
                        MutableValue, NarrativeDialogueRules.PermitChoice, world.NextOperation());
                    last = world.SubmitAndPump(envelope, "commit-command");
                    if (!last.Published)
                    {
                        return last;
                    }
                }

                if (module != null && module.RefusedChoices > refusedBefore)
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Refused,
                        "the dialogue rules refused "
                        + (module.RefusedChoices - refusedBefore).ToString(CultureInfo.InvariantCulture)
                        + " of " + copies.ToString(CultureInfo.InvariantCulture)
                        + " submitted choices, so the world did not apply every copy (P-042)");
                }

                return last;
            }

            /// <summary>
            /// Submits one choice the narrative rules reject: it names a dialogue node the conversation does not sit
            /// on, so `NarrativeDialogueRules.Validate` refuses it with its stable refusal code and writes neither
            /// conversation state nor a fact (07 s3.2, P-042's "admission is not gameplay success"). The refusal is
            /// read from the genre's own stage runtime, which counts it, so the answer is the world's and not the
            /// caller's guess about the envelope it built.
            /// </summary>
            private ConformanceOperationResult CommitRejected(ConformanceWorld world)
            {
                if (world.Host == null || world.Runtime == null)
                {
                    return Unsupported("the world or its stage runtime is missing");
                }

                NarrativeModule? module = world.Runtime!.Adapters?.Narrative;
                if (module == null)
                {
                    return Unsupported("this world has no narrative stage runtime attached (P-043)");
                }

                int refusedBefore = module.RefusedChoices;
                ConformanceOperationResult submitted = world.SubmitAndPump(
                    ChoiceEnvelope(MutableValue + 1, NarrativeDialogueRules.PermitChoice, world.NextOperation()),
                    "commit-command-rejected");
                if (submitted.Outcome == ConformanceOperationOutcome.Refused)
                {
                    return submitted;
                }

                if (module.RefusedChoices > refusedBefore)
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Refused,
                        "the dialogue rules refused the choice at node "
                        + (MutableValue + 1).ToString(CultureInfo.InvariantCulture)
                        + " while the conversation sits on node "
                        + MutableValue.ToString(CultureInfo.InvariantCulture)
                        + ", so no conversation state and no fact was written (P-042)");
                }

                return new ConformanceOperationResult(
                    ConformanceOperationOutcome.Unsupported,
                    "the world committed the step but the dialogue owner recorded no rule refusal: the choice was"
                    + " rejected before validation, so this operation observed no refusal to report (P-042)");
            }

            /// <summary>
            /// The family's own choice envelope: the narrative package's two-scalar encoding on the choice route this
            /// world registers, addressed to the villager the family's own command addresses (P-042, 05 s6). The
            /// expected domain version is left open, exactly as the family's own GC-021 sequence leaves it.
            /// </summary>
            private static CommandEnvelope ChoiceEnvelope(int nodeOrdinal, int choiceOrdinal, OperationId operation) =>
                new CommandEnvelope(
                    operation,
                    NarrativeKeys.ChoiceRoute,
                    NarrativeKeys.Mara,
                    NarrativeKeys.ChoiceCommandSchema,
                    null,
                    new FrozenPayload(NarrativePayloadCodec.EncodeChoice(
                        new NarrativeChoice(nodeOrdinal, choiceOrdinal))));

            private static ConformanceOperationResult Unsupported(string detail) =>
                new ConformanceOperationResult(ConformanceOperationOutcome.Unsupported, detail);
        }
    }
}
