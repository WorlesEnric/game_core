// Sample plugin types annotated with the Studio authoring metadata (03 s4). ToolCatalogBuilder over these types must
// produce Samples/tool-catalog.json exactly. The engine value type is a local stand-in recognised by name.
#nullable enable
using System.Runtime.Serialization;
using GameCore.Studio.Model;

namespace GameCore.Studio.Model.Tests.Plugin
{
    /// <summary>Stand-in for the engine's Vector3; the builder maps it by type name.</summary>
    public struct Vector3
    {
        public float x;
        public float y;
        public float z;
    }

    public sealed class PrefabAsset
    {
    }

    public sealed class EditContext
    {
    }

    public sealed class OperationResult
    {
    }

    public enum NpcMood
    {
        Calm,
        Wary,
        [EnumMember(Value = "hostile")]
        Hostile,
    }

    [Authorable("npc.definition", DisplayName = "NPC", Scope = AuthorScope.Definition | AuthorScope.Instance,
        RuntimeApplicability = RuntimeApply.Live, Doc = "A non-player character definition")]
    public sealed class NpcDefinition
    {
        [AuthorField(Unit = "m/s", Min = 0.5f, Max = 6f, Doc = "Walking speed")]
        public float speed = 1.8f;

        [AuthorRef(Category = "dialogue.graph", Required = false)]
        public DialogueGraph? dialogue;

        [AuthorRef(Category = "prefab.character")]
        public PrefabAsset? prefab;

        [AuthorField(Doc = "Behaviour mode")]
        public NpcMood mood;

        public int notAuthored;
    }

    [Authorable("dialogue.graph", DisplayName = "Dialogue graph", Scope = AuthorScope.Definition)]
    public sealed class DialogueGraph
    {
        [AuthorField(Doc = "Graph title", Required = true)]
        public string? title;
    }

    [Authorable("item.definition", DisplayName = "Item", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild)]
    public sealed class ItemDefinition
    {
        [AuthorField(Min = 0, Step = 1)]
        public int stackLimit = 1;
    }

    [AuthorValidator("npc.patrol", Codes = new[] { DiagnosticCodes.ValidationFailed, DiagnosticCodes.InvalidArgs })]
    public sealed class PatrolValidator
    {
    }

    public static class NpcTools
    {
        [AuthorOperation("npc.setPatrol", Doc = "Replace the patrol route", Validator = typeof(PatrolValidator))]
        public static OperationResult SetPatrol(EditContext ctx, NpcDefinition npc, [AuthorArg(Unit = "m", Doc = "Patrol points")] Vector3[] points)
        {
            return new OperationResult();
        }

        [AuthorOperation("npc.place", Doc = "Place an NPC in a region", Tier = ToolTier.Compose, Requires = "world.region",
            TargetKinds = new[] { AuthoringKind.Entity }, Scope = AuthorScope.Instance)]
        public static OperationResult Place(
            EditContext ctx,
            NpcDefinition npc,
            [AuthorArg(Unit = "m")] Vector3 position,
            [AuthorArg(Unit = "deg", Min = 0, Max = 360)] float yaw = 0f)
        {
            return new OperationResult();
        }
    }

    public static class InventoryTools
    {
        [AuthorOperation("inventory.grantStarting", Doc = "Grant a starting item",
            TargetKinds = new[] { AuthoringKind.Entity, AuthoringKind.Definition })]
        public static OperationResult GrantStarting(
            EditContext ctx,
            NpcDefinition npc,
            [AuthorArg(Type = ValueTypes.Ref, Category = "item.definition", Doc = "Item definition")] string item,
            [AuthorArg(Min = 1, Max = 99)] int count)
        {
            return new OperationResult();
        }
    }

    public static class DialogueTools
    {
        [AuthorOperation("dialogue.addNode", Doc = "Append a line node", Tier = ToolTier.Compose, RequiresOnTarget = "dialogue.graph")]
        public static OperationResult AddNode(
            EditContext ctx,
            DialogueGraph graph,
            [AuthorArg] string text,
            [AuthorArg(Type = ValueTypes.Artifact, Required = false, Doc = "Voice line")] object? voice)
        {
            return new OperationResult();
        }
    }

    /// <summary>Not part of the sample catalog: declares the same tool id twice.</summary>
    public static class DuplicateTools
    {
        [AuthorOperation("dup.tool")]
        public static void First(EditContext ctx)
        {
        }

        [AuthorOperation("dup.tool")]
        public static void Second(EditContext ctx)
        {
        }
    }

    /// <summary>Not part of the sample catalog: a member with both field and reference metadata.</summary>
    [Authorable("broken.both")]
    public sealed class BothAttributes
    {
        [AuthorField]
        [AuthorRef]
        public PrefabAsset? asset;
    }

    /// <summary>Not part of the sample catalog: an unknown value-type override.</summary>
    public static class BadOverride
    {
        [AuthorOperation("bad.override")]
        public static void Run(EditContext ctx, [AuthorArg(Type = "matrix")] float value)
        {
        }
    }

    /// <summary>Not part of the sample catalog: inferred value types.</summary>
    [Authorable("types.probe")]
    public sealed class TypeProbe
    {
        [AuthorField] public bool flag;
        [AuthorField] public long big;
        [AuthorField] public double ratio;
        [AuthorField] public string? label;
        [AuthorField] public Vector3 offset;
        [AuthorField] public int[]? counts;
        [AuthorField] public System.Collections.Generic.List<NpcDefinition>? crowd;
        [AuthorField] public PrefabAsset? opaque;
        [AuthorField(Min = 0.1f)] public float small;
        [AuthorField] public int? maybe;
    }
}
