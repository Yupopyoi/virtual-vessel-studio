using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.UI.Theme;

namespace VirtualVessel.UI.Settings
{
    /// <summary>
    /// Holds the UI settings, reports changes right away, and saves them shortly after the last change
    /// (UI foundation detailed design 7.9).
    /// </summary>
    /// <remarks>
    /// Call everything except <see cref="FlushAsync"/> from the main thread. Files are written on a
    /// worker thread, one at a time in order, so a slow disk never stalls a frame; the outcome is
    /// picked up by <see cref="Tick"/> on the main thread.
    /// </remarks>
    public sealed class UiSettingsService
    {
        public static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(0.5);

        private readonly ConfigFileStore<UiSettings> _store;
        private readonly UiSettingsChoices _choices;
        private readonly IMonotonicClock _clock;
        private readonly ILog _log;
        private readonly long _saveDelayTicks;

        // Outcomes of finished saves: null for success, otherwise the failure.
        private readonly ConcurrentQueue<Exception> _outcomes = new ConcurrentQueue<Exception>();

        private UiSettings _stored;
        private SaveDebounce _debounce;
        private Task _saving = Task.CompletedTask;
        private bool _failing;

        /// <param name="log">Optional; records the load result, unknown values, and save failures.</param>
        public UiSettingsService(ConfigFileStore<UiSettings> store, UiSettingsChoices choices, IMonotonicClock clock, ILog log)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _choices = choices ?? throw new ArgumentNullException(nameof(choices));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log;
            _saveDelayTicks = (long)(SaveDelay.TotalSeconds * clock.Frequency);
            _stored = UiSettings.CreateDefault(choices.InitialLanguage);
            Current = UiSettingsResolver.Resolve(_stored, choices).Value;
        }

        /// <summary>Raised on the main thread whenever the settings in use change.</summary>
        public event Action<ResolvedUiSettings> Changed;

        /// <summary>Raised on the main thread when saving starts failing; not again until a save succeeds.</summary>
        public event Action SaveFailed;

        public ResolvedUiSettings Current { get; private set; }

        /// <summary>The page last open on the settings screen, as saved; the screen checks it.</summary>
        public string LastSetupPage => _stored.LastSetupPage;

        /// <summary>True while a change waits to be saved.</summary>
        public bool HasPendingSave => _debounce.Pending;

        public string FilePath => _store.FilePath;

        /// <summary>The last save started; lets tests wait for the worker thread.</summary>
        internal Task PendingSave => _saving;

        /// <summary>Reads the file. Does not raise <see cref="Changed"/>; the caller applies <see cref="Current"/>.</summary>
        public ConfigLoadResult<UiSettings> Load()
        {
            ConfigLoadResult<UiSettings> result = _store.Load();
            _stored = result.Value;

            LogLevel level = result.Status == ConfigLoadStatus.Recovered ? LogLevel.Warning : LogLevel.Information;
            _log?.Write(level, $"UI settings: {result.Status} ({_store.FilePath}).");
            foreach (string warning in result.Warnings)
            {
                _log?.Warning(warning);
            }

            (ResolvedUiSettings resolved, var warnings) = UiSettingsResolver.Resolve(_stored, _choices);
            foreach (string warning in warnings)
            {
                _log?.Warning(warning);
            }

            Current = resolved;
            return result;
        }

        public void SetLanguage(string code)
        {
            Change(settings => settings.Language = code);
        }

        public void SetPalette(string palette)
        {
            Change(settings => settings.Palette = palette);
        }

        public void SetBackground(ThemeBackground background)
        {
            Change(settings => settings.Background = UiSettingsResolver.ToText(background));
        }

        public void SetReducedMotion(ReducedMotionMode mode)
        {
            Change(settings => settings.ReducedMotion = UiSettingsResolver.ToText(mode));
        }

        public void SetFontSet(string fontSet)
        {
            Change(settings => settings.FontSet = fontSet);
        }

        /// <summary>Only the allowed steps (<see cref="UiSettingsResolver.UiScales"/>) are accepted.</summary>
        public void SetUiScale(float scale)
        {
            float step = UiSettingsResolver.FindScale(scale)
                ?? throw new ArgumentOutOfRangeException(nameof(scale), scale, "Not one of the UI scale steps.");

            // Rounded so the file says 1.1, not the float's 1.100000023841858.
            Change(settings => settings.UiScale = Math.Round(step, 2));
        }

        public void SetDeveloperMode(bool enabled)
        {
            Change(settings => settings.DeveloperMode = enabled);
        }

        public void SetLastScreen(string screen)
        {
            Change(settings => settings.LastScreen = screen);
        }

        public void SetLastSetupPage(string page)
        {
            Change(settings => settings.LastSetupPage = page);
        }

        /// <summary>Call once per frame: reports finished saves and starts a save when it is due.</summary>
        public void Tick()
        {
            ReportOutcomes();
            if (_debounce.IsDue(_clock.GetTimestamp()))
            {
                _debounce = SaveDebounce.Idle;
                StartSave();
            }
        }

        /// <summary>
        /// Saves a waiting change now and waits for every save to finish. For shutdown; safe to call
        /// from any thread, and never waits for the main thread.
        /// </summary>
        public async Task FlushAsync()
        {
            if (_debounce.Pending)
            {
                _debounce = SaveDebounce.Idle;
                StartSave();
            }

            // ConfigureAwait(false): the Editor's synchronous shutdown blocks the main thread on this task.
            await _saving.ConfigureAwait(false);
            while (_outcomes.TryDequeue(out Exception failure))
            {
                if (failure != null)
                {
                    _log?.Warning($"Failed to save the UI settings to {_store.FilePath} on exit.", failure);
                }
            }
        }

        private void Change(Action<UiSettings> edit)
        {
            // Edit a copy so that a save already running keeps a consistent snapshot.
            UiSettings next = _stored.Clone();
            edit(next);
            if (UiSettings.SameValues(next, _stored))
            {
                // Choosing the current value again, such as reopening the same screen, writes nothing.
                return;
            }

            _stored = next;
            _debounce = _debounce.Changed(_clock.GetTimestamp(), _saveDelayTicks);

            (ResolvedUiSettings resolved, _) = UiSettingsResolver.Resolve(next, _choices);
            if (resolved.Equals(Current))
            {
                return;
            }

            Current = resolved;
            Changed?.Invoke(resolved);
        }

        private void StartSave()
        {
            UiSettings snapshot = _stored.Clone();
            _saving = _saving.ContinueWith(
                _ => Save(snapshot),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }

        private void Save(UiSettings snapshot)
        {
            try
            {
                _store.Save(snapshot);
                _outcomes.Enqueue(null);
            }
            catch (Exception exception)
            {
                // Reported on the main thread by Tick, or by FlushAsync on exit.
                _outcomes.Enqueue(exception);
            }
        }

        private void ReportOutcomes()
        {
            while (_outcomes.TryDequeue(out Exception failure))
            {
                if (failure == null)
                {
                    if (_failing)
                    {
                        _failing = false;
                        _log?.Information($"UI settings saved again to {_store.FilePath}.");
                    }

                    continue;
                }

                if (_failing)
                {
                    continue;
                }

                _failing = true;
                _log?.Warning($"Failed to save the UI settings to {_store.FilePath}. Further failures are not reported until a save succeeds.", failure);
                SaveFailed?.Invoke();
            }
        }
    }
}
