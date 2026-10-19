using NUnit.Framework;
using VirtualVessel.Diagnostics.Performance;

namespace VirtualVessel.Diagnostics.Tests
{
    public sealed class AppLoadSamplerTests
    {
        [Test]
        public void Compute_GivesFramesPerSecond_AndTheProcessShareOfAllCores()
        {
            AppLoad load = AppLoadSampler.Compute(seconds: 2, frames: 120, cpuSeconds: 1.6, cores: 8, gpuFrameMilliseconds: 4);

            Assert.That(load.FramesPerSecond, Is.EqualTo(60).Within(1e-9));
            Assert.That(load.CpuPercent, Is.EqualTo(10).Within(1e-9), "1.6 s of 16 core-seconds.");
            Assert.That(load.GpuPercent, Is.EqualTo(24).Within(1e-9), "4 ms of a 16.7 ms frame.");
        }

        [Test]
        public void Compute_SaysWhenTheGpuIsUnknown_AndStaysWithinRange()
        {
            Assert.That(double.IsNaN(AppLoadSampler.Compute(1, 60, 0.1, 4, double.NaN).GpuPercent), Is.True);
            Assert.That(AppLoadSampler.Compute(1, 60, 10, 1, 100).CpuPercent, Is.EqualTo(100));
            Assert.That(AppLoadSampler.Compute(1, 60, 10, 1, 100).GpuPercent, Is.EqualTo(100));
            Assert.That(AppLoadSampler.Compute(0, 0, 0, 4, 1).FramesPerSecond, Is.EqualTo(0), "No time, no rate.");
        }
    }
}
