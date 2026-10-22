using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.Tracking.Recording;

namespace VirtualVessel.Tracking.Tests
{
    /// <summary>The recording file's bytes, names, and listing (tracking detailed design 12.3).</summary>
    public sealed class RecordingFormatTests
    {
        [Test]
        public void AnObservation_SurvivesItsBytes()
        {
            TrackingFrame frame = Sample();
            var bytes = new byte[TrackingRecordingFormat.FrameSize];
            var back = new TrackingFrame();

            TrackingRecordingFormat.EncodeFrame(frame, bytes);
            TrackingRecordingFormat.DecodeFrame(bytes, back);

            Assert.That(TrackingRecordingFormat.FrameSize, Is.EqualTo(1680), "About 1.7 KB, as the design says.");
            Assert.That(back.Sequence, Is.EqualTo(frame.Sequence));
            Assert.That(back.Time, Is.EqualTo(frame.Time));
            Assert.That(TrackingRecordingFormat.ReadTime(bytes), Is.EqualTo(frame.Time));
            Assert.That(back.Body.Positions, Is.EqualTo(frame.Body.Positions));
            Assert.That(back.Body.ImagePositions, Is.EqualTo(frame.Body.ImagePositions));
            Assert.That(back.Body.JointConfidence, Is.EqualTo(frame.Body.JointConfidence));
            Assert.That(back.Body.Status, Is.EqualTo(frame.Body.Status));
            Assert.That(back.Body.Timestamp, Is.EqualTo(frame.Body.Timestamp));
            Assert.That(back.LeftHand.Positions, Is.EqualTo(frame.LeftHand.Positions));
            Assert.That(back.RightHand.PalmRotation, Is.EqualTo(frame.RightHand.PalmRotation));
            Assert.That(back.RightHand.Status, Is.EqualTo(TrackingStatus.LowConfidence));
            Assert.That(back.Face.Shapes, Is.EqualTo(frame.Face.Shapes));
            Assert.That(back.Face.HeadRotation, Is.EqualTo(frame.Face.HeadRotation));
            Assert.That(back.Face.HeadPosition, Is.EqualTo(frame.Face.HeadPosition));
            Assert.That(back.Face.SupportedShapes, Is.EqualTo(frame.Face.SupportedShapes));
            Assert.That(back.Face.LeftEyeGaze, Is.EqualTo(frame.Face.LeftEyeGaze));
            Assert.That(back.Face.Confidence, Is.EqualTo(frame.Face.Confidence));
        }

        [Test]
        public void AHeader_SurvivesItsBytes_AndForeignOrBrokenOnesAreRefused()
        {
            var started = new DateTime(2026, 10, 22, 4, 5, 6, DateTimeKind.Utc);
            var header = new TrackingRecordingHeader(TrackingRecordingFormat.Version, TrackingRecordingFormat.FrameSize, started, TrackingRegions.Body | TrackingRegions.Face, "腕を前へ");
            byte[] bytes = TrackingRecordingFormat.EncodeHeader(header);

            Assert.That(TrackingRecordingFormat.TryDecodeFixedHeader(bytes, out int version, out int size, out DateTime at, out TrackingRegions supported, out int descriptionBytes), Is.True);
            Assert.That((version, size, at, supported), Is.EqualTo((1, TrackingRecordingFormat.FrameSize, started, TrackingRegions.Body | TrackingRegions.Face)));
            Assert.That(descriptionBytes, Is.EqualTo(bytes.Length - TrackingRecordingFormat.FixedHeaderSize));

            byte[] other = (byte[])bytes.Clone();
            other[0] = (byte)'X';
            Assert.That(TrackingRecordingFormat.TryDecodeFixedHeader(other, out _, out _, out _, out _, out _), Is.False, "Not a recording.");
            other = (byte[])bytes.Clone();
            other[4] = 2;
            Assert.That(TrackingRecordingFormat.TryDecodeFixedHeader(other, out _, out _, out _, out _, out _), Is.False, "A version this one cannot read.");
            other = (byte[])bytes.Clone();
            other[24] = 0xFF;
            other[25] = 0xFF;
            Assert.That(TrackingRecordingFormat.TryDecodeFixedHeader(other, out _, out _, out _, out _, out _), Is.False, "A broken length is not followed.");
        }

