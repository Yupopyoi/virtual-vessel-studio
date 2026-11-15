using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Audio;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.State;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Voice;
using VirtualVessel.Voice.Models;
using VirtualVessel.Voice.Rvc;

namespace VirtualVessel.Voice
{
    /// <summary>
    /// Runs <see cref="RvcStream"/> as a stage on the microphone. Inference Engine must be driven from the
    /// main thread, so each frame moves blocks along and the GPU works between frames; the mix never waits
    /// for it (audio detailed design 8.3). A prototype: developers turn it on, and it is not saved.
    /// A chosen voice loads behind the one converting and takes over at a block's end, so the audio never
    /// stops for a switch (system design 9.8, Voice detailed design 8.2).
    /// </summary>
    public sealed class VoiceService : IApplicationService, IVoiceConversion
    {
        public const string ServiceName = "Voice";

        private const int ChannelCount = 2;

        /// <summary>Marks a voice ID that is a folder, for a voice chosen by its folder.</summary>
        public const string FolderPrefix = "folder:";

        // 48 kHz samples in one 10 ms frame of the models.
        private const int BlockSamplesPerFrame = 480;

        // The stage's output comes in bursts, so the first one is put back by this much silence, which
        // every later burst inherits, to let a late burst arrive before the mix runs dry.
        private const int MarginFrames = 1440;

        // More waiting than this and the oldest is left out: being late would last for the whole stream.
        private const int MaxWaitingBlocks = 2;

        /// <summary>
        /// The window the models see, in 10 ms frames: 1 s only. With the user's speech before it, the user heard
        /// no gain from 2.5 s or 5.4 s, and 1 s is the lightest (decision record, 2026-11-03).
        /// </summary>
        public const int WindowFrames = VoiceModelLibrary.WindowFrames;

        private readonly IAudioStage _stage;
        private readonly IFrameLoop _frameLoop;
        private readonly VoiceModelLibrary _library;
        private readonly VoiceLoader _loader;
        private readonly VoiceStorage _storage;
        private Trial _trial;
        private readonly VoiceWarmup _next = new VoiceWarmup();
        private readonly PitchMeter _meter = new PitchMeter();
        private float[] _blockPitch;
        private bool _autoPitch = true;

        // The median pitch of the voice converting, and of the one loading or warming behind it.
        private float _voicePitchHz;
        private float _loadingPitchHz;
        private readonly ILog _log;
        private readonly ObservableValue<VoiceConversionState> _state = new ObservableValue<VoiceConversionState>(VoiceConversionState.Off);

        private IDisposable _tick;
        private RvcStream _stream;
        private string _streamVoice;
        private string _wantedVoice;
        private float[] _stereo;
        private float[] _waiting;
        private float[] _block;
        private float[] _output;
        private int _waitingSamples;
        private bool _primed;
        private bool _wanted;
        private readonly PerfTimer _workTimer;
        private readonly PerfTimer _issueTimer;
        private readonly PerfCounter _droppedCounter;
        private readonly PerfTimer[] _stageTimers;
        private readonly PerfTimer[] _delayTimers;
        private readonly double[] _delayParts = new double[DelayParts.Length];
        private readonly IPerformanceMetrics _metrics;
        private double _lastWorkMs;
        private float _semitones;
        // When each block on its way started; two can be on their way at once.
        private readonly Queue<long> _startedAt = new Queue<long>(4);
        private long _blocks;
        private long _dropped;
        private VoiceConversionStats _stats;
        private float[] _file;
        private string _fileName;
        private int _fileRead;
        private readonly System.Diagnostics.Stopwatch _fileClock = new System.Diagnostics.Stopwatch();

