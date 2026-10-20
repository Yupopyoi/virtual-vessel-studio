using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VirtualVessel.UI.Credits;
using VirtualVessel.UI.Screens;

namespace VirtualVessel.UI.Tests
{
    /// <summary>The credits read from the SBOM and the shipped license files (docs/&lt;lang&gt;/ui/screens/setup.md 8).</summary>
    public sealed class CreditTests
    {
        private const string Sbom = @"{
            ""bomFormat"": ""CycloneDX"",
            ""metadata"": { ""component"": { ""bom-ref"": ""app"", ""name"": ""App"", ""version"": ""0.1.0"",
                ""licenses"": [ { ""license"": { ""id"": ""Apache-2.0"" } } ],
                ""properties"": [ { ""name"": ""virtual-vessel-studio:license-file"", ""value"": ""LICENSE"" } ] } },
            ""components"": [
                { ""name"": ""Plugin"", ""version"": ""1.2"", ""scope"": ""required"", ""supplier"": { ""name"": ""Someone"" },
                  ""licenses"": [ { ""license"": { ""id"": ""MIT"" } }, { ""license"": { ""name"": ""Custom terms"" } } ],
                  ""properties"": [
                    { ""name"": ""virtual-vessel-studio:license-file"", ""value"": ""licenses/a.md"" },
                    { ""name"": ""virtual-vessel-studio:license-file"", ""value"": ""licenses/a.md"" },
                    { ""name"": ""other"", ""value"": ""x"" } ],
                  ""components"": [ { ""name"": ""Inside"", ""licenses"": [ { ""expression"": ""BSD-3-Clause OR MIT"" } ] } ] },
                { ""name"": ""Tool"", ""scope"": ""excluded"", ""components"": [ { ""name"": ""Tool part"", ""scope"": ""required"" } ] },
                { ""name"": ""Font"", ""scope"": ""optional"" }
            ] }";

        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_directory, recursive: true);
        }

        [Test]
        public void Parse_ListsTheApplication_ThenShippedComponents_WithWhatIsBuiltIntoThem()
        {
            CreditCatalog catalog = CreditCatalog.Parse(Sbom);

            Assert.That(catalog.Application.Name, Is.EqualTo("App"));
            Assert.That(catalog.Application.LicenseFiles, Is.EqualTo(new[] { "LICENSE" }));
            Assert.That(catalog.Components.Select(c => (c.Name, c.Depth)), Is.EqualTo(new[] { ("Plugin", 0), ("Inside", 1), ("Font", 0) }), "Development-only components and their parts are left out.");

            CreditEntry plugin = catalog.Components[0];
            Assert.That(plugin.Version, Is.EqualTo("1.2"));
            Assert.That(plugin.Supplier, Is.EqualTo("Someone"));
            Assert.That(plugin.License, Is.EqualTo("MIT, Custom terms"));
            Assert.That(plugin.LicenseFiles, Is.EqualTo(new[] { "licenses/a.md" }), "Only the license-file property, each file once.");
            Assert.That(catalog.Components[1].License, Is.EqualTo("BSD-3-Clause OR MIT"));
            Assert.That(catalog.Components[1].Version, Is.Null);
            Assert.That(catalog.Components[2].License, Is.Null);
        }

        [TestCase("{ \"bomFormat\": \"SPDX\" }")]
        [TestCase("{ \"bomFormat\": \"CycloneDX\", \"components\": 3 }")]
        [TestCase("not json")]
        public void Parse_RejectsWhatIsNotACycloneDxSbom(string json)
        {
            Assert.Throws<FormatException>(() => CreditCatalog.Parse(json));
        }

        [TestCase("licenses/a.md", true)]
        [TestCase("LICENSE", true)]
        [TestCase("../secret.txt", false)]
        [TestCase("licenses/../../x", false)]
        [TestCase("/etc/passwd", false)]
        [TestCase("C:/Windows/win.ini", false)]
        [TestCase("licenses\\a.md", false)]
        [TestCase("licenses//a.md", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void OnlyPathsInsideTheFolder_AreRead(string path, bool safe)
        {
            Assert.That(CreditFiles.IsSafeRelativePath(path), Is.EqualTo(safe));
        }

        [Test]
        public void ForPackage_ReadsNextToTheApplication_AndNothingOutsideIt()
        {
            string package = Path.Combine(_directory, "app");
            Directory.CreateDirectory(Path.Combine(package, "licenses"));
            File.WriteAllText(Path.Combine(package, "licenses", "a.md"), "MIT text");
            File.WriteAllText(Path.Combine(_directory, "outside.txt"), "private");
            CreditFiles files = CreditFiles.ForPackage(package, null);

            Assert.That(files.ReadText("licenses/a.md"), Is.EqualTo("MIT text"));
            Assert.That(files.ReadText("licenses/missing.md"), Is.Null);
            Assert.That(files.ReadText("../outside.txt"), Is.Null);
        }

        [Test]
        public void ForRepository_ReadsTheSourcesOfTheList_MatchingAPackageHash()
        {
            Directory.CreateDirectory(Path.Combine(_directory, "tools", "release"));
            File.WriteAllText(Path.Combine(_directory, CreditFiles.LicenseFileListPath), @"{ ""files"": {
                ""LICENSE"": ""LICENSE"",
                ""licenses/pkg.md"": ""Library/PackageCache/com.example@*/LICENSE.md"",
                ""licenses/two.md"": ""Two/*/LICENSE.md"" } }");
            File.WriteAllText(Path.Combine(_directory, "LICENSE"), "Apache text");
            Directory.CreateDirectory(Path.Combine(_directory, "Library", "PackageCache", "com.example@1a2b"));
            File.WriteAllText(Path.Combine(_directory, "Library", "PackageCache", "com.example@1a2b", "LICENSE.md"), "Package text");
            Directory.CreateDirectory(Path.Combine(_directory, "Two", "a"));
            Directory.CreateDirectory(Path.Combine(_directory, "Two", "b"));
            CreditFiles files = CreditFiles.ForRepository(_directory, null);

            Assert.That(files.ReadText("LICENSE"), Is.EqualTo("Apache text"));
            Assert.That(files.ReadText("licenses/pkg.md"), Is.EqualTo("Package text"));
            Assert.That(files.ReadText("licenses/two.md"), Is.Null, "A '*' that matches two folders is ambiguous.");
            Assert.That(files.ReadText("licenses/unlisted.md"), Is.Null);
        }

        [Test]
        public void TheRepositorysSbom_NamesFilesThatTheEditorCanRead()
        {
            string repository = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "..", ".."));
            CreditFiles files = CreditFiles.ForRepository(repository, null);
            CreditCatalog catalog = CreditCatalog.Parse(files.ReadText(CreditFiles.SbomPath));

            Assert.That(catalog.Application.LicenseFiles, Does.Contain("LICENSE"));
            bool mediaPipeFetched = Directory.Exists(Path.Combine(UnityEngine.Application.dataPath, "..", "Packages", "com.github.homuler.mediapipe"));
            foreach (string file in catalog.Components.Prepend(catalog.Application).SelectMany(c => c.LicenseFiles).Distinct())
            {
                if (!mediaPipeFetched && file.StartsWith("licenses/MediaPipeUnityPlugin", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.That(files.ReadText(file), Is.Not.Null.And.Not.Empty, file);
            }
        }

        [Test]
        public void TheCard_SaysSo_WhenTheSbomCannotBeRead()
        {
            Assert.That(new CreditsCard(new MapFiles()).Catalog, Is.Null);
            Assert.That(new CreditsCard(new MapFiles { [CreditFiles.SbomPath] = "{ }" }).Catalog, Is.Null);
        }

        [Test]
        public void Details_JoinThePartsTheSbomGives()
        {
            Assert.That(CreditsCard.Details(new CreditEntry("A", "1.0", "Someone", "MIT", null, 0)), Is.EqualTo("1.0 · Someone · MIT"));
            Assert.That(CreditsCard.Details(new CreditEntry("A", null, null, "MIT", null, 0)), Is.EqualTo("MIT"));
            Assert.That(CreditsCard.Details(new CreditEntry("A", null, null, null, null, 0)), Is.Empty);
        }

        [Test]
        public void Split_BreaksBetweenLines_AndCutsOnlyOverlongLines()
        {
            Assert.That(CreditText.Split("ab\r\ncd\nef\n", 5), Is.EqualTo(new[] { "ab\ncd", "ef" }));
            Assert.That(CreditText.Split("a\n\nb", 10), Is.EqualTo(new[] { "a\n\nb" }), "Blank lines between paragraphs stay.");
            Assert.That(CreditText.Split("x\nabcdefgh", 3), Is.EqualTo(new[] { "x", "abc", "def", "gh" }));
            Assert.That(CreditText.Split(string.Empty), Is.Empty);
            Assert.That(CreditText.Split(new string('a', 20000)).Max(part => part.Length), Is.LessThanOrEqualTo(CreditText.MaxCharactersPerPart));
        }

        private sealed class MapFiles : System.Collections.Generic.Dictionary<string, string>, ICreditFiles
        {
            public string ReadText(string packagePath)
            {
                return TryGetValue(packagePath, out string text) ? text : null;
            }
        }
    }
}
