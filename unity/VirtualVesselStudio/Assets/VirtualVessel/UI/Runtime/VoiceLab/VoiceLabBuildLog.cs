using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine.UIElements;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// STEP 3's record of the build, in the same monospace frame as the setup's (the user's request, 2026-11-15; screen
    /// spec 5): a line when a stage begins, then its count every so often ("Learning the voice 30 / 600 epochs"), and
    /// how it ended. In the app's words, never the scripts' own output (system design 4.27). Kept while the app runs.
    /// </summary>
    public sealed class VoiceLabBuildLog : VisualElement
    {
        /// <summary>A stage's count is written about this many times, so 600 epochs make about 20 lines.</summary>
        public const int LinesPerStage = 20;

        private readonly ScrollView _frame;
        private readonly List<Entry> _entries = new List<Entry>();
        private VoiceLabBuildState _state = VoiceLabBuildState.None;
        private int _done = -1;

        public VoiceLabBuildLog()
        {
            _frame = new ScrollView(ScrollViewMode.Vertical);
            _frame.AddToClassList(VoiceLabSetupView.LogClass);
            _frame.AddToClassList(VoiceLabScreen.GapClass);
            Add(_frame);
            style.display = DisplayStyle.None;
            PanelLifetime.OnLocaleChanged(this, Render);
        }

        /// <summary>The lines as shown, for tests.</summary>
        public IReadOnlyList<string> Lines
        {
            get
            {
                var lines = new List<string>();
                foreach (VisualElement line in _frame.Children())
                {
                    lines.Add(((Label)line).text);
                }

                return lines;
            }
        }

        /// <summary>A new build begins: the last one's record goes.</summary>
        public void Start()
        {
            _entries.Clear();
            _state = VoiceLabBuildState.None;
            _done = -1;
            Render();
        }

        /// <summary>Adds a line when the build has moved on enough since the last one.</summary>
        public void Follow(VoiceLabBuildProgress progress, DateTime now)
        {
            if (!Worth(_state, _done, progress))
            {
                return;
            }

            _state = progress.State;
            _done = progress.Done;
            _entries.Add(new Entry(now, progress.State, progress.Done, progress.Total, ended: false));
            Render();
        }

        /// <summary>How the build ended: completed, failed, or stopped.</summary>
        public void End(VoiceLabBuildState state, DateTime now)
        {
            _entries.Add(new Entry(now, state, 0, 0, ended: true));
            Render();
        }

        /// <summary>
        /// Whether <paramref name="progress"/> is worth a line after the last one: a new stage, or its count moved on by
        /// a twentieth of its total, or reached it. A count of one (a stage loading) is not written. Pure.
        /// </summary>
        public static bool Worth(VoiceLabBuildState lastState, int lastDone, VoiceLabBuildProgress progress)
        {
            if (progress.State == VoiceLabBuildState.None)
            {
                return false;
            }

            if (progress.State != lastState)
            {
                return true;
            }

            if (progress.Total <= 1 || progress.Done == lastDone)
            {
                return false;
            }

            return progress.Done >= progress.Total || progress.Done - lastDone >= Math.Max(1, progress.Total / LinesPerStage);
        }

        /// <summary>A line: its time, the stage, and its count ("14:20:05  Learning the voice  30 / 600 epochs"). Pure.</summary>
        public static string LineText(ILocalizer localizer, DateTime at, VoiceLabBuildState state, int done, int total, bool ended)
        {
            string time = at.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            string key = ended ? EndKey(state) : VoiceLabView.BuildStageKey(state);
            string stage = key != null ? localizer?.Get(key) ?? key : state.ToString();
            if (ended || total <= 1)
            {
                return time + "  " + stage;
            }

            string countKey = state == VoiceLabBuildState.Training ? VoiceLabStrings.BuildLogEpochs : VoiceLabStrings.BuildLogCount;
            string doneText = done.ToString(CultureInfo.InvariantCulture);
            string totalText = total.ToString(CultureInfo.InvariantCulture);
            string count = localizer?.Get(countKey, ("done", doneText), ("total", totalText)) ?? doneText + " / " + totalText;
            return time + "  " + stage + "  " + count;
        }

        private static string EndKey(VoiceLabBuildState state)
        {
            switch (state)
            {
                case VoiceLabBuildState.Completed:
                    return VoiceLabStrings.BuildLogDone;
                case VoiceLabBuildState.Cancelled:
                    return VoiceLabStrings.BuildLogStopped;
                default:
                    return VoiceLabStrings.BuildLogFailed;
            }
        }

        private void Render()
        {
            ILocalizer localizer = UiServices.Find(this)?.Localizer;
            _frame.Clear();
            foreach (Entry entry in _entries)
            {
                Label line = QuietText.Of(new Label(LineText(localizer, entry.At, entry.State, entry.Done, entry.Total, entry.Ended)));
                line.AddToClassList("vv-mono");
                _frame.Add(line);
            }

            style.display = _entries.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_frame.childCount > 0)
            {
                VisualElement newest = _frame[_frame.childCount - 1];
                _frame.schedule.Execute(() => _frame.ScrollTo(newest)).StartingIn(50);
            }
        }

        private readonly struct Entry
        {
            public Entry(DateTime at, VoiceLabBuildState state, int done, int total, bool ended)
            {
                At = at;
                State = state;
                Done = done;
                Total = total;
                Ended = ended;
            }

            public DateTime At { get; }

            public VoiceLabBuildState State { get; }

            public int Done { get; }

            public int Total { get; }

            public bool Ended { get; }
        }
    }
}