        /// <param name="library">The voices; null leaves the conversion unavailable.</param>
        /// <param name="metrics">Optional; each block's work and the blocks left out, for the totals at exit.</param>
        /// <param name="storage">The project's voice and this PC's measured pitch, read and saved; null keeps them in memory.</param>
        public VoiceService(IAudioStage stage, IFrameLoop frameLoop, VoiceModelLibrary library, ILog log, IPerformanceMetrics metrics = null, VoiceStorage storage = null)
        {
            _workTimer = metrics?.Timer("Voice", "BlockWork");
            _issueTimer = metrics?.Timer("Voice", "BlockIssue");
            _droppedCounter = metrics?.Counter("Voice", "DroppedBlocks");
            _stageTimers = new[] { "Prepare", "Features", "Pitch", "Generator", "Finish" }.Select(name => metrics?.Timer("Voice", "Issue." + name)).ToArray();
            _metrics = metrics;
            // Timers, not gauges: they are durations, and the totals at exit list timers.
            _delayTimers = DelayParts.Select(name => metrics?.Timer("Voice", "Delay." + name)).ToArray();
            _stage = stage ?? throw new ArgumentNullException(nameof(stage));
            _frameLoop = frameLoop ?? throw new ArgumentNullException(nameof(frameLoop));
            _library = library;
            _loader = new VoiceLoader(library);
            _log = log;
            _storage = storage ?? VoiceStorage.InMemory();
            _wantedVoice = _storage.Profile.VoiceModelId;
            _semitones = _storage.Profile.Semitones;
            _autoPitch = _storage.Profile.AutoPitch;
            _meter.Restore(_storage.Settings.UserPitchHz);
        }

        /// <summary>The voice chosen for the stream, as saved; a trial does not change it. Voice Lab keeps it from being deleted.</summary>
        public string InUseVoiceId => _storage.Profile.VoiceModelId;

        public bool InTrial => _trial != null;

        public string Name => ServiceName;

        public IReadOnlyObservable<VoiceConversionState> State => _state;

        public VoiceConversionStats Stats => _stats;

        private bool IsAttached => _state.Value == VoiceConversionState.Running || _state.Value == VoiceConversionState.NoMicrophone || _state.Value == VoiceConversionState.PlayingFile;

        public string VoiceId => _wantedVoice;

        /// <summary>The voices it converts with, for a trial that converts recordings with the same ones.</summary>
        public VoiceModelLibrary Library => _library;

        public bool IsSwitching => _next.IsActive || (_loader.IsLoading && _stream != null);

        public CrudResult<IReadOnlyList<VoiceConversionVoice>> ListVoices()
        {
            return _library?.List() ?? CrudResult<IReadOnlyList<VoiceConversionVoice>>.Success(Array.Empty<VoiceConversionVoice>());
        }

        public void SelectVoice(string voiceId)
        {
            VoiceProfile profile = _storage.Profile.Clone();
            profile.VoiceModelId = voiceId;
            _storage.SaveProfile(profile);
            if (_trial == null)
            {
                Choose(voiceId);
            }
        }

        /// <summary>
        /// Tries a voice by its folder, such as one just made and not yet registered, on the microphone (Voice Lab
        /// detailed design 8.3.4): the conversion turns on with it. Nothing is saved; the pitch settings changed during
        /// the trial are the trial's own.
        /// </summary>
        public void BeginTrial(string folder)
        {
            _trial ??= new Trial(_wanted);
            Choose(FolderPrefix + folder);
            Enable(true);
        }

        /// <summary>Ends a trial: the saved voice and pitch come back, and the conversion is off again if it was.</summary>
        public void EndTrial()
        {
            if (_trial == null)
            {
                return;
            }

            bool wasOn = _trial.WasOn;
            _trial = null;
            if (!wasOn)
            {
                Enable(false);
            }

            _semitones = _storage.Profile.Semitones;
            _autoPitch = _storage.Profile.AutoPitch;
            Choose(_storage.Profile.VoiceModelId);
            ApplyPitch();
        }

        private void Choose(string voiceId)
        {
            _wantedVoice = voiceId;
            if (_tick == null && _stream != null && _streamVoice != voiceId)
            {
                // Off, nothing is heard, so the old voice is simply let go; turning on loads the new one.
                _stream.Dispose();
                _stream = null;
            }
        }

