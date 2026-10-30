using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Time;

namespace VirtualVessel.Audio.Tests
{
    public sealed class AudioSettingsTests
    {
        private static readonly AudioDevice[] s_devices =
        {
            new AudioDevice("usb", "USB Microphone", false),
            new AudioDevice("headset", "Headset", true),
        };

        private static Func<string> Ids()
        {
            int next = 0;
            return () => (++next).ToString("x32");
        }

        [Test]
        public void FirstUse_HasTheMicrophoneOnTheDefaultDevice_AndTheGameAudioOnNone()
        {
            AudioSettings prepared = AudioSettings.Prepared(new AudioSettings(), s_devices, Ids());

            Assert.That(prepared.Sources.Select(s => s.Role), Is.EqualTo(new[] { AudioSettings.MicrophoneRole, AudioSettings.GameAudioRole }));
            Assert.That(prepared.Sources[0].DeviceId, Is.EqualTo("headset"), "Windows' default input device.");
            Assert.That(prepared.Sources[1].DeviceId, Is.Null, "A capture board must be chosen.");
            Assert.That(prepared.Sources[1].GainDb, Is.EqualTo(AudioSettings.GameAudioGainDb));
            Assert.That(prepared.Sources.All(s => s.Sends[AudioSettings.StreamBus] == 0f), Is.True, "Both go to the stream.");
            Assert.That(prepared.Sources.Select(s => s.Id).Distinct().Count(), Is.EqualTo(2));
        }

        [Test]
        public void FirstUse_HearsTheGame_NotThemselves_OnTheDefaultOutput()
        {
            var outputs = new[] { new AudioDevice("speakers", "Speakers", false), new AudioDevice("phones", "Headphones", true) };

            AudioSettings prepared = AudioSettings.Prepared(new AudioSettings(), s_devices, Ids(), outputs);

            Assert.That(prepared.Monitor.DeviceId, Is.EqualTo("phones"));
            Assert.That(prepared.Sources[0].ToOptions().Monitored, Is.False, "One's own voice echoes back, and leaks into the microphone from speakers.");
            Assert.That(prepared.Sources[1].ToOptions().Monitored, Is.True);
        }

        [Test]
        public void ASavedChoice_OfNoMonitor_StaysNone()
        {
            var saved = new AudioSettings { Monitor = new AudioMonitorSettings { DeviceId = null, GainDb = -6f } };

            AudioSettings prepared = AudioSettings.Prepared(saved, s_devices, Ids(), new[] { new AudioDevice("speakers", "Speakers", true) });

            Assert.That(prepared.Monitor.DeviceId, Is.Null);
            Assert.That(prepared.Monitor.GainDb, Is.EqualTo(-6f));
        }

        [Test]
        public void TheMonitorSend_GoesBothWays()
        {
            var saved = new AudioSourceSettings { Id = "a", Role = AudioSettings.GameAudioRole };

            AudioSourceOptions on = saved.With(new AudioSourceOptions(null, 0f, false, 0f).WithMonitored(true)).ToOptions();
            AudioSourceOptions off = saved.With(on.WithMonitored(false)).ToOptions();

            Assert.That(on.MonitorSendDb, Is.EqualTo(0f));
            Assert.That(off.Monitored, Is.False);
            Assert.That(saved.With(off).Sends[AudioSettings.MonitorBus], Is.EqualTo(AudioSettings.NoSendDb), "JSON cannot write negative infinity.");
        }

