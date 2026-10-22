using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.State;
using VirtualVessel.Diagnostics.Logging;

namespace VirtualVessel.Tracking.Recording
{
    /// <summary>
    /// Developers' recordings of tracking observations (tracking detailed design 5.5, 12): records raw
    /// observations to files in the recordings folder, replays one in place of the camera, lists them,
    /// and deletes them. An Optional application service after Tracking, so it stops first and closes a
    /// recording in progress before tracking stops.
    /// </summary>
    /// <remarks>
    /// Main thread only. Files are read and deleted on other threads; awaits come back to the caller's
    /// context, so the observables change on the main thread.
    /// </remarks>
    public sealed class TrackingRecordings : IApplicationService, ITrackingRecordings
    {
        public const string ServiceName = "TrackingRecordings";

        /// <summary>The folder's name inside the Data Root's Cache (tracking detailed design 12.3).</summary>
        public const string FolderName = "TrackingRecordings";

        /// <summary>A recording stops by itself after this long (about 15 MB).</summary>
        public const double MaxSeconds = 300;

        private readonly TrackingService _tracking;
        private readonly string _directory;
        private readonly Func<DateTime> _utcNow;
        private readonly Func<double> _now;
        private readonly ILog _log;
        private readonly ObservableValue<RecordingActivity> _activity = new ObservableValue<RecordingActivity>(RecordingActivity.Idle);
        private readonly ObservableValue<IReadOnlyList<TrackingRecordingInfo>> _recordings = new ObservableValue<IReadOnlyList<TrackingRecordingInfo>>(Array.Empty<TrackingRecordingInfo>());
        private TrackingRecorder _recorder;
        private Task _closing = Task.CompletedTask;

        /// <param name="directory">The recordings folder, created when the first recording starts.</param>
        /// <param name="utcNow">The wall clock, for naming files.</param>
        /// <param name="now">Seconds on the clock tracking uses, for replay.</param>
        public TrackingRecordings(TrackingService tracking, string directory, Func<DateTime> utcNow, Func<double> now, ILog log)
        {
            _tracking = tracking ?? throw new ArgumentNullException(nameof(tracking));
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            _now = now ?? throw new ArgumentNullException(nameof(now));
            _log = log;
            _tracking.Stopping += StopRecording;
            _tracking.ReplayEnded += OnReplayEnded;
        }

        public string Name => ServiceName;

        public IReadOnlyObservable<RecordingActivity> Activity => _activity;

        public IReadOnlyObservable<IReadOnlyList<TrackingRecordingInfo>> Recordings => _recordings;

        public string ActiveId { get; private set; }

        public double RecordedSeconds => _recorder?.Seconds ?? 0;

        /// <summary>The recordings in <paramref name="directory"/>, newest first; files of other names are left alone.</summary>
        public static IReadOnlyList<TrackingRecordingInfo> List(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return Array.Empty<TrackingRecordingInfo>();
            }

