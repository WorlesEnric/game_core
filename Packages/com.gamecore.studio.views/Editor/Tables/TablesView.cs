// GameCore.Studio.Views - W-VIEW-05 Tables (GameCore/Studio/Tables, F11 / SR-11.x).
// A virtualised MultiColumnListView over a TableModel: tabs for entities, definitions, items, facts (with the running
// world's current values in Play Mode) and save slots, plus any [Authorable] type id. Columns sort (click a header) and
// the search field filters. Editable cells stage values; Enter (or moving to another row) commits the row as one set
// change set. "Apply to selected" sets one column on every selected row in one change set (one op per row). CSV
// export writes the visible rows.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Studio.Views.Canvas;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.Views
{
    public sealed class TablesView : StudioViewBase
    {
        public const string EntitiesTab = "Entities";
        public const string DefinitionsTab = "Definitions";
        public const string ItemsTab = "Items";
        public const string FactsTab = "Facts";
        public const string SavesTab = "Save slots";
        public const string AnyTab = "Any type";

        private static readonly IReadOnlyList<string> Tabs = new[] { EntitiesTab, DefinitionsTab, ItemsTab, FactsTab, SavesTab, AnyTab };

        private readonly Dictionary<string, ToolbarToggle> _tabToggles = new Dictionary<string, ToolbarToggle>(StringComparer.Ordinal);
        private readonly PopupField<string> _typePicker;
        private readonly VisualElement _host;
        private readonly PopupField<string> _bulkColumn;
        private readonly TextField _bulkValue;
        private readonly Dictionary<string, JObject> _pending = new Dictionary<string, JObject>(StringComparer.Ordinal);
        private MultiColumnListView? _list;
        private string _tab = EntitiesTab;
        private string _anyType = "entity.definition";
        private string _filter = string.Empty;
        private string? _lastRow;

        public TablesView(StudioViewContext context)
            : base(context, StudioViewIds.Tables)
        {
            foreach (string tab in Tabs)
            {
                ToolbarToggle toggle = new ToolbarToggle { text = tab, value = tab == _tab };
                string name = tab;
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                    {
                        ShowTab(name);
                    }
                    else if (_tab == name)
                    {
                        toggle.SetValueWithoutNotify(true);
                    }
                });
                _tabToggles[tab] = toggle;
                Toolbar.Add(toggle);
            }

            _typePicker = new PopupField<string>(new List<string> { _anyType }, 0);
            _typePicker.style.width = 180f;
            _typePicker.RegisterValueChangedCallback(evt =>
            {
                _anyType = evt.newValue;
                ShowTab(AnyTab);
            });
            Toolbar.Add(_typePicker);
            AddSpacer();
            AddSearch(text =>
            {
                _filter = text;
                Model?.Filter(text);
                _list?.RefreshItems();
            });
            AddButton("CSV", () =>
            {
                if (Model != null)
                {
                    ViewSupport.Export("Export table", Model.Title + ".csv", "csv", Model.ToCsv());
                }
            }, "Export the visible rows as CSV (also copied to the clipboard).");
            VisualElement column = new VisualElement();
            column.style.flexGrow = 1f;
            Toolbar bulk = new Toolbar();
            bulk.Add(new Label("Apply to selected rows:") { style = { unityTextAlign = TextAnchor.MiddleLeft, marginLeft = 4f } });
            _bulkColumn = new PopupField<string>(new List<string> { "-" }, 0);
            _bulkColumn.style.width = 160f;
            bulk.Add(_bulkColumn);
            _bulkValue = new TextField();
            _bulkValue.style.width = 160f;
            bulk.Add(_bulkValue);
            bulk.Add(new ToolbarButton(() => ApplyToSelection(_bulkColumn.value, _bulkValue.value)) { text = "Apply" });
            column.Add(bulk);
            _host = new VisualElement();
            _host.style.flexGrow = 1f;
            column.Add(_host);
            Body.Add(column);
        }

        public TableModel? Model { get; private set; }

        public string CurrentTab => _tab;

        public MultiColumnListView? List => _list;

        /// <summary>Rows with staged, uncommitted values.</summary>
        public int PendingRows => _pending.Count;

        public void ShowTab(string tab, string? anyType = null)
        {
            CommitPending();
            _tab = tab;
            if (anyType != null)
            {
                _anyType = anyType;
            }

            foreach (KeyValuePair<string, ToolbarToggle> pair in _tabToggles)
            {
                pair.Value.SetValueWithoutNotify(pair.Key == tab);
            }

            Refresh();
        }

        /// <summary>Stages a cell value (parsed and checked by the column's field spec); false with a status on a problem.</summary>
        public bool Stage(TableRow row, string column, string text)
        {
            TableColumn? spec = Model?.Column(column);
            if (spec?.Spec == null || !spec.Editable || row.Ref == null)
            {
                return false;
            }

            if (!TableModel.TryParse(spec.Spec, text, out JToken value, out string problem))
            {
                SetStatus(problem, ViewPalette.Bad);
                return false;
            }

            if (!_pending.TryGetValue(row.Key, out JObject? fields))
            {
                fields = new JObject();
                _pending.Add(row.Key, fields);
            }

            fields[column] = value;
            _lastRow = row.Key;
            SetStatus(row.Name + ": " + fields.Count + " field(s) staged; Enter or another row commits.", ViewPalette.Info);
            return true;
        }

        /// <summary>Commits one row's staged values as one set change set.</summary>
        public ApplyReport? CommitRow(TableRow row)
        {
            if (Model == null || !_pending.TryGetValue(row.Key, out JObject? fields) || fields.Count == 0)
            {
                return null;
            }

            _pending.Remove(row.Key);
            return ApplyEdit(TableModel.CommitRow(row, fields, Model.Title));
        }

        /// <summary>Sets one column on the selected rows (or <paramref name="rows"/>) in one change set.</summary>
        public ApplyReport? ApplyToSelection(string column, string text, IReadOnlyList<TableRow>? rows = null)
        {
            TableColumn? spec = Model?.Column(column);
            if (Model == null || spec?.Spec == null || !spec.Editable)
            {
                SetStatus("Choose an editable column.", ViewPalette.Warn);
                return null;
            }

            if (!TableModel.TryParse(spec.Spec, text, out JToken value, out string problem))
            {
                SetStatus(problem, ViewPalette.Bad);
                return null;
            }

            List<TableRow> targets = new List<TableRow>();
            if (rows != null)
            {
                targets.AddRange(rows);
            }
            else if (_list != null)
            {
                foreach (object item in _list.selectedItems)
                {
                    if (item is TableRow row)
                    {
                        targets.Add(row);
                    }
                }
            }

            if (targets.Count == 0)
            {
                SetStatus("Select rows first (Shift/Ctrl-click).", ViewPalette.Warn);
                return null;
            }

            return ApplyEdit(TableModel.ApplyToRows(targets, column, value, Model.Title));
        }

        protected override void OnRefresh()
        {
            IndexGraph graph = Context.Graph();
            List<string> types = new List<string>(graph.Types);
            if (types.Count == 0)
            {
                types.Add(_anyType);
            }

            _typePicker.choices = types;
            _typePicker.SetValueWithoutNotify(types.Contains(_anyType) ? _anyType : types[0]);
            ToolCatalog catalog = Context.Runtime.Registry.Catalog;
            switch (_tab)
            {
                case EntitiesTab:
                    Model = TableModel.ForType(catalog, graph, "entity.instance", EntitiesTab);
                    break;
                case DefinitionsTab:
                    Model = TableModel.ForType(catalog, graph, "entity.definition", DefinitionsTab);
                    break;
                case ItemsTab:
                    Model = TableModel.ForType(catalog, graph, "inventory.item", ItemsTab);
                    break;
                case FactsTab:
                    Model = Facts(catalog, graph);
                    break;
                case SavesTab:
                    Model = TableModel.SaveSlots(Path.Combine(Application.persistentDataPath, "saves"));
                    break;
                default:
                    Model = TableModel.ForType(catalog, graph, _typePicker.value, _typePicker.value);
                    break;
            }

            Model.Filter(_filter);
            BuildList();
            List<string> editable = new List<string>();
            foreach (TableColumn column in Model.Columns)
            {
                if (column.Editable)
                {
                    editable.Add(column.Name);
                }
            }

            if (editable.Count == 0)
            {
                editable.Add("-");
            }

            _bulkColumn.choices = editable;
            _bulkColumn.SetValueWithoutNotify(editable[0]);
            SetStatus(Model.Title + ": " + Model.Rows.Count + " of " + Model.AllRows.Count + " rows" + (Model.TypeId == null ? " (read-only)" : string.Empty));
        }

        private TableModel Facts(ToolCatalog catalog, IndexGraph graph)
        {
            TableModel facts = TableModel.ForType(catalog, graph, "narrative.fact", FactsTab);
            if (!Context.IsPlaying)
            {
                return facts;
            }

            List<TableColumn> columns = new List<TableColumn>(facts.Columns) { new TableColumn(TableModel.LiveColumn, null, false) };
            List<TableRow> rows = new List<TableRow>(facts.AllRows);
            foreach (TableRow row in rows)
            {
                string name = row.Text("factName");
                string live = name.Length > 0 && Context.Gameplay.TryReadFact(name, out int value) ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-";
                row.Values[TableModel.LiveColumn] = live;
                row.Texts[TableModel.LiveColumn] = live;
            }

            return new TableModel(FactsTab, facts.TypeId, columns, rows);
        }

        private void BuildList()
        {
            TableModel model = Model!;
            _host.Clear();
            _list = new MultiColumnListView
            {
                itemsSource = model.Rows,
                fixedItemHeight = 22f,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.Multiple,
                sortingMode = ColumnSortingMode.Custom,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                reorderable = false,
            };
            _list.style.flexGrow = 1f;
            foreach (TableColumn column in model.Columns)
            {
                TableColumn current = column;
                _list.columns.Add(new Column
                {
                    name = column.Name,
                    title = column.Title,
                    width = column.Name == TableModel.NameColumn ? 200f : 140f,
                    sortable = true,
                    makeCell = () => current.Editable ? (VisualElement)MakeEditor(current) : new Label { style = { unityTextAlign = TextAnchor.MiddleLeft, paddingLeft = 4f } },
                    bindCell = (element, index) => BindCell(element, index, current),
                });
            }

            _list.columnSortingChanged += () =>
            {
                foreach (SortColumnDescription description in _list.sortedColumns)
                {
                    model.Sort(description.columnName, description.direction == SortDirection.Ascending);
                    break;
                }

                _list.RefreshItems();
            };
            _list.selectionChanged += _ =>
            {
                if (_lastRow != null && _list.selectedItem is TableRow selected && selected.Key != _lastRow)
                {
                    schedule.Execute(CommitPending);
                }
            };
            _host.Add(_list);
        }

        private TextField MakeEditor(TableColumn column)
        {
            TextField field = new TextField { isDelayed = true };
            field.style.marginLeft = 0f;
            field.style.marginRight = 0f;
            field.RegisterValueChangedCallback(evt =>
            {
                if (field.userData is TableRow row && evt.newValue != row.Text(column.Name))
                {
                    Stage(row, column.Name, evt.newValue);
                }
            });
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && field.userData is TableRow row)
                {
                    if (field.value != row.Text(column.Name))
                    {
                        Stage(row, column.Name, field.value);
                    }

                    CommitRow(row);
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            return field;
        }

        private void BindCell(VisualElement element, int index, TableColumn column)
        {
            if (Model == null || index < 0 || index >= Model.Rows.Count)
            {
                return;
            }

            TableRow row = Model.Rows[index];
            string text = row.Text(column.Name);
            if (_pending.TryGetValue(row.Key, out JObject? staged) && staged[column.Name] is JToken value)
            {
                text = AuthoredData.Display(value);
            }

            if (element is TextField field)
            {
                field.userData = row;
                field.SetValueWithoutNotify(text);
            }
            else if (element is Label label)
            {
                label.text = text;
            }
        }

        private void CommitPending()
        {
            if (Model == null || _pending.Count == 0)
            {
                return;
            }

            List<TableRow> rows = new List<TableRow>();
            foreach (TableRow row in Model.AllRows)
            {
                if (_pending.ContainsKey(row.Key))
                {
                    rows.Add(row);
                }
            }

            foreach (TableRow row in rows)
            {
                CommitRow(row);
            }

            _pending.Clear();
            _lastRow = null;
        }

        protected override void OnDispose()
        {
            _pending.Clear();
        }
    }

    public sealed class TablesWindow : StudioViewWindow
    {
        protected override string ViewTitle => "Tables";

        [MenuItem(StudioViewIds.TablesMenu, false, 2104)]
        public static void OpenWindow() => Open<TablesWindow>();

        protected override StudioViewBase CreateView(StudioViewContext context) => new TablesView(context);
    }
}