        [Test]
        public void Descriptions_AreTrimmed_Cleaned_AndCut()
        {
            Assert.That(TrackingRecordingFormat.NormalizeDescription("  arms\tforward \n"), Is.EqualTo("armsforward"));
            Assert.That(TrackingRecordingFormat.NormalizeDescription(null), Is.EqualTo(string.Empty));
            Assert.That(TrackingRecordingFormat.NormalizeDescription(new string('a', 150)).Length, Is.EqualTo(TrackingRecordingFormat.MaxDescriptionLength));
            string emoji = new string('a', 99) + "\U0001F600";
            Assert.That(char.IsHighSurrogate(TrackingRecordingFormat.NormalizeDescription(emoji).Last()), Is.False, "A character is never cut in half.");
        }

        [Test]
        public void OnlyTheApplicationsOwnNames_AreRecordings()
        {
            Assert.That(TrackingRecordingFile.IsRecordingName("20261022-130512.vvtrack"), Is.True);
            Assert.That(TrackingRecordingFile.IsRecordingName("20261022-130512-2.vvtrack"), Is.True);
            Assert.That(TrackingRecordingFile.IsRecordingName("..\\20261022-130512.vvtrack"), Is.False, "No path.");
            Assert.That(TrackingRecordingFile.IsRecordingName("notes.vvtrack"), Is.False);
            Assert.That(TrackingRecordingFile.IsRecordingName("20261022-130512.vvtrack.txt"), Is.False);
            Assert.That(TrackingRecordingFile.IsRecordingName(null), Is.False);

            var started = new DateTime(2026, 10, 22, 13, 5, 12, DateTimeKind.Utc);
            Assert.That(TrackingRecordingFile.NameFor(started, _ => false), Is.EqualTo("20261022-130512.vvtrack"));
            Assert.That(TrackingRecordingFile.NameFor(started, name => name == "20261022-130512.vvtrack"), Is.EqualTo("20261022-130512-2.vvtrack"));
            Assert.That(TrackingRecordingFormat.FrameCount(TrackingRecordingFormat.FrameSize * 3 + 100 + 10, 100), Is.EqualTo(3), "A cut-off last observation is not counted.");
        }

        internal static TrackingFrame Sample()
        {
            TrackingFrame frame = SyntheticMotion.RaiseAndWaveFrames(1, 30, 0.01f, seed: 3)[7];
            frame.Face.HeadPosition = new Vector3(0.1f, -0.2f, 0.6f);
            frame.Face.SupportedShapes = FacePose.AllShapesExcept(FaceShape.TongueOut);
            frame.Face.LeftEyeGaze = new Vector2(3f, -2f);
            frame.Face.Confidence = 0.9f;
            frame.Face[FaceShape.JawOpen] = 0.42f;
            for (int i = 0; i < HandPose.JointCount; i++)
            {
                frame.LeftHand.Positions[i] = new Vector3(i, -i, i * 0.5f);
                frame.LeftHand.ImagePositions[i] = new Vector2(i * 0.01f, 0.5f);
            }

            frame.RightHand.Status = TrackingStatus.LowConfidence;
            frame.RightHand.PalmRotation = Quaternion.Euler(10f, 20f, 30f);
            return frame;
        }
    }

    /// <summary>Recording, listing, replaying, and deleting through the service (tracking detailed design 12).</summary>
    public sealed class TrackingRecordingsTests
    {
        private string _directory;
        private TrackingServiceTests.SecondsClock _clock;
        private FrameLoop _loop;
        private TrackingServiceTests.FakeCamera _camera;
        private TrackingService _tracking;
        private TrackingRecordings _recordings;
        private DateTime _utc;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _clock = new TrackingServiceTests.SecondsClock();
            _loop = new FrameLoop();
            _camera = new TrackingServiceTests.FakeCamera();

            // NUnit reuses the fixture between tests, so every field starts again here.
            _utc = new DateTime(2026, 10, 22, 4, 5, 6, DateTimeKind.Utc);
        }

