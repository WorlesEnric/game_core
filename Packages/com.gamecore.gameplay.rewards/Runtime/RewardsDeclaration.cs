// GameCore.Gameplay.Rewards — the installed reward plugin's declaration (GC-024).
//
// Normative sources: docs/game-core/07-reference-compositions.md s5. Three sentences of that section are the
// declaration below, verbatim:
//
//   * "the bridge explicitly depends on the committed quest output and the card command endpoint";
//   * "`rewards.dispatch` is a managed post-publication observer, not an execution stage" — which is why the
//     installation drives the bridge itself (`RewardsInstallation.RunBridgePass`) and the two stages below are the
//     two halves of a reward's life: `rewards.enqueue` persists the obligation, `rewards.ack` settles it;
//   * "`NarrativeCardRewards` declares `PreserveDormant` for its completed outbox, with a scratch-migration
//     precondition that no pending work remains" — the state slot of `RewardsOutboxSlot.Spec()`.
//
// THE STAGE EDGES. 07 s5 orders `rewards.enqueue` after the narrative quest stage and `rewards.ack` after the card
// commit stage, and this package declares both, by identity:
//
//   * `narrative.quest`       — `GameCore.Gameplay.Narrative.NarrativeKeys.QuestStage`, whose stable name is
//                               `NarrativeCompositionNames.QuestStageName` = "narrative.quest"
//                               (Packages/com.gamecore.rules.narrative/Runtime/NarrativeCompositionNames.cs:153);
//   * `cards.stage.commit`    — `GameCore.Gameplay.Cards.CardTableKeys.CommitStage`
//                               (Packages/com.gamecore.gameplay.cards/Runtime/CardTableKeys.cs:58). The contract
//                               named a `CardsCommitStageName` constant; no such constant exists in the tree (the
//                               card package declares the typed `StageId`, and the literal "cards.stage.commit"
//                               appears only inside `CardTableDeclarations.Stages()`), so the typed identity is
//                               what is used here.
//
// The two cross-package edges are declared as `OptionalBefore`/`OptionalAfter`, which the schedule compiler
// resolves into a real ordering edge whenever the named stage is part of the active declaration set and drops
// silently when it is not (ScheduleCompiler.cs:299-339; P-039). A `Required*` edge would instead reject assembly
// with `DiagnosticCode.MissingDependency` (ScheduleWitnessKind.RequiredStageEdgeMissing) whenever this plugin is
// mounted in a world that does not carry the narrative or card declaration set — and the reward installation is a
// *cross-family* plugin whose whole point is that it is mounted where both families are. Declaring the dependency
// as optional keeps 07 s5's ordering whenever its endpoints exist, without claiming that a world without them is
// invalid. The intra-package edge (`rewards.enqueue` before `rewards.ack`) is declared as required, because both
// endpoints are this package's own and the receipt buffer's playback order depends on it (P-041).
#nullable enable
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Gameplay.Cards;
using GameCore.Gameplay.Narrative;
using GameCore.Unity.Runtime.Integration;

namespace GameCore.Gameplay.Rewards
{
    /// <summary>
    /// The declarative surface of 07 s5's `NarrativeCardRewards`: one configuration schema, one state slot with its
    /// `PreserveDormant` last-support policy and registered migration, the two reward stages with their declared
    /// edges, the receipt buffer between them, and this package's own command route.
    /// </summary>
    public static class RewardsDeclaration
    {
        /// <summary>
        /// The one manifest of this plugin type. `PluginManifest` (`Packages/com.gamecore.contracts/Runtime/Manifest/
        /// PluginManifest.cs:18-52`) takes sixteen arguments, and the shape mirrors
        /// `CardTableDeclarations.Manifest(...)` (Packages/com.gamecore.gameplay.cards/Runtime/CardTableDeclarations.cs:399):
        /// declared protocol range 1.0.0, package content hash `ContentHash.Empty` (this package ships no compiled
        /// content document), exactly one declared state slot and exactly one declared buffer.
        /// </summary>
        public static PluginManifest Manifest()
        {
            return new PluginManifest(
                RewardsKeys.PluginType,
                RewardsKeys.PackageVersion,
                ContentHash.Empty,
                new SupportedProtocolRange(1, 0, 0),
                null,
                RewardsKeys.ConfigSchema,
                RewardsKeys.PluginFactory,
                null,
                null,
                null,
                null,
                null,
                RewardsOutboxSlot.Specs(),
                Stages(),
                new List<BufferSpec> { ReceiptStepBuffer() },
                null);
        }

