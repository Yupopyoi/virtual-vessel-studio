using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Core.Time;

namespace VirtualVessel.Avatar.Tests
{
    /// <summary>Registration and the avatar folders (avatar detailed design 6.1, 6.2) with a fake loader.</summary>
    public sealed class AvatarImportTests
    {
        private static readonly byte[] s_glb = { 0x67, 0x6C, 0x54, 0x46, 2, 0, 0, 0, 1, 2, 3, 4 };

        private string _root;
        private string _source;
        private AvatarStore _store;
        private FakeLoader _loader;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "vv-avatar-tests-" + Guid.NewGuid().ToString("N"));
            _source = Path.Combine(_root, "source");
            Directory.CreateDirectory(_source);
            _store = new AvatarStore(Path.Combine(_root, "Avatars"), new FixedClock());
            _loader = new FakeLoader();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        [Test]
        public async Task Import_CopiesTheFile_WritesTheProfileAndThumbnail_AndListsIt()
        {
            string file = WriteSource("Model.vrm", s_glb);
            AvatarImportResult result = await Importer().ImportAsync(file, Array.Empty<AvatarEntry>(), null, CancellationToken.None);

            Assert.That(result.Imported, Is.True);
            AvatarEntry entry = result.Entry;
            Assert.That(AvatarStore.IsId(entry.Id), Is.True);
            Assert.That(File.ReadAllBytes(_store.ModelPathOf(entry)), Is.EqualTo(s_glb));
            Assert.That(entry.Profile.SourceFileName, Is.EqualTo("Model.vrm"));
            Assert.That(entry.Profile.Sha256, Has.Length.EqualTo(64));
            Assert.That(entry.DisplayName, Is.EqualTo("Fake"));
            Assert.That(File.ReadAllBytes(entry.ThumbnailPath), Is.EqualTo(new byte[] { 9, 9 }));
            Assert.That(_loader.Disposed, Is.EqualTo(1), "The model loaded to check it is destroyed.");
            Assert.That(Directory.Exists(Path.Combine(_store.Directory, AvatarStore.ImportingFolder, entry.Id)), Is.False);
            Assert.That(File.Exists(file), Is.True, "The picked file is not changed.");

            IReadOnlyList<AvatarEntry> listed = _store.List(new List<string>());
            Assert.That(listed.Select(e => e.Id), Is.EqualTo(new[] { entry.Id }));
            Assert.That(listed[0].IsUsable, Is.True);
            Assert.That(listed[0].Profile.License.Authors, Is.EqualTo(new[] { "Author" }));
        }

        [Test]
        public async Task Import_TheSameFileTwice_PointsToTheFirst()
        {
            string file = WriteSource("a.vrm", s_glb);
            AvatarImportResult first = await Importer().ImportAsync(file, Array.Empty<AvatarEntry>(), null, CancellationToken.None);
            string renamed = WriteSource("b.vrm", s_glb);
            AvatarImportResult second = await Importer().ImportAsync(renamed, _store.List(null), null, CancellationToken.None);

            Assert.That(second.Problem, Is.EqualTo(AvatarProblem.AlreadyImported));
            Assert.That(second.Entry.Id, Is.EqualTo(first.Entry.Id));
            Assert.That(_store.List(null), Has.Count.EqualTo(1));
            AssertNoImportLeft();
        }

        [Test]
        public async Task Import_ANonVrmFile_IsRejectedBeforeCopying()
        {
            string file = WriteSource("a.vrm", new byte[] { 0x50, 0x4B, 0x03, 0x04, 0 });
            AvatarImportResult result = await Importer().ImportAsync(file, Array.Empty<AvatarEntry>(), null, CancellationToken.None);

            Assert.That(result.Problem, Is.EqualTo(AvatarProblem.NotVrm));
            Assert.That(_loader.Loads, Is.Zero);
            Assert.That(Directory.Exists(_store.Directory), Is.False, "Nothing was written.");
        }

