using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.Audio;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Screens;
using Object = UnityEngine.Object;

namespace VirtualVessel.UI.Tests
{
    /// <summary>The audio Card and the live screen's meters inside the real UI root (audio detailed design 18).</summary>
    public sealed class AudioPanelTests
    {
        private string _directory;
        private GameObject _host;
        private UiRoot _root;
        private FakeAudio _audio;
        private FakeSettings _settings;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _audio = new FakeAudio();
            _settings = new FakeSettings();
            _host = new GameObject(nameof(AudioPanelTests));
            _host.AddComponent<UIDocument>();
            _root = _host.AddComponent<UiRoot>();
            _root.SetAssets(UnityEditor.AssetDatabase.LoadAssetAtPath<UiAssets>("Assets/VirtualVessel/UI/UiAssets.asset"));
            _root.Build(new UiRootContext(_directory, new StopwatchMonotonicClock(), new UtcSystemClock(), null, null, false, screens: new ScreenServices { Audio = _audio, SystemSettings = _settings }));
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
        public IEnumerator TheCard_ChoosesTheDevice_Gain_AndMute_PerSource()
        {
            AudioCard card = OpenCard();
            yield return null;
            Assert.That(card.Groups.Count, Is.EqualTo(2));
            AudioSourceRow game = card.Groups[1];
            Assert.That(game.Devices.Choices.Select(c => c.Id), Is.EqualTo(new[] { string.Empty, "mic", "board" }), "None first, then the devices.");
            Assert.That(game.Devices.value, Is.EqualTo(string.Empty));

            game.Devices.value = "board";
            game.Gain.value = -4f;
            game.Mute.value = true;

            AudioSourceOptions options = _audio.Game.Options.Value;
            Assert.That((options.DeviceId, options.GainDb, options.Muted), Is.EqualTo(("board", -4f, true)));
            Assert.That(card.Groups[0].Devices.value, Is.EqualTo("mic"), "Each source keeps its own.");

            Assert.That(card.Delay.value, Is.EqualTo(300f), "Recording and streaming 300 ms later by default.");
            card.Delay.value = 200f;
            Assert.That(_audio.StreamDelay.Value, Is.EqualTo(TimeSpan.FromMilliseconds(200)));
        }

