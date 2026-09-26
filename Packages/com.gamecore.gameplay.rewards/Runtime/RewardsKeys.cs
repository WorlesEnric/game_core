// GameCore.Gameplay.Rewards — the reward installation's stable identities (GC-024).
//
// Normative sources: docs/game-core/07-reference-compositions.md s5, which names one plugin this tree did not yet
// have: "`NarrativeCardRewards` declares `PreserveDormant` for its completed outbox, with a scratch-migration
// precondition that no pending work remains; alternatively an explicitly selected compatible `TransferTo` owner may
// take the outbox. Unmounting with pending work therefore rejects until it drains or transfers." Supporting
// protocols: 00 P-004 (stable 128-bit identities with a catalog name for diagnostics; the name is a label, never
// the runtime identity), P-009 (registration is data: a precompiled factory key resolves a plugin, never
// reflection), P-032 (every state slot identifies an initialization, configuration-update, owner-transfer and
// last-support policy, and a version change uses a registered `Migrate`), P-039/P-043 (declared stages, systems,
// buffers and routes) and P-048 (a resource unfinished work may still reach is retained, never freed).
//
// HOW AN IDENTITY IS DERIVED. Every identity below is derived from a canonical stable name with the production
// `GameCore.Contracts.StableNameKeyDerivation` — SHA-256 over the UTF-8 name, the first 16 digest bytes read as two
// big-endian u64 — which is the same rule `GameCore.Rules.Cards.CardIdentity` and
// `GameCore.Rules.Narrative.NarrativeIds` apply, so a literal here and a generated catalog derive the same id from
// the same name. Every name uses only the characters `StableNameKeyDerivation.AllowedCharacters` permits
// (`a-z 0-9 . _ -`), and `DerivationHolds()` re-checks the derived values against their names (P-004).
//
// There is no `GameCore.Rules.Rewards` package to hold the vocabulary: the reward *content* vocabulary already
// belongs to the integration package's `RewardRule`/`RewardCatalog`, and what this file declares is the
// installation's own composition surface (a plugin, an owner, one state slot, two stages, one buffer, one route).
// The two cross-package stage names this package's schedule edges name are NOT re-declared here: they are read from
// `GameCore.Gameplay.Narrative.NarrativeKeys.QuestStage` ("narrative.quest",
// `NarrativeCompositionNames.QuestStageName`, Packages/com.gamecore.rules.narrative/Runtime/NarrativeCompositionNames.cs:153)
// and `GameCore.Gameplay.Cards.CardTableKeys.CommitStage` ("cards.stage.commit",
// Packages/com.gamecore.gameplay.cards/Runtime/CardTableKeys.cs:58), so the package that declares a stage and the
// package that orders against it cannot disagree about what its name means.
#nullable enable
using GameCore.Contracts;

namespace GameCore.Gameplay.Rewards
{
    /// <summary>
    /// Stable identities of the `NarrativeCardRewards` installation of 07 s5: its plugin type and precompiled
    /// factory, its configuration schema, its installed instance, its outbox owner and state slot with the policy
    /// keys around them, its two declared stages and the receipt buffer between them, and its own command route.
    /// </summary>
    public static class RewardsKeys
    {
        /// <summary>Package version every manifest of this package declares.</summary>
        public const string PackageVersion = "0.1.0";

        // ---------------------------------------------------------------- derivation wrappers (P-004)

        /// <summary>The documented derivation of one stable name (SHA-256, first 16 digest bytes).</summary>
        public static Id128 Id(string stableName) => StableNameKeyDerivation.Derive(stableName);

        /// <summary>A logical state-owner identity.</summary>
        public static OwnerId Owner(string stableName) => new OwnerId(Id(stableName));

        /// <summary>An owned state-slot identity.</summary>
        public static SlotId Slot(string stableName) => new SlotId(Id(stableName));

        /// <summary>An execution-stage identity.</summary>
        public static StageId Stage(string stableName) => new StageId(Id(stableName));

        /// <summary>A declared buffer identity.</summary>
        public static BufferId Buffer(string stableName) => new BufferId(Id(stableName));

        /// <summary>A command-route identity (05 `CommandEnvelope`, P-042).</summary>
        public static RouteId Route(string stableName) => new RouteId(Id(stableName));

        /// <summary>A plugin-type identity.</summary>
        public static PluginTypeId PluginTypeOf(string stableName) => new PluginTypeId(Id(stableName));

        /// <summary>An installed plugin-instance identity; it survives a remount (P-005).</summary>
        public static PluginInstanceId InstanceOf(string stableName) => new PluginInstanceId(Id(stableName));

