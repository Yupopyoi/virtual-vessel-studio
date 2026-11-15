using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Audio;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.UI.Voice;
using VirtualVessel.Voice.Models;
using VirtualVessel.Voice.Rvc;

namespace VirtualVessel.Voice
{
    /// <summary>
    /// Converts a recording with a voice into a file to play to the user only (Voice detailed design 8.5). It runs its
    /// own stream a block at a time from the frame loop, as the conversion for the stream does, so the result sounds as
    /// the stream would; the microphone and the voice converting for the stream are untouched. One trial at a time.
    /// </summary>
    public sealed class VoiceTrialService : IVoiceTrial
    {
        /// <summary>Converted recordings, in the temporary folder; the last ones are deleted when a new trial starts.</summary>
        public const string FolderName = "VirtualVesselStudio-voice-trials";

        private readonly IFrameLoop _frameLoop;
        private readonly VoiceModelLibrary _library;
        // How often a trial looks again whether a cancelled one's voice has finished loading.
        private const int LoadWaitMilliseconds = 50;

        private readonly VoiceLoader _loader;
        private readonly string _folder;
        private readonly ILog _log;
        private Job _running;

        /// <param name="folder">Where converted recordings go; the temporary folder's <see cref="FolderName"/> when null.</param>
        public VoiceTrialService(IFrameLoop frameLoop, VoiceModelLibrary library, ILog log, string folder = null)
        {
            _frameLoop = frameLoop ?? throw new ArgumentNullException(nameof(frameLoop));
            _library = library;
            _loader = new VoiceLoader(library);
            _log = log;
            _folder = folder ?? Path.Combine(Path.GetTempPath(), FolderName);
        }

        public async Task<VoiceTrialResult> ConvertAsync(VoiceTrialRequest request, IProgress<float> progress, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (_running != null)
            {
                throw new InvalidOperationException("A trial is converting already.");
            }

            VoiceModelFiles files = _library == null ? VoiceModelFiles.Unusable("There is no voice library.") : _library.LocateFolder(request.VoiceFolder);
            if (files.Problem != null)
            {
                throw new VoiceTrialFailedException($"The voice in '{request.VoiceFolder}' cannot be used: {files.Problem}");
            }

            float[] recording;
            try
            {
                recording = await Task.Run(() => VoiceService.ReadFile(request.Recording), cancellationToken).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidDataException || exception is AudioEngineException)
            {
                throw new VoiceTrialFailedException($"The recording cannot be read: {exception.Message}", exception);
            }

            // A cancelled trial's voice may still be loading; its job lets it go once loaded. Starting meanwhile threw
            // and the trial could not be started again for a while (2026-11-15), so this one waits for it.
            while (_loader.IsLoading)
            {
                await Task.Delay(LoadWaitMilliseconds, cancellationToken).ConfigureAwait(true);
            }

            string problem = _loader.Begin(files, "trial");
            if (problem != null)
            {
                throw new VoiceTrialFailedException($"The voice cannot load: {problem}");
            }

            var job = new Job(this, recording, files.MedianPitchHz, request, progress, cancellationToken);
            _running = job;
            try
            {
                float[] converted = await job.Run().ConfigureAwait(true);
                string path = await Task.Run(() => Write(converted), cancellationToken).ConfigureAwait(true);
                _log?.Information($"A trial converted {recording.Length / RvcStream.DeviceRate} s at {job.Semitones:+0;-0;0} semitones.");
                return new VoiceTrialResult(path, job.UserHz, files.MedianPitchHz, job.Semitones);
            }
            finally
            {
                _running = null;
            }
        }

        public async Task<string> FinishAsync(string converted, VoiceTone tone, CancellationToken cancellationToken)
        {
            if (tone.IsFlat)
            {
                return converted;
            }

            try
            {
                return await Task.Run(() => WriteFinished(converted, tone), cancellationToken).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidDataException)
            {
                throw new VoiceTrialFailedException($"The finish could not be applied: {exception.Message}", exception);
            }
        }

        // The finish only: the conversion is kept, so a change of finish is heard in moments.
        private string WriteFinished(string converted, VoiceTone tone)
        {
            float[] samples = WavFile.Read(File.ReadAllBytes(converted), out int rate);
            float[] finished = ToneFilter.Apply(samples, tone, rate);
            new PeakLimiter(rate).Process(finished);
            foreach (string old in Directory.GetFiles(_folder, "finished-*.wav"))
            {
                try
                {
                    File.Delete(old);
                }
                catch (IOException)
                {
                    // Still playing; it goes next time.
                }
            }

            string path = Path.Combine(_folder, $"finished-{Guid.NewGuid():N}.wav");
            File.WriteAllBytes(path, WavFile.Write(finished, rate));
            return path;
        }

        /// <summary>The shift a trial converts with, and whether it must measure the user first. Pure.</summary>
        public static (float Semitones, bool Measure) Plan(bool autoPitch, float adjustment, float knownUserHz, float voiceHz)
        {
            bool measure = autoPitch && knownUserHz <= 0f && voiceHz > 0f;
            return (PitchMatch.Applied(autoPitch, PitchMatch.Semitones(voiceHz, knownUserHz), adjustment), measure);
        }

