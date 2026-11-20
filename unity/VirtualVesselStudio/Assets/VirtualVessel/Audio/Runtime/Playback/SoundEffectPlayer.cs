using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Audio.Clips;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.ProjectData.Records;

namespace VirtualVessel.Audio.Playback
{
    /// <summary>
    /// The SE player (BGM and SE detailed design 7, 8.2): keeps every registered sound effect loaded in the engine, off
    /// the main thread, following the library, so a press plays at once.
    /// </summary>
    public sealed class SoundEffectPlayer : IApplicationService, ISoundEffectPlayer
    {
        public const string ServiceName = "SoundEffects";

        private readonly Func<IAudioPlayerEngine> _engine;
        private readonly IAudioClipLibrary _library;
        private readonly ILog _log;
        private readonly PerfCounter _stolen;
        private readonly object _gate = new object();

        // The effects loaded, by registration ID: the engine's handle and the file's volume. Under _gate.
        private readonly Dictionary<string, (int Effect, float GainDb, string Path)> _loaded = new Dictionary<string, (int, float, string)>();
        private long _countedStolen;
        private Task _syncing = Task.CompletedTask;
        private bool _stopped;

        public SoundEffectPlayer(Func<IAudioPlayerEngine> engine, IAudioClipLibrary library, ILog log, IPerformanceMetrics metrics = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _log = log;
            _stolen = metrics?.Counter("Audio", "SoundEffect.VoicesStolen");
        }

        public string Name => ServiceName;

        /// <summary>The loading that runs now, for tests.</summary>
        public Task Syncing
        {
            get
            {
                lock (_gate)
                {
                    return _syncing;
                }
            }
        }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            _library.Changed += OnLibraryChanged;
            Sync();
            return Task.CompletedTask;
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken)
        {
            _library.Changed -= OnLibraryChanged;
            Task syncing;
            lock (_gate)
            {
                _stopped = true;
                syncing = _syncing;
            }

            await syncing.ConfigureAwait(false);
        }

        public void Dispose()
        {
        }

        public void Play(string id)
        {
            IAudioPlayerEngine engine = _engine();
            (int Effect, float GainDb, string Path) loaded;
            lock (_gate)
            {
                if (engine == null || id == null || !_loaded.TryGetValue(id, out loaded))
                {
                    _log?.Warning($"Sound effect {id} is not loaded and does not play.");
                    return;
                }
            }

            engine.PlayEffect(loaded.Effect, loaded.GainDb);
            long stolen = engine.EffectsStolen;
            _stolen?.Add(stolen - _countedStolen);
            _countedStolen = stolen;
        }

        public void StopAll()
        {
            _engine()?.StopEffects();
        }

        public bool IsReady(string id)
        {
            lock (_gate)
            {
                return id != null && _loaded.ContainsKey(id);
            }
        }

        /// <summary>
        /// What to load and unload so the engine holds what is registered: new effects, and effects whose file
        /// changed, load; those gone unload. Pure.
        /// </summary>
        public static (IReadOnlyList<AudioClipRecord> Load, IReadOnlyList<string> Unload) Plan(IReadOnlyDictionary<string, string> loadedPaths, IReadOnlyList<AudioClipRecord> registered)
        {
            var load = new List<AudioClipRecord>();
            var unload = new List<string>();
            var present = new HashSet<string>();
            foreach (AudioClipRecord record in registered)
            {
                present.Add(record.Id);
                if (!loadedPaths.TryGetValue(record.Id, out string path) || path != record.File.Path)
                {
                    load.Add(record);
                }
            }

            foreach (string id in loadedPaths.Keys)
            {
                if (!present.Contains(id))
                {
                    unload.Add(id);
                }
            }

            return (load, unload);
        }

        private void OnLibraryChanged(AudioClipKind kind)
        {
            if (kind == AudioClipKind.SoundEffect)
            {
                Sync();
            }
        }

        // Loading decodes files, so it runs off the main thread, one after another.
        private void Sync()
        {
            lock (_gate)
            {
                if (_stopped)
                {
                    return;
                }

                _syncing = _syncing.ContinueWith(_ => SyncNow(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
        }

        private void SyncNow()
        {
            IAudioPlayerEngine engine = _engine();
            CrudResult<IReadOnlyList<AudioClipRecord>> listed = _library.List(AudioClipKind.SoundEffect);
            if (engine == null || !listed.Ok)
            {
                if (!listed.Ok)
                {
                    _log?.Warning($"Sound effects could not be listed ({listed.Error}): {listed.Detail}");
                }

                return;
            }

            Dictionary<string, string> paths;
            lock (_gate)
            {
                paths = new Dictionary<string, string>();
                foreach (KeyValuePair<string, (int Effect, float GainDb, string Path)> pair in _loaded)
                {
                    paths[pair.Key] = pair.Value.Path;
                }

                // A volume changed is taken at once; it needs no loading.
                foreach (AudioClipRecord record in listed.Value)
                {
                    if (_loaded.TryGetValue(record.Id, out (int Effect, float GainDb, string Path) loaded))
                    {
                        _loaded[record.Id] = (loaded.Effect, record.Settings.GainDb, loaded.Path);
                    }
                }
            }

            (IReadOnlyList<AudioClipRecord> load, IReadOnlyList<string> unload) = Plan(paths, listed.Value);
            foreach (string id in unload)
            {
                int effect;
                lock (_gate)
                {
                    effect = _loaded[id].Effect;
                    _loaded.Remove(id);
                }

                engine.UnloadEffect(effect);
            }

            foreach (AudioClipRecord record in load)
            {
                AudioEffectLoad loaded = engine.LoadEffect(record.File.Path);
                if (loaded.Effect < 0)
                {
                    _log?.Warning($"Sound effect {record.Id} cannot be loaded: {loaded.Problem}");
                    continue;
                }

                lock (_gate)
                {
                    if (_loaded.TryGetValue(record.Id, out (int Effect, float GainDb, string Path) previous))
                    {
                        engine.UnloadEffect(previous.Effect);
                    }

                    _loaded[record.Id] = (loaded.Effect, record.Settings.GainDb, record.File.Path);
                }
            }

            if (load.Count > 0 || unload.Count > 0)
            {
                _log?.Information($"Sound effects: {load.Count} loaded, {unload.Count} unloaded.");
            }
        }
    }
}
