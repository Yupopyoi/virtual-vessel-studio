using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using VirtualVessel.Application.Hosting;
using VirtualVessel.Application.Logging;
using VirtualVessel.Core.Time;

namespace VirtualVessel.Application.Tests
{
    public sealed class ApplicationHostTests
    {
        private static readonly TimeSpan s_shortTimeout = TimeSpan.FromMilliseconds(100);

        private List<string> _journal;
        private BufferedApplicationLog _log;

        [SetUp]
        public void SetUp()
        {
            _journal = new List<string>();
            _log = new BufferedApplicationLog(() => DateTimeOffset.UtcNow, null);
        }

        [Test]
        public async Task Start_ThenStop_InitializesInOrderAndShutsDownInReverse()
        {
            ApplicationHost host = CreateHost(
                Describe("A", ServiceCriticality.Required),
                Describe("B", ServiceCriticality.Optional),
                Describe("C", ServiceCriticality.Optional));

            await host.StartAsync();
            await host.StopAsync();

            Assert.That(_journal, Is.EqualTo(new[]
            {
                "init:A", "init:B", "init:C",
                "shutdown:C", "shutdown:B", "shutdown:A",
                "dispose:C", "dispose:B", "dispose:A",
            }));
            Assert.That(host.State, Is.EqualTo(ApplicationState.Stopped));
        }

        [Test]
        public async Task Start_AllSucceed_IsRunning()
        {
            ApplicationHost host = CreateHost(Describe("A", ServiceCriticality.Required));

            await host.StartAsync();

            Assert.That(host.State, Is.EqualTo(ApplicationState.Running));
            Assert.That(host.Services.Single().State, Is.EqualTo(ApplicationServiceState.Running));
        }

        [Test]
        public async Task Start_RequiredFails_IsFailedAndStopsStartingLaterServices()
        {
            ApplicationHost host = CreateHost(
                Describe("A", ServiceCriticality.Optional),
                Describe("Required", ServiceCriticality.Required, FakeBehavior.Throw),
                Describe("Later", ServiceCriticality.Optional));

            await host.StartAsync();

            Assert.That(host.State, Is.EqualTo(ApplicationState.Failed));
            Assert.That(_journal, Does.Not.Contain("init:Later"));
            Assert.That(StatusOf(host, "Later").State, Is.EqualTo(ApplicationServiceState.NotCreated));
        }

        [Test]
        public async Task Stop_AfterRequiredFailure_StopsStartedServicesAndDisposesAllCreated()
        {
            ApplicationHost host = CreateHost(
                Describe("A", ServiceCriticality.Optional),
                Describe("Required", ServiceCriticality.Required, FakeBehavior.Throw));

            await host.StartAsync();
            await host.StopAsync();

            Assert.That(_journal, Does.Contain("shutdown:A"));
            Assert.That(_journal, Does.Not.Contain("shutdown:Required"));
            Assert.That(_journal, Does.Contain("dispose:Required"));
            Assert.That(_journal, Does.Contain("dispose:A"));
        }

        [Test]
        public async Task Start_OptionalFails_ContinuesInDegradedRunningState()
        {
            ApplicationHost host = CreateHost(
                Describe("Broken", ServiceCriticality.Optional, FakeBehavior.Throw),
                Describe("Healthy", ServiceCriticality.Optional));

            await host.StartAsync();

            Assert.That(host.State, Is.EqualTo(ApplicationState.Running));
            Assert.That(StatusOf(host, "Broken").State, Is.EqualTo(ApplicationServiceState.Failed));
            Assert.That(StatusOf(host, "Broken").FailureReason, Does.Contain("Simulated failure"));
            Assert.That(StatusOf(host, "Healthy").State, Is.EqualTo(ApplicationServiceState.Running));
        }

        [Test]
        public async Task Start_DependencyFailed_SkipsDependentService()
        {
            ApplicationHost host = CreateHost(
                Describe("Base", ServiceCriticality.Optional, FakeBehavior.Throw),
                Describe("Dependent", ServiceCriticality.Optional, dependsOn: new[] { "Base" }));

            await host.StartAsync();

            Assert.That(StatusOf(host, "Dependent").State, Is.EqualTo(ApplicationServiceState.Skipped));
            Assert.That(_journal, Does.Not.Contain("init:Dependent"));
        }

        [Test]
        public async Task Start_InitializeTimesOut_MarksFailedAndContinues()
        {
            ApplicationHost host = CreateHost(
                Describe("Hangs", ServiceCriticality.Optional, FakeBehavior.NeverComplete, timeout: s_shortTimeout),
                Describe("After", ServiceCriticality.Optional));

            await host.StartAsync();

            Assert.That(StatusOf(host, "Hangs").State, Is.EqualTo(ApplicationServiceState.Failed));
            Assert.That(StatusOf(host, "Hangs").FailureReason, Does.Contain("timed out"));
            Assert.That(StatusOf(host, "After").State, Is.EqualTo(ApplicationServiceState.Running));
        }