        [Test]
        public void SavedSources_AreKept_IncludingRolesThisVersionDoesNotKnow()
        {
            var saved = new AudioSettings
            {
                Sources = new List<AudioSourceSettings>
                {
                    new AudioSourceSettings { Id = "a", Role = AudioSettings.MicrophoneRole, DeviceId = "usb", GainDb = 3, Muted = true, Sends = new Dictionary<string, float> { [AudioSettings.StreamBus] = -3f } },
                    new AudioSourceSettings { Id = "b", Role = "music", DeviceId = null },
                },
            };

            AudioSettings prepared = AudioSettings.Prepared(saved, s_devices, Ids());

            Assert.That(prepared.Sources.Select(s => s.Id).Take(2), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(prepared.Sources[0].DeviceId, Is.EqualTo("usb"), "A chosen device is not replaced by the default.");
            Assert.That(prepared.Sources[2].Role, Is.EqualTo(AudioSettings.GameAudioRole), "The missing role is added.");
            Assert.That(AudioSettings.RoleOf("music"), Is.Null);
            Assert.That(saved.Sources.Count, Is.EqualTo(2), "The original is not changed.");
        }

        [Test]
        public void Options_AndTheSavedSource_GoBothWays()
        {
            var saved = new AudioSourceSettings { Id = "a", Role = AudioSettings.MicrophoneRole, DeviceId = "usb", GainDb = 40, Sends = new Dictionary<string, float> { [AudioSettings.StreamBus] = -130f, ["monitor"] = -6f } };

            AudioSourceOptions options = saved.ToOptions();

            Assert.That(options.GainDb, Is.EqualTo(AudioSourceOptions.MaxGainDb), "A gain edited out of range is kept in range.");
            Assert.That(float.IsNegativeInfinity(options.StreamSendDb), Is.True, "-120 dB or below is none.");

            AudioSourceSettings next = saved.With(options.WithMuted(true).WithDevice(""));
            Assert.That((next.DeviceId, next.Muted, next.Sends[AudioSettings.StreamBus], next.Sends["monitor"]), Is.EqualTo(((string)null, true, -120f, -6f)), "JSON cannot hold -inf; other buses are kept.");
            Assert.That(options.WithGain(-99).GainDb, Is.EqualTo(AudioSourceOptions.MinGainDb));
        }
    }

    public sealed class AudioServiceTests
    {
        private FakeEngine _engine;
        private FrameLoop _loop;
        private ManualClock _clock;

        [SetUp]
        public void SetUp()
        {
            _engine = new FakeEngine();
            _loop = new FrameLoop();
            _clock = new ManualClock();
        }

        [Test]
        public void TheSources_AreBuiltInTheEngine_MicrophoneFirst()
        {
            using AudioService service = Start(new AudioSettings());

            Assert.That(service.Sources.Select(s => s.Role), Is.EqualTo(new[] { AudioSourceRole.Microphone, AudioSourceRole.GameAudio }));
            Assert.That(_engine.Devices[0], Is.EqualTo("default-mic"));
            Assert.That(_engine.Gains[1], Is.EqualTo((AudioSettings.GameAudioGainDb, false)));
            Assert.That(service.InputDevices.Value.Count, Is.EqualTo(2));
        }

        [Test]
        public void ARoleThisVersionDoesNotKnow_IsNotPlayed()
        {
            var saved = new AudioSettings { Sources = new List<AudioSourceSettings> { new AudioSourceSettings { Id = "m", Role = "music" } } };

            using AudioService service = Start(saved);

            Assert.That(service.Sources.Count, Is.EqualTo(2));
            Assert.That(_engine.Added, Is.EqualTo(2));
        }

        [Test]
        public void Changes_ReachTheEngine()
        {
            using AudioService service = Start(new AudioSettings());
            IAudioSource game = service.Sources[1];

            game.SetOptions(game.Options.Value.WithDevice("capture-board").WithGain(-3).WithMuted(true));

            Assert.That(_engine.Devices[1], Is.EqualTo("capture-board"));
            Assert.That(_engine.Gains[1], Is.EqualTo((-3f, true)));
            Assert.That(game.Options.Value.DeviceId, Is.EqualTo("capture-board"));
        }