        /// <summary>A registered managed resource identity (P-048).</summary>
        public static ResourceKey Resource(string stableName) => new ResourceKey(Id(stableName));

        /// <summary>A generated registration/factory key plus its key version (05 s3, P-009).</summary>
        public static FactoryKey Key(string stableName, uint version = 1U) =>
            new FactoryKey(Id(stableName), version);

        /// <summary>A schema identity plus its integer version (05 s3).</summary>
        public static SchemaRef Schema(string stableName, uint version) =>
            new SchemaRef(new SchemaId(Id(stableName)), version);

        // ---------------------------------------------------------------- plugin declarations

        /// <summary>Stable name of the precompiled plugin factory (P-009).</summary>
        public const string PluginFactoryStableName = "gamecore.rewards.factory.narrative-card-rewards";

        /// <summary>Stable name of the plugin type; one declaration per plugin type (P-009).</summary>
        public const string PluginTypeStableName = "gamecore.rewards.plugin.narrative-card-rewards.type";

        /// <summary>Stable name of the installed instance 07 s5's `NarrativeCardRewards` names.</summary>
        public const string InstallationStableName = "gamecore.rewards.plugin.narrative-card-rewards.instance";

        /// <summary>Stable name of the configuration schema a mount is admitted under (P-020).</summary>
        public const string ConfigSchemaStableName = "gamecore.rewards.schema.config";

        /// <summary>
        /// The precompiled plugin factory the catalog registers for this plugin type
        /// (`gamecore.rewards.factory.narrative-card-rewards`). A mount whose factory the catalog does not register
        /// is refused before activation, never resolved through reflection (P-009).
        /// </summary>
        public static FactoryKey PluginFactory { get; } = Key(PluginFactoryStableName);

        /// <summary>The configuration schema every declaration of this plugin type is admitted under.</summary>
        public static SchemaRef ConfigSchema { get; } = Schema(ConfigSchemaStableName, 1U);

        /// <summary>Stable plugin type of the reward installation.</summary>
        public static PluginTypeId PluginType { get; } = PluginTypeOf(PluginTypeStableName);

        /// <summary>
        /// The installed instance identity of 07 s5's `NarrativeCardRewards`. It is the installation's own identity:
        /// the world-delivery owner of the bridge it owns is derived from it (see
        /// <see cref="RewardsInstallation.Mount"/>), not from a caller-supplied literal.
        /// </summary>
        public static PluginInstanceId Installation { get; } = InstanceOf(InstallationStableName);

        /// <summary>Owning package of every stage, slot and registration this package declares (P-039).</summary>
        public static Id128 OwnerPackage { get; } = Id("gamecore.rewards.package");

        /// <summary>Stable issuer of every operation identity this installation mints (P-050).</summary>
        public static Id128 Issuer { get; } = Id("gamecore.rewards.issuer");

        // ---------------------------------------------------------------- the outbox slot and its policies

        /// <summary>Stable name of the outbox owner: the authoritative owner of the declared outbox slot (P-034).</summary>
        public const string OutboxOwnerStableName = "gamecore.rewards.owner.outbox";

        /// <summary>Stable name of the outbox state slot.</summary>
        public const string OutboxSlotStableName = "gamecore.rewards.slot.outbox";

        /// <summary>Stable name of the outbox slot's declared schema and physical layout.</summary>
        public const string OutboxSchemaStableName = "gamecore.rewards.schema.outbox";

        /// <summary>Stable name of the registered v1 -> v2 migration: the "no pending work" precondition (P-032).</summary>
        public const string OutboxMigrationStableName = "gamecore.rewards.migration.outbox.v1-v2";

        /// <summary>
        /// Version the outbox slot is declared at. The installation seeds the live row at
        /// <see cref="OutboxSeededSchemaVersion"/>, so a state-policy pass over it requests a `Migrate` and runs
        /// the registered precondition migration on the copied value (P-029, P-032).
        /// </summary>
        public const uint OutboxDeclaredSchemaVersion = 2U;

        /// <summary>Version the installation seeds the live outbox row at: one below the declared version.</summary>
        public const uint OutboxSeededSchemaVersion = 1U;

        /// <summary>The single logical owner of the reward outbox slot (P-034).</summary>
        public static OwnerId OutboxOwner { get; } = Owner(OutboxOwnerStableName);

        /// <summary>The owned outbox state slot; its value is the pending-work count (0 = nothing pending).</summary>
        public static SlotId OutboxSlot { get; } = Slot(OutboxSlotStableName);

        /// <summary>The outbox slot's declared schema at its declared version (P-032).</summary>
        public static SchemaRef OutboxSchema { get; } = Schema(OutboxSchemaStableName, OutboxDeclaredSchemaVersion);

