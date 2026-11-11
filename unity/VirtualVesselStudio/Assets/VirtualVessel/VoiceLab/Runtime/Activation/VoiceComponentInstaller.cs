using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ExternalServices.Process;
using VirtualVessel.VoiceLab.Scripts;

namespace VirtualVessel.VoiceLab.Activation
{
    /// <summary>The steps of building the environment, in order.</summary>
    public enum VoiceActivationPhase
    {
        Downloading,
        Placing,
        InstallingPython,
        InstallingPackages,
        Verifying,
        Finishing,
    }

    /// <summary>Where a build is: the phase, and bytes downloaded of the total while downloading.</summary>
    public readonly struct VoiceActivationProgress
    {
        public VoiceActivationProgress(VoiceActivationPhase phase, long done, long total)
        {
            Phase = phase;
            Done = done;
            Total = total;
        }

        public VoiceActivationPhase Phase { get; }

        public long Done { get; }

        public long Total { get; }
    }

    /// <summary>Why a build stopped, for the screen to say what to do.</summary>
    public enum VoiceActivationFailure
    {
        /// <summary>A download did not complete from its source; trying again continues it.</summary>
        Network,

        /// <summary>A file did not match its hash from either place; it is not used.</summary>
        Corrupt,

        /// <summary>The disk filled up.</summary>
        DiskFull,

        /// <summary>Installing Python or the packages failed; the log has the details.</summary>
        Setup,

        /// <summary>The environment was built but the trial run failed.</summary>
        Verify,
    }

    /// <summary>A build that did not complete; what was downloaded stays, so trying again continues.</summary>
    public sealed class VoiceActivationException : Exception
    {
        public VoiceActivationException(VoiceActivationFailure failure, string message, Exception innerException = null)
            : base(message, innerException)
        {
            Failure = failure;
        }

        public VoiceActivationFailure Failure { get; }
    }

    /// <summary>The folders a build uses and writes.</summary>
    public sealed class VoiceActivationFolders
    {
        /// <param name="root">Where environments are built, one folder per version, with downloads and uv's cache.</param>
        /// <param name="sharedModels">The Data Root's VoiceModels/Shared/1, for the models streaming uses.</param>
        /// <param name="scripts">The app's Python scripts, with pyproject.toml and the lock file.</param>
        public VoiceActivationFolders(string root, string sharedModels, string scripts)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            SharedModels = sharedModels ?? throw new ArgumentNullException(nameof(sharedModels));
            Scripts = scripts ?? throw new ArgumentNullException(nameof(scripts));
        }

        public string Root { get; }

        /// <summary>
        /// Where environments are built inside a folder the user chose: the app's own folders within it, so nothing the
        /// user keeps there is ever deleted with them. Pure.
        /// </summary>
        public static string RootIn(string location)
        {
            return Path.Combine(location, "VirtualVesselStudio", "External", "VoiceLab");
        }

        /// <summary>The default location, the user's local application data.</summary>
        public static string DefaultLocation => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        public string SharedModels { get; }

        public string Scripts { get; }

        public string Downloads => Path.Combine(Root, "downloads");

        public string Cache => Path.Combine(Root, "cache");

        public string Logs => Path.Combine(Root, "logs");

