using System;
using System.Collections.Generic;
using System.IO;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Voice;

namespace VirtualVessel.Voice.Models
{
    /// <summary>What the conversion loads for one voice; <see cref="Problem"/> is null when it can be used.</summary>
    public readonly struct VoiceModelFiles
    {
        public VoiceModelFiles(string features, string pitch, string generator, string speaker, string problem, int medianPitchHz = 0)
        {
            Features = features;
            Pitch = pitch;
            Generator = generator;
            Speaker = speaker;
            Problem = problem;
            MedianPitchHz = medianPitchHz;
        }

        /// <summary>The voice's median pitch from its voice.json, when it was found by folder; 0 when unknown.</summary>
        public int MedianPitchHz { get; }

        /// <summary>HuBERT, shared by every voice.</summary>
        public string Features { get; }

        /// <summary>RMVPE, shared by every voice.</summary>
        public string Pitch { get; }

        public string Generator { get; }

        public string Speaker { get; }

        /// <summary>Why the voice cannot be used, for the developers' log; null when it can.</summary>
        public string Problem { get; }

        public static VoiceModelFiles Unusable(string problem)
        {
            return new VoiceModelFiles(null, null, null, null, problem);
        }
    }

    /// <summary>
    /// Finds and checks the voices the conversion can run (Voice detailed design 6.1): the registered voices, and
    /// the prototype's voice that developers place by hand. HuBERT and RMVPE are the same for every voice, so one
    /// pair serves them all.
    /// </summary>
    public sealed class VoiceModelLibrary
    {
        /// <summary>The prototype's voice, which has no record.</summary>
        public const string PrototypeId = "";

        // A voice found by its folder: a registered voice's ID shape, so Locate checks it the same way.
        private const string TrialId = VoiceModelStore.IdPrefix + "trial";

        /// <summary>The prototype's folder under the Data Root's VoiceModels.</summary>
        public const string PrototypeFolder = "Prototype";

        /// <summary>
        /// Where the shared models go, by the version of their export: they pair with the generators' shape and the
        /// speech anchor. Until activation places them (Voice Lab detailed design 8.1), the prototype's are used.
        /// </summary>
        public const string SharedFolder = "Shared/1";

        /// <summary>The window the models see, in 10 ms frames; 1 s only (Voice detailed design 10).</summary>
        public const int WindowFrames = 100;

        /// <summary>The frames a generator returns each block: block, crossfade, search, and the guards.</summary>
        public const int ReturnedFrames = 21;

        public const string PitchFile = "rmvpe_32.sentis";

        private readonly string _root;
        private readonly string _prototype;
        private readonly IVoiceModelRepository _voices;

        /// <param name="root">VoiceModels, holding the registered voices and the shared models.</param>
        /// <param name="prototype">The prototype's flat folder of models.</param>
        /// <param name="voices">The registered voices; null lists the prototype's only.</param>
        public VoiceModelLibrary(string root, string prototype, IVoiceModelRepository voices)
        {
            _root = root;
            _prototype = prototype;
            _voices = voices;
        }

        /// <summary>The library over the Data Root's VoiceModels.</summary>
        public static VoiceModelLibrary Create(string voiceModels, IVoiceModelRepository voices)
        {
            return new VoiceModelLibrary(voiceModels, Path.Combine(voiceModels, PrototypeFolder), voices);
        }

        /// <summary>The prototype's generator for a window. Pure.</summary>
        public static string GeneratorFile(int windowFrames)
        {
            return $"fused_w{windowFrames}.sentis";
        }

        /// <summary>
        /// HuBERT's file for a window, named by the window's 16 kHz samples. It takes the speech anchor before the
        /// window and returns the window's frames only (SpeechAnchor). Pure.
        /// </summary>
        public static string FeaturesFile(int windowFrames)
        {
            return $"hubert_{windowFrames * 160}_anchored.sentis";
        }

