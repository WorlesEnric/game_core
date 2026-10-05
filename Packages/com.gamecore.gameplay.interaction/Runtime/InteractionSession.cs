// GameCore.Gameplay.Interaction - commands, focus candidates, the dispatcher, the binder, trigger volumes and the
// proximity interactor, and the session that installs them (P1.3).
//
// Before the pump (inputs):
//   InteractionDispatcher  reads committed events with its own cursor: InteractRequested on a focused interactable
//                          becomes interact.use{actor = the requesting player, target}; a committed InteractionSucceeded
//                          runs the definition's action ref through IActionRunner and sends a cue through IFeedbackSink;
//                          a committed InteractionRefused sends a refusal cue; a committed first trigger entry runs the
//                          trigger's action ref.
//   ProximityInteractor    judges trigger volumes from committed positions (the focus actor's world.pos against each
//                          trigger's committed placement and size) and submits interact.trigger on enter / exit; whether
//                          the actor is inside is its committed bit in the trigger's interact.occupants mask (P1.7a).
// After the pump (binders):
//   InteractableBinder     gives each interactable view a collider (when it has none) and its per-state look
//                          (doors/gates swing open and stop colliding, switches tint on); headless it touches nothing.
//                          It is residency-aware: interactables whose committed region is not Resident are skipped.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Interaction;
using UnityEngine;

namespace GameCore.Gameplay.Interaction
{
    /// <summary>Submits interaction commands with the world's gameplay issuer.</summary>
    public sealed class InteractionCommands
    {
        private readonly GameplayWorld world;

        public InteractionCommands(GameplayWorld world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
        }

        /// <summary>interact.use{actor, target}.</summary>
        public CommandAdmissionReceipt Use(TargetId actor, int actorKey, TargetId target) =>
            world.Commands.Submit(InteractionSlots.UseRoute, target, InteractionSlots.UseCommand, UsePayload.Encode(actor, actorKey));

        public CommandAdmissionReceipt SetState(TargetId target, int state) =>
            world.Commands.Submit(InteractionSlots.SetStateRoute, target, InteractionSlots.SetStateCommand, InteractionValuePayload.Encode(state, 0));

        public CommandAdmissionReceipt Trigger(TargetId trigger, int actorKey, bool entered) =>
            world.Commands.Submit(InteractionSlots.TriggerRoute, trigger, InteractionSlots.TriggerCommand, InteractionValuePayload.Encode(actorKey, entered ? 1 : 0));
    }

    /// <summary>One interactable as a focus candidate (position = committed world.pos + focus height).</summary>
    public sealed class InteractableCandidate : IInteractable
    {
        private readonly InteractableRecord record;

        public InteractableCandidate(InteractableRecord record)
        {
            this.record = record ?? throw new ArgumentNullException(nameof(record));
        }

        public string AuthoringId => record.AuthoringId;

        public int Key => record.Key;

        public TargetId Target => record.Target;

        public int Priority => record.Interactable != null ? record.Interactable.FocusPriority : 0;

        public bool TryGetFocusPoint(ICommittedSlotReader slots, out int x, out int y, out int z, out int regionKey)
        {
            bool alive = !slots.TryRead(record.Target, GameplaySlots.EntityOwner, GameplaySlots.Alive, out int aliveValue) || aliveValue != 0;
            regionKey = slots.TryRead(record.Target, GameplaySlots.WorldOwner, GameplaySlots.Region, out int region) ? region : 0;
            y = (slots.TryRead(record.Target, GameplaySlots.WorldOwner, GameplaySlots.PosY, out int py) ? py : 0)
                + (record.Interactable != null ? GameplayUnits.ToMillimetres(record.Interactable.FocusHeight) : 0);
            z = 0;
            return slots.TryRead(record.Target, GameplaySlots.WorldOwner, GameplaySlots.PosX, out x)
                && slots.TryRead(record.Target, GameplaySlots.WorldOwner, GameplaySlots.PosZ, out z)
                && alive;
        }