        // The state before a trial, to put back after it.
        private sealed class Trial
        {
            public Trial(bool wasOn)
            {
                WasOn = wasOn;
            }

            public bool WasOn { get; }
        }

        public bool GpuAvailable => IsGpuUsable(UnityEngine.SystemInfo.supportsComputeShaders, UnityEngine.SystemInfo.graphicsDeviceType);

        /// <summary>
        /// Whether the GPU can run the conversion: compute shaders, and not a software device. No speed is required, as
        /// none is known yet (setup screen 13.3; Voice detailed design 17). Pure.
        /// </summary>
        public static bool IsGpuUsable(bool computeShaders, UnityEngine.Rendering.GraphicsDeviceType device)
        {
            return computeShaders && device != UnityEngine.Rendering.GraphicsDeviceType.Null;
        }

        public float LoadProgress => _loader.Progress;

        public string FileName => _file == null ? null : _fileName;

        public (float Position, float Length) FileTime => _file == null ? (0f, 0f) : (_fileRead / (float)RvcStream.DeviceRate, (_file.Length - RvcStream.DeviceRate) / (float)RvcStream.DeviceRate);

        /// <summary>The last block's times on the main thread, for diagnostics.</summary>
        public RvcTimings LastTimings { get; private set; }

        /// <summary>What stopped the conversion, for tests and diagnostics.</summary>
        public Exception Failure { get; private set; }

        public float Semitones
        {
            get => _semitones;
            set
            {
                _semitones = value;
                ApplyPitch();
                SavePitch();
            }
        }

        public bool AutoPitch
        {
            get => _autoPitch;
            set
            {
                _autoPitch = value;
                ApplyPitch();
                SavePitch();
            }
        }

        // A trial's pitch settings are its own and are not saved.
        private void SavePitch()
        {
            if (_trial != null)
            {
                return;
            }

            VoiceProfile profile = _storage.Profile.Clone();
            profile.Semitones = _semitones;
            profile.AutoPitch = _autoPitch;
            _storage.SaveProfile(profile);
        }

        public float AutoSemitones => PitchMatch.Semitones(_voicePitchHz, _meter.MedianHz);

        public float UserPitchHz => _meter.MedianHz;

        public float VoicePitchHz => _voicePitchHz;

        public void RemeasurePitch()
        {
            _meter.Reset();
            ApplyPitch();
            SaveUserPitch(0f);
        }

        private void SaveUserPitch(float hz)
        {
            VoiceSettings settings = _storage.Settings.Clone();
            settings.UserPitchHz = hz;
            _storage.SaveSettings(settings);
        }

        // The shift the stream converts with; it applies from the next block.
        private float AppliedSemitones => PitchMatch.Applied(_autoPitch, AutoSemitones, _semitones);

        private void ApplyPitch()
        {
            if (_stream != null)
            {
                _stream.Semitones = AppliedSemitones;
            }
        }

        /// <summary>What to show while on: running only when there is a voice to convert. Pure.</summary>
        public static VoiceConversionState AttachedState(bool capturing)
        {
            return capturing ? VoiceConversionState.Running : VoiceConversionState.NoMicrophone;
        }

        /// <summary>Samples to leave out so that no more than <paramref name="maxBlocks"/> wait. Pure.</summary>
        public static int SamplesToDrop(int waiting, int block, int maxBlocks)
        {
            int excess = waiting - maxBlocks * block;
            return excess <= 0 ? 0 : (excess + block - 1) / block * block;
        }

        /// <summary>Mixes interleaved stereo into mono, for a voice that a device may give on one channel. Pure apart from the target.</summary>
        public static void ToMono(ReadOnlySpan<float> stereo, Span<float> mono)
        {
            for (int i = 0; i < mono.Length; i++)
            {
                mono[i] = (stereo[i * ChannelCount] + stereo[i * ChannelCount + 1]) * 0.5f;
            }
        }

