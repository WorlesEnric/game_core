// GameCore.Studio.Model - authoring metadata attributes (docs/studio/03-authoring-contracts.md s4).
// Unity-free: plugins put these on their definition types, fields and operation methods; ToolCatalogBuilder reflects
// over them to produce ToolCatalog entries. Numeric constraints use NaN for "unset" because attribute properties
// cannot be nullable.
#nullable enable
using System;

namespace GameCore.Studio.Model
{
    /// <summary>Marks a type as an authorable object type with a stable type id (e.g. <c>npc.definition</c>).</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class AuthorableAttribute : Attribute
    {
        public AuthorableAttribute(string typeId)
        {
            ObjectTypeId = typeId ?? throw new ArgumentNullException(nameof(typeId));
        }

        /// <summary>The stable object type id (named so because <see cref="Attribute.TypeId"/> is taken).</summary>
        public string ObjectTypeId { get; }

        public string? DisplayName { get; set; }

        /// <summary>Edit scopes the type supports (flags); zero means unrestricted.</summary>
        public AuthorScope Scope { get; set; }

        /// <summary>How a field change of this type reaches a running world.</summary>
        public RuntimeApply RuntimeApplicability { get; set; }

        public string? Doc { get; set; }
    }

    /// <summary>An editable value field of an authorable type.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class AuthorFieldAttribute : Attribute
    {
        /// <summary>Overrides the inferred value type (a <see cref="ValueTypes"/> name, e.g. <c>artifact</c>).</summary>
        public string? Type { get; set; }

        public string? Unit { get; set; }

        public double Min { get; set; } = double.NaN;

        public double Max { get; set; } = double.NaN;

        public double Step { get; set; } = double.NaN;

        public string? Doc { get; set; }

        /// <summary>The field must be set (non-null). Value-type fields are always set.</summary>
        public bool Required { get; set; }

        /// <summary>
        /// The field shapes what the runtime builds (prefab, slot layout, variant set, kind): a change needs a new
        /// recipe revision. Tuning fields (speeds, texts, numbers read at runtime) stay false; the recipe revision is
        /// derived from structural fields only.
        /// </summary>
        public bool Structural { get; set; }
    }

    /// <summary>A reference field of an authorable type (to another authored thing or asset).</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class AuthorRefAttribute : Attribute
    {
        /// <summary>Reference category: an authorable type id or a capability (e.g. <c>dialogue.graph</c>).</summary>
        public string? Category { get; set; }

        /// <summary>Defaults to true: a reference must be assigned unless declared optional.</summary>
        public bool Required { get; set; } = true;

        public string? Doc { get; set; }

        /// <summary>The reference shapes what the runtime builds (see <see cref="AuthorFieldAttribute.Structural"/>).</summary>
        public bool Structural { get; set; }
    }

    /// <summary>Marks a method as a tool. Parameters: the first <c>[Authorable]</c>-typed parameter without
    /// <c>[AuthorArg]</c> is the target; <c>[AuthorArg]</c> parameters are arguments; any other parameter (the edit
    /// context) is supplied by the engine and does not appear in the catalog.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class AuthorOperationAttribute : Attribute
    {
        public AuthorOperationAttribute(string toolId)
        {
            ToolId = toolId ?? throw new ArgumentNullException(nameof(toolId));
        }

        public string ToolId { get; }

        public string? Doc { get; set; }

        /// <summary>A validator type; its <see cref="AuthorValidatorAttribute"/> supplies the id and codes.</summary>
        public Type? Validator { get; set; }

        public ToolTier Tier { get; set; }

        /// <summary>The tool's runtime requirement; the effective value is the maximum of this and the target type's.</summary>
        public RuntimeApply RuntimeApplicability { get; set; }

        /// <summary>Allowed edit scopes (flags); zero means the target type's scopes.</summary>
        public AuthorScope Scope { get; set; }

        /// <summary>Comma-separated types/capabilities some project node must provide (e.g. <c>world.region</c>).</summary>
        public string? Requires { get; set; }

        /// <summary>Comma-separated types/capabilities the target node itself must provide.</summary>
        public string? RequiresOnTarget { get; set; }

        /// <summary>Authoring kinds the target may have; null means any.</summary>
        public AuthoringKind[]? TargetKinds { get; set; }

        /// <summary>
        /// True for a pure tool (an inspection, a simulation, an explanation): it changes nothing, so the registry runs it
        /// directly (<c>ToolRegistry.Invoke</c>) and the engine records neither undo nor dirty state for its target.
        /// </summary>
        public bool ReadOnly { get; set; }
    }

    /// <summary>A tool argument (method parameter).</summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class AuthorArgAttribute : Attribute
    {
        /// <summary>Argument name in the change set; defaults to the parameter name.</summary>
        public string? Name { get; set; }

        /// <summary>Overrides the inferred value type (a <see cref="ValueTypes"/> name, e.g. <c>ref</c> for a
        /// DefinitionRef passed as text, or <c>artifact</c>).</summary>
        public string? Type { get; set; }

        public string? Unit { get; set; }

        public double Min { get; set; } = double.NaN;

        public double Max { get; set; } = double.NaN;

        public double Step { get; set; } = double.NaN;

        public string? Doc { get; set; }

        /// <summary>Reference category for <c>ref</c> arguments.</summary>
        public string? Category { get; set; }

        /// <summary>Defaults to true; an optional C# parameter is never required.</summary>
        public bool Required { get; set; } = true;
    }

    /// <summary>Names a validator and the diagnostic codes it can raise (exported as <see cref="ValidatorRef"/>).</summary>
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
