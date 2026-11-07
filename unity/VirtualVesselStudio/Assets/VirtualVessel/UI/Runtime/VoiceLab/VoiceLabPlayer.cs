using System;
using System.Threading.Tasks;
using UnityEngine.UIElements;
using VirtualVessel.Audio;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Icons;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// Plays candidates and previews to the user (Audio's monitor bus, never the stream) and says when one
    /// ends, so a button can show Play again. One sample at a time.
    /// </summary>
    public sealed class VoiceLabPlayer
    {
        private const long WatchMs = 200;

        private readonly IAudioPreview _audio;
        private readonly VisualElement _owner;
        private Action _finished;
        private IVisualElementScheduledItem _watch;
        private IconButton _button;

        public VoiceLabPlayer(IAudioPreview audio, VisualElement owner)
        {
            _audio = audio;
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public bool Available => _audio != null;

        /// <summary>Plays <paramref name="file"/>, stopping the one before; <paramref name="finished"/> runs when it ends or is replaced.</summary>
        public async Task<bool> PlayAsync(string file, Action finished)
        {
            Stop();
            if (_audio == null || file == null)
            {
                finished?.Invoke();
                return false;
            }

            _finished = finished;
            bool playing = await _audio.PlayAsync(file);
            if (_finished != finished)
            {
                // Another sample started or Stop ran while this one was being read.
                return playing;
            }

            if (!playing)
            {
                Finish();
                return false;
            }

            _watch = _owner.schedule.Execute(Watch).Every(WatchMs);
            return true;
        }

        /// <summary>
        /// Plays <paramref name="file"/> from a play button, which shows Stop while it plays; pressing the
        /// playing button again stops it. A null file does nothing.
        /// </summary>
        public async void Toggle(IconButton button, string file)
        {
            if (_button == button)
            {
                Stop();
                return;
            }

            if (file == null)
            {
                return;
            }

            _button = button;
            button.IconName = IconName.Stop;
            await PlayAsync(file, () =>
            {
                button.IconName = IconName.Play;
                if (_button == button)
                {
                    _button = null;
                }
            });
        }

        public void Stop()
        {
            if (_finished != null)
            {
                _audio?.Stop();
            }

            Finish();
        }

        /// <summary>Whether a preview's progress means it has ended. Pure.</summary>
        public static bool HasEnded(float? progress)
        {
            return progress == null || progress.Value >= 1f;
        }

        private void Watch()
        {
            if (HasEnded(_audio.Progress))
            {
                Finish();
            }
        }

        private void Finish()
        {
            _watch?.Pause();
            _watch = null;
            Action finished = _finished;
            _finished = null;
            finished?.Invoke();
        }
    }
}
