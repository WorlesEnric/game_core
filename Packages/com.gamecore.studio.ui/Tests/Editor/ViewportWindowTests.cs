// GameCore.Studio.UI.Tests - the viewport window: the UI tree (modes, pump indicator, prompt bar, candidate strip),
// Tab-cycle and Esc semantics, and (with a graphics device only; skipped under -nographics) the render target matching
// the viewport area at the editor's pixel scale with the camera's image in it.
#nullable enable
using System.Collections;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
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
        private bool _shown;

        [SetUp]
        public void SetUp() => _bed = new UiTestBed();

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                if (_shown)
                {
                    _window.Close();
                }
                else
                {
                    Object.DestroyImmediate(_window);
                }
            }

            _bed.Dispose();
        }

        private StudioViewportWindow OpenWindow()
        {
            StudioViewportWindow window = ScriptableObject.CreateInstance<StudioViewportWindow>();
            window.UseContext(_bed.Context);
            window.position = new Rect(80f, 80f, 960f, 600f);
            if (ViewportRenderer.CanRender)
            {
                window.Show();
                _shown = true;
            }

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
            Assert.That(window.HandleKey(KeyCode.Escape), Is.True);
            Assert.That(_bed.Context.Selection.IsEmpty, Is.True, "Esc clears the selection");
            Assert.That(window.HandleKey(KeyCode.Tab), Is.True);
            Assert.That(window.Mode, Is.EqualTo(ViewportMode.Inspect), "Tab cycles the mode");
            Assert.That(window.HandleKey(KeyCode.Alpha2), Is.True);
            Assert.That(window.Mode, Is.EqualTo(ViewportMode.Select), "2 selects Select mode");
        }

        [UnityTest]
        public IEnumerator R7_D_W_UI_02_MarqueeChooser_OnlyAmbiguityOpensAndFilteringPreservesOriginalOperation()
        {
            if (!ViewportRenderer.CanRender)
            {
                Assert.Ignore("W-UI-02 popup interaction requires a graphical editor panel; geometry classification is covered without graphics.");
            }

            FixtureAuthoredEntity left = _bed.SpawnEntity("Left", new Vector3(-2f, 0f, 0f));
            FixtureAuthoredEntity right = _bed.SpawnEntity("Right", new Vector3(2f, 0f, 0f));
            FixtureAuthoredEntity outside = _bed.SpawnEntity("OutsideMarquee", new Vector3(100f, 0f, 0f));
            _bed.SaveScene();
            StudioViewportWindow window = OpenWindow();
            window.SetMode(ViewportMode.Select);
            Camera camera = window.Renderer.FreeCamera;
            camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200f;
            for (int frame = 0; frame < 60 && (window.Area == null || float.IsNaN(window.Area.contentRect.width) || window.Area.contentRect.width < 2f); frame++)
            {
                window.Repaint();
                yield return null;
            }

            if (window.Area == null || float.IsNaN(window.Area.contentRect.width) || window.Area.contentRect.width < 2f)
            {
                Assert.Ignore("W-UI-02 popup interaction requires the editor to lay out its panel.");
            }

            Rect rectangle = window.ImageRect;
            camera.aspect = rectangle.width / rectangle.height;
            PickResult plain = window.MarqueeSelect(rectangle, SelectionOp.Replace, true);
            Assert.That(plain.Candidates.Count, Is.EqualTo(2));
            Assert.That(window.Overlap!.Visible, Is.False, "a multi-object marquee alone is not ambiguous");
            Assert.That(_bed.Context.Selection.Targets.Count, Is.EqualTo(2));

            right.transform.position = left.transform.position;
            Physics.SyncTransforms();
            AuthoringRef leftRef = _bed.Ref(left);
            AuthoringRef outsideRef = _bed.Ref(outside);
            foreach (SelectionOp operation in new[] { SelectionOp.Replace, SelectionOp.Add, SelectionOp.Toggle })
            {
                _bed.Context.Selection.Set(new[] { leftRef, outsideRef });
                PickResult ambiguous = window.MarqueeSelect(rectangle, operation, true);
                Assert.That(window.Overlap.Visible, Is.True, operation.ToString());
                Assert.That(ambiguous.Candidates.Count, Is.EqualTo(2));
                for (int index = 0; index < window.Overlap.Candidates.Count; index++)
                {
                    Toggle include = window.Overlap.Q<Toggle>("overlap-include-" + index);
                    Assert.That(include, Is.Not.Null);
                    Assert.That(include.value, Is.True, "every marquee candidate starts included");
                    include.value = window.Overlap.Candidates[index].Ref.SameTarget(leftRef);
                }

                Button apply = window.Overlap.Q<Button>("overlap-apply");
                Assert.That(apply, Is.Not.Null);
                using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
                {
                    apply.SendEvent(submit);
                }
                yield return null;

                Assert.That(window.Overlap.Visible, Is.False);
                SelectionModel selection = _bed.Context.Selection;
                if (operation == SelectionOp.Replace)
                {
                    Assert.That(selection.Targets.Count, Is.EqualTo(1));
                    Assert.That(selection.Targets[0].SameTarget(leftRef), Is.True);
                }
                else if (operation == SelectionOp.Add)
                {
                    Assert.That(selection.Targets.Count, Is.EqualTo(2));
                    Assert.That(selection.Targets[0].SameTarget(leftRef), Is.True);
                    Assert.That(selection.Targets[1].SameTarget(outsideRef), Is.True);
                }
                else
                {
                    Assert.That(selection.Targets.Count, Is.EqualTo(1));
                    Assert.That(selection.Targets[0].SameTarget(outsideRef), Is.True, "toggle must use the selection before the initial marquee, not toggle it twice");
                }
                Assert.That(selection.RegionRect!.Screen, Is.EqualTo(new double[] { rectangle.xMin, rectangle.yMin, rectangle.xMax, rectangle.yMax }));
            }

            window.MarqueeSelect(rectangle, SelectionOp.Replace, true);
            Assert.That(window.Overlap.Visible, Is.True);
            right.transform.position = new Vector3(2f, 0f, 0f);
            Physics.SyncTransforms();
            window.MarqueeSelect(rectangle, SelectionOp.Replace, true);
            Assert.That(window.Overlap.Visible, Is.False, "a subsequent plain marquee dismisses stale ambiguity");
        }

        [UnityTest]
        public IEnumerator Render_TargetMatchesTheViewportArea()
        {
            if (!ViewportRenderer.CanRender)
            {
                Assert.Ignore("R2-37: graphical qualification required; not rendered: no graphics device (-nographics). The graphical evidence run (studio/tools/evidence-p2.1.sh) covers rendering.");
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
                Assert.Ignore("R2-37: graphical qualification required; not rendered: the editor did not lay the window out in this session (batch mode).");
            }

            Assert.That(window.RenderNow(), Is.True);
            RenderTexture texture = window.Texture!;
            float scale = EditorGUIUtility.pixelsPerPoint;
            Rect area = window.Area!.contentRect;
            Assert.That(texture.width, Is.EqualTo(Mathf.RoundToInt(area.width * scale)));
            Assert.That(texture.height, Is.EqualTo(Mathf.RoundToInt(area.height * scale)));
            Assert.That(window.Renderer.Renders, Is.GreaterThan(0));
            Debug.Log("[P2.1] viewport render " + texture.width + "x" + texture.height + " in " + window.Renderer.LastRenderMs.ToString("0.00") + " ms");
        }
    }
}
