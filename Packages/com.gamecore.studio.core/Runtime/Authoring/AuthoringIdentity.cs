// GameCore.Studio.Authoring - duck-typed authoring identity and authoring metadata of Unity objects
// (docs/studio/03-authoring-contracts.md s1, s4).
//
// An object is authored when its type carries [Authorable] (declared on the type itself, as ToolCatalogBuilder reads
// it). Its authoring id is read, in order, through
//   1. an implemented interface named "IAuthoredObject" (any namespace: this package's or gameplay.contracts'),
//   2. a public instance property "AuthoringId" of type string,
//   3. a serialized field named "authoringId" (public, or non-public with [SerializeField]) of type string.
// Nothing here references UnityEditor, so the picking service and runtime tools can use it in a player too.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using GameCore.Studio.Model;
using UnityEngine;

namespace GameCore.Studio.Authoring
{
    /// <summary>One [AuthorField]/[AuthorRef] member of an authorable type, with its catalog spec.</summary>
    public sealed class AuthorMemberInfo
    {
        internal AuthorMemberInfo(MemberInfo member, Type valueType, bool serialized, AuthorFieldAttribute? field, AuthorRefAttribute? reference, FieldSpec spec)
        {
            Member = member;
            ValueType = valueType;
            IsSerializedField = serialized;
            Field = field;
            Reference = reference;
            Spec = spec;
        }

        /// <summary>Member name; for a serialized field this is also the SerializedProperty path.</summary>
        public string Name => Member.Name;

        public MemberInfo Member { get; }

        /// <summary>CLR type of the member.</summary>
        public Type ValueType { get; }

        /// <summary>True for a field Unity serializes (public, or [SerializeField]); false for properties.</summary>
        public bool IsSerializedField { get; }

        public AuthorFieldAttribute? Field { get; }

        public AuthorRefAttribute? Reference { get; }

        /// <summary>True for an [AuthorRef] member.</summary>
        public bool IsReference => Reference != null;

        /// <summary>The catalog field spec (03 s4), built with the model's value-type vocabulary.</summary>
        public FieldSpec Spec { get; }

        /// <summary>Element type for array/list members, else the member type.</summary>
        public Type ElementType
        {
            get
            {
                if (ValueType.IsArray)
                {
                    return ValueType.GetElementType() ?? ValueType;
                }

                if (ValueType.IsGenericType && ValueType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    return ValueType.GetGenericArguments()[0];
                }

                return ValueType;
            }
        }

        /// <summary>True for an array or List member.</summary>
        public bool IsCollection => ValueType != typeof(string) && (ValueType.IsArray || (ValueType.IsGenericType && ValueType.GetGenericTypeDefinition() == typeof(List<>)));

        /// <summary>Reads the member value by reflection.</summary>
        public object? GetValue(object target)
        {
            if (Member is FieldInfo field)
            {
                return field.GetValue(target);
            }

            return ((PropertyInfo)Member).GetValue(target);
        }

        /// <summary>Writes the member value by reflection (properties need a setter).</summary>
        public bool TrySetValue(object target, object? value)
        {
            if (Member is FieldInfo field)
            {
                field.SetValue(target, value);
                return true;
            }

            PropertyInfo property = (PropertyInfo)Member;
            if (!property.CanWrite)
            {
                return false;
            }

            property.SetValue(target, value);
            return true;
        }
    }

    /// <summary>What the Studio knows about one [Authorable] type.</summary>
    public sealed class AuthoringTypeInfo
    {
        private readonly Func<object, string?> _readId;

        internal AuthoringTypeInfo(Type type, AuthorableAttribute authorable, Func<object, string?> readId, FieldInfo? idField, IReadOnlyList<AuthorMemberInfo> members, PropertyInfo? capabilities)
        {
            Type = type;
            Authorable = authorable;
            _readId = readId;
            IdField = idField;
            Members = members;
            CapabilitiesProperty = capabilities;
        }

        public Type Type { get; }

        public AuthorableAttribute Authorable { get; }

