using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using VirtualVessel.Application.Build;
using VirtualVessel.Application.Hosting;
using VirtualVessel.Application.Logging;
using VirtualVessel.Application.Session;
using VirtualVessel.Application.Startup;
using VirtualVessel.Core.Threading;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.DataRoot;

namespace VirtualVessel.Application.Tests
{
    public sealed class ApplicationRuntimeTests
    {
        private string _tempDirectory;
        private BufferedApplicationLog _log;
        private List<string> _journal;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
            _log = new BufferedApplicationLog(() => DateTimeOffset.UtcNow, null);
            _journal = new List<string>();
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
        public async Task Start_ResolvesDataRootCreatesSessionAndRuns()
        {
            ApplicationRuntime runtime = CreateRuntime(DataRootOverride());

            await runtime.StartAsync();

            Assert.That(runtime.State, Is.EqualTo(ApplicationState.Running));
            Assert.That(runtime.DataRoot.RootPath, Is.EqualTo(Path.GetFullPath(DataRootOverride())));
            Assert.That(runtime.Session.PreviousSessionEndedAbnormally, Is.False);
            Assert.That(File.Exists(MarkerPath()), Is.True);
        }

        [Test]
        public async Task Stop_DeletesSessionMarker()
        {
            ApplicationRuntime runtime = CreateRuntime(DataRootOverride());

            await runtime.StartAsync();
            await runtime.StopAsync();

            Assert.That(File.Exists(MarkerPath()), Is.False);
            Assert.That(runtime.State, Is.EqualTo(ApplicationState.Stopped));
        }

        [Test]
        public async Task Start_AfterRunThatDidNotStop_ReportsPreviousAbnormalExit()
        {
            ApplicationRuntime crashed = CreateRuntime(DataRootOverride());
            await crashed.StartAsync();
            string crashedSessionId = crashed.Session.SessionId;

            // The first runtime is never stopped, simulating a crash that leaves the marker behind.
            ApplicationRuntime next = CreateRuntime(DataRootOverride());
            await next.StartAsync();

            Assert.That(next.Session.PreviousSessionEndedAbnormally, Is.True);
            Assert.That(next.Session.Previous.SessionId, Is.EqualTo(crashedSessionId));
            Assert.That(next.Session.SessionId, Is.Not.EqualTo(crashedSessionId));
        }

        [Test]
        public async Task Start_AfterNormalStop_DoesNotReportAbnormalExit()
        {
            ApplicationRuntime first = CreateRuntime(DataRootOverride());
            await first.StartAsync();
            await first.StopAsync();

            ApplicationRuntime second = CreateRuntime(DataRootOverride());
            await second.StartAsync();

            Assert.That(second.Session.PreviousSessionEndedAbnormally, Is.False);
        }

        [Test]
        public async Task Start_CorruptedMarker_StillReportsAbnormalExit()
        {
            string logs = Path.Combine(DataRootOverride(), DataRootDirectory.Logs.ToString());
            Directory.CreateDirectory(logs);
            File.WriteAllText(Path.Combine(logs, SessionMarker.FileName), "{ broken");
            ApplicationRuntime runtime = CreateRuntime(DataRootOverride());

            await runtime.StartAsync();

            Assert.That(runtime.Session.PreviousSessionEndedAbnormally, Is.True);
            Assert.That(runtime.Session.Previous.SessionId, Is.Null);
        }

        [Test]
        public async Task Start_DataRootUnavailable_FailsWithoutStartingServices()
        {
            string fileInsteadOfDirectory = Path.Combine(_tempDirectory, "NotADirectory");
            File.WriteAllText(fileInsteadOfDirectory, "x");
            bool composed = false;
            ApplicationRuntime runtime = CreateRuntime(fileInsteadOfDirectory, _ =>
            {
                composed = true;
                return Array.Empty<ApplicationServiceDescriptor>();
            });

            await runtime.StartAsync();

            Assert.That(runtime.State, Is.EqualTo(ApplicationState.Failed));
            Assert.That(composed, Is.False);
            Assert.That(_log.Snapshot().Any(entry => entry.Level == ApplicationLogLevel.Error), Is.True);
        }

        [Test]
        public async Task Stop_AfterDataRootFailure_DoesNotThrow()
        {
            string fileInsteadOfDirectory = Path.Combine(_tempDirectory, "NotADirectory");
            File.WriteAllText(fileInsteadOfDirectory, "x");
            ApplicationRuntime runtime = CreateRuntime(fileInsteadOfDirectory);

            await runtime.StartAsync();

            Assert.DoesNotThrowAsync(() => runtime.StopAsync());
        }

        [Test]
        public async Task Start_CompositionThrows_Fails()
        {
            ApplicationRuntime runtime = CreateRuntime(DataRootOverride(), _ => throw new InvalidOperationException("bad composition"));

            await runtime.StartAsync();

            Assert.That(runtime.State, Is.EqualTo(ApplicationState.Failed));
        }

        [Test]
        public async Task Start_PassesContextToCompositionAndStartsComposedServices()
        {
            ApplicationCompositionContext captured = null;
            ApplicationRuntime runtime = CreateRuntime(DataRootOverride(), context =>
            {
                captured = context;
                return new[]
                {
                    new ApplicationServiceDescriptor("A", ServiceCriticality.Required, () => new FakeService("A", _journal)),
                };
            });

            await runtime.StartAsync();

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured.DataRoot, Is.SameAs(runtime.DataRoot));
            Assert.That(captured.Session, Is.SameAs(runtime.Session));
            Assert.That(_journal, Is.EqualTo(new[] { "init:A" }));
        }

        [Test]
        public async Task SynchronousStop_StopsServicesAndDeletesMarker()
        {
            ApplicationRuntime runtime = CreateRuntime(DataRootOverride(), _ => new[]
            {
                new ApplicationServiceDescriptor("A", ServiceCriticality.Required, () => new FakeService("A", _journal)),
            });

            await runtime.StartAsync();
            runtime.Stop();

            Assert.That(_journal, Does.Contain("shutdown:A"));
            Assert.That(File.Exists(MarkerPath()), Is.False);
        }

        private string DataRootOverride()
        {
            return Path.Combine(_tempDirectory, "Data");
        }

        private string MarkerPath()
        {
            return Path.Combine(DataRootOverride(), DataRootDirectory.Logs.ToString(), SessionMarker.FileName);
        }

        private ApplicationRuntime CreateRuntime(
            string dataRootOverride,
            Func<ApplicationCompositionContext, IReadOnlyList<ApplicationServiceDescriptor>> compose = null)
        {
            var options = new ApplicationStartupOptions(dataRootOverride, TimeSpan.FromSeconds(10));
            var buildInfo = new BuildInfo("0.0.0-test", "test", null, "test");

            return new ApplicationRuntime(
                options,
                buildInfo,
                RuntimeEnvironment.Editor,
                new DataRootResolver(Path.Combine(_tempDirectory, "LocalAppData")),
                new StopwatchMonotonicClock(),
                new UtcSystemClock(),
                new MainThreadDispatcher(),
                _log,
                compose ?? (_ => Array.Empty<ApplicationServiceDescriptor>()));
        }
    }
}
