using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ExternalServices.Process;

namespace VirtualVessel.ExternalServices.Tests
{
    /// <summary>
    /// Real processes from Windows' own folder: cmd.exe for output and exit codes, PowerShell for a child to stop
    /// (external services detailed design 14).
    /// </summary>
    public sealed class ExternalProcessRunnerTests
    {
        private static readonly string s_system = Environment.SystemDirectory;
        private static readonly string s_cmd = Path.Combine(s_system, "cmd.exe");
        private static readonly string s_powerShell = Path.Combine(s_system, "WindowsPowerShell", "v1.0", "powershell.exe");

        private string _folder;
        private TestLog _log;
        private ExternalProcessRunner _runner;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "vv-process-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            _log = new TestLog();
            _runner = new ExternalProcessRunner(new[] { s_system }, _log, new UtcSystemClock());
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_folder, true);
        }

        [Test]
        public void Run_DeliversEachLineAndTheExitCode()
        {
            var lines = new ConcurrentQueue<string>();

            ExternalProcessResult result = Run(Cmd("echo one& echo two& echo oops 1>&2& exit /b 3"), lines.Enqueue);

            Assert.That(result.State, Is.EqualTo(ExternalProcessState.Failed));
            Assert.That(result.ExitCode, Is.EqualTo(3));
            Assert.That(lines.Select(l => l.Trim()), Is.EqualTo(new[] { "one", "two" }));
            Assert.That(result.LastErrorLines.Select(l => l.Trim()), Is.EqualTo(new[] { "oops" }));
            string log = File.ReadAllText(Path.Combine(_folder, "run.log"));
            Assert.That(log, Does.Contain("one").And.Contain("oops").And.Contain("end Failed, exit code 3"));
        }

        [Test]
        public void Run_ExitZeroCompletes()
        {
            ExternalProcessResult result = Run(Cmd("exit /b 0"), null);

            Assert.That(result.State, Is.EqualTo(ExternalProcessState.Completed));
            Assert.That(result.ExitCode, Is.EqualTo(0));
        }

        [Test]
        public void Run_PassesEnvironmentAndKeepsSecretsOutOfLogs()
        {
            var lines = new ConcurrentQueue<string>();
            ExternalProcessRequest request = Cmd("echo %VV_PLAIN%& echo %VV_TOKEN%");
            request.Environment = new Dictionary<string, string> { ["VV_PLAIN"] = "visible", ["VV_TOKEN"] = "s3cret" };
            request.SecretVariables = new[] { "VV_TOKEN" };

            Run(request, lines.Enqueue);

            Assert.That(lines.Select(l => l.Trim()), Is.EqualTo(new[] { "visible", "s3cret" }));
            Assert.That(_log.Text, Does.Contain("VV_PLAIN=visible").And.Not.Contain("s3cret"));
            Assert.That(File.ReadAllLines(Path.Combine(_folder, "run.log")).Where(l => l.Contains("environment")), Has.All.Not.Contain("s3cret"));
        }

        [Test]
        public void Run_RefusesAProgramOutsideTheAllowedFolders()
        {
            var runner = new ExternalProcessRunner(new[] { _folder }, _log, new UtcSystemClock());

            ExternalProcessResult result = runner.RunAsync(Cmd("exit /b 0"), null, CancellationToken.None).Result;

            Assert.That(result.State, Is.EqualTo(ExternalProcessState.Failed));
            Assert.That(result.ExitCode, Is.Null);
            Assert.That(result.Failure, Does.Contain("outside"));
            Assert.That(File.Exists(Path.Combine(_folder, "run.log")), Is.False);
        }

        [Test]
        public void Cancel_StopsTheProcessAndItsChildren()
        {
            var lines = new BlockingCollection<string>();
            var request = new ExternalProcessRequest(s_powerShell, new[]
            {
                "-NoProfile", "-NonInteractive", "-Command",
                "$c = Start-Process -FilePath ping.exe -ArgumentList '-n','60','127.0.0.1' -NoNewWindow -PassThru; Write-Output \"id $($c.Id)\"; Start-Sleep -Seconds 60",
            }, _folder, Path.Combine(_folder, "run.log")) { CancelGrace = TimeSpan.FromMilliseconds(200) };
            using var cancel = new CancellationTokenSource();

            Task<ExternalProcessResult> run = _runner.RunAsync(request, lines.Add, cancel.Token);
            // The child shares the output, so its own lines may come first.
            string childId = null;
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (childId == null && lines.TryTake(out string line, deadline - DateTime.UtcNow > TimeSpan.Zero ? deadline - DateTime.UtcNow : TimeSpan.Zero))
            {
                childId = line.StartsWith("id ") ? line.Substring(3) : null;
            }

            Assert.That(childId, Is.Not.Null, "The child's ID never came.");
            int child = int.Parse(childId.Trim());
            Assert.That(IsRunning(child), Is.True);
            cancel.Cancel();
            ExternalProcessResult result = run.Result;

            Assert.That(result.State, Is.EqualTo(ExternalProcessState.Cancelled));
            Assert.That(result.ExitCode, Is.Null);
            Assert.That(WaitUntilGone(child), Is.True, "The child outlived the cancelled run.");
        }

        [Test]
        public void Timeout_StopsTheRunAsFailed()
        {
            ExternalProcessRequest request = Cmd("ping -n 30 127.0.0.1 > nul");
            request.Timeout = TimeSpan.FromMilliseconds(500);
            request.CancelGrace = TimeSpan.Zero;

            ExternalProcessResult result = Run(request, null);

            Assert.That(result.State, Is.EqualTo(ExternalProcessState.Failed));
            Assert.That(result.TimedOut, Is.True);
            Assert.That((result.EndedAt - result.StartedAt).TotalSeconds, Is.LessThan(10));
        }

        [Test]
        public void Run_LeftoverChildrenDoNotHoldTheRunOpen()
        {
            // The parent exits at once; its child keeps the output pipe. The run still ends, and the child with it.
            var lines = new ConcurrentQueue<string>();
            var request = new ExternalProcessRequest(s_powerShell, new[]
            {
                "-NoProfile", "-NonInteractive", "-Command",
                "$c = Start-Process -FilePath ping.exe -ArgumentList '-n','60','127.0.0.1' -NoNewWindow -PassThru; Write-Output \"id $($c.Id)\"; exit 0",
            }, _folder, Path.Combine(_folder, "run.log"));

            ExternalProcessResult result = Run(request, lines.Enqueue);

            Assert.That(result.State, Is.EqualTo(ExternalProcessState.Completed));
            Assert.That((result.EndedAt - result.StartedAt).TotalSeconds, Is.LessThan(30));
            int child = int.Parse(lines.First(l => l.StartsWith("id ")).Substring(3).Trim());
            Assert.That(WaitUntilGone(child), Is.True);
        }

        [Test]
        public void Refusal_ChecksPlaceFileAndFolder()
        {
            var request = new ExternalProcessRequest(@"C:\Tools\run.exe", null, @"C:\Work", @"C:\Logs\run.log");
            string[] allowed = { @"C:\Tools" };

            Assert.That(ExternalProcessRunner.Refusal(request, allowed, _ => true, _ => true), Is.Null);
            Assert.That(ExternalProcessRunner.Refusal(request, new[] { @"C:\Other" }, _ => true, _ => true), Does.Contain("outside"));
            Assert.That(ExternalProcessRunner.Refusal(request, allowed, _ => false, _ => true), Does.Contain("does not exist"));
            Assert.That(ExternalProcessRunner.Refusal(request, allowed, _ => true, _ => false), Does.Contain("working folder"));
        }

        private ExternalProcessRequest Cmd(string command)
        {
            return new ExternalProcessRequest(s_cmd, new[] { "/d", "/s", "/c", command }, _folder, Path.Combine(_folder, "run.log"));
        }

        private ExternalProcessResult Run(ExternalProcessRequest request, Action<string> onLine)
        {
            Task<ExternalProcessResult> run = _runner.RunAsync(request, onLine, CancellationToken.None);
            Assert.That(run.Wait(TimeSpan.FromSeconds(60)), Is.True, "The run did not end.");
            return run.Result;
        }

        private static bool IsRunning(int id)
        {
            try
            {
                using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(id);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool WaitUntilGone(int id)
        {
            for (int i = 0; i < 50; i++)
            {
                if (!IsRunning(id))
                {
                    return true;
                }

                Thread.Sleep(100);
            }

            return false;
        }

        private sealed class TestLog : ILog
        {
            private readonly ConcurrentQueue<string> _entries = new ConcurrentQueue<string>();

            public string Text => string.Join("\n", _entries);

            public bool IsEnabled(LogLevel level)
            {
                return true;
            }

            public void Write(LogLevel level, string message, Exception exception = null)
            {
                _entries.Enqueue(message);
            }

            public void Write(LogLevel level, string message, IReadOnlyList<LogProperty> properties, Exception exception = null)
            {
                _entries.Enqueue(message + " " + string.Join(" ", (properties ?? Array.Empty<LogProperty>()).Select(p => $"{p.Key}={p.Value}")));
            }

            public ILog WithContext(string key, string value)
            {
                return this;
            }
        }
    }
}
