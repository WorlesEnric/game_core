// GameCore.Studio.Model - closed vocabularies of the Studio authoring contracts (docs/studio/03-authoring-contracts.md).
// Unity-free: BCL + Newtonsoft.Json only. Every enum is written as a JSON string; integers are refused on read.
// Members whose JSON spelling differs from the C# name carry [EnumMember] so the schema emitter and the
// serializer read the same spelling.
#nullable enable
using System;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace GameCore.Studio.Model
{
    /// <summary>What an <see cref="AuthoringRef"/> points at (03 s1).</summary>
    [JsonConverter(typeof(StrictStringEnumConverter))]
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

    /// <summary>
    /// What the user chose to edit (03 s1). A flags set in authoring metadata (allowed scopes); a single value in an
    /// <see cref="AuthoringRef"/>. There is deliberately no zero member: zero means "not specified".
    /// </summary>
    [Flags]
    [JsonConverter(typeof(StrictStringEnumConverter))]
    public enum AuthorScope
    {
        Instance = 1,
        Prefab = 2,
        Definition = 4,
        Scope = 8,
    }

    /// <summary>Viewport mode a selection was captured in (03 s2).</summary>
    [JsonConverter(typeof(StrictStringEnumConverter))]
    public enum SelectionMode
    {
        Edit,
        Play,
    }

    /// <summary>Relationship kinds of the semantic index (03 s3).</summary>
    [JsonConverter(typeof(StrictStringEnumConverter))]
    public enum EdgeKind
    {
        [EnumMember(Value = "references")]
        References,
        [EnumMember(Value = "contains")]
        Contains,
        [EnumMember(Value = "spawns")]
        Spawns,
        [EnumMember(Value = "bindsUi")]
        BindsUi,
        [EnumMember(Value = "triggers")]
        Triggers,
    }

    /// <summary>Tool tier (03 s5): Configure edits existing objects, Compose creates structure, Mechanism stages code.</summary>
    [JsonConverter(typeof(StrictStringEnumConverter))]
    public enum ToolTier
    {
        Configure,
        Compose,
        Mechanism,
    }

    /// <summary>
    /// When a change takes effect at runtime (03 s4/s6, 02 s7). Ordered: a later member is a stronger requirement, so
    /// the maximum over a change set's operations is its <see cref="Requirements.Max"/>.
    /// </summary>
    [JsonConverter(typeof(StrictStringEnumConverter))]
    public enum RuntimeApply
    {
        Live,
        Rebuild,
        Compile,
        Build,
    }

    /// <summary>What a tool prerequisite is evaluated against.</summary>
    [JsonConverter(typeof(StrictStringEnumConverter))]
    public enum PrerequisiteSubject
    {
        /// <summary>Some node of the semantic index has the required type or capability.</summary>
        Project,
        /// <summary>The operation's target node has the required type or capability.</summary>
        Target,
    }

    /// <summary>Where a change set's intent came from (03 s6).</summary>
    [JsonConverter(typeof(StrictStringEnumConverter))]
    public enum IntentOrigin
    {
        [EnumMember(Value = "agent")]
        Agent,
        [EnumMember(Value = "manual")]
        Manual,
        [EnumMember(Value = "voice")]
        Voice,
        [EnumMember(Value = "replay")]
        Replay,
    }

    /// <summary>Operation precondition mode (03 s1/s6). Absent means <see cref="Stamp"/>.</summary>
    [JsonConverter(typeof(StrictStringEnumConverter))]
    public enum Preconditions
    {
        [EnumMember(Value = "stamp")]
        Stamp,
        [EnumMember(Value = "none")]
        None,
    }

    /// <summary>Status of one validation scenario attached to a change set (03 s6).</summary>
    [JsonConverter(typeof(StrictStringEnumConverter))]
    public enum ScenarioStatus
    {
        [EnumMember(Value = "pending")]
        Pending,
        [EnumMember(Value = "pass")]
        Pass,
        [EnumMember(Value = "fail")]
        Fail,
    }

    /// <summary>Change-set lifecycle state (03 s6, 02 s5).</summary>
    [JsonConverter(typeof(StrictStringEnumConverter))]
    public enum ChangeSetState
    {
        Requested,
        Running,
        Candidate,
        Staged,
        Applied,
        Rejected,
        Failed,
        Undone,
        Interrupted,
    }

    /// <summary>Per-operation apply outcome (03 s6).</summary>
    [JsonConverter(typeof(StrictStringEnumConverter))]
    public enum OutcomeStatus
    {
        Applied,
        Skipped,
        Refused,
        Failed,
    }

    /// <summary>Partial-failure policy (03 s6). Absent means <see cref="AllOrNothing"/>.</summary>
    [JsonConverter(typeof(StrictStringEnumConverter))]
    public enum ApplyPolicy
    {
        AllOrNothing,
        BestEffort,
    }
}
