using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.ProjectData.Projects;

namespace VirtualVessel.ProjectData.Tests
{
    /// <summary>Opening the project at startup (project data detailed design 7, 8).</summary>
    public sealed class ProjectOpenerTests
    {
        private static readonly LegacySettings s_legacy = new LegacySettings(
            new Dictionary<ProfileKind, string> { [ProfileKind.Tracking] = "tracking-settings.json", [ProfileKind.Capture] = "capture-settings.json" },
            new[] { "avatar-settings.json", "stage-settings.json" });

        private string _root;
        private ProjectFolders _folders;
        private FixedClock _clock;
        private int _ids;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _folders = new ProjectFolders(Path.Combine(_root, "Config"), Path.Combine(_root, "Data", "Projects"), Path.Combine(_root, "Data", "Profiles"));
            _clock = new FixedClock(new DateTimeOffset(2026, 10, 27, 1, 0, 0, TimeSpan.Zero));
            _ids = 0;
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
        public void TheFirstStart_MakesAProject_AndTheNextOpensIt()
        {
            OpenedProject first = Open();

            Assert.That(first.Created, Is.True);
            Assert.That(first.ProjectId, Is.EqualTo(Id(1)));
            Assert.That(first.Manifest.TrackingProfileId, Is.EqualTo(Id(2)));
            Assert.That(first.Manifest.CaptureProfileId, Is.EqualTo(Id(3)));
            Assert.That(first.Manifest.StreamingProfileId, Is.EqualTo(Id(4)));
            Assert.That(first.Manifest.DisplayName, Is.EqualTo(ProjectOpener.DefaultDisplayName));
            Assert.That(first.Manifest.CreatedAt, Is.EqualTo("2026-10-27T01:00:00.0000000Z"));
            Assert.That(File.Exists(Path.Combine(first.Directory, ProjectManifest.FileName)), Is.True);
            Assert.That(first.ProfileDirectory(ProfileKind.Tracking), Is.EqualTo(Path.Combine(_folders.ProfilesDirectory, "Tracking")));
            Assert.That(first.ProfileFileName(ProfileKind.Capture), Is.EqualTo(Id(3) + ".json"));

            OpenedProject again = Open();
            Assert.That(again.Created, Is.False);
            Assert.That(again.ProjectId, Is.EqualTo(first.ProjectId), "app-settings.json names it.");
            Assert.That(again.Manifest.TrackingProfileId, Is.EqualTo(first.Manifest.TrackingProfileId));
        }

        [Test]
        public void TheOldSettings_AreCopiedIntoTheFirstProject_AndKept()
        {
            Directory.CreateDirectory(_folders.ConfigDirectory);
            foreach (string name in new[] { "tracking-settings.json", "capture-settings.json", "avatar-settings.json", "stage-settings.json" })
            {
                File.WriteAllText(Path.Combine(_folders.ConfigDirectory, name), $"{{ \"schemaVersion\": 1, \"from\": \"{name}\" }}");
            }

            OpenedProject project = Open();

            string tracking = Path.Combine(project.ProfileDirectory(ProfileKind.Tracking), project.ProfileFileName(ProfileKind.Tracking));
            Assert.That(File.ReadAllText(tracking), Does.Contain("tracking-settings.json"));
            Assert.That(File.ReadAllText(Path.Combine(project.ProfileDirectory(ProfileKind.Capture), project.ProfileFileName(ProfileKind.Capture))), Does.Contain("capture-settings.json"));
            Assert.That(File.ReadAllText(Path.Combine(project.Directory, "avatar-settings.json")), Does.Contain("avatar-settings.json"));
            Assert.That(File.ReadAllText(Path.Combine(project.Directory, "stage-settings.json")), Does.Contain("stage-settings.json"));
            Assert.That(File.Exists(Path.Combine(_folders.ConfigDirectory, "tracking-settings.json")), Is.True, "The original stays as a backup.");
            Assert.That(project.Notes.Count(note => note.StartsWith("Copied", StringComparison.Ordinal)), Is.EqualTo(4));

            // A change saved in the project is not overwritten by the old file at the next start.
            File.WriteAllText(tracking, "{ \"schemaVersion\": 1, \"from\": \"profile\" }");
            Open();
            Assert.That(File.ReadAllText(tracking), Does.Contain("profile"));
        }

