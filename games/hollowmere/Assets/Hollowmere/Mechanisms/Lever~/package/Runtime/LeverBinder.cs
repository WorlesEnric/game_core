#nullable enable
using System;
using GameCore.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Hollowmere.Mechanism.Lever
{
    /// <summary>A visible bistable handle, driven only by committed slots after the game's normal pump.</summary>
    public sealed class LeverBinder : IPresentationBinder, IDisposable
    {
        private readonly GameObject? view;
        private readonly Transform? handle;
        private readonly Renderer? indicator;
        private readonly MaterialPropertyBlock? properties;
        private readonly Func<bool> toggle;
        private readonly GameObject? controls;
        private readonly PanelSettings? panel;
        private readonly Label? stateLabel;
        private readonly Button? toggleButton;
        private int presented = -1;

        public LeverBinder(Func<bool> toggle)
        {
            this.toggle = toggle ?? throw new ArgumentNullException(nameof(toggle));
            if (Application.isBatchMode && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                return;
            }

            view = new GameObject("Admitted Lever (committed state)");
            view.transform.position = new Vector3(2f, 0f, 2f);
            GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pedestal.name = "Lever pedestal";
            pedestal.transform.SetParent(view.transform, false);
            pedestal.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            pedestal.transform.localScale = new Vector3(0.5f, 0.6f, 0.5f);
            GameObject pivot = new GameObject("Lever pivot");
            pivot.transform.SetParent(view.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            handle = pivot.transform;
            GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arm.name = "Lever handle";
            arm.transform.SetParent(handle, false);
            arm.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            arm.transform.localScale = new Vector3(0.12f, 0.7f, 0.12f);
            indicator = arm.GetComponent<Renderer>();
            properties = new MaterialPropertyBlock();
            view.SetActive(false);

            // Runtime UI Toolkit control: a click submits one command; no autonomous Update or pump.
            controls = new GameObject("Admitted Lever Controls");
            PanelSettings? template = null;
            foreach (UIDocument existing in UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                if (existing.panelSettings != null && existing.panelSettings.themeStyleSheet != null)
                {
                    template = existing.panelSettings;
                    break;
                }
            }
            if (template == null) throw new InvalidOperationException("The lever controls require the game's runtime UI theme");
            panel = UnityEngine.Object.Instantiate(template);
            panel.name = "Admitted Lever Panel (runtime)";
            panel.sortingOrder = 100;
            panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            UIDocument document = controls.AddComponent<UIDocument>();
            document.panelSettings = panel;
            VisualElement root = document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.style.flexGrow = 1;
            var card = new VisualElement { name = "lever-controls" };
            card.style.position = Position.Absolute;
            card.style.left = 20;
            card.style.top = 140;
            card.style.width = 210;
            card.style.paddingTop = 10;
            card.style.paddingBottom = 10;
            card.style.paddingLeft = 10;
            card.style.paddingRight = 10;
            card.style.backgroundColor = new Color(0.08f, 0.1f, 0.13f, 0.95f);
            card.style.unityFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            card.style.color = Color.white;
            card.style.fontSize = 16;
            stateLabel = new Label("Lever: unavailable") { name = "lever-state" };
            stateLabel.style.height = 26;
            card.Add(stateLabel);
            toggleButton = new Button(OnToggle) { name = "lever-toggle", text = "Toggle lever" };
            toggleButton.style.height = 36;
            toggleButton.style.unityTextAlign = TextAnchor.MiddleCenter;
            toggleButton.style.backgroundColor = new Color(0.18f, 0.3f, 0.42f);
            toggleButton.SetEnabled(false);
            card.Add(toggleButton);
            root.Add(card);
        }

        public string BinderName => "hollowmere.lever.binder";
        public bool IsActive => view != null;

        public int Present(ICommittedSlotReader slots)
        {
            if (view == null || handle == null || indicator == null || properties == null)
            {
                return 0;
            }

            if (!slots.TryRead(LeverDeclarations.Target, LeverDeclarations.Owner, LeverDeclarations.StateSlot, out int state)
                || (state != 0 && state != 1))
            {
                view.SetActive(false);
                toggleButton?.SetEnabled(false);
                if (stateLabel != null) { stateLabel.text = "Lever: unavailable"; }
                return 0;
            }

            view.SetActive(true);
            toggleButton?.SetEnabled(true);
            if (stateLabel != null) { stateLabel.text = state == 1 ? "Lever: On" : "Lever: Off"; }
            if (presented != state)
            {
                handle.localRotation = Quaternion.Euler(state == 1 ? 35f : -35f, 0f, 0f);
                Color color = state == 1 ? Color.green : Color.red;
                properties.SetColor("_Color", color);
                properties.SetColor("_BaseColor", color);
                indicator.SetPropertyBlock(properties);
                presented = state;
            }

            return 1;
        }

        private void OnToggle()
        {
            bool admitted = toggle();
            if (toggleButton != null)
            {
                toggleButton.tooltip = admitted ? "Toggle queued; state changes only after commit."
                    : "Toggle was not admitted; the game must be running and the command lane available.";
            }
        }

        public void Dispose()
        {
            if (toggleButton != null) { toggleButton.clicked -= OnToggle; }
            if (controls != null) { UnityEngine.Object.Destroy(controls); }
            if (panel != null) { UnityEngine.Object.Destroy(panel); }
            if (view != null) { UnityEngine.Object.Destroy(view); }
        }
    }
}
