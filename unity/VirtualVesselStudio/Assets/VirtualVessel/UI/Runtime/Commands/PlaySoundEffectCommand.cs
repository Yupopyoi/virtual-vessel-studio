using System;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Audio.Playback;

namespace VirtualVessel.UI.Commands
{
    /// <summary>
    /// Plays one sound effect (BGM and SE detailed design 8.2; system design 14.28). The Cards' buttons, the live
    /// screen's buttons, and the shortcuts all play through it. Safe on any thread, as the player is.
    /// </summary>
    public sealed class PlaySoundEffectCommand : IUiCommand
    {
        public const string LoadingKey = "setup.se.loading";

        private readonly ISoundEffectPlayer _player;

        public PlaySoundEffectCommand(ISoundEffectPlayer player, string id)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            Id = id ?? throw new ArgumentNullException(nameof(id));
        }

        // Loading ends off the main thread without telling; the buttons read CanExecute again on their own.
        public event Action CanExecuteChanged
        {
            add
            {
            }

            remove
            {
            }
        }

        public string Id { get; }

        public bool CanExecute => _player.IsReady(Id);

        public string DisabledReasonKey => CanExecute ? null : LoadingKey;

        /// <summary>Plays it at once; one not loaded yet does not play, and the player logs it.</summary>
        public void Execute()
        {
            _player.Play(Id);
        }

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            Execute();
            return Task.CompletedTask;
        }
    }
}
