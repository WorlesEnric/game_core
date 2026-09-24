// API snapshot generator for the W0 reference seam (GC-002). Tooling only: reflection over a compiled
// assembly is allowed here, and this project is never referenced by runtime code.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace GameCore.ApiSnapshot
{
    /// <summary>
    /// Produces a canonical, sorted public-API listing for one assembly and namespace. Types are sorted by
    /// full name (ordinal); members are grouped fields, constructors, properties, methods, events and sorted
    /// ordinally inside each group. The assembly name is deliberately excluded from every line so a rename
    /// is not reported as a surface change.
    /// </summary>
    public static class ApiSnapshotGenerator
    {
        /// <summary>Placeholder committed while nothing has been generated on a build host yet.</summary>
        public const string PendingHeader = "# snapshot pending generation on build host";

        private static readonly string[] KindOrder = { "field", "enumvalue", "ctor", "property", "method", "event" };
        private static readonly Dictionary<string, string> OperatorNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "op_Equality", "operator ==" },
            { "op_Inequality", "operator !=" },
            { "op_LessThan", "operator <" },
            { "op_GreaterThan", "operator >" },
            { "op_LessThanOrEqual", "operator <=" },
            { "op_GreaterThanOrEqual", "operator >=" },
            { "op_Addition", "operator +" },
            { "op_Subtraction", "operator -" },
            { "op_Implicit", "operator implicit" },
            { "op_Explicit", "operator explicit" },
        };


        public static string GenerateFromPath(string assemblyPath, string? namespaceFilter)
        {
            if (string.IsNullOrEmpty(assemblyPath))
            {
                throw new ArgumentException("An assembly path is required.", nameof(assemblyPath));
            }
            Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
            return Generate(assembly, namespaceFilter);
        }

        public static string Generate(Assembly assembly, string? namespaceFilter)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            List<Type> types = new List<Type>();
            foreach (Type type in assembly.GetExportedTypes())
            {
                if (Included(type.Namespace, namespaceFilter))
                {
                    types.Add(type);
                }
            }

            types.Sort(CompareTypes);

            int memberCount = 0;
            StringBuilder builder = new StringBuilder();
            builder.Append("# GameCore reference seam API snapshot\n");
            builder.Append("# source: ").Append(assembly.GetName().Name).Append('\n');
            builder.Append("# namespace filter: ").Append(namespaceFilter ?? "<all>").Append('\n');
            builder.Append("# type count: ").Append(types.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            builder.Append("# canonical form: types sorted by full name (ordinal); members grouped ").
                Append("fields, enum values, constructors, properties, methods, events and sorted ordinally; ")
                .Append("operators included, property accessors and inherited System.Object members omitted.\n");
            builder.Append('\n');

            foreach (Type type in types)
            {
                builder.Append(TypeHeader(type)).Append('\n');
                List<string> members = CollectMembers(type);
                members.Sort(CompareMemberLines);
                foreach (string member in members)
                {
                    builder.Append("  ").Append(member).Append('\n');
                    memberCount++;
                }
            }

            builder.Append("# member count: ").Append(memberCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
            return builder.ToString();
        }

        private static bool Included(string? ns, string? filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return true;
            }

            if (ns == null)
            {
                return false;
            }

            return string.Equals(ns, filter, StringComparison.Ordinal) ||
                   ns.StartsWith(filter + ".", StringComparison.Ordinal);
        }

        private static int CompareTypes(Type left, Type right) =>
            string.CompareOrdinal(left.FullName ?? left.Name, right.FullName ?? right.Name);

        private static int CompareMemberLines(string left, string right)
        {
            int leftRank = Rank(left);
            int rightRank = Rank(right);
            return leftRank != rightRank ? leftRank.CompareTo(rightRank) : string.CompareOrdinal(left, right);
        }

        private static int Rank(string line)
        {
            for (int i = 0; i < KindOrder.Length; i++)
            {
                if (line.StartsWith(KindOrder[i], StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return KindOrder.Length;
        }

        private static string TypeHeader(Type type)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("type ").Append(KindOf(type)).Append(' ').Append(Format(type, null));
            if (type.IsAbstract && type.IsSealed && !type.IsEnum && !type.IsValueType)
            {
                builder.Append(" [static]");
            }

            List<string> inherited = new List<string>();
            Type? baseType = type.BaseType;
            if (baseType != null &&
                baseType != typeof(object) &&
                baseType != typeof(ValueType) &&
                baseType != typeof(Enum) &&
                baseType != typeof(MulticastDelegate))
            {
                inherited.Add(Format(baseType, null));
            }

            Type[] interfaces = type.GetInterfaces();
            List<string> interfaceNames = new List<string>(interfaces.Length);
            foreach (Type implemented in interfaces)
            {
                interfaceNames.Add(Format(implemented, null));
            }

            interfaceNames.Sort(string.CompareOrdinal);
            inherited.AddRange(interfaceNames);
            if (inherited.Count != 0)
            {
                builder.Append(" : ").Append(string.Join(", ", inherited.ToArray()));
            }

            return builder.ToString();
        }

        private static List<string> CollectMembers(Type type)
        {
            List<string> members = new List<string>();
            NullabilityInfoContext nullability = new NullabilityInfoContext();

            const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            if (type.IsEnum)
            {
                foreach (FieldInfo literal in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    object? raw = literal.GetRawConstantValue();
                    string value = raw == null ? "?" : Convert.ToInt64(raw, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                    members.Add("enumvalue public " + literal.Name + " = " + value);
                }

                return members;
            }

            if (typeof(Delegate).IsAssignableFrom(type))
            {
                MethodInfo? invoke = type.GetMethod("Invoke");
                if (invoke != null)
                {
                    members.Add("method public " + Format(invoke.ReturnType, ReturnNullability(nullability, invoke)) + " " +
                                type.Name + "(" + FormatParameters(invoke, nullability) + ")");
                }

                return members;
            }

            foreach (FieldInfo field in type.GetFields(Declared))
            {
                if (!field.IsPublic)
                {
                    continue;
                }

                members.Add("field public " + Modifiers(field) + Format(field.FieldType, FieldNullability(nullability, field)) + " " + field.Name);
            }

            foreach (ConstructorInfo ctor in type.GetConstructors(Declared))
            {
                if (!ctor.IsPublic)
                {
                    continue;
                }

                members.Add("ctor public " + type.Name + "(" + FormatParameters(ctor, nullability) + ")");
            }

            foreach (PropertyInfo property in type.GetProperties(Declared))
            {
                MethodInfo? getter = property.GetMethod;
                MethodInfo? setter = property.SetMethod;
                bool publicGetter = getter != null && getter.IsPublic;
                bool publicSetter = setter != null && setter.IsPublic;
                if (!publicGetter && !publicSetter)
                {
                    continue;
                }

                string index = FormatIndexParameters(property, nullability);
                string accessors = publicGetter && publicSetter ? "{ get; set; }" : publicGetter ? "{ get; }" : "{ set; }";
                string modifiers = (publicGetter && getter!.IsStatic) || (publicSetter && setter!.IsStatic) ? "static " : string.Empty;
                members.Add("property public " + modifiers + Format(property.PropertyType, PropertyNullability(nullability, property)) +
                            " " + property.Name + index + " " + accessors);
            }

            foreach (MethodInfo method in type.GetMethods(Declared))
            {
                if (!method.IsPublic || method.IsConstructor)
                {
                    continue;
                }

                if (method.IsSpecialName && !method.Name.StartsWith("op_", StringComparison.Ordinal))
                {
                    continue;
                }

                string generic = string.Empty;
                if (method.IsGenericMethodDefinition)
                {
                    Type[] arguments = method.GetGenericArguments();
                    string[] names = new string[arguments.Length];
                    for (int i = 0; i < arguments.Length; i++)
                    {
                        names[i] = arguments[i].Name;
                    }

                    generic = "<" + string.Join(", ", names) + ">";
                }

                members.Add("method public " + (method.IsStatic ? "static " : string.Empty) +
                            Format(method.ReturnType, ReturnNullability(nullability, method)) + " " +
                            MemberName(method) + generic + "(" + FormatParameters(method, nullability) + ")");
            }

            foreach (EventInfo declaredEvent in type.GetEvents(Declared))
            {
                MethodInfo? adder = declaredEvent.AddMethod;
                if (adder == null || !adder.IsPublic)
                {
                    continue;
                }

                members.Add("event public " + Format(declaredEvent.EventHandlerType ?? typeof(void), null) + " " + declaredEvent.Name);
            }

            return members;
        }

        private static string MemberName(MethodInfo method) =>
            OperatorNames.TryGetValue(method.Name, out string? name) ? name : method.Name;

        private static string Modifiers(FieldInfo field)
        {
            StringBuilder builder = new StringBuilder();
            if (field.IsLiteral)
            {
                builder.Append("const ");
            }
            else if (field.IsStatic)
            {
                builder.Append("static ");
                if (field.IsInitOnly)
                {
                    builder.Append("readonly ");
                }
            }
            else if (field.IsInitOnly)
            {
                builder.Append("readonly ");
            }

            return builder.ToString();
        }

        private static string KindOf(Type type)
        {
            if (type.IsEnum)
            {
                return "enum";
            }

            if (type.IsValueType)
            {
                return "struct";
            }

            if (type.IsInterface)
            {
                return "interface";
            }

            if (typeof(Delegate).IsAssignableFrom(type))
            {
                return "delegate";
            }

            return "class";
        }

        private static string FormatIndexParameters(PropertyInfo property, NullabilityInfoContext nullability)
        {
            ParameterInfo[] parameters = property.GetIndexParameters();
            if (parameters.Length == 0)
            {
                return string.Empty;
            }

            return "[" + FormatParameters(parameters, nullability) + "]";
        }

        private static string FormatParameters(MethodBase method, NullabilityInfoContext nullability)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return FormatParameters(parameters, nullability);
        }

        private static string FormatParameters(ParameterInfo[] parameters, NullabilityInfoContext nullability)
        {
            string[] parts = new string[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];
                StringBuilder builder = new StringBuilder();
                if (parameter.IsDefined(typeof(ParamArrayAttribute), false))
                {
                    builder.Append("params ");
                }

                if (parameter.ParameterType.IsByRef)
                {
                    if (parameter.IsOut)
                    {
                        builder.Append("out ");
                    }
                    else if (parameter.IsIn)
                    {
                        builder.Append("in ");
                    }
                    else
                    {
                        builder.Append("ref ");
                    }
                }

                builder.Append(Format(parameter.ParameterType, ParameterNullability(nullability, parameter)));
                builder.Append(' ').Append(parameter.Name);
                if (parameter.HasDefaultValue)
                {
                    builder.Append(" = ").Append(FormatDefaultValue(parameter));
                }

                parts[i] = builder.ToString();
            }

            return string.Join(", ", parts);
        }

        private static string FormatDefaultValue(ParameterInfo parameter)
        {
            object? value = parameter.DefaultValue;
            if (value == null)
            {
                return "null";
            }

            Type type = parameter.ParameterType;
            if (type.IsEnum)
            {
                return Format(type, null) + "." + value.ToString();
            }

            if (value is bool flag)
            {
                return flag ? "true" : "false";
            }

            if (value is string text)
            {
                return "\"" + text + "\"";
            }

            if (value is char character)
            {
                return "'" + character + "'";
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
        }

        private static string Format(Type type, NullabilityState? annotation)
        {
            if (type.IsGenericParameter)
            {
                return type.Name;
            }

            if (type.IsByRef)
            {
                return Format(type.GetElementType() ?? type, annotation);
            }

            if (type.IsArray)
            {
                return Format(type.GetElementType() ?? type, annotation) + "[]";
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                return Format(type.GetGenericArguments()[0], null) + "?";
            }

            if (type.IsGenericType)
            {
                string name = type.GetGenericTypeDefinition().FullName ?? type.Name;
                int tick = name.IndexOf('`');
                if (tick >= 0)
                {
                    name = name.Substring(0, tick);
                }

                Type[] arguments = type.GetGenericArguments();
                string[] formatted = new string[arguments.Length];
                for (int i = 0; i < arguments.Length; i++)
                {
                    formatted[i] = Format(arguments[i], null);
                }

                name = name + "<" + string.Join(", ", formatted) + ">";
                return AppendAnnotation(name, type, annotation);
            }

            string plain = type.FullName ?? type.Name;
            return AppendAnnotation(plain, type, annotation);
        }

        private static string AppendAnnotation(string name, Type type, NullabilityState? annotation)
        {
            if (type.IsValueType || annotation != NullabilityState.Nullable)
            {
                return name;
            }

            return name + "?";
        }

        private static NullabilityState? FieldNullability(NullabilityInfoContext context, FieldInfo field)
        {
            try
            {
                return context.Create(field).ReadState;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static NullabilityState? PropertyNullability(NullabilityInfoContext context, PropertyInfo property)
        {
            try
            {
                return context.Create(property).ReadState;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static NullabilityState? ReturnNullability(NullabilityInfoContext context, MethodInfo method)
        {
            try
            {
                return context.Create(method.ReturnParameter).ReadState;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static NullabilityState? ParameterNullability(NullabilityInfoContext context, ParameterInfo parameter)
        {
            try
            {
                return context.Create(parameter).ReadState;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }
}