        [TearDown]
        public void TearDown()
        {
            _recordings?.ShutdownAsync(CancellationToken.None).Wait();
            _recordings?.Dispose();
            _tracking?.Dispose();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Test]
        public async Task Recording_TakesRawObservations_UntilTrackingStops()
        {
            Create();
            Assert.Throws<InvalidOperationException>(() => _recordings.StartRecording("too early"), "Tracking is not running.");

            _tracking.Start();
            Frames(1);
            _recordings.StartRecording("  arms forward ");
            Assert.That(_recordings.Activity.Value, Is.EqualTo(RecordingActivity.Recording));
            string id = _recordings.ActiveId;
            Assert.That(id, Is.EqualTo("20261022-040506.vvtrack"));
            Frames(30);
            Assert.That(_recordings.RecordedSeconds, Is.GreaterThan(0.4));

            _tracking.Stop();
            Assert.That(_recordings.Activity.Value, Is.EqualTo(RecordingActivity.Idle), "Stopping tracking ends the recording.");
            await _recordings.ShutdownAsync(CancellationToken.None);
            await _recordings.RefreshAsync(CancellationToken.None);

            TrackingRecordingInfo info = _recordings.Recordings.Value.Single();
            Assert.That(info.Id, Is.EqualTo(id));
            Assert.That(info.Description, Is.EqualTo("arms forward"));
            Assert.That(info.Readable, Is.True);
            Assert.That(info.Frames, Is.InRange(14, 16), "Observations come 30 a second; frames 60.");
            Assert.That(info.Seconds, Is.GreaterThan(0.4).And.LessThan(0.6));

            (TrackingRecordingHeader header, var frames) = TrackingRecordingFile.ReadAll(Path.Combine(_directory, id));
            Assert.That(header.Supported, Is.EqualTo(TrackingRegions.Body | TrackingRegions.Face));
            Assert.That(frames[0].Body.Status, Is.EqualTo(TrackingStatus.Tracked), "Raw observations, as the provider gave them.");
        }

        [Test]
        public async Task ACutOffOrForeignFile_IsStillListed()
        {
            Create();
            Directory.CreateDirectory(_directory);
            string path = Path.Combine(_directory, "20261022-000000.vvtrack");
            var header = new TrackingRecordingHeader(TrackingRecordingFormat.Version, TrackingRecordingFormat.FrameSize, DateTime.UtcNow, TrackingRegions.Body, "cut");
            TrackingRecorder recorder = TrackingRecorder.Create(path, header);
            TrackingFrame frame = RecordingFormatTests.Sample();
            for (int i = 0; i < 3; i++)
            {
                frame.Time = i;
                recorder.Append(frame);
            }

            await recorder.FinishAsync();
            using (var stream = new FileStream(path, FileMode.Append))
            {
                stream.Write(new byte[100], 0, 100);
            }

            File.WriteAllText(Path.Combine(_directory, "20261022-000001.vvtrack"), "not a recording");
            File.WriteAllText(Path.Combine(_directory, "notes.vvtrack"), "someone else's file");

            await _recordings.RefreshAsync(CancellationToken.None);
            TrackingRecordingInfo[] list = _recordings.Recordings.Value.ToArray();

            Assert.That(list.Select(info => info.Id), Is.EqualTo(new[] { "20261022-000001.vvtrack", "20261022-000000.vvtrack" }), "Newest first; other names are left alone.");
            Assert.That(list[0].Readable, Is.False, "Listed, not dropped.");
            Assert.That(list[1].Frames, Is.EqualTo(3), "The cut-off end is left out.");
            Assert.That(list[1].Seconds, Is.EqualTo(2.0));
            Assert.That(TrackingRecordingFile.ReadAll(path).Frames.Count, Is.EqualTo(3));
        }

        [Test]
        public async Task Replay_PlaysARecording_WithoutTheCamera_AndIsNotSaved()
        {
            Create();
            string id = await RecordAsync(seconds: 1);
            Assert.That(_tracking.State.Value, Is.EqualTo(TrackingState.Stopped));

            await _recordings.StartReplayAsync(id, CancellationToken.None);
            Frames(20);

            Assert.That(_recordings.Activity.Value, Is.EqualTo(RecordingActivity.Replaying));
            Assert.That(_recordings.ActiveId, Is.EqualTo(id));
            Assert.That(_tracking.State.Value, Is.EqualTo(TrackingState.Running));
            Assert.That(_camera.Started, Is.False, "The camera is not needed for replay.");
            Assert.That(_tracking.Current.Body.Status, Is.EqualTo(TrackingStatus.Tracked));
            Assert.Throws<InvalidOperationException>(() => _recordings.StartRecording(null), "Replays are not recorded.");
            Assert.ThrowsAsync<InvalidOperationException>(() => _recordings.DeleteAsync(id, CancellationToken.None), "The recording being replayed stays.");

            _recordings.StopReplay();
            Assert.That(_recordings.Activity.Value, Is.EqualTo(RecordingActivity.Idle));
            Assert.That(_tracking.State.Value, Is.EqualTo(TrackingState.Stopped), "Back to the state before the replay.");
            await _recordings.StartReplayAsync(id, CancellationToken.None);
            await _tracking.ShutdownAsync(CancellationToken.None);
            ConfigLoadResult<TrackingSettings> saved = TrackingService.CreateStore(Path.Combine(_directory, "config"), new UtcSystemClock()).Load();
            Assert.That(saved.Value.Running, Is.False, "Starting tracking for a replay is not saved, even when the application ends during it.");
        }

