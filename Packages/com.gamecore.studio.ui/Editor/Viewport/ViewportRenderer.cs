// GameCore.Studio.UI - what the viewport renders (02 s1/s7, SR-1.1, SR-8.2): the game camera when a GameCore world is
// running in Play (the player's camera: Camera.main, else the highest-depth enabled scene camera), otherwise a Studio
// free camera (a hidden, never-saved camera that is not pickable). The chosen camera is rendered into the viewport's
// RenderTexture through RenderPipeline.SubmitRenderRequest (URP StandardRequest: the camera's own pipeline, its target
// texture restored afterwards), falling back to Camera.Render with a temporary target texture. Rendering never pumps
// the world. Under -nographics nothing is rendered (CanRender is false).
#nullable enable
using System;
using System.Diagnostics;
using GameCore.Unity.App;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameCore.Studio.UI
{
    /// <summary>Which camera the viewport shows.</summary>
    public enum ViewportCameraSource
    {
        /// <summary>The Studio free camera (Edit mode, or Play without a running GameCore world).</summary>
        FreeCamera,
        /// <summary>The running game's camera.</summary>
        GameCamera,
    }

    /// <summary>Camera choice, the free camera and render-to-texture.</summary>
    public sealed class ViewportRenderer : IDisposable
    {
        public const string FreeCameraName = "GameCoreStudio.FreeCamera";

        private Camera? _free;
        private RenderTexture? _target;

        /// <summary>True when a graphics device exists (false under -nographics).</summary>
        public static bool CanRender => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        public RenderTexture? Target => _target;

        public ViewportCameraSource Source { get; private set; }

        /// <summary>The camera rendered last (or chosen by <see cref="ChooseCamera"/>).</summary>
        public Camera? Current { get; private set; }

        public double LastRenderMs { get; private set; }

        public long Renders { get; private set; }

        /// <summary>When true the free camera is used even while a world runs (inspect the world from outside).</summary>
        public bool ForceFreeCamera { get; set; }

        /// <summary>The hidden free camera (created on first use; reused after a domain reload).</summary>
        public Camera FreeCamera
        {
            get
            {
                if (_free == null)
                {
                    _free = FindOrCreateFreeCamera();
                }

                return _free;
            }
        }

        /// <summary>The running game's camera, or null.</summary>
        public static Camera? FindGameCamera()
        {
            if (!EditorApplication.isPlaying)
            {
                return null;
            }

            Camera? main = Camera.main;
            if (main != null && main.isActiveAndEnabled && !IsInternal(main))
            {
                return main;
            }

            Camera? best = null;
            foreach (Camera camera in Camera.allCameras)
            {
                if (camera == null || IsInternal(camera) || camera.targetTexture != null)
                {
                    continue;
                }

                if (best == null || camera.depth > best.depth)
                {
                    best = camera;
                }
            }

            return best;
        }

        /// <summary>True when a GameCore application is running (or paused) in Play mode.</summary>
        public static bool WorldRunning()
        {
            GameApplicationRoot? root = EditorApplication.isPlaying ? GameApplication.Current : null;
            return root != null && (root.State == GameApplicationState.Running || root.State == GameApplicationState.Paused || root.State == GameApplicationState.Ready);
        }

        /// <summary>Chooses the camera for this frame.</summary>
        public Camera ChooseCamera()
        {
            Camera? game = !ForceFreeCamera && WorldRunning() ? FindGameCamera() : null;
            if (game != null)
            {
                Source = ViewportCameraSource.GameCamera;
                Current = game;
                return game;
            }

            Source = ViewportCameraSource.FreeCamera;
            Current = FreeCamera;
            return Current;
        }

        /// <summary>(Re)creates the target texture for a viewport of <paramref name="width"/> x <paramref name="height"/> pixels.</summary>
        public RenderTexture EnsureTarget(int width, int height)
        {
            width = Mathf.Clamp(width, 16, 8192);
            height = Mathf.Clamp(height, 16, 8192);
            if (_target != null && _target.width == width && _target.height == height)
            {
                return _target;
            }

            ReleaseTarget();
            _target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "GameCoreStudio.Viewport",
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = 1,
            };
            _target.Create();
            return _target;
        }

        /// <summary>Renders <paramref name="camera"/> into the target (no-op without a graphics device or target).</summary>
        public bool Render(Camera camera)
        {
            if (!CanRender || _target == null || camera == null)
            {
                return false;
            }

            Stopwatch watch = Stopwatch.StartNew();
            RenderPipeline.StandardRequest request = new RenderPipeline.StandardRequest { destination = _target };
            if (GraphicsSettings.currentRenderPipeline != null && RenderPipeline.SupportsRenderRequest(camera, request))
            {
                RenderPipeline.SubmitRenderRequest(camera, request);
            }
            else
            {
                RenderTexture previous = camera.targetTexture;
                camera.targetTexture = _target;
                try
                {
                    camera.Render();
                }
                finally
                {
                    camera.targetTexture = previous;
                }
            }

            watch.Stop();
            LastRenderMs = watch.Elapsed.TotalMilliseconds;
            Renders++;
            return true;
        }

        /// <summary>Places the free camera like the last active Scene view camera.</summary>
        public void MatchSceneView()
        {
            SceneView? view = SceneView.lastActiveSceneView;
            if (view != null && view.camera != null)
            {
                FreeCamera.transform.SetPositionAndRotation(view.camera.transform.position, view.camera.transform.rotation);
                FreeCamera.fieldOfView = view.camera.fieldOfView;
            }
        }

        /// <summary>Points the free camera at bounds (F to frame the selection).</summary>
        public void Frame(Bounds bounds)
        {
            Camera camera = FreeCamera;
            float radius = Mathf.Max(bounds.extents.magnitude, 0.5f);
            float distance = radius / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            camera.transform.position = bounds.center - (camera.transform.forward * distance);
        }

        public void Dispose()
        {
            ReleaseTarget();
        }

        public static bool IsInternal(Component component)
        {
            return (component.gameObject.hideFlags & (HideFlags.DontSave | HideFlags.HideInHierarchy)) != 0;
        }

        private void ReleaseTarget()
        {
            if (_target != null)
            {
                _target.Release();
                UnityEngine.Object.DestroyImmediate(_target);
                _target = null;
            }
        }

        private static Camera FindOrCreateFreeCamera()
        {
            foreach (Camera camera in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (camera != null && camera.name == FreeCameraName && !EditorUtility.IsPersistent(camera))
                {
                    return camera;
                }
            }

            GameObject host = new GameObject(FreeCameraName) { hideFlags = HideFlags.HideAndDontSave };
            Camera created = host.AddComponent<Camera>();
            created.enabled = false;
            created.nearClipPlane = 0.05f;
            created.farClipPlane = 2000f;
            created.fieldOfView = 60f;
            created.clearFlags = CameraClearFlags.Skybox;
            host.transform.SetPositionAndRotation(new Vector3(0f, 12f, -18f), Quaternion.Euler(30f, 0f, 0f));
            return created;
        }
    }
}