        [Test]
        public void TheMonitor_StartsOnTheDefaultOutput_AndFollowsTheUser()
        {
            using AudioService service = Start(new AudioSettings());
            Assert.That(_engine.MonitorDevice, Is.EqualTo("speakers"));
            Assert.That(_engine.MonitorSends[0], Is.EqualTo(float.NegativeInfinity), "The microphone is not heard.");
            Assert.That(_engine.MonitorSends[1], Is.EqualTo(0f), "The game is.");

            service.Monitor.SetOptions(service.Monitor.Options.Value.WithDevice("phones").WithGain(-12f).WithMuted(true));
            service.Sources[0].SetOptions(service.Sources[0].Options.Value.WithMonitored(true));

            Assert.That(_engine.MonitorDevice, Is.EqualTo("phones"));
            Assert.That(_engine.MonitorGain, Is.EqualTo((-12f, true)));
            Assert.That(_engine.MonitorSends[0], Is.EqualTo(0f));
        }

        [Test]
        public void TheMonitorsState_IsReadWithTheSources()
        {
            using AudioService service = Start(new AudioSettings());

            _engine.MonitorStatus = new AudioEngineStatus(AudioSourceState.Unavailable, AudioProblem.None, 0, -20f, 0, 0, 0);
            _loop.Tick(e => throw e);

            Assert.That(service.Monitor.State.Value, Is.EqualTo(AudioSourceState.Unavailable));
            Assert.That(service.Monitor.PeakDb, Is.EqualTo(-20f));
        }

        [Test]
        public void StatesAndLevels_AreReadEveryTenthOfASecond()
        {
            using AudioService service = Start(new AudioSettings());
            IAudioSource microphone = service.Sources[0];
            var seen = new List<AudioSourceState>();
            microphone.State.Subscribe(seen.Add);

            _engine.Statuses[0] = new AudioEngineStatus(AudioSourceState.Running, AudioProblem.None, 0, -12f, 0, 0, 0);
            _loop.Tick(e => throw e);
            Assert.That(microphone.PeakDb, Is.EqualTo(-12f));

            _engine.Statuses[0] = new AudioEngineStatus(AudioSourceState.Error, AudioProblem.AccessDenied, unchecked((int)0x80070005), -60f, 0, 0, 0);
            _clock.Advance(0.05);
            _loop.Tick(e => throw e);
            Assert.That(microphone.State.Value, Is.EqualTo(AudioSourceState.Running), "Not before a tenth of a second.");

            _clock.Advance(0.06);
            _loop.Tick(e => throw e);
            Assert.That((microphone.State.Value, microphone.Problem.Value), Is.EqualTo((AudioSourceState.Error, AudioProblem.AccessDenied)));
            Assert.That(seen, Does.Contain(AudioSourceState.Running));
        }

        [Test]
        public void TheStreamDelay_StartsAt300Ms_AndReachesTheEngine()
        {
            using AudioService service = Start(new AudioSettings());
            Assert.That(service.StreamDelay.Value, Is.EqualTo(TimeSpan.FromMilliseconds(300)), "The user's default.");
            Assert.That(_engine.Delay, Is.EqualTo(TimeSpan.FromMilliseconds(300)));

            service.SetStreamDelay(TimeSpan.FromMilliseconds(5000));
            Assert.That(_engine.Delay, Is.EqualTo(TimeSpan.FromMilliseconds(1000)), "At most a second.");
            service.SetStreamDelay(TimeSpan.FromMilliseconds(-20));
            Assert.That(service.StreamDelay.Value, Is.EqualTo(TimeSpan.Zero));
        }

        [Test]
        public void ASavedDelay_IsUsed()
        {
            using AudioService service = Start(new AudioSettings { StreamDelayMs = 120 });

            Assert.That(_engine.Delay, Is.EqualTo(TimeSpan.FromMilliseconds(120)));
        }

        [Test]
        public void TheEngine_IsReleasedWhenTheServiceIsDisposed()
        {
            AudioService service = Start(new AudioSettings());
            service.ShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();
            service.Dispose();

            Assert.That(_engine.Disposed, Is.True);
        }