        /// <summary>The stable object type id (e.g. <c>npc.definition</c>), the index node <c>type</c>.</summary>
        public string TypeId => Authorable.ObjectTypeId;

        /// <summary>The serialized <c>authoringId</c> field when the type has one (used to mint ids for new objects).</summary>
        public FieldInfo? IdField { get; }

        /// <summary>[AuthorField]/[AuthorRef] members, base class first, declaration order.</summary>
        public IReadOnlyList<AuthorMemberInfo> Members { get; }

        /// <summary>A public <c>IEnumerable&lt;string&gt; Capabilities</c> property, when the type declares capabilities.</summary>
        public PropertyInfo? CapabilitiesProperty { get; }

        public bool IsComponent => typeof(Component).IsAssignableFrom(Type);

        public bool IsScriptableObject => typeof(ScriptableObject).IsAssignableFrom(Type);

        /// <summary>The authoring id of <paramref name="instance"/>, or null when empty or unreadable.</summary>
        public string? ReadId(object instance)
        {
            string? id = _readId(instance);
            return string.IsNullOrEmpty(id) ? null : id;
        }

        /// <summary>The member named <paramref name="name"/>, or null.</summary>
        public AuthorMemberInfo? FindMember(string name)
        {
            for (int i = 0; i < Members.Count; i++)
            {
                if (string.Equals(Members[i].Name, name, StringComparison.Ordinal))
                {
                    return Members[i];
                }
            }

            return null;
        }

        /// <summary>The object type entry of the tool catalog (03 s4) including non-public serialized members.</summary>
        public ObjectTypeEntry ToObjectTypeEntry()
        {
            List<FieldSpec> fields = new List<FieldSpec>(Members.Count);
            for (int i = 0; i < Members.Count; i++)
            {
                fields.Add(Members[i].Spec);
            }

            return new ObjectTypeEntry(
                TypeId,
                Authorable.RuntimeApplicability,
                fields,
                Authorable.DisplayName,
                Authorable.Doc,
                ToolCatalogBuilder.ExpandScopes(Authorable.Scope));
        }
    }

    /// <summary>
    /// Duck-typed reader of authoring identity and metadata. Instances cache per-type reflection; there is no static
    /// mutable state, so each owner (picking service, Studio runtime) keeps its own instance.
    /// </summary>
    public sealed class AuthoringIdentity
    {
        /// <summary>Name of the interface matched in any assembly (this package's or gameplay.contracts').</summary>
        public const string AuthoredInterfaceName = "IAuthoredObject";

        /// <summary>Name of the serialized id field read when neither interface nor property is present.</summary>
        public const string AuthoringIdFieldName = "authoringId";

        public const string AuthoringIdPropertyName = "AuthoringId";

        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly Regex DefinitionName = new Regex("^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant);

        private static readonly Regex DefinitionRevision = new Regex("^[0-9A-Za-z]+$", RegexOptions.CultureInvariant);

        private readonly Dictionary<Type, AuthoringTypeInfo?> _cache = new Dictionary<Type, AuthoringTypeInfo?>();

        /// <summary>Forgets cached type metadata (after a code change).</summary>
        public void Invalidate()
        {
            _cache.Clear();
        }

        /// <summary>The metadata of <paramref name="type"/>, or null when the type has no [Authorable].</summary>
        public AuthoringTypeInfo? Describe(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (_cache.TryGetValue(type, out AuthoringTypeInfo? known))
            {
                return known;
            }

            AuthoringTypeInfo? info = Build(type);
            _cache[type] = info;
            return info;
        }

        /// <summary>The metadata of the object's runtime type, or null.</summary>
        public AuthoringTypeInfo? Describe(UnityEngine.Object? target)
        {
            if (target == null)
            {
                return null;
            }

            return Describe(target.GetType());
        }

        public bool IsAuthorable(UnityEngine.Object? target) => Describe(target) != null;

        /// <summary>The authoring id of an authored object, or null.</summary>
        public string? GetAuthoringId(UnityEngine.Object? target)
        {
            AuthoringTypeInfo? info = Describe(target);
            return info == null ? null : info.ReadId(target!);
        }

        /// <summary>The first authorable MonoBehaviour on <paramref name="gameObject"/>, or null.</summary>
        public MonoBehaviour? FindAuthoredComponent(GameObject? gameObject)
        {
            if (gameObject == null)
            {
                return null;
            }

            MonoBehaviour[] behaviours = gameObject.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != null && Describe(behaviours[i].GetType()) != null)
                {
                    return behaviours[i];
                }
            }