        public PromptRequest PromptFor(ICommittedSlotReader slots)
        {
            int state = slots.TryRead(record.Target, InteractionSlots.Owner, InteractionSlots.State, out int value) ? value : 0;
            string text = record.Interactable != null ? record.Interactable.PromptFor(state) : "Use";
            string kind = record.Interactable != null ? "interaction." + record.Interactable.Kind.ToString().ToLowerInvariant() : "interaction";
            return new PromptRequest(record.AuthoringId, text, kind, state);
        }
    }

    /// <summary>Every interactable (not trigger) of the extension as focus candidates.</summary>
    public sealed class InteractableSource : IInteractableSource
    {
        private readonly List<IInteractable> items = new List<IInteractable>();

        public InteractableSource(InteractionWorldExtension extension)
        {
            for (int i = 0; i < extension.Records.Count; i++)
            {
                if (!extension.Records[i].IsTrigger)
                {
                    items.Add(new InteractableCandidate(extension.Records[i]));
                }
            }
        }

        public void Collect(List<IInteractable> into) => into.AddRange(items);
    }

    /// <summary>Maps player interact requests to interact.use and runs actions and cues on committed outcomes.</summary>
    public sealed class InteractionDispatcher : IGameplayInputSource
    {
        private readonly List<CommittedEvent> events = new List<CommittedEvent>();
        private readonly GameplayEventCursor cursor = new GameplayEventCursor();
        private readonly InteractionWorldExtension extension;
        private readonly InteractionCommands commands;

        public InteractionDispatcher(InteractionWorldExtension extension, InteractionCommands commands)
        {
            this.extension = extension ?? throw new ArgumentNullException(nameof(extension));
            this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
        }

        public IActionRunner Actions { get; set; } = new NullActionRunner();

        public IFeedbackSink Feedback { get; set; } = new NullFeedbackSink();

        public int UsesSubmitted { get; private set; }

        public int Succeeded { get; private set; }

        public int Refused { get; private set; }

        /// <summary>The refusal code of the last committed InteractionRefused (e.g. interaction.locked).</summary>
        public string LastRefusalCode { get; private set; } = string.Empty;

        public int Collect(GameplayWorld world)
        {
            InteractionModule? module = extension.Module;
            if (module == null)
            {
                return 0;
            }

            events.Clear();
            cursor.ReadInto(world, events);
            int submitted = 0;
            for (int i = 0; i < events.Count; i++)
            {
                CommittedEvent committed = events[i];
                if (!GameplayActorEvent.TryDecode(committed.Payload, out GameplayActorEvent e))
                {
                    continue;
                }

                if (committed.Schema.Equals(PlayerSlots.InteractRequestedEvent))
                {
                    if (module.TryGetByKey(e.A, out InteractableRecord? focused) && focused != null && !focused.IsTrigger
                        && commands.Use(e.Target, e.B, focused.Target).Admitted)
                    {
                        UsesSubmitted++;
                        submitted++;
                    }
                }
                else if (committed.Schema.Equals(InteractionSlots.SucceededEvent))
                {
                    if (module.TryGet(e.Target, out InteractableRecord? record) && record != null)
                    {
                        Succeeded++;
                        var context = new InteractionContext(record.AuthoringId, record.Key, e.A, e.B, world.Slots);
                        if (!string.IsNullOrEmpty(record.ActionRef) && world.StepTap == null)
                        {
                            // With a step tap the action became an outbox obligation in the committing step (P1.7a, A1).
                            Actions.Run(record.ActionRef, context);
                        }

                        Cue(world, record, (record.Interactable != null ? record.Interactable.Cue : "interaction") + "." + InteractableStates.Name(e.B), e.B);
                    }
                }
                else if (committed.Schema.Equals(InteractionSlots.RefusedEvent))
                {
                    Refused++;
                    LastRefusalCode = InteractionSlots.RefusalCode(e.B);
                    if (module.TryGet(e.Target, out InteractableRecord? record) && record != null)
                    {
                        Cue(world, record, "interaction.refused." + LastRefusalCode, e.C);
                    }
                }
                else if (committed.Schema.Equals(InteractionSlots.TriggerEnteredEvent))
                {
                    if (e.C != 0 && world.StepTap == null && module.TryGet(e.Target, out InteractableRecord? record) && record != null && !string.IsNullOrEmpty(record.ActionRef))
                    {
                        Actions.Run(record.ActionRef, new InteractionContext(record.AuthoringId, record.Key, e.A, e.B, world.Slots));
                    }
                }
            }

            return submitted;
        }