        [Test]
        public async Task Start_FactoryThrows_MarksFailed()
        {
            var descriptor = new ApplicationServiceDescriptor("Broken", ServiceCriticality.Optional, () => throw new InvalidOperationException("no"));
            ApplicationHost host = CreateHost(descriptor);

            await host.StartAsync();

            Assert.That(StatusOf(host, "Broken").State, Is.EqualTo(ApplicationServiceState.Failed));
            Assert.That(host.State, Is.EqualTo(ApplicationState.Running));
        }

        [Test]
        public async Task Stop_ShutdownThrows_StillShutsDownOthersAndDisposesAll()
        {
            ApplicationHost host = CreateHost(
                Describe("A", ServiceCriticality.Optional),
                Describe("Throws", ServiceCriticality.Optional, shutdownBehavior: FakeBehavior.Throw));

            await host.StartAsync();
            await host.StopAsync();

            Assert.That(_journal, Does.Contain("shutdown:A"));
            Assert.That(_journal.Count(entry => entry.StartsWith("dispose:", StringComparison.Ordinal)), Is.EqualTo(2));
            Assert.That(_log.Snapshot().Any(entry => entry.Level == ApplicationLogLevel.Error), Is.True);
        }

        [Test]
        public async Task Stop_ShutdownHangs_RespectsOverallTimeout()
        {
            ApplicationHost host = CreateHost(
                s_shortTimeout,
                Describe("Hangs", ServiceCriticality.Optional, shutdownBehavior: FakeBehavior.NeverComplete));

            await host.StartAsync();
            Task stop = host.StopAsync();
            Task finished = await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.That(finished, Is.SameAs(stop), "Shutdown should give up after its timeout.");
            Assert.That(host.State, Is.EqualTo(ApplicationState.Stopped));
            Assert.That(_journal, Does.Contain("dispose:Hangs"));
        }

        [Test]
        public async Task SynchronousStop_ShutsDownInReverseAndDisposes()
        {
            ApplicationHost host = CreateHost(
                Describe("A", ServiceCriticality.Required),
                Describe("B", ServiceCriticality.Optional));

            await host.StartAsync();
            host.Stop();

            Assert.That(_journal.Skip(2), Is.EqualTo(new[] { "shutdown:B", "shutdown:A", "dispose:B", "dispose:A" }));
            Assert.That(host.State, Is.EqualTo(ApplicationState.Stopped));
        }

        [Test]
        public async Task Stop_CalledTwice_RunsOnce()
        {
            var services = new List<FakeService>();
            ApplicationHost host = CreateHost(Describe("A", ServiceCriticality.Required, created: services));

            await host.StartAsync();
            await host.StopAsync();
            await host.StopAsync();

            Assert.That(services.Single().DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task Stop_CancelsStoppingTokenButGivesShutdownAFreshToken()
        {
            var services = new List<FakeService>();
            ApplicationHost host = CreateHost(Describe("A", ServiceCriticality.Required, created: services));

            await host.StartAsync();
            await host.StopAsync();

            FakeService service = services.Single();
            Assert.That(service.InitializeToken.IsCancellationRequested, Is.True);
            Assert.That(service.ShutdownToken, Is.Not.EqualTo(service.InitializeToken));
        }

        [Test]
        public void Start_Twice_Throws()
        {
            ApplicationHost host = CreateHost();
            host.StartAsync().GetAwaiter().GetResult();

            Assert.Throws<InvalidOperationException>(() => host.StartAsync().GetAwaiter().GetResult());
        }

        [Test]
        public void Constructor_DuplicateNames_Throws()
        {
            Assert.Throws<ArgumentException>(() => CreateHost(
                Describe("A", ServiceCriticality.Optional),
                Describe("A", ServiceCriticality.Optional)));
        }

        private ApplicationHost CreateHost(params ApplicationServiceDescriptor[] descriptors)
        {
            return CreateHost(TimeSpan.FromSeconds(10), descriptors);
        }

        private ApplicationHost CreateHost(TimeSpan shutdownTimeout, params ApplicationServiceDescriptor[] descriptors)
        {
            return new ApplicationHost(descriptors, new StopwatchMonotonicClock(), _log, shutdownTimeout);
        }

        private ApplicationServiceDescriptor Describe(
            string name,
            ServiceCriticality criticality,
            FakeBehavior initializeBehavior = FakeBehavior.Succeed,
            FakeBehavior shutdownBehavior = FakeBehavior.Succeed,
            string[] dependsOn = null,
            TimeSpan? timeout = null,
            List<FakeService> created = null)
        {
            return new ApplicationServiceDescriptor(
                name,
                criticality,
                () =>
                {
                    var service = new FakeService(name, _journal, initializeBehavior, shutdownBehavior);
                    created?.Add(service);
                    return service;
                },
                dependsOn,
                timeout);
        }

        private static ApplicationServiceStatus StatusOf(ApplicationHost host, string name)
        {
            return host.Services.Single(status => status.Name == name);
        }
    }
}
