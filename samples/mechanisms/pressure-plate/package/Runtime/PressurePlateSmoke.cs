#nullable enable
// Hollowmere.Mechanism.PressurePlate - the mechanism's smoke world (W-MECH-01 sample).
//
// The staging lane's slot harness calls PressurePlateSmoke.Begin() by name, steps the session for a fixed number of
// frames and compares SlotHash() across two sequential sessions. The smoke world is standalone: only the plate plugin,
// only the plate's own generated catalog, one root scope and two plate targets seeded by boot steps. Presses happen at
// fixed frames, so the committed slots after any frame are a pure function of the frames stepped.
//
//   frame  10  A: actor 1 steps on                       A weight 1, pressed   (PlatePressed)
//   frame  20  B: actors 1 and 2 step on                 B weight 2, pressed   (PlateWeightChanged, PlatePressed)
//   frame  40  A: actor 1 steps off, then off again      A weight 0, released  (PlateReleased; plate.not-loaded)
//   frame  60  B: actors 3 and 1 step on                 B weight 3            (PlateWeightChanged; plate.overloaded)
//   frame  80  B: actor 2 steps off                      B weight 2, pressed   (PlateWeightChanged)
//   frame 100  B: actor 3 steps off                      B weight 1, released  (PlateReleased)
//
// Step only submits; the frames themselves are pumped by the PlayerLoop node (Begin()) or by the caller
// (Begin(options) with InstallPlayerLoop = false), so there is exactly one pump path (SADR-010).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
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

namespace Hollowmere.Mechanism.PressurePlate
{
    /// <summary>Entry point of the mechanism's smoke world.</summary>
    public static class PressurePlateSmoke
    {
        /// <summary>Frames the harness steps a session for.</summary>
        public const int Steps = 120;

        public static readonly ScopeId RootScope = new ScopeId(PlateIds.Id("smoke.root-scope"));

        public static readonly TargetId PlateA = new TargetId(PlateIds.Id("smoke.plate.a"));

        public static readonly TargetId PlateB = new TargetId(PlateIds.Id("smoke.plate.b"));

        public static readonly TargetId Actor1 = new TargetId(PlateIds.Id("smoke.actor.1"));

        public static readonly TargetId Actor2 = new TargetId(PlateIds.Id("smoke.actor.2"));

        public static readonly TargetId Actor3 = new TargetId(PlateIds.Id("smoke.actor.3"));

        /// <summary>Boots the smoke world with the PlayerLoop node installed and the world running.</summary>
        public static PressurePlateSmokeSession Begin() =>
            Begin(new GameApplicationBootOptions { InstallPlayerLoop = true, StartImmediately = true });

        /// <summary>Boots the smoke world with explicit options (EditMode tests pump frames themselves).</summary>
        public static PressurePlateSmokeSession Begin(GameApplicationBootOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            GameApplicationRoot root = GameApplication.Boot(Definition(), options);
            try
            {
                PressurePlateWorld plates = PressurePlateMechanism.Attach(root);
                Place(plates, PlateA, 1, 2);
                Place(plates, PlateB, 2, 3);
                if (root.State != GameApplicationState.Running)
                {
                    root.Start();
                }

                return new PressurePlateSmokeSession(root, plates);
            }
            catch (Exception)
            {
                root.Stop("pressure plate smoke failed to begin");
                throw;
            }
        }

        /// <summary>The smoke world's application definition: the plate plugin alone on its own catalog.</summary>
        public static GameApplicationDefinition Definition()
        {
            ICatalog catalog = PressurePlateMechanism.Catalog();
            CatalogPluginDeclaration declaration = PressurePlateMechanism.Declaration();
            var readers = new CommandPayloadReaders();
            PlateReaders.BindInto(readers);
            var plane = new MessagePlaneRegistration(
                PressurePlateDeclarations.Routes(),
                PressurePlateDeclarations.Lanes(),
                null,
                maxPendingRequests: 64,
                maxRetainedResults: 64,
                maxRetainedEvents: 512,
                maxEventsPerStep: 32,
                nextStepCapacity: 4);

            return new GameApplicationDefinition.Builder("HollowmerePressurePlateSmoke")
                .WithCatalog(catalog, catalog.Fingerprint)
                .AddPlugin(declaration)
                .WithWorld(new WorldDefinitionId(PlateIds.Id("smoke.world")), TemporalModel.CommandDriven)
                .WithPropagation(PropagationMode.Automatic)
                .WithRootScope(RootScope)
                .AddSystem(PressurePlateDeclarations.CommandSystemRegistration())
                .WithDispatchKinds(new ScheduleDispatchKindTable()
                    .Add(PressurePlateDeclarations.CommandSystem, SystemDispatchKind.ManagedSystem))
                .WithMessages(plane, readers)
                .WithRecipes(new SpawnRecipeCatalog(new List<SpawnRecipe> { PlateRecipes.Create() }))
                .WithValues(new GameplayValueSource())
                .WithTargetCapacity(16)
                .WithIssuer(PlateIds.Id("smoke.issuer"))
                .AddBootStep(GameApplicationBootStep.Seed("seed-plate:a", PlateA, RootScope, PressurePlateDeclarations.PlateRecipe))
                .AddBootStep(GameApplicationBootStep.Seed("seed-plate:b", PlateB, RootScope, PressurePlateDeclarations.PlateRecipe))
                .AddBootStep(GameApplicationBootStep.Apply(
                    PressurePlateMechanism.MountStepName,
                    WorldBuilder.Mount(declaration, PressurePlateDeclarations.Instance, RootScope)))
                .Build();
        }

