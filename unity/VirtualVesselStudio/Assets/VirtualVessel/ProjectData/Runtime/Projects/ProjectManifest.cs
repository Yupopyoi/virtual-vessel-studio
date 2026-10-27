namespace VirtualVessel.ProjectData.Projects
{
    /// <summary>
    /// The functions whose reusable settings are profiles (system design 6.2, 6.3), each in its own
    /// folder under the Data Root's Profiles (6.10). Only those that exist so far.
    /// </summary>
    public enum ProfileKind
    {
        Tracking,
        Capture,

        /// <summary>The encoding settings shared by recording and, later, streaming (recording detailed design 10).</summary>
        Streaming,
    }

    /// <summary>
    /// A project as written in Projects/&lt;ProjectId&gt;/project.json (system design 6.4, project data
    /// detailed design 6): who it is, and which profiles it uses, by ID (6.6).
    /// </summary>
    public sealed class ProjectManifest
    {
        public const int SchemaVersion = 1;
        public const string FileName = "project.json";

        public string ProjectId { get; set; }

        public string DisplayName { get; set; }

        /// <summary>UTC, ISO 8601 ("o"), as text so the file reads the same everywhere.</summary>
        public string CreatedAt { get; set; }

        public string UpdatedAt { get; set; }

        public string TrackingProfileId { get; set; }

        public string CaptureProfileId { get; set; }

        /// <summary>Added without a schema step: a project from before reads null, and gets a new profile when opened.</summary>
        public string StreamingProfileId { get; set; }

        public ProjectManifest Clone()
        {
            return (ProjectManifest)MemberwiseClone();
        }

        /// <summary>The ID of the profile of <paramref name="kind"/>.</summary>
        public string ProfileId(ProfileKind kind)
        {
            return kind switch
            {
                ProfileKind.Tracking => TrackingProfileId,
                ProfileKind.Capture => CaptureProfileId,
                _ => StreamingProfileId,
            };
        }

        /// <summary>A copy that uses <paramref name="id"/> as the profile of <paramref name="kind"/>. Pure.</summary>
        public ProjectManifest WithProfile(ProfileKind kind, string id)
        {
            ProjectManifest next = Clone();
            switch (kind)
            {
                case ProfileKind.Tracking:
                    next.TrackingProfileId = id;
                    break;
                case ProfileKind.Capture:
                    next.CaptureProfileId = id;
                    break;
                default:
                    next.StreamingProfileId = id;
                    break;
            }

            return next;
        }
    }

    /// <summary>
    /// The application's own small settings in Config/app-settings.json (system design 6.8): which project
    /// is open. UI settings stay in their own file (ui-settings.json).
    /// </summary>
    public sealed class AppSettings
    {
        public const int SchemaVersion = 1;
        public const string FileName = "app-settings.json";

        public string CurrentProjectId { get; set; }

        public AppSettings Clone()
        {
            return (AppSettings)MemberwiseClone();
        }
    }
}