        [UnityTest]
        public IEnumerator TheColumns_MakeWayForTheControlsOwnNames_WhenTheCardIsNarrow()
        {
            AudioCard card = OpenCard();
            yield return null;
            card.style.width = 1000;
            yield return null;
            yield return null;
            VisualElement columns = card.Q(className: AudioCard.ColumnsClass);
            VisualElement label = card.Q(className: AudioCard.CellLabelClass);
            Assert.That(columns.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(label.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            AudioSourceRow game = card.Groups[1];
            Assert.That(game.Mute.worldBound.y, Is.EqualTo(game.Meter.worldBound.y).Within(24f), "One row per source.");
            CheckInside(card);

            card.style.width = 400;
            yield return null;
            yield return null;
            Assert.That(columns.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(label.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            CheckInside(card);
        }

        [Test]
        public void TheDevice_IsOnlyShown_WhenTheGameCaptureCardChoosesIt()
        {
            var card = new AudioCard(_audio, _settings, gameDeviceOnCapture: true);
            Assert.That(card.Groups[0].Devices.enabledSelf, Is.True, "The microphone's device is chosen here.");
            Assert.That(card.Groups[1].Devices.enabledSelf, Is.False);
        }

        [Test]
        public void Find_MatchesTheVideoDevicesName_InsideTheAudioDevicesName()
        {
            var devices = new[]
            {
                new AudioDevice("camera", "マイク (USB 2.0 Camera)", false),
                new AudioDevice("board", "デジタル オーディオ インターフェイス (MiraBox Capture)", false),
            };

            Assert.That(GameAudioRow.Find(devices, "MiraBox Capture"), Is.EqualTo("board"));
            Assert.That(GameAudioRow.Find(devices, "mirabox-capture"), Is.EqualTo("board"), "Case and punctuation do not count.");
            Assert.That(GameAudioRow.Find(devices, "Other Board"), Is.Null);
            Assert.That(GameAudioRow.Find(devices, null), Is.Null);
        }

        [Test]
        public void Follow_KeepsAChoiceThatIsTheUsersOwn()
        {
            var devices = new[]
            {
                new AudioDevice("line", "Line In", false),
                new AudioDevice("a", "Audio (Board A)", false),
                new AudioDevice("b", "Audio (Board B)", false),
            };

            Assert.That(GameAudioRow.Follow(devices, string.Empty, null, "Board A"), Is.EqualTo("a"), "Nothing chosen yet.");
            Assert.That(GameAudioRow.Follow(devices, "a", "Board A", "Board B"), Is.EqualTo("b"), "It followed the old board.");
            Assert.That(GameAudioRow.Follow(devices, "line", "Board A", "Board B"), Is.EqualTo("line"), "Chosen on purpose.");
            Assert.That(GameAudioRow.Follow(devices, "a", "Board A", "Webcam"), Is.EqualTo("a"), "No sound for the new one: nothing to go to.");
        }

        [UnityTest]
        public IEnumerator TheGameCaptureCard_ChoosesTheAudioWithTheVideo()
        {
            var video = new LiveSetupPanelTests.FakeDeviceSource("game", VirtualVessel.Capture.CaptureSourceKind.GameCapture);
            var row = new GameAudioRow(_audio);
            var card = new CaptureSourceCard(video, CaptureStrings.GameTitle, CaptureStrings.ChooseCamera, row);
            _root.Content.Add(card);
            yield return null;
            Assert.That(row.Devices.value, Is.EqualTo(string.Empty));

            card.Devices.value = "Board A";
            yield return null;
            Assert.That(_audio.Game.Options.Value.DeviceId, Is.EqualTo("board"));
            Assert.That(row.Devices.value, Is.EqualTo("board"), "The row shows what the source now uses.");
            Assert.That(video.SelectedDeviceId, Is.EqualTo("Board A"));

            row.Devices.value = string.Empty;
            Assert.That(_audio.Game.Options.Value.DeviceId, Is.Null, "It can be chosen by hand here too.");
        }

        [UnityTest]
        public IEnumerator TheMonitor_HasItsOwnOutputAndVolume_AndEachSourceChoosesToBeHeard()
        {
            AudioCard card = OpenCard();
            yield return null;
            Assert.That(card.Monitor.Devices.value, Is.EqualTo("speakers"));

            card.Monitor.Devices.value = "phones";
            card.Monitor.Gain.value = -12f;
            Assert.That(_audio.Monitor.Options.Value.DeviceId, Is.EqualTo("phones"));
            Assert.That(_audio.Monitor.Options.Value.GainDb, Is.EqualTo(-12f));

            card.Groups[1].Monitored.value = true;
            Assert.That(_audio.Game.Options.Value.Monitored, Is.True);
            Assert.That(_audio.Microphone.Options.Value.Monitored, Is.False, "Only the chosen source.");
        }

        [UnityTest]
        public IEnumerator AProblem_SaysWhatToDo_AndDeniedAccessOpensWindowsSettings()
        {
            AudioCard card = OpenCard();
            AudioSourceRow microphone = card.Groups[0];
            _audio.Microphone.Report(AudioSourceState.Running, AudioProblem.None);
            yield return null;
            Assert.That((microphone.Status.Status, microphone.Status.TextKey), Is.EqualTo((StatusKind.Success, AudioStrings.Running)));
            Assert.That(microphone.PrivacyButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));

            _audio.Microphone.Report(AudioSourceState.Error, AudioProblem.AccessDenied);
            yield return null;
            Assert.That((microphone.Status.Status, microphone.ProblemKey), Is.EqualTo((StatusKind.Error, AudioStrings.ProblemAccessDenied)), "A shape and text, never color alone.");
            Assert.That(microphone.PrivacyButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Submit(microphone.PrivacyButton);
            yield return null;
            Assert.That(_settings.Opened, Is.EqualTo(1));

            _audio.Microphone.Report(AudioSourceState.Unavailable, AudioProblem.None);
            yield return null;
            Assert.That(microphone.ProblemKey, Is.EqualTo(AudioStrings.ProblemNotFound));
            Assert.That(microphone.PrivacyButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator TheLiveScreen_ShowsTheLevels_AndMutesFromTheButton()
        {
            _root.Shell.Open("live");
            yield return null;
            AudioStrip strip = _root.Content.Q<LiveScreen>().Audio;
            Assert.That(strip, Is.Not.Null);
            Assert.That(strip.MuteButtons.Count, Is.EqualTo(2));

            _audio.Microphone.Peak = -6f;
            yield return new WaitForSeconds(0.25f);
            Assert.That(strip.Meters[0].Level, Is.EqualTo(0.9f).Within(0.01f));

            IconButton mute = strip.MuteButtons[0];
            Assert.That(mute.IconName, Is.EqualTo(IconName.Mic));
            Submit(mute);
            yield return null;
            Assert.That(_audio.Microphone.Options.Value.Muted, Is.True);
            Assert.That(mute.IconName, Is.EqualTo(IconName.MicOff), "Muted shows by the icon's shape.");
            Assert.That(mute.TooltipKey, Is.EqualTo(AudioStrings.LiveUnmute));
        }

        [UnityTest]
        public IEnumerator TheCard_KeepsItsControlsInside_AndThePrivacyMessageReadable()
        {
            AudioCard card = OpenCard();
            card.Q<Card>().Expanded = true;
            _audio.Microphone.Report(AudioSourceState.Error, AudioProblem.AccessDenied);
            yield return null;
            yield return null;
            CheckInside(card);
            card.style.width = 900;
            card.style.flexShrink = 0;
            yield return null;
            yield return null;
            CheckInside(card);
        }

        [Test]
        public void Levels_States_AndChoices_AreShownAsTheyAre()
        {
            Assert.That(AudioCard.LevelOf(float.NegativeInfinity), Is.EqualTo(0f));
            Assert.That(AudioCard.LevelOf(-60f), Is.EqualTo(0f));
            Assert.That(AudioCard.LevelOf(0f), Is.EqualTo(1f));
            Assert.That(AudioCard.LevelOf(6f), Is.EqualTo(1f));
            Assert.That(AudioCard.Describe(AudioSourceState.Running, AudioProblem.None, muted: true).Kind, Is.EqualTo(StatusKind.Warning), "Muted is not mistaken for silence.");
            Assert.That(AudioCard.Describe(AudioSourceState.Error, AudioProblem.InUse, false).ProblemKey, Is.EqualTo(AudioStrings.ProblemInUse));
            Assert.That(AudioCard.Describe(AudioSourceState.Off, AudioProblem.None, false), Is.EqualTo((StatusKind.Idle, AudioStrings.Off, (string)null)));

            var devices = new[] { new AudioDevice("a", "A", true) };
            Assert.That(AudioCard.Choices(devices, "gone", "Not connected").Select(c => c.Id), Is.EqualTo(new[] { string.Empty, "a", "gone" }), "An unplugged choice is kept.");
            Assert.That(AudioCard.Choices(devices, "a", "Not connected").Count, Is.EqualTo(2));
        }

        private AudioCard OpenCard()
        {
            _root.Shell.Open("setup");
            ((SetupScreen)_root.Shell.Host.GetScreen("setup")).Tabs.value = SetupScreen.AudioPage;
            return _root.Content.Q<AudioCard>();
        }

        private static void CheckInside(AudioCard card)
        {
            float right = card.Q<Card>().contentContainer.worldBound.xMax;
            Assert.That(card.Delay.worldBound.xMax, Is.LessThanOrEqualTo(right + 0.5f), "The delay slider reaches past the Card.");
            foreach (AudioSourceRow group in card.Groups)
            {
                foreach (VisualElement element in new VisualElement[] { group.Devices, group.Gain, group.Mute, group.Meter, group.Problem, group.PrivacyButton, group.Status })
                {
                    if (element.resolvedStyle.display == DisplayStyle.Flex)
                    {
                        Assert.That(element.worldBound.xMax, Is.LessThanOrEqualTo(right + 0.5f), $"{element.GetType().Name} reaches past the Card.");
                    }
                }

                Assert.That(group.Problem.resolvedStyle.color, Is.Not.EqualTo(Color.black), "The message is in a text color.");
            }
        }

        private static void Submit(VisualElement target)
        {
            using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        private sealed class FakeSource : IAudioSource
        {
            private readonly ObservableValue<AudioSourceState> _state = new ObservableValue<AudioSourceState>(AudioSourceState.Off);
            private readonly ObservableValue<AudioProblem> _problem = new ObservableValue<AudioProblem>(AudioProblem.None);
            private readonly ObservableValue<AudioSourceOptions> _options;

            public FakeSource(string id, AudioSourceRole role, string deviceId)
            {
                Id = id;
                Role = role;
                _options = new ObservableValue<AudioSourceOptions>(new AudioSourceOptions(deviceId, 0f, false, 0f));
            }

            public string Id { get; }

            public AudioSourceRole Role { get; }

            public IReadOnlyObservable<AudioSourceState> State => _state;

            public IReadOnlyObservable<AudioProblem> Problem => _problem;

            public IReadOnlyObservable<AudioSourceOptions> Options => _options;

            public float Peak { get; set; } = float.NegativeInfinity;

            public float PeakDb => Peak;

            public void SetOptions(AudioSourceOptions options)
            {
                _options.Set(options);
            }

            public void Report(AudioSourceState state, AudioProblem problem)
            {
                _problem.Set(problem);
                _state.Set(state);
            }
        }

        private sealed class FakeMonitor : IAudioMonitor
        {
            private readonly ObservableValue<AudioSourceState> _state = new ObservableValue<AudioSourceState>(AudioSourceState.Running);
            private readonly ObservableValue<AudioProblem> _problem = new ObservableValue<AudioProblem>(AudioProblem.None);
            private readonly ObservableValue<AudioMonitorOptions> _options = new ObservableValue<AudioMonitorOptions>(new AudioMonitorOptions("speakers", 0f, false));

            public IReadOnlyObservable<AudioSourceState> State => _state;

            public IReadOnlyObservable<AudioProblem> Problem => _problem;

            public IReadOnlyObservable<AudioMonitorOptions> Options => _options;

            public float PeakDb => -20f;

            public void SetOptions(AudioMonitorOptions options)
            {
                _options.Set(options);
            }
        }

        private sealed class FakeAudio : IAudioService
        {
            private readonly ObservableValue<IReadOnlyList<AudioDevice>> _devices = new ObservableValue<IReadOnlyList<AudioDevice>>(new[]
            {
                new AudioDevice("mic", "Microphone", true),
                new AudioDevice("board", "Digital Audio Interface (Board A)", false),
            });

            public FakeAudio()
            {
                Microphone = new FakeSource("m", AudioSourceRole.Microphone, "mic");
                Game = new FakeSource("g", AudioSourceRole.GameAudio, null);
            }

            public FakeSource Microphone { get; }

            public FakeSource Game { get; }

            public IReadOnlyList<IAudioSource> Sources => new IAudioSource[] { Microphone, Game };

            public IReadOnlyObservable<IReadOnlyList<AudioDevice>> InputDevices => _devices;

            public IReadOnlyObservable<IReadOnlyList<AudioDevice>> OutputDevices { get; } = new ObservableValue<IReadOnlyList<AudioDevice>>(new[] { new AudioDevice("speakers", "Speakers", true), new AudioDevice("phones", "Headphones", false) });

            public IAudioMonitor Monitor { get; } = new FakeMonitor();

            public TimeSpan StreamLatency => TimeSpan.FromMilliseconds(30);

            public AudioStreamTap RecordingTap => default;

            public AudioStreamTap StreamingTap => default;

            private readonly ObservableValue<TimeSpan> _delay = new ObservableValue<TimeSpan>(TimeSpan.FromMilliseconds(300));

            public IReadOnlyObservable<TimeSpan> StreamDelay => _delay;

            public void SetStreamDelay(TimeSpan delay)
            {
                _delay.Set(delay);
            }

            public void RefreshDevices()
            {
            }
        }

        private sealed class FakeSettings : ISystemSettings
        {
            public int Opened { get; private set; }

            public void OpenMicrophonePrivacy()
            {
                Opened++;
            }
        }
    }
}
