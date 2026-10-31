using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Config;

namespace VirtualVessel.ProjectData.Projects
{
    /// <summary>Where projects, profiles, and the application's settings live (system design 6.8, 6.10).</summary>
    public readonly struct ProjectFolders
    {
        public ProjectFolders(string configDirectory, string projectsDirectory, string profilesDirectory)
        {
            ConfigDirectory = configDirectory ?? throw new ArgumentNullException(nameof(configDirectory));
            ProjectsDirectory = projectsDirectory ?? throw new ArgumentNullException(nameof(projectsDirectory));
            ProfilesDirectory = profilesDirectory ?? throw new ArgumentNullException(nameof(profilesDirectory));
        }

        public string ConfigDirectory { get; }

        public string ProjectsDirectory { get; }

        public string ProfilesDirectory { get; }
    }

    /// <summary>
    /// The settings files of the version before projects, in the settings folder, and where each goes
    /// (project data detailed design 8.2): one per profile kind, and the files that belong to the project.
    /// </summary>
    public sealed class LegacySettings
    {
        public LegacySettings(IReadOnlyDictionary<ProfileKind, string> profileFiles, IReadOnlyList<string> projectFiles)
        {
            ProfileFiles = profileFiles ?? throw new ArgumentNullException(nameof(profileFiles));
            ProjectFiles = projectFiles ?? throw new ArgumentNullException(nameof(projectFiles));
        }

        public IReadOnlyDictionary<ProfileKind, string> ProfileFiles { get; }

        public IReadOnlyList<string> ProjectFiles { get; }
    }

    /// <summary>The project in use and where its settings are (project data detailed design 5).</summary>
    public sealed class OpenedProject
    {
        private readonly string _profilesDirectory;

        public OpenedProject(ProjectManifest manifest, string directory, string profilesDirectory, bool created, IReadOnlyList<string> notes)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            Directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _profilesDirectory = profilesDirectory ?? throw new ArgumentNullException(nameof(profilesDirectory));
            Created = created;
            Notes = notes ?? Array.Empty<string>();
        }

        public ProjectManifest Manifest { get; }

        public string ProjectId => Manifest.ProjectId;

        /// <summary>The project's folder, which holds project.json and the settings that belong to this project only.</summary>
        public string Directory { get; }

        /// <summary>Whether the project was made at this start, as on the first start or after the old settings were moved over.</summary>
        public bool Created { get; }

        /// <summary>What was found and done, in English, for the developer log.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>The folder of the profiles of <paramref name="kind"/>.</summary>
        public string ProfileDirectory(ProfileKind kind)
        {
            return ProjectOpener.ProfileDirectory(_profilesDirectory, kind);
        }

