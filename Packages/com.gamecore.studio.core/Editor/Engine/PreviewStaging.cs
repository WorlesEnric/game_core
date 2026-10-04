// GameCore.Studio.Edit - preview objects of staged change sets (docs/studio/03-authoring-contracts.md s6 "Stage").
// Ghosts live under one hidden root (HideAndDontSave, never saved, never indexed or picked: the resolver treats DontSave
// objects as Studio-internal). A ghost mirrors only the renderers of its source (meshes with a translucent ghost
// material), so no gameplay component of the source runs or is copied. Staging is in-memory only: a domain reload
// drops it, and the journal state Candidate lets the user re-stage (02 s5).
#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameCore.Studio.Edit
{
    /// <summary>Owns the staging root and the ghost material.</summary>
    public sealed class PreviewStaging : IDisposable
    {
        public const string RootName = "GameCoreStudio.Staging";

        private readonly Dictionary<string, List<GameObject>> _byOwner = new Dictionary<string, List<GameObject>>(StringComparer.Ordinal);
        private GameObject? _root;
        private Material? _ghostMaterial;
        private string _owner = string.Empty;

        /// <summary>The hidden root (created on first use).</summary>
        public GameObject Root
        {
            get
            {
                if (_root == null)
                {
                    _root = new GameObject(RootName) { hideFlags = HideFlags.HideAndDontSave };
                }

                return _root;
            }
        }

        /// <summary>The translucent material of every ghost renderer.</summary>
        public Material GhostMaterial
        {
            get
            {
                if (_ghostMaterial == null)
                {
                    Shader? shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Hidden/Internal-Colored");
                    _ghostMaterial = new Material(shader!) { name = "GameCoreStudio.Ghost", hideFlags = HideFlags.HideAndDontSave };
                    Color tint = new Color(0.35f, 0.75f, 1f, 0.35f);
                    _ghostMaterial.color = tint;
                    if (_ghostMaterial.HasProperty("_BaseColor"))
                    {
                        _ghostMaterial.SetColor("_BaseColor", tint);
                    }

                    if (_ghostMaterial.HasProperty("_Surface"))
                    {
                        _ghostMaterial.SetFloat("_Surface", 1f);
                    }

                    _ghostMaterial.renderQueue = 3000;
                }

                return _ghostMaterial;
            }
        }

        /// <summary>Number of live ghosts.</summary>
        public int Count
        {
            get
            {
                int count = 0;
                foreach (List<GameObject> ghosts in _byOwner.Values)
                {
                    count += ghosts.Count;
                }

                return count;
            }
        }

        /// <summary>Ghosts created while <paramref name="owner"/> is current belong to it (the staged change set id).</summary>
        internal void BeginOwner(string owner)
        {
            _owner = owner ?? string.Empty;
        }

        internal void EndOwner()
        {
            _owner = string.Empty;
        }

        /// <summary>A ghost of <paramref name="source"/>'s renderers at a world pose, or null when it has no renderer.</summary>
        public GameObject? Ghost(GameObject source, Vector3 position, Quaternion rotation)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            Renderer[] renderers = source.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return null;
            }

            GameObject ghost = new GameObject("Ghost of " + source.name) { hideFlags = HideFlags.HideAndDontSave };
            ghost.transform.SetParent(Root.transform, false);
            ghost.transform.SetPositionAndRotation(position, rotation);
            ghost.transform.localScale = source.transform.lossyScale;
            Matrix4x4 toSource = source.transform.worldToLocalMatrix;
            foreach (Renderer renderer in renderers)
            {
                Mesh? mesh = null;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    mesh = skinned.sharedMesh;
                }
                else if (renderer.TryGetComponent(out MeshFilter filter))
                {
                    mesh = filter.sharedMesh;
                }

                if (mesh == null)
                {
                    continue;
                }

                GameObject part = new GameObject(renderer.name) { hideFlags = HideFlags.HideAndDontSave };
                part.transform.SetParent(ghost.transform, false);
                Matrix4x4 local = toSource * renderer.transform.localToWorldMatrix;
                part.transform.localPosition = local.GetColumn(3);
                part.transform.localRotation = local.rotation;
                part.transform.localScale = local.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer ghostRenderer = part.AddComponent<MeshRenderer>();
                Material[] materials = new Material[Math.Max(1, mesh.subMeshCount)];
                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i] = GhostMaterial;
                }

                ghostRenderer.sharedMaterials = materials;
                ghostRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ghostRenderer.receiveShadows = false;
            }

            Register(ghost);
            return ghost;
        }

        /// <summary>Adopts an object created for a preview (moved under the root, hidden, owned by the current owner).</summary>
        public GameObject Adopt(GameObject preview)
        {
            SetHideFlags(preview);
            preview.transform.SetParent(Root.transform, true);
            Register(preview);
            return preview;
        }

        /// <summary>The ghosts of one owner.</summary>
        public IReadOnlyList<GameObject> GhostsOf(string owner)
        {
            return _byOwner.TryGetValue(owner, out List<GameObject>? ghosts) ? ghosts : (IReadOnlyList<GameObject>)Array.Empty<GameObject>();
        }

        /// <summary>Destroys the ghosts of one owner.</summary>
        public void Clear(string owner)
        {
            if (!_byOwner.TryGetValue(owner, out List<GameObject>? ghosts))
            {
                return;
            }

            foreach (GameObject ghost in ghosts)
            {
                if (ghost != null)
                {
                    UnityEngine.Object.DestroyImmediate(ghost);
                }
            }

            _byOwner.Remove(owner);
        }

        /// <summary>Destroys every ghost and the root.</summary>
        public void ClearAll()
        {
            foreach (string owner in new List<string>(_byOwner.Keys))
            {
                Clear(owner);
            }

            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
                _root = null;
            }
        }

        public void Dispose()
        {
            ClearAll();
            if (_ghostMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_ghostMaterial);
                _ghostMaterial = null;
            }
        }

        private void Register(GameObject ghost)
        {
            if (!_byOwner.TryGetValue(_owner, out List<GameObject>? ghosts))
            {
                ghosts = new List<GameObject>();
                _byOwner[_owner] = ghosts;
            }

            ghosts.Add(ghost);
        }

        private static void SetHideFlags(GameObject root)
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                transform.gameObject.hideFlags = HideFlags.HideAndDontSave;
            }
        }
    }
}
