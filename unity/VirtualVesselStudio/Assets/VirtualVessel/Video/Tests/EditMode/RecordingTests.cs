using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NUnit.Framework;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Time;
using VirtualVessel.UI.Recording;
using VirtualVessel.Video.Recording;

namespace VirtualVessel.Video.Tests
{
    public sealed class RecordingNamesTests
    {
        [Test]
        public void FileName_IsTheLocalTime()
        {
            Assert.That(RecordingNames.FileName(new DateTime(2026, 10, 27, 22, 30, 5)), Is.EqualTo("VirtualVesselStudio_2026-10-27_22-30-05.mp4"));
        }

        [Test]
        public void NewFile_NumbersANameThatIsTaken()
        {
            var time = new DateTime(2026, 10, 27, 22, 30, 5);
            string first = Path.Combine("F", "VirtualVesselStudio_2026-10-27_22-30-05.mp4");
            string second = Path.Combine("F", "VirtualVesselStudio_2026-10-27_22-30-05_2.mp4");

            Assert.That(RecordingNames.NewFile("F", time, _ => false), Is.EqualTo(first));
            Assert.That(RecordingNames.NewFile("F", time, path => path == first), Is.EqualTo(second));
            Assert.That(RecordingNames.NewFile("F", time, path => path == first || path == second), Is.EqualTo(Path.Combine("F", "VirtualVesselStudio_2026-10-27_22-30-05_3.mp4")));
        }

        [Test]
        public void Folder_IsTheVideosFolderUnlessSet()
        {
            Assert.That(RecordingNames.Folder(null, "V"), Is.EqualTo(Path.Combine("V", "Virtual Vessel Studio")));
            Assert.That(RecordingNames.Folder(" ", "V"), Is.EqualTo(Path.Combine("V", "Virtual Vessel Studio")));
            Assert.That(RecordingNames.Folder("D:\\Rec", "V"), Is.EqualTo("D:\\Rec"));
        }

        [Test]
        public void FreeSpace_NeedsTwoGigabytesToStart_AndStopsBelowHalfAGigabyte()
        {
            const long Megabyte = 1024 * 1024;

            Assert.That(RecordingNames.HasRoom(2048 * Megabyte), Is.True);
            Assert.That(RecordingNames.HasRoom((2048 * Megabyte) - 1), Is.False);
            Assert.That(RecordingNames.HasRoom(null), Is.True, "Unknown space does not stop a recording; writing will tell.");
            Assert.That(RecordingNames.MustStop(499 * Megabyte), Is.True);
            Assert.That(RecordingNames.MustStop(500 * Megabyte), Is.False);
            Assert.That(RecordingNames.MustStop(null), Is.False);
        }

        [Test]
        public void Quality_ReadsAnyBitrateAsTheNearerOfTheTwo()
        {
            Assert.That(RecordingNames.QualityOf(6_000_000), Is.EqualTo(RecordingQuality.Standard));
            Assert.That(RecordingNames.QualityOf(8_000_000), Is.EqualTo(RecordingQuality.Standard));
            Assert.That(RecordingNames.QualityOf(9_000_000), Is.EqualTo(RecordingQuality.High));
            Assert.That(RecordingNames.BitrateOf(RecordingQuality.High), Is.EqualTo(12_000_000));
            Assert.That(RecordingNames.BitrateOf(RecordingQuality.Standard), Is.EqualTo(6_000_000));
        }

        [Test]
        public void Settings_OutOfRange_FallBackToTheirDefaults()
        {
            var edited = new RecordingSettings { Width = 1921, Height = 1080, FrameRate = 0, VideoBitrate = 100, AudioBitrate = 128_000, RecordingFolder = "D:\\Rec" };

            RecordingSettings usable = edited.Usable();

            Assert.That((usable.Width, usable.Height, usable.FrameRate, usable.VideoBitrate), Is.EqualTo((1920, 1080, 30, 6_000_000)));
            Assert.That((usable.AudioBitrate, usable.RecordingFolder), Is.EqualTo((128_000, "D:\\Rec")));
            Assert.That(edited.Width, Is.EqualTo(1921), "The original is not changed.");
        }

        [Test]
        public void With_KeepsTheDefaultFolderAsNull_AndAnEditedBitrateWhenTheQualityStays()
        {
            var settings = new RecordingSettings { VideoBitrate = 8_000_000 };
            string videos = "V";
            RecordingOptions options = RecordingService.OptionsOf(settings, new VideoSize(1920, 1080), videos);

            RecordingSettings same = RecordingService.With(settings, options, videos);
            RecordingSettings high = RecordingService.With(settings, options.WithQuality(RecordingQuality.High).WithFolder("D:\\Rec"), videos);

            Assert.That((same.RecordingFolder, same.VideoBitrate), Is.EqualTo(((string)null, 8_000_000)));
            Assert.That((high.RecordingFolder, high.VideoBitrate), Is.EqualTo(("D:\\Rec", 12_000_000)));
        }
    }