        /// <summary>The file of the profile of <paramref name="kind"/> that this project uses.</summary>
        public string ProfileFileName(ProfileKind kind)
        {
            return ProjectOpener.ProfileFileName(Manifest.ProfileId(kind));
        }
    }

    /// <summary>
    /// Opens the project to use at startup (system design 6.17, project data detailed design 7): the one
    /// app-settings.json names, else the first one found, else a new one, made from the old settings files
    /// when there are any. Only resolves what to use; each module reads its own settings from there.
    /// </summary>
    /// <remarks>
    /// IDs come from files any program running as the user can write, so they are used as folder and file
    /// names only when they are 32 lowercase hexadecimal digits (system design 6.6, CLAUDE.md 26).
    /// </remarks>
    public static class ProjectOpener
    {
        public const string DefaultDisplayName = "Default";

        private static readonly Regex s_id = new Regex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant);

        /// <summary>A new ID, as for avatars.</summary>
        public static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }

        /// <summary>Whether <paramref name="id"/> can name a folder or file: 32 lowercase hexadecimal digits. Pure.</summary>
        public static bool IsValidId(string id)
        {
            return id != null && s_id.IsMatch(id);
        }

        /// <summary>
        /// The project to open: <paramref name="current"/> when it is one of <paramref name="existing"/>, else
        /// the first of them in ordinal order, else null for a new one. Pure.
        /// </summary>
        public static string Choose(string current, IReadOnlyList<string> existing)
        {
            if (current != null && existing.Contains(current, StringComparer.Ordinal))
            {
                return current;
            }

            return existing.OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault();
        }

        /// <summary>The folder of the profiles of <paramref name="kind"/> under <paramref name="profilesDirectory"/>. Pure.</summary>
        public static string ProfileDirectory(string profilesDirectory, ProfileKind kind)
        {
            return Path.Combine(profilesDirectory, kind.ToString());
        }

        /// <summary>The file of the profile <paramref name="id"/>. Pure.</summary>
        public static string ProfileFileName(string id)
        {
            return id + ".json";
        }

        /// <summary>
        /// <paramref name="manifest"/> made usable for the project in folder <paramref name="id"/>: its ID set
        /// to the folder's, a profile ID that cannot name a file replaced by a new one, and a missing name
        /// filled in. Each fix is described in <paramref name="notes"/>. Pure apart from calling
        /// <paramref name="newId"/> and adding to the caller's list.
        /// </summary>
        public static ProjectManifest Repair(ProjectManifest manifest, string id, Func<string> newId, List<string> notes)
        {
            ProjectManifest next = manifest.Clone();
            if (!string.Equals(next.ProjectId, id, StringComparison.Ordinal))
            {
                notes.Add($"project.json of {id} named project {next.ProjectId ?? "(none)"}; it now names its folder.");
                next.ProjectId = id;
            }

            foreach (ProfileKind kind in (ProfileKind[])Enum.GetValues(typeof(ProfileKind)))
            {
                if (!IsValidId(next.ProfileId(kind)))
                {
                    string fresh = newId();
                    notes.Add($"Project {id} had no usable {kind} profile ID ({next.ProfileId(kind) ?? "none"}); it now uses a new profile {fresh}.");
                    next = next.WithProfile(kind, fresh);
                }
            }

            if (string.IsNullOrWhiteSpace(next.DisplayName))
            {
                next.DisplayName = DefaultDisplayName;
            }

            return next;
        }

        /// <summary>
        /// Opens or makes the project to use, saving project.json and app-settings.json when they change.
        /// Old settings files are copied, never moved or deleted, so they stay as a backup (CLAUDE.md 25).
        /// </summary>
        /// <param name="newId">Makes IDs; <see cref="NewId"/> outside tests.</param>
        public static OpenedProject Open(ProjectFolders folders, LegacySettings legacy, ISystemClock clock, Func<string> newId)
        {
            if (legacy == null)
            {
                throw new ArgumentNullException(nameof(legacy));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (newId == null)
            {
                throw new ArgumentNullException(nameof(newId));
            }

            var notes = new List<string>();
            var appStore = new ConfigFileStore<AppSettings>(folders.ConfigDirectory, AppSettings.FileName, AppSettings.SchemaVersion, () => new AppSettings(), clock);
            ConfigLoadResult<AppSettings> app = appStore.Load();
            Note(notes, AppSettings.FileName, app);

            IReadOnlyList<string> existing = ExistingProjects(folders.ProjectsDirectory);
            string chosen = Choose(app.Value.CurrentProjectId, existing);
            bool created = chosen == null;
            string now = clock.UtcNow.UtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            ProjectManifest manifest;
            ConfigFileStore<ProjectManifest> store;
            if (created)
            {
                chosen = newId();
                store = ManifestStore(folders.ProjectsDirectory, chosen, clock);
                manifest = new ProjectManifest
                {
                    ProjectId = chosen,
                    DisplayName = DefaultDisplayName,
                    CreatedAt = now,
                    TrackingProfileId = newId(),
                    CaptureProfileId = newId(),
                    StreamingProfileId = newId(),
                    AudioProfileId = newId(),
                };
                notes.Add($"Made project {chosen}.");

                // Only the very first project takes the old settings; a later one starts fresh.
                if (existing.Count == 0)
                {
                    MoveOverLegacy(folders, legacy, manifest, notes);
                }
            }
            else
            {
                store = ManifestStore(folders.ProjectsDirectory, chosen, clock);
                ConfigLoadResult<ProjectManifest> loaded = store.Load();
                Note(notes, $"{chosen}/{ProjectManifest.FileName}", loaded);
                manifest = loaded.Value;
                if (!string.Equals(app.Value.CurrentProjectId, chosen, StringComparison.Ordinal))
                {
                    notes.Add($"Project {app.Value.CurrentProjectId ?? "(none)"} was not found; opened project {chosen}.");
                }
            }

            ProjectManifest repaired = Repair(manifest, chosen, newId, notes);
            if (created || !SameManifest(manifest, repaired))
            {
                repaired.UpdatedAt = now;
                store.Save(repaired);
            }

            if (!string.Equals(app.Value.CurrentProjectId, chosen, StringComparison.Ordinal))
            {
                AppSettings nextApp = app.Value.Clone();
                nextApp.CurrentProjectId = chosen;
                appStore.Save(nextApp);
            }

            return new OpenedProject(repaired, Path.Combine(folders.ProjectsDirectory, chosen), folders.ProfilesDirectory, created, notes);
        }

        private static ConfigFileStore<ProjectManifest> ManifestStore(string projectsDirectory, string id, ISystemClock clock)
        {
            return new ConfigFileStore<ProjectManifest>(Path.Combine(projectsDirectory, id), ProjectManifest.FileName, ProjectManifest.SchemaVersion, () => new ProjectManifest(), clock);
        }

        /// <summary>The project folders with a usable ID and a project.json.</summary>
        private static IReadOnlyList<string> ExistingProjects(string projectsDirectory)
        {
            if (!System.IO.Directory.Exists(projectsDirectory))
            {
                return Array.Empty<string>();
            }

            return System.IO.Directory.GetDirectories(projectsDirectory)
                .Select(Path.GetFileName)
                .Where(name => IsValidId(name) && File.Exists(Path.Combine(projectsDirectory, name, ProjectManifest.FileName)))
                .ToArray();
        }

        /// <summary>Copies each old settings file to its place in the new project, unless something is already there.</summary>
        private static void MoveOverLegacy(ProjectFolders folders, LegacySettings legacy, ProjectManifest manifest, List<string> notes)
        {
            foreach (KeyValuePair<ProfileKind, string> file in legacy.ProfileFiles)
            {
                string target = Path.Combine(ProfileDirectory(folders.ProfilesDirectory, file.Key), ProfileFileName(manifest.ProfileId(file.Key)));
                CopyIfPresent(Path.Combine(folders.ConfigDirectory, file.Value), target, notes);
            }

            foreach (string name in legacy.ProjectFiles)
            {
                CopyIfPresent(Path.Combine(folders.ConfigDirectory, name), Path.Combine(folders.ProjectsDirectory, manifest.ProjectId, name), notes);
            }
        }

        private static void CopyIfPresent(string source, string target, List<string> notes)
        {
            if (!File.Exists(source) || File.Exists(target))
            {
                return;
            }

            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(source, target);
            notes.Add($"Copied {source} to {target}; the original is kept.");
        }

        private static void Note<T>(List<string> notes, string name, ConfigLoadResult<T> result)
            where T : class
        {
            if (result.Status == ConfigLoadStatus.Recovered)
            {
                notes.Add($"{name} could not be read and was moved to {result.MovedAsidePath}; defaults were used.");
            }

            foreach (string warning in result.Warnings)
            {
                notes.Add($"{name}: {warning}");
            }
        }

        private static bool SameManifest(ProjectManifest a, ProjectManifest b)
        {
            return a.ProjectId == b.ProjectId && a.DisplayName == b.DisplayName && a.TrackingProfileId == b.TrackingProfileId && a.CaptureProfileId == b.CaptureProfileId && a.StreamingProfileId == b.StreamingProfileId && a.AudioProfileId == b.AudioProfileId;
        }
    }
}
