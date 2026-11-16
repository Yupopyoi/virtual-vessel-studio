using System;
using UnityEngine.UIElements;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// Keeps the unfinished STEP 1 and STEP 2 a moment after they change (Voice Lab detailed design 6.6). The wait gathers
    /// a slider dragged across several values into one write.
    /// </summary>
    internal sealed class VoiceLabWorkKeeper
    {
        public const long WaitMilliseconds = 500;

        private readonly IVoiceLab _lab;
        private readonly VisualElement _owner;
        private readonly Func<VoiceLabWork> _capture;
        private bool _pending;

        /// <param name="capture">The work as it is now; null when there is nothing to keep, such as before any candidate.</param>
        public VoiceLabWorkKeeper(IVoiceLab lab, VisualElement owner, Func<VoiceLabWork> capture)
        {
            _lab = lab ?? throw new ArgumentNullException(nameof(lab));
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        }

        /// <summary>While work is being put back, its changes are not news to keep.</summary>
        public bool Paused { get; set; }

        public void Changed()
        {
            if (Paused || _pending)
            {
                return;
            }

            _pending = true;
            _owner.schedule.Execute(Keep).StartingIn(WaitMilliseconds);
        }

        /// <summary>Keeps the work now, as the timer would.</summary>
        public void Keep()
        {
            _pending = false;
            VoiceLabWork work = _capture();
            if (work == null)
            {
                return;
            }

            // The caller of the CRUD (system design 6.31): a failure only means the work may not come back, so the
            // log keeps it and the user is not stopped.
            CrudResult kept = _lab.SaveWork(work);
            if (!kept.Ok)
            {
                VoiceLabScreen.LogFailure(_owner, "Keeping the unfinished work", kept.Error, kept.Detail);
            }
        }
    }
}
