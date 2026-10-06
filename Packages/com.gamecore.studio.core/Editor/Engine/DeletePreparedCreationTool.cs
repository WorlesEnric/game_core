#nullable enable
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Edit
{
    /// <summary>Idempotent inverse of a prepared creation: process loss may precede construction.
    /// The reference is an argument so an absent, never-created target is not a stale apply target.</summary>
    internal sealed class DeletePreparedCreationTool : BuiltInTool
    {
        public const string Id = "history.deleteCreated";
        public DeletePreparedCreationTool() : base(Entry_(Id, ToolTier.Compose, RuntimeApply.Rebuild, false,
            "Internal prepared-creation rollback.", null,
            Arg("created", ValueTypes.Ref, true, "The prepared authoring identity."),
            Arg("component", ValueTypes.Bool, false, "Remove only the created component."))) { }
        public override bool Internal => true;
        public override ToolStageResult Stage(EditContext context) => new ToolStageResult();
        public override OperationResult Apply(EditContext context)
        {
            AuthoringRef reference = StudioJson.Deserialize<AuthoringRef>(context.Arg("created")!.ToString());
            UnityEngine.Object? target = context.Resolver.Find(reference);
            if (target == null) return OperationResult.Applied();
            Operation operation = ToolSupport.InverseOp(context.BoolArg("component") ? "removeComponent" : "delete", reference, null);
            var deletion = new EditContext(context.Runtime, context.ChangeSet, operation, target, false, null);
            return context.BoolArg("component") ? new RemoveComponentTool().Apply(deletion) : new DeleteTool().Apply(deletion);
        }

        public static Operation Inverse(AuthoringRef reference, bool component = false) =>
            ToolSupport.InverseOp(Id, null, new JObject { ["created"] = StudioJson.ToToken(reference), ["component"] = component });
    }
}
