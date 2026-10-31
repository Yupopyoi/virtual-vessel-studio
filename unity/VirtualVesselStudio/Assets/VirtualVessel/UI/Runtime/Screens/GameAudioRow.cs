using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using VirtualVessel.Audio;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Screens
{
    /// <summary>
    /// The game audio's device on the game capture Card (setup.md 3): the board's sound is chosen where its
    /// video is, and follows it. Capture stays video only; the UI is what joins the two (audio detailed design 18).
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
        }

        public SelectField Devices { get; }

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
        /// The audio device after the video device changed from one name to another. A choice that has nothing
        /// to do with the old video device is the user's own, and stays. Pure.
        /// </summary>
        public static string Follow(IReadOnlyList<AudioDevice> devices, string chosen, string previousVideo, string nextVideo)
        {
            string next = Find(devices, nextVideo);
            bool followed = string.IsNullOrEmpty(chosen) || chosen == Find(devices, previousVideo);
            return next != null && followed ? next : chosen;
        }

        /// <summary>Called when the user chooses another video device.</summary>
        public void VideoChanged(string previousName, string nextName)
        {
            AudioSourceOptions options = _source.Options.Value;
            string chosen = Follow(_audio.InputDevices.Value, options.DeviceId, previousName, nextName);
            if (chosen != options.DeviceId)
            {
                _source.SetOptions(options.WithDevice(chosen));
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
            string missing = _services?.Localizer?.Get(AudioStrings.MissingDevice) ?? AudioStrings.MissingDevice;
            string chosen = _source.Options.Value.DeviceId;
            Devices.SetChoices(AudioCard.Choices(_audio.InputDevices.Value, chosen, missing));
            Devices.SetValueWithoutNotify(chosen ?? string.Empty);
        }
    }
}
