using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.Tracking;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// The registered avatars and the one on the stream (avatar detailed design 8.1): lists the avatar
    /// folders, loads the chosen avatar at startup without holding startup up, places it, and drives it
    /// from tracking every frame. An Optional application service; without it the stream has no avatar.
    /// </summary>
    /// <remarks>Main thread only; file work runs on other threads.</remarks>
    public sealed class AvatarService : IApplicationService, IAvatarService
    {
        public const string ServiceName = "Avatar";

        private readonly IAvatarLoader _loader;
        private readonly AvatarStore _store;
        private readonly AvatarImporter _importer;
        private readonly ConfigSaveScheduler<AvatarSettings> _saves;
        private readonly Func<AvatarPlacement> _placement;
        private readonly IFrameLoop _frameLoop;
        private readonly IMonotonicClock _clock;
        private readonly ILog _log;
        private readonly ITrackingSource _tracking;
        private readonly ObservableValue<FaceTuning> _faceTuning;
        private readonly ObservableValue<FingerTuning> _fingerTuning;
        private readonly ObservableValue<IReadOnlyList<AvatarEntry>> _avatars = new ObservableValue<IReadOnlyList<AvatarEntry>>(Array.Empty<AvatarEntry>());
        private readonly ObservableValue<string> _current = new ObservableValue<string>(null);
        private readonly ObservableValue<AvatarState> _state = new ObservableValue<AvatarState>(AvatarState.None);
        private readonly ObservableValue<AvatarProblem> _problem = new ObservableValue<AvatarProblem>(AvatarProblem.None);
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly PerfTimer _applyTimer;
        private readonly PerfTimer _loadTimer;
        private readonly PerfTimer _loadFrameTimer;
        private AvatarSettings _settings;
        private ExpressionMapping _mapping;
        private IAvatarModel _model;
        private AvatarController _controller;
        private CancellationTokenSource _loading;
        private string _loadingId;
        private int _generation;
        private long _lastFrame;
        private TimeSpan _longestLoadingFrame;
        private IDisposable _tick;

        /// <param name="placement">Where the avatar goes; asked each time one is placed.</param>
        /// <param name="saves">Where changes of the avatar in use go; null to save nothing, as in tests.</param>
        /// <param name="tracking">What moves the avatar; null leaves it in its rest pose with a neutral face.</param>
        public AvatarService(
            IAvatarLoader loader,
            AvatarStore store,
            AvatarSettings settings,
            ConfigSaveScheduler<AvatarSettings> saves,
            Func<AvatarPlacement> placement,
            IFrameLoop frameLoop,
            IMonotonicClock clock,
            ISystemClock systemClock,
            IPerformanceMetrics metrics,
            ILog log,
            ITrackingSource tracking = null)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _settings = settings?.Clone() ?? new AvatarSettings();
            _saves = saves;
            _placement = placement ?? throw new ArgumentNullException(nameof(placement));
            _frameLoop = frameLoop ?? throw new ArgumentNullException(nameof(frameLoop));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log;
            _tracking = tracking;
            FaceTuning tuning = Normalized(_settings.Face);
            _faceTuning = new ObservableValue<FaceTuning>(tuning);
            _mapping = ExpressionMapping.For(tuning);
            _fingerTuning = new ObservableValue<FingerTuning>(VirtualVessel.Avatar.FingerTuning.Normalized(_settings.Fingers));
            _importer = new AvatarImporter(store, loader, systemClock, log);
            _applyTimer = metrics?.Timer("Avatar", "Apply");
            _loadTimer = metrics?.Timer("Avatar", "Load");
            _loadFrameTimer = metrics?.Timer("Avatar", "LoadLongestFrame");
        }

        public string Name => ServiceName;

        public IReadOnlyObservable<IReadOnlyList<AvatarEntry>> Avatars => _avatars;

        public IReadOnlyObservable<string> CurrentAvatarId => _current;

        public IReadOnlyObservable<AvatarState> State => _state;

        public IReadOnlyObservable<AvatarProblem> Problem => _problem;

        public IReadOnlyList<string> Extensions => _loader.Extensions;

        public IReadOnlyObservable<FaceTuning> FaceTuning => _faceTuning;

        public IReadOnlyObservable<FingerTuning> FingerTuning => _fingerTuning;

        public FaceTarget Face => _controller?.Face;

        public BodyTarget Body => _controller?.Body;

        /// <summary>The avatar on the stream, or null; for tests.</summary>
        internal IAvatarModel Model => _model;

        /// <summary>What drives the avatar on the stream, or null; for tests.</summary>
        internal AvatarController Controller => _controller;

        /// <summary>Creates the store of avatar-settings.json in <paramref name="directory"/>, the project's folder (project data detailed design 6).</summary>
        public static ConfigFileStore<AvatarSettings> CreateStore(string directory, ISystemClock clock)
        {
            return new ConfigFileStore<AvatarSettings>(directory, AvatarSettings.FileName, AvatarSettings.SchemaVersion, () => new AvatarSettings(), clock);
        }

        /// <summary>
        /// <paramref name="tuning"/> with every part within range and its wink choice, or the defaults
        /// for none (a file written before tuning existed). Pure.
        /// </summary>
        public static FaceTuning Normalized(FaceTuning tuning)
        {
            var normalized = new FaceTuning { Winks = tuning?.Winks ?? false };
            for (int i = 0; tuning != null && i < VirtualVessel.Avatar.FaceTuning.PartCount; i++)
            {
                normalized = normalized.With((FaceTuningPart)i, tuning.Get((FaceTuningPart)i));
            }

            return normalized;
        }

        /// <summary>A copy of <paramref name="entries"/> with <paramref name="added"/> at the end. Pure.</summary>
        public static IReadOnlyList<AvatarEntry> With(IReadOnlyList<AvatarEntry> entries, AvatarEntry added)
        {
            var next = new List<AvatarEntry>(entries) { added };
            return next;
        }

        /// <summary>A copy of <paramref name="entries"/> without the avatar <paramref name="id"/>. Pure.</summary>
        public static IReadOnlyList<AvatarEntry> Without(IReadOnlyList<AvatarEntry> entries, string id)
        {
            var next = new List<AvatarEntry>(entries.Count);
            foreach (AvatarEntry entry in entries)
            {
                if (entry.Id != id)
                {
                    next.Add(entry);
                }
            }

            return next;
        }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            // Listing reads a few small files per avatar, so it stays on the main thread at startup.
            var warnings = new List<string>();
            try
            {
                _store.CleanImports();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                _log?.Warning("Could not delete what an interrupted avatar registration left behind.", exception);
            }

            _avatars.Value = _store.List(warnings);
            foreach (string warning in warnings)
            {
                _log?.Warning(warning);
            }

            _tick = _frameLoop.Register(Tick);
            _log?.Information($"Avatars started with {_avatars.Value.Count} registered.");

            string chosen = _settings.CurrentAvatarId;
            if (chosen != null)
            {
                if (AvatarEntries.Find(_avatars.Value, chosen) == null)
                {
                    // Removed outside the application; nothing to load.
                    _log?.Warning($"The avatar in use, {chosen}, is no longer registered.");
                    SetCurrent(null);
                }
                else
                {
                    // Shown as the avatar in use while it loads. Loading spreads over frames; startup
                    // goes on while it runs.
                    _current.Value = chosen;
                    _ = SelectAsync(chosen, _lifetime.Token);
                }
            }

            return Task.CompletedTask;
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken)
        {
            _lifetime.Cancel();
            Unload();
            if (_saves != null)
            {
                await _saves.FlushAsync().ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            _tick?.Dispose();
            _tick = null;
            if (!_lifetime.IsCancellationRequested)
            {
                _lifetime.Cancel();
            }

            Unload();
        }

        public async Task<AvatarImportResult> ImportAsync(string path, Func<AvatarModelInfo, Task<bool>> confirm, CancellationToken cancellationToken)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken))
            {
                AvatarImportResult result = await _importer.ImportAsync(path, _avatars.Value, confirm, linked.Token);
                if (result.Imported)
                {
                    _avatars.Value = With(_avatars.Value, result.Entry);
                    if (_current.Value == null)
                    {
                        await SelectAsync(result.Entry.Id, linked.Token);
                    }
                }

                return result;
            }
        }

        public async Task SelectAsync(string avatarId, CancellationToken cancellationToken)
        {
            if (avatarId == null)
            {
                CancelLoad();
                Unload();
                SetCurrent(null);
                _state.Value = AvatarState.None;
                _problem.Value = AvatarProblem.None;
                return;
            }

            if (avatarId == _current.Value && _model != null)
            {
                return;
            }

            AvatarEntry entry = AvatarEntries.Find(_avatars.Value, avatarId);
            if (entry == null)
            {
                throw new ArgumentException($"Avatar {avatarId} is not registered.", nameof(avatarId));
            }

            CancelLoad();
            int generation = ++_generation;
            var load = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
            _loading = load;
            _loadingId = avatarId;
            _state.Value = AvatarState.Loading;
            _longestLoadingFrame = TimeSpan.Zero;
            long started = _clock.GetTimestamp();
            IAvatarModel model = null;
            try
            {
                if (!entry.IsUsable)
                {
                    throw new AvatarLoadException(AvatarProblem.LoadFailed, "Its avatar-profile.json or model could not be read.");
                }

                model = await _loader.LoadAsync(_store.ModelPathOf(entry), load.Token);
                IReadOnlyList<AvatarBone> missing = AvatarBones.MissingRequired(model.Rig.Has);
                if (missing.Count > 0)
                {
                    throw new AvatarLoadException(AvatarProblem.MissingBones, $"The model lacks the required bones {string.Join(", ", missing)}.");
                }

                if (generation != _generation || _lifetime.IsCancellationRequested)
                {
                    return;
                }

                Place(model, _placement());
                IAvatarModel old = _model;
                _model = model;
                _controller = new AvatarController(model, _mapping) { FingerTuning = _fingerTuning.Value };
                model = null;
                old?.Dispose();
                SetCurrent(avatarId);
                _state.Value = AvatarState.Ready;
                _problem.Value = AvatarProblem.None;

                TimeSpan took = _clock.GetElapsed(started, _clock.GetTimestamp());
                _loadTimer?.Record(took);
                _loadFrameTimer?.Record(_longestLoadingFrame);
                _log?.Information($"Avatar {avatarId} is on the stream: {entry.Profile.Format} {entry.Profile.FormatVersion}{(entry.Profile.Converted ? " (converted)" : string.Empty)}, loaded in {took.TotalSeconds:F2} s, longest frame {_longestLoadingFrame.TotalMilliseconds:F0} ms.");
                LogMissingParts(avatarId, _model);
                _log?.Information($"Avatar {avatarId} moves its face {(_controller.Expressions.Profile != null ? $"part by part ({_controller.Expressions.Profile.Name} shapes)" : "by whole-face expressions")}.");
            }
            catch (OperationCanceledException) when (load.IsCancellationRequested)
            {
                // A newer choice, removal, or shutdown took over; it sets the state.
            }
            catch (Exception exception)
            {
                AvatarProblem problem = AvatarImporter.ProblemOf(exception);
                _log?.Warning($"Avatar {avatarId} could not be loaded: {problem}.", exception);
                if (generation == _generation)
                {
                    // A failed swap keeps the avatar on the stream (avatar detailed design 12).
                    _problem.Value = problem;
                    if (_model == null)
                    {
                        SetCurrent(avatarId);
                        _state.Value = AvatarState.Error;
                    }
                    else
                    {
                        _state.Value = AvatarState.Ready;
                    }
                }
            }
            finally
            {
                model?.Dispose();
                if (_loading == load)
                {
                    _loading = null;
                    _loadingId = null;
                }

                load.Dispose();
            }
        }

        public void SetFaceTuning(FaceTuning tuning)
        {
            FaceTuning next = Normalized(tuning);
            if (next.Equals(_faceTuning.Value))
            {
                return;
            }

            _faceTuning.Value = next;
            _mapping = ExpressionMapping.For(next);
            if (_controller != null)
            {
                _controller.Mapping = _mapping;
            }

            _settings = _settings.Clone();
            _settings.Face = next.Clone();
            _saves?.Changed(_settings.Clone());
        }

        public void SetFingerTuning(FingerTuning tuning)
        {
            FingerTuning next = VirtualVessel.Avatar.FingerTuning.Normalized(tuning);
            if (next.Equals(_fingerTuning.Value))
            {
                return;
            }

            _fingerTuning.Value = next;
            if (_controller != null)
            {
                _controller.FingerTuning = next;
            }

            _settings = _settings.Clone();
            _settings.Fingers = next;
            _saves?.Changed(_settings.Clone());
        }

        public async Task RemoveAsync(string avatarId, CancellationToken cancellationToken)
        {
            if (AvatarEntries.Find(_avatars.Value, avatarId) == null)
            {
                return;
            }

            if (avatarId == _loadingId)
            {
                CancelLoad();
            }

            if (avatarId == _current.Value)
            {
                Unload();
                SetCurrent(null);
                _state.Value = AvatarState.None;
                _problem.Value = AvatarProblem.None;
            }

            try
            {
                await Task.Run(() => _store.Delete(avatarId), cancellationToken);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                _log?.Warning($"Could not delete avatar {avatarId}.", exception);
                _problem.Value = AvatarProblem.StorageFailed;
                return;
            }

            _avatars.Value = Without(_avatars.Value, avatarId);
            _log?.Information($"Removed avatar {avatarId}.");
        }

        private static void Place(IAvatarModel model, AvatarPlacement placement)
        {
            Transform root = model.Root;
            foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
            {
                part.gameObject.layer = placement.Layer;
            }

            root.SetPositionAndRotation(placement.Spawn.position, placement.Spawn.rotation);
            AvatarRestPose.Apply(model.Rig);
            model.Apply();
            model.SetVisible(true);
        }

        private void LogMissingParts(string avatarId, IAvatarModel model)
        {
            var missing = new List<string>();
            foreach (AvatarExpression expression in (AvatarExpression[])Enum.GetValues(typeof(AvatarExpression)))
            {
                if (!model.Expressions.Has(expression))
                {
                    missing.Add(expression.ToString());
                }
            }

            for (int i = 0; i < AvatarBones.Count; i++)
            {
                if (!model.Rig.Has((AvatarBone)i) && (AvatarBone)i >= AvatarBone.LeftThumbProximal && (AvatarBone)i <= AvatarBone.RightLittleDistal)
                {
                    missing.Add(((AvatarBone)i).ToString());
                }
            }

            if (missing.Count > 0)
            {
                _log?.Warning($"Avatar {avatarId} lacks {string.Join(", ", missing)}; those parts do not move.");
            }
        }

        private void Tick()
        {
            long now = _clock.GetTimestamp();
            if (_loading != null && _lastFrame != 0)
            {
                TimeSpan frame = _clock.GetElapsed(_lastFrame, now);
                if (frame > _longestLoadingFrame)
                {
                    _longestLoadingFrame = frame;
                }
            }

            _lastFrame = now;
            if (_model != null)
            {
                // Tracking registers with the frame loop before Avatar, so its frame is this frame's.
                long begin = _applyTimer?.Begin() ?? 0;
                TrackingOptions options = _tracking?.Options.Value ?? TrackingOptions.Default;
                _controller.Update(_tracking?.Current, options.Mirror, now / (double)_clock.Frequency, options.BodyDepth == BodyDepth.Flat);
                _model.Apply();
                _applyTimer?.End(begin);
            }

            _saves?.Tick();
        }

        private void CancelLoad()
        {
            _loading?.Cancel();
            _loading = null;
            _loadingId = null;
        }

        private void Unload()
        {
            IAvatarModel model = _model;
            _model = null;
            _controller = null;
            model?.Dispose();
        }

        private void SetCurrent(string avatarId)
        {
            if (!_current.Set(avatarId) && _settings.CurrentAvatarId == avatarId)
            {
                return;
            }

            _settings = _settings.Clone();
            _settings.CurrentAvatarId = avatarId;
            _saves?.Changed(_settings.Clone());
        }
    }
}
