using NUnit.Framework;
using VirtualVessel.Application.Hosting;
using VirtualVessel.Application.Startup;
using VirtualVessel.UI;

namespace VirtualVessel.Application.Tests
{
    public sealed class StartupProblemPolicyTests
    {
        [Test]
        public void Decide_DataRootFailure_ShowsTheDataRootScreen()
        {
            Assert.That(StartupProblemPolicy.Decide(ApplicationState.Failed, dataRootUnavailable: true, uiState: null), Is.EqualTo(StartupProblemKind.DataRootUnavailable));
        }

        [Test]
        public void Decide_RequiredServiceFailure_ShowsTheGeneralScreen()
        {
            Assert.That(StartupProblemPolicy.Decide(ApplicationState.Failed, false, null), Is.EqualTo(StartupProblemKind.StartupFailed));
        }

        [TestCase(ApplicationServiceState.Failed)]
        [TestCase(ApplicationServiceState.Skipped)]
        public void Decide_RunningWithoutUi_ShowsTheGeneralScreen(ApplicationServiceState uiState)
        {
            Assert.That(StartupProblemPolicy.Decide(ApplicationState.Running, false, uiState), Is.EqualTo(StartupProblemKind.StartupFailed));
        }

        [Test]
        public void Decide_RunningWithUi_ShowsNothing()
        {
            Assert.That(StartupProblemPolicy.Decide(ApplicationState.Running, false, ApplicationServiceState.Running), Is.Null);
        }

        [Test]
        public void Decide_ShutdownDuringStartup_ShowsNothing()
        {
            Assert.That(StartupProblemPolicy.Decide(ApplicationState.ShuttingDown, false, null), Is.Null);
        }
    }
}
