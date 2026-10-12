using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VirtualVessel.ProjectData.Tests
{
    /// <summary>
    /// Newtonsoft.Json is referenced automatically by every assembly, so the module boundary
    /// (CLAUDE.md 5) is checked here: only ProjectData's settings storage may use it.
    /// </summary>
    public sealed class JsonLibraryBoundaryTests
    {
        [Test]
        public void NewtonsoftJson_IsUsedOnlyByProjectDataConfig()
        {
            string root = Path.Combine(UnityEngine.Application.dataPath, "VirtualVessel");
            string[] allowed =
            {
                Path.Combine(root, "ProjectData", "Runtime", "Config"),
                Path.Combine(root, "ProjectData", "Tests"),
            };

            string[] offenders = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(file => !allowed.Any(dir => Path.GetFullPath(file).StartsWith(Path.GetFullPath(dir))))
                .Where(file => File.ReadAllText(file).Contains("Newtonsoft"))
                .Select(file => file.Substring(root.Length + 1))
                .ToArray();

            Assert.That(offenders, Is.Empty, "Use ProjectData's ConfigFileStore/ConfigDocument instead of Newtonsoft.Json directly.");
        }
    }
}