        /// <summary>The voices to choose from: the prototype's first when it is there, then the registered ones newest first.</summary>
        public CrudResult<IReadOnlyList<VoiceConversionVoice>> List()
        {
            var choices = new List<VoiceConversionVoice>();
            if (Locate(PrototypeId).Problem == null)
            {
                choices.Add(new VoiceConversionVoice(PrototypeId, null, 0, true));
            }

            if (_voices == null)
            {
                return CrudResult<IReadOnlyList<VoiceConversionVoice>>.Success(choices);
            }

            CrudResult<IReadOnlyList<VoiceModelRecord>> voices = _voices.List();
            if (!voices.Ok)
            {
                return CrudResult<IReadOnlyList<VoiceConversionVoice>>.From(voices.WithoutValue);
            }

            foreach (VoiceModelRecord voice in voices.Value)
            {
                choices.Add(new VoiceConversionVoice(voice.Id, voice.Name, voice.MedianPitchHz, Locate(voice.Id).Problem == null));
            }

            return CrudResult<IReadOnlyList<VoiceConversionVoice>>.Success(choices);
        }

        /// <summary>A registered voice's median pitch in Hz; 0 for the prototype's or when unknown. Reads its record.</summary>
        /// <summary>A registered voice's finish; flat for another voice or when it cannot be read.</summary>
        public VoiceTone ToneOf(string voiceId)
        {
            if (_voices == null || !VoiceModelStore.IsVoiceId(voiceId))
            {
                return default;
            }

            CrudResult<VoiceModelRecord> voice = _voices.Get(voiceId);
            return voice.Ok ? voice.Value.GetTone() : default;
        }

        /// <summary>Keeps a registered voice's finish. Invalid for a voice that is not registered.</summary>
        public CrudResult SetTone(string voiceId, VoiceTone tone)
        {
            if (_voices == null || !VoiceModelStore.IsVoiceId(voiceId))
            {
                return CrudResult.Fail(CrudError.Invalid, $"'{voiceId}' is not a registered voice.");
            }

            return _voices.SetTone(voiceId, tone);
        }

        public int MedianPitchOf(string voiceId)
        {
            if (_voices == null || !VoiceModelStore.IsVoiceId(voiceId))
            {
                return 0;
            }

            CrudResult<VoiceModelRecord> voice = _voices.Get(voiceId);
            return voice.Ok ? voice.Value.MedianPitchHz : 0;
        }

