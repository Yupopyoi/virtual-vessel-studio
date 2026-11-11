using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.VoiceLab.Scripts;

namespace VirtualVessel.VoiceLab.Activation
{
    /// <summary>How a build ended, for the record.</summary>
    public enum VoiceActivationOutcome
    {
        Started,
        Succeeded,
        Failed,
        Cancelled,
    }

    /// <summary>
    /// Activation as a whole (Voice Lab detailed design 6.1, 8.1): the record of what is built, a build or an update with
    /// <see cref="VoiceComponentInstaller"/>, and the switch to the new environment once it succeeded. The switch writes
    /// the environment file Voice Lab starts from, then removes earlier versions, downloads, and caches. Until then the
    /// environment in use stays as it was, so a stopped or failed update leaves Voice Lab working.
    /// </summary>
    public sealed class VoiceLabActivation
    {
        /// <summary>Where a developer's own environment file goes when activation replaces it, so it is not lost.</summary>
        public const string DeveloperBackupFile = "voice-lab-environment.before-activation.json";

        private readonly string _configDirectory;
        private readonly VoiceComponentManifest _manifest;
        private readonly string _sharedModels;
        private readonly string _scripts;
        private readonly Func<VoiceActivationFolders, VoiceComponentInstaller> _installerFor;
        private readonly ConfigFileStore<VoiceActivationRecord> _store;
        private readonly ISystemClock _clock;
        private readonly ILog _log;

        /// <param name="sharedModels">The Data Root's VoiceModels/Shared/1, for the models streaming uses.</param>
        /// <param name="scripts">The app's Python scripts, with the lock file.</param>
        /// <param name="installerFor">Makes the installer for the folders of the chosen location.</param>
        /// <param name="termsVersion">The version of the terms this app shows; a build needs it agreed to.</param>
        public VoiceLabActivation(string configDirectory, VoiceComponentManifest manifest, string sharedModels, string scripts, Func<VoiceActivationFolders, VoiceComponentInstaller> installerFor, string termsVersion, ISystemClock clock, ILog log)
        {
            TermsVersion = termsVersion ?? throw new ArgumentNullException(nameof(termsVersion));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _configDirectory = configDirectory ?? throw new ArgumentNullException(nameof(configDirectory));
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            _sharedModels = sharedModels ?? throw new ArgumentNullException(nameof(sharedModels));
            _scripts = scripts ?? throw new ArgumentNullException(nameof(scripts));
            _installerFor = installerFor ?? throw new ArgumentNullException(nameof(installerFor));
            _log = log;
            _store = new ConfigFileStore<VoiceActivationRecord>(configDirectory, VoiceActivationRecord.FileName, VoiceActivationRecord.SchemaVersion, () => new VoiceActivationRecord(), clock);
            Record = _store.Load().Value ?? new VoiceActivationRecord();
        }

        public VoiceActivationRecord Record { get; private set; }

        /// <summary>The terms' version this app shows.</summary>
        public string TermsVersion { get; }

        /// <summary>Whether this app's terms are agreed to; a new version needs agreeing again (Voice Lab detailed design 8.2).</summary>
        public bool TermsAgreed => VoiceTerms.IsAgreed(Record, TermsVersion);

        /// <summary>Keeps that this app's terms were agreed to, and when.</summary>
        public void AgreeToTerms()
        {
            VoiceActivationRecord next = Record.Clone();
            next.TermsVersion = TermsVersion;
            next.AgreedAtUtc = _clock.UtcNow;
            Save(next);
            _log?.Information($"Voice Lab's terms {TermsVersion} were agreed to.");
        }

        /// <summary>The folder chosen to build in; the user's local application data unless another was chosen.</summary>
        public string Location => Record.Location ?? VoiceActivationFolders.DefaultLocation;

        /// <summary>The folders a build in <see cref="Location"/> uses.</summary>
        public VoiceActivationFolders Folders => new VoiceActivationFolders(VoiceActivationFolders.RootIn(Location), _sharedModels, _scripts);

        /// <summary>
        /// Builds in another folder from now on (the user's choice, 2026-11-11). Only before an environment is built: one in
        /// use is deleted first and built again where wanted. What a stopped build left in the old place stays there.
        /// Invalid for a folder that does not exist or is not a full path; Busy while an environment is in use.
        /// </summary>
        public CrudResult SetLocation(string folder)
        {
            if (IsReady)
            {
                return CrudResult.Fail(CrudError.Busy, "An environment is in use; delete it before building elsewhere.");
            }

            if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathRooted(folder) || !Directory.Exists(folder))
            {
                return CrudResult.Fail(CrudError.Invalid, $"'{folder}' is not a folder that exists.");
            }

