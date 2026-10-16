using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.Core.Time;
using VirtualVessel.UI.Controls;
using Object = UnityEngine.Object;

namespace VirtualVessel.UI.Tests
{
    /// <summary>The table on the real styles, inside the real UI root (components.md 3.24).</summary>
    public sealed class DataTablePanelTests
    {
        private static readonly TableColumn[] s_columns =
        {
            TableColumn.Filling("t.name", 200),
            TableColumn.Number("t.a", 300),
            TableColumn.Number("t.b", 300),
        };

        private string _directory;
        private GameObject _host;
        private UiRoot _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _host = new GameObject(nameof(DataTablePanelTests));
            _host.AddComponent<UIDocument>();
            _root = _host.AddComponent<UiRoot>();
            _root.SetAssets(UnityEditor.AssetDatabase.LoadAssetAtPath<UiAssets>("Assets/VirtualVessel/UI/UiAssets.asset"));
            _root.Build(new UiRootContext(_directory, new StopwatchMonotonicClock(), new UtcSystemClock(), null, null, false));
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_host);
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [UnityTest]
        public IEnumerator HeaderAndRows_LineUp_NeverOverflowTheBox_AndFillAWideOne()
        {
            var box = Overlay(500, 200);
            var table = new DataTable(s_columns);
            box.Add(table);
            table.SetRows(new[] { new[] { "name", "1", "2" } });
            yield return null;
            yield return null;

            Assert.That(DataTable.MinWidth(s_columns), Is.EqualTo(800f));
            for (int column = 0; column < s_columns.Length; column++)
            {
                Assert.That(table.GetCell(0, column).worldBound.xMin, Is.EqualTo(table.HeaderCells[column].worldBound.xMin).Within(0.5f), $"Column {column} starts with its header.");
                Assert.That(table.GetCell(0, column).worldBound.xMax, Is.EqualTo(table.HeaderCells[column].worldBound.xMax).Within(0.5f), $"Column {column} ends with its header.");
            }

            Assert.That(table.worldBound.xMax, Is.LessThanOrEqualTo(box.worldBound.xMax + 0.5f), "A narrow box scrolls the table instead of letting it out.");
            Assert.That(table.GetCell(0, 2).worldBound.xMax - table.HeaderCells[0].worldBound.xMin, Is.EqualTo(800f).Within(0.5f), "Columns keep their widths inside the scroll.");

            box.style.width = 1200;
            yield return null;
            yield return null;
            Assert.That(table.GetCell(0, 2).worldBound.xMax, Is.EqualTo(box.worldBound.xMax).Within(0.5f), "A wide box: the filling column takes the rest.");
            Assert.That(table.GetCell(0, 0).worldBound.width, Is.EqualTo(600f).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator VerticalWheelOverATable_ScrollsThePageInstead()
        {
            var page = new ScrollView(ScrollViewMode.Vertical);
            page.style.position = Position.Absolute;
            page.style.width = 400;
            page.style.height = 300;
            _root.Content.Add(page);
            var table = new DataTable(s_columns);
            table.SetRows(new[] { new[] { "name", "1", "2" } });
            page.Add(table);
            var filler = new VisualElement();
            filler.style.height = 3000;
            page.Add(filler);
            yield return null;
            yield return null;

            Label cell = table.GetCell(0, 0);
            var wheel = new Event { type = EventType.ScrollWheel, delta = new Vector2(0, 3), mousePosition = cell.worldBound.center };
            using (WheelEvent evt = WheelEvent.GetPooled(wheel))
            {
                evt.target = cell;
                cell.SendEvent(evt);
            }

            yield return null;
            Assert.That(page.scrollOffset.y, Is.GreaterThan(0f), "A vertical wheel over a sideways-scrolling table scrolls the page.");
            Assert.That(table.ScrollOffset, Is.EqualTo(0f), "...and does not slide the table sideways.");
        }

        private VisualElement Overlay(float width, float height)
        {
            var box = new VisualElement();
            box.style.position = Position.Absolute;
            box.style.width = width;
            box.style.height = height;
            _root.Content.Add(box);
            return box;
        }
    }
}
