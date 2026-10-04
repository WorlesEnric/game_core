// GameCore.Studio.Edit - JSON <-> Unity value conversion for tool arguments, index fields, content stamps and inverse
// operations (docs/studio/03-authoring-contracts.md s3, s4, s6; value vocabulary of GameCore.Studio.Model.ValueTypes).
//   bool/int/float/string     JSON scalars (floats are written with their shortest float text: 1.8f -> 1.8)
//   enum                      the member name (or its [EnumMember] value)
//   vector2/3/4, quaternion   [x, y(, z(, w))]
//   color                     [r, g, b, a] (read also as #rrggbb(aa))
//   ref                       an AuthoringRef object (written without stamp); read also as an authoring id or name@revision
//   arrays                    JSON arrays; other serializable structs JSON objects of their visible children
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Edit
{
    /// <summary>Writes object references as JSON and reads them back to objects.</summary>
    public interface IObjectRefCodec
    {
        /// <summary>The JSON form of a reference (an AuthoringRef object, or null).</summary>
        JToken WriteRef(UnityEngine.Object? target);

        /// <summary>The object a JSON reference points at, adapted to <paramref name="expected"/>; null with a problem when unresolvable.</summary>
        UnityEngine.Object? ReadRef(JToken value, Type expected, out string? problem);
    }

    /// <summary>Converts between JSON values and serialized properties / CLR values.</summary>
    public sealed class ValueCodec
    {
        public ValueCodec(IObjectRefCodec refs)
        {
            Refs = refs ?? throw new ArgumentNullException(nameof(refs));
        }

        public IObjectRefCodec Refs { get; }

        /// <summary>A float widened to its shortest round-trip double (1.8f becomes 1.8, not 1.79999995).</summary>
        public static double Widen(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0d;
            }

            return double.Parse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        /// <summary>The JSON value of an authorable member: from its serialized property when it has one, else by reflection.</summary>
        public JToken ReadMember(UnityEngine.Object target, AuthorMemberInfo member, SerializedObject? serialized = null)
        {
            if (member.IsSerializedField && member.ValueType.IsEnum == false)
            {
                SerializedObject so = serialized ?? new SerializedObject(target);
                SerializedProperty? property = so.FindProperty(member.Name);
                if (property != null)
                {
                    return FromProperty(property, member.ValueType);
                }
            }

            return FromClr(member.GetValue(target));
        }

        /// <summary>
        /// Writes an authorable member. Serialized fields go through <paramref name="serialized"/> (the caller applies it
        /// with or without undo); properties are set by reflection (the caller records undo first).
        /// </summary>
        public bool WriteMember(UnityEngine.Object target, AuthorMemberInfo member, JToken value, SerializedObject serialized, out string? problem)
        {
            if (member.IsSerializedField)
            {
                SerializedProperty? property = serialized.FindProperty(member.Name);
                if (property != null)
                {
                    return ToProperty(property, value, member.ValueType, out problem);
                }
            }

            if (!TryToClr(value, member.ValueType, out object? converted, out problem))
            {
                return false;
            }

            if (!member.TrySetValue(target, converted))
            {
                problem = "member '" + member.Name + "' is read-only";
                return false;
            }

            return true;
        }

        /// <summary>The JSON value of a serialized property; <paramref name="clrType"/> refines enums and references.</summary>
        public JToken FromProperty(SerializedProperty property, Type? clrType = null)
        {
            if (property.isArray && property.propertyType != SerializedPropertyType.String)
            {
                JArray array = new JArray();
                Type? element = ElementTypeOf(clrType);
                for (int i = 0; i < property.arraySize; i++)
                {
                    array.Add(FromProperty(property.GetArrayElementAtIndex(i), element));
                }

                return array;
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.ArraySize:
                    return new JValue(property.longValue);
                case SerializedPropertyType.Boolean:
                    return new JValue(property.boolValue);
                case SerializedPropertyType.Float:
                    return string.Equals(property.type, "double", StringComparison.Ordinal) ? new JValue(property.doubleValue) : new JValue(Widen(property.floatValue));
                case SerializedPropertyType.String:
                    return new JValue(property.stringValue ?? string.Empty);
                case SerializedPropertyType.Character:
                    return new JValue(((char)property.intValue).ToString());
                case SerializedPropertyType.Enum:
                    if (clrType != null && clrType.IsEnum)
                    {
                        return new JValue(EnumName(clrType, Enum.ToObject(clrType, property.intValue)));
                    }

                    string[] names = property.enumNames;
                    int index = property.enumValueIndex;
                    return index >= 0 && index < names.Length ? new JValue(names[index]) : new JValue(property.intValue);
                case SerializedPropertyType.Vector2:
                    return Vector(property.vector2Value.x, property.vector2Value.y);
                case SerializedPropertyType.Vector3:
                    return Vector(property.vector3Value.x, property.vector3Value.y, property.vector3Value.z);
                case SerializedPropertyType.Vector4:
                    return Vector(property.vector4Value.x, property.vector4Value.y, property.vector4Value.z, property.vector4Value.w);
                case SerializedPropertyType.Quaternion:
                    return Vector(property.quaternionValue.x, property.quaternionValue.y, property.quaternionValue.z, property.quaternionValue.w);
                case SerializedPropertyType.Color:
                    return Vector(property.colorValue.r, property.colorValue.g, property.colorValue.b, property.colorValue.a);
                case SerializedPropertyType.Vector2Int:
                    return new JArray(property.vector2IntValue.x, property.vector2IntValue.y);
                case SerializedPropertyType.Vector3Int:
                    return new JArray(property.vector3IntValue.x, property.vector3IntValue.y, property.vector3IntValue.z);
                case SerializedPropertyType.Rect:
                    return Vector(property.rectValue.x, property.rectValue.y, property.rectValue.width, property.rectValue.height);
                case SerializedPropertyType.Bounds:
                    return new JObject
                    {
                        ["center"] = Vector(property.boundsValue.center.x, property.boundsValue.center.y, property.boundsValue.center.z),
                        ["size"] = Vector(property.boundsValue.size.x, property.boundsValue.size.y, property.boundsValue.size.z),
                    };
                case SerializedPropertyType.ObjectReference:
                    return Refs.WriteRef(property.objectReferenceValue);
                case SerializedPropertyType.ManagedReference:
                    return new JValue(property.managedReferenceFullTypename ?? string.Empty);
                case SerializedPropertyType.Generic:
                    return Children(property, clrType);
                default:
                    return new JValue(property.type);
            }
        }

        /// <summary>Writes a JSON value into a serialized property (no apply); false with a problem on a type mismatch.</summary>
        public bool ToProperty(SerializedProperty property, JToken value, Type? clrType, out string? problem)
        {
            problem = null;
            if (property.isArray && property.propertyType != SerializedPropertyType.String)
            {
                if (value.Type == JTokenType.Null)
                {
                    property.arraySize = 0;
                    return true;
                }

                if (!(value is JArray array))
                {
                    problem = "expected an array, got " + Describe(value);
                    return false;
                }

                property.arraySize = array.Count;
                Type? element = ElementTypeOf(clrType);
                for (int i = 0; i < array.Count; i++)
                {
                    if (!ToProperty(property.GetArrayElementAtIndex(i), array[i], element, out problem))
                    {
                        problem = "[" + i.ToString(CultureInfo.InvariantCulture) + "] " + problem;
                        return false;
                    }
                }

                return true;
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                    if (!IsInteger(value))
                    {
                        problem = "expected an integer, got " + Describe(value);
                        return false;
                    }

                    property.longValue = value.Value<long>();
                    return true;
                case SerializedPropertyType.Boolean:
                    if (value.Type != JTokenType.Boolean)
                    {
                        problem = "expected a bool, got " + Describe(value);
                        return false;
                    }

                    property.boolValue = value.Value<bool>();
                    return true;
                case SerializedPropertyType.Float:
                    if (!IsNumber(value))
                    {
                        problem = "expected a number, got " + Describe(value);
                        return false;
                    }

                    if (string.Equals(property.type, "double", StringComparison.Ordinal))
                    {
                        property.doubleValue = value.Value<double>();
                    }
                    else
                    {
                        property.floatValue = (float)value.Value<double>();
                    }

                    return true;
                case SerializedPropertyType.String:
                    if (value.Type != JTokenType.String && value.Type != JTokenType.Null)
                    {
                        problem = "expected a string, got " + Describe(value);
                        return false;
                    }

                    property.stringValue = value.Type == JTokenType.Null ? string.Empty : value.Value<string>() ?? string.Empty;
                    return true;
                case SerializedPropertyType.Enum:
                    return WriteEnum(property, value, clrType, out problem);
                case SerializedPropertyType.Vector2:
                    if (!Floats(value, 2, 2, out float[] v2, out problem))
                    {
                        return false;
                    }

                    property.vector2Value = new Vector2(v2[0], v2[1]);
                    return true;
                case SerializedPropertyType.Vector3:
                    if (!Floats(value, 3, 3, out float[] v3, out problem))
                    {
                        return false;
                    }

                    property.vector3Value = new Vector3(v3[0], v3[1], v3[2]);
                    return true;
                case SerializedPropertyType.Vector4:
                    if (!Floats(value, 4, 4, out float[] v4, out problem))
                    {
                        return false;
                    }

                    property.vector4Value = new Vector4(v4[0], v4[1], v4[2], v4[3]);
                    return true;
                case SerializedPropertyType.Quaternion:
                    if (!Floats(value, 4, 4, out float[] q, out problem))
                    {
                        return false;
                    }

                    property.quaternionValue = new Quaternion(q[0], q[1], q[2], q[3]);
                    return true;
                case SerializedPropertyType.Color:
                    if (!TryColor(value, out Color color, out problem))
                    {
                        return false;
                    }

                    property.colorValue = color;
                    return true;
                case SerializedPropertyType.Vector2Int:
                    if (!Floats(value, 2, 2, out float[] i2, out problem))
                    {
                        return false;
                    }

                    property.vector2IntValue = new Vector2Int(Mathf.RoundToInt(i2[0]), Mathf.RoundToInt(i2[1]));
                    return true;
                case SerializedPropertyType.Vector3Int:
                    if (!Floats(value, 3, 3, out float[] i3, out problem))
                    {
                        return false;
                    }

                    property.vector3IntValue = new Vector3Int(Mathf.RoundToInt(i3[0]), Mathf.RoundToInt(i3[1]), Mathf.RoundToInt(i3[2]));
                    return true;
                case SerializedPropertyType.Rect:
                    if (!Floats(value, 4, 4, out float[] r, out problem))
                    {
                        return false;
                    }

                    property.rectValue = new Rect(r[0], r[1], r[2], r[3]);
                    return true;
                case SerializedPropertyType.ObjectReference:
                    if (value.Type == JTokenType.Null)
                    {
                        property.objectReferenceValue = null;
                        return true;
                    }

                    UnityEngine.Object? target = Refs.ReadRef(value, clrType != null && typeof(UnityEngine.Object).IsAssignableFrom(clrType) ? clrType : typeof(UnityEngine.Object), out problem);
                    if (target == null)
                    {
                        return false;
                    }

                    property.objectReferenceValue = target;
                    return true;
                case SerializedPropertyType.Generic:
                    if (!(value is JObject fields))
                    {
                        problem = "expected an object, got " + Describe(value);
                        return false;
                    }

                    foreach (JProperty field in fields.Properties())
                    {
                        SerializedProperty? child = property.FindPropertyRelative(field.Name);
                        if (child == null)
                        {
                            problem = "unknown member '" + field.Name + "'";
                            return false;
                        }

                        Type? childType = MemberTypeOf(clrType, field.Name);
                        if (!ToProperty(child, field.Value, childType, out problem))
                        {
                            problem = field.Name + ": " + problem;
                            return false;
                        }
                    }

                    return true;
                default:
                    problem = "properties of type " + property.propertyType.ToString() + " cannot be written by the Studio";
                    return false;
            }
        }

        /// <summary>The JSON value of a CLR value (reflection paths, inverse capture, tool outputs).</summary>
        public JToken FromClr(object? value)
        {
            if (value == null)
            {
                return JValue.CreateNull();
            }

            switch (value)
            {
                case string text:
                    return new JValue(text);
                case bool flag:
                    return new JValue(flag);
                case float single:
                    return new JValue(Widen(single));
                case double number:
                    return new JValue(number);
                case decimal exact:
                    return new JValue(exact);
                case byte or sbyte or short or ushort or int or uint or long:
                    return new JValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                case ulong large:
                    return new JValue(large);
                case char character:
                    return new JValue(character.ToString());
                case Enum enumValue:
                    return new JValue(EnumName(enumValue.GetType(), enumValue));
                case Vector2 v2:
                    return Vector(v2.x, v2.y);
                case Vector3 v3:
                    return Vector(v3.x, v3.y, v3.z);
                case Vector4 v4:
                    return Vector(v4.x, v4.y, v4.z, v4.w);
                case Quaternion q:
                    return Vector(q.x, q.y, q.z, q.w);
                case Color c:
                    return Vector(c.r, c.g, c.b, c.a);
                case Color32 c32:
                    Color widened = c32;
                    return Vector(widened.r, widened.g, widened.b, widened.a);
                case Vector2Int i2:
                    return new JArray(i2.x, i2.y);
                case Vector3Int i3:
                    return new JArray(i3.x, i3.y, i3.z);
                case Rect rect:
                    return Vector(rect.x, rect.y, rect.width, rect.height);
                case UnityEngine.Object unityObject:
                    return Refs.WriteRef(unityObject == null ? null : unityObject);
                case AuthoringRef reference:
                    return StudioJson.ToToken(reference);
                case JToken token:
                    return token.DeepClone();
                case IEnumerable enumerable:
                    JArray array = new JArray();
                    foreach (object? item in enumerable)
                    {
                        array.Add(FromClr(item));
                    }

                    return array;
            }

            try
            {
                return JToken.FromObject(value, JsonSerializer.Create(StudioJson.CreateSettings()));
            }
            catch (JsonException)
            {
                return new JValue(value.ToString());
            }
        }

        /// <summary>Converts a JSON value to <paramref name="type"/> (method arguments, reflected properties).</summary>
        public bool TryToClr(JToken value, Type type, out object? result, out string? problem)
        {
            result = null;
            problem = null;
            Type? underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
            {
                if (value.Type == JTokenType.Null)
                {
                    return true;
                }

                type = underlying;
            }

            if (typeof(JToken).IsAssignableFrom(type))
            {
                if (!type.IsInstanceOfType(value))
                {
                    problem = "expected " + type.Name + ", got " + Describe(value);
                    return false;
                }

                result = value.DeepClone();
                return true;
            }

            if (value.Type == JTokenType.Null)
            {
                if (type.IsValueType)
                {
                    problem = "a " + type.Name + " cannot be null";
                    return false;
                }

                return true;
            }

            if (type == typeof(string))
            {
                if (value.Type != JTokenType.String)
                {
                    problem = "expected a string, got " + Describe(value);
                    return false;
                }

                result = value.Value<string>();
                return true;
            }

            if (type == typeof(bool))
            {
                if (value.Type != JTokenType.Boolean)
                {
                    problem = "expected a bool, got " + Describe(value);
                    return false;
                }

                result = value.Value<bool>();
                return true;
            }

            if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
                || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte))
            {
                if (!IsInteger(value))
                {
                    problem = "expected an integer, got " + Describe(value);
                    return false;
                }

                try
                {
                    result = Convert.ChangeType(value.Value<long>(), type, CultureInfo.InvariantCulture);
                }
                catch (OverflowException)
                {
                    problem = "integer out of range for " + type.Name;
                    return false;
                }

                return true;
            }

            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            {
                if (!IsNumber(value))
                {
                    problem = "expected a number, got " + Describe(value);
                    return false;
                }

                result = Convert.ChangeType(value.Value<double>(), type, CultureInfo.InvariantCulture);
                return true;
            }

            if (type.IsEnum)
            {
                return TryEnum(value, type, out result, out problem);
            }

            if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) || type == typeof(Quaternion)
                || type == typeof(Vector2Int) || type == typeof(Vector3Int) || type == typeof(Rect))
            {
                int count = type == typeof(Vector2) || type == typeof(Vector2Int) ? 2 : (type == typeof(Vector3) || type == typeof(Vector3Int) ? 3 : 4);
                if (!Floats(value, count, count, out float[] f, out problem))
                {
                    return false;
                }

                if (type == typeof(Vector2))
                {
                    result = new Vector2(f[0], f[1]);
                }
                else if (type == typeof(Vector3))
                {
                    result = new Vector3(f[0], f[1], f[2]);
                }
                else if (type == typeof(Vector4))
                {
                    result = new Vector4(f[0], f[1], f[2], f[3]);
                }
                else if (type == typeof(Quaternion))
                {
                    result = new Quaternion(f[0], f[1], f[2], f[3]);
                }
                else if (type == typeof(Vector2Int))
                {
                    result = new Vector2Int(Mathf.RoundToInt(f[0]), Mathf.RoundToInt(f[1]));
                }
                else if (type == typeof(Vector3Int))
                {
                    result = new Vector3Int(Mathf.RoundToInt(f[0]), Mathf.RoundToInt(f[1]), Mathf.RoundToInt(f[2]));
                }
                else
                {
                    result = new Rect(f[0], f[1], f[2], f[3]);
                }

                return true;
            }

            if (type == typeof(Color) || type == typeof(Color32))
            {
                if (!TryColor(value, out Color color, out problem))
                {
                    return false;
                }

                result = type == typeof(Color) ? (object)color : (Color32)color;
                return true;
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                UnityEngine.Object? target = Refs.ReadRef(value, type, out problem);
                result = target;
                return target != null;
            }

            if (type == typeof(AuthoringRef))
            {
                try
                {
                    result = value.ToObject<AuthoringRef>(JsonSerializer.Create(StudioJson.CreateSettings()));
                    return result != null;
                }
                catch (JsonException error)
                {
                    problem = "not an AuthoringRef: " + error.Message;
                    return false;
                }
            }

            if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)))
            {
                if (!(value is JArray items))
                {
                    problem = "expected an array, got " + Describe(value);
                    return false;
                }

                Type element = type.IsArray ? type.GetElementType()! : type.GetGenericArguments()[0];
                IList list = type.IsArray ? Array.CreateInstance(element, items.Count) : (IList)Activator.CreateInstance(type)!;
                for (int i = 0; i < items.Count; i++)
                {
                    if (!TryToClr(items[i], element, out object? item, out problem))
                    {
                        problem = "[" + i.ToString(CultureInfo.InvariantCulture) + "] " + problem;
                        return false;
                    }

                    if (type.IsArray)
                    {
                        list[i] = item;
                    }
                    else
                    {
                        list.Add(item);
                    }
                }

                result = list;
                return true;
            }

            try
            {
                result = value.ToObject(type, JsonSerializer.Create(StudioJson.CreateSettings()));
                return true;
            }
            catch (Exception error) when (error is JsonException || error is ArgumentException || error is InvalidCastException)
            {
                problem = "cannot convert " + Describe(value) + " to " + type.Name + ": " + error.Message;
                return false;
            }
        }

        /// <summary>The JSON spelling of an enum value (its [EnumMember] value or name).</summary>
        public static string EnumName(Type enumType, object value)
        {
            string? name = Enum.GetName(enumType, value);
            if (name == null)
            {
                return Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            }

            FieldInfo? field = enumType.GetField(name, BindingFlags.Public | BindingFlags.Static);
            EnumMemberAttribute? member = field?.GetCustomAttribute<EnumMemberAttribute>(false);
            return member?.Value ?? name;
        }

        public static string Describe(JToken value)
        {
            switch (value.Type)
            {
                case JTokenType.Null:
                    return "null";
                case JTokenType.String:
                    return "string";
                case JTokenType.Integer:
                    return "integer";
                case JTokenType.Float:
                    return "number";
                case JTokenType.Boolean:
                    return "bool";
                case JTokenType.Array:
                    return "array";
                case JTokenType.Object:
                    return "object";
                default:
                    return value.Type.ToString();
            }
        }

        public static bool IsNumber(JToken value) => value.Type == JTokenType.Integer || value.Type == JTokenType.Float;

        public static bool IsInteger(JToken value)
        {
            if (value.Type == JTokenType.Integer)
            {
                return true;
            }

            if (value.Type == JTokenType.Float)
            {
                double number = value.Value<double>();
                return Math.Abs(number - Math.Round(number)) < 1e-9;
            }

            return false;
        }

        private JToken Children(SerializedProperty property, Type? clrType)
        {
            JObject result = new JObject();
            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            bool enter = true;
            while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
            {
                enter = false;
                result[child.name] = FromProperty(child, MemberTypeOf(clrType, child.name));
            }

            return result;
        }

        private static JArray Vector(params float[] components)
        {
            JArray array = new JArray();
            foreach (float component in components)
            {
                array.Add(Widen(component));
            }

            return array;
        }

        private static bool Floats(JToken value, int min, int max, out float[] result, out string? problem)
        {
            result = Array.Empty<float>();
            problem = null;
            if (!(value is JArray array) || array.Count < min || array.Count > max)
            {
                problem = "expected an array of " + (min == max ? min.ToString(CultureInfo.InvariantCulture) : min.ToString(CultureInfo.InvariantCulture) + ".." + max.ToString(CultureInfo.InvariantCulture)) + " numbers, got " + Describe(value);
                return false;
            }

            result = new float[array.Count];
            for (int i = 0; i < array.Count; i++)
            {
                if (!IsNumber(array[i]))
                {
                    problem = "component " + i.ToString(CultureInfo.InvariantCulture) + " is not a number";
                    return false;
                }

                result[i] = (float)array[i].Value<double>();
            }

            return true;
        }

        private static bool TryColor(JToken value, out Color color, out string? problem)
        {
            color = Color.white;
            problem = null;
            if (value.Type == JTokenType.String)
            {
                if (ColorUtility.TryParseHtmlString(value.Value<string>() ?? string.Empty, out color))
                {
                    return true;
                }

                problem = "not a #rrggbb(aa) color";
                return false;
            }

            if (!Floats(value, 3, 4, out float[] c, out problem))
            {
                return false;
            }

            color = new Color(c[0], c[1], c[2], c.Length == 4 ? c[3] : 1f);
            return true;
        }

        private static bool TryEnum(JToken value, Type enumType, out object? result, out string? problem)
        {
            result = null;
            problem = null;
            if (value.Type == JTokenType.Integer)
            {
                result = Enum.ToObject(enumType, value.Value<long>());
                return true;
            }

            if (value.Type != JTokenType.String)
            {
                problem = "expected an enum name, got " + Describe(value);
                return false;
            }

            string text = value.Value<string>() ?? string.Empty;
            foreach (FieldInfo field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                EnumMemberAttribute? member = field.GetCustomAttribute<EnumMemberAttribute>(false);
                if (string.Equals(member?.Value ?? field.Name, text, StringComparison.Ordinal) || string.Equals(field.Name, text, StringComparison.Ordinal))
                {
                    result = field.GetValue(null);
                    return true;
                }
            }

            problem = "'" + text + "' is not a " + enumType.Name + " value";
            return false;
        }

        private static bool WriteEnum(SerializedProperty property, JToken value, Type? clrType, out string? problem)
        {
            if (clrType != null && clrType.IsEnum)
            {
                if (!TryEnum(value, clrType, out object? converted, out problem))
                {
                    return false;
                }

                property.intValue = Convert.ToInt32(converted, CultureInfo.InvariantCulture);
                return true;
            }

            problem = null;
            if (value.Type == JTokenType.Integer)
            {
                property.intValue = value.Value<int>();
                return true;
            }

            string text = value.Value<string>() ?? string.Empty;
            string[] names = property.enumNames;
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], text, StringComparison.Ordinal))
                {
                    property.enumValueIndex = i;
                    return true;
                }
            }

            problem = "'" + text + "' is not one of " + string.Join(", ", names);
            return false;
        }

        private static Type? ElementTypeOf(Type? type)
        {
            if (type == null)
            {
                return null;
            }

            if (type.IsArray)
            {
                return type.GetElementType();
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                return type.GetGenericArguments()[0];
            }

            return null;
        }

        private static Type? MemberTypeOf(Type? type, string name)
        {
            for (Type? current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                FieldInfo? field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field.FieldType;
                }
            }

            return null;
        }
    }
}
