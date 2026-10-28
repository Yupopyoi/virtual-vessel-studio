using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VirtualVessel.Audio;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Icons;

namespace VirtualVessel.UI.Screens
{
    /// <summary>
    /// The sound going in, on the live screen next to recording (audio detailed design 18; live.md 3): per
    /// source a mute button, its name, and a compact meter, so the user sees the voice and the game are heard
    /// while recording. The button's icon shows mute by its shape (crossed out), not by color alone.
    /// </summary>
    public sealed class AudioStrip : VisualElement
    {
        public const string UssClass = "vv-audio-strip";
        public const string SourceClass = UssClass + "__source";
        public const string MeterClass = UssClass + "__meter";

        private readonly List<Entry> _entries = new List<Entry>();

        public AudioStrip(IAudioService audio)
        {
            if (audio == null)
            {
                throw new ArgumentNullException(nameof(audio));
            }

            AddToClassList(UssClass);
            foreach (IAudioSource source in audio.Sources)
            {
                var entry = new Entry(source);
                _entries.Add(entry);
                Add(entry.Root);
            }

            PanelLifetime.WhileAttached(this, _ =>
            {
                var subscriptions = new List<IDisposable>();
                foreach (Entry entry in _entries)
                {
                    subscriptions.Add(entry.Attach());
                }

                return new DisposeAll(subscriptions);
            });
        }

        /// <summary>The mute button of each source, the microphone first.</summary>
        public IReadOnlyList<IconButton> MuteButtons => _entries.ConvertAll(entry => entry.Mute);

        public IReadOnlyList<LevelMeter> Meters => _entries.ConvertAll(entry => entry.Meter);

        private sealed class Entry
        {
            private readonly IAudioSource _source;

            public Entry(IAudioSource source)
            {
                _source = source;
                Root = new VisualElement();
                Root.AddToClassList(SourceClass);
                Mute = Ui.IconButton(IconName.Mic, toggle: true);
                Mute.RegisterCallback<ChangeEvent<bool>>(evt => _source.SetOptions(_source.Options.Value.WithMuted(evt.newValue)));
                Root.Add(Mute);
                Root.Add(Ui.Text(source.Role == AudioSourceRole.Microphone ? AudioStrings.LiveMicrophone : AudioStrings.LiveGameAudio, "vv-text-caption"));
                Meter = new LevelMeter { Segments = 16 };
                Meter.AddToClassList(MeterClass);
                Root.Add(Meter);
                Show(_source.Options.Value);
            }

            public VisualElement Root { get; }

            public IconButton Mute { get; }

            public LevelMeter Meter { get; }

            public IDisposable Attach()
            {
                IVisualElementScheduledItem level = Root.schedule.Execute(() => Meter.Level = AudioCard.LevelOf(_source.PeakDb)).Every(LevelMeter.DefaultIntervalMs * 3);
                return new DisposeAll(new List<IDisposable>
                {
                    new DisposeAction(level.Pause),
                    _source.Options.Subscribe(Show),
                });
            }

            private void Show(AudioSourceOptions options)
            {
                Mute.SetValueWithoutNotify(options.Muted);
                Mute.IconName = options.Muted ? IconName.MicOff : IconName.Mic;
                Mute.TooltipKey = options.Muted ? AudioStrings.LiveUnmute : AudioStrings.LiveMute;
            }
        }
    }
}
