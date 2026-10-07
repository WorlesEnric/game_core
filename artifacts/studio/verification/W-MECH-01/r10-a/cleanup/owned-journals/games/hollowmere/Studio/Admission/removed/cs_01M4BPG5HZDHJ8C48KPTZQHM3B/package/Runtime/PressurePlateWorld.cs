#nullable enable
// Hollowmere.Mechanism.PressurePlate - the plate surface of one booted world (W-MECH-01 sample).
//
// Place makes a target a plate: a target that is not live yet is spawned at the composition boundary exactly as the
// gameplay EntitySpawner spawns (one child scope under the requested scope, then the spawn as that publication's
// assembly, P-024) from the plate recipe; a target that is already live (seeded by a boot step) is adopted. Either way
// its plate.pressed / plate.weight slots are seeded to 0 and the module learns its threshold and maximum weight.
// Press submits plate.press through the host's command ingress with this surface's own issuer (P-050).
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.World;
using GameCore.Unity.App;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;
using Hollowmere.Mechanism.PressurePlate.Rules;
using Unity.Entities;

namespace Hollowmere.Mechanism.PressurePlate
{
    /// <summary>Places, presses and reads the pressure plates of one booted world.</summary>
    public sealed class PressurePlateWorld
    {
        private readonly List<PlateEvent> events = new List<PlateEvent>();
        private readonly WorldSlotReader slots;
        private EventCursor cursor;
        private ulong sequence;

        internal PressurePlateWorld(GameApplicationRoot root, PlateModule module)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            Module = module ?? throw new ArgumentNullException(nameof(module));
            slots = new WorldSlotReader(root.Host.EntityWorld, root.Registry);
            cursor = new EventCursor(root.World, EventSequence.Zero);
            Issuer = PlateIds.Id("issuer");
        }

        public GameApplicationRoot Root { get; }

        public PlateModule Module { get; }

        /// <summary>Issuer of every plate.press this surface submits.</summary>
        public Id128 Issuer { get; }

        /// <summary>Why the most recent <see cref="Place"/> was refused; empty after a success.</summary>
        public string LastDetail { get; private set; } = string.Empty;

        /// <summary>Every plate event read so far by <see cref="Poll"/>, in commit order.</summary>
        public IReadOnlyList<PlateEvent> Events => events;

        /// <summary>
        /// Makes <paramref name="plate"/> a plate in <paramref name="scope"/> with the given definition values. False (see
        /// <see cref="LastDetail"/>) when the values are invalid, the target is already a plate, or the spawn is refused.
        /// </summary>
        public bool Place(TargetId plate, ScopeId scope, int threshold, int maxWeight)
        {
            if (plate.Value.IsDefault)
            {
                return Refuse(PlateRefusals.Unknown + ": a plate needs a real target id");
            }

            if (!new PlateSpec(threshold, maxWeight).IsValid)
            {
                return Refuse(PlateRefusals.InvalidDefinition + ": threshold " + threshold + " / max weight " + maxWeight
                    + " (threshold >= 1, max weight >= threshold)");
            }

            if (Module.TryGet(plate, out PlateRecord? _))
            {
                return Refuse("plate " + plate + " is already placed");
            }

            ScopeId placedIn = scope;
            if (!Root.Registry.TryResolveTarget(plate, out TargetHandle _, out Entity _))
            {
                if (!TrySpawn(plate, scope, out placedIn, out string refused))
                {
                    return Refuse(refused);
                }
            }

            if (!Seed(plate, PressurePlateDeclarations.PressedSlot, 0, out string pressedDetail)
                || !Seed(plate, PressurePlateDeclarations.WeightSlot, 0, out pressedDetail))
            {
                return Refuse(pressedDetail);
            }

            Module.Add(new PlateRecord(plate, placedIn, threshold, maxWeight));
            LastDetail = string.Empty;
            return true;
        }

