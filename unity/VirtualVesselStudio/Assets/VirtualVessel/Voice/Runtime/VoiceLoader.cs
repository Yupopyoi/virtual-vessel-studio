using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Unity.InferenceEngine;
using VirtualVessel.Voice.Models;
using VirtualVessel.Voice.Rvc;

namespace VirtualVessel.Voice
{
    /// <summary>
    /// Loads a voice's models on the thread pool and makes its <see cref="RvcStream"/> on the main thread, where
    /// Inference Engine's workers must be made (Voice detailed design 8.2, 9). HuBERT and RMVPE are the same for every
    /// voice, so they are kept and a switch loads only the generator and the speaker vector. One load at a time.
    /// </summary>
    public sealed class VoiceLoader
    {
        /// <summary>The crossfade at each join, in 10 ms frames; the generators are exported for it.</summary>
        public const int Crossfade = 5;

        private readonly VoiceModelLibrary _library;
        private Task<Loaded> _loading;
        private Model _features;
        private Model _pitch;
        private string _featuresPath;
        private string _pitchPath;
        private int _loadedModels;
        private int _modelsToLoad;

        /// <param name="library">The voices; null leaves nothing to load.</param>
        public VoiceLoader(VoiceModelLibrary library)
        {
            _library = library;
        }

        public bool IsLoading => _loading != null;

        public bool IsDone => _loading != null && _loading.IsCompleted;

        /// <summary>The voice being loaded; null when none is.</summary>
        public string LoadingVoice { get; private set; }

        /// <summary>From 0 to 1. The last step, warming up, is counted as much as a model: it takes as long.</summary>
        public float Progress => _modelsToLoad == 0 ? 0f : _loadedModels / (_modelsToLoad + 1f);

        /// <summary>Starts loading a voice; returns why it cannot, or null when it has started.</summary>
        public string Begin(string voiceId)
        {
            if (_loading != null)
            {
                throw new InvalidOperationException("A voice is loading already.");
            }

            VoiceModelFiles files = _library == null || voiceId == null ? VoiceModelFiles.Unusable("There is no voice.") : _library.Locate(voiceId);
            if (files.Problem != null)
            {
                return files.Problem;
            }

            // Read here, on the main thread, so the loading thread touches no field it does not own.
            Model features = files.Features == _featuresPath ? _features : null;
            Model pitch = files.Pitch == _pitchPath ? _pitch : null;
            _loadedModels = 0;
            _modelsToLoad = 1 + (features == null ? 1 : 0) + (pitch == null ? 1 : 0);
            LoadingVoice = voiceId;
            _loading = Task.Run(() => new Loaded
            {
                FeaturesPath = files.Features,
                PitchPath = files.Pitch,
                Features = features ?? LoadModel(files.Features),
                Pitch = pitch ?? LoadModel(files.Pitch),
                Generator = LoadModel(files.Generator),
                Speaker = ReadSpeaker(files.Speaker),
            });
            return null;
        }

        /// <summary>
        /// The loaded voice's stream, once <see cref="IsDone"/>; the next load can then begin. Main thread. Throws what
        /// the load threw.
        /// </summary>
        public RvcStream Finish(float semitones, out string voiceId)
        {
            Task<Loaded> loading = _loading ?? throw new InvalidOperationException("Nothing is loading.");
            voiceId = LoadingVoice;
            _loading = null;
            LoadingVoice = null;
            Loaded voice = loading.GetAwaiter().GetResult();
            _features = voice.Features;
            _pitch = voice.Pitch;
            _featuresPath = voice.FeaturesPath;
            _pitchPath = voice.PitchPath;
            return new RvcStream(voice.Features, voice.Pitch, voice.Generator, voice.Speaker, Shape(VoiceModelLibrary.WindowFrames), BackendType.GPUCompute, semitones);
        }

        /// <summary>The voice's speaker vector: 256 little-endian floats, beside its generator.</summary>
        public static float[] ReadSpeaker(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            var speaker = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, speaker, 0, speaker.Length * sizeof(float));
            return speaker;
        }

        /// <summary>The shape the generators are exported for. Pure.</summary>
        public static RvcStreamShape Shape(int windowFrames)
        {
            // A 50 ms crossfade: with 20 ms the joins changed the sound every block, heard on held notes as a
            // wobble in pitch (decision record, 2026-11-03).
            return new RvcStreamShape(windowFrames, 10, Crossfade, 1, 2, 3, 10 + Crossfade + 1 + 2 + 3 + 24, 32);
        }

        private Model LoadModel(string path)
        {
            Model model = ModelLoader.Load(path);
            Interlocked.Increment(ref _loadedModels);
            return model;
        }

        // What a load gives back: the shared models, which may be the ones already loaded, and the voice's own.
        private sealed class Loaded
        {
            public string FeaturesPath;
            public string PitchPath;
            public Model Features;
            public Model Pitch;
            public Model Generator;
            public float[] Speaker;
        }
    }
}