        [Test]
        public void TheLog_NamesDevices_NotTheirIds()
        {
            var devices = new[] { new AudioDevice("{0.0.1}.{abc}", "Microphone (USB)", true) };

            Assert.That(AudioService.DeviceName("{0.0.1}.{abc}", devices), Is.EqualTo("Microphone (USB)"));
            Assert.That(AudioService.DeviceName("{gone}", devices), Is.EqualTo("a device not connected now"));
            Assert.That(AudioService.DeviceName(null, devices), Is.EqualTo("no device"));
        }

        [Test]
        public void NativeCodes_MapToStatesAndProblems()
        {
            Assert.That(Enumerable.Range(0, 5).Select(NativeAudioEngine.StateOf), Is.EqualTo(new[] { AudioSourceState.Off, AudioSourceState.Starting, AudioSourceState.Running, AudioSourceState.Unavailable, AudioSourceState.Error }));
            Assert.That(NativeAudioEngine.ProblemOf(2), Is.EqualTo(AudioProblem.AccessDenied));
        }

        private AudioService Start(AudioSettings settings)
        {
            int next = 0;
            var service = new AudioService(settings, null, () => _engine, _loop, _clock, () => (++next).ToString("x32"), null, null);
            service.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            return service;
        }

        private sealed class FakeEngine : IAudioEngine
        {
            public Dictionary<int, string> Devices { get; } = new Dictionary<int, string>();

            public Dictionary<int, (float, bool)> Gains { get; } = new Dictionary<int, (float, bool)>();

            public Dictionary<int, AudioEngineStatus> Statuses { get; } = new Dictionary<int, AudioEngineStatus>();

            public int Added { get; private set; }

            public bool Disposed { get; private set; }

            public TimeSpan StreamLatency => TimeSpan.FromMilliseconds(30);

            public AudioStreamTap StreamTap => default;

            public IReadOnlyList<AudioDevice> Outputs { get; set; } = new[] { new AudioDevice("speakers", "Speakers", true) };

            public string MonitorDevice { get; private set; }

            public (float GainDb, bool Muted) MonitorGain { get; private set; }

            public Dictionary<int, float> MonitorSends { get; } = new Dictionary<int, float>();

            public AudioEngineStatus MonitorStatus { get; set; }

            public IReadOnlyList<AudioDevice> ListOutputs()
            {
                return Outputs;
            }

            public void SetMonitorSend(int source, float sendDb)
            {
                MonitorSends[source] = sendDb;
            }

            public void SetMonitorDevice(string deviceId)
            {
                MonitorDevice = deviceId;
            }

            public void SetMonitorGain(float gainDb, bool muted)
            {
                MonitorGain = (gainDb, muted);
            }

            public AudioEngineStatus GetMonitorStatus()
            {
                return MonitorStatus;
            }

            public IReadOnlyList<AudioDevice> ListInputs()
            {
                return new[] { new AudioDevice("default-mic", "Microphone", true), new AudioDevice("capture-board", "Capture board", false) };
            }

            public int AddSource()
            {
                return Added++;
            }

            public void RemoveSource(int source)
            {
            }

            public void SetDevice(int source, string deviceId)
            {
                Devices[source] = deviceId;
            }

            public void SetGain(int source, float gainDb, bool muted)
            {
                Gains[source] = (gainDb, muted);
            }

            public void SetStreamSend(int source, float sendDb)
            {
            }

            public TimeSpan Delay { get; private set; }

            public void SetStreamDelay(TimeSpan delay)
            {
                Delay = delay;
            }

            public AudioEngineStatus GetStatus(int source)
            {
                return Statuses.TryGetValue(source, out AudioEngineStatus status) ? status : default;
            }

            public void Dispose()
            {
                Disposed = true;
            }
        }

        private sealed class ManualClock : IMonotonicClock
        {
            private long _now = 1_000_000;

            public long Frequency => 1_000_000;

            public long GetTimestamp()
            {
                return _now;
            }

            public TimeSpan GetElapsed(long startTimestamp, long endTimestamp)
            {
                return TimeSpan.FromTicks((endTimestamp - startTimestamp) * 10);
            }

            public void Advance(double seconds)
            {
                _now += (long)(seconds * Frequency);
            }
        }
    }
}
