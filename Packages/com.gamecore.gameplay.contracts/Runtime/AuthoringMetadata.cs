// GameCore.Gameplay.Contracts - authoring metadata attributes and authoring identity interfaces
// (docs/studio/03-authoring-contracts.md s1, s4).
//
// The attribute set mirrors GameCore.Studio.Model's P0.3 attributes member for member (same type names, same property
// names, types and defaults; same enum member names and values). The gameplay runtime packages must not depend on
// com.gamecore.studio.* (P1.1 coordination note), so they annotate their definitions and tools with this mirror, and
// Studio binds them by attribute name and shape. The dotnet test AuthoringMetadataParityTests compares both sets by
// reflection so the mirror cannot drift.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Gameplay.Contracts
{
    /// <summary>Anything Studio can select and address: it carries one persistent authoring id.</summary>
    public interface IAuthoredObject
    {
        /// <summary>The persistent authoring id (lowercase GUID, see <see cref="AuthoringIds"/>); empty until minted.</summary>
        string AuthoringId { get; }
    }

    /// <summary>An authored definition asset: a named, content-stamped object a placed thing refers to.</summary>
    public interface IDefinitionAsset : IAuthoredObject
    {
        /// <summary>Human-readable name (the asset name).</summary>
        string DefinitionName { get; }

        /// <summary>
        /// Content stamp of the definition's authorable fields: lowercase hex SHA-256 over their canonical serialization,
        /// written by the bake. Empty before the first bake.
        /// </summary>
        string ContentStamp { get; }
    }

    /// <summary>What an authoring reference points at (mirror of Studio's AuthoringKind).</summary>
    public enum AuthoringKind
    {
        Entity,
        Definition,
        Region,
        SceneObject,
        Asset,
        UiElement,
        Scope,
        Location,
    }

    /// <summary>Edit scopes (flags; mirror of Studio's AuthorScope). Zero means "not specified".</summary>
    [Flags]
    public enum AuthorScope
    {
        Instance = 1,
        Prefab = 2,
        Definition = 4,
        Scope = 8,
    }

    /// <summary>Tool tier (mirror of Studio's ToolTier).</summary>
    public enum ToolTier
    {
        Configure,
        Compose,
        Mechanism,
    }

    /// <summary>When a change takes effect at runtime (mirror of Studio's RuntimeApply).</summary>
    public enum RuntimeApply
    {
        Live,
        Rebuild,
        Compile,
        Build,
    }

    /// <summary>Marks a type as an authorable object type with a stable type id (e.g. <c>entity.definition</c>).</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class AuthorableAttribute : Attribute
    {
        public AuthorableAttribute(string typeId)
        {
            ObjectTypeId = typeId ?? throw new ArgumentNullException(nameof(typeId));
        }

        public string ObjectTypeId { get; }

        public string? DisplayName { get; set; }

        public AuthorScope Scope { get; set; }

        public RuntimeApply RuntimeApplicability { get; set; }

        public string? Doc { get; set; }
    }

    /// <summary>An editable value field of an authorable type.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class AuthorFieldAttribute : Attribute
    {
        public string? Type { get; set; }

        public string? Unit { get; set; }

        public double Min { get; set; } = double.NaN;

        public double Max { get; set; } = double.NaN;

        public double Step { get; set; } = double.NaN;

        public string? Doc { get; set; }

        public bool Required { get; set; }

        /// <summary>The field shapes what the runtime builds (prefab, slot layout, variant set, kind); the recipe revision is derived from structural fields only.</summary>
        public bool Structural { get; set; }
    }

    /// <summary>A reference field of an authorable type.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class AuthorRefAttribute : Attribute
    {
        public string? Category { get; set; }

        public bool Required { get; set; } = true;

        public string? Doc { get; set; }

        /// <summary>The reference shapes what the runtime builds (see <see cref="AuthorFieldAttribute.Structural"/>).</summary>
        public bool Structural { get; set; }
    }

    /// <summary>Marks a static method as a tool (the first authorable parameter without [AuthorArg] is the target).</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class AuthorOperationAttribute : Attribute
    {
        public AuthorOperationAttribute(string toolId)
        {
            ToolId = toolId ?? throw new ArgumentNullException(nameof(toolId));
        }

        public string ToolId { get; }

        public string? Doc { get; set; }

        public Type? Validator { get; set; }

        public ToolTier Tier { get; set; }

        public RuntimeApply RuntimeApplicability { get; set; }

        public AuthorScope Scope { get; set; }

        public string? Requires { get; set; }

        public string? RequiresOnTarget { get; set; }

        public AuthoringKind[]? TargetKinds { get; set; }
    }

    /// <summary>A tool argument (method parameter).</summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class AuthorArgAttribute : Attribute
    {
        public string? Name { get; set; }

        public string? Type { get; set; }

        public string? Unit { get; set; }

        public double Min { get; set; } = double.NaN;

        public double Max { get; set; } = double.NaN;

        public double Step { get; set; } = double.NaN;

        public string? Doc { get; set; }

        public string? Category { get; set; }

        public bool Required { get; set; } = true;
    }

    /// <summary>Names a validator and the diagnostic codes it can raise.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AuthorValidatorAttribute : Attribute
    {
        public AuthorValidatorAttribute(string id)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
        }

        public string Id { get; }

        public string[]? Codes { get; set; }
    }

    /// <summary>
    /// Reference categories that are not [Authorable] type ids (P1.7b, 05 rows 1-12). Every other [AuthorRef] category is
    /// the target's [Authorable] type id (<c>dialogue.graph</c>, <c>logic.conditionSet</c>, ...).
    /// </summary>
    public static class AuthorRefCategories
    {
        /// <summary>A placed scene entity, referenced by its authoring id string (assets cannot reference scene objects).</summary>
        public const string EntityInstance = "entity.instance";

        /// <summary>A scene asset, referenced by its project path (SceneAsset is Editor-only).</summary>
        public const string AssetScene = "asset.scene";

        /// <summary>An audio clip: an AudioClip asset, or a clip id of the project's audio bank.</summary>
        public const string AudioClip = "audio.clip";

        /// <summary>A music state, referenced by its state id.</summary>
        public const string MusicState = "audio.musicState";
    }

    /// <summary>An authored definition that gates a use, an entry or a travel on a condition (interactable, trigger, portal).</summary>
    public interface IConditionGated
    {
        /// <summary>
        /// The effective condition reference the runtime evaluates: a condition set's authoring id, a fact shorthand
        /// <c>narrative.fact.&lt;name&gt;&lt;op&gt;&lt;value&gt;</c>, a legacy string, or empty (always holds).
        /// </summary>
        string ConditionRef { get; }
    }

    /// <summary>
    /// A legacy string id an authored object still answers to (e.g. a dialogue graph's P1.4 npcGraphRef), so
    /// authoring.migrateRefs can resolve old string references to the object.
    /// </summary>
    public interface IAuthoringAlias
    {
        /// <summary>The alias, or empty.</summary>
        string AuthoringAlias { get; }
    }

    /// <summary>What a condition reference decided over a state (logic.whyNot, interaction.explain).</summary>
    public sealed class ConditionExplanation
    {
        public ConditionExplanation(string conditionRef, string setName, bool known, bool passed, int failedIndex, string failedCondition, IReadOnlyList<string> inputs)
        {
            ConditionRef = conditionRef ?? string.Empty;
            SetName = setName ?? string.Empty;
            Known = known;
            Passed = passed;
            FailedIndex = failedIndex;
            FailedCondition = failedCondition ?? string.Empty;
            Inputs = inputs ?? Array.Empty<string>();
        }

        public string ConditionRef { get; }

        /// <summary>Name of the condition set (or the fact shorthand) that was evaluated.</summary>
        public string SetName { get; }

        /// <summary>False when the reference names nothing the content declares.</summary>
        public bool Known { get; }

        public bool Passed { get; }

        /// <summary>Index of the first failing condition, -1 when none failed.</summary>
        public int FailedIndex { get; }

        /// <summary>The first failing condition with the value it read, e.g. <c>fact gate_open != 0 (read 0)</c>.</summary>
        public string FailedCondition { get; }

        /// <summary>Every input the evaluation read.</summary>
        public IReadOnlyList<string> Inputs { get; }

        public override string ToString() =>
            !Known ? ConditionRef + ": unknown condition (" + FailedCondition + ")"
                : (Passed ? ConditionRef + ": holds" : ConditionRef + ": fails at #" + FailedIndex + " " + FailedCondition);
    }

    /// <summary>
    /// Editor-side seam: evaluates a condition reference over the authored content's state (initial values plus test
    /// terms). The logic package's Editor assembly implements it; packages that cannot depend on logic (interaction,
    /// world) find the implementation through the type cache.
    /// </summary>
    public interface IConditionExplainer
    {
        /// <summary>
        /// Evaluates <paramref name="conditionRef"/>. <paramref name="state"/> uses logic.test's terms
        /// (<c>fact.&lt;name&gt;=v; item.&lt;item&gt;=n; ...</c>); empty means the content's initial state.
        /// </summary>
        ConditionExplanation Explain(string conditionRef, string state);
    }

    /// <summary>What one package's reference migration found and changed.</summary>
    public sealed class AuthoringMigrationReport
    {
        private readonly List<string> changed = new List<string>();
        private readonly List<string> unresolved = new List<string>();

        public AuthoringMigrationReport(string migrationId)
        {
            MigrationId = migrationId ?? string.Empty;
        }

        public string MigrationId { get; }

        /// <summary>One line per migrated reference: <c>asset path: field 'old' -&gt; new</c>.</summary>
        public IReadOnlyList<string> Changed => changed;

        /// <summary>Legacy references that name nothing (left as they are; the validators report them).</summary>
        public IReadOnlyList<string> Unresolved => unresolved;

        public void AddChanged(string line) => changed.Add(line ?? string.Empty);

        public void AddUnresolved(string line) => unresolved.Add(line ?? string.Empty);
    }

    /// <summary>
    /// Editor-side seam of <c>authoring.migrateRefs</c>: one package's migration of legacy string / pseudo-category
    /// references to typed [AuthorRef]s (P1.7b, B1). Implementations live in the packages' Editor assemblies and are
    /// discovered through the type cache; each needs a public parameterless constructor.
    /// </summary>
    public interface IAuthoringRefMigration
    {
        string MigrationId { get; }

        /// <summary>Finds legacy references under <paramref name="folders"/> (empty: the whole project) and, when
        /// <paramref name="apply"/> is true, rewrites them (with Undo, assets marked dirty, not saved).</summary>
        AuthoringMigrationReport Migrate(IReadOnlyList<string> folders, bool apply);
    }

    /// <summary>Diagnostic codes added by the authoring-metadata hardening (P1.7b); stable once released.</summary>
    public static class AuthoringHardeningCodes
    {
        /// <summary>A legacy string or pseudo-category reference is still stored; run authoring.migrateRefs.</summary>
        public const string LegacyReference = "GP-REF-001";

        /// <summary>authoring.migrateRefs could not resolve a legacy reference.</summary>
        public const string UnresolvedReference = "GP-REF-002";

        /// <summary>An [AuthorRef] holds an object of another authorable type than its category.</summary>
        public const string WrongReferenceCategory = "GP-REF-003";

        /// <summary>Both a condition set and a fact condition are set; only the condition set is evaluated.</summary>
        public const string AmbiguousCondition = "GP-REF-004";

        /// <summary>An entity reference is not a canonical authoring id.</summary>
        public const string EntityReferenceInvalid = "GP-REF-005";

        /// <summary>A portal names a spawn point its region does not declare.</summary>
        public const string PortalSpawnPointMissing = "GP-WLD-030";

        /// <summary>A region lists a neighbour that is not in the world or not reachable through a portal.</summary>
        public const string RegionNeighbourUnconnected = "GP-WLD-031";

        /// <summary>A region's bounds have a non-positive size.</summary>
        public const string RegionBoundsInvalid = "GP-WLD-032";

        /// <summary>Two spawn points of a region share a name.</summary>
        public const string RegionSpawnPointDuplicate = "GP-WLD-033";

        /// <summary>The world's start spawn point is not declared by the start region.</summary>
        public const string WorldStartPointMissing = "GP-WLD-034";

        /// <summary>An NPC's appearance is not one of its entity definition's variants.</summary>
        public const string NpcAppearanceNotVariant = "GP-NPC-008";

        /// <summary>An interactable's or trigger's built-in action is not one the runtime knows.</summary>
        public const string InteractableUnknownBuiltInAction = "GP-INT-008";

        /// <summary>A consequence was set on a dialogue node that is not an action node.</summary>
        public const string DialogueNodeNotAction = "GP-DLG-020";

        /// <summary>A quest lists itself (directly or through others) as a prerequisite.</summary>
        public const string QuestPrerequisiteCycle = "GP-QST-020";

        /// <summary>A quest branch number is outside 1..n.</summary>
        public const string QuestBranchOutOfRange = "GP-QST-021";

        /// <summary>A quest is closed because a prerequisite failed (failure closes dependents).</summary>
        public const string QuestClosedByPrerequisite = "GP-QST-022";

        /// <summary>A price is negative, or a vendor price names an item the vendor does not stock.</summary>
        public const string PriceInvalid = "GP-INV-020";

        /// <summary>A buy action names no vendor, no item, or a non-positive count.</summary>
        public const string BuyActionInvalid = "GP-LOG-030";

        /// <summary>A restoreStamina action restores a non-positive amount.</summary>
        public const string RestoreStaminaInvalid = "GP-LOG-031";

        /// <summary>A save schema has no canonical authoring id.</summary>
        public const string SaveSchemaMissingId = "GP-SAV-001";

        /// <summary>A save schema is malformed (names, versions, migration chains, fingerprints).</summary>
        public const string SaveSchemaInvalid = "GP-SAV-002";

        /// <summary>Every code of this table, in declaration order.</summary>
        public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
        {
            LegacyReference, UnresolvedReference, WrongReferenceCategory, AmbiguousCondition, EntityReferenceInvalid,
            PortalSpawnPointMissing, RegionNeighbourUnconnected, RegionBoundsInvalid, RegionSpawnPointDuplicate, WorldStartPointMissing,
            NpcAppearanceNotVariant, InteractableUnknownBuiltInAction, DialogueNodeNotAction,
            QuestPrerequisiteCycle, QuestBranchOutOfRange, QuestClosedByPrerequisite, PriceInvalid,
            BuyActionInvalid, RestoreStaminaInvalid, SaveSchemaMissingId, SaveSchemaInvalid,
        });
    }
}
