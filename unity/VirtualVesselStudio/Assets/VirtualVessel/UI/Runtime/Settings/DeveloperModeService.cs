using System;
using System.Collections.Generic;
using System.Linq;
using VirtualVessel.Diagnostics.Logging;

namespace VirtualVessel.UI.Settings
{
    /// <summary>
    /// Tells the functions that depend on Developer Mode, such as detailed performance metrics, when it
    /// changes (UI foundation detailed design 7.7). Developer Mode only shows more; it grants no rights.
    /// </summary>
    /// <remarks>
    /// The receivers come from the composition, so the UI never depends on the classes behind them.
    /// Call from the main thread; the settings service raises its changes there.
    /// </remarks>
    public sealed class DeveloperModeService : IDisposable
    {
        private readonly UiSettingsService _settings;
        private readonly IReadOnlyList<Action<bool>> _receivers;
        private readonly ILog _log;
        private bool _started;

        /// <param name="receivers">Called with the new state; may be empty.</param>
        /// <param name="log">Optional; records changes and failing receivers.</param>
        public DeveloperModeService(UiSettingsService settings, IEnumerable<Action<bool>> receivers, ILog log)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _receivers = (receivers ?? Enumerable.Empty<Action<bool>>()).Where(receiver => receiver != null).ToList();
            _log = log;
            IsEnabled = settings.Current.DeveloperMode;
        }

        /// <summary>Raised after the receivers were told.</summary>
        public event Action<bool> Changed;

        public bool IsEnabled { get; private set; }

        /// <summary>Tells the receivers the current state once, then follows the settings.</summary>
        public void Start()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            IsEnabled = _settings.Current.DeveloperMode;
            Tell(IsEnabled);
            _settings.Changed += OnSettingsChanged;
        }

        public void Dispose()
        {
            if (_started)
            {
                _settings.Changed -= OnSettingsChanged;
                _started = false;
            }
        }

        private void OnSettingsChanged(ResolvedUiSettings settings)
        {
            if (settings.DeveloperMode == IsEnabled)
            {
                return;
            }

            IsEnabled = settings.DeveloperMode;
            _log?.Information(IsEnabled ? "Developer Mode turned on." : "Developer Mode turned off.");
            Tell(IsEnabled);
            Changed?.Invoke(IsEnabled);
        }

        private void Tell(bool enabled)
        {
            foreach (Action<bool> receiver in _receivers)
            {
                try
                {
                    receiver(enabled);
                }
                catch (Exception exception)
                {
                    // One failing function must not keep the others, or the UI, from following.
                    _log?.Error("A Developer Mode receiver failed.", exception);
                }
            }
        }
    }
}
