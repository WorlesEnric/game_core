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
    }

    /// <summary>A reference field of an authorable type.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class AuthorRefAttribute : Attribute
    {
        public string? Category { get; set; }

        public bool Required { get; set; } = true;

        public string? Doc { get; set; }
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
}