        [Test]
        public async Task Import_AModelWithoutRequiredBones_IsRejected_AndLeavesNothing()
        {
            _loader.Missing = AvatarBone.LeftHand;
            AvatarImportResult result = await Importer().ImportAsync(WriteSource("a.vrm", s_glb), Array.Empty<AvatarEntry>(), null, CancellationToken.None);

            Assert.That(result.Problem, Is.EqualTo(AvatarProblem.MissingBones));
            Assert.That(_loader.Disposed, Is.EqualTo(1));
            Assert.That(_store.List(null), Is.Empty);
            AssertNoImportLeft();
        }

        [Test]
        public async Task Import_ALoadFailure_IsReported_AndLeavesNothing()
        {
            _loader.Failure = new AvatarLoadException(AvatarProblem.LoadFailed, "broken");
            AvatarImportResult result = await Importer().ImportAsync(WriteSource("a.vrm", s_glb), Array.Empty<AvatarEntry>(), null, CancellationToken.None);

            Assert.That(result.Problem, Is.EqualTo(AvatarProblem.LoadFailed));
            Assert.That(_store.List(null), Is.Empty);
            AssertNoImportLeft();
        }

        [Test]
        public async Task Import_ShowsTheConditions_AndDecliningRegistersNothing()
        {
            AvatarModelInfo shown = null;
            AvatarImportResult result = await Importer().ImportAsync(
                WriteSource("a.vrm", s_glb),
                Array.Empty<AvatarEntry>(),
                info =>
                {
                    shown = info;
                    return Task.FromResult(false);
                },
                CancellationToken.None);

            Assert.That(shown.License.Authors, Is.EqualTo(new[] { "Author" }));
            Assert.That(result.Declined, Is.True);
            Assert.That(result.Imported, Is.False);
            Assert.That(_store.List(null), Is.Empty);
            AssertNoImportLeft();
        }

        [Test]
        public async Task Import_Canceled_LeavesNothing()
        {
            // Awaited, not waited on: continuations run on the main thread, which a blocking wait would hold.
            using (var cancel = new CancellationTokenSource())
            {
                _loader.OnLoad = cancel.Cancel;
                bool canceled = false;
                try
                {
                    await Importer().ImportAsync(WriteSource("a.vrm", s_glb), Array.Empty<AvatarEntry>(), null, cancel.Token);
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                }

                Assert.That(canceled, Is.True);
            }

            Assert.That(_store.List(null), Is.Empty);
            AssertNoImportLeft();
        }

        [Test]
        public void List_AFolderWithoutAReadableProfile_IsListedAsNotUsable_AndOtherFoldersAreIgnored()
        {
            string id = AvatarStore.NewId();
            Directory.CreateDirectory(_store.FolderOf(id));
            File.WriteAllText(Path.Combine(_store.FolderOf(id), AvatarProfile.FileName), "{ not json");
            Directory.CreateDirectory(Path.Combine(_store.Directory, "my notes"));
            var warnings = new List<string>();

            IReadOnlyList<AvatarEntry> listed = _store.List(warnings);

            Assert.That(listed, Has.Count.EqualTo(1));
            Assert.That(listed[0].Id, Is.EqualTo(id));
            Assert.That(listed[0].IsUsable, Is.False);
            Assert.That(warnings, Is.Not.Empty);
            Assert.That(Directory.Exists(Path.Combine(_store.Directory, "my notes")), Is.True);
        }

        [Test]
        public async Task Profile_KeepsFieldsThisVersionDoesNotKnow()
        {
            AvatarImportResult result = await Importer().ImportAsync(WriteSource("a.vrm", s_glb), Array.Empty<AvatarEntry>(), null, CancellationToken.None);
            string file = Path.Combine(_store.FolderOf(result.Entry.Id), AvatarProfile.FileName);
            string text = File.ReadAllText(file);
            File.WriteAllText(file, text.Insert(text.LastIndexOf('}'), ",\n  \"extensions\": { \"later\": 1 }\n"));

            var store = new ProjectData.Config.ConfigFileStore<AvatarProfile>(_store.FolderOf(result.Entry.Id), AvatarProfile.FileName, AvatarProfile.SchemaVersion, () => new AvatarProfile(), new FixedClock());
            AvatarProfile profile = store.Load().Value;
            profile.DisplayName = "Renamed";
            store.Save(profile);

            Assert.That(File.ReadAllText(file), Does.Contain("\"later\": 1").And.Contain("Renamed"));
        }

