using System.Collections.Generic;
using UnityEngine.UIElements;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.Tracking;
using VirtualVessel.Tracking.Recording;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.Screens
{
    /// <summary>
    /// The Diagnostics screen (Developer Mode only): a table of performance metrics, refreshed every
    /// second while shown, and the tracking recordings (UI foundation detailed design 7.7). Minimal for
    /// now; may be revised.
    /// </summary>
    public sealed class DiagnosticsScreen : VisualElement, IScreen
    {
        public const string UssClass = "vv-diagnostics";
        public const string StatusClass = UssClass + "__status";

        /// <summary>Snapshots allocate, so they are taken only while the screen is shown, once a second.</summary>
        public const long RefreshMilliseconds = 1000;

        // Widths include the cell padding; numbers fit "1,553.86 MB" and the budget "12 (> 16.67 ms)".
        private static readonly TableColumn[] s_columns =
        {
            TableColumn.Text(ScreenStrings.ColumnModule, 112, monospace: true),
            TableColumn.Filling(ScreenStrings.ColumnName, 200, monospace: true),
            TableColumn.Key(ScreenStrings.ColumnKind, 72),
            TableColumn.Number(ScreenStrings.ColumnCurrent, 108),
            TableColumn.Number(ScreenStrings.ColumnAverage, 108),
            TableColumn.Number(ScreenStrings.ColumnMax, 108),
            TableColumn.Number(ScreenStrings.ColumnP95, 108),
            TableColumn.Number(ScreenStrings.ColumnP99, 108),
            TableColumn.Number(ScreenStrings.ColumnCount, 80),
            TableColumn.Number(ScreenStrings.ColumnOverBudget, 140),
        };

        private readonly IPerformanceMetrics _metrics;
        private readonly KeyedText _statusText;
        private IVisualElementScheduledItem _refresh;

        /// <param name="metrics">Null when the performance metrics service is not available.</param>
        /// <param name="recordings">Tracking recordings; null leaves their Card out.</param>
        /// <param name="tracking">Tracking, to know whether recording can start.</param>
        public DiagnosticsScreen(IPerformanceMetrics metrics, ITrackingRecordings recordings = null, ITrackingSource tracking = null)
        {
            _metrics = metrics;
            AddToClassList(UssClass);
            Card card = Ui.Card(ScreenStrings.Metrics);
            Add(card);

            Status = new Label();
            Status.AddToClassList(StatusClass);
            Status.AddToClassList("vv-text-body");
            _statusText = new KeyedText(Status, value => Status.text = value);
            card.Add(Status);

            if (metrics == null)
            {
                _statusText.Key = ScreenStrings.MetricsUnavailable;
            }
            else
            {
                Table = Ui.Table(s_columns);
                card.Add(Table);
            }

            if (recordings != null)
            {
                Recordings = new TrackingRecordingsCard(recordings, tracking);
                Add(Recordings);
            }
        }

        public Label Status { get; }

        /// <summary>Null without tracking recordings.</summary>
        public TrackingRecordingsCard Recordings { get; }

        /// <summary>Null when the performance metrics service is not available.</summary>
        public DataTable Table { get; }

        /// <summary>How many times the table was refreshed; lets tests see that it stops while hidden.</summary>
        public int RefreshCount { get; private set; }

        public int RowCount => Table?.RowCount ?? 0;

        public bool IsRefreshing => _refresh != null;

        public void OnShown()
        {
            if (_metrics == null || _refresh != null)
            {
                return;
            }

            Refresh();
            _refresh = schedule.Execute(Refresh).Every(RefreshMilliseconds).StartingIn(RefreshMilliseconds);
        }

        public void OnHidden()
        {
            _refresh?.Pause();
            _refresh = null;
        }

        /// <summary>The cell text of a row as shown, for tests.</summary>
        public string GetCell(int row, int column)
        {
            return Table.GetCell(row, column).text;
        }

        internal void Refresh()
        {
            PerformanceSnapshot snapshot = _metrics.GetSnapshot();
            RefreshCount++;
            _statusText.Key = snapshot.DetailedEnabled ? ScreenStrings.DetailedOn : ScreenStrings.DetailedOff;

            var rows = new List<string[]>(snapshot.Metrics.Count);
            foreach (MetricSnapshot metric in snapshot.Metrics)
            {
                rows.Add(DiagnosticsFormat.Row(metric));
            }

            Table.SetRows(rows);
        }
    }
}
