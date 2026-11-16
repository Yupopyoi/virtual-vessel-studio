using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// Continuing the unfinished STEP 1 and STEP 2 after the app was closed, and a voice made but not registered
    /// (Voice Lab detailed design 6.6; screen spec 2.2).
    /// </summary>
    public sealed partial class VoiceLabScreen
    {
        private VoiceLabWorkKeeper _keeper;
        private VoiceLabWork _keptWork;
        private IReadOnlyList<VoiceLabRunRecord> _unregistered = new VoiceLabRunRecord[0];

        /// <summary>The offers to continue at the top of Make a voice; null until Voice Lab is ready.</summary>
        public VoiceLabResumeNotice Resume { get; private set; }

        public void WorkChanged()
        {
            _keeper?.Changed();
            ShowResume(reload: false);
        }

        /// <summary>Keeps the unfinished work now rather than after the wait, as when the screen is left.</summary>
        public void KeepWorkNow()
        {
            _keeper?.Keep();
        }

        private void BuildResume()
        {
            _keeper = new VoiceLabWorkKeeper(_lab, this, CaptureWork);
            Resume = new VoiceLabResumeNotice(ContinueWork, DiscardWork, runId => TryVoice(runId, null));
            _steps.Insert(0, Resume);
            ShowResume(reload: true);
        }

        // The work as it stands; nothing to keep before a candidate, or once building has begun, as the run is kept from
        // then on. STEP 3 before building still holds STEP 2's work.
        private VoiceLabWork CaptureWork()
        {
            if (_base == null)
            {
                return null;
            }

            int step = VoiceLabResumeNotice.StepToKeep(Current, _draft.RunId != null, _lab.BuildProgress.State != VoiceLabBuildState.None);
            if (step < 0)
            {
                return null;
            }

            IReadOnlyList<VoiceLabWorkCandidate> candidates = _base.Candidates;
            if (candidates.Count == 0)
            {
                return null;
            }

            int picked = _draft.Candidate != null ? _draft.CandidateNumber : 0;
            return new VoiceLabWork(step, _base.CurrentRequest(), candidates, picked, _timbre.Timbre, _timbre.HeldList.Held);
        }

        private void ShowResume(bool reload)
        {
            if (Resume == null)
            {
                return;
            }

            bool madeThisSession = _base.Candidates.Count > 0;
            if (reload)
            {
                _keptWork = madeThisSession ? null : LoadKeptWork();
                _unregistered = Unregistered();
            }

            bool building = _lab.BuildProgress.State != VoiceLabBuildState.None;
            (bool work, bool voice) = VoiceLabResumeNotice.WhatToShow(madeThisSession, _keptWork != null, _unregistered.Count, Current, building);
            Resume.Show(work ? _keptWork : null, voice ? _unregistered[0] : null, _unregistered.Count);
        }

        private VoiceLabWork LoadKeptWork()
        {
            CrudResult<VoiceLabWork> read = _lab.LoadWork();
            if (!read.Ok && read.Error != CrudError.NotFound)
            {
                // Damaged or unreadable: nothing is offered, and the log keeps why (system design 6.31).
                LogFailure(this, "Reading the unfinished work", read.Error, read.Detail);
            }

            return read.Ok ? read.Value : null;
        }

        // Completed runs no voice was registered from, newest first.
        private IReadOnlyList<VoiceLabRunRecord> Unregistered()
        {
            CrudResult<IReadOnlyList<VoiceLabRunRecord>> runs = _services.VoiceLabLibrary?.ListRuns() ?? CrudResult<IReadOnlyList<VoiceLabRunRecord>>.Success(new VoiceLabRunRecord[0]);
            if (!runs.Ok)
            {
                LogFailure(this, "Listing the made voices", runs.Error, runs.Detail);
                return new VoiceLabRunRecord[0];
            }

            return VoiceLabResumeNotice.UnregisteredOf(runs.Value);
        }

        private void ContinueWork()
        {
            VoiceLabWork work = LoadKeptWork();
            if (work == null)
            {
                ShowResume(reload: true);
                return;
            }

            PutBack(work);
        }

        /// <summary>Puts the unfinished work back: the request, the candidates, the one picked, and STEP 2's timbres.</summary>
        public void PutBack(VoiceLabWork work)
        {
            _keeper.Paused = true;
            try
            {
                _base.PutBack(work.Request, work.Request.FirstSeed, baseGone: false);
                _draft.ForgetRun();
                VoiceLabWorkCandidate picked = work.PickedCandidate;
                _draft.Candidate = picked?.Candidate;
                _draft.CandidateNumber = picked?.Number ?? 0;
                _base.RestoreCandidates(work.Candidates);
                ShowTab(MakeTab);
                if (picked == null)
                {
                    GoTo(BaseStep, reset: true);
                }
                else
                {
                    // STEP 2 first, as opening it lets go of timbres held for another base.
                    GoTo(TimbreStep, reset: true);
                    _timbre.Restore(work.Timbre);
                    foreach (VoiceLabTimbre held in work.Held)
                    {
                        _timbre.HeldList.Hold(held);
                    }

                    _timbre.Refresh();
                    if (work.Step == BaseStep)
                    {
                        GoTo(BaseStep);
                    }
                }
            }
            finally
            {
                _keeper.Paused = false;
            }

            _keptWork = null;
            ShowResume(reload: false);
        }

        private async void DiscardWork()
        {
            IDialogService dialogs = UiServices.Find(this)?.Dialogs;
            if (dialogs != null && !await dialogs.ConfirmAsync(VoiceLabStrings.ResumeDiscardTitle, VoiceLabStrings.ResumeDiscardBody, VoiceLabStrings.ResumeDiscardConfirm, ConfirmStyle.Danger))
            {
                return;
            }

            CrudResult deleted = _lab.DeleteWork();
            if (!deleted.Ok)
            {
                LogFailure(this, "Discarding the unfinished work", deleted.Error, deleted.Detail);
            }

            ShowResume(reload: true);
        }
    }
}