        [Test]
        public async Task Delete_RemovesTheAvatarsFolder_AndCleanImports_RemovesLeftovers()
        {
            AvatarImportResult result = await Importer().ImportAsync(WriteSource("a.vrm", s_glb), Array.Empty<AvatarEntry>(), null, CancellationToken.None);
            Directory.CreateDirectory(_store.ImportFolderOf(AvatarStore.NewId()));

            _store.Delete(result.Entry.Id);
            _store.CleanImports();

            Assert.That(_store.List(null), Is.Empty);
            AssertNoImportLeft();
            Assert.That(() => _store.Delete("..\\.."), Throws.ArgumentException, "Only avatar folders are ever deleted.");
        }

        private AvatarImporter Importer()
        {
            return new AvatarImporter(_store, _loader, new FixedClock(), null);
        }

        private string WriteSource(string name, byte[] bytes)
        {
            string path = Path.Combine(_source, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private void AssertNoImportLeft()
        {
            string importing = Path.Combine(_store.Directory, AvatarStore.ImportingFolder);
            Assert.That(!Directory.Exists(importing) || Directory.GetFileSystemEntries(importing).Length == 0, Is.True, "The temporary folder is deleted.");
        }

        private sealed class FixedClock : ISystemClock
        {
            public DateTimeOffset UtcNow { get; } = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        }

        private sealed class FakeLoader : IAvatarLoader
        {
            public int Loads { get; private set; }

            public int Disposed { get; set; }

            public AvatarBone? Missing { get; set; }

            public Exception Failure { get; set; }

            public Action OnLoad { get; set; }

            public string Format => "vrm";

            public IReadOnlyList<string> Extensions { get; } = new[] { ".vrm" };

            public Task<IAvatarModel> LoadAsync(string path, CancellationToken cancellationToken)
            {
                Loads++;
                Assert.That(File.Exists(path), Is.True, "The copy is loaded.");
                OnLoad?.Invoke();
                cancellationToken.ThrowIfCancellationRequested();
                if (Failure != null)
                {
                    throw Failure;
                }

                var license = new AvatarLicense { Authors = { "Author" }, AvatarPermission = AvatarPermission.Everyone };
                var info = new AvatarModelInfo("Fake", "vrm", "1.0", false, license, new AvatarThumbnail(new byte[] { 9, 9 }, ".png"));
                return Task.FromResult<IAvatarModel>(new FakeModel(this, info, Missing));
            }
        }

        private sealed class FakeModel : IAvatarModel, IAvatarRig
        {
            private readonly FakeLoader _loader;
            private readonly AvatarBone? _missing;

            public FakeModel(FakeLoader loader, AvatarModelInfo info, AvatarBone? missing)
            {
                _loader = loader;
                Info = info;
                _missing = missing;
            }

            public Transform Root => null;

            public IAvatarRig Rig => this;

            public IAvatarExpressions Expressions => null;

            public IAvatarFaceShapes FaceShapes => null;

            public AvatarModelInfo Info { get; }

            public bool Has(AvatarBone bone)
            {
                return bone != _missing;
            }

            public float BoneLength(AvatarBone bone)
            {
                return 0;
            }

            public void SetRotation(AvatarBone bone, Quaternion rotation)
            {
            }

            public void SetRootPosition(Vector3 position)
            {
            }

            public void ResetPose()
            {
            }

            public void SetVisible(bool visible)
            {
            }

            public void Apply()
            {
            }

            public void Dispose()
            {
                _loader.Disposed++;
            }
        }
    }
}
