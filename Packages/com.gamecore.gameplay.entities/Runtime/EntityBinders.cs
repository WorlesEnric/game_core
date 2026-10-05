// GameCore.Gameplay.Entities - presentation binders (P1.1; P-045: presentation reads committed output only).
//
//   PrefabViewBinder        instantiates each alive+visible entity's definition prefab (or its variant's replacement)
//                           under the root of the region the entity is in, applies pose (world.pos), scale, tint and
//                           overrides, keeps one view pool per region and destroys a view when its entity despawns. It
//                           is residency-aware (P1.7a, A7): views exist only in Resident regions. A region it has had no
//                           residency notification for counts as not Resident. When a region stops being Resident all
//                           of its views are destroyed (the region holds zero views); they are recreated on the first
//                           Present after it is Resident again. A view whose entity moves region is moved to the new
//                           region's root, or destroyed when that region is not Resident.
//   AnimatorBinder          drives Animator integer parameters from entity slots by a name map on the definition;
//                           residency-aware like every view-reading binder (it skips targets in non-Resident regions
//                           and forgets their cached animators when the region goes non-Resident).
//   AudioSourceBinder       hook only: forwards spawn/despawn of views to registered audio hooks.
//   InteractionTargetBinder stub: tags each view with its target so interaction systems (P1.3) can resolve it.
//
// Every binder is headless-safe: under batchmode with no graphics device it reports inactive and touches nothing.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameCore.Gameplay.Entities
{
    /// <summary>Where binders may touch engine objects.</summary>
    public static class BinderEnvironment
    {
        /// <summary>True under batchmode with no graphics device (-batchmode -nographics): binders skip.</summary>
        public static bool IsHeadless =>
            Application.isBatchMode && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
    }

    /// <summary>Destroys engine objects correctly in both play and edit mode.</summary>
    public static class ViewObjects
    {
        public static void Destroy(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }

    /// <summary>What a view binder needs to present one entity target.</summary>
    public sealed class EntityViewSpec
    {
        public EntityViewSpec(TargetId target, string authoringId, string name, EntityDefinition definition, IReadOnlyDictionary<string, string>? overrides)
        {
            Target = target;
            AuthoringId = authoringId;
            Name = name;
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Overrides = overrides ?? new Dictionary<string, string>();
        }

        public TargetId Target { get; }

        public string AuthoringId { get; }

        public string Name { get; }

        public EntityDefinition Definition { get; }

        public IReadOnlyDictionary<string, string> Overrides { get; }
    }

    /// <summary>A hook told when entity views appear and disappear (audio, effects). Implemented by game code.</summary>
    public interface IEntityViewHook
    {
        void OnViewCreated(TargetId target, GameObject view);

        void OnViewDestroyed(TargetId target, GameObject view);
    }

    /// <summary>An interaction target the interaction binder exposes on a view (P1.3 consumes it).</summary>
    public interface IInteractionTarget
    {
        TargetId Target { get; }

        string InteractionKind { get; }
    }

    /// <summary>
    /// The last residency notified for each region (by authoring id), for binders that implement IResidencyAware. A
    /// region never notified is not Resident. The streamer replays every notified region to a listener when it is added,
    /// so a binder that records each call here knows the current residency from its first Present.
    /// </summary>
    public sealed class RegionResidencySet
    {
        private readonly Dictionary<string, RegionResidency> states = new Dictionary<string, RegionResidency>(StringComparer.Ordinal);

        /// <summary>Records a notification; returns true when the region is Resident afterwards.</summary>
        public bool Set(string regionId, RegionResidency residency)
        {
            states[regionId ?? string.Empty] = residency;
            return residency == RegionResidency.Resident;
        }

        /// <summary>True only for a region whose last notification was Resident.</summary>
        public bool IsResident(string regionId) =>
            states.TryGetValue(regionId ?? string.Empty, out RegionResidency state) && state == RegionResidency.Resident;

        /// <summary>The last notified residency; false when the region was never notified.</summary>
        public bool TryGet(string regionId, out RegionResidency residency) => states.TryGetValue(regionId ?? string.Empty, out residency);
    }

    /// <summary>Instantiates and maintains entity views from committed slots, only in Resident regions.</summary>
    public sealed class PrefabViewBinder : IPresentationBinder, IResidencyAware
    {
        private readonly Transform parent;
        private readonly IReadOnlyDictionary<int, string> regionByKey;
        private readonly IReadOnlyDictionary<string, string> regionNames;
        private readonly List<EntityViewSpec> specs = new List<EntityViewSpec>();
        private readonly Dictionary<TargetId, View> views = new Dictionary<TargetId, View>();
        private readonly Dictionary<string, Transform> regionRoots = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private readonly RegionResidencySet residency = new RegionResidencySet();
        private readonly List<TargetId> evicted = new List<TargetId>();
        private readonly List<IEntityViewHook> hooks = new List<IEntityViewHook>();
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        /// <param name="parent">Transform the per-region view roots are created under.</param>
        /// <param name="regionByKey">Region stable key (the world.region slot value) to region authoring id.</param>
        /// <param name="regionNames">Region authoring id to display name (names the view roots).</param>
        public PrefabViewBinder(Transform parent, IReadOnlyDictionary<int, string> regionByKey, IReadOnlyDictionary<string, string> regionNames)
        {
            this.parent = parent;
            this.regionByKey = regionByKey ?? throw new ArgumentNullException(nameof(regionByKey));
            this.regionNames = regionNames ?? throw new ArgumentNullException(nameof(regionNames));
            IsActive = !BinderEnvironment.IsHeadless && parent != null;
        }

        private readonly Dictionary<TargetId, Func<int>> appearances = new Dictionary<TargetId, Func<int>>();

        /// <summary>Registers a live definition-default variant resolver (NPC session); explicit slot variants win.</summary>
        public void BindAppearance(TargetId target, Func<int> variant) => appearances[target] = variant;

        public int ResolveVariant(TargetId target, int committedVariant) => committedVariant != 0 ? committedVariant
            : appearances.TryGetValue(target, out Func<int> resolve) ? Math.Max(0, resolve()) : 0;

        public string BinderName => "gameplay.prefab-view";

        public bool IsActive { get; }

        public int ViewCount => views.Count;

        /// <summary>Live views under the region's root (always 0 for a region that is not Resident).</summary>
        public int ViewCountIn(string regionId)
        {
            int count = 0;
            foreach (View view in views.Values)
            {
                if (view.Instance != null && string.Equals(view.RegionId, regionId, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>True when the region's last residency notification was Resident (never notified: false).</summary>
        public bool IsResident(string regionId) => residency.IsResident(regionId);

        /// <summary>The authoring id of a region stable key (the world.region slot value); empty when unknown.</summary>
        public string RegionIdOf(int regionKey) => regionByKey.TryGetValue(regionKey, out string? id) ? id : string.Empty;

        public int CreatedCount { get; private set; }

        public int DestroyedCount { get; private set; }

        public void Add(EntityViewSpec spec) => specs.Add(spec ?? throw new ArgumentNullException(nameof(spec)));

        public void AddHook(IEntityViewHook hook) => hooks.Add(hook ?? throw new ArgumentNullException(nameof(hook)));

        /// <summary>The live view of a target, if any.</summary>
        public bool TryGetView(TargetId target, out GameObject? view)
        {
            if (views.TryGetValue(target, out View? found) && found.Instance != null)
            {
                view = found.Instance;
                return true;
            }

            view = null;
            return false;
        }

        public IEnumerable<KeyValuePair<TargetId, GameObject>> LiveViews()
        {
            foreach (KeyValuePair<TargetId, View> pair in views)
            {
                if (pair.Value.Instance != null)
                {
                    yield return new KeyValuePair<TargetId, GameObject>(pair.Key, pair.Value.Instance);
                }
            }
        }

        public EntityViewSpec? SpecOf(TargetId target)
        {
            for (int i = 0; i < specs.Count; i++)
            {
                if (specs[i].Target.Equals(target))
                {
                    return specs[i];
                }
            }

            return null;
        }

        public void OnResidencyChanged(string regionId, RegionResidency value)
        {
            bool live = residency.Set(regionId, value);
            if (!IsActive)
            {
                return;
            }

            if (!live)
            {
                EvictRegion(regionId ?? string.Empty);
            }

            if (regionRoots.TryGetValue(regionId ?? string.Empty, out Transform? root) && root != null && root.gameObject.activeSelf != live)
            {
                root.gameObject.SetActive(live);
            }
        }

        public int Present(ICommittedSlotReader slots)
        {
            if (!IsActive)
            {
                return 0;
            }

            int touched = 0;
            for (int i = 0; i < specs.Count; i++)
            {
                touched += PresentOne(slots, specs[i]) ? 1 : 0;
            }

            return touched;
        }

        /// <summary>Destroys every view and region root (application stop).</summary>
        public void Clear()
        {
            foreach (KeyValuePair<TargetId, View> pair in views)
            {
                DestroyView(pair.Key, pair.Value);
            }

            views.Clear();
            foreach (Transform root in regionRoots.Values)
            {
                if (root != null)
                {
                    ViewObjects.Destroy(root.gameObject);
                }
            }

            regionRoots.Clear();
        }

        private void EvictRegion(string regionId)
        {
            evicted.Clear();
            foreach (KeyValuePair<TargetId, View> pair in views)
            {
                if (string.Equals(pair.Value.RegionId, regionId, StringComparison.Ordinal))
                {
                    evicted.Add(pair.Key);
                }
            }

            for (int i = 0; i < evicted.Count; i++)
            {
                DestroyView(evicted[i], views[evicted[i]]);
                views.Remove(evicted[i]);
            }

            evicted.Clear();
        }

        private bool PresentOne(ICommittedSlotReader slots, EntityViewSpec spec)
        {
            OwnerId entityOwner = GameplaySlots.EntityOwner;
            OwnerId worldOwner = GameplaySlots.WorldOwner;
            bool alive = slots.TryRead(spec.Target, entityOwner, GameplaySlots.Alive, out int aliveValue) && aliveValue != 0;
            bool visible = !slots.TryRead(spec.Target, entityOwner, GameplaySlots.Visible, out int visibleValue) || visibleValue != 0;
            views.TryGetValue(spec.Target, out View? view);

            if (!alive || !visible)
            {
                if (view != null)
                {
                    DestroyView(spec.Target, view);
                    views.Remove(spec.Target);
                    return true;
                }

                return false;
            }

            int variant = ResolveVariant(spec.Target, slots.TryRead(spec.Target, entityOwner, GameplaySlots.Variant, out int variantValue) ? variantValue : 0);
            int scale = slots.TryRead(spec.Target, entityOwner, GameplaySlots.ScaleMilli, out int scaleValue) ? scaleValue : GameplayUnits.ScaleOne;
            int regionKey = slots.TryRead(spec.Target, worldOwner, GameplaySlots.Region, out int regionValue) ? regionValue : 0;
            string regionId = RegionIdOf(regionKey);
            if (!residency.IsResident(regionId))
            {
                if (view != null)
                {
                    DestroyView(spec.Target, view);
                    views.Remove(spec.Target);
                    return true;
                }

                return false;
            }

            if (view != null && view.Variant != variant)
            {
                DestroyView(spec.Target, view);
                views.Remove(spec.Target);
                view = null;
            }

            if (view == null || view.Instance == null)
            {
                GameObject? instance = Instantiate(spec, variant);
                if (instance == null)
                {
                    return false;
                }

                view = new View(instance, variant, regionId);
                views[spec.Target] = view;
                CreatedCount++;
                instance.transform.SetParent(RootOf(regionId), false);
                ApplyTint(instance, spec, variant);
                MaterialTextureBinding.Apply(instance, spec.Definition.MaterialTextures);
                for (int h = 0; h < hooks.Count; h++)
                {
                    hooks[h].OnViewCreated(spec.Target, instance);
                }
            }
            else if (!string.Equals(view.RegionId, regionId, StringComparison.Ordinal))
            {
                view.RegionId = regionId;
                view.Instance.transform.SetParent(RootOf(regionId), false);
            }

            Transform transform = view.Instance!.transform;
            transform.localPosition = new Vector3(
                (float)GameplayUnits.ToMetres(slots.TryRead(spec.Target, worldOwner, GameplaySlots.PosX, out int x) ? x : 0),
                (float)GameplayUnits.ToMetres(slots.TryRead(spec.Target, worldOwner, GameplaySlots.PosY, out int y) ? y : 0),
                (float)GameplayUnits.ToMetres(slots.TryRead(spec.Target, worldOwner, GameplaySlots.PosZ, out int z) ? z : 0));
            int yaw = slots.TryRead(spec.Target, worldOwner, GameplaySlots.Yaw, out int yawValue) ? yawValue : 0;
            transform.localRotation = Quaternion.Euler(0f, (float)GameplayUnits.MilliradiansToDegrees(yaw), 0f);
            float uniform = (float)GameplayUnits.FromMilli(scale);
            transform.localScale = new Vector3(uniform, uniform, uniform);
            return true;
        }

        private GameObject? Instantiate(EntityViewSpec spec, int variant)
        {
            VariantDefinition? variantDefinition = spec.Definition.VariantAt(variant);
            GameObject? prefab = variantDefinition != null && variantDefinition.Prefab != null
                ? variantDefinition.Prefab
                : spec.Definition.Prefab;
            if (prefab == null)
            {
                return null;
            }

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = spec.Name;
            if (instance.TryGetComponent(out AuthoredEntity proxy))
            {
                // A view is presentation, never an authoring proxy: it must not carry or mint an authoring id.
                ViewObjects.Destroy(proxy);
            }

            if (!instance.TryGetComponent(out EntityViewTag tag))
            {
                tag = instance.AddComponent<EntityViewTag>();
            }

            tag.Bind(spec.Target, spec.AuthoringId);
            return instance;
        }

        private void ApplyTint(GameObject instance, EntityViewSpec spec, int variant)
        {
            Color tint = Color.white;
            VariantDefinition? variantDefinition = spec.Definition.VariantAt(variant);
            if (variantDefinition != null)
            {
                tint = variantDefinition.Tint;
            }

            if (spec.Overrides.TryGetValue(OverrideSet.Tint, out string? hex) && ColorUtility.TryParseHtmlString(hex, out Color overridden))
            {
                tint = overridden;
            }

            if (tint == Color.white)
            {
                return;
            }

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].GetPropertyBlock(block);
                block.SetColor("_BaseColor", tint);
                block.SetColor("_Color", tint);
                renderers[i].SetPropertyBlock(block);
            }
        }

        private Transform RootOf(string regionId)
        {
            bool live = residency.IsResident(regionId);
            if (regionRoots.TryGetValue(regionId, out Transform? existing) && existing != null)
            {
                if (existing.gameObject.activeSelf != live)
                {
                    existing.gameObject.SetActive(live);
                }

                return existing;
            }

            string label = regionNames.TryGetValue(regionId, out string? name) ? name : (regionId.Length == 0 ? "No Region" : regionId);
            var root = new GameObject("[Views] " + label);
            root.transform.SetParent(parent, false);
            root.SetActive(live);
            regionRoots[regionId] = root.transform;
            return root.transform;
        }

        private void DestroyView(TargetId target, View view)
        {
            if (view.Instance == null)
            {
                return;
            }

            for (int h = 0; h < hooks.Count; h++)
            {
                hooks[h].OnViewDestroyed(target, view.Instance);
            }

            ViewObjects.Destroy(view.Instance);
            view.Instance = null;
            DestroyedCount++;
        }

        private sealed class View
        {
            public View(GameObject instance, int variant, string regionId)
            {
                Instance = instance;
                Variant = variant;
                RegionId = regionId;
            }

            public GameObject? Instance { get; set; }

            public int Variant { get; }

            public string RegionId { get; set; }
        }
    }

    /// <summary>Drives Animator integer parameters from entity slots (definition's AnimatorBindings), in Resident regions.</summary>
    public sealed class AnimatorBinder : IPresentationBinder, IResidencyAware
    {
        private readonly PrefabViewBinder views;
        private readonly RegionResidencySet residency = new RegionResidencySet();
        private readonly Dictionary<TargetId, AnimatorParameters> parameters = new Dictionary<TargetId, AnimatorParameters>();
        private readonly List<TargetId> evicted = new List<TargetId>();

        public AnimatorBinder(PrefabViewBinder views)
        {
            this.views = views ?? throw new ArgumentNullException(nameof(views));
            IsActive = views.IsActive;
        }

        public string BinderName => "gameplay.animator";

        public bool IsActive { get; }

        public void OnResidencyChanged(string regionId, RegionResidency value)
        {
            if (residency.Set(regionId, value))
            {
                return;
            }

            evicted.Clear();
            foreach (KeyValuePair<TargetId, AnimatorParameters> pair in parameters)
            {
                if (string.Equals(pair.Value.RegionId, regionId, StringComparison.Ordinal))
                {
                    evicted.Add(pair.Key);
                }
            }

            for (int i = 0; i < evicted.Count; i++)
            {
                parameters.Remove(evicted[i]);
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
            foreach (KeyValuePair<TargetId, GameObject> pair in views.LiveViews())
            {
                EntityViewSpec? spec = views.SpecOf(pair.Key);
                if (spec == null || spec.Definition.AnimatorBindings.Count == 0)
                {
                    continue;
                }

                int regionKey = slots.TryRead(pair.Key, GameplaySlots.WorldOwner, GameplaySlots.Region, out int regionValue) ? regionValue : 0;
                string regionId = views.RegionIdOf(regionKey);
                if (!residency.IsResident(regionId))
                {
                    continue;
                }

                Animator? animator = pair.Value.GetComponentInChildren<Animator>();
                if (animator == null || animator.runtimeAnimatorController == null)
                {
                    continue;
                }

                HashSet<string> known = ParametersOf(pair.Key, animator, regionId);
                IReadOnlyList<AnimatorSlotBinding> bindings = spec.Definition.AnimatorBindings;
                for (int i = 0; i < bindings.Count; i++)
                {
                    AnimatorSlotBinding binding = bindings[i];
                    if (!known.Contains(binding.Parameter)
                        || !GameplaySlots.TryEntitySlot(binding.Slot, out SlotId slot)
                        || !slots.TryRead(pair.Key, GameplaySlots.EntityOwner, slot, out int value))
                    {
                        continue;
                    }

                    if (animator.GetInteger(binding.Parameter) != value)
                    {
                        animator.SetInteger(binding.Parameter, value);
                        touched++;
                    }
                }
            }

            return touched;
        }

        private HashSet<string> ParametersOf(TargetId target, Animator animator, string regionId)
        {
            if (parameters.TryGetValue(target, out AnimatorParameters? cached) && cached.Animator == animator)
            {
                cached.RegionId = regionId;
                return cached.Known;
            }

            var known = new HashSet<string>(StringComparer.Ordinal);
            AnimatorControllerParameter[] declared = animator.parameters;
            for (int i = 0; i < declared.Length; i++)
            {
                if (declared[i].type == AnimatorControllerParameterType.Int)
                {
                    known.Add(declared[i].name);
                }
            }

            parameters[target] = new AnimatorParameters(animator, known, regionId);
            return known;
        }

        private sealed class AnimatorParameters
        {
            public AnimatorParameters(Animator animator, HashSet<string> known, string regionId)
            {
                Animator = animator;
                Known = known;
                RegionId = regionId;
            }

            public Animator Animator { get; }

            public HashSet<string> Known { get; }

            public string RegionId { get; set; }
        }
    }

    /// <summary>Audio hook: forwards view lifecycle to registered hooks. Plays nothing itself (no audio content in P1.1).</summary>
    public sealed class AudioSourceBinder : IPresentationBinder, IEntityViewHook
    {
        private readonly List<IEntityViewHook> hooks = new List<IEntityViewHook>();

        public AudioSourceBinder(PrefabViewBinder views)
        {
            if (views == null)
            {
                throw new ArgumentNullException(nameof(views));
            }

            IsActive = views.IsActive;
            views.AddHook(this);
        }

        public string BinderName => "gameplay.audio-source";

        public bool IsActive { get; }

        public int CreatedCount { get; private set; }

        public void AddHook(IEntityViewHook hook) => hooks.Add(hook ?? throw new ArgumentNullException(nameof(hook)));

        public void OnViewCreated(TargetId target, GameObject view)
        {
            CreatedCount++;
            for (int i = 0; i < hooks.Count; i++)
            {
                hooks[i].OnViewCreated(target, view);
            }
        }

        public void OnViewDestroyed(TargetId target, GameObject view)
        {
            for (int i = 0; i < hooks.Count; i++)
            {
                hooks[i].OnViewDestroyed(target, view);
            }
        }

        public int Present(ICommittedSlotReader slots) => 0;
    }

    /// <summary>Stub: marks views of definitions with an interaction kind. Interaction itself is P1.3.</summary>
    public sealed class InteractionTargetBinder : IPresentationBinder, IEntityViewHook
    {
        private readonly PrefabViewBinder views;

        public InteractionTargetBinder(PrefabViewBinder views)
        {
            this.views = views ?? throw new ArgumentNullException(nameof(views));
            IsActive = views.IsActive;
            views.AddHook(this);
        }

        public string BinderName => "gameplay.interaction-target";

        public bool IsActive { get; }

        public void OnViewCreated(TargetId target, GameObject view)
        {
            EntityViewSpec? spec = views.SpecOf(target);
            if (spec == null || string.IsNullOrEmpty(spec.Definition.InteractionKind))
            {
                return;
            }

            if (!view.TryGetComponent(out InteractionTargetMarker marker))
            {
                marker = view.AddComponent<InteractionTargetMarker>();
            }

            marker.Bind(target, spec.Definition.InteractionKind);
        }

        public void OnViewDestroyed(TargetId target, GameObject view)
        {
        }

        public int Present(ICommittedSlotReader slots) => 0;
    }
}