        private string Write(float[] samples)
        {
            Directory.CreateDirectory(_folder);
            foreach (string old in Directory.GetFiles(_folder, "trial-*.wav"))
            {
                try
                {
                    File.Delete(old);
                }
                catch (IOException)
                {
                    // Still playing; it goes next time.
                }
            }

            string path = Path.Combine(_folder, $"trial-{Guid.NewGuid():N}.wav");
            File.WriteAllBytes(path, WavFile.Write(samples, RvcStream.DeviceRate));
            return path;
        }

        // One conversion, stepped by the frame loop: load, prime and warm, measure the user if needed, convert.
        private sealed class Job
        {
            private readonly VoiceTrialService _owner;
            private readonly float[] _input;
            private readonly float _voiceHz;
            private readonly VoiceTrialRequest _request;
            private readonly IProgress<float> _progress;
            private readonly CancellationToken _cancellation;
            private readonly TaskCompletionSource<float[]> _done = new TaskCompletionSource<float[]>();
            private readonly VoiceWarmup _warmup = new VoiceWarmup();
            private readonly PitchMeter _meter = new PitchMeter();
            private IDisposable _tick;
            private RvcStream _stream;
            private float[] _block;
            private float[] _out;
            private float[] _pitch;
            private float[] _output;
            private int _read;
            private int _written;
            private bool _measuring;
            private readonly int _passes;
            private int _pass;

            public Job(VoiceTrialService owner, float[] input, float voiceHz, VoiceTrialRequest request, IProgress<float> progress, CancellationToken cancellation)
            {
                _owner = owner;
                _input = input;
                _voiceHz = voiceHz;
                _request = request;
                _progress = progress;
                _cancellation = cancellation;
                (float semitones, bool measure) = Plan(request.AutoPitch, request.Adjustment, request.KnownUserHz, voiceHz);
                Semitones = semitones;
                UserHz = request.KnownUserHz;
                _measuring = measure;
                _passes = measure ? 2 : 1;
            }

            public float Semitones { get; private set; }

            public float UserHz { get; private set; }

            public Task<float[]> Run()
            {
                _tick = _owner._frameLoop.Register(Tick);
                return _done.Task;
            }

            private void Tick()
            {
                try
                {
                    if (_cancellation.IsCancellationRequested)
                    {
                        End();
                        _done.TrySetCanceled(_cancellation);
                        return;
                    }

                    Step();
                }
                catch (Exception exception)
                {
                    End();
                    _done.TrySetException(new VoiceTrialFailedException("The trial could not convert.", exception));
                }
            }

            private void Step()
            {
                if (_stream == null && !_warmup.IsActive)
                {
                    if (_owner._loader.IsDone)
                    {
                        _warmup.Begin(_owner._loader.Finish(_measuring ? 0f : Semitones, out _), "trial");
                    }

                    return;
                }

                if (_stream == null)
                {
                    _warmup.Step();
                    if (_warmup.CanTakeOver(false))
                    {
                        _stream = _warmup.Take();
                        _block = new float[_stream.BlockSamples];
                        _out = new float[_stream.BlockSamples];
                        _pitch = new float[_stream.BlockSamples / 480];
                        _output = new float[_input.Length + _stream.BlockSamples];
                    }

                    return;
                }

                if (_stream.IsBusy)
                {
                    if (_stream.Advance(_out, false))
                    {
                        Collect();
                    }

                    return;
                }

                if (_read < _input.Length)
                {
                    int count = Math.Min(_block.Length, _input.Length - _read);
                    Array.Copy(_input, _read, _block, 0, count);
                    Array.Clear(_block, count, _block.Length - count);
                    _read += _block.Length;
                    _stream.Start(_block);
                    return;
                }

                FinishPass();
            }

            private void Collect()
            {
                if (_measuring)
                {
                    _stream.CopySettledPitch(_pitch);
                    _meter.Add(_pitch);
                }
                else
                {
                    int count = Math.Min(_out.Length, _output.Length - _written);
                    Array.Copy(_out, 0, _output, _written, count);
                    _written += count;
                }

                float pass = Math.Min(1f, _read / (float)_input.Length);
                _progress?.Report((_pass + pass) / _passes);
                if (_measuring && _meter.IsLocked)
                {
                    // Ten seconds of voice are enough; the rest of a long recording need not be read twice.
                    _read = _input.Length;
                }
            }

            private void FinishPass()
            {
                if (_measuring)
                {
                    _measuring = false;
                    UserHz = _meter.MedianHz;
                    Semitones = PitchMatch.Applied(_request.AutoPitch, PitchMatch.Semitones(_voiceHz, UserHz), _request.Adjustment);
                    _stream.Semitones = Semitones;
                    _read = 0;
                    _pass++;
                    return;
                }

                float[] output = _output;
                End();
                _done.TrySetResult(output);
            }

            private void End()
            {
                _tick?.Dispose();
                _tick = null;
                _warmup.Dispose();
                _stream?.Dispose();
                _stream = null;
                if (_owner._loader.IsLoading)
                {
                    // Ended while the voice was loading: its stream is made and let go once the load finishes, so the
                    // next trial can load.
                    _tick = _owner._frameLoop.Register(Drain);
                }
            }

            private void Drain()
            {
                if (!_owner._loader.IsDone)
                {
                    return;
                }

                _tick?.Dispose();
                _tick = null;
                try
                {
                    _owner._loader.Finish(0f, out _).Dispose();
                }
                catch (Exception exception)
                {
                    _owner._log?.Warning($"A cancelled trial's voice could not load: {exception.Message}");
                }
            }
        }
    }
}
