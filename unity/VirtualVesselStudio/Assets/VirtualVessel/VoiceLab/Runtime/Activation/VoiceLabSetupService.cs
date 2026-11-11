using System;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.VoiceLab;
using VirtualVessel.VoiceLab.Scripts;

namespace VirtualVessel.VoiceLab.Activation
{
    /// <summary>
    /// <see cref="IVoiceLabSetup"/> for the screen (Voice Lab detailed design 8.1, 8.2): activation, the check of the PC,
    /// and the terms, with Voice Lab set up again from the new environment once a build switched to it. A deleted
    /// environment is forgotten, so the screen offers to build it again.
    /// </summary>
    public sealed class VoiceLabSetupService : IVoiceLabSetup
    {
        private static readonly TimeSpan s_idlePoll = TimeSpan.FromSeconds(1);

        private readonly VoiceLabActivation _activation;
        private readonly VoiceLabService _voiceLab;
        private readonly VoiceLabPcProbe _probe;
        private readonly Func<VoiceLabEnvironment, CancellationToken, Task<string>> _verify;
        private readonly string _scripts;
        private readonly ILog _log;
        private readonly Shared<VoiceLabSetupProgress> _progress = new Shared<VoiceLabSetupProgress>();
        private volatile bool _running;

        /// <param name="verify">The trial run at the end of a build; null to skip it.</param>
        public VoiceLabSetupService(VoiceLabActivation activation, VoiceLabService voiceLab, VoiceLabPcProbe probe, Func<VoiceLabEnvironment, CancellationToken, Task<string>> verify, string scripts, ILog log)
        {
            _activation = activation ?? throw new ArgumentNullException(nameof(activation));
            _voiceLab = voiceLab ?? throw new ArgumentNullException(nameof(voiceLab));
            _probe = probe;
            _verify = verify;
            _scripts = scripts;
            _log = log;
            _voiceLab.EnvironmentRemoved += _activation.ForgetEnvironment;
        }

        public VoiceLabSetupState State => StateOf(_running, _activation.IsReady, _activation.CanResume, _activation.Record.State, LastFailure);

        public VoiceLabSetupFailure LastFailure { get; private set; }

        public bool UpdateAvailable => _activation.UpdateAvailable;

        public long DownloadBytes => _activation.DownloadSize;

        public VoiceLabSetupProgress Progress => _progress.Value;

        public string TermsVersion => _activation.TermsVersion;

        public bool TermsAgreed => _activation.TermsAgreed;

        public string Location => _activation.Location;

        /// <summary>
        /// The screen's state from activation's: a build running wins, then one in use; a stopped one is Paused unless it
        /// failed, now or before a restart. An update that stopped or failed leaves the environment in use, so Ready. Pure.
        /// </summary>
        public static VoiceLabSetupState StateOf(bool running, bool ready, bool canResume, VoiceActivationState recorded, VoiceLabSetupFailure failure)
        {
            if (running)
            {
                return VoiceLabSetupState.Preparing;
            }

            if (ready)
            {
                return VoiceLabSetupState.Ready;
            }

            if (failure != VoiceLabSetupFailure.None || recorded == VoiceActivationState.Failed)
            {
                return VoiceLabSetupState.Failed;
            }

            return canResume ? VoiceLabSetupState.Paused : VoiceLabSetupState.NotStarted;
        }

        /// <summary>The screen's step for a phase of the build. Pure.</summary>
        public static VoiceLabSetupStep StepOf(VoiceActivationPhase phase)
        {
            switch (phase)
            {
                case VoiceActivationPhase.Downloading:
                    return VoiceLabSetupStep.Download;
                case VoiceActivationPhase.Placing:
                    return VoiceLabSetupStep.Unpack;
                case VoiceActivationPhase.InstallingPython:
                case VoiceActivationPhase.InstallingPackages:
                    return VoiceLabSetupStep.Install;
                default:
                    return VoiceLabSetupStep.Check;
            }
        }

        /// <summary>The screen's failure for the build's. Pure.</summary>
        public static VoiceLabSetupFailure FailureOf(VoiceActivationFailure failure)
        {
            switch (failure)
            {
                case VoiceActivationFailure.Network:
                    return VoiceLabSetupFailure.Network;
                case VoiceActivationFailure.Corrupt:
                    return VoiceLabSetupFailure.Corrupt;
                case VoiceActivationFailure.DiskFull:
                    return VoiceLabSetupFailure.DiskFull;
                case VoiceActivationFailure.Verify:
                    return VoiceLabSetupFailure.Check;
                default:
                    return VoiceLabSetupFailure.Setup;
            }
        }

        public string TermsText(string language)
        {
            return _scripts == null ? null : VoiceTerms.Load(_scripts, language)?.Text;
        }

        public void AgreeToTerms()
        {
            _activation.AgreeToTerms();
        }

        public CrudResult SetLocation(string folder)
        {
            return _running ? CrudResult.Fail(CrudError.Busy, "A build is running.") : _activation.SetLocation(folder);
        }

        public Task<VoiceLabPcCheck> CheckPcAsync(CancellationToken cancellationToken)
        {
            return _probe == null
                ? Task.FromResult(new VoiceLabPcCheck(null, false, null, false, Location, 0L, false, false))
                : _probe.CheckAsync(Location, cancellationToken);
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            if (_running)
            {
                return;
            }

            _running = true;
            LastFailure = VoiceLabSetupFailure.None;
            _progress.Value = new VoiceLabSetupProgress(VoiceLabSetupStep.Download, 0L, DownloadBytes);
            var progress = new Forward<VoiceActivationProgress>(p => _progress.Value = new VoiceLabSetupProgress(StepOf(p.Phase), p.Done, p.Total));
            try
            {
                await _activation.RunAsync(_verify, progress, cancellationToken, WaitUntilIdleAsync).ConfigureAwait(false);
                _voiceLab.Reload();
            }
            catch (VoiceActivationException exception)
            {
                LastFailure = FailureOf(exception.Failure);
                _log?.Warning($"Preparing Voice Lab failed ({exception.Failure}).", exception);
            }
            finally
            {
                _running = false;
            }
        }

        // The update switches only once nothing runs from the environment in use, as switching removes it.
        private async Task WaitUntilIdleAsync(CancellationToken cancellationToken)
        {
            while (_voiceLab.IsBusy)
            {
                await Task.Delay(s_idlePoll, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
