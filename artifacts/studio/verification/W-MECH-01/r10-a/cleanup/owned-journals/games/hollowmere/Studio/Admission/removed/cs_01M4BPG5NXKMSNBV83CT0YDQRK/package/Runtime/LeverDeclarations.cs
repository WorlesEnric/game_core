#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Unity.Runtime;
using Hollowmere.Mechanism.Lever.Generated;

namespace Hollowmere.Mechanism.Lever
{
    public static class LeverIds
    {
        public const string Prefix = "hollowmere.lever.";
        public static Id128 Id(string name) => GameplayIds.Id(Prefix + name);
        public static FactoryKey Key(string name) => new FactoryKey(Id(name), 1U);
        public static SchemaRef Schema(string name) => new SchemaRef(new SchemaId(Id(name)), 1U);
    }

    public static class LeverDeclarations
    {
        public const string PackageName = "com.hollowmere.mechanism.lever";
        public const string PackageVersion = "0.1.0";
        public const int LaneCapacity = 16;
        public const uint SlotSchemaVersion = 1U;

        public static readonly PluginTypeId PluginType = new PluginTypeId(LeverIds.Id("plugin-type"));
        public static readonly PluginInstanceId Instance = new PluginInstanceId(LeverIds.Id("instance"));
        public static readonly FactoryKey PluginFactory = LeverIds.Key("plugin");
        public static readonly SchemaRef ConfigSchema = LeverIds.Schema("schema.config");
        public static readonly SchemaRef Domain = LeverIds.Schema("domain.lever");
        public static readonly FactoryKey Layout = LeverIds.Key("layout.lever");
        public static readonly OwnerId Owner = new OwnerId(LeverIds.Id("owner"));
        public static readonly SlotId StateSlot = new SlotId(LeverIds.Id("slot.lever.state"));
        public static readonly StageId Stage = new StageId(LeverIds.Id("stage.command"));
        public static readonly FactoryKey CommandSystem = LeverIds.Key("system.command");
        public static readonly FactoryKey Applier = LeverIds.Key("applier");
        public static readonly SchemaRef RecipeSchema = LeverIds.Schema("schema.lever-recipe");
        public static readonly DefinitionRef Recipe = new DefinitionRef(
            new DefinitionId(LeverIds.Id("lever-recipe")), RecipeSchema, DefinitionRevision.First);
        public static readonly FactoryKey IngressProducer = LeverIds.Key("ingress");
        public static readonly RouteId ToggleRoute = new RouteId(LeverIds.Id("route.lever.toggle"));
        public static readonly SchemaRef ToggleCommand = LeverIds.Schema("command.lever.toggle");
        public static readonly SchemaRef ToggledEvent = LeverIds.Schema("event.lever-toggled");
        public static readonly BufferId ToggleBuffer = new BufferId(LeverIds.Id("buffer.lever.toggle"));
        public static readonly FactoryKey ToggleOrder = LeverIds.Key("order.lever.toggle");
        public static readonly TargetId Target = new TargetId(LeverIds.Id("session.lever"));

        public static PluginManifest Manifest() => new PluginManifest(
            PluginType, PackageVersion, LeverPackageContent.Value,
            new SupportedProtocolRange(1, 0, 0), null, ConfigSchema, PluginFactory,
            null, null, null, null, null, Slots(), Stages(), Buffers(), null);

        public static IReadOnlyList<StateSlotSpec> Slots() => new[]
        {
            new StateSlotSpec(StateSlot, Owner, Domain, Layout,
                new[] { new FieldOwnership(Domain, LeverIds.Id("field.lever.state")) },
                default(FactoryKey), default(FactoryKey), default(FactoryKey),
                LastSupportPolicy.PreserveDormant, default(FactoryKey), null),
        };

        public static IReadOnlyList<StageSpec> Stages()
        {
            var access = new AccessSet(new[] { new AccessDeclaration(Domain, AccessMode.ReadWrite, default(Id128)) });
            return new[]
            {
                new StageSpec(Stage, 1U, LeverIds.Id("package"), HostAffinity.ManagedMain,
                    null, null, access, null, null, null, null,
                    new[] { new SystemSpec(CommandSystem, SystemMultiplicity.World, access, null, null, null, null) }, null),
            };
        }

        public static IReadOnlyList<BufferSpec> Buffers() => new[]
        {
            new BufferSpec(ToggleBuffer, ToggleCommand, new[] { IngressProducer }, Stage, Stage,
                ToggleOrder, BufferLifetime.Step, LaneCapacity, BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain),
        };

        public static IReadOnlyList<CommandRoute> Routes() => new[]
        {
            new CommandRoute(ToggleRoute, Owner, ToggleCommand, Stage, Stage, ToggleBuffer, IngressProducer, LaneCapacity, false),
        };

        public static IReadOnlyList<MessageBufferDescriptor> Lanes() => new[]
        {
            new MessageBufferDescriptor(ToggleBuffer, ToggleCommand, new[] { IngressProducer }, Owner, Stage, Stage,
                ToggleOrder, BufferLifetime.Step, LaneCapacity, LaneCapacity * sizeof(int),
                BufferOverflowPolicy.RejectBeforeMutation, BufferCancellationPolicy.Drain),
        };

        public static SystemRegistration CommandSystemRegistration() =>
            new ManagedSystemRegistration<LeverCommandSystem>(CommandSystem, Stage, "LeverCommandSystem");
    }
}
