#nullable enable
using System;
using System.Globalization;
using System.Text;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.World;
using GameCore.Unity.App;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;
using GameCore.Unity.Runtime.Time;
using Hollowmere.Mechanism.Lever.Generated;

namespace Hollowmere.Mechanism.Lever
{
    /// <summary>Sandbox-only entry. Live admission must never call this or boot a second game.</summary>
    public static class LeverSmoke
    {
        public const int Steps = 120;

        public static LeverSmokeSession Begin()
        {
            CatalogBuildResult built = LeverCatalog.BuildCatalog();
            ICatalog? catalog = built.Catalog;
            if (catalog == null || catalog.Fingerprint.ToHex() != LeverCatalog.CatalogFingerprint)
            {
                throw new InvalidOperationException("lever generated catalog is invalid or stale");
            }

            var extension = new LeverWorldExtension();
            var readers = new CommandPayloadReaders();
            extension.BindReaders(readers);
            var scope = new ScopeId(LeverIds.Id("smoke.root"));
            CatalogPluginDeclaration declaration = extension.Plugins[0].Declaration;
            var definition = new GameApplicationDefinition.Builder("HollowmereLeverSmoke")
                .WithCatalog(catalog, catalog.Fingerprint)
                .AddPlugin(declaration)
                .WithWorld(new WorldDefinitionId(LeverIds.Id("smoke.world")), TemporalModel.CommandDriven)
                .WithPropagation(PropagationMode.Automatic)
                .WithRootScope(scope)
                .AddSystem(LeverDeclarations.CommandSystemRegistration())
                .WithDispatchKinds(new ScheduleDispatchKindTable().Add(LeverDeclarations.CommandSystem, SystemDispatchKind.ManagedSystem))
                .WithMessages(new MessagePlaneRegistration(extension.Routes, extension.Lanes, null,
                    maxPendingRequests: 64, maxRetainedResults: 64, maxRetainedEvents: 128, maxEventsPerStep: 32, nextStepCapacity: 4), readers)
                .WithRecipes(new SpawnRecipeCatalog(extension.Recipes()))
                .WithValues(new GameplayValueSource())
                .WithTargetCapacity(8)
                .WithIssuer(LeverIds.Id("smoke.issuer"))
                .AddBootStep(GameApplicationBootStep.Seed("seed-lever", LeverDeclarations.Target, scope, LeverDeclarations.Recipe))
                .AddBootStep(GameApplicationBootStep.Apply("mount-lever", WorldBuilder.Mount(declaration, LeverDeclarations.Instance, scope)))
                .Build();
            GameApplicationRoot root = GameApplication.Boot(definition,
                new GameApplicationBootOptions { InstallPlayerLoop = true, StartImmediately = false });
            try
            {
                extension.AttachRoot(root);
                if (!extension.InitializeNewTarget())
                    throw new InvalidOperationException("The sandbox lever target could not initialize its state");
                root.Start();
                return new LeverSmokeSession(root, extension);
            }
            catch (Exception)
            {
                root.Stop("lever sandbox failed to begin");
                throw;
            }
        }
    }

    public sealed class LeverSmokeSession : IDisposable
    {
        private readonly LeverWorldExtension lever;
        internal LeverSmokeSession(GameApplicationRoot root, LeverWorldExtension lever)
        {
            Root = root;
            this.lever = lever;
        }

        public GameApplicationRoot Root { get; }

        // Only enqueue: the ordinary installed PlayerLoop owns every pump.
        public void Step(int frame)
        {
            if ((frame == 10 || frame == 40 || frame == 80) && !lever.Toggle())
            {
                throw new InvalidOperationException("sandbox lever toggle was not admitted");
            }

            if ((frame == 20 || frame == 90) && lever.State != 1)
            {
                throw new InvalidOperationException("sandbox lever did not latch on: state=" + lever.State
                    + ", committed=" + lever.Module?.Committed + ", refusal=" + lever.Module?.LastRefusal
                    + ", step=" + Root.Host.CurrentStep.Value + ", root=" + Root.State);
            }
            if (frame == 20 && (!lever.InitializeNewTarget() || lever.State != 1))
                throw new InvalidOperationException("initialization overwrote the committed on-state");

            if (frame == 60 && lever.State != 0)
            {
                throw new InvalidOperationException("sandbox lever did not latch off");
            }
        }

        public string SlotHash()
        {
            if (lever.State != 1 || lever.Module == null || lever.Module.Committed != 3)
            {
                throw new InvalidOperationException("sandbox lever did not commit exactly three toggles");
            }

            string row = Id128Codec.ToHex(LeverDeclarations.Target.Value) + "|"
                + Id128Codec.ToHex(LeverDeclarations.Owner.Value) + "|"
                + Id128Codec.ToHex(LeverDeclarations.StateSlot.Value) + "|"
                + lever.State.ToString(CultureInfo.InvariantCulture);
            return ContentHash.Compute(Encoding.UTF8.GetBytes(row)).ToHex();
        }

        public void Dispose()
        {
            if (Root.State != GameApplicationState.Stopped) { Root.Stop("lever sandbox disposed"); }
        }
    }
}
