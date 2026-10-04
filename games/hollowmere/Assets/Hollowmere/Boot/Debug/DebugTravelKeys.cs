// Hollowmere - keyboard travel (P1.1). REPLACED BY P1.3: throwaway debug control; P1.3 drives travel through the
// player controller and portal triggers. 1/2/3 travel the focus traveller to the 1st/2nd/3rd region (canonical
// order); P toggles neighbour preloading.
#nullable enable
using GameCore.Contracts;
using GameCore.Gameplay.World;
using Hollowmere.Boot;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hollowmere.Boot.Debug
{
    /// <summary>Keyboard travel for the focus traveller (replaced by P1.3).</summary>
    public sealed class DebugTravelKeys : MonoBehaviour
    {
        [SerializeField] private GameBoot? boot;

        public int Requests { get; private set; }

        public void Configure(GameBoot gameBoot) => boot = gameBoot;

        private void Update()
        {
            Keyboard? keyboard = Keyboard.current;
            GameplayWorld? world = boot != null ? boot.World : null;
            if (keyboard == null || world == null || world.Focus.IsDefault)
            {
                return;
            }

            if (keyboard.pKey.wasPressedThisFrame)
            {
                world.Streamer.PreloadNeighbours = !world.Streamer.PreloadNeighbours;
            }

            int index = keyboard.digit1Key.wasPressedThisFrame ? 0
                : keyboard.digit2Key.wasPressedThisFrame ? 1
                : keyboard.digit3Key.wasPressedThisFrame ? 2
                : -1;
            if (index < 0 || index >= world.Worlds.Regions.Count)
            {
                return;
            }

            Requests++;
            CommandAdmissionReceipt receipt = world.Commands.Travel(world.Focus, world.Worlds.Regions[index].AuthoringId);
            UnityEngine.Debug.Log("[Hollowmere/debug] travel to " + world.Worlds.Regions[index].Name + ": admitted=" + receipt.Admitted);
        }
    }
}