        [Test]
        public void AMissingCurrentProject_OpensAnExistingOne_AndOnlyTheFirstTakesTheOldSettings()
        {
            OpenedProject first = Open();
            File.WriteAllText(Path.Combine(_folders.ConfigDirectory, AppSettings.FileName), $"{{ \"schemaVersion\": 1, \"currentProjectId\": \"{new string('f', 32)}\" }}");

            OpenedProject opened = Open();

            Assert.That(opened.ProjectId, Is.EqualTo(first.ProjectId), "The project that is there, not a new one.");
            Assert.That(opened.Notes, Has.Some.Contains("was not found"));
            Assert.That(ProjectOpener.Choose(null, new[] { Id(9), Id(4) }), Is.EqualTo(Id(4)), "The first in ordinal order.");
            Assert.That(ProjectOpener.Choose(Id(9), new[] { Id(9), Id(4) }), Is.EqualTo(Id(9)));
            Assert.That(ProjectOpener.Choose(Id(9), Array.Empty<string>()), Is.Null);
        }

        [Test]
        public void AProjectFromBeforeStreamingProfiles_GetsOne()
        {
            OpenedProject first = Open();
            string manifest = Path.Combine(first.Directory, ProjectManifest.FileName);
            File.WriteAllText(manifest, $"{{ \"schemaVersion\": 1, \"projectId\": \"{first.ProjectId}\", \"displayName\": \"Default\", \"trackingProfileId\": \"{Id(2)}\", \"captureProfileId\": \"{Id(3)}\" }}");

            OpenedProject opened = Open();

            Assert.That(ProjectOpener.IsValidId(opened.Manifest.StreamingProfileId), Is.True);
            Assert.That(opened.Manifest.TrackingProfileId, Is.EqualTo(Id(2)), "The others are kept.");
            Assert.That(File.ReadAllText(manifest), Does.Contain(opened.Manifest.StreamingProfileId), "Saved.");
            Assert.That(opened.ProfileFileName(ProfileKind.Streaming), Is.EqualTo(opened.Manifest.StreamingProfileId + ".json"));
        }

        [Test]
        public void IDsFromFiles_NameFoldersOnlyWhenTheyAreHexadecimal()
        {
            // Settings files are untrusted input (CLAUDE.md 26): an ID like "../x" must not reach a path.
            OpenedProject first = Open();
            string manifest = Path.Combine(first.Directory, ProjectManifest.FileName);
            File.WriteAllText(manifest, $"{{ \"schemaVersion\": 1, \"projectId\": \"other\", \"trackingProfileId\": \"..\\\\..\\\\escape\", \"captureProfileId\": \"{Id(3)}\" }}");

            OpenedProject repaired = Open();

            Assert.That(repaired.ProjectId, Is.EqualTo(first.ProjectId), "Named by its folder.");
            Assert.That(ProjectOpener.IsValidId(repaired.Manifest.TrackingProfileId), Is.True);
            Assert.That(repaired.Manifest.TrackingProfileId, Is.Not.EqualTo(first.Manifest.TrackingProfileId), "A new profile instead.");
            Assert.That(repaired.Manifest.CaptureProfileId, Is.EqualTo(Id(3)), "A usable ID is kept.");
            Assert.That(repaired.Manifest.DisplayName, Is.EqualTo(ProjectOpener.DefaultDisplayName));
            Assert.That(repaired.Notes, Has.Some.Contains("no usable Tracking profile ID"));

            Directory.CreateDirectory(Path.Combine(_folders.ProjectsDirectory, "not-an-id"));
            File.WriteAllText(Path.Combine(_folders.ProjectsDirectory, "not-an-id", ProjectManifest.FileName), "{ \"schemaVersion\": 1 }");
            File.Delete(Path.Combine(_folders.ConfigDirectory, AppSettings.FileName));
            Assert.That(Open().ProjectId, Is.EqualTo(first.ProjectId), "A folder that is not an ID is not a project.");

            Assert.That(ProjectOpener.IsValidId(Id(1)), Is.True);
            Assert.That(ProjectOpener.IsValidId(Id(10).ToUpperInvariant()), Is.False);
            Assert.That(ProjectOpener.IsValidId(null), Is.False);
        }

        private OpenedProject Open()
        {
            return ProjectOpener.Open(_folders, s_legacy, _clock, () => Id(++_ids));
        }

        private static string Id(int n)
        {
            return n.ToString("x32");
        }

        private sealed class FixedClock : ISystemClock
        {
            public FixedClock(DateTimeOffset now)
            {
                UtcNow = now;
            }

            public DateTimeOffset UtcNow { get; }
        }
    }
}