        /// <summary>
        /// The catalog declaration a mount resolves: the manifest plus the configuration-schema defaults it
        /// contributes as the lowest configuration layer (P-020). This manifest's schema declares no default field,
        /// so the defaults document is empty rather than invented.
        /// </summary>
        public static CatalogPluginDeclaration Declaration() =>
            new CatalogPluginDeclaration(Manifest(), ConfigDocument.Empty);

        /// <summary>The declarations of this package, in canonical order (exactly one plugin type, P-009).</summary>
        public static IReadOnlyList<PluginManifest> Manifests() => new List<PluginManifest> { Manifest() };

        /// <summary>The two declared stages of 07 s5, in execution order.</summary>
        public static IReadOnlyList<StageSpec> Stages()
        {
            // `rewards.enqueue`: it reads the committed quest output through the bridge's own committed-event
            // reader and commits the obligation the accepted choice produces (07 s5 steps 1-2). It orders after
            // `narrative.quest` when the narrative declaration set is present, and is required before
            // `rewards.ack`, because the obligation must exist before anything is handed over (P-045).
            var enqueue = new StageSpec(
                RewardsKeys.EnqueueStage,
                1U,
                RewardsKeys.OwnerPackage,
                HostAffinity.ManagedMain,
                new List<FactoryKey> { RewardsKeys.EnqueueStageKey },
                null,
                new AccessSet(new[]
                {
                    new AccessDeclaration(RewardsKeys.OutboxSchema, AccessMode.ReadWrite, default(Id128)),
                }),
                new List<StageId> { RewardsKeys.AckStage },
                null,
                null,
                new List<StageId> { NarrativeKeys.QuestStage },
                new List<SystemSpec>
                {
                    new SystemSpec(
                        RewardsKeys.EnqueueSystem,
                        SystemMultiplicity.World,
                        new AccessSet(new[]
                        {
                            new AccessDeclaration(RewardsKeys.OutboxSchema, AccessMode.ReadWrite, default(Id128)),
                        }),
                        null,
                        null,
                        null,
                        null),
                },
                null);

            // `rewards.ack`: it hands each open obligation to the card command endpoint, whose commit stage owns
            // the mutation (P-034, P-042), and records the acknowledgement. It orders after `cards.stage.commit`
            // when the card declaration set is present and after `rewards.enqueue` always.
            var ack = new StageSpec(
                RewardsKeys.AckStage,
                1U,
                RewardsKeys.OwnerPackage,
                HostAffinity.ManagedMain,
                new List<FactoryKey> { RewardsKeys.AckStageKey },
                null,
                new AccessSet(new[]
                {
                    new AccessDeclaration(RewardsKeys.OutboxSchema, AccessMode.ReadWrite, default(Id128)),
                    new AccessDeclaration(RewardsKeys.ReceiptSchema, AccessMode.Read, default(Id128)),
                }),
                null,
                new List<StageId> { RewardsKeys.EnqueueStage },
                new List<StageId> { NarrativeKeys.QuestStage, CardTableKeys.CommitStage },
                null,
                new List<SystemSpec>
                {
                    new SystemSpec(
                        RewardsKeys.AckSystem,
                        SystemMultiplicity.World,
                        new AccessSet(new[]
                        {
                            new AccessDeclaration(RewardsKeys.OutboxSchema, AccessMode.ReadWrite, default(Id128)),
                            new AccessDeclaration(RewardsKeys.ReceiptSchema, AccessMode.Read, default(Id128)),
                        }),
                        null,
                        null,
                        null,
                        null),
                },
                null);

            return new List<StageSpec> { enqueue, ack };
        }

        /// <summary>
        /// The declared buffer that carries the tentative receipt from `rewards.enqueue` to `rewards.ack`: one
        /// producer, exactly one consuming stage, an order key, one step of lifetime and a bounded capacity whose
        /// overflow rejects before mutation, matching `CardTableDeclarations.DecisionStepBuffer()`
        /// (Packages/com.gamecore.gameplay.cards/Runtime/CardTableDeclarations.cs:269).
        /// </summary>
        public static BufferSpec ReceiptStepBuffer()
        {
            return new BufferSpec(
                RewardsKeys.EnqueueBuffer,
                RewardsKeys.ReceiptSchema,
                new List<FactoryKey> { RewardsKeys.EnqueueSystem },
                RewardsKeys.EnqueueStage,
                RewardsKeys.AckStage,
                RewardsKeys.ReceiptOrderKey,
                BufferLifetime.Step,
                RewardsKeys.LaneCapacity,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);
        }
    }
}
