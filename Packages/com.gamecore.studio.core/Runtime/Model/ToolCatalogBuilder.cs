// GameCore.Studio.Model - reflects authoring metadata into a ToolCatalog (docs/studio/03-authoring-contracts.md s4).
// Unity-free: engine value types and engine object references are recognised by CLR name only (no compile-time
// reference), so the same builder runs in plain dotnet and inside the Editor.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;

namespace GameCore.Studio.Model
{
    /// <summary>Builds <see cref="ToolCatalog"/> entries from <c>[Authorable]</c> types and <c>[AuthorOperation]</c> methods.</summary>
    public sealed class ToolCatalogBuilder
    {
        private const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.Instance;

        private const BindingFlags OperationFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        private static readonly string[] EngineObjectBaseNames = { "UnityEngine.Object" };

        private readonly SortedDictionary<string, ObjectTypeEntry> _types = new SortedDictionary<string, ObjectTypeEntry>(StringComparer.Ordinal);

        private readonly SortedDictionary<string, ToolEntry> _tools = new SortedDictionary<string, ToolEntry>(StringComparer.Ordinal);

        private readonly HashSet<Type> _scannedTypes = new HashSet<Type>();

        /// <summary>Adds every type of <paramref name="assembly"/> (see <see cref="AddType"/>).</summary>
        public ToolCatalogBuilder AddAssembly(Assembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            Type[] types = LoadableTypes(assembly);
            Array.Sort(types, (left, right) => string.CompareOrdinal(left.FullName, right.FullName));
            foreach (Type type in types)
            {
                AddType(type);
            }

            return this;
        }

        /// <summary>
        /// The types of <paramref name="assembly"/> that could be loaded. A plugin assembly whose optional dependencies
        /// are missing throws <see cref="ReflectionTypeLoadException"/> from <c>GetTypes</c>; its loadable types are
        /// still scanned, and the ones that failed are skipped.
        /// </summary>
        public static Type[] LoadableTypes(Assembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException partial)
            {
                List<Type> loaded = new List<Type>();
                foreach (Type? type in partial.Types)
                {
                    if (type != null)
                    {
                        loaded.Add(type);
                    }
                }

                return loaded.ToArray();
            }
        }

