using NUnit.Framework;
using VirtualVessel.Application.Build;
using VirtualVessel.Application.Editor;

namespace VirtualVessel.Application.Tests
{
    public sealed class BuildInfoTests
    {
        [TestCase("v0.2.0+3f2a1bc", "v0.2.0+3f2a1bc")]
        [TestCase("  v0.2.0+3f2a1bc\r\n", "v0.2.0+3f2a1bc")]
        [TestCase("", null)]
        [TestCase(null, null)]
        [TestCase("v0.2.0 ; drop", null)]
        public void ParseBuildId_AcceptsOnlyPlainIdentifiers(string text, string expected)
        {
            Assert.That(BuildInfo.ParseBuildId(text), Is.EqualTo(expected));
        }

        [Test]
        public void ParseBuildId_RejectsAnOverlongFile()
        {
            Assert.That(BuildInfo.ParseBuildId(new string('a', 101)), Is.Null);
        }

        [Test]
        public void NoIdentifier_IsADevelopmentBuild()
        {
            Assert.That(new BuildInfo("0.1.0", "6000.6.4f1", BuildInfo.ParseBuildId(null), "Windows").BuildIdentifier, Is.EqualTo(BuildInfo.DevelopmentBuildIdentifier));
        }

        [Test]
        public void ReleaseBuildArguments_AreReadByName()
        {
            string[] args = { "Unity.exe", "-vvOutput", "out", "-vvVersion" };

            Assert.That(ReleaseBuild.Argument(args, "-vvOutput"), Is.EqualTo("out"));
            Assert.That(ReleaseBuild.Argument(args, "-vvVersion"), Is.Null, "No value after it.");
            Assert.That(ReleaseBuild.Argument(args, "-vvBuildId"), Is.Null);
        }
    }
}