        /// <summary>Copies mono to both channels. Pure apart from the target.</summary>
        public static void ToStereo(ReadOnlySpan<float> mono, Span<float> stereo)
        {
            for (int i = 0; i < mono.Length; i++)
            {
                stereo[i * ChannelCount] = mono[i];
                stereo[i * ChannelCount + 1] = mono[i];
            }
        }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            // On again if it was on when the app closed: for someone who streams with a changed voice, starting with
            // their own voice is the worse failure (the user's decision, 2026-11-10).
            if (_storage.Profile.Enabled)
            {
                Enable(true);
            }

            return Task.CompletedTask;
        }

        /// <summary>Turns the conversion on or off as the user chose, and saves it; a trial's on and off are not saved.</summary>
        public void SetEnabled(bool enabled)
        {
            if (_trial == null)
            {
                VoiceProfile profile = _storage.Profile.Clone();
                profile.Enabled = enabled;
                _storage.SaveProfile(profile);
            }

            Enable(enabled);
        }

        private void Enable(bool enabled)
        {
            _wanted = enabled;
            if (!enabled)
            {
                _file = null;
                Stop();
                _next.Dispose();
                _state.Set(VoiceConversionState.Off);
                ReleaseTick();
                return;
            }

            // Only while it is wanted: turned off, the conversion costs the frames nothing.
            _tick ??= _frameLoop.Register(Tick);
            if (_stream != null && _streamVoice != _wantedVoice && !IsAttached)
            {
                _stream.Dispose();
                _stream = null;
            }

            if (_stream != null)
            {
                Attach();
            }
            else if (!_loader.IsLoading)
            {
                BeginLoading();
            }
        }

        public async Task<bool> PlayFileAsync(string path)
        {
            float[] samples;
            try
            {
                samples = await Task.Run(() => ReadFile(path));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidDataException || exception is AudioEngineException)
            {
                _log?.Warning($"This audio file cannot be read: {exception.Message}");
                return false;
            }

            _file = samples;
            _fileName = Path.GetFileName(path);
            _fileRead = 0;
            _fileClock.Reset();
            Enable(true);
            return true;
        }

        public IReadOnlyList<string> FileExtensions => AudioFile.Extensions;

        public void StopFile()
        {
            _file = null;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            _wanted = false;
            Stop();
            _tick?.Dispose();
            _tick = null;
            return _storage.FlushAsync();
        }

        public void Dispose()
        {
            _tick?.Dispose();
            _tick = null;
            _next.Dispose();
            _stream?.Dispose();
            _stream = null;
        }

        // A load in progress still needs the frames to finish on the main thread.
        private void ReleaseTick()
        {
            if (!_loader.IsLoading)
            {
                _tick?.Dispose();
                _tick = null;
            }
        }

