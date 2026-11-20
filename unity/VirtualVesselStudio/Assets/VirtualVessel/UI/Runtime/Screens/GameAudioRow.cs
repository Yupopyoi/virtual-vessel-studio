using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using VirtualVessel.Audio;
using VirtualVessel.Capture;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Screens
{
    /// <summary>
    /// The game audio's device on the game capture Card (setup.md 3): the board's sound is chosen where its
    /// video is, and follows it; a game's window on this PC brings that game's own sound (audio detailed design
    /// 8.7). Capture stays video only; the UI is what joins the two (audio detailed design 18).
    /// </summary>
    public sealed class GameAudioRow : VisualElement
    {
        private readonly IAudioService _audio;
        private readonly IAudioSource _source;
        private UiServices _services;

        public GameAudioRow(IAudioService audio)
        {
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            _source = audio.Sources.First(source => source.Role == AudioSourceRole.GameAudio);
            Devices = Ui.Select(null);
            Devices.RegisterValueChangedCallback(evt => _source.SetOptions(_source.Options.Value.WithDevice(evt.newValue)));
            SettingRow row = Ui.SettingRow(CaptureStrings.Audio, CaptureStrings.AudioDescription);
            row.Add(Devices);
            Add(row);
            PanelLifetime.WhileAttached(this, Attach);
            PanelLifetime.OnLocaleChanged(this, ShowDevices);
        }

        public SelectField Devices { get; }

        /// <summary>Whether <paramref name="audio"/> has the game audio's source to choose a device for. Pure.</summary>
        public static bool CanShow(IAudioService audio)
        {
            return audio?.Sources != null && audio.Sources.Any(source => source.Role == AudioSourceRole.GameAudio);
        }

        /// <summary>
        /// The input device that belongs to a video device, or null. Windows names a board's sound after the
        /// board, as in "Digital Audio Interface (MiraBox Capture)", in the system's language. Pure.
        /// </summary>
        public static string Find(IReadOnlyList<AudioDevice> devices, string videoName)
        {
            string video = Comparable(videoName);
            return video.Length == 0 ? null : devices.Where(device => Comparable(device.Name).Contains(video)).Select(device => device.Id).FirstOrDefault();
        }

        /// <summary>
        /// The sound that belongs to a video device: a window's program's own sound, or the input device named
        /// after a capture board; null when there is none. Pure.
        /// </summary>
        public static string Belonging(IReadOnlyList<AudioDevice> devices, string videoId, string videoName)
        {
            string program = CaptureWindow.ProgramOf(videoId);
            return program != null ? AudioApplication.IdOf(program) : Find(devices, videoName);
        }

        /// <summary>
        /// The audio device after the video device changed. A choice that has nothing to do with the old video
        /// device is the user's own, and stays; a window's sound does not stay without its window. Pure.
        /// </summary>
        public static string Follow(IReadOnlyList<AudioDevice> devices, string chosen, string previousVideoId, string previousVideo, string nextVideoId, string nextVideo)
        {
            bool followed = string.IsNullOrEmpty(chosen) || chosen == Belonging(devices, previousVideoId, previousVideo);
            if (!followed)
            {
                return chosen;
            }

            string next = Belonging(devices, nextVideoId, nextVideo);
            return next ?? (AudioApplication.IsApplication(chosen) ? null : chosen);
        }

        /// <summary>
        /// Whether the game audio is heard on the monitor after its device changed. A game on this PC is heard from
        /// the PC already, so its sound is not sent again, or it would be heard twice; back on a capture board it is
        /// heard again (audio detailed design 8.7). Otherwise the user's choice stays. Pure.
        /// </summary>
        public static bool MonitoredAfter(bool monitored, string previousDevice, string nextDevice)
        {
            bool wasApplication = AudioApplication.IsApplication(previousDevice);
            bool isApplication = AudioApplication.IsApplication(nextDevice);
            return isApplication ? false : wasApplication ? true : monitored;
        }

        /// <summary>Called when the user chooses another video device.</summary>
        public void VideoChanged(string previousId, string previousName, string nextId, string nextName)
        {
            AudioSourceOptions options = _source.Options.Value;
            string chosen = Follow(_audio.InputDevices.Value, options.DeviceId, previousId, previousName, nextId, nextName);
            if (chosen != options.DeviceId)
            {
                _source.SetOptions(options.WithDevice(chosen).WithMonitored(MonitoredAfter(options.Monitored, options.DeviceId, chosen)));
            }
        }

        private static string Comparable(string name)
        {
            return new string((name ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }

        private IDisposable Attach(UiServices services)
        {
            _services = services;
            ShowDevices();
            return new DisposeAll(new List<IDisposable>
            {
                _audio.InputDevices.Subscribe(_ => ShowDevices()),
                _source.Options.Subscribe(_ => ShowDevices()),
            });
        }

        private void ShowDevices()
        {
            string chosen = _source.Options.Value.DeviceId;
            Devices.SetChoices(AudioCard.Choices(_audio.InputDevices.Value, chosen, AudioCard.UnlistedName(_services?.Localizer, chosen)));
            Devices.SetValueWithoutNotify(chosen ?? string.Empty);
        }
    }
}
