using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Credentials;
using VirtualVessel.UI.Streaming;
using VirtualVessel.Video;
using VirtualVessel.Video.Recording;

namespace VirtualVessel.Streaming.Tests
{
    public sealed class StreamDestinationTests
    {
        [Test]
        public void Defaults_AreYouTube_WithAv1WhereTheGpuHasIt()
        {
            StreamingOptions withAv1 = StreamDestination.OptionsOf(new StreamSettings(), false, true);
            StreamingOptions without = StreamDestination.OptionsOf(new StreamSettings(), false, false);

            Assert.That(withAv1.ServerUrl, Is.EqualTo(StreamDestination.YouTubeUrl));
            Assert.That((withAv1.Codec, withAv1.Av1Available), Is.EqualTo((StreamingCodec.Av1, true)));
            Assert.That((without.Codec, without.Av1Available), Is.EqualTo((StreamingCodec.H264, false)));
            Assert.That(withAv1.VideoBitrate, Is.EqualTo(6_000_000));
        }

        [Test]
        public void ACustomServer_IsH264_EvenWithAv1Chosen()
        {
            var settings = new StreamSettings { Destination = "custom", VideoCodec = "av1", ServerUrl = "rtmp://server/live" };

            StreamingOptions options = StreamDestination.OptionsOf(settings, false, true);

            Assert.That((options.Codec, options.Av1Available), Is.EqualTo((StreamingCodec.H264, false)));
            Assert.That(options.ServerUrl, Is.EqualTo("rtmp://server/live"));
        }

        [Test]
        public void With_LeavesTheDefaultsUnmade_SoTheyFollowThePc()
        {
            var settings = new StreamSettings();
            StreamingOptions options = StreamDestination.OptionsOf(settings, false, true);

            StreamSettings next = StreamDestination.With(settings, options, true);

            Assert.That(next.ServerUrl, Is.Null);
            Assert.That(next.VideoCodec, Is.Null);
        }

        [Test]
        public void With_KeepsWhatTheUserChose_AndNeverChangesTheOriginal()
        {
            var settings = new StreamSettings();
            StreamingOptions options = StreamDestination.OptionsOf(settings, false, true).WithCodec(StreamingCodec.H264).WithVideoBitrate(99_000_000).WithServerUrl(" rtmps://other/live ");

            StreamSettings next = StreamDestination.With(settings, options, true);

            Assert.That((next.VideoCodec, next.VideoBitrate, next.ServerUrl), Is.EqualTo(("h264", StreamDestination.MaxVideoBitrate, "rtmps://other/live")));
            Assert.That(settings.VideoCodec, Is.Null);
        }

        [Test]
        public void ChangingTheDestination_DropsTheOldUrl()
        {
            var settings = new StreamSettings { ServerUrl = "rtmps://other/live" };
            StreamingOptions options = StreamDestination.OptionsOf(settings, false, true).WithDestination(StreamDestinationKind.CustomRtmp);

            StreamSettings next = StreamDestination.With(settings, options, true);

            Assert.That((next.Destination, next.ServerUrl), Is.EqualTo(("custom", (string)null)));
        }

