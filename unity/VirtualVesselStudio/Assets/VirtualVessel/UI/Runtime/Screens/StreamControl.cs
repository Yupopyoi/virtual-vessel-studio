using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine.UIElements;
using VirtualVessel.Core.State;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Streaming;

namespace VirtualVessel.UI.Screens
{
    /// <summary>String table keys of the stream controls (docs/&lt;lang&gt;/ui/screens/live.md 3).</summary>
    [StringKeys]
    public static class StreamStrings
    {
        public const string Start = "live.stream.start";
        public const string Connecting = "live.stream.connecting";
        public const string Ending = "live.stream.ending";
        public const string Stop = "live.stream.stop";
        public const string Busy = "live.stream.busy";
        public const string NoKeyReason = "live.stream.no-key";
        public const string Live = "live.stream.live";
        public const string Reconnecting = "live.stream.reconnecting";
        public const string Failed = "live.stream.failed";
        public const string ConfirmStartTitle = "live.stream.confirm-start.title";
        public const string ConfirmStartBody = "live.stream.confirm-start.body";
        public const string ConfirmStart = "live.stream.confirm-start.start";
        public const string ConfirmStopTitle = "live.stream.confirm-stop.title";
        public const string ConfirmStopBody = "live.stream.confirm-stop.body";
        public const string ConfirmStop = "live.stream.confirm-stop.stop";
        public const string Bitrate = "live.stream.bitrate";
        public const string Slow = "live.stream.slow";
        public const string NoStreamKey = "live.stream.problem.no-key";
        public const string ServerUnreachable = "live.stream.problem.unreachable";
        public const string Rejected = "live.stream.problem.rejected";
        public const string ConnectionLost = "live.stream.problem.lost";
        public const string EncoderFailed = "live.stream.problem.encoder";
        public const string AudioUnavailable = "live.stream.problem.audio";
    }

    /// <summary>What the stream controls show for a state (streaming detailed design 18.1). Pure data.</summary>
    public readonly struct StreamView
    {
        public StreamView(bool showStart, string startKey, bool showStop, StatusKind? chip, string chipKey, string problemKey, bool showElapsed)
        {
            ShowStart = showStart;
            StartKey = startKey;
            ShowStop = showStop;
            Chip = chip;
            ChipKey = chipKey;
            ProblemKey = problemKey;
            ShowElapsed = showElapsed;
        }

        public bool ShowStart { get; }

        public string StartKey { get; }

        public bool ShowStop { get; }

        /// <summary>The status chip's look; null hides it.</summary>
        public StatusKind? Chip { get; }

        public string ChipKey { get; }

        /// <summary>What went wrong and what to do; null hides it.</summary>
        public string ProblemKey { get; }

        public bool ShowElapsed { get; }
    }

    /// <summary>
    /// Going live and ending the stream, next to the record control (streaming detailed design 18.1). Unlike
    /// recording, starting asks first, since a stray start may be public at once; ending is a Danger action.
    /// </summary>
    public sealed class StreamControl : VisualElement
    {
        public const string UssClass = "vv-stream-control";

        /// <summary>The queue at which the connection is said not to keep up.</summary>
        public const long SlowQueueMs = 1000;

        private const long RefreshIntervalMs = 250;

        private readonly IStreamingService _streaming;
        private readonly ObservableValue<string> _disabled = new ObservableValue<string>(null);
        private readonly KeyedText _problemText;
        private UiServices _services;

        public StreamControl(IStreamingService streaming)
        {
            _streaming = streaming ?? throw new ArgumentNullException(nameof(streaming));
            AddToClassList(UssClass);
            AddToClassList(RecordControl.UssClass);

            var buttons = new VisualElement();
            buttons.AddToClassList(RecordControl.ButtonsClass);
            Chip = Ui.Chip(StatusKind.Live, StreamStrings.Live);
            buttons.Add(Chip);
            StartButton = Ui.Button(StreamStrings.Start, ButtonVariant.Primary).Bind(new DangerCommand(
                new UiCommand(_streaming.Start, _disabled),
                new ConfirmationRequest(StreamStrings.ConfirmStartTitle, StreamStrings.ConfirmStartBody, StreamStrings.ConfirmStart)));
            buttons.Add(StartButton);
            StopButton = Ui.Button(StreamStrings.Stop, ButtonVariant.Danger).Bind(new DangerCommand(
                new UiCommand(_streaming.Stop),
                new ConfirmationRequest(StreamStrings.ConfirmStopTitle, StreamStrings.ConfirmStopBody, StreamStrings.ConfirmStop)));
            buttons.Add(StopButton);
            Add(buttons);

            Problem = new Label();
            Problem.AddToClassList(RecordControl.ProblemClass);
            Problem.AddToClassList("vv-text-body");
            _problemText = new KeyedText(Problem, text => Problem.text = text);
            Add(Problem);

            Health = new Label();
            Health.AddToClassList(RecordControl.ProblemClass);
            Health.AddToClassList("vv-text-caption");
            Add(Health);

            Show();
            PanelLifetime.WhileAttached(this, Attach);
        }