        // A second of silence at the end carries the last block through, whatever its length. WAV is read here,
        // without the audio plugin; other formats, such as M4A from a phone, go through Windows' decoders.
        internal static float[] ReadFile(string path)
        {
            float[] decoded = string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase)
                ? WavFile.Read(File.ReadAllBytes(path), out int rate)
                : AudioFile.ReadMono(path, out rate);
            float[] samples = WavFile.ToRate(decoded, rate, RvcStream.DeviceRate);
            Array.Resize(ref samples, samples.Length + RvcStream.DeviceRate);
            return samples;
        }

        private void BeginLoading()
        {
            if (_wantedVoice == null && _library != null)
            {
                CrudResult<IReadOnlyList<VoiceConversionVoice>> voices = _library.List();
                _wantedVoice = voices.Ok ? VoiceModelLibrary.FirstUsable(voices.Value) : null;
            }

            bool switching = _stream != null;
            VoiceModelFiles files = Locate(_wantedVoice);
            string problem = _stage.IsAvailable ? _loader.Begin(files, _wantedVoice) : "the microphone's stage is unavailable.";
            if (problem == null)
            {
                _loadingPitchHz = FolderOf(_wantedVoice) != null ? files.MedianPitchHz : _library.MedianPitchOf(_wantedVoice);
            }
            else
            {
                _log?.Warning($"Voice conversion cannot load voice '{LogName(_wantedVoice)}': {problem}");
                if (switching)
                {
                    // The voice in use keeps converting.
                    _wantedVoice = _streamVoice;
                }
                else
                {
                    _state.Set(VoiceConversionState.Unavailable);
                }

                return;
            }

            if (!switching)
            {
                _state.Set(VoiceConversionState.Loading);
            }
        }

        private void Tick()
        {
            if (_loader.IsDone)
            {
                FinishLoading();
            }

            if (!IsAttached)
            {
                return;
            }

            if (_next.IsActive)
            {
                _next.Step();
            }
            else if (!_loader.IsLoading && _wantedVoice != _streamVoice)
            {
                BeginLoading();
            }

            _state.Set(_file != null ? VoiceConversionState.PlayingFile : AttachedState(_stage.IsCapturing));
            try
            {
                Pump();
            }
            catch (Exception exception)
            {
                _log?.Error("Voice conversion failed. The microphone goes back to the mix directly.", exception);
                Failure = exception;
                Stop();
                _state.Set(VoiceConversionState.Failed);
            }
        }

        private void FinishLoading()
        {
            bool switching = _stream != null && IsAttached;
            RvcStream stream = null;
            string voiceId = _loader.LoadingVoice;
            try
            {
                stream = _loader.Finish(PitchMatch.Applied(_autoPitch, PitchMatch.Semitones(_loadingPitchHz, _meter.MedianHz), _semitones), out voiceId);
                if (switching)
                {
                    // Warmed a little each frame, while the voice in use keeps converting.
                    _next.Begin(stream, voiceId);
                    _log?.Information($"Voice {LogName(voiceId)} loaded; warming up behind voice {LogName(_streamVoice)}.");
                    return;
                }

                _stereo ??= new float[RvcStream.DeviceRate * ChannelCount];
                _waiting ??= new float[RvcStream.DeviceRate];
                _block ??= new float[stream.BlockSamples];
                _output ??= new float[stream.BlockSamples * ChannelCount];
                _blockPitch ??= new float[stream.BlockSamples / BlockSamplesPerFrame];

                // The first runs compile the shaders and take far longer than a block, so they happen
                // here, where nobody is waiting for the audio yet.
                var silence = new float[stream.BlockSamples];
                for (int i = 0; i < VoiceWarmup.Blocks; i++)
                {
                    stream.Process(silence, silence);
                    Array.Clear(silence, 0, silence.Length);
                }

                _stream?.Dispose();
                _stream = stream;
                _streamVoice = voiceId;
                _voicePitchHz = _loadingPitchHz;
                _log?.Information($"Voice conversion is ready with voice {LogName(voiceId)}: block {_stream.BlockSamples / 48} ms, algorithm delay {_stream.AlgorithmDelaySamples / 48} ms.");
                if (_wanted)
                {
                    Attach();
                }
                else
                {
                    _state.Set(VoiceConversionState.Off);
                    ReleaseTick();
                }
            }
            catch (Exception exception)
            {
                stream?.Dispose();
                if (switching)
                {
                    _log?.Error($"Voice {LogName(voiceId)} could not load; voice {LogName(_streamVoice)} keeps converting.", exception);
                    _wantedVoice = _streamVoice;
                    return;
                }

                _log?.Error("The voice conversion models could not start.", exception);
                _stream?.Dispose();
                _stream = null;
                _state.Set(VoiceConversionState.Failed);
            }
        }

        /// <summary>A voice's ID as the log shows it: the prototype's is empty, so it gets a name. Pure.</summary>
        /// <summary>The folder a voice ID names, or null for a registered voice or the prototype's. Pure.</summary>
        public static string FolderOf(string voiceId)
        {
            return voiceId != null && voiceId.StartsWith(FolderPrefix, StringComparison.Ordinal) ? voiceId.Substring(FolderPrefix.Length) : null;
        }

        // Registered voices by their ID, a voice chosen by its folder by the folder.
        private VoiceModelFiles Locate(string voiceId)
        {
            if (_library == null || voiceId == null)
            {
                return VoiceModelFiles.Unusable("There is no voice.");
            }

            string folder = FolderOf(voiceId);
            return folder != null ? _library.LocateFolder(folder) : _library.Locate(voiceId);
        }

        public static string LogName(string voiceId)
        {
            return voiceId == VoiceModelLibrary.PrototypeId ? "prototype" : voiceId ?? "none";
        }

        private void SwapIfReady()
        {
            if (!_next.IsWarm)
            {
                return;
            }

            if (_next.VoiceId != _wantedVoice)
            {
                // Chosen and then changed again before it was ready.
                _next.Dispose();
                return;
            }

            if (!_next.CanTakeOver(_stream.IsBusy))
            {
                return;
            }

            _log?.Information($"Voice {LogName(_next.VoiceId)} takes over from voice {LogName(_streamVoice)}.");
            _streamVoice = _next.VoiceId;
            _voicePitchHz = _loadingPitchHz;
            _stream.Dispose();
            _stream = _next.Take();
            ApplyPitch();
        }

        private void Attach()
        {
            _waitingSamples = 0;
            _primed = false;
            _startedAt.Clear();
            _stage.SetEnabled(true);
            _state.Set(AttachedState(_stage.IsCapturing));
        }

        private void Stop()
        {
            if (IsAttached)
            {
                _stage.SetEnabled(false);
            }

            _stream?.Abandon();
        }

        /// <summary>The parts of the delay from the microphone to the monitor, in the order of DelayMs.</summary>
        public static readonly string[] DelayParts = { "CaptureDevice", "StageInput", "Waiting", "Work", "Algorithm", "StageOutput", "Monitor", "OutputBuffer", "OutputDevice", "Total" };

        /// <summary>
        /// The delay's parts in milliseconds into <paramref name="parts"/>, in the order of DelayParts, ending
        /// with their sum. Each queue's part is its average fill, which by Little's law is the average wait in it.
        /// Pure apart from writing the caller's buffer, which is reused every frame to avoid garbage.
        /// </summary>
        public static void DelayMs(AudioStageDelay outside, int waitingSamples, double workMs, int algorithmSamples, Span<double> parts)
        {
            const double MsPerSample = 1000.0 / RvcStream.DeviceRate;
            parts[0] = outside.CaptureDeviceMs;
            parts[1] = outside.StageInputMs;
            parts[2] = waitingSamples * MsPerSample;
            parts[3] = workMs;
            parts[4] = algorithmSamples * MsPerSample;
            parts[5] = outside.StageOutputMs;
            parts[6] = outside.MonitorMs;
            parts[7] = outside.OutputBufferMs;
            parts[8] = outside.OutputDeviceMs;
            double total = 0.0;
            for (int i = 0; i < 9; i++)
            {
                total += parts[i];
            }

            parts[9] = total;
        }

        private void Pump()
        {
            Collect();
            if (_stream.Advance(_block, false))
            {
                Deliver();
            }

            SwapIfReady();
            StartBlock();
            RecordDelay();
        }

        // What waits here stays until the next frame, so one sample a frame is its average. Only while the
        // totals record timers, so the call to the engine costs nothing otherwise.
        private void RecordDelay()
        {
            if (_metrics == null || !_metrics.IsDetailedEnabled || _file != null)
            {
                return;
            }

            DelayMs(_stage.Delay, _waitingSamples, _lastWorkMs, _stream.AlgorithmDelaySamples, _delayParts);
            for (int i = 0; i < _delayParts.Length; i++)
            {
                _delayTimers[i].Record(TimeSpan.FromMilliseconds(_delayParts[i]));
            }
        }

        private void StartBlock()
        {
            int block = _stream.BlockSamples;
            if (_stream.CanStart && _waitingSamples >= block)
            {
                int drop = SamplesToDrop(_waitingSamples, block, MaxWaitingBlocks);
                if (drop > 0)
                {
                    Consume(drop);
                    _dropped += drop / block;
                    _droppedCounter?.Add(drop / block);
                }

                // The microphone is cleaned as it is captured, if the user has it on (audio detailed design 8.6). A file
                // in its place is cleaned here the same way, so it sounds as the microphone would. The block is a whole
                // number of the denoiser's 10 ms frames, so nothing waits for it.
                if (_file != null && _stage.Denoises)
                {
                    _stage.Denoise(_waiting, block);
                }

                _startedAt.Enqueue(System.Diagnostics.Stopwatch.GetTimestamp());
                _stream.Start(new ReadOnlySpan<float>(_waiting, 0, block));
                Consume(block);
            }
        }

        // The user's pitch, from the frames of the block just delivered that no longer change, until the meter keeps
        // its median. Its buffer is reused: this runs every block.
        private void MeasurePitch()
        {
            if (_meter.IsLocked)
            {
                return;
            }

            _stream.CopySettledPitch(_blockPitch);
            if (_meter.Add(_blockPitch))
            {
                ApplyPitch();
            }

            if (_meter.IsLocked)
            {
                // Kept for this PC, so the next start matches the pitch from its first block.
                SaveUserPitch(_meter.MedianHz);
            }
        }

        private void Collect()
        {
            int room = _waiting.Length - _waitingSamples;
            int read = _stage.Read(_stereo, Math.Min(room, _stereo.Length / ChannelCount));
            if (_file != null)
            {
                // What the microphone captured is dropped, so it does not arrive late after the file.
                CollectFile(room);
                return;
            }

            ToMono(new ReadOnlySpan<float>(_stereo, 0, read * ChannelCount), new Span<float>(_waiting, _waitingSamples, read));
            _waitingSamples += read;
        }

        // At real time, like a microphone, so that it can be heard as it will sound on a stream.
        private void CollectFile(int room)
        {
            _fileClock.Start();
            long due = _fileClock.ElapsedMilliseconds * RvcStream.DeviceRate / 1000;
            int take = (int)Math.Min(room, Math.Min(due - _fileRead, _file.Length - _fileRead));
            Array.Copy(_file, _fileRead, _waiting, _waitingSamples, take);
            _fileRead += take;
            _waitingSamples += take;
            if (_fileRead == _file.Length)
            {
                _file = null;
            }
        }

        private void Consume(int samples)
        {
            _waitingSamples -= samples;
            Array.Copy(_waiting, samples, _waiting, 0, _waitingSamples);
        }

        private void Deliver()
        {
            ToStereo(_block, _output);
            if (!_primed)
            {
                _primed = true;
                var margin = new float[MarginFrames * ChannelCount];
                _stage.Write(margin, MarginFrames);
            }

            _stage.Write(_output, _block.Length);
            _blocks++;
            MeasurePitch();
            long started = _startedAt.Count > 0 ? _startedAt.Dequeue() : System.Diagnostics.Stopwatch.GetTimestamp();
            double work = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            _workTimer?.Record(TimeSpan.FromMilliseconds(work));
            _lastWorkMs = work;
            RvcTimings timings = _stream.Timings;
            _issueTimer?.Record(TimeSpan.FromMilliseconds(timings.Issue));
            double[] stages = { timings.Prepare, timings.Features, timings.Pitch, timings.Generator, timings.Finish };
            for (int i = 0; i < stages.Length; i++)
            {
                _stageTimers[i]?.Record(TimeSpan.FromMilliseconds(stages[i]));
            }

            LastTimings = timings;
            _stats = new VoiceConversionStats(_stream.BlockSamples / 48.0, work, _stream.Timings.Issue, _blocks, _dropped);
        }
    }
}