        private void Cue(GameplayWorld world, InteractableRecord record, string cue, int state)
        {
            int x = world.Slots.ReadOrDefault(record.Target, GameplaySlots.WorldOwner, GameplaySlots.PosX, 0);
            int y = world.Slots.ReadOrDefault(record.Target, GameplaySlots.WorldOwner, GameplaySlots.PosY, 0);
            int z = world.Slots.ReadOrDefault(record.Target, GameplaySlots.WorldOwner, GameplaySlots.PosZ, 0);
            Feedback.OnFeedback(new FeedbackCue(record.AuthoringId, cue, state, x, y, z));
        }
    }

    /// <summary>A trigger box in world millimetres: centre, half extents and yaw (mrad).</summary>
    public readonly struct TriggerVolume
    {
        public TriggerVolume(int centerX, int centerY, int centerZ, int halfX, int halfY, int halfZ, int yaw)
        {
            CenterX = centerX;
            CenterY = centerY;
            CenterZ = centerZ;
            HalfX = halfX;
            HalfY = halfY;
            HalfZ = halfZ;
            Yaw = yaw;
        }

        public int CenterX { get; }

        public int CenterY { get; }

        public int CenterZ { get; }

        public int HalfX { get; }

        public int HalfY { get; }

        public int HalfZ { get; }

        public int Yaw { get; }

        /// <summary>The volume of a placed trigger: its committed placement (box bottom at the placement) and size.</summary>
        public static bool TryOf(ICommittedSlotReader slots, InteractableRecord record, out TriggerVolume volume)
        {
            volume = default(TriggerVolume);
            if (record.Trigger == null
                || !slots.TryRead(record.Target, GameplaySlots.WorldOwner, GameplaySlots.PosX, out int x)
                || !slots.TryRead(record.Target, GameplaySlots.WorldOwner, GameplaySlots.PosZ, out int z))
            {
                return false;
            }

            int y = slots.TryRead(record.Target, GameplaySlots.WorldOwner, GameplaySlots.PosY, out int py) ? py : 0;
            int yaw = slots.TryRead(record.Target, GameplaySlots.WorldOwner, GameplaySlots.Yaw, out int pyaw) ? pyaw : 0;
            Vector3 size = record.Trigger.Size;
            int halfY = GameplayUnits.ToMillimetres(size.y * 0.5f);
            volume = new TriggerVolume(x, y + halfY, z, GameplayUnits.ToMillimetres(size.x * 0.5f), halfY, GameplayUnits.ToMillimetres(size.z * 0.5f), yaw);
            return true;
        }

        /// <summary>True when the point (mm) lies inside the box (rotation by yaw applied in double precision once).</summary>
        public bool Contains(int x, int y, int z)
        {
            double angle = -Yaw / 1000.0;
            double dx = x - (double)CenterX;
            double dz = z - (double)CenterZ;
            double localX = dx * Math.Cos(angle) + dz * Math.Sin(angle);
            double localZ = -dx * Math.Sin(angle) + dz * Math.Cos(angle);
            return Math.Abs(localX) <= HalfX && Math.Abs(localZ) <= HalfZ && Math.Abs(y - (double)CenterY) <= HalfY;
        }
    }

    /// <summary>
    /// Submits interact.trigger when the focus actor's committed pose (world.pos) enters or leaves a trigger volume.
    /// "Inside" is the actor's committed bit in the trigger's interact.occupants mask, so a restore or a refused command
    /// never leaves this interactor out of step with the kernel. After a submit the trigger is left alone until the bit
    /// agrees, or for <see cref="ResubmitFrames"/> frames, after which the same transit is submitted again.
    /// </summary>
    public sealed class ProximityInteractor : IGameplayInputSource
    {
        /// <summary>Frames to wait for a submitted transit to commit before submitting it again.</summary>
        public const int ResubmitFrames = 30;

