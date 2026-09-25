// GameCore.Unity.Adapters.Views — the GameObject/Transform engine half of presentation (GC-019).
//
// Normative sources: 04 s7's GameObject/Transform row ("Explicit adapter commands only"; "GameObjects read committed
// ECS snapshots; a visual interpolation cache is disposable. Transform parentage and scope membership are
// independent"), 00 P-010 ("Scope membership and parent changes are composition operations, never a side effect of
// moving a Transform"), P-034 (a presentation mirror is not an independently writable authoritative copy) and
// P-024 (a despawn destroys only recipe-owned entities/resources).
//
// This binder is the only place in the adapter package that touches a GameObject. It holds one object per committed
// view key, applies scalar presentation values to it, and offers an explicit visual-parent call. It cannot move
// composition: the composition parent is a `ScopeId` reported by the snapshot and never derived from a Transform.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Unity.Adapters.Views;
using UnityEngine;

namespace GameCore.Unity.Adapters.Views
{
    /// <summary>
    /// Unity implementation of <see cref="IViewBinder"/>. Each view is a `GameObject` created under a container the
    /// application supplies; the handle is the object's instance id, which is explicitly *not* an identity (P-054)
    /// and is never persisted.
    /// </summary>
    public sealed class GameObjectViewBinder : IViewBinder
    {
        private readonly Transform container;
        private readonly Dictionary<long, Entry> live = new Dictionary<long, Entry>();
        private readonly List<long> order = new List<long>();

        /// <summary>Set by a fixture to prove a refusal is a value the registry records rather than a crash.</summary>
        public bool RefuseCreate { get; set; }

        public bool RefuseDestroy { get; set; }

        public GameObjectViewBinder(Transform container)
        {
            this.container = container ?? throw new ArgumentNullException(nameof(container));
        }

        public int LiveViewCount => live.Count;

        public int ApplyCount { get; private set; }

        public int CreatedCount { get; private set; }

        public int DestroyedCount { get; private set; }

        public int VisualParentChangeCount { get; private set; }

        /// <summary>Containers this binder created; the application destroys it, not the binder (04 s9).</summary>
        public Transform Container => container;

        public bool TryCreate(ViewKey key, out long handle, out string detail)
        {
            handle = 0L;
            detail = string.Empty;
            if (RefuseCreate)
            {
                detail = "the binder is configured to refuse creation";
                return false;
            }

            var created = new GameObject("view-" + key.Target.ToString() + "-" + key.Slot.ToString(CultureInfo.InvariantCulture));
            created.transform.SetParent(container, false);
            handle = created.GetInstanceID();
            live[handle] = new Entry(created, key);
            order.Add(handle);
            CreatedCount++;
            return true;
        }

        public bool TryDestroy(long handle, out string detail)
        {
            detail = string.Empty;
            if (RefuseDestroy)
            {
                detail = "the binder is configured to refuse destruction";
                return false;
            }

            if (!live.TryGetValue(handle, out Entry entry))
            {
                // Already gone: a repeated destroy is not a second destroy (P-050).
                return true;
            }

            live.Remove(handle);
            order.Remove(handle);
            if (entry.Object != null)
            {
                // DestroyImmediate keeps teardown synchronous and observable in a batch-mode test run; a live player
                // would use Destroy, and the difference is presentation-only (04 s7).
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(entry.Object);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(entry.Object);
                }
            }

            DestroyedCount++;
            return true;
        }

        public bool TrySetVisualParent(long handle, long parentHandle, out string detail)
        {
            detail = string.Empty;
            if (!live.TryGetValue(handle, out Entry entry) || entry.Object == null)
            {
                detail = "no live view for that handle (P-005)";
                return false;
            }

            Transform parent = live.TryGetValue(parentHandle, out Entry parentEntry) && parentEntry.Object != null
                ? parentEntry.Object.transform
                : container;

            entry.Object.transform.SetParent(parent, false);
            VisualParentChangeCount++;
            return true;
        }

        /// <summary>
        /// Applies committed presentation values. It writes only presentation-side fields on presentation objects:
        /// there is no ECS write in this method, so "rendering cannot deduct resources or advance authoritative
        /// steps" (TEST-019) is a property of the code rather than a promise about callers.
        /// </summary>
        public void Apply(long handle, PresentationApplyData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            ApplyCount++;
            if (!live.TryGetValue(handle, out Entry entry) || entry.Object == null)
            {
                return;
            }

            // The committed token is recorded on the object's name, so a debugging session can see which publication
            // a view was last presented from without inventing a second authority (P-045).
            entry.Object.name = "view-" + data.Key.Target.ToString()
                + "-s" + data.Token.LogicalStepId.Value.ToString(CultureInfo.InvariantCulture)
                + "-e" + data.Token.AssemblyEpoch.Value.ToString(CultureInfo.InvariantCulture);

            IReadOnlyList<PresentationField> fields = data.Fields;
            if (fields.Count == 0)
            {
                return;
            }

            // A scalar presentation channel: the first value scales the object on X. That is deliberately a
            // presentation-only mapping, so a test can observe that presentation followed committed output without
            // asserting anything about gameplay semantics.
            entry.Object.transform.localScale = new Vector3(fields[0].Value, 1f, 1f);
        }

        /// <summary>Live objects in creation order; used by fixtures to prove teardown destroyed every view.</summary>
        public IReadOnlyList<GameObject> LiveObjects()
        {
            var objects = new List<GameObject>(order.Count);
            for (int i = 0; i < order.Count; i++)
            {
                if (live.TryGetValue(order[i], out Entry entry) && entry.Object != null)
                {
                    objects.Add(entry.Object);
                }
            }

            return objects;
        }

        private readonly struct Entry
        {
            public readonly GameObject? Object;
            public readonly ViewKey Key;

            public Entry(GameObject o, ViewKey key)
            {
                Object = o;
                Key = key;
            }
        }
    }
}