        private static void Place(PressurePlateWorld plates, TargetId plate, int threshold, int maxWeight)
        {
            if (!plates.Place(plate, RootScope, threshold, maxWeight))
            {
                throw new InvalidOperationException("the smoke plate " + plate + " could not be placed: " + plates.LastDetail);
            }
        }
    }

    /// <summary>One booted smoke world. Dispose stops it; only one session can be live at a time.</summary>
    public sealed class PressurePlateSmokeSession : IDisposable
    {
        internal PressurePlateSmokeSession(GameApplicationRoot root, PressurePlateWorld plates)
        {
            Root = root;
            Plates = plates;
        }

        public GameApplicationRoot Root { get; }

        /// <summary>The plate surface of the smoke world.</summary>
        public PressurePlateWorld Plates { get; }

        /// <summary>Submits the presses scheduled for <paramref name="frame"/> (see the file header); other frames do nothing.</summary>
        public void Step(int frame)
        {
            if (Root.State == GameApplicationState.Stopped)
            {
                return;
            }

            switch (frame)
            {
                case 10:
                    Plates.Press(PressurePlateSmoke.PlateA, PressurePlateSmoke.Actor1, true);
                    break;
                case 20:
                    Plates.Press(PressurePlateSmoke.PlateB, PressurePlateSmoke.Actor1, true);
                    Plates.Press(PressurePlateSmoke.PlateB, PressurePlateSmoke.Actor2, true);
                    break;
                case 40:
                    Plates.Press(PressurePlateSmoke.PlateA, PressurePlateSmoke.Actor1, false);
                    Plates.Press(PressurePlateSmoke.PlateA, PressurePlateSmoke.Actor1, false);
                    break;
                case 60:
                    Plates.Press(PressurePlateSmoke.PlateB, PressurePlateSmoke.Actor3, true);
                    Plates.Press(PressurePlateSmoke.PlateB, PressurePlateSmoke.Actor1, true);
                    break;
                case 80:
                    Plates.Press(PressurePlateSmoke.PlateB, PressurePlateSmoke.Actor2, false);
                    break;
                case 100:
                    Plates.Press(PressurePlateSmoke.PlateB, PressurePlateSmoke.Actor3, false);
                    break;
            }
        }

        /// <summary>
        /// Lowercase hex SHA-256 of the UTF-8 text of the sorted (ordinal) lines <c>target|owner|slot|value</c>, one per plate
        /// slot of every plate (ids as lowercase hex; value in invariant decimal, or <c>absent</c>), joined by "\n".
        /// </summary>
        public string SlotHash()
        {
            var reader = new GameCore.Gameplay.Entities.WorldSlotReader(Root.Host.EntityWorld, Root.Registry);
            var lines = new List<string>();
            IReadOnlyList<TargetId> plates = Plates.Module.Plates();
            SlotId[] slots = { PressurePlateDeclarations.PressedSlot, PressurePlateDeclarations.WeightSlot };
            for (int i = 0; i < plates.Count; i++)
            {
                for (int s = 0; s < slots.Length; s++)
                {
                    string value = reader.TryRead(plates[i], PressurePlateDeclarations.Owner, slots[s], out int read)
                        ? read.ToString(CultureInfo.InvariantCulture)
                        : "absent";
                    lines.Add(PlateIds.Hex(plates[i].Value) + "|" + PlateIds.Hex(PressurePlateDeclarations.Owner.Value) + "|"
                        + PlateIds.Hex(slots[s].Value) + "|" + value);
                }
            }

            lines.Sort(StringComparer.Ordinal);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
                var hex = new StringBuilder(digest.Length * 2);
                for (int i = 0; i < digest.Length; i++)
                {
                    hex.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
                }

                return hex.ToString();
            }
        }

        public void Dispose()
        {
            if (Root.State != GameApplicationState.Stopped)
            {
                Root.Stop("pressure plate smoke session disposed");
            }
        }
    }
}