        /// <summary>
        /// A voice in any folder holding voice.sentis, voice.spk, and voice.json, such as one just built and not yet
        /// registered (Voice Lab detailed design 8.3.4): checked as a registered one is. Reads its voice.json.
        /// </summary>
        public VoiceModelFiles LocateFolder(string folder)
        {
            string manifest = null;
            try
            {
                string path = Path.Combine(folder ?? string.Empty, VoiceModelStore.ManifestFile);
                manifest = File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return LocateFolder(folder, _root, _prototype, manifest, File.Exists);
        }

        /// <summary>A voice in <paramref name="folder"/>, with the shared models as <see cref="Locate(string, string, string, Func{string, string}, string, Func{string, bool})"/> finds them. Pure but for <paramref name="exists"/>.</summary>
        public static VoiceModelFiles LocateFolder(string folder, string root, string prototype, string manifest, Func<string, bool> exists)
        {
            if (string.IsNullOrEmpty(folder))
            {
                return VoiceModelFiles.Unusable("There is no folder.");
            }

            VoiceModelFiles files = Locate(TrialId, root, prototype, _ => folder, manifest, exists);
            return files.Problem != null
                ? files
                : new VoiceModelFiles(files.Features, files.Pitch, files.Generator, files.Speaker, null, VoiceModelStore.MedianPitch(manifest));
        }

        /// <summary>The voice's files, checked; reads its voice.json. Not for hot paths.</summary>
        public VoiceModelFiles Locate(string voiceId)
        {
            string manifest = null;
            if (voiceId != PrototypeId && _voices != null && VoiceModelStore.IsVoiceId(voiceId))
            {
                string path = Path.Combine(_voices.FolderOf(voiceId), VoiceModelStore.ManifestFile);
                try
                {
                    manifest = File.Exists(path) ? File.ReadAllText(path) : null;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return Locate(voiceId, _root, _prototype, _voices == null ? null : (Func<string, string>)_voices.FolderOf, manifest, File.Exists);
        }

        /// <summary>
        /// The files of a voice: the shared models from <see cref="SharedFolder"/>, or else the prototype's, and the
        /// voice's own generator and speaker. Pure but for <paramref name="exists"/>.
        /// </summary>
        /// <param name="folderOf">A registered voice's folder; null when there are none.</param>
        /// <param name="manifest">The registered voice's voice.json text; null when it could not be read.</param>
        public static VoiceModelFiles Locate(string voiceId, string root, string prototype, Func<string, string> folderOf, string manifest, Func<string, bool> exists)
        {
            string features = Shared(root, prototype, FeaturesFile(WindowFrames), exists);
            string pitch = Shared(root, prototype, PitchFile, exists);
            if (features == null || pitch == null)
            {
                return VoiceModelFiles.Unusable($"The shared models ({FeaturesFile(WindowFrames)}, {PitchFile}) are in neither {SharedFolder} nor the prototype's folder.");
            }

            string generator;
            string speaker;
            if (voiceId == PrototypeId)
            {
                if (string.IsNullOrEmpty(prototype))
                {
                    return VoiceModelFiles.Unusable("There is no prototype folder.");
                }

                generator = Path.Combine(prototype, GeneratorFile(WindowFrames));
                speaker = Path.ChangeExtension(generator, ".spk");
            }
            else
            {
                if (folderOf == null || !VoiceModelStore.IsVoiceId(voiceId))
                {
                    return VoiceModelFiles.Unusable($"'{voiceId}' is not a registered voice.");
                }

                string problem = ShapeProblem(manifest);
                if (problem != null)
                {
                    return VoiceModelFiles.Unusable($"Voice {voiceId}: {problem}");
                }

                string folder = folderOf(voiceId);
                generator = Path.Combine(folder, "voice.sentis");
                speaker = Path.Combine(folder, "voice.spk");
            }

            return exists(generator) && exists(speaker)
                ? new VoiceModelFiles(features, pitch, generator, speaker, null)
                : VoiceModelFiles.Unusable($"Voice '{voiceId}' has no {Path.GetFileName(generator)} or {Path.GetFileName(speaker)}.");
        }

        /// <summary>
        /// Why a voice.json does not fit this version's conversion; null when it does. The window and the frames a
        /// generator returns fix its tensors, so a voice made for others cannot run (Voice detailed design 6.1). Pure.
        /// </summary>
        public static string ShapeProblem(string manifest)
        {
            if (manifest == null)
            {
                return "voice.json cannot be read.";
            }

            ConfigDocument document;
            try
            {
                document = ConfigDocument.Parse(manifest);
            }
            catch (FormatException)
            {
                return "voice.json is not JSON.";
            }

            int frames = document.Get("frames", 0);
            int returned = document.Get("returned", 0);
            return frames == WindowFrames && returned == ReturnedFrames
                ? null
                : $"made for a window of {frames} frames returning {returned}; this version runs {WindowFrames} and {ReturnedFrames}.";
        }

        /// <summary>The voice to start with when none is chosen: the first usable one, or null. Pure.</summary>
        public static string FirstUsable(IReadOnlyList<VoiceConversionVoice> choices)
        {
            foreach (VoiceConversionVoice choice in choices)
            {
                if (choice.Usable)
                {
                    return choice.Id;
                }
            }

            return null;
        }

        private static string Shared(string root, string prototype, string file, Func<string, bool> exists)
        {
            string shared = string.IsNullOrEmpty(root) ? null : Path.Combine(root, SharedFolder, file);
            if (shared != null && exists(shared))
            {
                return shared;
            }

            string old = string.IsNullOrEmpty(prototype) ? null : Path.Combine(prototype, file);
            return old != null && exists(old) ? old : null;
        }
    }
}