        /// <summary>Submits plate.press: <paramref name="actor"/> steps on (<paramref name="load"/>) or off the plate.</summary>
        public CommandAdmissionReceipt Press(TargetId plate, TargetId actor, bool load)
        {
            sequence++;
            var envelope = new CommandEnvelope(
                new OperationId(Root.World, Issuer, sequence),
                PressurePlateDeclarations.PressRoute,
                plate,
                PressurePlateDeclarations.PressCommand,
                null,
                PlatePressCommand.Encode(actor, load));
            return Root.Host.Submit(envelope);
        }

        /// <summary>True while the plate's committed plate.pressed slot is 1.</summary>
        public bool IsPressed(TargetId plate) =>
            slots.ReadOrDefault(plate, PressurePlateDeclarations.Owner, PressurePlateDeclarations.PressedSlot, 0) == 1;

        /// <summary>The plate's committed plate.weight slot, or -1 when the target holds no plate slots.</summary>
        public int Weight(TargetId plate) =>
            slots.ReadOrDefault(plate, PressurePlateDeclarations.Owner, PressurePlateDeclarations.WeightSlot, -1);

        /// <summary>The plate's committed plate.pressed slot, or -1 when the target holds no plate slots.</summary>
        public int Pressed(TargetId plate) =>
            slots.ReadOrDefault(plate, PressurePlateDeclarations.Owner, PressurePlateDeclarations.PressedSlot, -1);

        /// <summary>Reads the committed events published since the last call and keeps the plate events; returns how many.</summary>
        public int Poll(int maxEvents = 256)
        {
            WorldMessagePlane? plane = Root.Host.Messages;
            if (plane == null)
            {
                return 0;
            }

            CommittedEventPage page = plane.ReadEvents(cursor, maxEvents);
            cursor = page.NextCursor;
            int added = 0;
            for (int i = 0; i < page.Events.Count; i++)
            {
                if (PlateEvent.TryDecode(page.Events[i], out PlateEvent decoded))
                {
                    events.Add(decoded);
                    added++;
                }
            }

            return added;
        }

        /// <summary>Plate events of <paramref name="kind"/> read so far, optionally for one plate.</summary>
        public int CountEvents(PlateEventKind kind, TargetId plate = default(TargetId))
        {
            int count = 0;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Kind == kind && (plate.Value.IsDefault || events[i].Plate.Equals(plate)))
                {
                    count++;
                }
            }

            return count;
        }

        private bool TrySpawn(TargetId plate, ScopeId parent, out ScopeId scope, out string detail)
        {
            scope = new ScopeId(PlateIds.Id("scope." + PlateIds.Hex(plate.Value)));
            CompositionHost lane = Root.Lane;
            EditAdmission admission = lane.SubmitEdit(WorldBuilder.ScopeCreate(scope, parent), Root.NextOperation(), lane.Committed.Revision);
            if (!admission.Staged)
            {
                detail = "the plate scope was refused: " + admission.Kind + "/" + admission.Code;
                return false;
            }

            IReadOnlyList<PublishedOperation> published = lane.Drain();
            if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
            {
                detail = "the plate scope publication was refused";
                return false;
            }

            DerivedAssemblyReport report = Root.Pipeline.PublishSpawn(Root.NextOperation(), plate, PressurePlateDeclarations.PlateRecipe, scope);
            if (report.Outcome != DerivedAssemblyOutcome.Published)
            {
                detail = "the plate spawn was refused: " + report.Describe();
                return false;
            }

            if (!Root.Targets.TryRegister(plate, scope, PressurePlateDeclarations.PlateRecipe, out DiagnosticCode code, out string registered))
            {
                detail = "the plate target could not be indexed: " + code + ": " + registered;
                return false;
            }

            detail = string.Empty;
            return true;
        }

        private bool Seed(TargetId plate, SlotId slot, int value, out string detail)
        {
            if (Root.Seeder.TrySeedSlot(
                    plate, PressurePlateDeclarations.Owner, slot, PressurePlateDeclarations.SlotSchemaVersion, value,
                    out DiagnosticCode code, out string seeded))
            {
                detail = string.Empty;
                return true;
            }

            detail = "seeding plate " + plate + " failed: " + code + ": " + seeded;
            return false;
        }

        private bool Refuse(string detail)
        {
            LastDetail = detail;
            return false;
        }
    }
}
