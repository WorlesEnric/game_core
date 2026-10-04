// GameCore.Unity.App — SADR-010 (studio): the immutable description a game hands to its application root.
//
// A definition is data plus generated-style registrations: the catalog and its declared fingerprint, the plugin
// declarations the control lane resolves, the world's identity and temporal model, the propagation mode, the root
// scope and the scope seeds, the system registrations and dispatch kinds the ownership/schedule pipeline compiles,
// the recipes, the derivation value source, the configuration bindings (SADR-013) and an ordered boot script of
// target seeds and composition edits. Nothing here discovers anything at runtime (04 s3).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Execution.Messages;
using GameCore.Planning;
using GameCore.Planning.Ownership;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;
using GameCore.Unity.Runtime.Persistence;
using GameCore.Unity.Runtime.Time;

namespace GameCore.Unity.App
{
    /// <summary>What one boot step does (SADR-010).</summary>
    public enum GameApplicationBootStepKind
    {
        /// <summary>Seeds one live target of a registered recipe under a scope (P-024, 04 s6).</summary>
        SeedTarget = 0,

        /// <summary>Submits one composition edit through the bridge (scope creation, mount, configure; O-02, O-03).</summary>
        Edit = 1,
    }

    /// <summary>
    /// One ordered step of the boot script. Scopes must exist before targets are seeded under them and targets before
    /// a provider is mounted over them, so the definition states the order instead of the root guessing it.
    /// </summary>
    public sealed class GameApplicationBootStep
    {
        private GameApplicationBootStep(
            GameApplicationBootStepKind kind,
            string name,
            TargetId target,
            ScopeId scope,
            DefinitionRef recipe,
            CompositionEditPayload? edit)
        {
            Kind = kind;
            Name = string.IsNullOrEmpty(name) ? kind.ToString() : name;
            Target = target;
            Scope = scope;
            Recipe = recipe;
            Edit = edit;
        }

        public GameApplicationBootStepKind Kind { get; }

        /// <summary>Stable name the boot failure reports when this step refuses.</summary>
        public string Name { get; }

        public TargetId Target { get; }

        public ScopeId Scope { get; }

        public DefinitionRef Recipe { get; }

        public CompositionEditPayload? Edit { get; }

        /// <summary>A target seed: <paramref name="target"/> of <paramref name="recipe"/> under <paramref name="scope"/>.</summary>
        public static GameApplicationBootStep Seed(string name, TargetId target, ScopeId scope, DefinitionRef recipe)
        {
            if (target.IsDefault)
            {
                throw new ArgumentException("A seeded target has a real identity (P-004).", nameof(target));
            }

            if (scope.IsDefault)
            {
                throw new ArgumentException("A seeded target has one owner scope (P-010).", nameof(scope));
            }

            return new GameApplicationBootStep(GameApplicationBootStepKind.SeedTarget, name, target, scope, recipe, null);
        }

        /// <summary>A composition edit applied through the bridge at the then-published revision.</summary>
        public static GameApplicationBootStep Apply(string name, CompositionEditPayload edit)
        {
            if (edit == null)
            {
                throw new ArgumentNullException(nameof(edit));
            }

            return new GameApplicationBootStep(
                GameApplicationBootStepKind.Edit, name, default(TargetId), default(ScopeId), default(DefinitionRef), edit);
        }

        public override string ToString() => Kind.ToString() + "(" + Name + ")";
    }

    /// <summary>
    /// The restore seam a later packet fills (SADR-010, P1.x checkpoint/restore): given the booted root, it supplies the
    /// <see cref="IRestoreTargetBuilder"/> that builds an unexposed staging world for a restore plan. The root never
    /// restores by itself; it only hands the hook what the hook needs.
    /// </summary>
    public interface IGameApplicationRestoreHook
    {
        IRestoreTargetBuilder CreateRestoreTargetBuilder(GameApplicationRoot root);
    }

    /// <summary>Bounds of the application's derivation provenance store (P-026).</summary>
    public readonly struct GameApplicationProvenanceSettings
    {
        public GameApplicationProvenanceSettings(int epochRetention, int maxRecordsPerEpoch, int maxEntriesPerEpoch, int stagedRetention)
        {
            if (epochRetention <= 0 || maxRecordsPerEpoch <= 0 || maxEntriesPerEpoch <= 0 || stagedRetention <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(epochRetention), "Every provenance bound is positive (P-026).");
            }

            EpochRetention = epochRetention;
            MaxRecordsPerEpoch = maxRecordsPerEpoch;
            MaxEntriesPerEpoch = maxEntriesPerEpoch;
            StagedRetention = stagedRetention;
        }