            return null;
        }

        /// <summary>
        /// The logical owner of a hit: the nearest authorable component on <paramref name="transform"/> or one of its
        /// parents (03 s2: subparts resolve to their logical owner), or null when nothing above is authored.
        /// </summary>
        public MonoBehaviour? FindLogicalOwner(Transform? transform)
        {
            for (Transform? current = transform; current != null; current = current.parent)
            {
                MonoBehaviour? owner = FindAuthoredComponent(current.gameObject);
                if (owner != null)
                {
                    return owner;
                }
            }

            return null;
        }

        /// <summary>Capability ids the object declares through a public <c>Capabilities</c> property.</summary>
        public IReadOnlyList<string>? GetCapabilities(UnityEngine.Object? target)
        {
            AuthoringTypeInfo? info = Describe(target);
            if (info?.CapabilitiesProperty == null)
            {
                return null;
            }

            object? value = info.CapabilitiesProperty.GetValue(target);
            if (!(value is IEnumerable<string> items))
            {
                return null;
            }

            List<string> capabilities = new List<string>();
            foreach (string item in items)
            {
                if (!string.IsNullOrEmpty(item) && !capabilities.Contains(item))
                {
                    capabilities.Add(item);
                }
            }

            capabilities.Sort(StringComparer.Ordinal);
            return capabilities.Count == 0 ? null : capabilities;
        }

        /// <summary>
        /// The DefinitionRef text (<c>name@revision</c>) of an object, read the IDefinitionAsset way by reflection:
        /// a <c>DefinitionRef</c> string property; else <c>Name</c> plus <c>Revision</c> (or the first 12 hex digits
        /// of <c>ContentStamp</c>); else, one level deep, the same on the value of a <c>definition</c> member (an
        /// entity pointing at its definition). Null when nothing valid is found.
        /// </summary>
        public string? GetDefinitionRef(object? target)
        {
            return GetDefinitionRef(target, 0);
        }

        private string? GetDefinitionRef(object? target, int depth)
        {
            if (target == null || (target is UnityEngine.Object unityObject && unityObject == null))
            {
                return null;
            }

            Type type = target.GetType();
            string? direct = ReadStringMember(target, type, "DefinitionRef");
            if (direct != null && IsDefinitionRef(direct))
            {
                return direct;
            }

            string? name = ReadStringMember(target, type, "Name");
            if (name != null && DefinitionName.IsMatch(name))
            {
                string? revision = ReadRevision(target, type);
                if (revision != null)
                {
                    return name + "@" + revision;
                }
            }

            if (depth > 0)
            {
                return null;
            }

            object? definition = ReadObjectMember(target, type, "definition") ?? ReadObjectMember(target, type, "Definition");
            return definition == null || ReferenceEquals(definition, target) ? null : GetDefinitionRef(definition, depth + 1);
        }

        /// <summary>True for text matching the DefinitionRef pattern.</summary>
        public static bool IsDefinitionRef(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            int at = text!.IndexOf('@');
            return at > 0 && text.IndexOf('@', at + 1) < 0
                && DefinitionName.IsMatch(text.Substring(0, at))
                && DefinitionRevision.IsMatch(text.Substring(at + 1));
        }

