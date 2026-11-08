using System;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.VoiceLab;

namespace VirtualVessel.UI.Screens
{
    [StringKeys]
    public static class BuildGuardStrings
    {
        public const string Title = "live.voice-lab-busy.title";
        public const string StreamBody = "live.voice-lab-busy.stream-body";
        public const string StreamConfirm = "live.voice-lab-busy.stream-confirm";
        public const string RecordBody = "live.voice-lab-busy.record-body";
        public const string RecordConfirm = "live.voice-lab-busy.record-confirm";
    }

    /// <summary>
    /// Starting a stream or a recording while Voice Lab makes a voice: the build's GPU load would spoil them, so the
    /// user chooses to stop the build and start, or not to start (user's decision, 2026-11-08; system design 14.26).
    /// Every way of starting goes through this command, so a shortcut added later asks the same.
    /// </summary>
    public sealed class BuildGuardedStart : IConfirmedCommand
    {
        private readonly IUiCommand _start;
        private readonly ConfirmationRequest _usual;
        private readonly ConfirmationRequest _whileBuilding;
        private readonly IVoiceLab _lab;

        /// <param name="usual">The confirmation when no voice is being made; null starts without asking.</param>
        /// <param name="lab">Voice Lab; null when it is not there, and then nothing is ever being made.</param>
        public BuildGuardedStart(IUiCommand start, ConfirmationRequest usual, ConfirmationRequest whileBuilding, IVoiceLab lab)
        {
            _start = start ?? throw new ArgumentNullException(nameof(start));
            _usual = usual;
            _whileBuilding = whileBuilding ?? throw new ArgumentNullException(nameof(whileBuilding));
            _lab = lab;
        }

        public ConfirmationRequest Confirmation => ConfirmationFor(IsBuilding, _usual, _whileBuilding);

        public bool CanExecute => _start.CanExecute;

        public string DisabledReasonKey => _start.DisabledReasonKey;

        private bool IsBuilding => _lab != null && Building(_lab.BuildProgress.State);

        public event Action CanExecuteChanged
        {
            add => _start.CanExecuteChanged += value;
            remove => _start.CanExecuteChanged -= value;
        }

        /// <summary>The confirmation for starting now: about the build while one runs. Pure.</summary>
        public static ConfirmationRequest ConfirmationFor(bool building, ConfirmationRequest usual, ConfirmationRequest whileBuilding)
        {
            return building ? whileBuilding : usual;
        }

        /// <summary>Whether a voice is being made. Pure.</summary>
        public static bool Building(VoiceLabBuildState state)
        {
            return state != VoiceLabBuildState.None;
        }

        /// <summary>The confirmation of starting a stream while a voice is being made.</summary>
        public static ConfirmationRequest WhileBuildingStream()
        {
            return new ConfirmationRequest(BuildGuardStrings.Title, BuildGuardStrings.StreamBody, BuildGuardStrings.StreamConfirm);
        }

        /// <summary>The confirmation of starting a recording while a voice is being made.</summary>
        public static ConfirmationRequest WhileBuildingRecording()
        {
            return new ConfirmationRequest(BuildGuardStrings.Title, BuildGuardStrings.RecordBody, BuildGuardStrings.RecordConfirm);
        }

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            // Confirmed while building, so the build stops first; cancelling ends its run as cancelled.
            if (IsBuilding)
            {
                _lab.CancelBuild();
            }

            return _start.ExecuteAsync(cancellationToken);
        }
    }
}