    public sealed class FramePacingTests
    {
        [Test]
        public void At60FramesASecond_EverySecondFrameIsDue()
        {
            const long Interval = 1000;
            long next = 0;
            int due = 0;
            for (int frame = 0; frame < 60; frame++)
            {
                // A little jitter, as real frame times have.
                long now = (frame * Interval / 2) + (frame % 3 == 0 ? 40 : -40);
                (bool isDue, long following) = FramePacing.Pace(now, next, Interval);
                next = following;
                due += isDue ? 1 : 0;
                Assert.That(isDue, Is.EqualTo(frame % 2 == 0), $"Frame {frame}");
            }

            Assert.That(due, Is.EqualTo(30));
        }

        [Test]
        public void AfterAStall_TheScheduleStartsFromNow()
        {
            (bool due, long next) = FramePacing.Pace(10_000, 2000, 1000);

            Assert.That(due, Is.True);
            Assert.That(next, Is.EqualTo(11_000));
        }
    }

    public sealed class RecordingServiceTests
    {
        private const long Gigabyte = 1024L * 1024 * 1024;

        private string _folder;
        private ManualClock _clock;
        private FrameLoop _loop;
        private FakeEncoder _encoder;
        private FakeReader _reader;
        private long? _free;
        private Exception _openFailure;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "vv-recording-tests-" + Guid.NewGuid().ToString("N"));
            _clock = new ManualClock();
            _loop = new FrameLoop();
            _encoder = null;
            _reader = null;
            _free = 100 * Gigabyte;
            _openFailure = null;
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        [Test]
        public void ARecording_StartsOpensRecordsAndFinishesItsFile()
        {
            using RecordingService service = Create();
            service.Start();
            Assert.That(service.State.Value, Is.EqualTo(RecordingState.Starting));

            TickUntil(service, RecordingState.Recording);
            _loop.Tick(e => throw e);
            Assert.That(_reader.Ticks, Is.GreaterThan(0), "Frames are read while recording.");
            _clock.Advance(3);
            Assert.That(service.Elapsed, Is.EqualTo(TimeSpan.FromSeconds(3)));

            service.Stop();
            Assert.That(service.State.Value, Is.EqualTo(RecordingState.Stopping));
            Assert.That(_reader.Stopped, Is.True);
            TickUntil(service, RecordingState.Idle);

            Assert.That(_encoder.Closed, Is.True);
            Assert.That(service.LastFile, Is.EqualTo(_encoder.Path));
            Assert.That(Path.GetDirectoryName(service.LastFile), Is.EqualTo(_folder));
            Assert.That(service.Problem.Value, Is.EqualTo(RecordingProblem.None));
            Assert.That(service.Elapsed, Is.EqualTo(TimeSpan.Zero));
        }

        [Test]
        public void Stopping_WaitsForReadbacksOnTheirWay_UpToASecond()
        {
            using RecordingService service = Create();
            service.Start();
            TickUntil(service, RecordingState.Recording);
            _reader.Waiting = 1;

            service.Stop();
            _loop.Tick(e => throw e);
            Assert.That(_encoder.CloseStarted, Is.False, "The last frames are still coming.");
            Assert.That((_reader.Stopped, _reader.Released), Is.EqualTo((true, false)), "No new frames, but those on their way are pushed.");

            _clock.Advance(1);
            TickUntil(service, RecordingState.Idle);
            Assert.That(_encoder.Closed, Is.True);
            Assert.That(_reader.Released, Is.True, "Let go before the encoder closes.");
        }

        [Test]
        public void WithoutTwoGigabytesFree_ItDoesNotStart()
        {
            _free = Gigabyte;
            using RecordingService service = Create();

            service.Start();

            Assert.That(service.State.Value, Is.EqualTo(RecordingState.Failed));
            Assert.That(service.Problem.Value, Is.EqualTo(RecordingProblem.DiskFull));
            Assert.That(_encoder, Is.Null);
        }

        [Test]
        public void AFolderThatCannotBeMade_IsReported()
        {
            using RecordingService service = Create();

            // Concatenated: Path.Combine itself rejects the character.
            service.SetOptions(service.Options.Value.WithFolder(_folder + Path.DirectorySeparatorChar + "bad|name"));

            service.Start();

            Assert.That(service.Problem.Value, Is.EqualTo(RecordingProblem.FolderUnavailable));
        }

        [Test]
        public void AnEncoderThatDoesNotOpen_FailsTheStart_AndTheNextStartTriesAgain()
        {
            _openFailure = new EncoderException("No H.264 encoder.");
            using RecordingService service = Create();

            service.Start();
            TickUntil(service, RecordingState.Failed);
            Assert.That(service.Problem.Value, Is.EqualTo(RecordingProblem.EncoderFailed));

            _openFailure = null;
            service.Start();
            Assert.That(service.Problem.Value, Is.EqualTo(RecordingProblem.None));
            TickUntil(service, RecordingState.Recording);
        }

