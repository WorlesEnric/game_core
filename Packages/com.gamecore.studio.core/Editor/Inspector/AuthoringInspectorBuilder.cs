// GameCore.Studio.Edit - inspector generator (docs/studio/03-authoring-contracts.md s4: "inspectors are generated from
// this metadata"). Builds a UI Toolkit inspector for an [Authorable] object from its member metadata (catalog type,
// unit, range, enum values, reference category). Controls are not bound to the SerializedObject: a commit (Enter, focus
// loss, toggle, pick) is checked against the catalog spec with the engine's own rules and then applied as a `set` or
// `assign` change set, so manual edits are journaled and undoable exactly like agent edits.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.Edit
{
    /// <summary>Generates metadata-driven inspectors.</summary>
    public sealed class AuthoringInspectorBuilder
    {
        public const string RootClass = "gamecore-studio-inspector";
        public const string ErrorClass = "gamecore-studio-inspector__error";

        private readonly AuthoringIdentity _identity;
        private readonly ValueCodec _codec;
        private readonly IManualEditSink _sink;

        public AuthoringInspectorBuilder(AuthoringIdentity identity, ValueCodec codec, IManualEditSink sink)
        {
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        /// <summary>The inspector of <paramref name="target"/>, or null when it is not [Authorable].</summary>
        public VisualElement? Build(UnityEngine.Object target)
        {
            AuthoringTypeInfo? info = _identity.Describe(target);
            if (info == null)
            {
                return null;
            }

            VisualElement root = new VisualElement();
            root.AddToClassList(RootClass);
            Label header = new Label(info.Authorable.DisplayName ?? info.TypeId);
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(header);
            string? id = info.ReadId(target);
            if (id != null)
            {
                Label idLabel = new Label("id " + id);
                idLabel.style.opacity = 0.6f;
                root.Add(idLabel);
            }

            foreach (AuthorMemberInfo member in info.Members)
            {
                root.Add(BuildRow(target, member));
            }

            return root;
        }

        private VisualElement BuildRow(UnityEngine.Object target, AuthorMemberInfo member)
        {
            VisualElement row = new VisualElement { name = "field-" + member.Name };
            Label error = new Label { name = "error-" + member.Name };
            error.AddToClassList(ErrorClass);
            error.style.color = new Color(0.9f, 0.3f, 0.3f);
            error.style.display = DisplayStyle.None;
            string label = Label(member);
            JToken current = _codec.FromClr(member.GetValue(target));
            bool writable = member.IsSerializedField || (member.Member is System.Reflection.PropertyInfo property && property.CanWrite);
            VisualElement control = Control(target, member, label, current, error);
            control.SetEnabled(writable);
            if (!string.IsNullOrEmpty(member.Spec.Doc))
            {
                control.tooltip = member.Spec.Doc;
            }

            row.Add(control);
            row.Add(error);
            return row;
        }

        private VisualElement Control(UnityEngine.Object target, AuthorMemberInfo member, string label, JToken current, Label error)
        {
            string type = member.Spec.Type;
            if (member.IsReference && !member.IsCollection)
            {
                ObjectField field = new ObjectField(label)
                {
                    objectType = typeof(UnityEngine.Object).IsAssignableFrom(member.ValueType) ? member.ValueType : typeof(UnityEngine.Object),
                    allowSceneObjects = true,
                    value = member.GetValue(target) as UnityEngine.Object,
                };
                field.RegisterValueChangedCallback(change => Commit(target, member, change.newValue == null ? JValue.CreateNull() : _codec.Refs.WriteRef(change.newValue), error));
                return field;
            }

            switch (type)
            {
                case ValueTypes.Bool:
                {
                    Toggle field = new Toggle(label) { value = current.Type == JTokenType.Boolean && current.Value<bool>() };
                    field.RegisterValueChangedCallback(change => Commit(target, member, new JValue(change.newValue), error));
                    return field;
                }

                case ValueTypes.Int:
                {
                    IntegerField field = new IntegerField(label) { isDelayed = true, value = ValueCodec.IsInteger(current) ? current.Value<int>() : 0 };
                    field.RegisterValueChangedCallback(change => Commit(target, member, new JValue(change.newValue), error));
                    return field;
                }

                case ValueTypes.Float:
                {
                    float value = ValueCodec.IsNumber(current) ? current.Value<float>() : 0f;
                    if (member.Spec.Min.HasValue && member.Spec.Max.HasValue)
                    {
                        Slider slider = new Slider(label, (float)member.Spec.Min.Value, (float)member.Spec.Max.Value) { value = value, showInputField = true };
                        slider.RegisterCallback<PointerCaptureOutEvent>(_ => Commit(target, member, new JValue(ValueCodec.Widen(slider.value)), error));
                        slider.RegisterCallback<FocusOutEvent>(_ => Commit(target, member, new JValue(ValueCodec.Widen(slider.value)), error));
                        return slider;
                    }

                    FloatField field = new FloatField(label) { isDelayed = true, value = value };
                    field.RegisterValueChangedCallback(change => Commit(target, member, new JValue(ValueCodec.Widen(change.newValue)), error));
                    return field;
                }

                case ValueTypes.String:
                {
                    TextField field = new TextField(label) { isDelayed = true, value = current.Type == JTokenType.String ? current.Value<string>() : string.Empty };
                    field.RegisterValueChangedCallback(change => Commit(target, member, new JValue(change.newValue), error));
                    return field;
                }

                case ValueTypes.Enum:
                {
                    List<string> choices = new List<string>(member.Spec.EnumValues ?? Array.Empty<string>());
                    string selected = current.Type == JTokenType.String ? current.Value<string>()! : (choices.Count > 0 ? choices[0] : string.Empty);
                    PopupField<string> field = new PopupField<string>(label, choices, Math.Max(0, choices.IndexOf(selected)));
                    field.RegisterValueChangedCallback(change => Commit(target, member, new JValue(change.newValue), error));
                    return field;
                }

                case ValueTypes.Vector2:
                {
                    Vector2Field field = new Vector2Field(label) { value = ToVector(current, 2) };
                    field.RegisterCallback<FocusOutEvent>(_ => Commit(target, member, new JArray(ValueCodec.Widen(field.value.x), ValueCodec.Widen(field.value.y)), error));
                    return field;
                }

                case ValueTypes.Vector3:
                {
                    Vector3Field field = new Vector3Field(label) { value = ToVector(current, 3) };
                    field.RegisterCallback<FocusOutEvent>(_ => Commit(target, member, new JArray(ValueCodec.Widen(field.value.x), ValueCodec.Widen(field.value.y), ValueCodec.Widen(field.value.z)), error));
                    return field;
                }

                case ValueTypes.Color:
                {
                    ColorField field = new ColorField(label) { value = ToColor(current) };
                    field.RegisterValueChangedCallback(change => Commit(target, member, new JValue("#" + ColorUtility.ToHtmlStringRGBA(change.newValue)), error));
                    return field;
                }

                default:
                {
                    // Lists, objects and other shapes: a read-only JSON view; edit them with `set` (fields) from tools.
                    TextField field = new TextField(label) { value = ValueCodec.Describe(current), isReadOnly = true, multiline = true };
                    return field;
                }
            }
        }

        private void Commit(UnityEngine.Object target, AuthorMemberInfo member, JToken value, Label error)
        {
            IReadOnlyList<string> problems = _sink.Check(member, value);
            if (problems.Count > 0)
            {
                error.text = string.Join("\n", problems);
                error.style.display = DisplayStyle.Flex;
                return;
            }

            ApplyReport? report = _sink.Commit(target, member, value);
            if (report != null && !report.Ok && report.Diagnostics.Count > 0)
            {
                error.text = report.Diagnostics[0].Code + ": " + report.Diagnostics[0].Message;
                error.style.display = DisplayStyle.Flex;
                return;
            }

            error.text = string.Empty;
            error.style.display = DisplayStyle.None;
        }

        private static string Label(AuthorMemberInfo member)
        {
            string name = UnityEditor.ObjectNames.NicifyVariableName(member.Name);
            return member.Spec.Unit == null ? name : name + " (" + member.Spec.Unit + ")";
        }

        private static Vector3 ToVector(JToken value, int size)
        {
            Vector3 result = Vector3.zero;
            if (value is JArray array)
            {
                for (int i = 0; i < Math.Min(size, array.Count); i++)
                {
                    if (ValueCodec.IsNumber(array[i]))
                    {
                        result[i] = array[i].Value<float>();
                    }
                }
            }

            return result;
        }

        private static Color ToColor(JToken value)
        {
            if (value.Type == JTokenType.String && ColorUtility.TryParseHtmlString(value.Value<string>(), out Color parsed))
            {
                return parsed;
            }

            return Color.white;
        }

        /// <summary>Invariant text of a number (labels).</summary>
        internal static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