        /// <summary>The bounds the observation suites use: eight epochs, 8192 records, 4096 entries, eight staged plans.</summary>
        public static GameApplicationProvenanceSettings Default => new GameApplicationProvenanceSettings(8, 8192, 4096, 8);

        public int EpochRetention { get; }

        public int MaxRecordsPerEpoch { get; }

        public int MaxEntriesPerEpoch { get; }

        public int StagedRetention { get; }

        /// <summary>True for a value built through the constructor; a default struct has zero bounds.</summary>
        public bool IsSpecified => EpochRetention > 0;
    }

    /// <summary>
    /// The immutable definition of one game application (SADR-010). Build it with <see cref="Builder"/>; the root
    /// validates it again at boot and reports every defect as a typed <see cref="GameApplicationBootFailed"/>.
    /// </summary>
    public sealed class GameApplicationDefinition
    {
        internal GameApplicationDefinition(Builder builder)
        {
            Name = builder.ApplicationName;
            Catalog = builder.CatalogValue ?? throw new ArgumentException("A game application declares its catalog (P-009).");
            CatalogHash = builder.CatalogHashValue;
            Plugins = Freeze(builder.PluginList);
            WorldDefinition = builder.WorldDefinitionValue;
            TemporalModel = builder.TemporalModelValue;
            FixedStep = builder.FixedStepValue;
            Propagation = builder.PropagationValue;
            RootScope = builder.RootScopeValue;
            ScopeSeeds = Freeze(builder.ScopeSeedList);
            Systems = Freeze(builder.SystemList);
            DispatchKinds = builder.DispatchKindsValue ?? new ScheduleDispatchKindTable();
            SlotPolicyMigrations = builder.SlotPolicyMigrationsValue ?? new SlotMigrationRegistry();
            Migrations = Freeze(builder.MigrationList);
            Messages = builder.MessagesValue;
            MessageReaders = builder.MessageReadersValue;
            SeedWorldState = builder.SeedWorldStateValue;
            Recipes = builder.RecipesValue ?? new SpawnRecipeCatalog(new List<SpawnRecipe>());
            Values = builder.ValuesValue ?? throw new ArgumentException("A game application declares its derivation value source (P-019).");
            RuleKeys = Freeze(builder.RuleKeyList);
            Overrides = Freeze(builder.OverrideList);
            ConfigBindings = Freeze(builder.ConfigBindingList);
            Budget = builder.BudgetValue ?? new PlanBudget(1024UL * 1024UL, 1024UL * 1024UL, 64UL * 1024UL, 16UL);
            StagedByteCeiling = builder.StagedByteCeilingValue;
            TargetCapacity = builder.TargetCapacityValue;
            Issuer = builder.IssuerValue;
            BootSteps = Freeze(builder.BootStepList);
            Provenance = builder.ProvenanceValue.IsSpecified ? builder.ProvenanceValue : GameApplicationProvenanceSettings.Default;
            RestoreHook = builder.RestoreHookValue;
            AdapterFrame = builder.AdapterFrameFactoryValue;
        }

        /// <summary>Application name; also the world's diagnostic name (the host appends the session id).</summary>
        public string Name { get; }

        /// <summary>The generated catalog every plugin declaration resolves against (P-009).</summary>
        public ICatalog Catalog { get; }

        /// <summary>
        /// The fingerprint the catalog was published with. The root refuses to boot when the built catalog disagrees
        /// (a corrupted or substituted catalog), and the world is created with exactly this value (SADR-010).
        /// </summary>
        public ContentHash CatalogHash { get; }

        public IReadOnlyList<CatalogPluginDeclaration> Plugins { get; }

        public WorldDefinitionId WorldDefinition { get; }

        public TemporalModel TemporalModel { get; }

        public FixedStepSettings? FixedStep { get; }

        /// <summary>The world's propagation mode at creation (O-03).</summary>
        public PropagationMode Propagation { get; }

        /// <summary>The lane's root scope (P-010).</summary>
        public ScopeId RootScope { get; }

        /// <summary>Scopes the lane starts with besides the root (<see cref="CompositionLaneSeed.WithScopes"/>).</summary>
        public IReadOnlyList<ScopeRecord> ScopeSeeds { get; }

        /// <summary>Generated-style system registrations; their stages come from the compiled schedule (04 s3).</summary>
        public IReadOnlyList<SystemRegistration> Systems { get; }

        public IScheduleDispatchKindResolver DispatchKinds { get; }

        /// <summary>Migration keys the slot-policy validator accepts (P-032).</summary>
        public ISlotMigrationRegistry SlotPolicyMigrations { get; }

        /// <summary>Migration executors the publisher's plans run on scratch (P-029, P-032).</summary>
        public IReadOnlyList<ISlotMigration> Migrations { get; }

        public MessagePlaneRegistration? Messages { get; }

        public CommandPayloadReaders? MessageReaders { get; }

        /// <summary>Optional initial ECS state installer of the registration (04 s3).</summary>
        public Action<global::Unity.Entities.World>? SeedWorldState { get; }

        public SpawnRecipeCatalog Recipes { get; }

        public IDerivationValueSource Values { get; }

        public IReadOnlyList<DerivationRuleKeys> RuleKeys { get; }

        public IReadOnlyList<ProviderSelectionOverride> Overrides { get; }

        /// <summary>SADR-013: which configuration field supplies which rule's payload.</summary>
        public IReadOnlyList<RuleConfigBinding> ConfigBindings { get; }

        public PlanBudget Budget { get; }

        public ulong StagedByteCeiling { get; }

        public int TargetCapacity { get; }

        /// <summary>Issuer identity of every operation the root mints (P-050).</summary>
        public Id128 Issuer { get; }

        /// <summary>The ordered boot script: target seeds and composition edits.</summary>
        public IReadOnlyList<GameApplicationBootStep> BootSteps { get; }

        public GameApplicationProvenanceSettings Provenance { get; }

        /// <summary>Restore seam for a later packet; null when the game declares none yet.</summary>
        public IGameApplicationRestoreHook? RestoreHook { get; }

        /// <summary>
        /// Optional factory of the game's own adapter frame (input and presentation, 04 s7). The root wraps it in its
        /// pump counter; a game with no adapters still gets the counter.
        /// </summary>
        public Func<GameApplicationRoot, GameCore.Unity.Adapters.IAdapterFrame?>? AdapterFrame { get; }

        private static IReadOnlyList<T> Freeze<T>(List<T> list) => list.Count == 0 ? Array.Empty<T>() : list.ToArray();

        /// <summary>Mutable builder; <see cref="Build"/> freezes a copy, so a builder can be reused for variants.</summary>
        public sealed class Builder
        {
            internal readonly List<CatalogPluginDeclaration> PluginList = new List<CatalogPluginDeclaration>();
            internal readonly List<ScopeRecord> ScopeSeedList = new List<ScopeRecord>();
            internal readonly List<SystemRegistration> SystemList = new List<SystemRegistration>();
            internal readonly List<ISlotMigration> MigrationList = new List<ISlotMigration>();
            internal readonly List<DerivationRuleKeys> RuleKeyList = new List<DerivationRuleKeys>();
            internal readonly List<ProviderSelectionOverride> OverrideList = new List<ProviderSelectionOverride>();
            internal readonly List<RuleConfigBinding> ConfigBindingList = new List<RuleConfigBinding>();
            internal readonly List<GameApplicationBootStep> BootStepList = new List<GameApplicationBootStep>();

            public Builder(string name)
            {
                ApplicationName = string.IsNullOrEmpty(name) ? "GameCoreApplication" : name;
            }

            internal string ApplicationName { get; }

            internal ICatalog? CatalogValue { get; private set; }

            internal ContentHash CatalogHashValue { get; private set; } = ContentHash.Empty;

            internal WorldDefinitionId WorldDefinitionValue { get; private set; }

            internal TemporalModel TemporalModelValue { get; private set; } = TemporalModel.CommandDriven;

            internal FixedStepSettings? FixedStepValue { get; private set; }

            internal PropagationMode PropagationValue { get; private set; } = PropagationMode.Automatic;

            internal ScopeId RootScopeValue { get; private set; }

            internal IScheduleDispatchKindResolver? DispatchKindsValue { get; private set; }

            internal ISlotMigrationRegistry? SlotPolicyMigrationsValue { get; private set; }

            internal MessagePlaneRegistration? MessagesValue { get; private set; }

            internal CommandPayloadReaders? MessageReadersValue { get; private set; }

            internal Action<global::Unity.Entities.World>? SeedWorldStateValue { get; private set; }

            internal SpawnRecipeCatalog? RecipesValue { get; private set; }

            internal IDerivationValueSource? ValuesValue { get; private set; }

            internal PlanBudget? BudgetValue { get; private set; }

            internal ulong StagedByteCeilingValue { get; private set; } = 1024UL * 1024UL;

            internal int TargetCapacityValue { get; private set; } = 64;

            internal Id128 IssuerValue { get; private set; }

            internal GameApplicationProvenanceSettings ProvenanceValue { get; private set; }

            internal IGameApplicationRestoreHook? RestoreHookValue { get; private set; }

            internal Func<GameApplicationRoot, GameCore.Unity.Adapters.IAdapterFrame?>? AdapterFrameFactoryValue { get; private set; }

            /// <summary>The catalog and the fingerprint it was published with (SADR-010).</summary>
            public Builder WithCatalog(ICatalog catalog, ContentHash declaredFingerprint)
            {
                CatalogValue = catalog ?? throw new ArgumentNullException(nameof(catalog));
                CatalogHashValue = declaredFingerprint;
                return this;
            }

            public Builder AddPlugin(CatalogPluginDeclaration declaration)
            {
                PluginList.Add(declaration);
                return this;
            }

            public Builder WithWorld(WorldDefinitionId definition, TemporalModel temporalModel, FixedStepSettings? fixedStep = null)
            {
                WorldDefinitionValue = definition;
                TemporalModelValue = temporalModel;
                FixedStepValue = fixedStep;
                return this;
            }

            public Builder WithPropagation(PropagationMode mode)
            {
                PropagationValue = mode;
                return this;
            }

            public Builder WithRootScope(ScopeId rootScope)
            {
                RootScopeValue = rootScope;
                return this;
            }

            public Builder AddScopeSeed(ScopeRecord scope)
            {
                ScopeSeedList.Add(scope ?? throw new ArgumentNullException(nameof(scope)));
                return this;
            }

            public Builder AddSystem(SystemRegistration system)
            {
                SystemList.Add(system ?? throw new ArgumentNullException(nameof(system)));
                return this;
            }

            public Builder WithDispatchKinds(IScheduleDispatchKindResolver kinds)
            {
                DispatchKindsValue = kinds ?? throw new ArgumentNullException(nameof(kinds));
                return this;
            }

            public Builder WithSlotPolicyMigrations(ISlotMigrationRegistry migrations)
            {
                SlotPolicyMigrationsValue = migrations ?? throw new ArgumentNullException(nameof(migrations));
                return this;
            }

            public Builder AddMigration(ISlotMigration migration)
            {
                MigrationList.Add(migration ?? throw new ArgumentNullException(nameof(migration)));
                return this;
            }

            public Builder WithMessages(MessagePlaneRegistration messages, CommandPayloadReaders? readers)
            {
                MessagesValue = messages ?? throw new ArgumentNullException(nameof(messages));
                MessageReadersValue = readers;
                return this;
            }

            public Builder WithSeedWorldState(Action<global::Unity.Entities.World> seed)
            {
                SeedWorldStateValue = seed ?? throw new ArgumentNullException(nameof(seed));
                return this;
            }

            public Builder WithRecipes(SpawnRecipeCatalog recipes)
            {
                RecipesValue = recipes ?? throw new ArgumentNullException(nameof(recipes));
                return this;
            }

            public Builder WithValues(IDerivationValueSource values)
            {
                ValuesValue = values ?? throw new ArgumentNullException(nameof(values));
                return this;
            }

            public Builder AddRuleKeys(DerivationRuleKeys keys)
            {
                RuleKeyList.Add(keys ?? throw new ArgumentNullException(nameof(keys)));
                return this;
            }

            public Builder AddOverride(ProviderSelectionOverride selection)
            {
                OverrideList.Add(selection);
                return this;
            }

            /// <summary>SADR-013: the payload of <paramref name="rule"/> is the value of configuration field <paramref name="field"/>.</summary>
            public Builder BindRuleToConfig(RuleId rule, Id128 field)
            {
                ConfigBindingList.Add(new RuleConfigBinding(rule, field));
                return this;
            }

            public Builder WithBudget(PlanBudget budget, ulong stagedByteCeiling)
            {
                BudgetValue = budget ?? throw new ArgumentNullException(nameof(budget));
                StagedByteCeilingValue = stagedByteCeiling;
                return this;
            }

            public Builder WithTargetCapacity(int capacity)
            {
                if (capacity <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(capacity));
                }

                TargetCapacityValue = capacity;
                return this;
            }

            public Builder WithIssuer(Id128 issuer)
            {
                IssuerValue = issuer;
                return this;
            }

            public Builder AddBootStep(GameApplicationBootStep step)
            {
                BootStepList.Add(step ?? throw new ArgumentNullException(nameof(step)));
                return this;
            }

            public Builder WithProvenance(GameApplicationProvenanceSettings settings)
            {
                ProvenanceValue = settings;
                return this;
            }

            public Builder WithRestoreHook(IGameApplicationRestoreHook hook)
            {
                RestoreHookValue = hook ?? throw new ArgumentNullException(nameof(hook));
                return this;
            }

            public Builder WithAdapterFrame(Func<GameApplicationRoot, GameCore.Unity.Adapters.IAdapterFrame?> factory)
            {
                AdapterFrameFactoryValue = factory ?? throw new ArgumentNullException(nameof(factory));
                return this;
            }

            /// <summary>Freezes the definition. Structural defects are reported at boot, not here.</summary>
            public GameApplicationDefinition Build() => new GameApplicationDefinition(this);
        }
    }
}
