// GameCore.Gameplay.Player - PlayerSession: installs the player loop into an attached GameplayWorld (P1.3).
//
// Order matters and is fixed here:
//   inputs (CollectInput, before the pump):  PortalProbe -> InteractionFocus -> PlayerInputAdapter
//     the probe and the focus read the committed state of the previous step; the adapter's move is the step heartbeat.
//   binders (Present, after the pump):        PlayerLocomotion (view follow, footsteps) -> InteractionFocus (prompt)
//                                             -> ThirdPersonCamera
//   (the prefab view binder that CreateViews added runs before all of them.)
#nullable enable
using System;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Player;
using UnityEngine;

namespace GameCore.Gameplay.Player
{
    /// <summary>The installed player loop of one world.</summary>
    public sealed class PlayerSession : IDisposable
    {
        private readonly InputSystemIntentSource? ownedSource;

        private PlayerSession(
            PlayerWorldExtension extension,
            PlayerCommands commands,
            PlayerInputAdapter input,
            PlayerLocomotion locomotion,
            InteractionFocus focus,
            PortalProbe portals,
            ThirdPersonCamera camera,
            InputSystemIntentSource? ownedSource)
        {
            Extension = extension;
            Commands = commands;
            Input = input;
            Locomotion = locomotion;
            Focus = focus;
            Portals = portals;
            Camera = camera;
            this.ownedSource = ownedSource;
        }

        public PlayerWorldExtension Extension { get; }

        public PlayerCommands Commands { get; }

        public PlayerInputAdapter Input { get; }

        public PlayerLocomotion Locomotion { get; }

        public InteractionFocus Focus { get; }

        public PortalProbe Portals { get; }

        public ThirdPersonCamera Camera { get; }

        /// <summary>
        /// Installs the player loop. <paramref name="intents"/> overrides the Input System source (tests); a null camera
        /// leaves the orbit camera inactive.
        /// </summary>
        public static PlayerSession Install(
            GameplayWorld world,
            PlayerWorldExtension extension,
            Camera? camera,
            IPlayerIntentSource? intents = null,
            GameObject? rigPrefab = null)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (extension == null || extension.Module == null)
            {
                throw new InvalidOperationException(PlayerNpcInteractionCodes.PlayerNotInWorld + ": the player extension is not attached to this world");
            }

            PlayerDefinition? definition = extension.Definition;
            InputSystemIntentSource? owned = null;
            IPlayerIntentSource source = intents ?? (owned = new InputSystemIntentSource(
                definition != null && definition.Input != null ? definition.Input.Actions : null,
                definition != null && definition.Input != null ? definition.Input.Map : InputProfile.DefaultMap));

            var commands = new PlayerCommands(world, extension.Player);
            var input = new PlayerInputAdapter(extension, commands, source);
            var locomotion = new PlayerLocomotion(
                world.Views, extension.Player, extension.PlayerAuthoringId, definition, rigPrefab != null ? rigPrefab : definition != null ? definition.Rig : null);
            input.Resolver = locomotion;
            var focus = new InteractionFocus(extension, commands, definition != null ? definition.ToFocusTuning() : FocusTuning.Default);
            var portals = new PortalProbe(extension, definition != null ? definition.Radius : 0.35f);
            float initialYaw = (float)GameplayUnits.MilliradiansToDegrees(
                world.Slots.ReadOrDefault(extension.Player, PlayerSlots.Owner, PlayerSlots.Yaw, 0));
            var orbit = new ThirdPersonCamera(camera, extension.Player, input, OrbitSettings.From(definition), initialYaw);
            if (orbit.IsActive)
            {
                input.CameraYaw = () => orbit.Yaw;
            }

            world.AddInput(portals);
            world.AddInput(focus);
            world.AddInput(input);
            world.AddBinder(locomotion);
            world.AddBinder(focus);
            world.AddBinder(orbit);
            return new PlayerSession(extension, commands, input, locomotion, focus, portals, orbit, owned);
        }

        public void Dispose()
        {
            Locomotion.Dispose();
            ownedSource?.Dispose();
        }
    }
}