            return Directory.EnumerateFiles(directory, "*" + TrackingRecordingFormat.Extension)
                .Where(path => TrackingRecordingFile.IsRecordingName(Path.GetFileName(path)))
                .Select(TrackingRecordingFile.ReadInfo)
                .OrderByDescending(info => info.Id, StringComparer.Ordinal)
                .ToArray();
        }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken)
        {
            StopAll();

            // Closing never needs the main thread, so a caller may block it while waiting here.
            await _closing.ConfigureAwait(false);
        }

        public void Dispose()
        {
            StopAll();
            _tracking.Stopping -= StopRecording;
            _tracking.ReplayEnded -= OnReplayEnded;
        }

        public async Task RefreshAsync(CancellationToken cancellationToken)
        {
            string directory = _directory;
            IReadOnlyList<TrackingRecordingInfo> list = await Task.Run(() => List(directory), cancellationToken);
            _recordings.Value = list;
        }

        public void StartRecording(string description)
        {
            if (_recorder != null)
            {
                return;
            }

            if (_tracking.IsReplaying || _tracking.State.Value != TrackingState.Running)
            {
                throw new InvalidOperationException("Recording needs tracking running from the camera, not a replay.");
            }

            Directory.CreateDirectory(_directory);
            DateTime startedAt = _utcNow();
            string name = TrackingRecordingFile.NameFor(startedAt, candidate => File.Exists(Path.Combine(_directory, candidate)));
            var header = new TrackingRecordingHeader(TrackingRecordingFormat.Version, TrackingRecordingFormat.FrameSize, startedAt, _tracking.Supported, TrackingRecordingFormat.NormalizeDescription(description));
            _recorder = TrackingRecorder.Create(Path.Combine(_directory, name), header);
            ActiveId = name;
            _tracking.Observed = OnObserved;
            _activity.Value = RecordingActivity.Recording;
            _log?.Information($"Recording tracking to {name}.");
        }

        public void StopRecording()
        {
            if (_recorder == null)
            {
                return;
            }

            TrackingRecorder recorder = _recorder;
            _recorder = null;
            _tracking.Observed = null;
            ActiveId = null;
            _activity.Value = RecordingActivity.Idle;
            _closing = CloseAsync(recorder);
            _ = RefreshWhenClosedAsync(_closing);
        }

        public async Task StartReplayAsync(string id, CancellationToken cancellationToken)
        {
            string path = PathOf(id);
            if (_recorder != null)
            {
                throw new InvalidOperationException("A recording is in progress.");
            }

            (TrackingRecordingHeader header, List<TrackingFrame> frames) = await Task.Run(() => TrackingRecordingFile.ReadAll(path), cancellationToken);
            if (frames.Count == 0)
            {
                throw new InvalidDataException($"{id} has no observations.");
            }

            _tracking.BeginReplay(new TrackingReplayProvider(frames, header.Supported, _now, loop: true));
            ActiveId = id;
            _activity.Value = RecordingActivity.Replaying;
            _log?.Information($"Replaying {id}: {frames.Count} observations.");
        }

        public void StopReplay()
        {
            _tracking.EndReplay();
        }

        public async Task DeleteAsync(string id, CancellationToken cancellationToken)
        {
            string path = PathOf(id);
            if (id == ActiveId)
            {
                throw new InvalidOperationException("The recording being recorded or replayed cannot be deleted.");
            }

            await Task.Run(() => File.Delete(path), cancellationToken);
            _log?.Information($"Deleted the tracking recording {id}.");
            await RefreshAsync(cancellationToken);
        }

        public async Task DeleteAllAsync(CancellationToken cancellationToken)
        {
            string directory = _directory;
            string active = ActiveId;
            int deleted = await Task.Run(
                () =>
                {
                    int count = 0;
                    foreach (TrackingRecordingInfo info in List(directory))
                    {
                        if (info.Id != active)
                        {
                            File.Delete(Path.Combine(directory, info.Id));
                            count++;
                        }
                    }

                    return count;
                },
                cancellationToken);
            _log?.Information($"Deleted {deleted} tracking recordings.");
            await RefreshAsync(cancellationToken);
        }

        public void StopAll()
        {
            StopRecording();
            StopReplay();
        }

        private void OnObserved(TrackingFrame frame)
        {
            TrackingRecorder recorder = _recorder;
            if (recorder == null)
            {
                return;
            }

            recorder.Append(frame);
            if (recorder.Failed)
            {
                StopRecording();
            }
            else if (recorder.Seconds >= MaxSeconds)
            {
                _log?.Information($"The tracking recording {recorder.Name} reached {MaxSeconds:F0} s and stopped.");
                StopRecording();
            }
        }

        private void OnReplayEnded()
        {
            if (_activity.Value == RecordingActivity.Replaying)
            {
                ActiveId = null;
                _activity.Value = RecordingActivity.Idle;
            }
        }

        /// <summary>Writes what is left and closes the file, on another thread; reports a failure instead of throwing.</summary>
        private async Task CloseAsync(TrackingRecorder recorder)
        {
            try
            {
                await recorder.FinishAsync().ConfigureAwait(false);
                _log?.Information($"Recorded {recorder.Name}: {recorder.Frames} observations, {recorder.Seconds:F1} s.");
            }
            catch (Exception exception)
            {
                _log?.Warning($"Writing the tracking recording {recorder.Name} failed; what was written up to then can still be replayed.", exception);
            }
        }

        /// <summary>Lists the recordings again once the file is closed, back on the caller's (main) thread.</summary>
        private async Task RefreshWhenClosedAsync(Task closing)
        {
            await closing;
            try
            {
                await RefreshAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                _log?.Warning("Listing the tracking recordings failed.", exception);
            }
        }

        /// <summary>The file of <paramref name="id"/> in the recordings folder; any other name is refused, so no path outside it is made.</summary>
        private string PathOf(string id)
        {
            if (!TrackingRecordingFile.IsRecordingName(id))
            {
                throw new ArgumentException("Not a tracking recording's name.", nameof(id));
            }

            return Path.Combine(_directory, id);
        }
    }
}