        [Test]
        public async Task StoppingTracking_EndsTheReplay_AndARunningTrackingComesBack()
        {
            Create();
            string id = await RecordAsync(seconds: 1);
            _tracking.Start();
            Frames(1);

            await _recordings.StartReplayAsync(id, CancellationToken.None);
            Assert.That(_camera.Started, Is.False);
            _recordings.StopReplay();
            Assert.That(_camera.Started, Is.True, "Tracking was on before the replay, so the camera comes back.");

            await _recordings.StartReplayAsync(id, CancellationToken.None);
            _tracking.Stop();
            Assert.That(_recordings.Activity.Value, Is.EqualTo(RecordingActivity.Idle), "Turning tracking off ends the replay.");
            Assert.That(_tracking.State.Value, Is.EqualTo(TrackingState.Stopped));
        }

        [Test]
        public async Task ARecording_StopsByItself_AfterFiveMinutes()
        {
            // One observation a second, so five minutes pass in a few hundred frames.
            Create(rate: 1, seconds: 400);
            _tracking.Start();
            Frames(1);
            _recordings.StartRecording(null);
            for (int i = 0; i < 320 && _recordings.Activity.Value == RecordingActivity.Recording; i++)
            {
                _clock.Seconds += 1.0;
                _loop.Tick(null);
            }

            Assert.That(_recordings.Activity.Value, Is.EqualTo(RecordingActivity.Idle));
            await _recordings.ShutdownAsync(CancellationToken.None);
            await _recordings.RefreshAsync(CancellationToken.None);
            Assert.That(_recordings.Recordings.Value.Single().Seconds, Is.EqualTo(TrackingRecordings.MaxSeconds).Within(1.0));
        }

        [Test]
        public async Task Deleting_TakesOnlyRecordings_OneOrAll()
        {
            Create();
            string first = await RecordAsync(seconds: 0.2);
            _clock.Seconds += 1;
            string second = await RecordAsync(seconds: 0.2, utc: new DateTime(2026, 10, 22, 5, 0, 0, DateTimeKind.Utc));
            File.WriteAllText(Path.Combine(_directory, "keep.txt"), "not ours");

            Assert.ThrowsAsync<ArgumentException>(() => _recordings.DeleteAsync("..\\keep.txt", CancellationToken.None), "No path outside the folder.");
            await _recordings.DeleteAsync(first, CancellationToken.None);
            Assert.That(_recordings.Recordings.Value.Select(info => info.Id), Is.EqualTo(new[] { second }));

            await _recordings.DeleteAllAsync(CancellationToken.None);
            Assert.That(_recordings.Recordings.Value, Is.Empty);
            Assert.That(File.Exists(Path.Combine(_directory, "keep.txt")), Is.True, "Other files are left alone.");
        }

        private void Create(double rate = 30, double seconds = 2)
        {
            TrackingFrame[] frames = SyntheticMotion.RaiseAndWaveFrames(seconds, rate, 0f, seed: 0);
            var provider = new TrackingReplayProvider(frames, TrackingRegions.Body | TrackingRegions.Face, () => _clock.Seconds, loop: true);
            string config = Path.Combine(_directory, "config");
            var saves = new ConfigSaveScheduler<TrackingSettings>(TrackingService.CreateStore(config, new UtcSystemClock()), _clock, TimeSpan.FromSeconds(0.5), null);
            _tracking = new TrackingService(provider, _camera, _loop, _clock, new TrackingSettings(), saves, null, null);
            _tracking.InitializeAsync(CancellationToken.None).Wait();
            _recordings = new TrackingRecordings(_tracking, _directory, () => _utc, () => _clock.Seconds, null);
        }

        /// <summary>Records <paramref name="seconds"/> of the provider's motion and leaves tracking as it was.</summary>
        private async Task<string> RecordAsync(double seconds, DateTime? utc = null)
        {
            bool wasRunning = _tracking.State.Value == TrackingState.Running;
            _utc = utc ?? _utc;
            _tracking.Start();
            Frames(1);
            _recordings.StartRecording(null);
            string id = _recordings.ActiveId;
            Frames((int)(seconds * 60));
            _recordings.StopRecording();
            if (!wasRunning)
            {
                _tracking.Stop();
            }

            await _recordings.ShutdownAsync(CancellationToken.None);
            await _recordings.RefreshAsync(CancellationToken.None);
            return id;
        }

        private void Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                _clock.Seconds += 1.0 / 60;
                _loop.Tick(null);
            }
        }
    }
}
