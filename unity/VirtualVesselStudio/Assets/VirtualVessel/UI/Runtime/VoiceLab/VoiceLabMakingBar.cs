using System;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// The bar shown while Voice Lab makes several files (candidates, samples): Getting ready while the voice
    /// model loads, then what is being made with "2 / 6", read every 0.3 s, and Stop.
    /// </summary>
    public sealed class VoiceLabMakingBar : VisualElement
    {
        public const string UssClass = VoiceLabScreen.UssClass + "__busy";

        private const long WatchMs = 300;

        private readonly Func<VoiceLabMaking> _read;
        private readonly string _makingKey;
        private readonly ProgressMeter _meter;
        private IVisualElementScheduledItem _watch;

        /// <param name="read">Where the work is; read on the main thread as often as the bar needs.</param>
        /// <param name="makingKey">What is being made, once the model has loaded.</param>
        /// <param name="stop">Runs when Stop is pressed.</param>
        public VoiceLabMakingBar(Func<VoiceLabMaking> read, string makingKey, Action stop)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _makingKey = makingKey;
            AddToClassList(UssClass);
            _meter = Ui.Progress(VoiceLabStrings.Loading);
            Add(_meter);
            StopButton = Ui.Button(VoiceLabStrings.Stop);
            StopButton.clicked += () => stop?.Invoke();
            Add(StopButton);
            style.display = DisplayStyle.None;
        }

        public ActionButton StopButton { get; }

        /// <summary>Shows the bar and reads the work while it runs; hides it after.</summary>
        public void SetRunning(bool running)
        {
            _watch?.Pause();
            _watch = null;
            style.display = running ? DisplayStyle.Flex : DisplayStyle.None;
            if (running)
            {
                Read();
                _watch = schedule.Execute(Read).Every(WatchMs);
            }
        }

        private void Read()
        {
            (string label, float fraction, string detail) = VoiceLabView.MakingBar(_read());
            _meter.LabelKey = label == VoiceLabStrings.Making ? _makingKey : label;
            _meter.Progress = fraction;
            _meter.Detail = detail;
        }
    }
}
