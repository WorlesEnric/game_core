// GameCore.Validation.ProbeHost — the W4 integration-gate card family adapter.
//
// The W4 gate's one runner (`W4GateScenario`) drives both genres through one scripted sequence; this file owns
// everything the card genre declares for it and nothing else. It is the card half of `IW4GateFamily`: it adds no
// fourth implementation of anything the kernel already has, because the lifecycle half is GC-014's installation
// lifecycle over the card market's own provider and the state-policy half is GC-015's declared slot surface over
// the card table's own owner.
//
// Five design decisions are worth naming, because a reader will ask about them:
//
//   * **The lifecycle subject.** `CardLifecycleScenario` chose the family's own scoring provider
//     (`CardVocabulary.FestivalScoring` at `cards.league-a`) as *the* installation whose activation carries
//     observable behavior, because it owns the `cards.set-bonus` rows of the two League A seats: a suspend
//     retracts exactly those rows and a resume restores them (P-046). The table runtime is mounted by the same
//     scenario, but it derives nothing, so a rows-based suspend/resume observation could not be a statement about
//     it. The W4 gate therefore suspends and resumes the same installation identity the GC-013 card host already
//     mounts, rather than inventing a second one.
//   * **The required-service pair.** The card revision's own table-runtime declaration makes its definition-lookup
//     dependency *optional* (`CardTableFixture.TableRuntimeDeclaration` passes `required: false`), so it produces
//     no required edge whose loss could make a consumer wait. This file declares its own pair through real
//     manifests: a scoring-shaped consumer whose dependency on <see cref="W4GateCardsHost.ServiceContract"/> is
//     **required**, and two scoring-shaped providers that export that contract — the second is the compatible
//     replacement whose return resumes the waiting consumer, which is the other half of P-012. Every one of them
//     declares a contribution on **its own capability identity**, because a published row is attributed to the
//     ranked winner of one `(target, capability, slot)` group (P-017): two installations sharing one capability
//     would leave all of that slot's rows with one of them and the other with nothing attributable. So "the
//     provider's rows are gone and the consumer waits" and "the consumer's rows are back" are statements about
//     real attributed rows. The service contract identity is this gate's own, so it never contends with the
//     revision's definition-lookup contract.
//   * **The second owner.** The card revision declares exactly one logical state owner
//     (`CardTableKeys.TableOwner`): one table runtime owns the table and every seat, which is what makes a
//     settlement of several entities one bounded domain decision (P-034). P-032's `TransferTo` names an
//     *available* owner, and this revision genuinely declares no second one, so this gate's own provider manifest
//     declares the destination owner beside the one slot that owner holds. That is the minimum a transfer
//     destination can be declared with, and it is reported as such rather than smuggled in through a policy
//     override: the runner reads the real `StatePolicyCatalog` this manifest produces.
//   * **The transfer's destination target.** A transfer moves the state under the *source* slot identity, so the
//     destination row is `(destinationTarget, destinationOwner, sourceSlot)`: one slot with a second owner, which
//     no single declaration can cover. A later policy pass that read that row would be refused as an ownership
//     conflict (P-034), so the transfer names a destination target that is deliberately *outside*
//     `PolicyTargets` — a real, live seat — while the destination owner stays declared by the manifest set.
//   * **The one policy target.** `PolicyTargets` names the market table alone. A policy executor decides on *every*
//     live slot row of the targets it is shown, so a target whose rows another manifest declares would be refused by
//     a pass validating against this gate's manifest only. The card table's authoritative state lives in this
//     package's ECS components rather than in the protocol's `TargetSlotState` rows, so the table's live rows are
//     exactly the ones this gate seeds and declares — the only target for which that is true.
//   * **No policy override, no hand-made authorization.** The reset case's permission is the manifest field GC-012
//     added (`StateSlotSpec`'s `resetSupported` plus its recorded reason), so the policy surface the runner
//     validates is the production one; nothing here constructs a `SlotStatePolicy` by hand.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Gameplay.Cards;
using GameCore.Gameplay.Cards.Fixtures;
using GameCore.Planning;
using GameCore.Planning.StatePolicies;
using GameCore.Rules.Cards;
using GameCore.Unity.Runtime.Integration;
using GameCore.Validation.GeneratedCards;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// The card family's declared facts for the W4 integration gate: its catalogs, its lifecycle-half identities
    /// and manifests, its state-policy provider manifest and the values its five slot cases carry. Every member
    /// here is data or a manifest builder; the runner owns the ordering, the publications and the observations.
    /// </summary>
    public static class W4GateCardsHost
    {
        /// <summary>Family label every observation name of this family carries.</summary>
        public const string Label = Gc013CardsHost.Label;

        /// <summary>Package version every W4-gate card declaration carries (mirrors the gameplay package's own).</summary>
        private const string PackageVersion = "1.0.0";

        /// <summary>
        /// The scope every installation this gate mounts itself lives at: the match root, which is the world's own
        /// root scope (P-010). It is an ancestor of both policy targets' scopes, which is what the state-policy
        /// provider's mount requires, and it is where GC-014 mounts the pair whose loss and return it observes.
        /// </summary>
        public static readonly ScopeId MatchScope = CardMarketComposition.MatchScope;

        // ------------------------------------------------ the required-service surface (P-011, P-012)

        /// <summary>Stable name of the consumer of this gate's required service (P-011).</summary>
        private const string RequiredConsumerName = "cards.w4gate.required-consumer";

        /// <summary>Stable name of the required service's provider, whose removal makes the consumer wait (P-012).</summary>
        private const string RequiredProviderName = "cards.w4gate.required-provider";

        /// <summary>Stable name of the compatible provider whose return resumes the waiting consumer (P-012).</summary>
        private const string RequiredReplacementName = "cards.w4gate.required-replacement";

        /// <summary>
        /// Stable name of the installation the unload step tears down (O-07). It is mounted by the sequence itself
        /// so the leases it staged are the ones its teardown retires in reverse acquisition order (P-047, P-048).
        /// </summary>
        private const string UnloadInstallName = "cards.w4gate.unload-install";

        /// <summary>
        /// The consumer's own capability: one output slot folded by the card rules package's *registered* Int32 sum
        /// reducer, exactly as the family's own scoring contract declares its slot (P-017, P-019, P-028). It is the
        /// consumer's alone because a published binding row is attributed to the ranked winner of one `(target,
        /// capability, slot)` group: an installation sharing another's capability would hold none of that slot's
        /// rows, and "the consumer's rows are gone and back" would have no subject (P-011, P-012, P-017).
        /// </summary>
        private const string RequiredConsumerCapability = "cards.w4gate.consumer-score";

        /// <summary>Payload schema of the consumer's capability's single output slot.</summary>
        private const string RequiredConsumerPayload = "cards.w4gate.consumer-score-value";

        /// <summary>
        /// The required provider's own capability, same shape and same reason: its rows are the ones the pair's loss
        /// retracts, so they must be attributable to it alone (P-012, P-017).
        /// </summary>
        private const string RequiredProviderCapability = "cards.w4gate.provider-score";

        /// <summary>Payload schema of the required provider's capability's single output slot.</summary>
        private const string RequiredProviderPayload = "cards.w4gate.provider-score-value";

        /// <summary>
        /// The compatible replacement's own capability, same shape: its return restores the consumer and
        /// contributes its own attributable rows under its own identity (P-009, P-012, P-017).
        /// </summary>
        private const string RequiredReplacementCapability = "cards.w4gate.replacement-score";

        /// <summary>Payload schema of the compatible replacement's capability's single output slot.</summary>
        private const string RequiredReplacementPayload = "cards.w4gate.replacement-score-value";

        /// <summary>
        /// The unload installation's own capability, same shape: its teardown retracts its own rows beside retiring
        /// its leases (P-047, P-048, P-017).
        /// </summary>
        private const string UnloadCapability = "cards.w4gate.unload-score";

        /// <summary>Payload schema of the unload installation's capability's single output slot.</summary>
        private const string UnloadPayload = "cards.w4gate.unload-score-value";

        /// <summary>Rule identity of the consumer's own contribution; one declaration per rule identity (P-021).</summary>
        private const string RequiredConsumerRule = "cards.w4gate.required-consumer.score";

        /// <summary>Rule identity of the required provider's contribution.</summary>
        private const string RequiredProviderRule = "cards.w4gate.required-provider.score";

        /// <summary>Rule identity of the compatible provider's contribution.</summary>
        private const string RequiredReplacementRule = "cards.w4gate.required-replacement.score";

        /// <summary>Rule identity of the unloaded installation's contribution.</summary>
        private const string UnloadRule = "cards.w4gate.unload-install.score";

        /// <summary>
        /// Value each of those four rules carries, one per seat beneath its scope (07 s2.1, P-019). Each folds alone
        /// in its own capability's slot, so each row's value is its own rule's value.
        /// </summary>
        private const int RequiredConsumerValue = 1;

        /// <summary>Payload value of the required provider's rule.</summary>
        private const int RequiredProviderValue = 2;

        /// <summary>Payload value of the compatible provider's rule.</summary>
        private const int RequiredReplacementValue = 3;

        /// <summary>Payload value of the unloaded installation's rule.</summary>
        private const int UnloadValue = 4;

        /// <summary>
        /// The contract the consumer requires and both providers export (P-011). It is this gate's own identity, so
        /// it never contends with the card revision's definition-lookup contract, whose dependency the revision
        /// declares as optional.
        /// </summary>
        public static readonly ContractRef ServiceContract =
            new ContractRef(new Id128(0x5734474154454E43UL, 1UL), 1U);

        /// <summary>Generated key of the exported service implementation both providers declare (P-012).</summary>
        public static readonly FactoryKey ServiceFactory = CardIdentity.Key("cards.w4gate.service.required");

        /// <summary>Installation of the required service's consumer (P-011).</summary>
        public static readonly PluginInstanceId RequiredConsumerInstall = CardTableKeys.Instance(RequiredConsumerName);

        /// <summary>Installation of the required service's provider, whose removal makes the consumer wait (P-012).</summary>
        public static readonly PluginInstanceId RequiredProviderInstall = CardTableKeys.Instance(RequiredProviderName);

        /// <summary>Installation of the compatible provider whose return resumes the consumer (P-012).</summary>
        public static readonly PluginInstanceId RequiredProviderReplacementInstall =
            CardTableKeys.Instance(RequiredReplacementName);

        /// <summary>Installation the unload step tears down through the P-048 order (O-07).</summary>
        public static readonly PluginInstanceId UnloadInstall = CardTableKeys.Instance(UnloadInstallName);

        // ------------------------------------------------ the state-policy provider (P-032, P-033)

        /// <summary>Stable name of the provider that declares this gate's slot policies (GC-015).</summary>
        private const string StatePolicyHostName = "cards.w4gate.state-policy-host";

        /// <summary>Installation of the provider that declares this gate's slot policies.</summary>
        public static readonly PluginInstanceId StatePolicyInstall = CardTableKeys.Instance(StatePolicyHostName);

        /// <summary>
        /// Owner of the five policy slots: the card table's own logical owner, because one table runtime owns the
        /// table and every seat and this revision declares no second owner (P-034).
        /// </summary>
        public static readonly OwnerId PolicyOwner = CardTableKeys.TableOwner;

        /// <summary>
        /// The second owner the transfer case moves state to. The card revision declares exactly one owner, so this
        /// gate declares the destination owner itself, beside the slot that owner holds (P-032): an owner a transfer
        /// can name is one this catalog revision declares.
        /// </summary>
        public static readonly OwnerId PolicyTransferOwner = CardIdentity.Owner("cards.w4gate.owner.policy-transfer");

        /// <summary>
        /// Registered owner-transfer policy key of the `TransferTo` slot. Its declaration must name one; the request
        /// that applies the declared last-support loss never supplies a policy of its own (P-032).
        /// </summary>
        public static readonly FactoryKey PolicyTransferPolicy = CardIdentity.Key("cards.w4gate.policy.transfer");

        /// <summary>
        /// Initialization policy of the reset slot. A reset writes the value this policy registers, never an
        /// implicit zero, so an unregistered policy would be a validation error (P-032).
        /// </summary>
        public static readonly FactoryKey PolicyResetInit = CardIdentity.Key("cards.w4gate.policy.reset-init");

        /// <summary>The value a declared reset of the reset slot writes: non-zero and not the seeded value (P-032).</summary>
        public const int PolicyResetValue = 7;

        /// <summary>
        /// The reason the reset slot's declaration records (P-032). The declaration's reason says why the slot may
        /// ever be reset; the proposal carries its own reason as well, and this gate's proposal reuses this literal
        /// so the recorded reason and the applied one are one fact, exactly as GC-015's card half does.
        /// </summary>
        public const string PolicyResetReason = "w4 gate: an operator-authorized repair of the table bookkeeping slot";

        /// <summary>Schema version every policy slot is declared and seeded at; no migration is declared for it.</summary>
        public const uint PolicySchemaVersion = 1U;

        /// <summary>Stable-name suffix of the slot the explicit `Preserve` case acts on (P-032).</summary>
        private const string PreserveSlotName = "preserve";

        /// <summary>Stable-name suffix of the slot whose declaration says `PreserveDormant` (P-032).</summary>
        private const string PreserveDormantSlotName = "preserve-dormant";

        /// <summary>Stable-name suffix of the slot whose declaration says `RemoveDerived` (P-032, P-033).</summary>
        private const string RemoveDerivedSlotName = "remove-derived";

        /// <summary>Stable-name suffix of the slot whose declaration says `TransferTo` (P-025, P-032).</summary>
        private const string TransferSlotName = "transfer";

        /// <summary>Stable-name suffix of the slot whose manifest declares reset support and a reason (P-032).</summary>
        private const string ResetSlotName = "reset";

        /// <summary>Stable-name suffix of the slot the destination owner holds, which makes it available (P-032).</summary>
        private const string DestinationSlotName = "destination";

        /// <summary>Slot the `Preserve` case acts on.</summary>
        public static readonly SlotId PolicyPreserveSlot = PolicySlotId(PreserveSlotName);

        /// <summary>Slot the `PreserveDormant` case acts on.</summary>
        public static readonly SlotId PolicyPreserveDormantSlot = PolicySlotId(PreserveDormantSlotName);

        /// <summary>Slot the `RemoveDerived` case acts on.</summary>
        public static readonly SlotId PolicyRemoveDerivedSlot = PolicySlotId(RemoveDerivedSlotName);

        /// <summary>Slot the `TransferTo` case acts on.</summary>
        public static readonly SlotId PolicyTransferSlot = PolicySlotId(TransferSlotName);

        /// <summary>Slot the `Reset` case acts on.</summary>
        public static readonly SlotId PolicyResetSlot = PolicySlotId(ResetSlotName);

        /// <summary>Slot the destination owner holds, so a transfer to that owner names a declared owner (P-032).</summary>
        public static readonly SlotId PolicyDestinationSlot = PolicySlotId(DestinationSlotName);

        // The values the cases are seeded with. Each is a value no default initialization produces, so a policy
        // that lost it is observable rather than inferred (P-032).
        /// <summary>Value the `Preserve` case's slot is seeded with.</summary>
        public const int PolicyPreserveValue = 17;

        /// <summary>Value the `PreserveDormant` case's slot is seeded with.</summary>
        public const int PolicyPreserveDormantValue = 23;

        /// <summary>Value the `RemoveDerived` case's slot is seeded with.</summary>
        public const int PolicyRemoveDerivedValue = 29;

        /// <summary>Value the `TransferTo` case's slot is seeded with.</summary>
        public const int PolicyTransferValue = 31;

        /// <summary>Value the `Reset` case's slot is seeded with, before the reset replaces it (P-032).</summary>
        public const int PolicyResetSeedValue = 37;

        // ------------------------------------------------ runs

        /// <summary>Runs the W4 sequence against the committed generated card catalog (GC-011 compiler output).</summary>
        public static W4GateScenarioResult RunGeneratedCatalog()
        {
            CatalogBuildResult build = CardCatalog.BuildVerifiedCatalog(out ContentHash _);
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the committed generated card catalog was rejected by the production catalog rules: "
                    + build.Describe());
            }

            ImmutableCatalog catalog = build.Catalog;
            if (!ContentHash.TryParseHex(CardCatalog.CatalogFingerprint, out ContentHash emittedFingerprint)
                || !catalog.Fingerprint.Equals(emittedFingerprint))
            {
                throw new InvalidOperationException(
                    "the generated card catalog's emitted fingerprint literal is not the catalog this run derived over (P-028).");
            }

            return W4GateScenario.Run(new Gc013CardsHost.CardFamily(
                catalog,
                Gc013CardsHost.Declarations(),
                CardCatalog.CatalogFingerprint));
        }

        /// <summary>Runs the W4 sequence against the hand-written generated-style catalog in the fixture package.</summary>
        public static W4GateScenarioResult RunFixtureCatalog()
        {
            CatalogBuildResult build = CardCatalogTable.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException("the hand-written card catalog was rejected: " + build.Describe());
            }

            ImmutableCatalog catalog = build.Catalog;
            return W4GateScenario.Run(new Gc013CardsHost.CardFamily(
                catalog,
                Gc013CardsHost.Declarations(),
                CardCatalogTable.Fingerprint().ToHex()));
        }

        /// <summary>
        /// Runs both catalogs and returns the combined observations: the generated-catalog steps keep their names,
        /// and the fixture-catalog steps are prefixed with <see cref="W4GateScenario.FixtureRunPrefix"/> so no two
        /// collide.
        /// </summary>
        public static IReadOnlyList<W4GateStep> RunBoth(
            out W4GateScenarioResult generated,
            out W4GateScenarioResult fixture)
        {
            generated = RunGeneratedCatalog();
            fixture = RunFixtureCatalog();

            var combined = new List<W4GateStep>(generated.Steps.Count + fixture.Steps.Count);
            for (int i = 0; i < generated.Steps.Count; i++)
            {
                combined.Add(generated.Steps[i]);
            }

            for (int i = 0; i < fixture.Steps.Count; i++)
            {
                W4GateStep step = fixture.Steps[i];
                combined.Add(new W4GateStep(W4GateScenario.FixtureRunPrefix + step.Name, step.Passed, step.Detail));
            }

            return combined;
        }

        // ------------------------------------------------ the lifecycle manifests

        /// <summary>
        /// The consumer of the required service: a scoring-shaped provider whose capability is its own
        /// (`cards.w4gate.consumer-score`, foldable by the registered Int32 sum reducer, so its rows are attributed
        /// to it alone) and whose dependency on <see cref="ServiceContract"/> is **required**, so removing the
        /// exporting provider leaves it `WaitingForDependencies` in that same publication (P-011, P-012, P-017).
        /// </summary>
        public static PluginManifest ConsumerManifest()
            => LifecycleManifest(
                RequiredConsumerName,
                RequiredConsumerRule,
                RequiredConsumerCapability,
                RequiredConsumerPayload,
                RequiredConsumerValue,
                null,
                RequiredDependency());

        /// <summary>
        /// The required service's provider: it exports <see cref="ServiceContract"/> and contributes its own rule
        /// under its own capability, so its removal both makes the consumer wait and retracts real attributed rows
        /// (P-012, P-017).
        /// </summary>
        public static PluginManifest ProviderManifest()
            => LifecycleManifest(
                RequiredProviderName,
                RequiredProviderRule,
                RequiredProviderCapability,
                RequiredProviderPayload,
                RequiredProviderValue,
                ServiceExports(),
                null);

        /// <summary>
        /// The compatible provider whose return resumes the waiting consumer: the same exported contract and
        /// factory under its own plugin type, installation identity, rule identity and capability, so the consumer's
        /// new binding names a *different* provider than the one it lost (P-009, P-012).
        /// </summary>
        public static PluginManifest ReplacementProviderManifest()
            => LifecycleManifest(
                RequiredReplacementName,
                RequiredReplacementRule,
                RequiredReplacementCapability,
                RequiredReplacementPayload,
                RequiredReplacementValue,
                ServiceExports(),
                null);

        /// <summary>
        /// The installation the unload step tears down: the same lifecycle-shaped manifest as the pair's providers
        /// (a contribution under its own capability, no stage, no buffer, no state slot), so its teardown is an
        /// accounting statement about the leases the sequence staged on it while its rows are a real contribution
        /// that goes with it (P-047, P-048, P-017).
        /// </summary>
        public static PluginManifest UnloadInstallManifest()
            => LifecycleManifest(
                UnloadInstallName,
                UnloadRule,
                UnloadCapability,
                UnloadPayload,
                UnloadValue,
                null,
                null);

        // ------------------------------------------------ the state-policy manifest

        /// <summary>
        /// The state-policy provider's manifest: six declared state slots and nothing else. Five of them carry the
        /// last-support policies this gate's cases name, and the sixth is the slot that makes the transfer
        /// destination owner available (P-032). The manifest declares no stage, buffer or capability, so it never
        /// enters the compiled schedule; its slots do enter the compiled ownership surface, which is what makes a
        /// policy pass over live rows of those slots legal (P-032, P-034).
        ///
        /// It is one instance, shared by the catalog declaration this revision compiles from and by every mount of
        /// this provider, so the declaration the lane resolves and the manifest a mount publishes cannot drift
        /// (P-009). It is declared here, after every identity it derives from, so its initializer sees them
        /// initialized (static field initializers run in textual declaration order).
        /// </summary>
        public static readonly PluginManifest StatePolicyManifest = BuildStatePolicyManifest();

        private static PluginManifest BuildStatePolicyManifest()
        {
            return new PluginManifest(
                CardTableKeys.PluginType(StatePolicyHostName),
                PackageVersion,
                ContentHash.Empty,
                new SupportedProtocolRange(1, 0, 0),
                null,
                CardTableKeys.ConfigSchema,
                CardTableKeys.PluginFactoryKey,
                null,
                null,
                null,
                null,
                null,
                PolicySlots(),
                null,
                null,
                null);
        }

        /// <summary>
        /// The initialization values a declared reset reads from (P-032): the reset slot's declared initialization
        /// policy resolves to a declared value, so a reset writes a value this revision states rather than a zero.
        /// </summary>
        public static InitializationPolicyRegistry CreatePolicyInitialValues()
        {
            var registry = new InitializationPolicyRegistry();
            registry.Register(PolicyResetInit, PolicyDomain(ResetSlotName), PolicyResetValue);
            return registry;
        }

        // ------------------------------------------------ the slot declarations

        /// <summary>
        /// The six slots of the state-policy manifest. Each declares its own domain, layout key and physical field,
        /// so no two slots share a storage shape and no domain has two owners (P-033, P-034), and each names its own
        /// declared last-support policy. Only the reset slot declares reset support, and it records the reason P-032
        /// requires beside it.
        /// </summary>
        private static IReadOnlyList<StateSlotSpec> PolicySlots()
        {
            return new List<StateSlotSpec>
            {
                // The preserve case: an explicit `Preserve` request is legal against any declaration (it is P-032's
                // compatibility default), so this slot only has to exist with real committed state on it.
                PolicySlot(PreserveSlotName, PolicyOwner, LastSupportPolicy.PreserveDormant, default(FactoryKey), false, null),

                // A durable domain that retains its value dormant when its last support leaves (P-032).
                PolicySlot(PreserveDormantSlotName, PolicyOwner, LastSupportPolicy.PreserveDormant, default(FactoryKey), false, null),

                // A step-scoped domain: its declaration says the state is disposable derived data (P-032, P-033).
                PolicySlot(RemoveDerivedSlotName, PolicyOwner, LastSupportPolicy.RemoveDerived, default(FactoryKey), false, null),

                // The transferable domain: losing its last support moves the value to a named available owner (P-025).
                PolicySlot(TransferSlotName, PolicyOwner, LastSupportPolicy.TransferTo, PolicyTransferPolicy, false, null),

                // The reset permission is the manifest field GC-012 added: the declaration is what authorizes the
                // reset, beside the proposal reason the runner submits (P-032).
                PolicySlot(ResetSlotName, PolicyOwner, LastSupportPolicy.PreserveDormant, default(FactoryKey), true, PolicyResetReason),

                // The destination owner's own slot. Without it this revision would declare one owner only, and a
                // transfer to a second owner would be refused as one this revision does not declare (P-032).
                PolicySlot(DestinationSlotName, PolicyTransferOwner, LastSupportPolicy.PreserveDormant, default(FactoryKey), false, null),
            };
        }

        /// <summary>
        /// One declared policy slot: its own domain, layout key and single physical field, the declared last-support
        /// policy, the registered owner-transfer policy when the declaration says `TransferTo`, and the explicit
        /// reset support with its recorded reason when the manifest supports one (P-032, P-033). Every slot is
        /// declared at schema version one and names no migration key, because this gate's state is created with its
        /// world and no version change is requested.
        /// </summary>
        private static StateSlotSpec PolicySlot(
            string name,
            OwnerId owner,
            LastSupportPolicy lastSupport,
            FactoryKey transferPolicy,
            bool resetSupported,
            string? resetReason)
        {
            SchemaRef domain = PolicyDomain(name);
            var ownership = new List<FieldOwnership>
            {
                new FieldOwnership(domain, PolicyField(name).RegistrationKey),
            };

            return new StateSlotSpec(
                PolicySlotId(name),
                owner,
                domain,
                PolicyLayout(name),
                ownership,
                resetSupported ? PolicyResetInit : default(FactoryKey),
                default(FactoryKey),
                default(FactoryKey),
                lastSupport,
                transferPolicy,
                null,
                resetSupported,
                resetReason);
        }

        /// <summary>The declared slot identity of one policy slot.</summary>
        private static SlotId PolicySlotId(string name) => CardIdentity.Slot("cards.w4gate.slot.policy-" + name);

        /// <summary>The authoritative domain of one policy slot (P-034: one owner per domain).</summary>
        private static SchemaRef PolicyDomain(string name)
            => CardIdentity.SchemaRef("cards.w4gate.domain.policy-" + name);

        /// <summary>The physical layout key of one policy slot: its own component's ownership claim (P-033).</summary>
        private static FactoryKey PolicyLayout(string name) => CardIdentity.Key("cards.w4gate.layout.policy-" + name);

        /// <summary>The single physical field one policy slot's layout holds (P-033).</summary>
        private static FactoryKey PolicyField(string name)
            => CardIdentity.Key("cards.w4gate.field.policy-" + name + ".cursor");

        // ------------------------------------------------ the lifecycle manifest builder

        /// <summary>
        /// One lifecycle-shaped installation: its own plugin type, rule identity and capability contract, an
        /// optional service export and an optional dependency. Its own capability is the point: a published row is
        /// attributed to the ranked winner of one `(target, capability, slot)` group (P-017), so each installation
        /// must be the only contributor of its own capability for its rows to be attributable to it. It declares no
        /// stage, buffer, state slot or resource, so it never enters the compiled schedule and its lifecycle is the
        /// only thing it can be observed through (P-011, P-046, P-048).
        /// </summary>
        private static PluginManifest LifecycleManifest(
            string stableName,
            string ruleStableName,
            string capability,
            string payloadSchema,
            int value,
            IReadOnlyList<ServiceExport>? exports,
            IReadOnlyList<ServiceDependency>? dependencies)
        {
            return new PluginManifest(
                CardTableKeys.PluginType(stableName),
                PackageVersion,
                ContentHash.Empty,
                new SupportedProtocolRange(1, 0, 0),
                null,
                CardTableKeys.ConfigSchema,
                CardTableKeys.PluginFactoryKey,
                exports,
                dependencies,
                new List<CapabilityContract> { RequiredContract(capability, payloadSchema) },
                new List<DerivationRule> { RequiredRule(ruleStableName, capability, value) },
                null,
                null,
                null,
                null,
                null);
        }

        /// <summary>The export both providers declare: the required contract under this gate's service factory (P-012).</summary>
        private static IReadOnlyList<ServiceExport> ServiceExports()
        {
            return new List<ServiceExport>
            {
                new ServiceExport(
                    ServiceContract,
                    ServiceFactory,
                    ServiceVisibility.ExportToDescendants,
                    false,
                    ServiceBindingKind.Single),
            };
        }

        /// <summary>
        /// The consumer's required dependency on <see cref="ServiceContract"/>. `AncestorsAndSelf` is the resolution
        /// domain both providers are mounted inside, so the edge resolves from the consumer's own scope and its loss
        /// is exactly the removal P-012 describes.
        /// </summary>
        private static IReadOnlyList<ServiceDependency> RequiredDependency()
        {
            return new List<ServiceDependency>
            {
                new ServiceDependency(
                    ServiceContract,
                    new VersionRange(1U, 1U),
                    true,
                    ServiceResolutionDomain.AncestorsAndSelf,
                    default(ProviderInstallationId),
                    default(FactoryKey)),
            };
        }

        /// <summary>
        /// One lifecycle installation's capability contract: one output slot under the `Additive` policy with the
        /// card rules package's *registered* Int32 sum reducer, exactly as the family's own scoring contract
        /// declares its slot (P-017, P-019, P-028). The registered path is what makes the contribution appear in
        /// real derived storage.
        /// </summary>
        private static CapabilityContract RequiredContract(string capability, string payloadSchema)
        {
            SlotId slot = CardIdentity.Slot(capability + ".slot-0");
            return new CapabilityContract(
                CardIdentity.CapabilityRef(capability),
                CardVocabulary.BonusStratum,
                new List<OutputSlotSchema>
                {
                    new OutputSlotSchema(slot, CardIdentity.SchemaRef(payloadSchema)),
                },
                new List<SlotCompositionPolicy>
                {
                    new SlotCompositionPolicy(
                        slot,
                        CompositionPolicy.Additive,
                        CardVocabulary.BonusReducerKey),
                },
                null);
        }

        /// <summary>
        /// One lifecycle installation's rule. The reusable seat recipe is its selector, so its contribution reaches
        /// the seats beneath its scope and nothing else, and the registered always-accepting predicate selects them
        /// (P-013, P-015).
        /// </summary>
        private static DerivationRule RequiredRule(string ruleStableName, string capability, int value)
        {
            return new DerivationRule(
                CardIdentity.Rule(ruleStableName),
                CardIdentity.CapabilityRef(capability),
                CardVocabulary.BonusStratum,
                1U,
                new List<SchemaRef> { CardVocabulary.SelectorSchema(CardVocabulary.CardSeatRecipe) },
                CardVocabulary.AlwaysPredicateKey,
                null,
                PropagationReach.SelfAndDescendants,
                true,
                0,
                CompositionPolicy.Additive,
                CardTableDeclarations.WriteInt32(value));
        }
    }

    /// <summary>
    /// The card family as the W4 integration gate's <see cref="IW4GateFamily"/>. This is the second half of the
    /// partial class <c>Gc013CardsHost.CardFamily</c> declares: GC-013's own half (catalog, declared scope tree,
    /// live targets, move and mode edits) is untouched, and everything GC-014's installation lifecycle and GC-015's
    /// declared slot policies add is here.
    /// </summary>
    public static partial class Gc013CardsHost
    {
        public sealed partial class CardFamily : IW4GateFamily
        {
            /// <summary>
            /// The gate's registered initialization values. A declared reset writes one of these, so the value it
            /// writes is a declared fact rather than an implicit zero (P-032).
            /// </summary>
            private static readonly IInitializationPolicyRegistry PolicyInitialValues =
                W4GateCardsHost.CreatePolicyInitialValues();

            /// <summary>
            /// The five declared cases, in the order the sequence reports them: the compatibility default
            /// `Preserve`, then the three last-support outcomes, then the explicit manifest-supported reset.
            /// </summary>
            private static readonly IReadOnlyList<W4GateSlotCase> DeclaredSlotCases = BuildSlotCases();

            /// <summary>
            /// One neutral scope creation per case, in the same order. A gate needs one per case because a scope is
            /// created once per composition and each policy pass rides its own publication; none of these scopes
            /// holds a live target, so the pass's dispositions are that publication's only effective change (P-010).
            /// </summary>
            private static readonly IReadOnlyList<CompositionEditPayload> DeclaredNeutralEdits = BuildNeutralEdits();

            /// <summary>
            /// The live targets the policy pass runs over: the market table alone. The card table's own authoritative
            /// state lives in this package's ECS components, not in the protocol's `TargetSlotState` rows, so the
            /// table's live slot rows are exactly the four this gate seeds, and every one of them is declared by this
            /// gate's own manifest — which is what makes a pass over this target decide on declared state rather than
            /// reject an undeclared row (P-032). The seat the sequence reparents is deliberately not here: GC-013's
            /// own seed writes a card slot on it, and a policy set built from one manifest alone must not be asked to
            /// decide a row another manifest declares. The transfer destination is deliberately not here either: a
            /// transferred row carries the source slot identity under a second owner, which no single declaration
            /// covers (P-034).
            /// </summary>
            private static readonly IReadOnlyList<TargetId> DeclaredPolicyTargets = new List<TargetId>
            {
                CardIdentity.Target(CardVocabulary.TableOne),
            };

            /// <summary>
            /// The four lifecycle installations' manifests, in the order the properties below name them: the required
            /// service's consumer, its provider, the compatible replacement provider, and the installation the unload
            /// step tears down. Built once, so a manifest the lane resolves and the manifest a mount publishes are one
            /// instance rather than two equal ones (P-009).
            /// </summary>
            private static readonly IReadOnlyList<PluginManifest> LifecycleManifests = BuildLifecycleManifests();

            /// <summary>
            /// The manifests the lane's manifest source must resolve beside the catalog's own declarations: the four
            /// lifecycle installations and the state-policy provider. The state-policy provider is in both because a
            /// declaration drives the compiled ownership and schedule surface while these four drive nothing but
            /// their own installation lifecycle.
            /// </summary>
            private static readonly IReadOnlyList<PluginManifest> ExtraManifestList = BuildExtraManifests();

            /// <summary>The lifecycle provider's own declaration, resolved once rather than re-derived (P-009).</summary>
            private static readonly PluginManifest DeclaredLifecycleProviderManifest =
                CardTableFixture.ScoringDeclaration(true).Manifest;

            // ------------------------------------------------ the lifecycle half (GC-014)

            /// <summary>
            /// The installation the sequence suspends and resumes: the family's own scoring provider at
            /// `cards.league-a`. It is the installation whose activation carries the genre's real behavior, because
            /// it owns the `cards.set-bonus` rows of the two League A seats, so "suspend retracts behavior and resume
            /// restores it" is a statement about real attributed rows (P-046).
            /// </summary>
            public PluginInstanceId LifecycleProviderInstall => CardTableFixture.FestivalScoringInstance;

            /// <summary>The scope that installation is mounted at: the festival league (07 s2.1).</summary>
            public ScopeId LifecycleProviderScope => CardIdentity.Scope(CardVocabulary.LeagueA);

            /// <summary>The festival scoring provider's own declaration, exactly as the GC-013 half mounts it.</summary>
            public PluginManifest LifecycleProviderManifest => DeclaredLifecycleProviderManifest;

            /// <summary>Installation of the required service's consumer (P-011).</summary>
            public PluginInstanceId RequiredConsumerInstall => W4GateCardsHost.RequiredConsumerInstall;

            /// <summary>The match root: the consumer sits where the providers' export reaches it (P-011).</summary>
            public ScopeId RequiredConsumerScope => W4GateCardsHost.MatchScope;

            /// <summary>The consumer's manifest, with its required <see cref="RequiredService"/> dependency.</summary>
            public PluginManifest RequiredConsumerManifest => LifecycleManifests[0];

            /// <summary>Installation of the provider whose removal makes the consumer wait (P-012).</summary>
            public PluginInstanceId RequiredProviderInstall => W4GateCardsHost.RequiredProviderInstall;

            /// <summary>The scope the provider exports the contract from: the same match root.</summary>
            public ScopeId RequiredProviderScope => W4GateCardsHost.MatchScope;

            /// <summary>The provider's manifest: the export of the required contract beside its own contribution.</summary>
            public PluginManifest RequiredProviderManifest => LifecycleManifests[1];

            /// <summary>The compatible provider whose return resumes the waiting consumer (P-012).</summary>
            public PluginInstanceId RequiredProviderReplacementInstall =>
                W4GateCardsHost.RequiredProviderReplacementInstall;

            /// <summary>The compatible provider's manifest: its own identities, the same exported contract.</summary>
            public PluginManifest RequiredProviderReplacementManifest => LifecycleManifests[2];

            /// <summary>The contract the consumer requires and both providers export (P-011).</summary>
            public ContractRef RequiredService => W4GateCardsHost.ServiceContract;

            /// <summary>Installation the unload step tears down through the P-048 order (O-07).</summary>
            public PluginInstanceId UnloadInstall => W4GateCardsHost.UnloadInstall;

            /// <summary>The match root, where GC-014 mounts the installation it unloads (P-010).</summary>
            public ScopeId UnloadScope => W4GateCardsHost.MatchScope;

            /// <summary>The unload installation's manifest: no stage, buffer or state slot.</summary>
            public PluginManifest UnloadManifest => LifecycleManifests[3];

            /// <summary>O-03: the package's own mount payload, so a mount's declared hash is the applier's (P-020).</summary>
            public CompositionEditPayload MountInstall(PluginManifest manifest, PluginInstanceId instance, ScopeId scope)
                => CardTablePayloads.Mount(manifest, instance, scope);

            /// <summary>O-06: the card family's own suspend payload (P-046).</summary>
            public CompositionEditPayload SuspendInstall(PluginInstanceId instance)
                => CardLifecyclePayloads.Suspend(instance);

            /// <summary>O-04: the card family's own resume payload (P-046).</summary>
            public CompositionEditPayload ResumeInstall(PluginInstanceId instance)
                => CardLifecyclePayloads.Resume(instance);

            /// <summary>O-07: the package's own unmount payload, whose teardown sequencing follows (P-048).</summary>
            public CompositionEditPayload UnmountInstall(PluginInstanceId instance)
                => CardTablePayloads.Unmount(instance);

            // ------------------------------------------------ the state-policy half (GC-015)

            /// <summary>Installation of the provider that declares this gate's slot policies.</summary>
            public PluginInstanceId StatePolicyInstall => W4GateCardsHost.StatePolicyInstall;

            /// <summary>
            /// The match root: the ancestor of the market table's own area, so a mount there is an ancestor of the
            /// policy target and the provider's appearance is a real revision of the lane (P-010).
            /// </summary>
            public ScopeId StatePolicyScope => W4GateCardsHost.MatchScope;

            /// <summary>The provider manifest with the six declared slots (P-032, P-033).</summary>
            public PluginManifest StatePolicyManifest => W4GateCardsHost.StatePolicyManifest;

            /// <summary>O-03: mount the state-policy provider, whose declared slots the cases act on.</summary>
            public CompositionEditPayload MountStatePolicyHost()
                => CardTablePayloads.Mount(
                    W4GateCardsHost.StatePolicyManifest,
                    W4GateCardsHost.StatePolicyInstall,
                    W4GateCardsHost.MatchScope);

            /// <summary>The five declared cases, in the order the sequence reports them (P-032).</summary>
            public IReadOnlyList<W4GateSlotCase> SlotCases => DeclaredSlotCases;

            /// <summary>One neutral scope creation per case, in <see cref="SlotCases"/> order.</summary>
            public IReadOnlyList<CompositionEditPayload> NeutralEdits => DeclaredNeutralEdits;

            /// <summary>The target every policy pass runs over.</summary>
            public IReadOnlyList<TargetId> PolicyTargets => DeclaredPolicyTargets;

            /// <summary>
            /// The migrations this revision registers: the family's own registry, the same one GC-013's card run
            /// publishes through, so a migration identity is declared once (P-029, P-054).
            /// </summary>
            public MigrationRegistry PolicyMigrations => CreateMigrations();

            /// <summary>The declared initialization values a reset reads from (P-032).</summary>
            public IInitializationPolicyRegistry InitialValues => PolicyInitialValues;

            /// <summary>The four lifecycle manifests and the state-policy provider's manifest.</summary>
            public IReadOnlyList<PluginManifest> ExtraManifests => ExtraManifestList;

            /// <summary>
            /// Seeds one case's slot with its non-default value at its declared schema version, through the world's
            /// real seeder, so the policy acts on state the world really owns rather than on a value an assertion
            /// invented (P-032). A refusal is reported as `false` rather than thrown, because the sequence observes
            /// the seeding it asked for.
            /// </summary>
            public bool SeedSlotCase(W4GateSlotCase slotCase, LiveTargetSeeder seeder)
            {
                if (seeder == null)
                {
                    throw new ArgumentNullException(nameof(seeder));
                }

                return seeder.TrySeedSlot(
                    slotCase.Slot.Target,
                    slotCase.Slot.Owner,
                    slotCase.Slot.Slot,
                    slotCase.SchemaVersion,
                    slotCase.Value,
                    out DiagnosticCode _,
                    out string _);
            }

            /// <summary>
            /// The five cases. Each names a slot this revision declares, the value it was seeded with, and - for the
            /// transfer - the destination owner this revision declares. The reset carries the reason its declaration
            /// records, and the transfer names a destination target outside <see cref="PolicyTargets"/> so the row it
            /// writes is not read by a later pass (P-032).
            /// </summary>
            private static IReadOnlyList<W4GateSlotCase> BuildSlotCases()
            {
                TargetId table = CardIdentity.Target(CardVocabulary.TableOne);

                // The quiet league's seat: a real, always-live target that no policy case acts on, so the row a
                // transfer writes there is never read back by a later pass of this gate.
                TargetId transferDestination = CardTableFixture.SeatTarget(CardTableKeys.SeatCOrdinal);

                return new List<W4GateSlotCase>
                {
                    new W4GateSlotCase(
                        W4GateSlotPolicy.Preserve,
                        new StateSlotKey(table, W4GateCardsHost.PolicyOwner, W4GateCardsHost.PolicyPreserveSlot),
                        W4GateCardsHost.PolicySchemaVersion,
                        W4GateCardsHost.PolicyPreserveValue,
                        default(OwnerId),
                        default(TargetId),
                        string.Empty),

                    // A durable domain of the same target: `PreserveDormant` retains this slot's committed value while
                    // its own declaration stops claiming an active writer (P-032).
                    new W4GateSlotCase(
                        W4GateSlotPolicy.PreserveDormant,
                        new StateSlotKey(table, W4GateCardsHost.PolicyOwner, W4GateCardsHost.PolicyPreserveDormantSlot),
                        W4GateCardsHost.PolicySchemaVersion,
                        W4GateCardsHost.PolicyPreserveDormantValue,
                        default(OwnerId),
                        default(TargetId),
                        string.Empty),

                    // The disposable-derived domain: "this row is gone while its durable sibling on the same target
                    // stayed" is one observation about one target (P-032, P-033).
                    new W4GateSlotCase(
                        W4GateSlotPolicy.RemoveDerived,
                        new StateSlotKey(table, W4GateCardsHost.PolicyOwner, W4GateCardsHost.PolicyRemoveDerivedSlot),
                        W4GateCardsHost.PolicySchemaVersion,
                        W4GateCardsHost.PolicyRemoveDerivedValue,
                        default(OwnerId),
                        default(TargetId),
                        string.Empty),

                    // The transfer moves the ledger slot's state to the second owner this revision declares, carrying
                    // the value across unchanged (P-025, P-032).
                    new W4GateSlotCase(
                        W4GateSlotPolicy.TransferTo,
                        new StateSlotKey(table, W4GateCardsHost.PolicyOwner, W4GateCardsHost.PolicyTransferSlot),
                        W4GateCardsHost.PolicySchemaVersion,
                        W4GateCardsHost.PolicyTransferValue,
                        W4GateCardsHost.PolicyTransferOwner,
                        transferDestination,
                        string.Empty),

                    new W4GateSlotCase(
                        W4GateSlotPolicy.Reset,
                        new StateSlotKey(table, W4GateCardsHost.PolicyOwner, W4GateCardsHost.PolicyResetSlot),
                        W4GateCardsHost.PolicySchemaVersion,
                        W4GateCardsHost.PolicyResetSeedValue,
                        default(OwnerId),
                        default(TargetId),
                        W4GateCardsHost.PolicyResetReason),
                };
            }

            /// <summary>
            /// One neutral scope creation per declared case, in <see cref="SlotCases"/> order, in the exact payload
            /// shape the control lane's applier validates (P-010). No live target lives in any of them, so the scope
            /// it creates is the publication's only effective change beside the policy plan, and the count is read
            /// from the cases themselves rather than restated, so the two can never disagree.
            /// </summary>
            private static IReadOnlyList<CompositionEditPayload> BuildNeutralEdits()
            {
                int count = DeclaredSlotCases.Count;
                var edits = new List<CompositionEditPayload>(count);
                for (int i = 0; i < count; i++)
                {
                    edits.Add(CardTablePayloads.ScopeCreate(
                        CardIdentity.Scope(
                            "cards.w4gate.neutral-scope-"
                            + i.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        W4GateCardsHost.MatchScope,
                        false));
                }

                return edits;
            }

            /// <summary>
            /// The four lifecycle installations' manifests, in the order the accessors above name them: the required
            /// service's consumer, its provider, the compatible replacement provider, and the installation the unload
            /// step tears down. Each is the host's own declaration, built once here so the instance a mount publishes
            /// and the instance the lane resolves through the manifest source are the same one (P-009).
            /// </summary>
            private static IReadOnlyList<PluginManifest> BuildLifecycleManifests()
            {
                return new List<PluginManifest>
                {
                    W4GateCardsHost.ConsumerManifest(),
                    W4GateCardsHost.ProviderManifest(),
                    W4GateCardsHost.ReplacementProviderManifest(),
                    W4GateCardsHost.UnloadInstallManifest(),
                };
            }

            /// <summary>
            /// Everything the lane's manifest source must resolve that is not part of this revision's compiled
            /// declaration list: the four lifecycle installations and the state-policy provider (P-009). The
            /// state-policy provider's manifest is the one instance the declaration set carries, so listing it here
            /// adds no second declaration of its plugin type.
            /// </summary>
            private static IReadOnlyList<PluginManifest> BuildExtraManifests()
            {
                return new List<PluginManifest>
                {
                    LifecycleManifests[0],
                    LifecycleManifests[1],
                    LifecycleManifests[2],
                    LifecycleManifests[3],
                    W4GateCardsHost.StatePolicyManifest,
                };
            }
        }
    }
}