        [Test]
        public void AWriteError_StopsTheRecording_AndKeepsWhatWasWritten()
        {
            using RecordingService service = Create();
            service.Start();
            TickUntil(service, RecordingState.Recording);

            _encoder.Status = new EncoderStatus(10, 9, 0, unchecked((int)0x80070070), true);
            _clock.Advance(1);
            TickUntil(service, RecordingState.Failed);

            Assert.That(service.Problem.Value, Is.EqualTo(RecordingProblem.EncoderFailed));
            Assert.That(_encoder.Closed, Is.True);
            Assert.That(service.LastFile, Is.EqualTo(_encoder.Path));
        }

        [Test]
        public void TheDiskFillingUp_StopsTheRecording()
        {
            using RecordingService service = Create();
            service.Start();
            TickUntil(service, RecordingState.Recording);

            _free = 400L * 1024 * 1024;
            _clock.Advance(10);
            TickUntil(service, RecordingState.Failed);

            Assert.That(service.Problem.Value, Is.EqualTo(RecordingProblem.DiskFull));
            Assert.That(_encoder.Closed, Is.True);
        }

        [Test]
        public void StopWhileStarting_StopsOnceOpen()
        {
            using RecordingService service = Create();
            service.Start();

            service.Stop();
            TickUntil(service, RecordingState.Idle);

            Assert.That(_encoder.Closed, Is.True);
        }

        [Test]
        public void Shutdown_FinishesARecordingInProgress()
        {
            RecordingService service = Create();
            service.Start();
            TickUntil(service, RecordingState.Recording);

            service.ShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();
            service.Dispose();

            Assert.That(_encoder.Closed, Is.True);
            Assert.That(_reader.Released, Is.True);
        }

        [Test]
        public void StartWhileRecording_IsIgnored()
        {
            using RecordingService service = Create();
            service.Start();
            TickUntil(service, RecordingState.Recording);
            FakeEncoder first = _encoder;

            service.Start();
            _loop.Tick(e => throw e);

            Assert.That(_encoder, Is.SameAs(first));
            Assert.That(service.State.Value, Is.EqualTo(RecordingState.Recording));
        }

        [Test]
        public void Options_ChangeTheNextRecording()
        {
            using RecordingService service = Create();
            string other = Path.Combine(_folder, "other");

            service.SetOptions(service.Options.Value.WithFolder(other).WithQuality(RecordingQuality.High));
            service.Start();
            TickUntil(service, RecordingState.Recording);

            Assert.That(service.Options.Value.Quality, Is.EqualTo(RecordingQuality.High));
            Assert.That(Path.GetDirectoryName(_encoder.Path), Is.EqualTo(other));
            Assert.That(_encoder.Format.VideoBitrate, Is.EqualTo(12_000_000));
        }

        private RecordingService Create()
        {
            var environment = new RecordingEnvironment(
                (path, format) =>
                {
                    if (_openFailure != null)
                    {
                        throw _openFailure;
                    }

                    _encoder = new FakeEncoder(path, format);
                    return _encoder;
                },
                (format, encoder) => _reader = new FakeReader(),
                _ => _free,
                () => new DateTime(2026, 10, 27, 22, 30, 5),
                _folder);
            var settings = new RecordingSettings { RecordingFolder = _folder };
            var service = new RecordingService(settings, null, new VideoSize(1920, 1080), _loop, _clock, environment, null, null);
            service.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            return service;
        }

        /// <summary>Ticks frames until the service reaches <paramref name="state"/>; opening and closing run on worker threads.</summary>
        private void TickUntil(RecordingService service, RecordingState state)
        {
            var waited = Stopwatch.StartNew();
            while (service.State.Value != state)
            {
                Assert.That(waited.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), $"Still {service.State.Value}, waiting for {state}.");
                _loop.Tick(e => throw e);
                Thread.Sleep(1);
            }
        }

        private sealed class FakeEncoder : IVideoEncoder
        {
            public FakeEncoder(string path, EncoderFormat format)
            {
                Path = path;
                Format = format;
                File.WriteAllBytes(path, new byte[] { 0 });
            }

            public string Path { get; }

            public EncoderFormat Format { get; }

            public EncoderStatus Status { get; set; }

            public volatile bool CloseStarted;

            public volatile bool Closed;

            public bool Push(IntPtr bgra, int length, long timestamp)
            {
                return true;
            }

            public EncoderStatus GetStatus()
            {
                return Status;
            }

            public EncoderStatus Close()
            {
                CloseStarted = true;
                Closed = true;
                return Status;
            }
        }

        private sealed class FakeReader : IStreamFrameReader
        {
            public long Skipped => 0;

            public int LatencyFrames => 1;

            public int Waiting { get; set; }

            public int Ticks { get; private set; }

            public bool Stopped { get; private set; }

            public bool Released { get; private set; }

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
                Released = true;
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
