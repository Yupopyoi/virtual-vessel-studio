using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using VirtualVessel.Application.Build;
using VirtualVessel.Application.Hosting;
using VirtualVessel.Application.Logging;
using VirtualVessel.Application.Startup;
using VirtualVessel.Core.Threading;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ProjectData.DataRoot;

namespace VirtualVessel.Application.Tests
{
    /// <summary>
    /// Runs the real composition so that the logging hand-over is tested end to end.
    /// </summary>
    public sealed class ApplicationLoggingIntegrationTests
    {
        private string _tempDirectory;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        [Test]
        public void Composition_StartsWithRequiredLogging()
        {
            var descriptors = ApplicationComposition.Create(CreateContext(RuntimeEnvironment.Editor));

            Assert.That(descriptors[0].Name, Is.EqualTo(LoggingService.ServiceName));
            Assert.That(descriptors[0].Criticality, Is.EqualTo(ServiceCriticality.Required));
        }

        [TestCase(RuntimeEnvironment.Editor, "Editor")]
        [TestCase(RuntimeEnvironment.Player, "Application")]
        public void GetLogDirectory_SeparatesEditorAndPlayer(RuntimeEnvironment environment, string expectedFolder)
        {
            IDataRoot dataRoot = new DataRootResolver(Path.Combine(_tempDirectory, "LocalAppData")).Resolve(Path.Combine(_tempDirectory, "Data")).DataRoot;

            string directory = ApplicationComposition.GetLogDirectory(dataRoot, environment);

            Assert.That(Path.GetFileName(directory), Is.EqualTo(expectedFolder));
            Assert.That(Path.GetDirectoryName(directory), Is.EqualTo(dataRoot.GetDirectory(DataRootDirectory.Logs)));
        }

        [Test]
        public async Task Runtime_WithRealComposition_WritesFoundationLogsToFile()
        {
            var foundationLog = new BufferedApplicationLog(() => DateTimeOffset.UtcNow, null);
            var runtime = new ApplicationRuntime(
                new ApplicationStartupOptions(Path.Combine(_tempDirectory, "Data"), TimeSpan.FromSeconds(10)),
                new BuildInfo("0.0.0-test", UnityEngine.Application.unityVersion, null, "test"),
                RuntimeEnvironment.Editor,
                new DataRootResolver(Path.Combine(_tempDirectory, "LocalAppData")),
                new StopwatchMonotonicClock(),
                new UtcSystemClock(),
                new MainThreadDispatcher(),
                foundationLog,
                ApplicationComposition.Create);

            await runtime.StartAsync();
            Assert.That(runtime.State, Is.EqualTo(ApplicationState.Running));
            await runtime.StopAsync();

            string logDirectory = ApplicationComposition.GetLogDirectory(runtime.DataRoot, RuntimeEnvironment.Editor);
            string file = Directory.GetFiles(logDirectory, "*.jsonl").Single();
            string[] lines = File.ReadAllLines(file);

            Assert.That(lines[0], Does.Contain(runtime.Session.SessionId));
            Assert.That(lines.Any(l => l.Contains("\"mod\":\"Application\"") && l.Contains("Data Root:")), Is.True, "Entries from before logging started are replayed.");
            Assert.That(lines.Any(l => l.Contains("Application started in")), Is.True, "Entries after logging started are forwarded.");
            Assert.That(lines.Last(), Does.Contain("Logging stopped."));
        }

        private ApplicationCompositionContext CreateContext(RuntimeEnvironment environment)
        {
            IDataRoot dataRoot = new DataRootResolver(Path.Combine(_tempDirectory, "LocalAppData")).Resolve(Path.Combine(_tempDirectory, "Data")).DataRoot;
            return new ApplicationCompositionContext(
                dataRoot,
                new Session.SessionInfo(Session.SessionInfo.NewSessionId(), DateTimeOffset.UtcNow, null),
                new BuildInfo("0.0.0-test", "test", null, "test"),
                environment,
                new StopwatchMonotonicClock(),
                new UtcSystemClock(),
                new MainThreadDispatcher(),
                new BufferedApplicationLog(() => DateTimeOffset.UtcNow, null));
        }
    }
}
