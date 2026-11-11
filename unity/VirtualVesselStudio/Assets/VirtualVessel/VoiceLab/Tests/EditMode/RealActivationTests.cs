using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NUnit.Framework;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ExternalServices.Process;
using VirtualVessel.VoiceLab.Activation;
using VirtualVessel.VoiceLab.Scripts;

namespace VirtualVessel.VoiceLab.Tests
{
    /// <summary>
    /// A real activation (Voice Lab detailed design 14, by hand): the bundled list, downloaded over the internet and
    /// built with uv into VV_VOICE_ACTIVATION_ROOT, then torch and voxcpm imported from it. About 10 GB to download and
    /// an hour; run it by name with the variable set.
    /// </summary>
    public sealed class RealActivationTests
    {
        [Test]
        [Timeout(4 * 60 * 60 * 1000)]
        public void TheBundledList_BuildsAnEnvironmentThatImportsTorchAndVoxCpm()
        {
            string root = Environment.GetEnvironmentVariable("VV_VOICE_ACTIVATION_ROOT");
            if (string.IsNullOrEmpty(root))
            {
                Assert.Ignore("VV_VOICE_ACTIVATION_ROOT is not set.");
            }

            // Unity runs tests from the project folder; the scripts are two folders up, in services/voice-lab.
            string scripts = Path.GetFullPath(Path.Combine("..", "..", "services", "voice-lab"));
            VoiceComponentManifest manifest = VoiceComponentManifest.Parse(File.ReadAllText(Path.Combine(scripts, VoiceComponentManifest.FileName)), out IReadOnlyList<string> problems);
            Assert.That(problems, Is.Empty);
            var folders = new VoiceActivationFolders(Path.Combine(root, "External"), Path.Combine(root, "VoiceModels", "Shared", "1"), scripts);
            var clock = new UtcSystemClock();
            var log = new ConsoleLog();
            var runner = new ExternalProcessRunner(new[] { folders.Root }, log, clock);
            using var downloader = new HttpComponentDownloader();
            var installer = new VoiceComponentInstaller(manifest, folders, downloader, runner, log);
            var activation = new VoiceLabActivation(Path.Combine(root, "Config"), manifest, folders, installer, clock, log);
            VoiceActivationPhase? shown = null;
            var progress = new Progress<VoiceActivationProgress>(p =>
            {
                if (p.Phase != shown)
                {
                    shown = p.Phase;
                    TestContext.Progress.WriteLine($"[activation] {p.Phase} at {DateTime.Now:HH:mm:ss}");
                }
            });

            VoiceLabEnvironment environment = activation.RunAsync(null, progress, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(environment.Problems(File.Exists, Directory.Exists), Is.Empty);
            var check = new ExternalProcessRequest(environment.Python, new[] { "-c", "import torch, voxcpm, librosa; print('torch', torch.__version__, torch.cuda.is_available())" }, root, Path.Combine(root, "import-check.log"));
            ExternalProcessResult result = runner.RunAsync(check, line => TestContext.Progress.WriteLine("[python] " + line), CancellationToken.None).GetAwaiter().GetResult();
            Assert.That((result.State, result.ExitCode), Is.EqualTo((ExternalProcessState.Completed, (int?)0)), string.Join(" | ", result.LastErrorLines));
            Assert.That(activation.IsReady, Is.True);
        }

        // Writes to the test's progress, so a run from the command line shows where it is.
        private sealed class ConsoleLog : ILog
        {
            public bool IsEnabled(LogLevel level)
            {
                return true;
            }

            public void Write(LogLevel level, string message, Exception exception = null)
            {
                TestContext.Progress.WriteLine($"[{level}] {message} {exception?.Message}");
            }

            public void Write(LogLevel level, string message, IReadOnlyList<LogProperty> properties, Exception exception = null)
            {
                Write(level, message, exception);
            }

            public ILog WithContext(string key, string value)
            {
                return this;
            }
        }
    }
}