        /// <summary>Physical layout key of the outbox slot's storage.</summary>
        public static FactoryKey OutboxLayout { get; } = Key("gamecore.rewards.layout.outbox");

        /// <summary>Field key of the one physically owned field of the outbox slot (P-032).</summary>
        public static FactoryKey OutboxField { get; } = Key("gamecore.rewards.field.outbox.registration");

        /// <summary>Declared initialization policy key of the outbox slot (read by a `Reset`, which is not permitted).</summary>
        public static FactoryKey OutboxInitPolicy { get; } = Key("gamecore.rewards.policy.outbox.init");

        /// <summary>Declared configuration-update policy key: reconfiguration never resets the outbox (P-020).</summary>
        public static FactoryKey OutboxConfigChangePolicy { get; } =
            Key("gamecore.rewards.policy.outbox.config-change");

        /// <summary>
        /// Declared owner-transfer policy key. It is declared because P-032 requires every state slot to identify
        /// one, and because 07 s5 names a `TransferTo` alternative; see
        /// <see cref="RewardsInstallation.TransferOutboxTo"/> for the clause the shipped transfer contract cannot
        /// express for a slot whose last-support policy is `PreserveDormant`.
        /// </summary>
        public static FactoryKey OutboxTransferPolicy { get; } = Key("gamecore.rewards.policy.outbox.transfer");

        /// <summary>
        /// Registered version-change policy and the one registered migration of this package
        /// (`gamecore.rewards.migration.outbox.v1-v2`): it copies the pending-work count and refuses when the copy
        /// says work is still pending, which is 07 s5's scratch-migration precondition (P-032, P-029).
        /// </summary>
        public static FactoryKey OutboxMigration { get; } = Key(OutboxMigrationStableName);

        /// <summary>The registered managed resource the installation's pending work holds a lease on (P-048).</summary>
        public static ResourceKey OutboxResource { get; } = Resource("gamecore.rewards.resource.outbox");

        /// <summary>Recorded disposer key of the outbox resource lease (P-007, P-048).</summary>
        public static FactoryKey OutboxDisposer { get; } = Key("gamecore.rewards.disposer.outbox");

        // ---------------------------------------------------------------- stages, systems and buffers

        /// <summary>Stable name of `rewards.enqueue`: observe the committed choice and persist the obligation.</summary>
        public const string EnqueueStageStableName = "rewards.enqueue";

        /// <summary>Stable name of `rewards.ack`: hand the obligation to the card command endpoint and settle it.</summary>
        public const string AckStageStableName = "rewards.ack";

        /// <summary>`rewards.enqueue` (07 s5 steps 1-2): it commits the obligation the accepted choice produces.</summary>
        public static StageId EnqueueStage { get; } = Stage(EnqueueStageStableName);

        /// <summary>`rewards.ack` (07 s5 step 3): it dispatches the obligation and records the acknowledgement.</summary>
        public static StageId AckStage { get; } = Stage(AckStageStableName);

        /// <summary>Generated stage-level key of `rewards.enqueue`, beside the stage identity.</summary>
        public static FactoryKey EnqueueStageKey { get; } = Key("gamecore.rewards.stage-key.enqueue");

        /// <summary>Generated stage-level key of `rewards.ack`.</summary>
        public static FactoryKey AckStageKey { get; } = Key("gamecore.rewards.stage-key.ack");

        /// <summary>Generated dispatch key of the `rewards.enqueue` system.</summary>
        public static FactoryKey EnqueueSystem { get; } = Key("gamecore.rewards.system.enqueue");

        /// <summary>Generated dispatch key of the `rewards.ack` system.</summary>
        public static FactoryKey AckSystem { get; } = Key("gamecore.rewards.system.ack");

        /// <summary>
        /// The declared step buffer that carries the tentative receipt from `rewards.enqueue` to `rewards.ack`
        /// (07 s5's persist-then-apply order, P-043): one producer, one consuming stage, one step of lifetime.
        /// </summary>
        public static BufferId EnqueueBuffer { get; } = Buffer("gamecore.rewards.buffer.tentative-receipt");

        /// <summary>Payload schema of one tentative receipt row.</summary>
        public static SchemaRef ReceiptSchema { get; } = Schema("gamecore.rewards.schema.receipt", 1U);

        /// <summary>
        /// Owner of the tentative receipt. It is a routing identity only: the durable outbox row itself belongs to
        /// <see cref="OutboxOwner"/>, and the receipt is the step-local row the ack stage consumes (P-042).
        /// </summary>
        public static OwnerId ReceiptOwner { get; } = Owner("gamecore.rewards.owner.receipt");

