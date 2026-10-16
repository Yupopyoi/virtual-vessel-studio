using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace VirtualVessel.UI.Controls
{
    public enum CellAlign
    {
        Start,
        End,
    }

    /// <summary>
    /// One column of a <see cref="DataTable"/>: its width, alignment, and how its cells read
    /// (components.md 3.24). The header and every row use the same column, so they always line up.
    /// </summary>
    public sealed class TableColumn
    {
        public TableColumn(string headerKey, float width, CellAlign align = CellAlign.Start, bool monospace = false, bool fill = false, bool cellsAreKeys = false)
        {
            if (width <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(width), width, "A column needs a width (the minimum width for a filling column).");
            }

            HeaderKey = headerKey;
            Width = width;
            Align = align;
            Monospace = monospace;
            Fill = fill;
            CellsAreKeys = cellsAreKeys;
        }

        public string HeaderKey { get; }

        /// <summary>The width, or the minimum width when <see cref="Fill"/> is set; includes the cell padding.</summary>
        public float Width { get; }

        public CellAlign Align { get; }

        public bool Monospace { get; }

        /// <summary>Takes the room the other columns leave, such as a name column.</summary>
        public bool Fill { get; }

        /// <summary>Cells hold string table keys, shown in the current language, such as a kind.</summary>
        public bool CellsAreKeys { get; }

        /// <summary>Text, left-aligned.</summary>
        public static TableColumn Text(string headerKey, float width, bool monospace = false)
        {
            return new TableColumn(headerKey, width, CellAlign.Start, monospace);
        }

        /// <summary>Text that takes the room left, at least <paramref name="minWidth"/>.</summary>
        public static TableColumn Filling(string headerKey, float minWidth, bool monospace = false)
        {
            return new TableColumn(headerKey, minWidth, CellAlign.Start, monospace, fill: true);
        }

        /// <summary>Numbers: monospace and right-aligned, so digits line up.</summary>
        public static TableColumn Number(string headerKey, float width)
        {
            return new TableColumn(headerKey, width, CellAlign.End, monospace: true);
        }

        /// <summary>Cells are string table keys, such as a kind shown as a word.</summary>
        public static TableColumn Key(string headerKey, float width)
        {
            return new TableColumn(headerKey, width, cellsAreKeys: true);
        }
    }

    /// <summary>
    /// A table whose columns always line up: widths, alignment, and the gutter between columns come
    /// from the column definitions and the stylesheet, not from each screen (components.md 3.24).
    /// </summary>
    /// <remarks>
    /// Rows are reused when the data changes, so refreshing a table does not rebuild it. When the room is
    /// narrower than the columns need, the table scrolls sideways inside itself instead of spilling out;
    /// when it is wider, the filling column takes the rest.
    /// </remarks>
    public sealed class DataTable : VisualElement
    {
        public const string UssClass = "vv-table";
        public const string RowClass = UssClass + "__row";
        public const string HeaderClass = UssClass + "__header";
        public const string CellClass = UssClass + "__cell";
        public const string EndClass = CellClass + "--end";
        public const string ScrollClass = UssClass + "__scroll";

        private readonly IReadOnlyList<TableColumn> _columns;
        private readonly VisualElement _body;
        private readonly List<Row> _rows = new List<Row>();
        private readonly Label[] _headerCells;
        private readonly ScrollView _scroll;

        public DataTable(IReadOnlyList<TableColumn> columns)
        {
            _columns = columns ?? throw new ArgumentNullException(nameof(columns));
            AddToClassList(UssClass);

            // A vertical wheel over it still scrolls the page; only sideways input scrolls the table.
            _scroll = new ScrollView(ScrollViewMode.Horizontal) { horizontalScrollerVisibility = ScrollerVisibility.Auto };
            _scroll.AddToClassList(ScrollClass);
            _scroll.contentContainer.style.minWidth = Length.Percent(100);
            hierarchy.Add(_scroll);

            var content = new VisualElement();
            content.style.minWidth = MinWidth(columns);
            content.style.flexGrow = 1;
            _scroll.Add(content);

            Header = new VisualElement();
            Header.AddToClassList(RowClass);
            Header.AddToClassList(HeaderClass);
            _headerCells = new Label[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                Label cell = CreateCell(columns[i], "vv-text-caption");
                string key = columns[i].HeaderKey;
                new KeyedText(cell, text => cell.text = text) { Key = key };
                _headerCells[i] = cell;
                Header.Add(cell);
            }

            content.Add(Header);
            _body = new VisualElement();
            content.Add(_body);
        }

        public IReadOnlyList<TableColumn> Columns => _columns;

        public VisualElement Header { get; }

        public IReadOnlyList<Label> HeaderCells => _headerCells;

        public int RowCount => _rows.Count;

        /// <summary>How far the table is scrolled sideways.</summary>
        public float ScrollOffset => _scroll.scrollOffset.x;

        /// <summary>The width the columns need: below it, the table scrolls sideways. Pure.</summary>
        public static float MinWidth(IReadOnlyList<TableColumn> columns)
        {
            float width = 0f;
            foreach (TableColumn column in columns)
            {
                width += column.Width;
            }

            return width;
        }

        public Label GetCell(int row, int column)
        {
            return _rows[row].Cells[column];
        }

        /// <summary>Shows <paramref name="rows"/>, one text per column; missing texts show empty.</summary>
        public void SetRows(IReadOnlyList<IReadOnlyList<string>> rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            while (_rows.Count < rows.Count)
            {
                var row = new Row(_columns);
                _rows.Add(row);
                _body.Add(row.Root);
            }

            while (_rows.Count > rows.Count)
            {
                _rows[_rows.Count - 1].Root.RemoveFromHierarchy();
                _rows.RemoveAt(_rows.Count - 1);
            }

            for (int i = 0; i < rows.Count; i++)
            {
                _rows[i].Show(rows[i]);
            }
        }

        private static Label CreateCell(TableColumn column, string typography)
        {
            var cell = new Label();
            cell.AddToClassList(CellClass);
            cell.AddToClassList(typography);
            cell.EnableInClassList(EndClass, column.Align == CellAlign.End);

            // Widths are structure, set in code from the column; the look is in table.uss.
            if (column.Fill)
            {
                cell.style.minWidth = column.Width;
                cell.style.flexGrow = 1;
                cell.style.flexShrink = 1;
                cell.style.flexBasis = 0;
            }
            else
            {
                cell.style.width = column.Width;
                cell.style.flexShrink = 0;
            }

            return cell;
        }

        private sealed class Row
        {
            private readonly IReadOnlyList<TableColumn> _columns;
            private readonly KeyedText[] _keys;

            public Row(IReadOnlyList<TableColumn> columns)
            {
                _columns = columns;
                Root = new VisualElement();
                Root.AddToClassList(RowClass);
                Cells = new Label[columns.Count];
                _keys = new KeyedText[columns.Count];
                for (int i = 0; i < columns.Count; i++)
                {
                    Label cell = CreateCell(columns[i], columns[i].Monospace ? "vv-text-mono" : "vv-text-body");
                    Cells[i] = cell;
                    if (columns[i].CellsAreKeys)
                    {
                        _keys[i] = new KeyedText(cell, text => cell.text = text);
                    }

                    Root.Add(cell);
                }
            }

            public VisualElement Root { get; }

            public Label[] Cells { get; }

            public void Show(IReadOnlyList<string> texts)
            {
                for (int i = 0; i < _columns.Count; i++)
                {
                    string text = i < texts.Count ? texts[i] ?? string.Empty : string.Empty;
                    if (_keys[i] != null)
                    {
                        _keys[i].Key = text;
                    }
                    else if (Cells[i].text != text)
                    {
                        Cells[i].text = text;
                    }
                }
            }
        }
    }
}