        public string Version(string version)
        {
            return Path.Combine(Root, version);
        }
    }

    /// <summary>
    /// Builds Voice Lab's training environment on this PC (Voice Lab detailed design 8.1; system design 13.4): downloads
    /// every component, checks its hash, places it, installs Python and the locked packages with uv, and checks the
    /// result. It builds into the version's own folder and leaves any earlier version alone, so an update can stop or
    /// fail without losing the environment in use; the caller switches to the new one only after it succeeds. Downloads
    /// are kept by their hash, so a stopped build continues.
    /// </summary>
    public sealed class VoiceComponentInstaller
    {
        private const int HashBuffer = 1 << 20;

        private readonly VoiceComponentManifest _manifest;
        private readonly VoiceActivationFolders _folders;
        private readonly IComponentDownloader _downloader;
        private readonly IExternalProcessRunner _runner;
        private readonly ILog _log;

        /// <param name="runner">Runs uv; it must allow programs inside <see cref="VoiceActivationFolders.Root"/>.</param>
        public VoiceComponentInstaller(VoiceComponentManifest manifest, VoiceActivationFolders folders, IComponentDownloader downloader, IExternalProcessRunner runner, ILog log)
        {
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            _folders = folders ?? throw new ArgumentNullException(nameof(folders));
            _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _log = log;
        }

        /// <summary>
        /// Builds the manifest's version and returns the environment in it; it is not switched to. The version's folder
        /// is cleared first, as one left by a stopped build may be half made.
        /// </summary>
        /// <param name="verify">Runs the built environment briefly; returns why it failed, or null.</param>
        /// <exception cref="VoiceActivationException">It could not be built.</exception>
        /// <exception cref="OperationCanceledException">It was stopped; downloads stay for next time.</exception>
        public async Task<VoiceLabEnvironment> InstallAsync(Func<VoiceLabEnvironment, CancellationToken, Task<string>> verify, IProgress<VoiceActivationProgress> progress, CancellationToken cancellationToken)
        {
            string folder = _folders.Version(_manifest.Version);
            try
            {
                Dictionary<string, string> files = await DownloadAllAsync(progress, cancellationToken).ConfigureAwait(false);

                progress?.Report(new VoiceActivationProgress(VoiceActivationPhase.Placing, 0, _manifest.Components.Count));
                await Task.Run(() => Place(files, folder, cancellationToken), cancellationToken).ConfigureAwait(false);

                string uv = Path.Combine(folder, UvExecutable);
                progress?.Report(new VoiceActivationProgress(VoiceActivationPhase.InstallingPython, 0, 1));
                await RunUvAsync(uv, PythonArguments(_manifest.Python, PythonFolder(folder)), folder, "python", cancellationToken).ConfigureAwait(false);
                progress?.Report(new VoiceActivationProgress(VoiceActivationPhase.InstallingPackages, 0, 1));
                await RunUvAsync(uv, SyncArguments(_folders.Scripts), folder, "packages", cancellationToken).ConfigureAwait(false);

                VoiceLabEnvironment environment = EnvironmentIn(folder, _folders.Scripts, _manifest);
                IReadOnlyList<string> missing = environment.Problems(File.Exists, Directory.Exists);
                if (missing.Count > 0)
                {
                    throw new VoiceActivationException(VoiceActivationFailure.Setup, $"The built environment is missing parts: {string.Join("; ", missing)}");
                }

                if (verify != null)
                {
                    progress?.Report(new VoiceActivationProgress(VoiceActivationPhase.Verifying, 0, 1));
                    string problem = await verify(environment, cancellationToken).ConfigureAwait(false);
                    if (problem != null)
                    {
                        throw new VoiceActivationException(VoiceActivationFailure.Verify, $"The trial run failed: {problem}");
                    }
                }

                return environment;
            }
            catch (IOException exception) when (IsDiskFull(exception))
            {
                throw new VoiceActivationException(VoiceActivationFailure.DiskFull, $"The disk filled up while building in {folder}.", exception);
            }
        }

        /// <summary>
        /// After switching to <paramref name="keepVersion"/>: removes other versions' folders, the downloads, and uv's
        /// cache, which hold several GB once the build is done. Failures are logged, not thrown; they only cost space.
        /// </summary>
        public void CleanUp(string keepVersion)
        {
            foreach (string folder in Directory.Exists(_folders.Root) ? Directory.GetDirectories(_folders.Root) : Array.Empty<string>())
            {
                string name = Path.GetFileName(folder);
                bool keep = string.Equals(name, keepVersion, StringComparison.OrdinalIgnoreCase) || string.Equals(name, "logs", StringComparison.OrdinalIgnoreCase);
                if (!keep)
                {
                    TryDelete(folder);
                }
            }
        }

        /// <summary>uv.exe's place in a version folder.</summary>
        public const string UvExecutable = "uv\\uv.exe";

        /// <summary>Where uv puts Python inside a version folder. Pure.</summary>
        public static string PythonFolder(string versionFolder)
        {
            return Path.Combine(versionFolder, "python");
        }

        /// <summary>The virtual environment's folder inside a version folder. Pure.</summary>
        public static string VenvFolder(string versionFolder)
        {
            return Path.Combine(versionFolder, "venv");
        }

        /// <summary>uv's arguments to install the Python version into a folder. Pure.</summary>
        public static IReadOnlyList<string> PythonArguments(string python, string pythonFolder)
        {
            return new[] { "python", "install", python, "--install-dir", pythonFolder };
        }

        /// <summary>
        /// uv's arguments to install the locked packages and nothing else: the lock as it is (--frozen), without the
        /// developers' group. Pure.
        /// </summary>
        public static IReadOnlyList<string> SyncArguments(string scripts)
        {
            return new[] { "sync", "--frozen", "--no-dev", "--project", scripts };
        }

        /// <summary>
        /// uv's settings for a build in a version folder: its own Python, venv, and cache, copies rather than links (the
        /// cache goes when it is done), and no Python from elsewhere on the PC. Pure.
        /// </summary>
        public static IReadOnlyDictionary<string, string> UvEnvironment(string versionFolder, string python, string cache)
        {
            return new Dictionary<string, string>
            {
                ["UV_CACHE_DIR"] = cache,
                ["UV_PYTHON_INSTALL_DIR"] = PythonFolder(versionFolder),
                ["UV_PYTHON"] = python,
                ["UV_PYTHON_PREFERENCE"] = "only-managed",
                ["UV_PROJECT_ENVIRONMENT"] = VenvFolder(versionFolder),
                ["UV_LINK_MODE"] = "copy",
                ["UV_NO_CONFIG"] = "1",
            };
        }

        /// <summary>
        /// The environment as built in a version folder: RVC and VoxCPM2 in their folders, which the list's targets fill,
        /// and the template by its components. Pure.
        /// </summary>
        public static VoiceLabEnvironment EnvironmentIn(string versionFolder, string scripts, VoiceComponentManifest manifest)
        {
            string Target(string id)
            {
                VoiceComponent component = manifest.Components.FirstOrDefault(c => c.Id == id);
                return component == null ? null : Path.Combine(versionFolder, component.Target.Replace('/', Path.DirectorySeparatorChar));
            }

            return new VoiceLabEnvironment(
                Path.Combine(VenvFolder(versionFolder), "Scripts", "python.exe"),
                scripts,
                Path.Combine(versionFolder, "rvc"),
                Path.Combine(versionFolder, "voxcpm2"),
                Target("template"),
                Target("template-manifest"));
        }

        /// <summary>
        /// Where a zip entry goes: the target folder joined with the entry's path less <paramref name="strip"/> leading
        /// folders; null for an entry with nothing left, or one whose path would leave the folder (zip slip). Pure.
        /// </summary>
        public static string ZipEntryPath(string targetFolder, string entryName, int strip)
        {
            string[] parts = entryName.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= strip)
            {
                return null;
            }

            string relative = string.Join("/", parts.Skip(strip));
            if (!VoiceComponentManifest.IsRelativeInside(relative))
            {
                return null;
            }

            return Path.Combine(targetFolder, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>Whether an IOException is the disk filling up (ERROR_DISK_FULL, ERROR_HANDLE_DISK_FULL). Pure.</summary>
        public static bool IsDiskFull(IOException exception)
        {
            int code = exception.HResult & 0xFFFF;
            return code == 112 || code == 39;
        }

        private async Task<Dictionary<string, string>> DownloadAllAsync(IProgress<VoiceActivationProgress> progress, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(_folders.Downloads);
            long total = _manifest.TotalSize;
            long finished = 0L;
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (VoiceComponent component in _manifest.Components)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long before = finished;
                var received = new Progress<long>(bytes => progress?.Report(new VoiceActivationProgress(VoiceActivationPhase.Downloading, before + Math.Min(bytes, component.Size), total)));
                files[component.Id] = await DownloadAsync(component, received, cancellationToken).ConfigureAwait(false);
                finished += component.Size;
                progress?.Report(new VoiceActivationProgress(VoiceActivationPhase.Downloading, finished, total));
            }

            return files;
        }

        // A file checked once is kept under its hash, so a build that stopped, or the next version sharing it, skips it.
        private async Task<string> DownloadAsync(VoiceComponent component, IProgress<long> received, CancellationToken cancellationToken)
        {
            string done = Path.Combine(_folders.Downloads, component.Sha256);
            if (File.Exists(done))
            {
                return done;
            }

            // Only from its source: the project keeps no copies of others' files (the user's decision, 2026-11-11).
            string part = done + ".part";
            try
            {
                await _downloader.DownloadAsync(component.Url, part, received, cancellationToken).ConfigureAwait(false);
            }
            catch (ComponentDownloadException exception)
            {
                // What was received stays, so trying again continues from there.
                throw new VoiceActivationException(VoiceActivationFailure.Network, $"{component.Id} could not be downloaded: {exception.Message}");
            }

            string hash = await Task.Run(() => Sha256Of(part), cancellationToken).ConfigureAwait(false);
            if (hash != component.Sha256)
            {
                File.Delete(part);
                throw new VoiceActivationException(VoiceActivationFailure.Corrupt, $"{component.Id} from {component.Url} did not match its hash.");
            }

            File.Move(part, done);
            return done;
        }

        private void Place(Dictionary<string, string> files, string folder, CancellationToken cancellationToken)
        {
            TryDelete(folder);
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(_folders.SharedModels);
            foreach (VoiceComponent component in _manifest.Components)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string root = component.Place == VoiceComponentPlace.SharedModels ? _folders.SharedModels : folder;
                string target = Path.Combine(root, component.Target.Replace('/', Path.DirectorySeparatorChar));
                if (component.Extract)
                {
                    Extract(files[component.Id], target, component.Strip);
                }
                else if (component.Place == VoiceComponentPlace.SharedModels)
                {
                    // Streaming may be reading the shared models; one that is already right is left alone, and a new
                    // one is written beside it and swapped in.
                    if (!File.Exists(target) || Sha256Of(target) != component.Sha256)
                    {
                        File.Copy(files[component.Id], target + ".part", true);
                        File.Delete(target);
                        File.Move(target + ".part", target);
                    }
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(files[component.Id], target, true);
                }
            }
        }

        private static void Extract(string zip, string targetFolder, int strip)
        {
            Directory.CreateDirectory(targetFolder);
            using ZipArchive archive = ZipFile.OpenRead(zip);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string path = ZipEntryPath(targetFolder, entry.FullName, strip);
                if (path == null)
                {
                    continue;
                }

                if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(path);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                entry.ExtractToFile(path, true);
            }
        }

        private async Task RunUvAsync(string uv, IReadOnlyList<string> arguments, string folder, string what, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(_folders.Logs);
            var request = new ExternalProcessRequest(uv, arguments, folder, Path.Combine(_folders.Logs, $"{_manifest.Version}-{what}.log"))
            {
                Environment = UvEnvironment(folder, _manifest.Python, _folders.Cache),
            };
            ExternalProcessResult result = await _runner.RunAsync(request, null, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.State != ExternalProcessState.Completed || result.ExitCode != 0)
            {
                string tail = string.Join(" | ", result.LastErrorLines ?? Array.Empty<string>());
                throw new VoiceActivationException(VoiceActivationFailure.Setup, $"Installing the {what} failed ({result.State}, exit {result.ExitCode}): {result.Failure} {tail}");
            }
        }

        private static string Sha256Of(string path)
        {
            using var sha = SHA256.Create();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, HashBuffer);
            return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
        }

        private void TryDelete(string folder)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                _log?.Warning($"Could not delete {folder}: {exception.Message}");
            }
        }
    }
}
