using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.ProjectData.Records;

namespace VirtualVessel.Audio.Clips
{
    /// <summary>The list of the files that come with the app, built-in-audio.json (BGM and SE detailed design 6.3).</summary>
    public sealed class BuiltInAudioManifest
    {
        public const int SchemaVersion = 1;

        public List<BuiltInAudioClip> Clips { get; set; } = new List<BuiltInAudioClip>();
    }

    public sealed class BuiltInAudioClip
    {
        /// <summary>Stable across versions, such as "se-chime"; what <see cref="BuiltInAudioState"/> remembers.</summary>
        public string Id { get; set; }

        /// <summary>"bgm" or "soundEffect".</summary>
        public string Kind { get; set; }

        /// <summary>The file, relative to the manifest's folder.</summary>
        public string File { get; set; }

        /// <summary>The name per language code, such as "ja" and "en".</summary>
        public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// built-in-audio.json in the Data Root's Audio folder: which built-in files were added. A file is added once; one
    /// the user deleted does not come back.
    /// </summary>
    public sealed class BuiltInAudioState
    {
        public const int SchemaVersion = 1;

        public List<string> Added { get; set; } = new List<string>();
    }

    /// <summary>
    /// Adds the BGM and sound effects that come with the app to the library, as ordinary registrations the user can
    /// rename and delete (BGM and SE detailed design 6.3). Runs after startup without holding it up, and adds only what
    /// it has not added before, so a later version can bring new ones.
    /// </summary>
    public sealed class BuiltInAudioClips : IApplicationService
    {
        public const string ServiceName = "BuiltInAudio";

        /// <summary>The folder under StreamingAssets with the files and their list.</summary>
        public const string Folder = "Audio/BuiltIn";

        public const string ManifestFile = "built-in-audio.json";
        public const string StateFile = "built-in-audio.json";
        public const string FallbackLanguage = "en";

        private readonly IAudioClipLibrary _library;
        private readonly string _builtInFolder;
        private readonly string _language;
        private readonly ILog _log;
        private readonly ConfigFileStore<BuiltInAudioManifest> _manifest;
        private readonly ConfigFileStore<BuiltInAudioState> _state;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();

        /// <param name="builtInFolder">The folder with the files and <see cref="ManifestFile"/>.</param>
        /// <param name="audioFolder">The Data Root's Audio folder, where <see cref="StateFile"/> is kept.</param>
        /// <param name="language">The language of the names the files are added with; the user may rename them later.</param>
        public BuiltInAudioClips(IAudioClipLibrary library, string builtInFolder, string audioFolder, string language, ISystemClock clock, ILog log)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _builtInFolder = builtInFolder ?? throw new ArgumentNullException(nameof(builtInFolder));
            _language = language;
            _log = log;
            _manifest = new ConfigFileStore<BuiltInAudioManifest>(builtInFolder, ManifestFile, BuiltInAudioManifest.SchemaVersion, () => new BuiltInAudioManifest(), clock);
            _state = new ConfigFileStore<BuiltInAudioState>(audioFolder, StateFile, BuiltInAudioState.SchemaVersion, () => new BuiltInAudioState(), clock);
        }

        public string Name => ServiceName;

        /// <summary>The adding that runs now, for tests.</summary>
        public Task Adding { get; private set; } = Task.CompletedTask;

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            // Not awaited: copying the files on the first start must not hold up the app. The Cards follow the
            // library's Changed event as each one is added. Runs off the main thread, as the library's adding does.
            Adding = AddMissingAsync(_stop.Token);
            return Task.CompletedTask;
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken)
        {
            _stop.Cancel();
            try
            {
                await Adding.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _stop.Dispose();
        }

        /// <summary>The built-in files not added yet, in the list's order. Pure.</summary>
        public static IReadOnlyList<BuiltInAudioClip> Missing(IEnumerable<BuiltInAudioClip> clips, IEnumerable<string> added)
        {
            var done = new HashSet<string>(added ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            return (clips ?? Enumerable.Empty<BuiltInAudioClip>())
                .Where(clip => clip != null && !string.IsNullOrEmpty(clip.Id) && !done.Contains(clip.Id) && KindOf(clip.Kind) != null && IsInside(clip.File))
                .ToList();
        }

        /// <summary>The clip's name in <paramref name="language"/>, else in English, else its ID. Pure.</summary>
        public static string NameFor(BuiltInAudioClip clip, string language)
        {
            if (clip.Names != null)
            {
                if (language != null && clip.Names.TryGetValue(language, out string name) && !string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }

                if (clip.Names.TryGetValue(FallbackLanguage, out string fallback) && !string.IsNullOrWhiteSpace(fallback))
                {
                    return fallback;
                }
            }

            return clip.Id;
        }

        public static AudioClipKind? KindOf(string kind)
        {
            switch (kind)
            {
                case "bgm":
                    return AudioClipKind.Bgm;
                case "soundEffect":
                    return AudioClipKind.SoundEffect;
                default:
                    return null;
            }
        }

        /// <summary>Whether a file in the list stays under its folder: a plain relative path, without "..". Pure.</summary>
        public static bool IsInside(string file)
        {
            if (string.IsNullOrWhiteSpace(file) || Path.IsPathRooted(file))
            {
                return false;
            }

            return file.Split('/', '\\').All(part => part.Length > 0 && part != "." && part != "..");
        }

        private async Task AddMissingAsync(CancellationToken cancellationToken)
        {
            try
            {
                await AddAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                // The Data Root may be unwritable; the user's own files still work, and the next start tries again.
                _log?.Warning("The built-in audio files could not be added.", exception);
            }
        }

        private async Task AddAsync(CancellationToken cancellationToken)
        {
            if (!Directory.Exists(_builtInFolder))
            {
                _log?.Information("There are no built-in audio files to add.");
                return;
            }

            ConfigLoadResult<BuiltInAudioManifest> manifest = _manifest.Load();
            ConfigLoadResult<BuiltInAudioState> state = _state.Load();
            List<string> added = state.Value.Added ?? new List<string>();
            IReadOnlyList<BuiltInAudioClip> missing = Missing(manifest.Value.Clips, added);
            if (missing.Count == 0)
            {
                return;
            }

            int failed = 0;
            foreach (BuiltInAudioClip clip in missing)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = Path.Combine(_builtInFolder, clip.File);
                CrudResult<AudioClipRecord> result = await _library.AddAsync(KindOf(clip.Kind).Value, path, NameFor(clip, _language), clip.Names, cancellationToken).ConfigureAwait(false);
                if (!result.Ok)
                {
                    // Not remembered, so it is tried again on the next start.
                    failed++;
                    _log?.Warning($"Built-in audio {clip.Id} could not be added ({result.Error}): {result.Detail}");
                    continue;
                }

                // Remembered after each one, so a stop part way does not add the same file twice.
                added = new List<string>(added) { clip.Id };
                _state.Save(new BuiltInAudioState { Added = added });
            }

            _log?.Information($"Added {missing.Count - failed} built-in audio files; {failed} could not be added.");
        }
    }
}
