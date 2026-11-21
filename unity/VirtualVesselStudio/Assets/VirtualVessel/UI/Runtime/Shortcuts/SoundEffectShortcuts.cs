using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Audio.Clips;
using VirtualVessel.Audio.Playback;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Commands;

namespace VirtualVessel.UI.Shortcuts
{
    /// <summary>
    /// The sound effects' shortcuts (BGM and SE detailed design 8.3): listens to the keys the registered sound effects
    /// have, following the library, and plays the one whose keys are pressed. Lives as long as the app, so the keys
    /// work on every screen and with the app behind a game.
    /// </summary>
    public sealed class SoundEffectShortcuts : IApplicationService
    {
        public const string ServiceName = "SoundEffectShortcuts";

        private readonly IAudioClipLibrary _library;
        private readonly ISoundEffectPlayer _player;
        private readonly IGlobalShortcuts _shortcuts;
        private readonly ILog _log;
        private readonly object _gate = new object();

        // Swapped whole, read on the listening thread.
        private volatile IReadOnlyDictionary<KeyChord, PlaySoundEffectCommand> _bindings = new Dictionary<KeyChord, PlaySoundEffectCommand>();
        private bool _started;

        public SoundEffectShortcuts(IAudioClipLibrary library, ISoundEffectPlayer player, IGlobalShortcuts shortcuts, ILog log)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
            _log = log;
        }

        public string Name => ServiceName;

        /// <summary>The keys, for the Cards to pause while one is assigned and to show those another app holds.</summary>
        public IGlobalShortcuts Keys => _shortcuts;

        /// <summary>
        /// The sound effect each chord plays: the first in the list's order when two have the same keys, and none for
        /// text that is not a chord. Pure.
        /// </summary>
        public static IReadOnlyDictionary<KeyChord, string> Bindings(IEnumerable<AudioClipRecord> effects)
        {
            var bindings = new Dictionary<KeyChord, string>();
            foreach (AudioClipRecord effect in effects ?? Enumerable.Empty<AudioClipRecord>())
            {
                KeyChord? chord = KeyChord.Parse(effect.Settings.Shortcut);
                if (chord.HasValue && !bindings.ContainsKey(chord.Value))
                {
                    bindings[chord.Value] = effect.Id;
                }
            }

            return bindings;
        }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            _shortcuts.Pressed += OnPressed;
            _library.Changed += OnChanged;
            _started = true;
            Follow();
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            Stop();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Stop();
            _shortcuts.Dispose();
        }

        private void Stop()
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            _library.Changed -= OnChanged;
            _shortcuts.Pressed -= OnPressed;
            _shortcuts.Listen(Array.Empty<KeyChord>());
        }

        // Raised on any thread; listing reads a few small files, which is cheap next to how seldom it changes.
        private void OnChanged(AudioClipKind kind)
        {
            if (kind == AudioClipKind.SoundEffect)
            {
                Follow();
            }
        }

        // One at a time, so a change on another thread cannot leave an older list listened to.
        private void Follow()
        {
            lock (_gate)
            {
                CrudResult<IReadOnlyList<AudioClipRecord>> effects = _library.List(AudioClipKind.SoundEffect);
                if (!effects.Ok)
                {
                    _log?.Warning($"The sound effects' shortcuts could not follow the library: {effects.Error} {effects.Detail}");
                    return;
                }

                IReadOnlyDictionary<KeyChord, string> bindings = Bindings(effects.Value);
                _bindings = bindings.ToDictionary(pair => pair.Key, pair => new PlaySoundEffectCommand(_player, pair.Value));
                _shortcuts.Listen(bindings.Keys.ToArray());
            }
        }

        // On the listening thread: the player takes a press from any thread, and plays it without waiting for a frame.
        private void OnPressed(KeyChord chord)
        {
            if (_bindings.TryGetValue(chord, out PlaySoundEffectCommand command))
            {
                command.Execute();
            }
        }
    }
}