        private readonly Dictionary<TargetId, PendingTransit> pending = new Dictionary<TargetId, PendingTransit>();
        private readonly InteractionWorldExtension extension;
        private readonly InteractionCommands commands;
        private int frame;

        public ProximityInteractor(InteractionWorldExtension extension, InteractionCommands commands, TargetId actor)
        {
            this.extension = extension ?? throw new ArgumentNullException(nameof(extension));
            this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
            Actor = actor;
            ActorKey = 0;
        }

        public TargetId Actor { get; }

        /// <summary>The actor's stable key carried in trigger events; 0 means "no actor" and nothing is submitted.</summary>
        public int ActorKey { get; set; }

        public int Transits { get; private set; }

        /// <summary>Transits submitted again because the committed bit still disagreed after <see cref="ResubmitFrames"/>.</summary>
        public int Resubmits { get; private set; }

        public int Collect(GameplayWorld world)
        {
            frame++;
            int bit = OccupancyRules.BitOf(ActorKey);
            if (bit == 0 || extension.Module == null || !InteractionModule.TryActorPosition(world.Slots, Actor, out int x, out int z))
            {
                return 0;
            }

            int y = world.Slots.ReadOrDefault(Actor, GameplaySlots.WorldOwner, GameplaySlots.PosY, 0);
            int region = world.Slots.ReadOrDefault(Actor, GameplaySlots.WorldOwner, GameplaySlots.Region, 0);
            int submitted = 0;
            IReadOnlyList<InteractableRecord> records = extension.Module.Records;
            for (int i = 0; i < records.Count; i++)
            {
                InteractableRecord record = records[i];
                if (!record.IsTrigger || !TriggerVolume.TryOf(world.Slots, record, out TriggerVolume volume))
                {
                    continue;
                }

                bool sameRegion = world.Slots.ReadOrDefault(record.Target, GameplaySlots.WorldOwner, GameplaySlots.Region, 0) == region;
                bool now = sameRegion && volume.Contains(x, y, z);
                int occupants = world.Slots.ReadOrDefault(record.Target, InteractionSlots.Owner, InteractionSlots.Occupants, 0);
                bool was = (occupants & bit) != 0;
                if (now == was)
                {
                    pending.Remove(record.Target);
                    continue;
                }

                bool resubmit = false;
                if (pending.TryGetValue(record.Target, out PendingTransit last) && last.Entered == now)
                {
                    if (frame - last.Frame < ResubmitFrames)
                    {
                        continue;
                    }

                    resubmit = true;
                }

                if (commands.Trigger(record.Target, ActorKey, now).Admitted)
                {
                    pending[record.Target] = new PendingTransit(frame, now);
                    Transits++;
                    Resubmits += resubmit ? 1 : 0;
                    submitted++;
                }
            }

            return submitted;
        }

        private readonly struct PendingTransit
        {
            public PendingTransit(int frame, bool entered)
            {
                Frame = frame;
                Entered = entered;
            }

            public int Frame { get; }

            public bool Entered { get; }
        }
    }

    /// <summary>Per-state look of interactable views; adds a collider to views that have none.</summary>
    public sealed class InteractableBinder : IPresentationBinder, IResidencyAware
    {
        private readonly PrefabViewBinder views;
        private readonly InteractionWorldExtension extension;
        private readonly RegionResidencySet residency = new RegionResidencySet();
        private readonly Dictionary<TargetId, int> presented = new Dictionary<TargetId, int>();
        private readonly Dictionary<TargetId, string> presentedRegions = new Dictionary<TargetId, string>();
        private readonly List<TargetId> evicted = new List<TargetId>();
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        public InteractableBinder(PrefabViewBinder views, InteractionWorldExtension extension)
        {
            this.views = views;
            this.extension = extension;
        }

        public string BinderName => "gameplay.interactable";

        public bool IsActive => views != null && views.IsActive;

        public void OnResidencyChanged(string regionId, RegionResidency value)
        {
            if (residency.Set(regionId, value))
            {
                return;
            }

            evicted.Clear();
            foreach (KeyValuePair<TargetId, string> pair in presentedRegions)
            {
                if (string.Equals(pair.Value, regionId, StringComparison.Ordinal))
                {
                    evicted.Add(pair.Key);
                }
            }

            for (int i = 0; i < evicted.Count; i++)
            {
                presented.Remove(evicted[i]);
                presentedRegions.Remove(evicted[i]);
            }

            evicted.Clear();
        }

