// GameCore.Studio.Edit - the [Authorable] types of the loaded assemblies (docs/studio/03-authoring-contracts.md s4).
// Found through TypeCache; this is the Unity-specific pass of the catalog: ObjectTypeEntry fields include non-public
// [SerializeField] members, which the Unity-free ToolCatalogBuilder (public members only) cannot see.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Edit
{
    /// <summary>Authorable types by type id, with their catalog entries.</summary>
    public sealed class AuthorableTypeRegistry
    {
        private readonly AuthoringIdentity _identity;
        private readonly Func<IEnumerable<Type>> _typeSource;
        private List<AuthoringTypeInfo>? _types;
        private readonly List<Diagnostic> _problems = new List<Diagnostic>();

        /// <summary>Types from <c>TypeCache.GetTypesWithAttribute&lt;AuthorableAttribute&gt;()</c>.</summary>
        public AuthorableTypeRegistry(AuthoringIdentity identity)
            : this(identity, () => TypeCache.GetTypesWithAttribute<AuthorableAttribute>())
        {
        }

        /// <summary>Types from an explicit source (tests).</summary>
        public AuthorableTypeRegistry(AuthoringIdentity identity, Func<IEnumerable<Type>> typeSource)
        {
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            _typeSource = typeSource ?? throw new ArgumentNullException(nameof(typeSource));
        }

        public AuthoringIdentity Identity => _identity;

        /// <summary>Every authorable type, sorted by type id.</summary>
        public IReadOnlyList<AuthoringTypeInfo> Types => _types ??= Load();

        /// <summary>Declaration problems found while loading (duplicate type ids, conflicting attributes).</summary>
        public IReadOnlyList<Diagnostic> Problems
        {
            get
            {
                _ = Types;
                return _problems;
            }
        }

        /// <summary>Forgets the loaded types (after a domain reload the registry is rebuilt anyway).</summary>
        public void Invalidate()
        {
            _types = null;
            _problems.Clear();
        }

        public AuthoringTypeInfo? FindByTypeId(string typeId)
        {
            foreach (AuthoringTypeInfo info in Types)
            {
                if (string.Equals(info.TypeId, typeId, StringComparison.Ordinal))
                {
                    return info;
                }
            }

            return null;
        }

        /// <summary>
        /// A CLR type for <c>create</c>/<c>addComponent</c>: an authorable type id, an authorable type's full or short
        /// name, or (for components) any component type's full name.
        /// </summary>
        public Type? ResolveType(string nameOrTypeId)
        {
            AuthoringTypeInfo? byId = FindByTypeId(nameOrTypeId);
            if (byId != null)
            {
                return byId.Type;
            }

            foreach (AuthoringTypeInfo info in Types)
            {
                if (string.Equals(info.Type.FullName, nameOrTypeId, StringComparison.Ordinal) || string.Equals(info.Type.Name, nameOrTypeId, StringComparison.Ordinal))
                {
                    return info.Type;
                }
            }

            foreach (Type type in TypeCache.GetTypesDerivedFrom<Component>())
            {
                if (string.Equals(type.FullName, nameOrTypeId, StringComparison.Ordinal))
                {
                    return type;
                }
            }

            return null;
        }

        /// <summary>The object type entries of the catalog, sorted by type id.</summary>
        public IReadOnlyList<ObjectTypeEntry> ObjectTypeEntries()
        {
            List<ObjectTypeEntry> entries = new List<ObjectTypeEntry>();
            foreach (AuthoringTypeInfo info in Types)
            {
                entries.Add(info.ToObjectTypeEntry());
            }

            return entries;
        }

        private List<AuthoringTypeInfo> Load()
        {
            SortedDictionary<string, AuthoringTypeInfo> byId = new SortedDictionary<string, AuthoringTypeInfo>(StringComparer.Ordinal);
            List<Type> types = new List<Type>(_typeSource());
            types.Sort((left, right) => string.CompareOrdinal(left.FullName, right.FullName));
            foreach (Type type in types)
            {
                AuthoringTypeInfo? info;
                try
                {
                    info = _identity.Describe(type);
                }
                catch (InvalidOperationException error)
                {
                    _problems.Add(StudioDiagnostics.General(DiagnosticCodes.CandidateInvalid, "Authorable type " + type.FullName + ": " + error.Message));
                    continue;
                }

                if (info == null)
                {
                    continue;
                }

                if (byId.TryGetValue(info.TypeId, out AuthoringTypeInfo? existing))
                {
                    _problems.Add(StudioDiagnostics.General(
                        DiagnosticCodes.CandidateInvalid,
                        "Authorable type id '" + info.TypeId + "' is declared by " + existing.Type.FullName + " and " + type.FullName + "; the second is ignored."));
                    continue;
                }

                byId.Add(info.TypeId, info);
            }

            return new List<AuthoringTypeInfo>(byId.Values);
        }
    }
}
