// GameCore.Studio.Authoring - authoring metadata read by attribute name and shape (docs/studio/03-authoring-contracts.md
// s4; P1.1 coordination note).
//
// The Model attributes (GameCore.Studio.Model.AuthorableAttribute, ...) are Editor-only, so runtime gameplay types
// cannot carry them. Gameplay packages declare a mirror set instead (GameCore.Gameplay.Contracts: same type names,
// property names, types, defaults and enum member names). Every Studio reader goes through this class: it returns the
// Model attribute when present and otherwise converts a mirror attribute (matched by type name, any namespace) into a
// Model attribute instance, enums by member name. Tool entries of mirror [AuthorOperation] methods are built here with
// the same rules as ToolCatalogBuilder.
#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using GameCore.Studio.Model;

namespace GameCore.Studio.Authoring
{
    /// <summary>Model or mirror authoring attributes, as Model attribute instances.</summary>
    public static class AuthoringMetadata
    {
        public const string AuthorableName = "AuthorableAttribute";
        public const string AuthorFieldName = "AuthorFieldAttribute";
        public const string AuthorRefName = "AuthorRefAttribute";
        public const string AuthorOperationName = "AuthorOperationAttribute";
        public const string AuthorArgName = "AuthorArgAttribute";
        public const string AuthorValidatorName = "AuthorValidatorAttribute";

        /// <summary>True for an attribute type named <paramref name="name"/> that is not the Model's own.</summary>
        public static bool IsMirror(Type attributeType, string name)
        {
            return attributeType != null && attributeType.Name == name && typeof(Attribute).IsAssignableFrom(attributeType)
                && attributeType.Assembly != typeof(AuthorableAttribute).Assembly;
        }

        public static AuthorableAttribute? Authorable(Type type)
        {
            if (type == null)
            {
                return null;
            }

            AuthorableAttribute? model = type.GetCustomAttribute<AuthorableAttribute>(false);
            if (model != null)
            {
                return model;
            }

            object? mirror = Mirror(type.GetCustomAttributes(false), AuthorableName);
            string? typeId = mirror == null ? null : Get<string>(mirror, "ObjectTypeId");
            if (mirror == null || string.IsNullOrEmpty(typeId))
            {
                return null;
            }

            return new AuthorableAttribute(typeId!)
            {
                DisplayName = Get<string>(mirror, "DisplayName"),
                Scope = EnumOf<AuthorScope>(Raw(mirror, "Scope")),
                RuntimeApplicability = EnumOf<RuntimeApply>(Raw(mirror, "RuntimeApplicability")),
                Doc = Get<string>(mirror, "Doc"),
            };
        }

        public static AuthorFieldAttribute? Field(MemberInfo member)
        {
            AuthorFieldAttribute? model = member.GetCustomAttribute<AuthorFieldAttribute>(true);
            if (model != null)
            {
                return model;
            }

            object? mirror = Mirror(member.GetCustomAttributes(true), AuthorFieldName);
            if (mirror == null)
            {
                return null;
            }

            return new AuthorFieldAttribute
            {
                Type = Get<string>(mirror, "Type"),
                Unit = Get<string>(mirror, "Unit"),
                Min = Number(mirror, "Min"),
                Max = Number(mirror, "Max"),
                Step = Number(mirror, "Step"),
                Doc = Get<string>(mirror, "Doc"),
                Required = Flag(mirror, "Required", false),
            };
        }

        public static AuthorRefAttribute? Reference(MemberInfo member)
        {
            AuthorRefAttribute? model = member.GetCustomAttribute<AuthorRefAttribute>(true);
            if (model != null)
            {
                return model;
            }

            object? mirror = Mirror(member.GetCustomAttributes(true), AuthorRefName);
            if (mirror == null)
            {
                return null;
            }

            return new AuthorRefAttribute
            {
                Category = Get<string>(mirror, "Category"),
                Required = Flag(mirror, "Required", true),
                Doc = Get<string>(mirror, "Doc"),
            };
        }

        public static AuthorOperationAttribute? Operation(MethodInfo method)
        {
            AuthorOperationAttribute? model = method.GetCustomAttribute<AuthorOperationAttribute>(false);
            if (model != null)
            {
                return model;
            }

            object? mirror = Mirror(method.GetCustomAttributes(false), AuthorOperationName);
            string? toolId = mirror == null ? null : Get<string>(mirror, "ToolId");
            if (mirror == null || string.IsNullOrEmpty(toolId))
            {
                return null;
            }

            AuthoringKind[]? kinds = null;
            if (Raw(mirror, "TargetKinds") is Array array)
            {
                kinds = new AuthoringKind[array.Length];
                for (int i = 0; i < array.Length; i++)
                {
                    kinds[i] = EnumOf<AuthoringKind>(array.GetValue(i));
                }
            }

            return new AuthorOperationAttribute(toolId!)
            {
                Doc = Get<string>(mirror, "Doc"),
                Validator = Get<Type>(mirror, "Validator"),
                Tier = EnumOf<ToolTier>(Raw(mirror, "Tier")),
                RuntimeApplicability = EnumOf<RuntimeApply>(Raw(mirror, "RuntimeApplicability")),
                Scope = EnumOf<AuthorScope>(Raw(mirror, "Scope")),
                Requires = Get<string>(mirror, "Requires"),
                RequiresOnTarget = Get<string>(mirror, "RequiresOnTarget"),
                TargetKinds = kinds,
            };
        }

