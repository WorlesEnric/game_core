// GameCore.Studio.Views - W-VIEW-05 model: a generic table over the index nodes of one [Authorable] type id.
// Columns are the type's catalog field specs ([AuthorField]/[AuthorRef] members); values come from the index
// projection (fields) and refs. Inline edits are parsed by the field spec, checked with FieldValueChecker (the engine's
// own rules) and committed as one `set {fields}` operation per row in an AllOrNothing change set; applying a value to
// several rows makes one change set with one operation per row. Also: filtering, sorting and CSV export.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GameCore.Studio.Views
{
    public sealed class TableColumn
    {
        public TableColumn(string name, FieldSpec? spec, bool editable)
        {
            Name = name;
            Spec = spec;
            Editable = editable;
        }

        public string Name { get; }

        public FieldSpec? Spec { get; }

        public bool Editable { get; }

        public string Title => Spec == null ? Name : Name + (Spec.Unit != null ? " (" + Spec.Unit + ")" : string.Empty);
    }

    public sealed class TableRow
    {
        public TableRow(string key, AuthoringRef? reference, string name)
        {
            Key = key;
            Ref = reference;
            Name = name;
        }

        public string Key { get; }

        public AuthoringRef? Ref { get; }

        public string Name { get; }

        /// <summary>Column name -> JSON value (refs as AuthoringRef JSON).</summary>
        public Dictionary<string, JToken?> Values { get; } = new Dictionary<string, JToken?>(StringComparer.Ordinal);

        /// <summary>Display texts by column name (cached).</summary>
        public Dictionary<string, string> Texts { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public string Text(string column) => Texts.TryGetValue(column, out string? text) ? text : string.Empty;
    }

    public sealed class TableModel
    {
        public const string NameColumn = "name";
        public const string LiveColumn = "live value";

        private readonly List<TableRow> _all;
        private readonly List<TableRow> _visible;
        private string _filter = string.Empty;
        private string? _sortColumn;
        private bool _ascending = true;

        public TableModel(string title, string? typeId, IReadOnlyList<TableColumn> columns, List<TableRow> rows)
        {
            Title = title;
            TypeId = typeId;
            Columns = columns;
            _all = rows;
            _visible = new List<TableRow>(rows);
        }

        public string Title { get; }

        /// <summary>The authorable type id (null for non-authorable tables such as save slots: read-only).</summary>
        public string? TypeId { get; }

        public IReadOnlyList<TableColumn> Columns { get; }

        public IReadOnlyList<TableRow> AllRows => _all;

        /// <summary>The filtered, sorted rows (the list view's source).</summary>
        public List<TableRow> Rows => _visible;

        public TableColumn? Column(string name)
        {
            foreach (TableColumn column in Columns)
            {
                if (string.Equals(column.Name, name, StringComparison.Ordinal))
                {
                    return column;
                }
            }

            return null;
        }

        /// <summary>A table of every index node of <paramref name="typeId"/>.</summary>
        public static TableModel ForType(ToolCatalog catalog, IndexGraph graph, string typeId, string? title = null)
        {
            List<TableColumn> columns = new List<TableColumn> { new TableColumn(NameColumn, null, false) };
            ObjectTypeEntry? type = null;
            foreach (ObjectTypeEntry entry in catalog.ObjectTypes)
            {
                if (string.Equals(entry.TypeId, typeId, StringComparison.Ordinal))
                {
                    type = entry;
                    break;
                }
            }

            if (type != null)
            {
                foreach (FieldSpec field in type.Fields)
                {
                    columns.Add(new TableColumn(field.Name, field, IsEditable(field)));
                }
            }

            List<TableRow> rows = new List<TableRow>();
            foreach (IndexNode node in graph.OfType(typeId))
            {
                TableRow row = new TableRow(node.Ref.IdentityKey, node.Ref, graph.NameOf(node.Ref.IdentityKey));
                row.Values[NameColumn] = row.Name;
                row.Texts[NameColumn] = row.Name;
                foreach (TableColumn column in columns)
                {
                    if (column.Spec == null)
                    {
                        continue;
                    }

                    JToken? value = IndexGraph.Field(node, column.Name);
                    if (value == null && ValueTypes.IsArray(column.Spec.Type) == false && column.Spec.Type == ValueTypes.Ref)
                    {
                        AuthoringRef? reference = IndexGraph.RefField(node, column.Name);
                        value = reference == null ? null : StudioJson.ToToken(reference);
                    }
                    else if (value == null && column.Spec.Type.StartsWith(ValueTypes.Ref, StringComparison.Ordinal))
                    {
                        JArray refs = new JArray();
                        foreach (AuthoringRef reference in IndexGraph.RefsField(node, column.Name))
                        {
                            refs.Add(StudioJson.ToToken(reference));
                        }

                        value = refs;
                    }

                    row.Values[column.Name] = value;
                    row.Texts[column.Name] = DisplayRefs(value, graph);
                }

                rows.Add(row);
            }

            return new TableModel(title ?? (type?.DisplayName ?? typeId), typeId, columns, rows);
        }

        /// <summary>Save slot headers: every *.json under <paramref name="folder"/>, top-level scalars as columns (read-only).</summary>
        public static TableModel SaveSlots(string folder)
        {
            List<TableRow> rows = new List<TableRow>();
            SortedSet<string> names = new SortedSet<string>(StringComparer.Ordinal);
            if (Directory.Exists(folder))
            {
                foreach (string path in Directory.GetFiles(folder, "*.json"))
                {
                    FileInfo info = new FileInfo(path);
                    TableRow row = new TableRow(path, null, Path.GetFileNameWithoutExtension(path));
                    Set(row, NameColumn, row.Name);
                    Set(row, "bytes", info.Length);
                    Set(row, "modified", info.LastWriteTimeUtc.ToString("u", CultureInfo.InvariantCulture));
                    try
                    {
                        using StreamReader reader = new StreamReader(path);
                        char[] buffer = new char[64 * 1024];
                        int read = reader.Read(buffer, 0, buffer.Length);
                        if (read < buffer.Length && JToken.Parse(new string(buffer, 0, read)) is JObject header)
                        {
                            foreach (JProperty property in header.Properties())
                            {
                                if (property.Value is JValue)
                                {
                                    Set(row, property.Name, property.Value);
                                    names.Add(property.Name);
                                }
                            }
                        }
                    }
                    catch (Exception error) when (error is IOException || error is Newtonsoft.Json.JsonException || error is UnauthorizedAccessException)
                    {
                        Set(row, "problem", error.Message);
                        names.Add("problem");
                    }

                    rows.Add(row);
                }
            }

            List<TableColumn> columns = new List<TableColumn> { new TableColumn(NameColumn, null, false), new TableColumn("bytes", null, false), new TableColumn("modified", null, false) };
            foreach (string name in names)
            {
                columns.Add(new TableColumn(name, null, false));
            }

            return new TableModel("Save slots", null, columns, rows);
        }

        public void Filter(string text)
        {
            _filter = text ?? string.Empty;
            Rebuild();
        }

        public void Sort(string column, bool ascending)
        {
            _sortColumn = column;
            _ascending = ascending;
            Rebuild();
        }

        /// <summary>Parses a cell text by the column's field spec and checks it with the engine's rules.</summary>
        public static bool TryParse(FieldSpec spec, string text, out JToken value, out string problem)
        {
            problem = string.Empty;
            value = JValue.CreateNull();
            string trimmed = (text ?? string.Empty).Trim();
            switch (spec.Type)
            {
                case ValueTypes.Bool:
                    if (trimmed == "1" || string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(trimmed, "yes", StringComparison.OrdinalIgnoreCase))
                    {
                        value = true;
                    }
                    else if (trimmed == "0" || string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase) || string.Equals(trimmed, "no", StringComparison.OrdinalIgnoreCase))
                    {
                        value = false;
                    }
                    else
                    {
                        problem = "'" + spec.Name + "' expects true or false";
                        return false;
                    }

                    break;
                case ValueTypes.Int:
                    if (!long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer))
                    {
                        problem = "'" + spec.Name + "' expects an integer";
                        return false;
                    }

                    value = integer;
                    break;
                case ValueTypes.Float:
                    if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                    {
                        problem = "'" + spec.Name + "' expects a number";
                        return false;
                    }

                    value = number;
                    break;
                case ValueTypes.Enum:
                    string? match = null;
                    foreach (string option in spec.EnumValues ?? Array.Empty<string>())
                    {
                        if (string.Equals(option, trimmed, StringComparison.OrdinalIgnoreCase))
                        {
                            match = option;
                        }
                    }

                    if (match == null)
                    {
                        problem = "'" + spec.Name + "' expects one of " + string.Join(", ", spec.EnumValues ?? Array.Empty<string>());
                        return false;
                    }

                    value = match;
                    break;
                case ValueTypes.Vector2:
                case ValueTypes.Vector3:
                case ValueTypes.Vector4:
                case ValueTypes.Quaternion:
                case ValueTypes.Color:
                    string[] parts = trimmed.Trim('(', ')', '[', ']').Split(',');
                    JArray vector = new JArray();
                    foreach (string part in parts)
                    {
                        if (!double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double component))
                        {
                            problem = "'" + spec.Name + "' expects numbers separated by commas";
                            return false;
                        }

                        vector.Add(component);
                    }

                    value = vector;
                    break;
                default:
                    value = text ?? string.Empty;
                    break;
            }

            IReadOnlyList<string> problems = FieldValueChecker.Check(spec, value);
            if (problems.Count > 0)
            {
                problem = problems[0];
                return false;
            }

            return true;
        }

        /// <summary>One row commit: one set op carrying every edited field of the row.</summary>
        public static ChangeSet CommitRow(TableRow row, JObject fields, string title)
        {
            if (row.Ref == null)
            {
                throw new InvalidOperationException("Row '" + row.Name + "' is not an authored object.");
            }

            return ViewEdits.Build("Table " + title + ": edit " + row.Name, new[] { ViewEdits.SetFieldsOp("op1", row.Ref, fields) });
        }

        /// <summary>One change set setting <paramref name="column"/> to <paramref name="value"/> on every row (N ops).</summary>
        public static ChangeSet ApplyToRows(IReadOnlyList<TableRow> rows, string column, JToken value, string title)
        {
            List<Operation> operations = new List<Operation>();
            foreach (TableRow row in rows)
            {
                if (row.Ref != null)
                {
                    operations.Add(ViewEdits.SetOp("op" + (operations.Count + 1).ToString(CultureInfo.InvariantCulture), row.Ref, column, value));
                }
            }

            return ViewEdits.Build("Table " + title + ": set " + column + " on " + operations.Count + " rows", operations);
        }

        /// <summary>The visible rows as CSV (RFC 4180; header = column names).</summary>
        public string ToCsv()
        {
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < Columns.Count; i++)
            {
                text.Append(i == 0 ? string.Empty : ",").Append(Csv(Columns[i].Name));
            }

            text.Append("\r\n");
            foreach (TableRow row in _visible)
            {
                for (int i = 0; i < Columns.Count; i++)
                {
                    text.Append(i == 0 ? string.Empty : ",").Append(Csv(row.Text(Columns[i].Name)));
                }

                text.Append("\r\n");
            }

            return text.ToString();
        }

        public static string Csv(string value)
        {
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public static bool IsEditable(FieldSpec field)
        {
            switch (field.Type)
            {
                case ValueTypes.Bool:
                case ValueTypes.Int:
                case ValueTypes.Float:
                case ValueTypes.String:
                case ValueTypes.Enum:
                case ValueTypes.Vector2:
                case ValueTypes.Vector3:
                case ValueTypes.Vector4:
                case ValueTypes.Color:
                case ValueTypes.Artifact:
                    return true;
                default:
                    return false;
            }
        }

        private void Rebuild()
        {
            _visible.Clear();
            foreach (TableRow row in _all)
            {
                if (_filter.Length == 0 || Matches(row, _filter))
                {
                    _visible.Add(row);
                }
            }

            if (_sortColumn != null)
            {
                string column = _sortColumn;
                int direction = _ascending ? 1 : -1;
                _visible.Sort((left, right) => direction * CompareCells(left.Text(column), right.Text(column)));
            }
        }

        private static bool Matches(TableRow row, string filter)
        {
            foreach (string text in row.Texts.Values)
            {
                if (text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareCells(string left, string right)
        {
            if (double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out double a) && double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out double b))
            {
                return a.CompareTo(b);
            }

            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static void Set(TableRow row, string column, JToken value)
        {
            row.Values[column] = value;
            row.Texts[column] = AuthoredData.Display(value);
        }

        private static string DisplayRefs(JToken? value, IndexGraph graph)
        {
            if (value is JArray array && array.Count > 0 && array[0] is JObject)
            {
                List<string> names = new List<string>();
                foreach (JToken item in array)
                {
                    names.Add(AuthoredData.Display(item, graph));
                }

                return string.Join(", ", names);
            }

            return AuthoredData.Display(value, graph);
        }
    }
}
