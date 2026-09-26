// GameCore.Rules.Traversal — stable-name-keyed identities of the real-time traversal challenge (GC-020).
//
// Every identity is derived with the production rule, never with a literal: SHA-256 over the UTF-8 stable name,
// first 16 digest bytes read as two big-endian 64-bit words (P-004, 05 s3). That is exactly what
// `GameCore.Rules.Cards.CardIdentity` and `GameCore.Derivation.Fixtures.FixtureIds` do, so a fixture-derived
// identity, a traversal-package identity and a generated catalog literal agree for the same stable name.
#nullable enable
using GameCore.Contracts;

namespace GameCore.Rules.Traversal
{
    /// <summary>Typed identities of the traversal challenge's stable names (P-004, 05 s3).</summary>
    public static class TraversalIdentity
    {
        /// <summary>The documented derivation of one stable name (P-004).</summary>
        public static Id128 Id(string stableName) => StableNameKeyDerivation.Derive(stableName);

        /// <summary>A scope identity; the course tree is 07 s4.1.</summary>
        public static ScopeId Scope(string stableName) => new ScopeId(Id(stableName));

        /// <summary>A target identity; runners and checkpoints are targets.</summary>
        public static TargetId Target(string stableName) => new TargetId(Id(stableName));

        /// <summary>A capability identity; `traversal.acceleration` is a capability.</summary>
        public static CapabilityId Capability(string stableName) => new CapabilityId(Id(stableName));

        /// <summary>A slot identity; a capability's declared outputs are numbered slots from 0.</summary>
        public static SlotId Slot(string stableName) => new SlotId(Id(stableName));

        /// <summary>A logical state-owner identity, as declared by a state slot.</summary>
        public static OwnerId Owner(string stableName) => new OwnerId(Id(stableName));

        /// <summary>A derivation-rule identity; a modifier's rule name is `&lt;install&gt;&lt;suffix&gt;`.</summary>
        public static RuleId Rule(string stableName) => new RuleId(Id(stableName));

        /// <summary>An execution-stage identity of the traversal stage graph (07 s4.2).</summary>
        public static StageId Stage(string stableName) => new StageId(Id(stableName));

        /// <summary>A plugin-type identity.</summary>
        public static PluginTypeId PluginType(string stableName) => new PluginTypeId(Id(stableName));

        /// <summary>A plugin-instance identity of one mounted traversal plugin.</summary>
        public static PluginInstanceId Instance(string stableName) => new PluginInstanceId(Id(stableName));

        /// <summary>A provider-installation identity; derived from the installation's instance identity (05 s2).</summary>
        public static ProviderInstallationId Provider(string stableName) =>
            new ProviderInstallationId(Instance(stableName).Value);

        /// <summary>A buffer-declaration identity; the sealed observation buffer is 07 s4.2.</summary>
        public static BufferId Buffer(string stableName) => new BufferId(Id(stableName));

        /// <summary>A command-route identity of an admitted movement input (05 CommandEnvelope).</summary>
        public static RouteId Route(string stableName) => new RouteId(Id(stableName));

        /// <summary>A definition identity of one reusable recipe descriptor.</summary>
        public static DefinitionId Definition(string stableName) => new DefinitionId(Id(stableName));

        /// <summary>A schema identity of one payload schema carried by a slot or a lane.</summary>
        public static SchemaId SchemaId(string stableName) => new SchemaId(Id(stableName));

        /// <summary>A schema identity plus its integer version (P-006).</summary>
        public static SchemaRef SchemaRef(string name, uint version = 1U) =>
            new SchemaRef(SchemaId(name), version);

        /// <summary>A capability identity plus its version (P-004, P-015).</summary>
        public static CapabilityRef CapabilityRef(string name, uint version = 1U) =>
            new CapabilityRef(Capability(name), version);

        /// <summary>A service-contract identity plus its version (P-011).</summary>
        public static ContractRef Contract(string name, uint version = 1U) =>
            new ContractRef(Id(name), version);

        /// <summary>A generated registration/factory key plus its version (05 s3, P-009).</summary>
        public static FactoryKey Key(string name, uint version = 1U) => new FactoryKey(Id(name), version);

        /// <summary>A recipe descriptor lookup: the recipe's definition under its selector schema (05 s2).</summary>
        public static DefinitionRef Recipe(string definitionName, string schemaName, uint schemaVersion = 1U) =>
            new DefinitionRef(
                Definition(definitionName),
                SchemaRef(schemaName, schemaVersion),
                DefinitionRevision.First);
    }
}
