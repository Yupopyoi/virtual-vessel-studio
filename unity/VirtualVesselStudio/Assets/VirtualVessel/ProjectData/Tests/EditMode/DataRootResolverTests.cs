using System;
using System.IO;
using NUnit.Framework;
using VirtualVessel.ProjectData.DataRoot;

namespace VirtualVessel.ProjectData.Tests
{
    public sealed class DataRootResolverTests
    {
        private string _tempDirectory;
        private DataRootResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
            _resolver = new DataRootResolver(_tempDirectory);
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
        public void Resolve_NoOverrideNoBootstrap_UsesDefaultAndCreatesFoundationDirectories()
        {
            DataRootResolution resolution = _resolver.Resolve(null);

            Assert.That(resolution.Source, Is.EqualTo(DataRootSource.Default));
            Assert.That(resolution.DataRoot.RootPath, Is.EqualTo(Path.GetFullPath(_resolver.DefaultDataRootPath)));
            Assert.That(Directory.Exists(resolution.DataRoot.GetDirectory(DataRootDirectory.Logs)), Is.True);
            Assert.That(Directory.Exists(resolution.DataRoot.GetDirectory(DataRootDirectory.Cache)), Is.True);
            Assert.That(resolution.Warnings, Is.Empty);
        }

        [Test]
        public void Resolve_Override_TakesPrecedenceOverBootstrap()
        {
            string bootstrapRoot = Path.Combine(_tempDirectory, "FromBootstrap");
            WriteBootstrap($"{{\"schemaVersion\":1,\"dataRoot\":\"{Escape(bootstrapRoot)}\"}}");
            string overrideRoot = Path.Combine(_tempDirectory, "FromOverride");

            DataRootResolution resolution = _resolver.Resolve(overrideRoot);

            Assert.That(resolution.Source, Is.EqualTo(DataRootSource.Override));
            Assert.That(resolution.DataRoot.RootPath, Is.EqualTo(Path.GetFullPath(overrideRoot)));
        }

        [Test]
        public void Resolve_BootstrapWithDataRoot_UsesBootstrap()
        {
            string bootstrapRoot = Path.Combine(_tempDirectory, "FromBootstrap");
            WriteBootstrap($"{{\"schemaVersion\":1,\"dataRoot\":\"{Escape(bootstrapRoot)}\"}}");

            DataRootResolution resolution = _resolver.Resolve(null);

            Assert.That(resolution.Source, Is.EqualTo(DataRootSource.BootstrapFile));
            Assert.That(resolution.DataRoot.RootPath, Is.EqualTo(Path.GetFullPath(bootstrapRoot)));
        }

        [Test]
        public void Resolve_BootstrapWithoutDataRoot_UsesDefaultWithoutWarning()
        {
            WriteBootstrap("{\"schemaVersion\":1}");

            DataRootResolution resolution = _resolver.Resolve(null);

            Assert.That(resolution.Source, Is.EqualTo(DataRootSource.Default));
            Assert.That(resolution.Warnings, Is.Empty);
        }

        [Test]
        public void Resolve_CorruptedBootstrap_UsesDefaultWarnsAndKeepsFile()
        {
            const string Corrupted = "{ this is not json";
            WriteBootstrap(Corrupted);

            DataRootResolution resolution = _resolver.Resolve(null);

            Assert.That(resolution.Source, Is.EqualTo(DataRootSource.Default));
            Assert.That(resolution.Warnings, Is.Not.Empty);
            Assert.That(File.ReadAllText(_resolver.BootstrapFilePath), Is.EqualTo(Corrupted));
        }

        [Test]
        public void Resolve_NewerSchemaVersion_UsesDataRootAndWarns()
        {
            string bootstrapRoot = Path.Combine(_tempDirectory, "FromBootstrap");
            WriteBootstrap($"{{\"schemaVersion\":99,\"dataRoot\":\"{Escape(bootstrapRoot)}\",\"futureField\":true}}");

            DataRootResolution resolution = _resolver.Resolve(null);

            Assert.That(resolution.Source, Is.EqualTo(DataRootSource.BootstrapFile));
            Assert.That(resolution.Warnings, Has.Count.EqualTo(1));
        }

        [Test]
        public void Resolve_RootIsAnExistingFile_ThrowsDataRootUnavailable()
        {
            // A file at the root path makes directory creation fail, standing in for an unwritable location.
            string fileInsteadOfDirectory = Path.Combine(_tempDirectory, "NotADirectory");
            File.WriteAllText(fileInsteadOfDirectory, "x");

            var exception = Assert.Throws<DataRootUnavailableException>(() => _resolver.Resolve(fileInsteadOfDirectory));

            Assert.That(exception.Path, Is.EqualTo(Path.GetFullPath(fileInsteadOfDirectory)));
        }

        [Test]
        public void Resolve_InvalidPathCharacters_ThrowsDataRootUnavailable()
        {
            Assert.Throws<DataRootUnavailableException>(() => _resolver.Resolve("C:\\invalid|path"));
        }

        [Test]
        public void Resolve_DoesNotLeaveWriteProbeBehind()
        {
            DataRootResolution resolution = _resolver.Resolve(null);

            string cache = resolution.DataRoot.GetDirectory(DataRootDirectory.Cache);
            Assert.That(Directory.GetFiles(cache), Is.Empty);
        }

        private void WriteBootstrap(string json)
        {
            Directory.CreateDirectory(_resolver.ApplicationDataDirectory);
            File.WriteAllText(_resolver.BootstrapFilePath, json);
        }

        private static string Escape(string path)
        {
            return path.Replace("\\", "\\\\");
        }
    }
}
