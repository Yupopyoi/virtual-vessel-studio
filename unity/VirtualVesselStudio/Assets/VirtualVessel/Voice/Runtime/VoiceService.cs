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
    public sealed partial class VoiceService : IApplicationService, IVoiceConversion
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

        /// <summary>The parts of the delay from the microphone to the monitor, in the order of DelayMs.</summary>
        public static readonly string[] DelayParts = { "CaptureDevice", "StageInput", "Waiting", "Work", "Algorithm", "StageOutput", "Monitor", "OutputBuffer", "OutputDevice", "Total" };

    }
}