        /// <summary>
        /// Adds the type's <c>[Authorable]</c> entry (when present) and every <c>[AuthorOperation]</c> method it declares.
        /// Adding the same type twice is a no-op.
        /// </summary>
        public ToolCatalogBuilder AddType(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (!_scannedTypes.Add(type))
            {
                return this;
            }

            AuthorableAttribute? authorable = type.GetCustomAttribute<AuthorableAttribute>(false);
            if (authorable != null)
            {
                AddObjectType(type, authorable);
            }

            MethodInfo[] methods = type.GetMethods(OperationFlags);
            Array.Sort(methods, (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
            foreach (MethodInfo method in methods)
            {
                if (method.GetCustomAttribute<AuthorOperationAttribute>(false) != null)
                {
                    AddMethod(method);
                }
            }

            return this;
        }

        /// <summary>Adds one <c>[AuthorOperation]</c> method as a tool.</summary>
        public ToolCatalogBuilder AddMethod(MethodInfo method)
        {
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }

            AuthorOperationAttribute? operation = method.GetCustomAttribute<AuthorOperationAttribute>(false);
            if (operation == null)
            {
                throw new ArgumentException(
                    "Method '" + method.DeclaringType?.FullName + "." + method.Name + "' has no [AuthorOperation].", nameof(method));
            }

            ToolEntry tool = BuildTool(method, operation);
            if (_tools.ContainsKey(tool.Id))
            {
                throw new InvalidOperationException("Tool '" + tool.Id + "' is declared more than once.");
            }

            _tools.Add(tool.Id, tool);
            return this;
        }

        /// <summary>The catalog of everything added so far, sorted by type id and tool id, with its revision minted.</summary>
        public ToolCatalog Build(string? plugin = null)
        {
            return new ToolCatalog(new List<ObjectTypeEntry>(_types.Values), new List<ToolEntry>(_tools.Values), plugin).WithRevision();
        }

        private void AddObjectType(Type type, AuthorableAttribute authorable)
        {
            if (!StableNameKeyIsUsable(authorable.ObjectTypeId))
            {
                throw new InvalidOperationException("Type id '" + authorable.ObjectTypeId + "' on " + type.FullName + " is empty.");
            }

            if (_types.ContainsKey(authorable.ObjectTypeId))
            {
                throw new InvalidOperationException("Object type '" + authorable.ObjectTypeId + "' is declared more than once.");
            }

            List<FieldSpec> fields = new List<FieldSpec>();
            FieldInfo[] fieldInfos = type.GetFields(MemberFlags);
            Array.Sort(fieldInfos, (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
            foreach (FieldInfo field in fieldInfos)
            {
                FieldSpec? spec = BuildField(field, field.FieldType);
                if (spec != null)
                {
                    fields.Add(spec);
                }
            }

            PropertyInfo[] properties = type.GetProperties(MemberFlags);
            Array.Sort(properties, (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
            foreach (PropertyInfo property in properties)
            {
                FieldSpec? spec = BuildField(property, property.PropertyType);
                if (spec != null)
                {
                    fields.Add(spec);
                }
            }

            _types.Add(authorable.ObjectTypeId, new ObjectTypeEntry(
                authorable.ObjectTypeId,
                authorable.RuntimeApplicability,
                fields,
                authorable.DisplayName,
                authorable.Doc,
                ExpandScopes(authorable.Scope)));
        }

        private static FieldSpec? BuildField(MemberInfo member, Type memberType)
        {
            AuthorFieldAttribute? field = member.GetCustomAttribute<AuthorFieldAttribute>(true);
            AuthorRefAttribute? reference = member.GetCustomAttribute<AuthorRefAttribute>(true);
            if (field == null && reference == null)
            {
                return null;
            }

            if (field != null && reference != null)
            {
                throw new InvalidOperationException(
                    "Member '" + member.DeclaringType?.FullName + "." + member.Name + "' has both [AuthorField] and [AuthorRef].");
            }

            if (reference != null)
            {
                string referenceType = IsCollection(memberType, out _) ? ValueTypes.Ref + ValueTypes.ArraySuffix : ValueTypes.Ref;
                string? category = reference.Category ?? AuthorableCategory(ElementType(memberType));
                return new FieldSpec(member.Name, referenceType, reference.Required, category: category, doc: reference.Doc, structural: reference.Structural);
            }

            AuthorFieldAttribute value = field!;
            string type = MapValueType(memberType, out string? inferredCategory, out IReadOnlyList<string>? enumValues);
            if (value.Type != null)
            {
                type = CheckedOverride(value.Type, member.Name);
            }

            return new FieldSpec(
                member.Name,
                type,
                value.Required,
                value.Unit,
                Number(value.Min),
                Number(value.Max),
                Number(value.Step),
                inferredCategory,
                value.Doc,
                enumValues,
                value.Structural);
        }

        private ToolEntry BuildTool(MethodInfo method, AuthorOperationAttribute operation)
        {
            if (!StableNameKeyIsUsable(operation.ToolId))
            {
                throw new InvalidOperationException("Tool id on " + method.Name + " is empty.");
            }

            AuthorableAttribute? targetAuthorable = null;
            List<ArgSpec> args = new List<ArgSpec>();
            HashSet<string> argNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                AuthorArgAttribute? arg = parameter.GetCustomAttribute<AuthorArgAttribute>(false);
                if (arg == null)
                {
                    AuthorableAttribute? authorable = parameter.ParameterType.GetCustomAttribute<AuthorableAttribute>(false);
                    if (authorable != null && targetAuthorable == null)
                    {
                        targetAuthorable = authorable;
                    }

                    continue;
                }

                string name = arg.Name ?? parameter.Name ?? throw new InvalidOperationException("An [AuthorArg] parameter has no name.");
                if (!argNames.Add(name))
                {
                    throw new InvalidOperationException("Tool '" + operation.ToolId + "' declares argument '" + name + "' twice.");
                }

                string type = MapValueType(parameter.ParameterType, out string? inferredCategory, out IReadOnlyList<string>? enumValues);
                if (arg.Type != null)
                {
                    type = CheckedOverride(arg.Type, name);
                }

                bool required = arg.Required && !parameter.IsOptional && Nullable.GetUnderlyingType(parameter.ParameterType) == null;
                args.Add(new ArgSpec(
                    name,
                    type,
                    required,
                    arg.Unit,
                    Number(arg.Min),
                    Number(arg.Max),
                    Number(arg.Step),
                    arg.Category ?? inferredCategory,
                    arg.Doc,
                    enumValues));
            }

            RuntimeApply apply = operation.RuntimeApplicability;
            if (targetAuthorable != null && targetAuthorable.RuntimeApplicability > apply)
            {
                apply = targetAuthorable.RuntimeApplicability;
            }

            AuthorScope scope = operation.Scope != 0 ? operation.Scope : (targetAuthorable != null ? targetAuthorable.Scope : 0);
            List<Prerequisite> prerequisites = new List<Prerequisite>();
            AddPrerequisites(prerequisites, operation.Requires, PrerequisiteSubject.Project);
            AddPrerequisites(prerequisites, operation.RequiresOnTarget, PrerequisiteSubject.Target);

            List<ValidatorRef>? validators = null;
            if (operation.Validator != null)
            {
                validators = new List<ValidatorRef> { BuildValidator(operation.Validator) };
            }

            return new ToolEntry(
                operation.ToolId,
                operation.Tier,
                apply,
                targetAuthorable != null,
                args,
                operation.Doc,
                targetAuthorable?.ObjectTypeId,
                operation.TargetKinds == null ? null : new List<AuthoringKind>(operation.TargetKinds),
                ExpandScopes(scope),
                prerequisites.Count == 0 ? null : prerequisites,
                validators);
        }

        private static ValidatorRef BuildValidator(Type validator)
        {
            AuthorValidatorAttribute? declared = validator.GetCustomAttribute<AuthorValidatorAttribute>(false);
            if (declared == null)
            {
                return new ValidatorRef(validator.FullName ?? validator.Name, new string[0]);
            }

            List<string> codes = new List<string>(declared.Codes ?? new string[0]);
            codes.Sort(StringComparer.Ordinal);
            return new ValidatorRef(declared.Id, codes);
        }

        private static void AddPrerequisites(List<Prerequisite> into, string? list, PrerequisiteSubject subject)
        {
            if (string.IsNullOrEmpty(list))
            {
                return;
            }

            foreach (string part in list!.Split(','))
            {
                string requires = part.Trim();
                if (requires.Length > 0)
                {
                    into.Add(new Prerequisite(requires, subject));
                }
            }
        }

        /// <summary>The flags of <paramref name="scope"/> as a list in declaration order, or null when unrestricted.</summary>
        public static IReadOnlyList<AuthorScope>? ExpandScopes(AuthorScope scope)
        {
            if (scope == 0)
            {
                return null;
            }

            List<AuthorScope> scopes = new List<AuthorScope>();
            foreach (AuthorScope flag in new[] { AuthorScope.Instance, AuthorScope.Prefab, AuthorScope.Definition, AuthorScope.Scope })
            {
                if ((scope & flag) == flag)
                {
                    scopes.Add(flag);
                }
            }

            return scopes;
        }

        /// <summary>
        /// Maps a CLR type to the <see cref="ValueTypes"/> vocabulary. Engine value types are recognised by name
        /// (Vector2/3/4, Quaternion, Color, Color32); <c>[Authorable]</c> types and engine object references map to
        /// <c>ref</c>; collections map to <c>element[]</c>; anything else is <c>object</c>.
        /// </summary>
        public static string MapValueType(Type type, out string? category, out IReadOnlyList<string>? enumValues)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            category = null;
            enumValues = null;
            Type? underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
            {
                type = underlying;
            }

            if (type != typeof(string) && IsCollection(type, out Type? element))
            {
                string elementType = MapValueType(element!, out category, out enumValues);
                return elementType + ValueTypes.ArraySuffix;
            }

            if (type == typeof(bool))
            {
                return ValueTypes.Bool;
            }

            if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
                || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong))
            {
                return ValueTypes.Int;
            }

            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            {
                return ValueTypes.Float;
            }

            if (type == typeof(string))
            {
                return ValueTypes.String;
            }

            if (type.IsEnum)
            {
                enumValues = EnumNames(type);
                return ValueTypes.Enum;
            }

            switch (type.Name)
            {
                case "Vector2":
                case "Vector2Int":
                    return ValueTypes.Vector2;
                case "Vector3":
                case "Vector3Int":
                    return ValueTypes.Vector3;
                case "Vector4":
                    return ValueTypes.Vector4;
                case "Quaternion":
                    return ValueTypes.Quaternion;
                case "Color":
                case "Color32":
                    return ValueTypes.Color;
            }

            string? authorableCategory = AuthorableCategory(type);
            if (authorableCategory != null)
            {
                category = authorableCategory;
                return ValueTypes.Ref;
            }

            if (DerivesFromEngineObject(type))
            {
                return ValueTypes.Ref;
            }

            return ValueTypes.Object;
        }

        private static IReadOnlyList<string> EnumNames(Type enumType)
        {
            List<string> names = new List<string>();
            FieldInfo[] fields = enumType.GetFields(BindingFlags.Public | BindingFlags.Static);
            Array.Sort(fields, (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
            foreach (FieldInfo field in fields)
            {
                EnumMemberAttribute? member = field.GetCustomAttribute<EnumMemberAttribute>(false);
                names.Add(member?.Value ?? field.Name);
            }

            return names;
        }

        private static string? AuthorableCategory(Type type)
        {
            return type.GetCustomAttribute<AuthorableAttribute>(false)?.ObjectTypeId;
        }

        private static Type ElementType(Type type)
        {
            return IsCollection(type, out Type? element) ? element! : type;
        }

        private static bool IsCollection(Type type, out Type? element)
        {
            element = null;
            if (type == typeof(string))
            {
                return false;
            }

            if (type.IsArray)
            {
                element = type.GetElementType();
                return element != null;
            }

            if (type.IsGenericType)
            {
                Type definition = type.GetGenericTypeDefinition();
                if (definition == typeof(List<>) || definition == typeof(IList<>) || definition == typeof(IReadOnlyList<>)
                    || definition == typeof(IEnumerable<>) || definition == typeof(ICollection<>) || definition == typeof(IReadOnlyCollection<>))
                {
                    element = type.GetGenericArguments()[0];
                    return true;
                }
            }

            return false;
        }

        private static bool DerivesFromEngineObject(Type type)
        {
            for (Type? current = type; current != null; current = current.BaseType)
            {
                foreach (string name in EngineObjectBaseNames)
                {
                    if (string.Equals(current.FullName, name, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Attribute numbers are doubles; a float literal (<c>0.1f</c>) widened at compile time is normalised back to
        /// its shortest float text so the catalog carries 0.1, not 0.100000001490116. NaN means unset.
        /// </summary>
        public static double? Number(double value)
        {
            if (double.IsNaN(value))
            {
                return null;
            }

            if (double.IsInfinity(value))
            {
                throw new InvalidOperationException("An authoring constraint cannot be infinite.");
            }

            float narrow = (float)value;
            if ((double)narrow == value)
            {
                return double.Parse(narrow.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            return value;
        }

        private static bool StableNameKeyIsUsable(string id) => !string.IsNullOrWhiteSpace(id);

        private static string CheckedOverride(string type, string member)
        {
            if (!ValueTypes.IsKnown(type))
            {
                throw new InvalidOperationException("Member '" + member + "' declares unknown value type '" + type + "'.");
            }

            return type;
        }
    }
}