        [TestCase("rtmp://host/app", true)]
        [TestCase("rtmps://host:443/app", true)]
        [TestCase("http://host/app", false)]
        [TestCase("host/app", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void Urls_MustBeRtmp(string url, bool usable)
        {
            Assert.That(StreamDestination.IsUsableUrl(url), Is.EqualTo(usable));
        }

        [Test]
        public void OnlyTheHost_IsKeptForLogs()
        {
            Assert.That(StreamDestination.HostOf("rtmp://host.example/app/secret-in-path"), Is.EqualTo("host.example"));
        }

        [Test]
        public void Unencrypted_IsRtmpWithoutTls()
        {
            Assert.That(StreamDestination.IsUnencrypted("rtmp://host/app"), Is.True);
            Assert.That(StreamDestination.IsUnencrypted("rtmps://host/app"), Is.False);
        }

        [Test]
        public void Keys_LoseThePasteAroundThem()
        {
            Assert.That(StreamDestination.CleanKey("  abcd-efgh\r\n"), Is.EqualTo("abcd-efgh"));
            Assert.That(StreamDestination.CleanKey(null), Is.Empty);
        }
    }

    public sealed class StreamingServiceTests
    {
        private const string Key = "secret-stream-key";

        private FrameLoop _loop;
        private ManualClock _clock;
        private FakeCredentials _credentials;
        private FakePublisher _publisher;
        private FakeReader _reader;
        private List<StreamSettings> _saved;
        private List<string> _keysOpened;
        private StreamFormat _format;
        private bool _audio;
        private StreamingService _service;

        [SetUp]
        public void SetUp()
        {
            _loop = new FrameLoop();
            _clock = new ManualClock();
            _credentials = new FakeCredentials();
            _publisher = new FakePublisher();
            _reader = new FakeReader();
            _saved = new List<StreamSettings>();
            _keysOpened = new List<string>();
            _audio = true;
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Dispose();
        }

        [Test]
        public void Start_WithoutAKey_FailsWithoutOpeningThePlugin()
        {
            Create();

            _service.Start();

            Assert.That(_service.State.Value, Is.EqualTo(StreamingState.Failed));
            Assert.That(_service.Problem.Value, Is.EqualTo(StreamingProblem.NoStreamKey));
            Assert.That(_keysOpened, Is.Empty);
        }

        [Test]
        public void Start_GivesThePluginTheKey_AndFollowsItToLive()
        {
            CreateWithKey();

            _service.Start();

            Assert.That(_service.State.Value, Is.EqualTo(StreamingState.Connecting));
            Assert.That(_keysOpened, Is.EqualTo(new[] { Key }));
            Assert.That(_format.Url, Is.EqualTo(StreamDestination.YouTubeUrl));

            Advance(PublisherState.Live);

            Assert.That(_service.State.Value, Is.EqualTo(StreamingState.Live));
            Assert.That(_reader.Ticks, Is.GreaterThan(0));
            _clock.Advance(3);
            Assert.That(_service.Elapsed, Is.EqualTo(TimeSpan.FromSeconds(3)));
        }

        [Test]
        public void ALostConnection_IsReconnecting_AndClearsWhenBack()
        {
            CreateWithKey();
            _service.Start();
            Advance(PublisherState.Live);

            Advance(PublisherState.Reconnecting, PublisherProblem.ConnectionLost);
            Assert.That(_service.State.Value, Is.EqualTo(StreamingState.Reconnecting));
            Assert.That(_service.Problem.Value, Is.EqualTo(StreamingProblem.ConnectionLost));

            Advance(PublisherState.Live);
            Assert.That(_service.State.Value, Is.EqualTo(StreamingState.Live));
            Assert.That(_service.Problem.Value, Is.EqualTo(StreamingProblem.None));
        }

        [Test]
        public void WithoutAudio_TheStreamGoesOnAndSaysSo_EvenAfterReconnecting()
        {
            _audio = false;
            CreateWithKey();
            _service.Start();
            Advance(PublisherState.Live);
            Assert.That(_service.Problem.Value, Is.EqualTo(StreamingProblem.AudioUnavailable));

            Advance(PublisherState.Reconnecting, PublisherProblem.ConnectionLost);
            Advance(PublisherState.Live);

            Assert.That(_service.Problem.Value, Is.EqualTo(StreamingProblem.AudioUnavailable));
        }

        [TestCase(PublisherProblem.Rejected, StreamingProblem.Rejected)]
        [TestCase(PublisherProblem.Unreachable, StreamingProblem.ServerUnreachable)]
        [TestCase(PublisherProblem.ConnectionLost, StreamingProblem.ConnectionLost)]
        [TestCase(PublisherProblem.EncoderFailed, StreamingProblem.EncoderFailed)]
        public void AFailedStream_ClosesThePlugin_AndKeepsTheProblem(PublisherProblem native, StreamingProblem problem)
        {
            CreateWithKey();
            _service.Start();

            Advance(PublisherState.Failed, native);
            TickUntil(StreamingState.Failed);

            Assert.That(_service.Problem.Value, Is.EqualTo(problem));
            Assert.That(_publisher.Closed, Is.True);
            Assert.That(_reader.Disposed, Is.True);
        }

        [Test]
        public void Stop_WaitsForTheLastFrames_ThenClosesOffTheMainThread()
        {
            CreateWithKey();
            _service.Start();
            Advance(PublisherState.Live);
            _reader.Waiting = 1;

            _service.Stop();
            _loop.Tick(e => throw e);

            Assert.That(_service.State.Value, Is.EqualTo(StreamingState.Stopping));
            Assert.That(_reader.Stopped, Is.True);
            Assert.That(_publisher.CloseStarted, Is.False);

            _reader.Waiting = 0;
            TickUntil(StreamingState.Idle);

            Assert.That(_publisher.Closed, Is.True);
            Assert.That(_service.Problem.Value, Is.EqualTo(StreamingProblem.None));
        }

        [Test]
        public void AFailedStream_CanStartAgain()
        {
            CreateWithKey();
            _service.Start();
            Advance(PublisherState.Failed, PublisherProblem.Rejected);
            TickUntil(StreamingState.Failed);
            _publisher = new FakePublisher();

            _service.Start();

            Assert.That(_service.State.Value, Is.EqualTo(StreamingState.Connecting));
            Assert.That(_service.Problem.Value, Is.EqualTo(StreamingProblem.None));
        }

        [Test]
        public void SetStreamKey_MakesAnIdOnce_AndKeepsTheKeyOutOfTheSettings()
        {
            Create();

            Assert.That(_service.SetStreamKey("  first\n"), Is.True);
            Assert.That(_service.SetStreamKey("second"), Is.True);

            Assert.That(_saved.Count, Is.EqualTo(1), "Only the ID is saved, once.");
            string id = _saved[0].CredentialId;
            Assert.That(CredentialId.IsValid(id), Is.True);
            Assert.That(_credentials.Stored[id], Is.EqualTo("second"));
            Assert.That(_service.Options.Value.KeyRegistered, Is.True);
            Assert.That(SavedText(_saved[0]), Does.Not.Contain("second").And.Not.Contain("first"));
        }

        [Test]
        public void SetStreamKey_Empty_RemovesIt()
        {
            CreateWithKey();

            Assert.That(_service.SetStreamKey(" "), Is.True);

            Assert.That(_credentials.Stored, Is.Empty);
            Assert.That(_service.Options.Value.KeyRegistered, Is.False);
        }

        [Test]
        public void SetStreamKey_TheStoreRefusing_LeavesItUnregistered()
        {
            Create();
            _credentials.Refuse = true;

            Assert.That(_service.SetStreamKey("key"), Is.False);
            Assert.That(_service.Options.Value.KeyRegistered, Is.False);
        }

        [Test]
        public void NothingChanges_WhileStreaming()
        {
            CreateWithKey();
            _service.Start();
            int saved = _saved.Count;

            _service.SetOptions(_service.Options.Value.WithVideoBitrate(3_000_000));

            Assert.That(_service.SetStreamKey("other"), Is.False);
            Assert.That(_service.Options.Value.VideoBitrate, Is.EqualTo(6_000_000));
            Assert.That(_saved.Count, Is.EqualTo(saved));
        }

        [Test]
        public void Stats_MeasureTheBitrateOverTheWindow()
        {
            CreateWithKey();
            _service.Start();
            for (int i = 1; i <= 8; i++)
            {
                _publisher.Status = Status(PublisherState.Live, PublisherProblem.None, bytesSent: i * 93_750);
                _clock.Advance(0.25);
                _loop.Tick(e => throw e);
            }

            Assert.That(_service.Stats.BitsPerSecond, Is.EqualTo(3_000_000).Within(1));
        }

        [Test]
        public void Shutdown_ClosesAStreamInProgress()
        {
            CreateWithKey();
            _service.Start();

            _service.ShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(_publisher.Closed, Is.True);
            Assert.That(_reader.Disposed, Is.True);
        }

        private static string SavedText(StreamSettings settings)
        {
            return $"{settings.Destination}{settings.ServerUrl}{settings.CredentialId}{settings.VideoCodec}";
        }

        private static PublisherStatus Status(PublisherState state, PublisherProblem problem, long bytesSent = 0)
        {
            return new PublisherStatus(state, problem, true, 0, bytesSent, 0, 0, 0, 0, 0, 0);
        }

        private void Create()
        {
            var environment = new StreamingEnvironment(
                (format, key) =>
                {
                    _format = format;
                    _keysOpened.Add(key);
                    return _publisher;
                },
                (format, publisher) => _reader,
                url => PublisherProblem.None,
                true,
                () => _audio ? new RecordingAudio((IntPtr)1, (IntPtr)2, TimeSpan.Zero) : default);
            _service = new StreamingService(
                new StreamSettings { VideoCodec = "h264" },
                _saved.Add,
                new VideoSize(1920, 1080),
                30,
                160_000,
                _loop,
                _clock,
                _credentials,
                environment,
                null,
                null);
            _service.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        private void CreateWithKey()
        {
            Create();
            _service.SetStreamKey(Key);
        }

        private void Advance(PublisherState state, PublisherProblem problem = PublisherProblem.None)
        {
            _publisher.Status = Status(state, problem);
            _clock.Advance(0.3);
            _loop.Tick(e => throw e);
        }

        private void TickUntil(StreamingState state)
        {
            var waited = Stopwatch.StartNew();
            while (_service.State.Value != state)
            {
                Assert.That(waited.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), $"Still {_service.State.Value}, waiting for {state}.");
                _loop.Tick(e => throw e);
                Thread.Sleep(1);
            }
        }

        private sealed class FakeCredentials : ICredentialStore
        {
            public Dictionary<string, string> Stored { get; } = new Dictionary<string, string>();

            public bool Refuse { get; set; }

            public bool Exists(string credentialId)
            {
                return credentialId != null && Stored.ContainsKey(credentialId);
            }

            public CredentialResult Write(string credentialId, string secret)
            {
                if (Refuse)
                {
                    return new CredentialResult(CredentialStatus.Failed, 5);
                }

                Stored[credentialId] = secret;
                return new CredentialResult(CredentialStatus.Ok);
            }

            public CredentialResult Read(string credentialId, out string secret)
            {
                secret = null;
                return Stored.TryGetValue(credentialId, out secret) ? new CredentialResult(CredentialStatus.Ok) : new CredentialResult(CredentialStatus.NotFound);
            }

            public CredentialResult Delete(string credentialId)
            {
                return new CredentialResult(credentialId != null && Stored.Remove(credentialId) ? CredentialStatus.Ok : CredentialStatus.NotFound);
            }
        }

        private sealed class FakePublisher : IStreamPublisher
        {
            public PublisherStatus Status { get; set; }

            public volatile bool CloseStarted;

            public volatile bool Closed;

            public bool Push(IntPtr data, int length, long timestamp)
            {
                return true;
            }

            public PublisherStatus GetStatus()
            {
                return Status;
            }

            public void Close()
            {
                CloseStarted = true;
                Closed = true;
            }
        }

        private sealed class FakeReader : IStreamFrameReader
        {
            public long Skipped => 0;

            public int LatencyFrames => 1;

            public int Waiting { get; set; }

            public int Ticks { get; private set; }

            public bool Stopped { get; private set; }

            public bool Disposed { get; private set; }

            public void Tick()
            {
                Ticks++;
            }

            public void Stop()
            {
                Stopped = true;
            }

            public void Release()
            {
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
