// GameCore.Studio.UI - maps runtime entity views back to their authored objects for picking in Play mode. Gameplay views
// (Hollowmere's NPC capsules, entity views) are spawned at runtime and are not authored objects themselves; they carry
// a view tag naming the authored object (by convention a component type named "EntityViewTag" with a public string
// property "AuthoringId"; bound by name, so the Studio takes no dependency on gameplay packages). A pick that lands on
// such a view is re-targeted to the semantic-index node with that authoring id; anything else passes through.
#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using UnityEngine;

namespace GameCore.Studio.UI
{
    /// <summary>Re-targets picks on runtime views to authored objects.</summary>
    public sealed class RuntimeViewMapper
    {
        public const string ViewTagTypeName = "EntityViewTag";
        public const string AuthoringIdProperty = "AuthoringId";

        private readonly StudioRuntime _runtime;
        private readonly Dictionary<Type, PropertyInfo?> _properties = new Dictionary<Type, PropertyInfo?>();
        private Dictionary<string, AuthoringRef>? _byAuthoringId;
        private long _indexRevision = -1;

        public RuntimeViewMapper(StudioRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        /// <summary>Picks re-targeted so far.</summary>
        public int Mapped { get; private set; }

        /// <summary>The authoring id of the view tag on <paramref name="hit"/> or its parents, or null.</summary>
        public string? ViewAuthoringId(GameObject? hit)
        {
            Transform? current = hit != null ? hit.transform : null;
            while (current != null)
            {
                foreach (MonoBehaviour behaviour in current.GetComponents<MonoBehaviour>())
                {
                    if (behaviour == null)
                    {
                        continue;
                    }

                    PropertyInfo? property = PropertyOf(behaviour.GetType());
                    if (property != null && property.GetValue(behaviour) is string id && id.Length > 0)
                    {
                        return id;
                    }
                }

                current = current.parent;
            }

            return null;
        }

        /// <summary>The candidate re-targeted to the authored object of its view, or the candidate itself.</summary>
        public PickCandidate Map(PickCandidate candidate)
        {
            if (candidate.Ref.AuthoringId != null || candidate.Source == PickSource.Ground || candidate.Source == PickSource.NavMesh || candidate.Source == PickSource.Ui)
            {
                return candidate;
            }

            string? id = ViewAuthoringId(candidate.HitObject);
            if (id == null || !Lookup().TryGetValue(id, out AuthoringRef? authored))
            {
                return candidate;
            }

            Mapped++;
            return new PickCandidate(authored, candidate.Owner, candidate.HitObject, null, candidate.Distance, candidate.Occluded, candidate.Source, candidate.OverlapGroup, candidate.Point);
        }

        private Dictionary<string, AuthoringRef> Lookup()
        {
            SemanticIndexService index = _runtime.Index;
            if (!index.IsBuilt)
            {
                index.Rebuild();
            }

            if (_byAuthoringId == null || _indexRevision != index.Revision)
            {
                Dictionary<string, AuthoringRef> map = new Dictionary<string, AuthoringRef>(StringComparer.Ordinal);
                foreach (IndexNode node in index.Snapshot().Nodes)
                {
                    if (node.Ref.AuthoringId != null && !map.ContainsKey(node.Ref.AuthoringId))
                    {
                        map.Add(node.Ref.AuthoringId, node.Ref);
                    }
                }

                _byAuthoringId = map;
                _indexRevision = index.Revision;
            }

            return _byAuthoringId;
        }

        private PropertyInfo? PropertyOf(Type type)
        {
            if (!_properties.TryGetValue(type, out PropertyInfo? property))
            {
                property = string.Equals(type.Name, ViewTagTypeName, StringComparison.Ordinal)
                    ? type.GetProperty(AuthoringIdProperty, BindingFlags.Public | BindingFlags.Instance)
                    : null;
                if (property != null && property.PropertyType != typeof(string))
                {
                    property = null;
                }

                _properties[type] = property;
            }

            return property;
        }
    }
}
