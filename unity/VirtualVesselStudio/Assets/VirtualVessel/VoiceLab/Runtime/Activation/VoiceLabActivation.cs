using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ProjectData.Config;
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
        private readonly VoiceActivationFolders _folders;
        private readonly VoiceComponentInstaller _installer;
        private readonly ConfigFileStore<VoiceActivationRecord> _store;
        private readonly ILog _log;

        public VoiceLabActivation(string configDirectory, VoiceComponentManifest manifest, VoiceActivationFolders folders, VoiceComponentInstaller installer, ISystemClock clock, ILog log)
        {
            _configDirectory = configDirectory ?? throw new ArgumentNullException(nameof(configDirectory));
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            _folders = folders ?? throw new ArgumentNullException(nameof(folders));
            _installer = installer ?? throw new ArgumentNullException(nameof(installer));
            _log = log;
            _store = new ConfigFileStore<VoiceActivationRecord>(configDirectory, VoiceActivationRecord.FileName, VoiceActivationRecord.SchemaVersion, () => new VoiceActivationRecord(), clock);
            Record = _store.Load().Value ?? new VoiceActivationRecord();
        }

        public VoiceActivationRecord Record { get; private set; }

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
            if (IsReady && !UpdateAvailable)
            {
                throw new InvalidOperationException($"Version {version} is built and in use already.");
            }

            Save(After(Record, VoiceActivationOutcome.Started, version));
            _log?.Information($"Building Voice Lab's environment {version} in {_folders.Version(version)}.");
            VoiceLabEnvironment environment;
            try
            {
                environment = await _installer.InstallAsync(verify, progress, cancellationToken).ConfigureAwait(false);
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
            SwitchTo(environment);
            Save(After(Record, VoiceActivationOutcome.Succeeded, version));
            _log?.Information($"Voice Lab's environment {version} is ready.");
            await Task.Run(() => _installer.CleanUp(version)).ConfigureAwait(false);
            return environment;
        }

        // The environment file Voice Lab starts from; a developer's own (outside the app's folder) is kept aside first.
        private void SwitchTo(VoiceLabEnvironment environment)
        {
            Directory.CreateDirectory(_configDirectory);
            string path = Path.Combine(_configDirectory, VoiceLabService.EnvironmentFile);
            string backup = Path.Combine(_configDirectory, DeveloperBackupFile);
            if (File.Exists(path) && !File.Exists(backup))
            {
                VoiceLabEnvironment before = VoiceLabEnvironment.Parse(File.ReadAllText(path), out _);
                if (before != null && VoiceLabEnvironmentRemover.Owned(before, _folders.Root) == null)
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
