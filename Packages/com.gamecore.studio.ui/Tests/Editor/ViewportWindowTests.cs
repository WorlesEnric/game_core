// GameCore.Studio.UI.Tests - the viewport window: the UI tree (modes, pump indicator, prompt bar, candidate strip),
// Tab-cycle and Esc semantics, and (with a graphics device only; skipped under -nographics) the render target matching
// the viewport area at the editor's pixel scale with the camera's image in it.
#nullable enable
using System.Collections;
using GameCore.Studio.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace GameCore.Studio.UI.Tests
{
    public sealed class ViewportWindowTests
    {
        private UiTestBed _bed = null!;
        private StudioViewportWindow? _window;

        [SetUp]
        public void SetUp() => _bed = new UiTestBed();

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                _window.Close();
            }

            _bed.Dispose();
        }

        private StudioViewportWindow OpenWindow()
        {
            StudioViewportWindow window = ScriptableObject.CreateInstance<StudioViewportWindow>();
            window.UseContext(_bed.Context);
            window.position = new Rect(80f, 80f, 960f, 600f);
            window.Show();
            window.EnsureGui();
            _window = window;
            return window;
        }

        [Test]
        public void Window_BuildsModesIndicatorsPromptAndStrip()
        {
            StudioViewportWindow window = OpenWindow();
            VisualElement root = window.rootVisualElement;
            Assert.That(root.Q<Button>("mode-play"), Is.Not.Null);
            Assert.That(root.Q<Button>("mode-select"), Is.Not.Null);
            Assert.That(root.Q<Button>("mode-inspect"), Is.Not.Null);
            Assert.That(root.Q<Label>("pump-indicator"), Is.Not.Null);
            Assert.That(root.Q("viewport-area"), Is.Not.Null);
            Assert.That(root.Q("prompt-bar"), Is.Not.Null);
            Assert.That(root.Q("candidate-strip"), Is.Not.Null);
            Assert.That(root.Q("overlap-popup"), Is.Not.Null);
            Assert.That(root.Q("move-gizmo"), Is.Not.Null);
            Assert.That(window.Prompt, Is.Not.Null);
        }

        [Test]
        public void Tab_CyclesModesAndEscClearsTheSelection()
        {
            FixtureAuthoredEntity entity = _bed.SpawnEntity("Npc", Vector3.zero);
            _bed.SaveScene();
            StudioViewportWindow window = OpenWindow();
            window.SetMode(ViewportMode.Select);
            window.CycleMode();
            Assert.That(window.Mode, Is.EqualTo(ViewportMode.Inspect));
            window.CycleMode();
            Assert.That(window.Mode, Is.EqualTo(EditorApplication.isPlaying ? ViewportMode.Play : ViewportMode.Select), "Play is only offered in Play Mode");
            window.SetMode(ViewportMode.Play);
            Assert.That(window.Mode, Is.EqualTo(ViewportMode.Select), "Play mode needs a running world");

            _bed.Context.Selection.Set(new[] { _bed.Ref(entity) });
            VisualElement area = window.Area!;
            using (KeyDownEvent escape = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None))
            {
                escape.target = area;
                area.SendEvent(escape);
            }

            Assert.That(_bed.Context.Selection.IsEmpty, Is.True, "Esc clears the selection");
            using (KeyDownEvent tab = KeyDownEvent.GetPooled('\t', KeyCode.Tab, EventModifiers.None))
            {
                tab.target = area;
                area.SendEvent(tab);
            }

            Assert.That(window.Mode, Is.EqualTo(ViewportMode.Inspect), "Tab cycles the mode");
        }

        [UnityTest]
        public IEnumerator Render_TargetMatchesTheViewportArea()
        {
            if (!ViewportRenderer.CanRender)
            {
                Assert.Ignore("No graphics device (-nographics); the graphical evidence run covers rendering.");
            }

            _bed.SpawnEntity("Npc", Vector3.zero);
            StudioViewportWindow window = OpenWindow();
            for (int frame = 0; frame < 60 && (window.Area == null || float.IsNaN(window.Area.contentRect.width) || window.Area.contentRect.width < 2f); frame++)
            {
                window.Repaint();
                yield return null;
            }

            if (window.Area == null || float.IsNaN(window.Area.contentRect.width) || window.Area.contentRect.width < 2f)
            {
                Assert.Ignore("The editor did not lay the window out in this session (batch mode).");
            }

            Assert.That(window.RenderNow(), Is.True);
            RenderTexture texture = window.Texture!;
            float scale = EditorGUIUtility.pixelsPerPoint;
            Rect area = window.Area.contentRect;
            Assert.That(texture.width, Is.EqualTo(Mathf.RoundToInt(area.width * scale)));
            Assert.That(texture.height, Is.EqualTo(Mathf.RoundToInt(area.height * scale)));
            Assert.That(window.Renderer.Renders, Is.GreaterThan(0));
            Debug.Log("[P2.1] viewport render " + texture.width + "x" + texture.height + " in " + window.Renderer.LastRenderMs.ToString("0.00") + " ms");
        }
    }
}
