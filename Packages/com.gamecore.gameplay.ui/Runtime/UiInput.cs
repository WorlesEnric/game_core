// GameCore.Gameplay.Ui - UI input through the Input System (P1.5): list navigation, dialogue submit, cancel.
//
// UI Toolkit's own event system moves focus between buttons and submits the focused one. On top of that UiInput reads
// three Input System actions created in code (no asset edit): Navigate (arrows, WASD, d-pad, left stick) moves the
// selection of the dialogue choices and of the save/load, journal and inventory lists; Submit (Enter, Space, gamepad
// south) chooses the highlighted dialogue choice or advances a line while a conversation is shown; Cancel (Backspace,
// gamepad east) raises UiIntent.Cancel. Pause, Journal and Inventory belong to the player's action map and arrive as
// IUiIntentSink intents. Created only when a graphics device exists (UiRoot).
#nullable enable
using System;
using GameCore.Gameplay.Contracts;
using GameCore.Rules.Gameplay.Ui;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GameCore.Gameplay.Ui
{
    /// <summary>Polls the UI actions once per frame.</summary>
    public sealed class UiInput : IDisposable
    {
        private const float Threshold = 0.5f;

        private readonly UiRuntime runtime;
        private readonly InputAction navigate;
        private readonly InputAction submit;
        private readonly InputAction cancel;
        private Vector2 last;

        public UiInput(UiRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            navigate = new InputAction("UiNavigate", InputActionType.Value, expectedControlType: "Vector2");
            navigate.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            navigate.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            navigate.AddBinding("<Gamepad>/dpad");
            navigate.AddBinding("<Gamepad>/leftStick");
            submit = new InputAction("UiSubmit", InputActionType.Button, "<Keyboard>/enter");
            submit.AddBinding("<Keyboard>/space");
            submit.AddBinding("<Gamepad>/buttonSouth");
            cancel = new InputAction("UiCancel", InputActionType.Button, "<Keyboard>/backspace");
            cancel.AddBinding("<Gamepad>/buttonEast");
            navigate.Enable();
            submit.Enable();
            cancel.Enable();
        }

        public int Navigations { get; private set; }

        public void Poll()
        {
            bool dialogue = runtime.Screen == UiScreen.Hud && runtime.Models.Dialogue.Visible;
            if (!dialogue && !ScreenFlowRules.IsModal(runtime.Screen))
            {
                last = Vector2.zero;
                return;
            }

            Vector2 value = navigate.ReadValue<Vector2>();
            int dx = Edge(value.x, last.x);
            int dy = Edge(value.y, last.y);
            last = value;
            if (dx != 0 || dy != 0)
            {
                Navigations++;
                runtime.Navigate(dx, dy);
            }

            if (dialogue && submit.WasPressedThisFrame())
            {
                runtime.Raise(UiIntent.Confirm);
            }

            if (cancel.WasPressedThisFrame())
            {
                runtime.Raise(UiIntent.Cancel);
            }
        }

        public void Dispose()
        {
            navigate.Dispose();
            submit.Dispose();
            cancel.Dispose();
        }

        private static int Edge(float now, float before)
        {
            if (now > Threshold && before <= Threshold)
            {
                return 1;
            }

            if (now < -Threshold && before >= -Threshold)
            {
                return -1;
            }

            return 0;
        }
    }

    /// <summary>
    /// Default session actions of a game whose boot scene builds the world: new game and restart reload the active scene
    /// with the HUD as the next start screen (a PlayerPrefs request, consumed by the next boot); quit leaves the player.
    /// </summary>
    public sealed class SceneReloadSessionActions : IUiSessionActions
    {
        public int Reloads { get; private set; }

        public bool NewGame(UiRuntime ui) => Reload();

        public bool Restart(UiRuntime ui) => Reload();

        public void Quit(UiRuntime ui)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private bool Reload()
        {
            UiSettingsStore.RequestStartScreen(UiScreen.Hud);
            UnityEngine.SceneManagement.Scene active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!active.IsValid())
            {
                return false;
            }

            Reloads++;
            if (active.buildIndex >= 0)
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(active.buildIndex);
            }
            else
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(active.name);
            }

            return true;
        }
    }
}