        /// <summary>Order key of the receipt buffer; consumption order is declared, never incidental (P-008).</summary>
        public static FactoryKey ReceiptOrderKey { get; } = Key("gamecore.rewards.order.tentative-receipt");

        // ---------------------------------------------------------------- the installation's own command endpoint

        /// <summary>
        /// This package's own command route (`gamecore.rewards.route.rewards-command`): the endpoint an external
        /// caller submits a reward command through. 07 s5's "missing command endpoint" clause is about a plugin
        /// that declares a dependency and declares no way to be commanded; this route, its ingress lane and
        /// <see cref="StatusSchema"/> are that declaration, and `RewardsMounts.Messages()` binds them into the one
        /// shipped check the clause is assertable through
        /// (`GameCore.Unity.Runtime.Messages.MessagePlaneRegistration.TryValidate`: a route whose ingress buffer the
        /// registration does not declare is `DiagnosticCode.MissingDependency`).
        /// </summary>
        public static RouteId StatusRoute { get; } = Route("gamecore.rewards.route.rewards-command");

        /// <summary>Payload schema of one reward command admitted through <see cref="StatusRoute"/>.</summary>
        public static SchemaRef StatusSchema { get; } = Schema("gamecore.rewards.schema.rewards-command", 1U);

        /// <summary>Ingress buffer <see cref="StatusRoute"/> admits into; a route names one declared ingress (P-043).</summary>
        public static BufferId StatusLane { get; } = Buffer("gamecore.rewards.buffer.rewards-command-lane");

        /// <summary>Declared producer key the host's ingress rows carry, so a lane's origin is declared (P-043).</summary>
        public static FactoryKey HostIngressProducer { get; } = Key("gamecore.rewards.producer.host");

        /// <summary>Order key of the command lane; admission sequence first (P-008).</summary>
        public static FactoryKey StatusOrderKey { get; } = Key("gamecore.rewards.order.rewards-command");

        /// <summary>Declared row capacity of the receipt buffer and of the command lane (P-043).</summary>
        public const int LaneCapacity = 8;

        /// <summary>
        /// Declared byte capacity of the command lane's rows. One reward command carries a 28-byte reward payload
        /// (`RewardPayloadCodec.PayloadBytes`, Packages/com.gamecore.gameplay.integration/Runtime/RewardOutbox/
        /// RewardPayloadCodec.cs), so the bound is that payload with room for the canonical envelope, never zero
        /// (P-043).
        /// </summary>
        public const int LaneByteCapacity = 64;

        /// <summary>
        /// Re-derives every literal identity from its stable name with the production rule, so a literal and its
        /// name cannot drift apart silently (P-004). The check is a comparison of derived ids, never a prefix test.
        /// </summary>
        public static bool DerivationHolds()
        {
            return PluginFactory.RegistrationKey.Equals(StableNameKeyDerivation.Derive(PluginFactoryStableName))
                && PluginType.Value.Equals(StableNameKeyDerivation.Derive(PluginTypeStableName))
                && Installation.Value.Equals(StableNameKeyDerivation.Derive(InstallationStableName))
                && ConfigSchema.Id.Value.Equals(StableNameKeyDerivation.Derive(ConfigSchemaStableName))
                && OutboxOwner.Value.Equals(StableNameKeyDerivation.Derive(OutboxOwnerStableName))
                && OutboxSlot.Value.Equals(StableNameKeyDerivation.Derive(OutboxSlotStableName))
                && OutboxSchema.Id.Value.Equals(StableNameKeyDerivation.Derive(OutboxSchemaStableName))
                && OutboxMigration.RegistrationKey.Equals(StableNameKeyDerivation.Derive(OutboxMigrationStableName))
                && EnqueueStage.Value.Equals(StableNameKeyDerivation.Derive(EnqueueStageStableName))
                && AckStage.Value.Equals(StableNameKeyDerivation.Derive(AckStageStableName));
        }

        /// <summary>Stable name of `rewards.enqueue`'s counterpart in the narrative graph this package orders against.</summary>
        public const string QuestStageStableName = "narrative.quest";

        /// <summary>Stable name of the card commit stage this package's ack stage orders against.</summary>
        public const string CardsCommitStageStableName = "cards.stage.commit";

        /// <summary>One diagnostic line naming every identity this class derives, for a scenario's failure detail.</summary>
        public static string Describe() =>
            "rewardsKeys(installation=" + Installation.ToString()
            + ", factory=" + PluginFactory.ToString()
            + ", owner=" + OutboxOwner.ToString()
            + ", slot=" + OutboxSlot.ToString()
            + ", schema=" + OutboxSchema.ToString() + ")";
    }
}
