using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VirtualVessel.ExternalServices.Process;
using VirtualVessel.VoiceLab.Activation;
using VirtualVessel.VoiceLab.Scripts;

namespace VirtualVessel.VoiceLab.Tests
{
    /// <summary>The activation record, and switching to a built environment only once it succeeded (Voice Lab detailed design 6.1).</summary>
    public sealed class ActivationTests
    {
        private string _folder;
        private string _config;
        private VoiceActivationFolders _folders;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "vv-activate-" + Guid.NewGuid().ToString("N"));
            _config = Path.Combine(_folder, "Config");
            _folders = new VoiceActivationFolders(VoiceActivationFolders.RootIn(_folder), Path.Combine(_folder, "Shared"), Path.Combine(_folder, "scripts"));
            Directory.CreateDirectory(_folders.Scripts);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_folder, true);
        }

        [Test]
        public void AnEnvironmentInUse_StaysReady_ThroughAnUpdateThatStopsOrFails()
        {
            var first = new VoiceActivationRecord();
            VoiceActivationRecord started = VoiceLabActivation.After(first, VoiceActivationOutcome.Started, "1");
            Assert.That((started.State, started.InstallingVersion), Is.EqualTo((VoiceActivationState.Installing, "1")));
            Assert.That(VoiceLabActivation.After(started, VoiceActivationOutcome.Failed, "1").State, Is.EqualTo(VoiceActivationState.Failed));

            VoiceActivationRecord ready = VoiceLabActivation.After(started, VoiceActivationOutcome.Succeeded, "1");
            Assert.That((ready.State, ready.ComponentsVersion, ready.InstallingVersion), Is.EqualTo((VoiceActivationState.Ready, "1", (string)null)));

            VoiceActivationRecord updating = VoiceLabActivation.After(ready, VoiceActivationOutcome.Started, "2");
            Assert.That((updating.State, updating.ComponentsVersion, updating.InstallingVersion), Is.EqualTo((VoiceActivationState.Ready, "1", "2")));
            Assert.That(VoiceLabActivation.After(updating, VoiceActivationOutcome.Failed, "2").State, Is.EqualTo(VoiceActivationState.Ready));
            Assert.That(VoiceLabActivation.After(updating, VoiceActivationOutcome.Cancelled, "2").InstallingVersion, Is.EqualTo("2"), "It continues next time.");
        }

        [Test]
        public void ASuccessfulBuild_WritesTheEnvironmentFile_KeepsADevelopersAside_AndIsRemembered()
        {
            Directory.CreateDirectory(_config);
            string developer = new VoiceLabEnvironment(@"E:\work\venv\Scripts\python.exe", @"E:\repo\scripts", @"E:\work\rvc", @"E:\work\vox", @"E:\work\t.sentis", @"E:\work\t.json").ToJson();
            File.WriteAllText(Path.Combine(_config, VoiceLabService.EnvironmentFile), developer);

            VoiceLabEnvironment built = Activation().RunAsync(null, null, CancellationToken.None).Result;

            VoiceLabEnvironment written = VoiceLabEnvironment.Parse(File.ReadAllText(Path.Combine(_config, VoiceLabService.EnvironmentFile)), out _);
            Assert.That(written.Python, Is.EqualTo(built.Python));
            Assert.That(File.ReadAllText(Path.Combine(_config, VoiceLabActivation.DeveloperBackupFile)), Is.EqualTo(developer), "A developer's file is not lost.");
            VoiceLabActivation again = Activation();
            Assert.That((again.IsReady, again.UpdateAvailable, again.Record.ComponentsVersion), Is.EqualTo((true, false, "1")));
            Assert.That(VoiceLabEnvironmentRemover.Owned(written, _folders.Root), Is.Not.Null, "What the app built, it can delete.");
        }

        [Test]
        public void NothingIsBuilt_UntilTheTermsAreAgreedTo_AndANewVersionAsksAgain()
        {
            VoiceLabActivation fresh = Activation(agree: false);
            Assert.That(fresh.TermsAgreed, Is.False);
            Assert.That(() => fresh.RunAsync(null, null, CancellationToken.None).Wait(), Throws.InstanceOf<AggregateException>().With.InnerException.InstanceOf<InvalidOperationException>());
            Assert.That(File.Exists(Path.Combine(_config, VoiceLabService.EnvironmentFile)), Is.False);

            fresh.AgreeToTerms();
            Assert.That(fresh.TermsAgreed, Is.True);
            Assert.That(VoiceTerms.IsAgreed(fresh.Record, "2"), Is.False, "A new version is agreed to again.");
        }

        [Test]
        public void TheBundledTerms_HaveAVersion_InBothLanguages()
        {
            string scripts = Path.GetFullPath(Path.Combine("..", "..", "services", "voice-lab"));
            VoiceTerms ja = VoiceTerms.Load(scripts, "ja");
            VoiceTerms en = VoiceTerms.Load(scripts, "en");

            Assert.That(ja.Version, Is.EqualTo(en.Version), "Both languages say the same version.");
            Assert.That(ja.Text, Does.Contain("禁止事項"));
            Assert.That(VoiceTerms.Load(scripts, "fr").Version, Is.EqualTo(en.Version), "Another language reads English.");
            Assert.That(VoiceTerms.Parse("# no version"), Is.Null);
        }

        [Test]
        public void TheLocation_IsAFolderThatExists_AndCannotMoveUnderAnEnvironmentInUse()
        {
            VoiceLabActivation activation = Activation();
            Assert.That(activation.SetLocation(Path.Combine(_folder, "missing")).Error, Is.EqualTo(ProjectData.Records.CrudError.Invalid));
            Assert.That(activation.SetLocation("relative").Error, Is.EqualTo(ProjectData.Records.CrudError.Invalid));
            Assert.That(activation.SetLocation(VoiceActivationFolders.DefaultLocation).Ok, Is.True);
            Assert.That(activation.Record.Location, Is.Null, "The default is kept as none, so it follows the user's profile.");
            Assert.That(activation.SetLocation(_folder).Ok, Is.True);

            activation.RunAsync(null, null, CancellationToken.None).Wait();

            Assert.That(activation.SetLocation(Path.GetTempPath()).Error, Is.EqualTo(ProjectData.Records.CrudError.Busy));
            activation.ForgetEnvironment();
            Assert.That((activation.IsReady, activation.TermsAgreed, activation.Location), Is.EqualTo((false, true, _folder)), "Forgetting keeps the agreement and the place.");
        }

        [Test]
        public void AFailedBuild_LeavesNoEnvironmentFile()
        {
            VoiceLabActivation activation = Activation(failUv: true);

            Assert.That(() => activation.RunAsync(null, null, CancellationToken.None).Wait(), Throws.InstanceOf<AggregateException>());

            Assert.That(File.Exists(Path.Combine(_config, VoiceLabService.EnvironmentFile)), Is.False);
            Assert.That((activation.Record.State, activation.CanResume), Is.EqualTo((VoiceActivationState.Failed, true)));
        }

        private VoiceLabActivation Activation(bool failUv = false, bool agree = true)
        {
            // An empty list is not valid, so one small file stands for the components; the fake uv makes the rest.
            byte[] bytes = { 1, 2, 3 };
            string sha;
            using (var hasher = System.Security.Cryptography.SHA256.Create())
            {
                sha = string.Concat(Array.ConvertAll(hasher.ComputeHash(bytes), b => b.ToString("x2")));
            }

            string json = "{\"schemaVersion\":1,\"version\":\"1\",\"python\":\"3.12.2\",\"lock\":\"uv.lock\",\"components\":["
                + $"{{\"id\":\"rvc\",\"url\":\"https://source/rvc\",\"sha256\":\"{sha}\",\"size\":3,\"license\":\"MIT\",\"place\":\"environment\",\"target\":\"rvc/f\"}},"
                + $"{{\"id\":\"template\",\"url\":\"https://source/t\",\"sha256\":\"{sha}\",\"size\":3,\"license\":\"MIT\",\"place\":\"environment\",\"target\":\"template/t.sentis\"}},"
                + $"{{\"id\":\"template-manifest\",\"url\":\"https://source/m\",\"sha256\":\"{sha}\",\"size\":3,\"license\":\"MIT\",\"place\":\"environment\",\"target\":\"template/t.json\"}},"
                + $"{{\"id\":\"voxcpm2-config\",\"url\":\"https://source/v\",\"sha256\":\"{sha}\",\"size\":3,\"license\":\"MIT\",\"place\":\"environment\",\"target\":\"voxcpm2/config.json\"}}"
                + "]}";
            VoiceComponentManifest manifest = VoiceComponentManifest.Parse(json, out IReadOnlyList<string> problems);
            Assert.That(problems, Is.Empty);
            var activation = new VoiceLabActivation(_config, manifest, _folders.SharedModels, _folders.Scripts, f => new VoiceComponentInstaller(manifest, f, new Bytes(bytes), new Uv(failUv), null) { RetryDelay = TimeSpan.Zero }, "1", new FixedClock(), null);
            if (!activation.IsReady)
            {
                Assert.That(activation.SetLocation(_folder).Ok, Is.True);
            }

            Assert.That(activation.Folders.Root, Is.EqualTo(_folders.Root), "The app's folders inside the one chosen.");
            if (agree)
            {
                activation.AgreeToTerms();
            }

            return activation;
        }

        private sealed class Bytes : IComponentDownloader
        {
            private readonly byte[] _bytes;

            public Bytes(byte[] bytes)
            {
                _bytes = bytes;
            }

            public Task DownloadAsync(string url, string partPath, IProgress<long> received, CancellationToken cancellationToken)
            {
                File.WriteAllBytes(partPath, _bytes);
                return Task.CompletedTask;
            }
        }

        private sealed class Uv : IExternalProcessRunner
        {
            private readonly bool _fail;

            public Uv(bool fail)
            {
                _fail = fail;
            }

            public Task<ExternalProcessResult> RunAsync(ExternalProcessRequest request, Action<string> onOutputLine, CancellationToken cancellationToken)
            {
                if (request.Arguments[0] == "sync" && !_fail)
                {
                    string python = Path.Combine(request.Environment["UV_PROJECT_ENVIRONMENT"], "Scripts", "python.exe");
                    Directory.CreateDirectory(Path.GetDirectoryName(python));
                    File.WriteAllText(python, string.Empty);
                }

                ExternalProcessState state = _fail ? ExternalProcessState.Failed : ExternalProcessState.Completed;
                return Task.FromResult(new ExternalProcessResult(state, _fail ? 1 : 0, false, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null));
            }
        }
    }
}
