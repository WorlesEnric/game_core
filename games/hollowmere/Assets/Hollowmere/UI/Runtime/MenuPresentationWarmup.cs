#nullable enable
using System.Collections;
using GameCore.Gameplay.Ui;
using GameCore.Rules.Gameplay.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hollowmere.UiAudio
{
    /// <summary>Warms the real menu's layout, glyph atlas and first render before enabling its controls.</summary>
    public sealed class MenuPresentationWarmup : MonoBehaviour
    {
        private UiRoot? root;
        private VisualElement? curtain;
        public bool Ready { get; private set; }
        public int PresentedFrame { get; private set; }

        public void Begin(UiRoot menu)
        {
            root = menu;
            VisualElement panel = menu.Document!.rootVisualElement;
            // Keep the actual menu in the render tree so its fonts, bindings and shaders warm normally.
            // Input and the opaque loading surface make this a real loading state, not a hidden logger delay.
            menu.enabled = false;
            foreach (VisualElement layer in panel.Children()) layer.SetEnabled(false);
            curtain = new VisualElement { name = "hollowmere-loading" };
            curtain.style.position = Position.Absolute;
            curtain.style.left = curtain.style.top = curtain.style.right = curtain.style.bottom = 0;
            curtain.style.backgroundColor = new Color(0.035f, 0.045f, 0.055f, 1f);
            curtain.style.alignItems = Align.Center;
            curtain.style.justifyContent = Justify.Center;
            var label = new Label("Loading Hollowmere…");
            label.style.color = Color.white;
            label.style.fontSize = 24;
            curtain.Add(label);
            panel.Add(curtain);
            StartCoroutine(Warm());
        }

        private IEnumerator Warm()
        {
            // End-of-frame runs after UI repaint and camera rendering. Present two loading frames to drain
            // the first render and its deferred glyph/atlas updates, then reveal during the following update.
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return null;
            if (root == null || root.Document == null) yield break;
            curtain?.RemoveFromHierarchy();
            foreach (VisualElement layer in root.Document.rootVisualElement.Children()) layer.SetEnabled(true);
            root.enabled = true;
            foreach (UiDocumentDefinition definition in root.Flow!.Documents)
                if (definition.Screen == UiScreen.Menu)
                    root.Document.rootVisualElement.Q<VisualElement>("layer-" + definition.name)?.Q<Button>()?.Focus();
            PresentedFrame = Time.frameCount;
            Ready = true;
            var focused = root.Document.rootVisualElement.panel?.focusController?.focusedElement as VisualElement;
            Debug.Log("[Hollowmere] menu presented frame=" + PresentedFrame + " input=" + root.enabled
                + " focus=" + (focused?.name ?? "none"));
        }
    }
}