        public StatusChip Chip { get; }

        public ActionButton StartButton { get; }

        public ActionButton StopButton { get; }

        public Label Problem { get; }

        /// <summary>The actual bitrate while live, and whether the connection keeps up.</summary>
        public Label Health { get; }

        /// <summary>What to show in <paramref name="state"/>. Pure.</summary>
        public static StreamView ViewOf(StreamingState state, StreamingProblem problem)
        {
            return state switch
            {
                StreamingState.Connecting => new StreamView(true, StreamStrings.Connecting, false, null, null, null, false),
                StreamingState.Live => new StreamView(false, StreamStrings.Start, true, StatusKind.Live, StreamStrings.Live, ProblemKey(problem), true),
                StreamingState.Reconnecting => new StreamView(false, StreamStrings.Start, true, StatusKind.Warning, StreamStrings.Reconnecting, ProblemKey(problem), false),
                StreamingState.Stopping => new StreamView(true, StreamStrings.Ending, false, null, null, null, false),
                StreamingState.Failed => new StreamView(true, StreamStrings.Start, false, StatusKind.Error, StreamStrings.Failed, ProblemKey(problem), false),
                _ => new StreamView(true, StreamStrings.Start, false, null, null, null, false),
            };
        }

        /// <summary>The message for <paramref name="problem"/>, which says what to do; null for none. Pure.</summary>
        public static string ProblemKey(StreamingProblem problem)
        {
            return problem switch
            {
                StreamingProblem.NoStreamKey => StreamStrings.NoStreamKey,
                StreamingProblem.ServerUnreachable => StreamStrings.ServerUnreachable,
                StreamingProblem.Rejected => StreamStrings.Rejected,
                StreamingProblem.ConnectionLost => StreamStrings.ConnectionLost,
                StreamingProblem.EncoderFailed => StreamStrings.EncoderFailed,
                StreamingProblem.AudioUnavailable => StreamStrings.AudioUnavailable,
                _ => null,
            };
        }

        /// <summary>Why going live cannot be pressed; null when it can. Pure.</summary>
        public static string DisabledReason(StreamingState state, bool keyRegistered)
        {
            if (state == StreamingState.Connecting || state == StreamingState.Stopping)
            {
                return StreamStrings.Busy;
            }

            return keyRegistered ? null : StreamStrings.NoKeyReason;
        }

        /// <summary>"5.9" megabits a second, from bits a second. Pure.</summary>
        public static string Megabits(long bitsPerSecond)
        {
            return (bitsPerSecond / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture);
        }

        private IDisposable Attach(UiServices services)
        {
            _services = services;
            IVisualElementScheduledItem refresh = schedule.Execute(ShowStats).Every(RefreshIntervalMs);
            var subscriptions = new List<IDisposable>
            {
                _streaming.State.Subscribe(_ => Show()),
                _streaming.Problem.Subscribe(_ => Show()),
                _streaming.Options.Subscribe(_ => Show()),
                new DisposeAction(refresh.Pause),
            };
            if (services?.Localizer != null)
            {
                Action<LocaleInfo> relabel = _ => Show();
                services.Localizer.LocaleChanged += relabel;
                subscriptions.Add(new DisposeAction(() => services.Localizer.LocaleChanged -= relabel));
            }

            Show();
            return new DisposeAll(subscriptions);
        }

        private void Show()
        {
            StreamingState state = _streaming.State.Value;
            StreamView view = ViewOf(state, _streaming.Problem.Value);
            _disabled.Set(DisabledReason(state, _streaming.Options.Value.KeyRegistered));
            StartButton.TextKey = view.StartKey;
            StartButton.style.display = view.ShowStart ? DisplayStyle.Flex : DisplayStyle.None;
            StopButton.style.display = view.ShowStop ? DisplayStyle.Flex : DisplayStyle.None;
            Chip.style.display = view.Chip != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (view.Chip is StatusKind chip)
            {
                Chip.Status = chip;
                Chip.TextKey = view.ChipKey;
            }

            _problemText.Key = view.ProblemKey;
            Problem.style.display = view.ProblemKey != null ? DisplayStyle.Flex : DisplayStyle.None;
            ShowStats();
        }

        private void ShowStats()
        {
            bool live = _streaming.State.Value == StreamingState.Live;
            Chip.Number = live ? RecordControl.FormatElapsed(_streaming.Elapsed) : null;
            Health.style.display = live ? DisplayStyle.Flex : DisplayStyle.None;
            if (!live)
            {
                return;
            }

            StreamingStats stats = _streaming.Stats;
            ILocalizer localizer = _services?.Localizer;
            string bitrate = localizer?.Get(StreamStrings.Bitrate, ("rate", Megabits(stats.BitsPerSecond))) ?? Megabits(stats.BitsPerSecond);
            Health.text = stats.QueueMs >= SlowQueueMs ? $"{bitrate}  {localizer?.Get(StreamStrings.Slow)}" : bitrate;
        }
    }
}