        private static string? ReadRevision(object target, Type type)
        {
            PropertyInfo? revision = type.GetProperty("Revision", BindingFlags.Instance | BindingFlags.Public);
            if (revision != null && revision.GetIndexParameters().Length == 0)
            {
                object? value = revision.GetValue(target);
                string? text = value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
                if (text != null && DefinitionRevision.IsMatch(text))
                {
                    return text;
                }
            }

            string? stamp = ReadStringMember(target, type, "ContentStamp");
            if (stamp != null && stamp.StartsWith(ContentStamp.Prefix, StringComparison.Ordinal) && stamp.Length >= ContentStamp.Prefix.Length + 12)
            {
                string hex = stamp.Substring(ContentStamp.Prefix.Length, 12);
                return DefinitionRevision.IsMatch(hex) ? hex : null;
            }

            return null;
        }

        private static string? ReadStringMember(object target, Type type, string name)
        {
            PropertyInfo? property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.PropertyType == typeof(string) && property.GetIndexParameters().Length == 0)
            {
                return property.GetValue(target) as string;
            }

            return null;
        }

        private static object? ReadObjectMember(object target, Type type, string name)
        {
            for (Type? current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                FieldInfo? field = current.GetField(name, InstanceMembers);
                if (field != null && !field.FieldType.IsValueType && field.FieldType != typeof(string))
                {
                    return field.GetValue(target);
                }

                PropertyInfo? property = current.GetProperty(name, InstanceMembers);
                if (property != null && property.CanRead && property.GetIndexParameters().Length == 0
                    && !property.PropertyType.IsValueType && property.PropertyType != typeof(string))
                {
                    return property.GetValue(target);
                }
            }

            return null;
        }

        private static AuthoringTypeInfo? Build(Type type)
        {
            AuthorableAttribute? authorable = AuthoringMetadata.Authorable(type);
            if (authorable == null)
            {
                return null;
            }

            FieldInfo? idField = FindIdField(type);
            Func<object, string?> readId = BuildIdReader(type, idField);
            List<AuthorMemberInfo> members = new List<AuthorMemberInfo>();
            List<Type> chain = new List<Type>();
            for (Type? current = type; current != null && current != typeof(MonoBehaviour) && current != typeof(ScriptableObject)
                && current != typeof(Component) && current != typeof(UnityEngine.Object) && current != typeof(object); current = current.BaseType)
            {
                chain.Insert(0, current);
            }

            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Type declaring in chain)
            {
                FieldInfo[] fields = declaring.GetFields(InstanceMembers);
                Array.Sort(fields, (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
                foreach (FieldInfo field in fields)
                {
                    AuthorMemberInfo? member = BuildMember(field, field.FieldType, IsUnitySerialized(field));
                    if (member != null && names.Add(member.Name))
                    {
                        members.Add(member);
                    }
                }

                PropertyInfo[] properties = declaring.GetProperties(InstanceMembers);
                Array.Sort(properties, (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
                foreach (PropertyInfo property in properties)
                {
                    if (property.GetIndexParameters().Length != 0 || !property.CanRead)
                    {
                        continue;
                    }

                    AuthorMemberInfo? member = BuildMember(property, property.PropertyType, false);
                    if (member != null && names.Add(member.Name))
                    {
                        members.Add(member);
                    }
                }
            }

            PropertyInfo? capabilities = type.GetProperty("Capabilities", BindingFlags.Instance | BindingFlags.Public);
            if (capabilities != null && (!typeof(IEnumerable<string>).IsAssignableFrom(capabilities.PropertyType) || capabilities.GetIndexParameters().Length != 0))
            {
                capabilities = null;
            }

            return new AuthoringTypeInfo(type, authorable, readId, idField, members, capabilities);
        }

        /// <summary>Unity's field serialization rule: public or [SerializeField], not static/readonly/[NonSerialized].</summary>
        public static bool IsUnitySerialized(FieldInfo field)
        {
            if (field.IsStatic || field.IsInitOnly || field.IsLiteral || field.IsNotSerialized)
            {
                return false;
            }

            return field.IsPublic || field.GetCustomAttribute<SerializeField>(true) != null;
        }

        private static AuthorMemberInfo? BuildMember(MemberInfo member, Type memberType, bool serialized)
        {
            AuthorFieldAttribute? field = AuthoringMetadata.Field(member);
            AuthorRefAttribute? reference = AuthoringMetadata.Reference(member);
            if (field == null && reference == null)
            {
                return null;
            }

            if (field != null && reference != null)
            {
                throw new InvalidOperationException(
                    "Member '" + member.DeclaringType?.FullName + "." + member.Name + "' has both [AuthorField] and [AuthorRef].");
            }

            FieldSpec spec;
            if (reference != null)
            {
                bool collection = memberType != typeof(string) && (memberType.IsArray || (memberType.IsGenericType && memberType.GetGenericTypeDefinition() == typeof(List<>)));
                Type element = collection ? (memberType.IsArray ? memberType.GetElementType()! : memberType.GetGenericArguments()[0]) : memberType;
                string? category = reference.Category ?? AuthoringMetadata.Authorable(element)?.ObjectTypeId;
                spec = new FieldSpec(member.Name, collection ? ValueTypes.Ref + ValueTypes.ArraySuffix : ValueTypes.Ref, reference.Required, category: category, doc: reference.Doc);
            }
            else
            {
                AuthorFieldAttribute value = field!;
                string type = ToolCatalogBuilder.MapValueType(memberType, out string? inferredCategory, out IReadOnlyList<string>? enumValues);
                if (value.Type != null)
                {
                    if (!ValueTypes.IsKnown(value.Type))
                    {
                        throw new InvalidOperationException("Member '" + member.Name + "' declares unknown value type '" + value.Type + "'.");
                    }

                    type = value.Type;
                }

                spec = new FieldSpec(
                    member.Name,
                    type,
                    value.Required,
                    value.Unit,
                    ToolCatalogBuilder.Number(value.Min),
                    ToolCatalogBuilder.Number(value.Max),
                    ToolCatalogBuilder.Number(value.Step),
                    inferredCategory,
                    value.Doc,
                    enumValues);
            }

            return new AuthorMemberInfo(member, memberType, serialized, field, reference, spec);
        }

        private static FieldInfo? FindIdField(Type type)
        {
            for (Type? current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                FieldInfo? field = current.GetField(AuthoringIdFieldName, InstanceMembers);
                if (field != null && field.FieldType == typeof(string) && IsUnitySerialized(field))
                {
                    return field;
                }
            }

            return null;
        }

        private static Func<object, string?> BuildIdReader(Type type, FieldInfo? idField)
        {
            foreach (Type candidate in type.GetInterfaces())
            {
                if (!string.Equals(candidate.Name, AuthoredInterfaceName, StringComparison.Ordinal))
                {
                    continue;
                }

                PropertyInfo? property = candidate.GetProperty(AuthoringIdPropertyName);
                if (property != null && property.PropertyType == typeof(string))
                {
                    return instance => property.GetValue(instance) as string;
                }
            }

            PropertyInfo? publicProperty = type.GetProperty(AuthoringIdPropertyName, BindingFlags.Instance | BindingFlags.Public);
            if (publicProperty != null && publicProperty.PropertyType == typeof(string) && publicProperty.GetIndexParameters().Length == 0)
            {
                return instance => publicProperty.GetValue(instance) as string;
            }

            if (idField != null)
            {
                return instance => idField.GetValue(instance) as string;
            }

            return instance => null;
        }

        /// <summary>A fresh authoring id: 32 lowercase hex digits (canonical stable-name text, 03 s1).</summary>
        public static string NewAuthoringId()
        {
            return Guid.NewGuid().ToString("N");
        }

        /// <summary>Values of a collection member as a list (empty for null).</summary>
        public static IReadOnlyList<object?> Items(object? collection)
        {
            List<object?> items = new List<object?>();
            if (collection is IEnumerable enumerable && !(collection is string))
            {
                foreach (object? item in enumerable)
                {
                    items.Add(item);
                }
            }

            return items;
        }
    }
}