        public int Present(ICommittedSlotReader slots)
        {
            if (!IsActive)
            {
                return 0;
            }

            int touched = 0;
            IReadOnlyList<InteractableRecord> records = extension.Records;
            for (int i = 0; i < records.Count; i++)
            {
                InteractableRecord record = records[i];
                if (record.IsTrigger || record.Interactable == null)
                {
                    continue;
                }

                string regionId = views.RegionIdOf(slots.TryRead(record.Target, GameplaySlots.WorldOwner, GameplaySlots.Region, out int regionKey) ? regionKey : 0);
                if (!residency.IsResident(regionId) || !views.TryGetView(record.Target, out GameObject? view) || view == null)
                {
                    continue;
                }

                if (view.GetComponentInChildren<Collider>() == null)
                {
                    view.AddComponent<BoxCollider>();
                }

                int state = slots.TryRead(record.Target, InteractionSlots.Owner, InteractionSlots.State, out int value) ? value : 0;
                Apply(view, record.Interactable.Kind, state);
                presented[record.Target] = state;
                presentedRegions[record.Target] = regionId;
                touched++;
            }

            return touched;
        }

        private void Apply(GameObject view, InteractableKind kind, int state)
        {
            bool open = state == InteractableStates.Open;
            if (kind == InteractableKind.Door || kind == InteractableKind.Gate)
            {
                Transform body = view.transform.childCount > 0 ? view.transform.GetChild(0) : view.transform;
                body.localRotation = Quaternion.Euler(0f, open ? 90f : 0f, 0f);
                Collider[] colliders = view.GetComponentsInChildren<Collider>();
                for (int i = 0; i < colliders.Length; i++)
                {
                    colliders[i].enabled = !open;
                }
            }

            if (kind == InteractableKind.Switch || state == InteractableStates.Locked || state == InteractableStates.Used)
            {
                Color tint = state == InteractableStates.On ? new Color(1f, 0.85f, 0.35f)
                    : state == InteractableStates.Locked ? new Color(0.55f, 0.25f, 0.2f)
                    : state == InteractableStates.Used ? new Color(0.5f, 0.5f, 0.5f)
                    : Color.white;
                Renderer[] renderers = view.GetComponentsInChildren<Renderer>();
                for (int i = 0; i < renderers.Length; i++)
                {
                    renderers[i].GetPropertyBlock(block);
                    block.SetColor("_BaseColor", tint);
                    block.SetColor("_Color", tint);
                    renderers[i].SetPropertyBlock(block);
                }
            }
        }
    }

    /// <summary>The installed interaction loop of one world.</summary>
    public sealed class InteractionSession
    {
        private InteractionSession(InteractionCommands commands, InteractableSource candidates, InteractionDispatcher dispatcher, ProximityInteractor proximity)
        {
            Commands = commands;
            Candidates = candidates;
            Dispatcher = dispatcher;
            Proximity = proximity;
        }

        public InteractionCommands Commands { get; }

        public InteractableSource Candidates { get; }

        public InteractionDispatcher Dispatcher { get; }

        public ProximityInteractor Proximity { get; }

        /// <summary>Adds the dispatcher and the proximity interactor (inputs) and, when the world has views, the binder.</summary>
        public static InteractionSession Install(GameplayWorld world, InteractionWorldExtension extension, TargetId actor, int actorKey)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (extension == null || extension.Module == null)
            {
                throw new InvalidOperationException("the interaction extension is not attached to this world");
            }

            var commands = new InteractionCommands(world);
            var dispatcher = new InteractionDispatcher(extension, commands);
            var proximity = new ProximityInteractor(extension, commands, actor) { ActorKey = actorKey };
            world.AddInput(dispatcher);
            world.AddInput(proximity);
            if (world.Views != null)
            {
                world.AddBinder(new InteractableBinder(world.Views, extension));
            }

            return new InteractionSession(commands, new InteractableSource(extension), dispatcher, proximity);
        }
    }
}
