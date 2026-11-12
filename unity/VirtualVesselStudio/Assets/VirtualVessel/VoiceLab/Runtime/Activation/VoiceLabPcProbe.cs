using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ExternalServices.Process;
using VirtualVessel.UI.VoiceLab;

namespace VirtualVessel.VoiceLab.Activation
{
    /// <summary>
    /// The check of the PC before a build (Voice Lab detailed design 8.1; decision record 2026-11-11): an NVIDIA GPU of
    /// sm_50 or later and a driver of 528.33 or later, which PyTorch's CUDA 12.8 build needs, space where it is built, and
    /// a connection. The GPU and the driver come from nvidia-smi, which the NVIDIA driver installs.
    /// </summary>
    public sealed class VoiceLabPcProbe
    {
        /// <summary>The driver PyTorch's CUDA 12.8 build needs on Windows (CUDA 12.x minor version compatibility).</summary>
        public static readonly Version MinimumDriver = new Version(528, 33);

        /// <summary>The oldest GPU generation PyTorch 2.7.1's CUDA 12.8 build has kernels for (Maxwell).</summary>
        public static readonly Version MinimumCapability = new Version(5, 0);

        /// <summary>Space a build may take at its fullest: the environment, the downloads, and uv's cache.</summary>
        public const long RequiredBytes = 30L * 1024 * 1024 * 1024;

        private const string ProbeUrl = "https://github.com";

        private readonly IExternalProcessRunner _runner;
        private readonly string _nvidiaSmi;
        private readonly string _logs;
        private readonly ILog _log;

        /// <param name="runner">Runs nvidia-smi; it must allow the folder <paramref name="nvidiaSmi"/> is in.</param>
        public VoiceLabPcProbe(IExternalProcessRunner runner, string nvidiaSmi, string logs, ILog log)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _nvidiaSmi = nvidiaSmi;
            _logs = logs;
            _log = log;
        }

        /// <summary>Where the NVIDIA driver puts nvidia-smi.</summary>
        public static string DefaultNvidiaSmi => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");

        /// <summary>
        /// nvidia-smi's answer to --query-gpu=name,driver_version,compute_cap --format=csv,noheader: the first GPU's name,
        /// driver, and compute capability; null when the line is not that. Pure.
        /// </summary>
        public static (string Name, string Driver, string Capability)? ParseGpu(string line)
        {
            string[] parts = (line ?? string.Empty).Split(',');
            if (parts.Length < 3 || string.IsNullOrWhiteSpace(parts[0]))
            {
                return null;
            }

            return (parts[0].Trim(), parts[1].Trim(), parts[2].Trim());
        }

        /// <summary>Whether a version text such as "610.88" or "8.9" is at least the minimum. Pure.</summary>
        public static bool AtLeast(string text, Version minimum)
        {
            return Version.TryParse(text?.Trim(), out Version version) && version >= minimum;
        }

        public async Task<VoiceLabPcCheck> CheckAsync(string location, CancellationToken cancellationToken)
        {
            (string Name, string Driver, string Capability)? gpu = await GpuAsync(cancellationToken).ConfigureAwait(false);
            long free = FreeBytes(location);
            bool online = await OnlineAsync(cancellationToken).ConfigureAwait(false);
            return new VoiceLabPcCheck(
                gpu?.Name,
                gpu != null && AtLeast(gpu.Value.Capability, MinimumCapability),
                gpu?.Driver,
                gpu != null && AtLeast(gpu.Value.Driver, MinimumDriver),
                location,
                free,
                free >= RequiredBytes,
                online);
        }

        private async Task<(string Name, string Driver, string Capability)?> GpuAsync(CancellationToken cancellationToken)
        {
            if (_nvidiaSmi == null || !File.Exists(_nvidiaSmi))
            {
                return null;
            }

            string first = null;
            var request = new ExternalProcessRequest(_nvidiaSmi, new[] { "--query-gpu=name,driver_version,compute_cap", "--format=csv,noheader" }, Path.GetDirectoryName(_nvidiaSmi), Path.Combine(_logs, "nvidia-smi.log"))
            {
                Timeout = TimeSpan.FromSeconds(15),
            };
            ExternalProcessResult result;
            try
            {
                Directory.CreateDirectory(_logs);
                result = await _runner.RunAsync(request, line => first ??= line, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // Its log could not be written; the check goes on without a GPU rather than stopping on "Checking".
                _log?.Information($"nvidia-smi could not run: {exception.Message}");
                return null;
            }

            if (result.State != ExternalProcessState.Completed || result.ExitCode != 0)
            {
                _log?.Information($"nvidia-smi gave no GPU ({result.State}, exit {result.ExitCode}).");
                return null;
            }

            return ParseGpu(first);
        }

        private static long FreeBytes(string location)
        {
            try
            {
                return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(location))).AvailableFreeSpace;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is UnauthorizedAccessException)
            {
                return 0L;
            }
        }

        private async Task<bool> OnlineAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                using var request = new HttpRequestMessage(HttpMethod.Head, ProbeUrl);
                using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception exception) when (HttpComponentDownloader.IsConnectionFailure(exception))
            {
                // Whatever type the runtime gives a failed connection (Unity's throws WebException and IOException too):
                // one that escaped would leave the check on "Checking" with no way on.
                cancellationToken.ThrowIfCancellationRequested();
                _log?.Information($"{ProbeUrl} could not be reached: {exception.Message}");
                return false;
            }
        }

        /// <summary>A size in GB with one decimal, for logs and the screen. Pure.</summary>
        public static string Gigabytes(long bytes)
        {
            return (bytes / (1024d * 1024d * 1024d)).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
        }
    }
}
