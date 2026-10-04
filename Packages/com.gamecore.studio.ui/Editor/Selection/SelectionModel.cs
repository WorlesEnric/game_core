// GameCore.Studio.UI - the Studio selection (docs/studio/03-authoring-contracts.md s2). Targets are AuthoringRefs built
// from engine data (picking, Unity's Selection), never Unity instance ids. The model:
//   * replaces / adds / toggles targets (click, shift-add, ctrl-toggle, marquee), keeps picked parts, the marquee rect
//     and a point-at Location;
//   * captures a SelectionSnapshot (mode, frame, world session, index revision) when a prompt is sent;
//   * mirrors itself to UnityEditor.Selection both ways, loop-safe: a push records what it wrote, and the echo of that
//     write (Selection.selectionChanged arrives later) is recognised and ignored; a Unity change that maps to the same
//     targets changes nothing;
//   * describes each target for badges: name, kind, residency (unloaded region -> disabled with a reason) and staleness.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.UI
{
    /// <summary>How a pick changes the selection.</summary>
    public enum SelectionOp
    {
        Replace,
        /// <summary>Shift: add targets not yet selected.</summary>
        Add,
        /// <summary>Ctrl/Cmd: remove selected targets, add the others.</summary>
        Toggle,
    }

    /// <summary>Badge data of one selected target.</summary>
    public sealed class SelectionBadge
    {
        public SelectionBadge(AuthoringRef target, string label, string typeId, bool resident, string? residencyReason, bool stale, string? staleReason)
        {
            Ref = target;
            Label = label;
            TypeId = typeId;
            Resident = resident;
            ResidencyReason = residencyReason;
            Stale = stale;
            StaleReason = staleReason;
        }

        public AuthoringRef Ref { get; }

        public string Label { get; }

        /// <summary>The index node type ([Authorable] type id), or the ref kind when not indexed.</summary>
        public string TypeId { get; }

        /// <summary>False when the target's region is not loaded: the badge is disabled and says why.</summary>
        public bool Resident { get; }

        public string? ResidencyReason { get; }

        public bool Stale { get; }

        public string? StaleReason { get; }
    }

    /// <summary>The Studio selection and its two-way mirror to Unity's Selection.</summary>
    public sealed class SelectionModel : IDisposable
    {
        private readonly StudioRuntime _runtime;
        private readonly List<AuthoringRef> _targets = new List<AuthoringRef>();
        private readonly List<PartRef> _parts = new List<PartRef>();
        private HashSet<int>? _lastPushed;
        private bool _mirrorAttached;

        public SelectionModel(StudioRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public StudioRuntime Runtime => _runtime;

        public IReadOnlyList<AuthoringRef> Targets => _targets;

        public IReadOnlyList<PartRef> Parts => _parts;

        /// <summary>The point-at location (kind Location), or null.</summary>
        public AuthoringRef? Location { get; private set; }

        /// <summary>The last marquee rectangle (viewport pixels), cleared by a click selection.</summary>
        public RegionRect? RegionRect { get; private set; }

        /// <summary>Bumped on every effective change.</summary>
        public long Version { get; private set; }

        public bool IsEmpty => _targets.Count == 0 && Location == null;

        /// <summary>The first target (the context panel's subject), or the location.</summary>
        public AuthoringRef? Primary => _targets.Count > 0 ? _targets[0] : Location;

        /// <summary>Pushes to UnityEditor.Selection performed.</summary>
        public int MirrorWrites { get; private set; }

        /// <summary>Unity selection changes adopted.</summary>
        public int MirrorReads { get; private set; }

        /// <summary>Unity selection callbacks recognised as the echo of our own push and ignored.</summary>
        public int MirrorEchoesIgnored { get; private set; }

        public event Action? Changed;

        /// <summary>Changes the targets. Returns true when the selection changed.</summary>
        public bool Set(IEnumerable<AuthoringRef> targets, SelectionOp op = SelectionOp.Replace, RegionRect? regionRect = null, IEnumerable<PartRef>? parts = null)
        {
            return SetCore(targets, op, regionRect, parts, true);
        }

        /// <summary>Clears targets, parts, the marquee and the location.</summary>
        public void Clear()
        {
            if (IsEmpty && _parts.Count == 0 && RegionRect == null)
            {
                return;
            }

            _targets.Clear();
            _parts.Clear();
            RegionRect = null;
            Location = null;
            Commit(true);
        }

        /// <summary>Sets (or clears) the point-at location.</summary>
        public void SetLocation(AuthoringRef? location)
        {
            if (location != null && location.Kind != AuthoringKind.Location)
            {
                throw new ArgumentException("A point-at location must be an AuthoringRef of kind Location.", nameof(location));
            }

            if (Equals(Location, location))
            {
                return;
            }

            Location = location;
            Commit(false);
        }

        /// <summary>Restores a persisted selection without pushing to Unity (domain reload).</summary>
        public void Restore(IReadOnlyList<AuthoringRef> targets, IReadOnlyList<PartRef>? parts, AuthoringRef? location, RegionRect? regionRect)
        {
            _targets.Clear();
            _targets.AddRange(targets);
            _parts.Clear();
            if (parts != null)
            {
                _parts.AddRange(parts);
            }

            Location = location;
            RegionRect = regionRect;
            Version++;
            Changed?.Invoke();
        }

        /// <summary>
        /// The snapshot sent with a prompt (03 s2): logical targets plus the location (kind Location) when one is set.
        /// </summary>
        public SelectionSnapshot Capture(SelectionMode mode, FrameContext? frame = null, string? worldSession = null)
        {
            List<AuthoringRef> targets = new List<AuthoringRef>(_targets);
            if (Location != null)
            {
                targets.Add(Location);
            }

            string id = IdDerivation.NewSelectionId(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), CryptoIdEntropy.Instance);
            return new SelectionSnapshot(
                id,
                mode,
                targets,
                _runtime.Index.Revision,
                _parts.Count == 0 ? null : new List<PartRef>(_parts),
                RegionRect,
                frame,
                mode == SelectionMode.Play ? worldSession : null);
        }

        /// <summary>The objects the targets resolve to (components map to their GameObject).</summary>
        public IReadOnlyList<UnityEngine.Object> ResolveObjects()
        {
            List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
            foreach (AuthoringRef target in _targets)
            {
                UnityEngine.Object? resolved = _runtime.Resolver.Find(target);
                if (resolved is Component component)
                {
                    resolved = component.gameObject;
                }

                if (resolved != null && !objects.Contains(resolved))
                {
                    objects.Add(resolved);
                }
            }

            return objects;
        }

        /// <summary>The first target's resolved object (authored component or asset), or null.</summary>
        public UnityEngine.Object? ResolvePrimary()
        {
            return _targets.Count == 0 ? null : _runtime.Resolver.Find(_targets[0]);
        }

        /// <summary>Badge data for every target (residency and stale checks run here; call it at most a few times per second).</summary>
        public IReadOnlyList<SelectionBadge> Describe()
        {
            List<SelectionBadge> badges = new List<SelectionBadge>();
            foreach (AuthoringRef target in _targets)
            {
                badges.Add(DescribeRef(_runtime, target));
            }

            return badges;
        }

        /// <summary>Badge data for one ref.</summary>
        public static SelectionBadge DescribeRef(StudioRuntime runtime, AuthoringRef target)
        {
            IndexNode? node = runtime.Index.FindNode(target);
            ResolveResult resolved = runtime.Resolver.Resolve(target);
            string label = node?.Name ?? (resolved.Object != null ? resolved.Object.name : LeafOf(target.Path) ?? target.Kind.ToString());
            RegionResidency residency = runtime.Resolver.Residency.ResidencyOf(target);
            bool resident = residency == RegionResidency.Resident;
            string? residencyReason = resident ? null : "Region " + (NamedResidencyQuery.DefaultRegionOf(target) ?? "of this target") + " is " + residency.ToString().ToLowerInvariant() + "; load it to edit.";
            string? staleReason = null;
            bool stale = false;
            foreach (StaleEntry entry in resolved.Stale)
            {
                if (entry.Reason == StaleReason.RegionUnloaded)
                {
                    resident = false;
                    residencyReason ??= entry.Diagnostic.Message;
                    continue;
                }

                if (entry.Blocking)
                {
                    stale = true;
                    staleReason = entry.Diagnostic.Code + ": " + entry.Diagnostic.Message;
                    break;
                }
            }

            if (!resolved.Resolved && !stale && resident)
            {
                stale = true;
                staleReason = DiagnosticCodes.StaleTarget + ": the target does not resolve.";
            }

            return new SelectionBadge(target, label, node?.Type ?? target.Kind.ToString(), resident, residencyReason, stale, staleReason);
        }

        /// <summary>The ref a Unity object stands for (a GameObject maps to its authored component when it has one).</summary>
        public AuthoringRef? RefOf(UnityEngine.Object target)
        {
            if (target == null)
            {
                return null;
            }

            UnityEngine.Object subject = target;
            if (target is GameObject gameObject)
            {
                MonoBehaviour? authored = _runtime.Identity.FindAuthoredComponent(gameObject);
                if (authored != null)
                {
                    subject = authored;
                }
            }

            return _runtime.Resolver.BuildRef(subject, null, true);
        }

        // ------------------------------------------------------------------------------------- Unity mirror

        /// <summary>Starts mirroring to and from UnityEditor.Selection.</summary>
        public void AttachUnityMirror()
        {
            if (_mirrorAttached)
            {
                return;
            }

            _mirrorAttached = true;
            Selection.selectionChanged += OnUnitySelectionChanged;
        }

        public void DetachUnityMirror()
        {
            if (!_mirrorAttached)
            {
                return;
            }

            _mirrorAttached = false;
            Selection.selectionChanged -= OnUnitySelectionChanged;
        }

        public bool MirrorAttached => _mirrorAttached;

        /// <summary>Writes the resolved targets to UnityEditor.Selection (no-op when Unity already shows exactly them).</summary>
        public void PushToUnity()
        {
            List<UnityEngine.Object> objects = new List<UnityEngine.Object>(ResolveObjects());
            HashSet<int> ids = IdsOf(objects);
            HashSet<int> current = IdsOf(Selection.objects);
            _lastPushed = ids;
            if (ids.SetEquals(current))
            {
                return;
            }

            MirrorWrites++;
            Selection.objects = objects.ToArray();
        }

        /// <summary>Adopts Unity's selection (called from Selection.selectionChanged; tests call it directly).</summary>
        public void OnUnitySelectionChanged()
        {
            UnityEngine.Object[] objects = Selection.objects;
            HashSet<int> ids = IdsOf(objects);
            if (_lastPushed != null && ids.SetEquals(_lastPushed))
            {
                MirrorEchoesIgnored++;
                return;
            }

            List<AuthoringRef> refs = new List<AuthoringRef>();
            foreach (UnityEngine.Object target in objects)
            {
                AuthoringRef? reference = target == null ? null : RefOf(target);
                if (reference != null && !Contains(refs, reference))
                {
                    refs.Add(reference);
                }
            }

            _lastPushed = ids;
            if (SameTargets(refs, _targets))
            {
                return;
            }

            MirrorReads++;
            SetCore(refs, SelectionOp.Replace, null, null, false);
        }

        public void Dispose()
        {
            DetachUnityMirror();
            Changed = null;
        }

        // ------------------------------------------------------------------------------------- internals

        private bool SetCore(IEnumerable<AuthoringRef> targets, SelectionOp op, RegionRect? regionRect, IEnumerable<PartRef>? parts, bool push)
        {
            if (targets == null)
            {
                throw new ArgumentNullException(nameof(targets));
            }

            List<AuthoringRef> next = op == SelectionOp.Replace ? new List<AuthoringRef>() : new List<AuthoringRef>(_targets);
            List<PartRef> nextParts = op == SelectionOp.Replace ? new List<PartRef>() : new List<PartRef>(_parts);
            foreach (AuthoringRef target in targets)
            {
                if (target == null || target.Kind == AuthoringKind.Location)
                {
                    continue;
                }

                int index = IndexOf(next, target);
                if (op == SelectionOp.Toggle && index >= 0)
                {
                    next.RemoveAt(index);
                    nextParts.RemoveAll(part => part.Owner.SameTarget(target));
                }
                else if (index < 0)
                {
                    next.Add(target);
                }
            }

            if (parts != null)
            {
                foreach (PartRef part in parts)
                {
                    if (IndexOf(next, part.Owner) >= 0 && !nextParts.Exists(existing => existing.Owner.SameTarget(part.Owner) && existing.Part == part.Part))
                    {
                        nextParts.Add(part);
                    }
                }
            }

            RegionRect? nextRect = regionRect ?? (op == SelectionOp.Replace ? null : RegionRect);
            if (SameTargets(next, _targets) && nextParts.Count == _parts.Count && Equals(nextRect, RegionRect))
            {
                return false;
            }

            _targets.Clear();
            _targets.AddRange(next);
            _parts.Clear();
            _parts.AddRange(nextParts);
            RegionRect = nextRect;
            Commit(push);
            return true;
        }

        private void Commit(bool push)
        {
            Version++;
            if (push && _mirrorAttached)
            {
                PushToUnity();
            }

            Changed?.Invoke();
        }

        private static HashSet<int> IdsOf(IEnumerable<UnityEngine.Object> objects)
        {
            HashSet<int> ids = new HashSet<int>();
            foreach (UnityEngine.Object target in objects)
            {
                if (target != null)
                {
                    ids.Add(target.GetInstanceID());
                }
            }

            return ids;
        }

        private static int IndexOf(List<AuthoringRef> list, AuthoringRef target)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].SameTarget(target))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool Contains(List<AuthoringRef> list, AuthoringRef target) => IndexOf(list, target) >= 0;

        private static bool SameTargets(IReadOnlyList<AuthoringRef> left, IReadOnlyList<AuthoringRef> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                bool found = false;
                for (int j = 0; j < right.Count; j++)
                {
                    if (left[i].SameTarget(right[j]))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        private static string? LeafOf(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            int slash = path!.LastIndexOfAny(new[] { '/', '#' });
            return slash >= 0 && slash < path.Length - 1 ? path.Substring(slash + 1) : path;
        }
    }
}
