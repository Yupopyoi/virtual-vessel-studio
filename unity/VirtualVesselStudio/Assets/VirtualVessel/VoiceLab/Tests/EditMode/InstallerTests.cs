using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VirtualVessel.ExternalServices.Process;
using VirtualVessel.VoiceLab.Activation;
using VirtualVessel.VoiceLab.Scripts;

namespace VirtualVessel.VoiceLab.Tests
{
    /// <summary>Building the training environment from the download list (Voice Lab detailed design 8.1).</summary>
    public sealed class InstallerTests
    {
        private string _folder;
        private VoiceActivationFolders _folders;
        private FakeDownloader _downloader;
        private FakeUv _uv;
        private Dictionary<string, byte[]> _files;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "vv-activation-" + Guid.NewGuid().ToString("N"));
            _folders = new VoiceActivationFolders(Path.Combine(_folder, "External"), Path.Combine(_folder, "VoiceModels", "Shared", "1"), Path.Combine(_folder, "scripts"));
            _downloader = new FakeDownloader();
            _uv = new FakeUv();
            _files = new Dictionary<string, byte[]>
            {
                ["uv"] = Zip(("uv.exe", "uv")),
                ["rvc"] = Zip(("Retrieval-abc/infer/train.py", "train"), ("Retrieval-abc/../../evil.txt", "evil")),
                ["template"] = Bytes("template"),
                ["template-manifest"] = Bytes("{}"),
                ["voxcpm2-config"] = Bytes("vox"),
                ["shared-rmvpe"] = Bytes("rmvpe"),
            };
            foreach (KeyValuePair<string, byte[]> file in _files)
            {
                _downloader.Served["https://source/" + file.Key] = file.Value;
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        [Test]
        public void AWholeBuild_PlacesEveryPart_AndRunsUvInItsOwnFolder()
        {
            VoiceLabEnvironment environment = Installer().InstallAsync(null, null, CancellationToken.None).Result;

            string version = _folders.Version("1");
            Assert.That(File.ReadAllText(Path.Combine(version, "rvc", "infer", "train.py")), Is.EqualTo("train"), "The zip's top folder is dropped.");
            Assert.That(Directory.GetFiles(_folder, "evil.txt", SearchOption.AllDirectories), Is.Empty, "An entry leaving its folder is skipped.");
            Assert.That(File.ReadAllText(Path.Combine(_folders.SharedModels, "rmvpe_32.sentis")), Is.EqualTo("rmvpe"), "Streaming's model goes beside the voices.");
            Assert.That(environment.Python, Is.EqualTo(Path.Combine(version, "venv", "Scripts", "python.exe")));
            Assert.That(environment.Problems(File.Exists, Directory.Exists), Is.Empty);

            Assert.That(_uv.Calls.Select(c => c.Arguments[0]), Is.EqualTo(new[] { "python", "sync" }));
            Assert.That(_uv.Calls[0].FileName, Is.EqualTo(Path.Combine(version, "uv", "uv.exe")));
            Assert.That(_uv.Calls[1].Arguments, Does.Contain("--frozen").And.Contain("--no-dev"));
            Assert.That(_uv.Calls[1].Environment["UV_PROJECT_ENVIRONMENT"], Is.EqualTo(Path.Combine(version, "venv")));
            Assert.That(_uv.Calls[1].Environment["UV_PYTHON_PREFERENCE"], Is.EqualTo("only-managed"), "No Python from elsewhere on the PC.");
        }

        [Test]
        public void ADownloadThatStopped_Continues_AndACheckedOneIsNotFetchedAgain()
        {
            string part = Path.Combine(_folders.Downloads, Sha(_files["template"]) + ".part");
            Directory.CreateDirectory(_folders.Downloads);
            File.WriteAllBytes(part, _files["template"].Take(3).ToArray());

            Installer().InstallAsync(null, null, CancellationToken.None).Wait();
            Assert.That(_downloader.StartedFrom["https://source/template"], Is.EqualTo(3), "It continues where it stopped.");

            _downloader.Requests.Clear();
            Installer().InstallAsync(null, null, CancellationToken.None).Wait();
            Assert.That(_downloader.Requests, Is.Empty, "Kept under its hash, nothing is downloaded again.");
        }

        [Test]
        public void ADroppedDownload_IsTriedAgain_FromWhereItStopped()
        {
            // The first two tries send part of the file and drop, as a connection does over a large file.
            _downloader.DropsLeft["https://source/template"] = 2;

            Installer().InstallAsync(null, null, CancellationToken.None).Wait();

            Assert.That(_downloader.Requests.Count(r => r == "https://source/template"), Is.EqualTo(3));
            Assert.That(_downloader.StartedFrom["https://source/template"], Is.GreaterThan(0L), "The last try continued.");
        }

        [Test]
        public void AConnectionFailure_IsToldFromABug_WhateverTheRuntimeCallsIt()
        {
            Assert.That(HttpComponentDownloader.IsConnectionFailure(new System.Net.WebException("aborted")), Is.True, "Unity's runtime.");
            Assert.That(HttpComponentDownloader.IsConnectionFailure(new IOException("reset")), Is.True);
            Assert.That(HttpComponentDownloader.IsConnectionFailure(new System.Net.Http.HttpRequestException("refused")), Is.True);
            Assert.That(HttpComponentDownloader.IsConnectionFailure(new NullReferenceException()), Is.False);
            // ERROR_DISK_FULL: the build must say to free space, not retry the download.
            Assert.That(HttpComponentDownloader.IsConnectionFailure(new IOException("full", unchecked((int)0x80070070))), Is.False);
        }

        [Test]
        public void AServerThatStopsSending_CountsAsAStoppedDownload()
        {
            // A local server sends the headers and a little, then nothing.
            var listener = new System.Net.HttpListener();
            int port = 50000 + new Random().Next(10000);
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            Task serve = Task.Run(async () =>
            {
                System.Net.HttpListenerContext context = await listener.GetContextAsync();
                context.Response.ContentLength64 = 1000;
                await context.Response.OutputStream.WriteAsync(new byte[10], 0, 10);
                await context.Response.OutputStream.FlushAsync();
                await Task.Delay(TimeSpan.FromSeconds(10));
            });
            try
            {
                using var downloader = new HttpComponentDownloader(TimeSpan.FromSeconds(1));
                string part = Path.Combine(_folder, "stall.part");
                var started = DateTime.UtcNow;
                AggregateException thrown = Assert.Throws<AggregateException>(() => downloader.DownloadAsync($"http://127.0.0.1:{port}/f", part, null, CancellationToken.None).Wait());
                Assert.That(thrown.InnerException, Is.InstanceOf<ComponentDownloadException>());
                Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(8)), "Not waiting for the server.");
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public void Failures_SayWhatKind()
        {
            _downloader.Served["https://source/template"] = Bytes("tampered");
            Assert.That(Failure(), Is.EqualTo(VoiceActivationFailure.Corrupt));

            _downloader.Served.Remove("https://source/template");
            Assert.That(Failure(), Is.EqualTo(VoiceActivationFailure.Network));

            _downloader.Served["https://source/template"] = _files["template"];
            _uv.FailOn = "sync";
            Assert.That(Failure(), Is.EqualTo(VoiceActivationFailure.Setup));

            _uv.FailOn = null;
            Assert.That(Failure((_, __) => Task.FromResult("no GPU")), Is.EqualTo(VoiceActivationFailure.Verify));
        }

        [Test]
        public void CleaningUp_KeepsTheVersionInUseAndTheLogs()
        {
            Installer().InstallAsync(null, null, CancellationToken.None).Wait();
            Directory.CreateDirectory(_folders.Version("0"));

            Installer().CleanUp("1");

            Assert.That(Directory.GetDirectories(_folders.Root).Select(Path.GetFileName), Is.EquivalentTo(new[] { "1", "logs" }));
        }

        [Test]
        public void ZipEntries_DropTheirTopFolders_AndNeverLeaveTheTarget()
        {
            Assert.That(VoiceComponentInstaller.ZipEntryPath(@"C:\t", "top/a/b.py", 1), Is.EqualTo(@"C:\t\a\b.py"));
            Assert.That(VoiceComponentInstaller.ZipEntryPath(@"C:\t", "top/", 1), Is.Null);
            Assert.That(VoiceComponentInstaller.ZipEntryPath(@"C:\t", "top/../../x", 1), Is.Null);
            Assert.That(VoiceComponentInstaller.ZipEntryPath(@"C:\t", "C:/x", 0), Is.Null);
        }

        private VoiceActivationFailure? Failure(Func<VoiceLabEnvironment, CancellationToken, Task<string>> verify = null)
        {
            try
            {
                Installer().InstallAsync(verify, null, CancellationToken.None).Wait();
                return null;
            }
            catch (AggregateException exception) when (exception.InnerException is VoiceActivationException failed)
            {
                return failed.Failure;
            }
        }

        private VoiceComponentInstaller Installer()
        {
            string Component(string id, string place, string target, bool zip = false, int strip = 0)
            {
                string extract = zip ? ",\"extract\":\"zip\",\"strip\":" + strip : string.Empty;
                return $"{{\"id\":\"{id}\",\"url\":\"https://source/{id}\",\"sha256\":\"{Sha(_files[id])}\",\"size\":{_files[id].Length},\"license\":\"MIT\",\"place\":\"{place}\",\"target\":\"{target}\"{extract}}}";
            }

            string json = "{\"schemaVersion\":1,\"version\":\"1\",\"python\":\"3.12.2\",\"lock\":\"uv.lock\",\"components\":["
                + string.Join(",", Component("uv", "environment", "uv", zip: true), Component("rvc", "environment", "rvc", zip: true, strip: 1),
                    Component("template", "environment", "template/t.sentis"), Component("template-manifest", "environment", "template/t.json"),
                    Component("voxcpm2-config", "environment", "voxcpm2/config.json"), Component("shared-rmvpe", "sharedModels", "rmvpe_32.sentis"))
                + "]}";
            VoiceComponentManifest manifest = VoiceComponentManifest.Parse(json, out IReadOnlyList<string> problems);
            Assert.That(problems, Is.Empty);
            Directory.CreateDirectory(_folders.Scripts);
            return new VoiceComponentInstaller(manifest, _folders, _downloader, _uv, null) { RetryDelay = TimeSpan.Zero };
        }

        private static byte[] Bytes(string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        private static string Sha(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
        }

        private static byte[] Zip(params (string Name, string Text)[] entries)
        {
            using var memory = new MemoryStream();
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                foreach ((string name, string text) in entries)
                {
                    using Stream stream = archive.CreateEntry(name).Open();
                    byte[] bytes = Bytes(text);
                    stream.Write(bytes, 0, bytes.Length);
                }
            }

            return memory.ToArray();
        }

        private sealed class FakeDownloader : IComponentDownloader
        {
            public Dictionary<string, byte[]> Served { get; } = new Dictionary<string, byte[]>();

            public List<string> Requests { get; } = new List<string>();

            public Dictionary<string, long> StartedFrom { get; } = new Dictionary<string, long>();

            /// <summary>Tries left that send half of what remains and then drop.</summary>
            public Dictionary<string, int> DropsLeft { get; } = new Dictionary<string, int>();

            public Task DownloadAsync(string url, string partPath, IProgress<long> received, CancellationToken cancellationToken)
            {
                Requests.Add(url);
                if (!Served.TryGetValue(url, out byte[] bytes))
                {
                    throw new ComponentDownloadException(url + " is not there.");
                }

                long have = File.Exists(partPath) ? new FileInfo(partPath).Length : 0L;
                StartedFrom[url] = have;
                using var file = new FileStream(partPath, FileMode.Append);
                int from = (int)Math.Min(have, bytes.Length);
                int rest = (int)Math.Max(0, bytes.Length - have);
                if (DropsLeft.TryGetValue(url, out int drops) && drops > 0)
                {
                    DropsLeft[url] = drops - 1;
                    file.Write(bytes, from, Math.Max(1, rest / 2));
                    throw new ComponentDownloadException(url + " dropped.");
                }

                file.Write(bytes, from, rest);
                return Task.CompletedTask;
            }
        }

        // Stands in for uv: installing Python makes its folder, syncing makes the venv's python.exe.
        private sealed class FakeUv : IExternalProcessRunner
        {
            public List<ExternalProcessRequest> Calls { get; } = new List<ExternalProcessRequest>();

            public string FailOn { get; set; }

            public Task<ExternalProcessResult> RunAsync(ExternalProcessRequest request, Action<string> onOutputLine, CancellationToken cancellationToken)
            {
                Calls.Add(request);
                string step = request.Arguments[0];
                if (step == FailOn)
                {
                    return Task.FromResult(new ExternalProcessResult(ExternalProcessState.Failed, 2, false, "failed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new[] { "error: no match" }));
                }

                if (step == "sync")
                {
                    string python = Path.Combine(request.Environment["UV_PROJECT_ENVIRONMENT"], "Scripts", "python.exe");
                    Directory.CreateDirectory(Path.GetDirectoryName(python));
                    File.WriteAllText(python, "python");
                }

                return Task.FromResult(new ExternalProcessResult(ExternalProcessState.Completed, 0, false, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Array.Empty<string>()));
            }
        }
    }
}