            VoiceActivationRecord next = Record.Clone();
            string full = Path.GetFullPath(folder);
            next.Location = string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), VoiceActivationFolders.DefaultLocation.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ? null : full;
            next.InstallingVersion = null;
            next.State = VoiceActivationState.NotStarted;
            Save(next);
            _log?.Information($"Voice Lab's environment will be built in {VoiceActivationFolders.RootIn(Location)}.");
            return CrudResult.Success;
        }

        /// <summary>
        /// Forgets the environment after it was deleted, keeping the terms agreed to and the location chosen, so the next
        /// build starts afresh.
        /// </summary>
        public void ForgetEnvironment()
        {
            VoiceActivationRecord next = Record.Clone();
            next.ComponentsVersion = null;
            next.InstallingVersion = null;
            next.State = VoiceActivationState.NotStarted;
            Save(next);
        }

        /// <summary>The version this app would build.</summary>
        public string AvailableVersion => _manifest.Version;

        /// <summary>Bytes the build downloads, to show before it starts.</summary>
        public long DownloadSize => _manifest.TotalSize;

        /// <summary>Whether an environment is built and in use.</summary>
        public bool IsReady => Record.State == VoiceActivationState.Ready && Record.ComponentsVersion != null;

        /// <summary>Whether this app brings a newer environment than the one in use. Pure over the record.</summary>
        public bool UpdateAvailable => IsReady && !string.Equals(Record.ComponentsVersion, _manifest.Version, StringComparison.Ordinal);

        /// <summary>Whether a build was stopped or failed partway, so starting again continues it.</summary>
        public bool CanResume => Record.InstallingVersion != null;

        /// <summary>
        /// The record after a build of <paramref name="version"/> reached <paramref name="outcome"/>. An environment
        /// already in use stays Ready through an update that is running, stopped, or failed. Pure.
        /// </summary>
        public static VoiceActivationRecord After(VoiceActivationRecord record, VoiceActivationOutcome outcome, string version)
        {
            VoiceActivationRecord next = record.Clone();
            bool inUse = record.ComponentsVersion != null && record.State == VoiceActivationState.Ready;
            switch (outcome)
            {
                case VoiceActivationOutcome.Started:
                    next.InstallingVersion = version;
                    next.State = inUse ? VoiceActivationState.Ready : VoiceActivationState.Installing;
                    break;
                case VoiceActivationOutcome.Succeeded:
                    next.ComponentsVersion = version;
                    next.InstallingVersion = null;
                    next.State = VoiceActivationState.Ready;
                    break;
                case VoiceActivationOutcome.Failed:
                    next.State = inUse ? VoiceActivationState.Ready : VoiceActivationState.Failed;
                    break;
                case VoiceActivationOutcome.Cancelled:
                    break;
            }

            return next;
        }

        /// <summary>
        /// Builds this app's version and switches to it. The environment file is written only after the build and its
        /// check succeeded.
        /// </summary>
        /// <exception cref="VoiceActivationException">It could not be built; the environment in use, if any, stays.</exception>
        /// <exception cref="OperationCanceledException">It was stopped; starting again continues it.</exception>
        public async Task<VoiceLabEnvironment> RunAsync(Func<VoiceLabEnvironment, CancellationToken, Task<string>> verify, IProgress<VoiceActivationProgress> progress, CancellationToken cancellationToken)
        {
            string version = _manifest.Version;
            if (!TermsAgreed)
            {
                throw new InvalidOperationException($"The terms {TermsVersion} are not agreed to; the environment is not built without them.");
            }

            if (IsReady && !UpdateAvailable)
            {
                throw new InvalidOperationException($"Version {version} is built and in use already.");
            }

            Save(After(Record, VoiceActivationOutcome.Started, version));
            VoiceActivationFolders folders = Folders;
            VoiceComponentInstaller installer = _installerFor(folders);
            _log?.Information($"Building Voice Lab's environment {version} in {folders.Version(version)}.");
            VoiceLabEnvironment environment;
            try
            {
                environment = await installer.InstallAsync(verify, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _log?.Information($"Building Voice Lab's environment {version} was stopped; it continues next time.");
                throw;
            }
            catch (VoiceActivationException exception)
            {
                Save(After(Record, VoiceActivationOutcome.Failed, version));
                _log?.Warning($"Building Voice Lab's environment {version} failed ({exception.Failure}): {exception.Message}");
                throw;
            }

            progress?.Report(new VoiceActivationProgress(VoiceActivationPhase.Finishing, 0, 1));
            SwitchTo(environment, folders.Root);
            Save(After(Record, VoiceActivationOutcome.Succeeded, version));
            _log?.Information($"Voice Lab's environment {version} is ready.");
            await Task.Run(() => installer.CleanUp(version)).ConfigureAwait(false);
            return environment;
        }

        // The environment file Voice Lab starts from; a developer's own (outside the app's folder) is kept aside first.
        private void SwitchTo(VoiceLabEnvironment environment, string root)
        {
            Directory.CreateDirectory(_configDirectory);
            string path = Path.Combine(_configDirectory, VoiceLabService.EnvironmentFile);
            string backup = Path.Combine(_configDirectory, DeveloperBackupFile);
            if (File.Exists(path) && !File.Exists(backup))
            {
                VoiceLabEnvironment before = VoiceLabEnvironment.Parse(File.ReadAllText(path), out _);
                if (before != null && VoiceLabEnvironmentRemover.Owned(before, root) == null)
                {
                    File.Copy(path, backup);
                    _log?.Information($"Kept the earlier environment file as {DeveloperBackupFile}.");
                }
            }

            string written = path + ".new";
            File.WriteAllText(written, environment.ToJson());
            if (File.Exists(path))
            {
                File.Replace(written, path, null);
            }
            else
            {
                File.Move(written, path);
            }
        }

        private void Save(VoiceActivationRecord record)
        {
            Record = record;
            _store.Save(record);
        }
    }
}
