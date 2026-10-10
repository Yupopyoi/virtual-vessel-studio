using NUnit.Framework;
using VirtualVessel.Application.Startup;

namespace VirtualVessel.Application.Tests
{
    public sealed class ApplicationStartupOptionsTests
    {
        [Test]
        public void FromEnvironment_CommandLineArgument_SetsDataRoot()
        {
            ApplicationStartupOptions options = ApplicationStartupOptions.FromEnvironment(
                new[] { "app.exe", "--data-root", "D:\\Data" },
                _ => null);

            Assert.That(options.DataRootOverride, Is.EqualTo("D:\\Data"));
        }

        [Test]
        public void FromEnvironment_EnvironmentVariable_SetsDataRoot()
        {
            ApplicationStartupOptions options = ApplicationStartupOptions.FromEnvironment(
                new[] { "app.exe" },
                name => name == ApplicationStartupOptions.DataRootEnvironmentVariable ? "E:\\Data" : null);

            Assert.That(options.DataRootOverride, Is.EqualTo("E:\\Data"));
        }

        [Test]
        public void FromEnvironment_CommandLineWinsOverEnvironment()
        {
            ApplicationStartupOptions options = ApplicationStartupOptions.FromEnvironment(
                new[] { "app.exe", "--data-root", "D:\\FromArgument" },
                _ => "E:\\FromEnvironment");

            Assert.That(options.DataRootOverride, Is.EqualTo("D:\\FromArgument"));
        }

        [Test]
        public void FromEnvironment_ArgumentWithoutValue_IsIgnored()
        {
            ApplicationStartupOptions options = ApplicationStartupOptions.FromEnvironment(
                new[] { "app.exe", "--data-root" },
                _ => null);

            Assert.That(options.DataRootOverride, Is.Null);
        }

        [Test]
        public void FromEnvironment_Nothing_UsesDefaults()
        {
            ApplicationStartupOptions options = ApplicationStartupOptions.FromEnvironment(null, null);

            Assert.That(options.DataRootOverride, Is.Null);
            Assert.That(options.ShutdownTimeout, Is.EqualTo(ApplicationStartupOptions.DefaultShutdownTimeout));
        }
    }
}
