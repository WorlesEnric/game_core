// GameCore.Studio fixtures - a mirror of the authoring attributes, as a runtime gameplay package declares them (the
// Model attributes are Editor-only, so runtime MonoBehaviours cannot carry them). Studio binds these by attribute type
// name and property names (AuthoringMetadata); the enum types differ in name on purpose and are converted by member
// name. The interface is matched by name as well.
#nullable enable
using System;

namespace GameCore.Studio.Fixtures
{
    /// <summary>Mirror of the authored-object interface (matched by name).</summary>
    public interface IAuthoredObject
    {
        string AuthoringId { get; }
    }

    [Flags]
    public enum FixtureScope
    {
        Instance = 1,
        Prefab = 2,
        Definition = 4,
        Scope = 8,
    }

    public enum FixtureRuntimeApply
    {
        Live,
        Rebuild,
        Compile,
        Build,
    }

    public enum FixtureToolTier
    {
        Configure,
        Compose,
        Mechanism,
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AuthorableAttribute : Attribute
    {
        public AuthorableAttribute(string typeId)
        {
            ObjectTypeId = typeId;
        }

        public string ObjectTypeId { get; }

        public string? DisplayName { get; set; }

        public FixtureScope Scope { get; set; }

        public FixtureRuntimeApply RuntimeApplicability { get; set; }

        public string? Doc { get; set; }
    }

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

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class AuthorRefAttribute : Attribute
    {
        public string? Category { get; set; }

        public bool Required { get; set; } = true;

        public string? Doc { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class AuthorOperationAttribute : Attribute
    {
        public AuthorOperationAttribute(string toolId)
        {
            ToolId = toolId;
        }

        public string ToolId { get; }

        public string? Doc { get; set; }

        public FixtureToolTier Tier { get; set; }

        public FixtureRuntimeApply RuntimeApplicability { get; set; }
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class AuthorArgAttribute : Attribute
    {
        public string? Name { get; set; }

        public string? Doc { get; set; }

        public bool Required { get; set; } = true;
    }
}