        public static AuthorArgAttribute? Arg(ParameterInfo parameter)
        {
            AuthorArgAttribute? model = parameter.GetCustomAttribute<AuthorArgAttribute>(false);
            if (model != null)
            {
                return model;
            }

            object? mirror = Mirror(parameter.GetCustomAttributes(false), AuthorArgName);
            if (mirror == null)
            {
                return null;
            }

            return new AuthorArgAttribute
            {
                Name = Get<string>(mirror, "Name"),
                Type = Get<string>(mirror, "Type"),
                Unit = Get<string>(mirror, "Unit"),
                Min = Number(mirror, "Min"),
                Max = Number(mirror, "Max"),
                Step = Number(mirror, "Step"),
                Doc = Get<string>(mirror, "Doc"),
                Category = Get<string>(mirror, "Category"),
                Required = Flag(mirror, "Required", true),
            };
        }

        /// <summary>
        /// The tool entry of an [AuthorOperation] method (Model or mirror attributes), with ToolCatalogBuilder's rules:
        /// the first authorable parameter without [AuthorArg] is the target, [AuthorArg] parameters are arguments.
        /// </summary>
        public static ToolEntry BuildToolEntry(MethodInfo method)
        {
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }

            if (method.GetCustomAttribute<AuthorOperationAttribute>(false) != null)
            {
                return new ToolCatalogBuilder().AddMethod(method).Build().Tools[0];
            }

            AuthorOperationAttribute operation = Operation(method)
                ?? throw new ArgumentException("Method '" + method.DeclaringType?.FullName + "." + method.Name + "' has no [AuthorOperation].", nameof(method));
            AuthorableAttribute? targetAuthorable = null;
            List<ArgSpec> args = new List<ArgSpec>();
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                AuthorArgAttribute? arg = Arg(parameter);
                if (arg == null)
                {
                    AuthorableAttribute? authorable = Authorable(parameter.ParameterType);
                    if (authorable != null && targetAuthorable == null)
                    {
                        targetAuthorable = authorable;
                    }

                    continue;
                }

                string name = arg.Name ?? parameter.Name ?? throw new InvalidOperationException("An [AuthorArg] parameter has no name.");
                if (!names.Add(name))
                {
                    throw new InvalidOperationException("Tool '" + operation.ToolId + "' declares argument '" + name + "' twice.");
                }

                string type = ToolCatalogBuilder.MapValueType(parameter.ParameterType, out string? category, out IReadOnlyList<string>? enumValues);
                if (arg.Type != null)
                {
                    if (!ValueTypes.IsKnown(arg.Type))
                    {
                        throw new InvalidOperationException("Argument '" + name + "' declares unknown value type '" + arg.Type + "'.");
                    }

                    type = arg.Type;
                }

                if (category == null && ValueTypes.ElementOf(type) == ValueTypes.Ref)
                {
                    Type element = parameter.ParameterType.IsArray ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
                    category = Authorable(element)?.ObjectTypeId;
                }

                bool required = arg.Required && !parameter.IsOptional && Nullable.GetUnderlyingType(parameter.ParameterType) == null;
                args.Add(new ArgSpec(
                    name,
                    type,
                    required,
                    arg.Unit,
                    ToolCatalogBuilder.Number(arg.Min),
                    ToolCatalogBuilder.Number(arg.Max),
                    ToolCatalogBuilder.Number(arg.Step),
                    arg.Category ?? category,
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
                validators = new List<ValidatorRef> { Validator(operation.Validator) };
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
                ToolCatalogBuilder.ExpandScopes(scope),
                prerequisites.Count == 0 ? null : prerequisites,
                validators);
        }

        private static ValidatorRef Validator(Type validator)
        {
            AuthorValidatorAttribute? model = validator.GetCustomAttribute<AuthorValidatorAttribute>(false);
            if (model != null)
            {
                List<string> declared = new List<string>(model.Codes ?? new string[0]);
                declared.Sort(StringComparer.Ordinal);
                return new ValidatorRef(model.Id, declared);
            }

            object? mirror = Mirror(validator.GetCustomAttributes(false), AuthorValidatorName);
            string? id = mirror == null ? null : Get<string>(mirror, "Id");
            if (mirror == null || string.IsNullOrEmpty(id))
            {
                return new ValidatorRef(validator.FullName ?? validator.Name, new string[0]);
            }

            List<string> codes = new List<string>(Get<string[]>(mirror, "Codes") ?? new string[0]);
            codes.Sort(StringComparer.Ordinal);
            return new ValidatorRef(id!, codes);
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

        private static object? Mirror(object[] attributes, string name)
        {
            foreach (object attribute in attributes)
            {
                if (IsMirror(attribute.GetType(), name))
                {
                    return attribute;
                }
            }

            return null;
        }

        private static object? Raw(object source, string property)
        {
            PropertyInfo? info = source.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public);
            return info == null || !info.CanRead ? null : info.GetValue(source);
        }

        private static T? Get<T>(object source, string property)
            where T : class
        {
            return Raw(source, property) as T;
        }

        private static double Number(object source, string property)
        {
            object? value = Raw(source, property);
            return value is double number ? number : double.NaN;
        }

        private static bool Flag(object source, string property, bool fallback)
        {
            object? value = Raw(source, property);
            return value is bool flag ? flag : fallback;
        }

        /// <summary>A mirror enum value as the Model enum, by member name (flags by their comma-joined names).</summary>
        private static TEnum EnumOf<TEnum>(object? value)
            where TEnum : struct, Enum
        {
            if (value == null)
            {
                return default;
            }

            if (value is TEnum same)
            {
                return same;
            }

            if (Enum.TryParse(value.ToString(), false, out TEnum parsed))
            {
                return parsed;
            }

            try
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception error) when (error is InvalidCastException || error is FormatException || error is OverflowException)
            {
                return default;
            }
        }
    }
}
