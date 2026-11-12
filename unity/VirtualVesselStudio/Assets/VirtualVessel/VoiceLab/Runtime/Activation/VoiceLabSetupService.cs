using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ExternalServices.Process;
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
        private readonly object _entriesGate = new object();
        private readonly List<VoiceLabSetupEntry> _entries = new List<VoiceLabSetupEntry>();
        private volatile bool _running;
        private volatile bool _live;
        private CancellationTokenSource _pause;

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

        /// <summary>
        /// Setup as the app runs it: the download list and the terms from the scripts folder the app ships, a build in the
        /// folder chosen for it, and the trial run at its end. Null when the list cannot be read, which leaves Voice Lab
        /// as it is, such as a developer's own environment.
        /// </summary>
        /// <param name="scripts">The project's Voice Lab folder: the repository's services/voice-lab in the Editor, the app's VoiceLab/scripts in a build.</param>
        /// <param name="sharedModels">The shared models' version folder in the data root (VoiceModels/Shared/1).</param>
        /// <param name="logs">Where the check of the PC keeps nvidia-smi's output.</param>
        public static VoiceLabSetupService Create(string configDirectory, string scripts, string sharedModels, string logs, VoiceLabService voiceLab, ISystemClock clock, ILog log)
        {
            string listFile = Path.Combine(scripts, VoiceComponentManifest.FileName);
            if (!File.Exists(listFile))
            {
                log?.Warning($"Voice Lab cannot be prepared here: {listFile} is missing.");
                return null;
            }

            VoiceComponentManifest manifest = VoiceComponentManifest.Parse(File.ReadAllText(listFile), out IReadOnlyList<string> problems);
            if (manifest == null || problems.Count > 0)
            {
                log?.Warning($"Voice Lab cannot be prepared here: {listFile} is not valid ({string.Join("; ", problems)}).");
                return null;
            }

            string termsVersion = VoiceTerms.Load(scripts, "en")?.Version;
            if (termsVersion == null)
            {
                log?.Warning($"Voice Lab cannot be prepared here: its terms in {scripts} cannot be read.");
                return null;
            }

            // App-lifetime: it is one HttpClient for every build.
            var downloader = new HttpComponentDownloader();
            VoiceLabActivation activation = null;
            activation = new VoiceLabActivation(
                configDirectory,
                manifest,
                sharedModels,
                scripts,
                folders => new VoiceComponentInstaller(manifest, folders, downloader, new ExternalProcessRunner(new[] { folders.Root }, log, clock), log),
                termsVersion,
                clock,
                log);

            // The trial run goes next to the build and is deleted after it.
            Task<string> Verify(VoiceLabEnvironment environment, CancellationToken cancellationToken)
            {
                var runner = new ExternalProcessRunner(environment.AllowedFolders, log, clock);
                return VoiceActivationCheck.Create(Path.Combine(activation.Folders.Root, "check"), runner, clock, log)(environment, cancellationToken);
            }

            string nvidiaSmi = VoiceLabPcProbe.DefaultNvidiaSmi;
            var probe = new VoiceLabPcProbe(new ExternalProcessRunner(new[] { Path.GetDirectoryName(nvidiaSmi) }, log, clock), nvidiaSmi, logs, log);
            return new VoiceLabSetupService(activation, voiceLab, probe, Verify, scripts, log);
        }

        /// <summary>
        /// Ready also with an environment set up outside activation, such as a developer's own: Voice Lab works, so
        /// nothing is offered to prepare. Trying activation then needs that environment's file moved away.
        /// </summary>
        public VoiceLabSetupState State => StateOf(_running, _activation.IsReady || _voiceLab.Availability == VoiceLabAvailability.Ready, _activation.CanResume, _activation.Record.State, LastFailure);

        public VoiceLabSetupFailure LastFailure { get; private set; }

        public bool PausedForLive { get; private set; }

        /// <summary>
        /// Streaming or recording started or ended. A build running is paused when they start: downloading and the
        /// check's voice compete with the stream for the connection and the GPU (the user's decision, 2026-11-13).
        /// </summary>
        public void SetLive(bool live)
        {
            _live = live;
            if (live)
            {
                _pause?.Cancel();
            }
        }

        public bool UpdateAvailable => _activation.UpdateAvailable;

        public long DownloadBytes => _activation.DownloadSize;

        public VoiceLabSetupProgress Progress => _progress.Value;

        public IReadOnlyList<VoiceLabSetupEntry> Entries
        {
            get
            {
                lock (_entriesGate)
                {
                    return _entries.ToArray();
                }
            }
        }

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

        /// <summary>The record's kind for a phase of the build. Pure.</summary>
        public static VoiceLabSetupEntryKind KindOf(VoiceActivationPhase phase)
        {
            switch (phase)
            {
                case VoiceActivationPhase.Downloading:
                    return VoiceLabSetupEntryKind.Download;
                case VoiceActivationPhase.Placing:
                    return VoiceLabSetupEntryKind.Unpack;
                case VoiceActivationPhase.InstallingPython:
                    return VoiceLabSetupEntryKind.Runtime;
                case VoiceActivationPhase.InstallingPackages:
                    return VoiceLabSetupEntryKind.Packages;
                case VoiceActivationPhase.Verifying:
                    return VoiceLabSetupEntryKind.Check;
                default:
                    return VoiceLabSetupEntryKind.Finish;
            }
        }

        /// <summary>Whether progress starts a new line of the record: another phase, or another component. Pure.</summary>
        public static bool StartsEntry(VoiceLabSetupEntry? last, VoiceActivationProgress progress)
        {
            return last == null || last.Value.Kind != KindOf(progress.Phase) || !string.Equals(last.Value.Item, progress.Item, StringComparison.Ordinal);
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

            if (_live)
            {
                PausedForLive = true;
                return;
            }

            _running = true;
            PausedForLive = false;
            LastFailure = VoiceLabSetupFailure.None;
            using var pause = _pause = new CancellationTokenSource();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, pause.Token);
            _progress.Value = new VoiceLabSetupProgress(VoiceLabSetupStep.Download, 0L, DownloadBytes);
            var progress = new Forward<VoiceActivationProgress>(p =>
            {
                _progress.Value = new VoiceLabSetupProgress(StepOf(p.Phase), p.Done, p.Total);
                Record(p);
            });
            try
            {
                await _activation.RunAsync(_verify, progress, linked.Token, WaitUntilIdleAsync).ConfigureAwait(false);
                _voiceLab.Reload();
                Record(VoiceLabSetupEntryKind.Done);
            }
            catch (VoiceActivationException exception)
            {
                LastFailure = FailureOf(exception.Failure);
                _log?.Warning($"Preparing Voice Lab failed ({exception.Failure}).", exception);
                Record(VoiceLabSetupEntryKind.Failed);
            }
            catch (OperationCanceledException) when (pause.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // Paused for streaming or recording: a stop like the user's, but the screen says why.
                PausedForLive = true;
                Record(VoiceLabSetupEntryKind.Stopped);
            }
            catch (OperationCanceledException)
            {
                Record(VoiceLabSetupEntryKind.Stopped);
                throw;
            }
            catch (Exception exception)
            {
                // Not one of the kinds the build names: still a failure the screen shows, not a silent stop.
                LastFailure = VoiceLabSetupFailure.Setup;
                _log?.Error("Preparing Voice Lab failed unexpectedly.", exception);
                Record(VoiceLabSetupEntryKind.Failed);
            }
            finally
            {
                _pause = null;
                _running = false;
            }
        }

        private void Record(VoiceActivationProgress progress)
        {
            lock (_entriesGate)
            {
                VoiceLabSetupEntry? last = _entries.Count == 0 ? (VoiceLabSetupEntry?)null : _entries[_entries.Count - 1];
                if (StartsEntry(last, progress))
                {
                    _entries.Add(new VoiceLabSetupEntry(DateTime.Now, KindOf(progress.Phase), progress.Item, progress.ItemSize));
                }
            }
        }

        private void Record(VoiceLabSetupEntryKind kind)
        {
            lock (_entriesGate)
            {
                _entries.Add(new VoiceLabSetupEntry(DateTime.Now, kind));
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
